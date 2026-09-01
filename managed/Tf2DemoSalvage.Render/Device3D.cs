using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.IO;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;

namespace Tf2DemoSalvage.Render;

/// <summary>
/// Owns the Direct3D 11 device, swap chain and back buffer view for one window.
/// </summary>
/// <remarks>
/// **Direct3D 11 rather than OpenGL, and not for the reason it looks like.** The map data this
/// will eventually draw is API-agnostic — BSP geometry is vertices and faces, and VTF textures are
/// DXT-compressed, which uploads unconverted as BC1/BC3 under either API. Nothing about TF2 being
/// a Direct3D game constrains a tool that reads its files rather than using its renderer. The
/// actual reasons are that this project is Windows-only regardless, and that PIX and the Windows
/// graphics tooling are better than the OpenGL equivalents. Recorded in `docs/DECISIONS.md` D18
/// so the wrong rationale does not get re-derived later.
///
/// Everything here is COM through raw pointers, which is what the project's `AllowUnsafeBlocks`
/// is for: the alternative at this boundary is a copy per frame, and this is exactly the case the
/// "unsafe before native" rule describes.
/// </remarks>
public sealed unsafe class Device3D : IDisposable, IModelUpload, IWorldUpload
{
    /// <summary>Whether to present in step with the display's refresh.</summary>
    /// <remarks>
    /// Off by default, matching the setting: it adds latency, and a driver that disables it
    /// globally ignores the request anyway.
    /// </remarks>
    public bool VerticalSync { get; set; }


    /// <summary>Back buffers. Two is the minimum a flip-model swap chain accepts.</summary>
    private const uint BufferCount = 2;

    private readonly D3D11 _d3d;
    private ComPtr<ID3D11Device> _device;
    private ComPtr<ID3D11DeviceContext> _context;
    private ComPtr<IDXGISwapChain> _swapChain;
    private PointRenderer? _points;

    /// <summary>Draws world-space debug lines, created on first use.</summary>
    /// <remarks>
    /// **Separate from <see cref="PointRenderer"/> rather than a method on it** (D95). That one is
    /// clip-space by design — its shader writes the incoming pair straight to `SV_POSITION` — and
    /// this one transforms on the GPU through the camera constant buffer, which is a different
    /// shader, a different input layout and a different vertex stride. Folding them together would
    /// mean one type holding two of each and choosing between them per call.
    /// </remarks>
    private WorldLineRenderer? _worldLines;
    private WorldRenderer? _world;

    /// <summary>Each model's own vertices and rebased runs, built once per model (D86).</summary>
    /// <remarks>
    /// Cleared with the world, because the packed set is rebuilt for a new map and the paths in it
    /// would otherwise resolve to the previous map's geometry.
    /// </remarks>
    private readonly Dictionary<string, PackedModel> _packedModels =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Draws HUD text, once an atlas has been given to it (D84).</summary>
    private HudRenderer? _hud;

    private int _width;
    private int _height;

    /// <summary>Where to write the next presented frame, when a capture has been asked for.</summary>
    private string? _captureTo;
    private ComPtr<ID3D11RenderTargetView> _backBufferView;

    /// <summary>Depth buffer, so a roof covers the floor beneath it rather than the draw order.</summary>
    /// <remarks>
    /// **Batching by material destroyed the ordering the flat fill relied on.** That version sorted
    /// faces by height and let the later draw win, which works only while every face is in one
    /// stream. Grouping by texture reorders them by definition, so ground-level terrain painted
    /// over buildings - on cp_process_final the result was a dirt field swallowing the map.
    ///
    /// A depth buffer is the honest fix rather than a sort: the height is already on every vertex,
    /// and the comparison belongs to the hardware.
    /// </remarks>
    private ComPtr<ID3D11DepthStencilView> _depthView;
    private ComPtr<ID3D11Texture2D> _depthBuffer;
    private ComPtr<ID3D11DepthStencilState> _depthOn;
    private ComPtr<ID3D11DepthStencilState> _depthOff;

    /// <summary>Depth tested but not written, for the pass that blends model materials.</summary>
    private ComPtr<ID3D11DepthStencilState> _depthReadOnly;
    private bool _disposed;

    /// <summary>Where this reports what it drew and what it silently did not (D83).</summary>
    private readonly ILogger _render;

    /// <summary>Kept so <see cref="WorldRenderer"/> can be given its own categories.</summary>
    private readonly ILoggerFactory _loggers;

    private Device3D(
        ILoggerFactory loggers,
        D3D11 d3d,
        ComPtr<ID3D11Device> device,
        ComPtr<ID3D11DeviceContext> context,
        ComPtr<IDXGISwapChain> swapChain)
    {
        ArgumentNullException.ThrowIfNull(loggers);

        _loggers = loggers;
        _render = loggers.CreateLogger("render");
        _d3d = d3d;
        _device = device;
        _context = context;
        _swapChain = swapChain;
    }

    /// <summary>Creates a device and swap chain bound to a window.</summary>
    /// <param name="handle">Win32 window handle to present into.</param>
    /// <param name="width">Back buffer width in pixels.</param>
    /// <param name="height">Back buffer height in pixels.</param>
    /// <returns>The device.</returns>
    /// <exception cref="ArgumentException">The handle is zero, or a dimension is not positive.</exception>
    /// <remarks>
    /// **Takes a handle, not a window.** A swap chain needs an HWND and nothing else, so binding
    /// this to a particular windowing framework would be a dependency the renderer does not
    /// actually have — and it was one: the first version took Silk's <c>IWindow</c> purely to read
    /// <c>Native.DXHandle</c> off it. Hosting the viewport in a WinForms control then meant
    /// changing the renderer, which is the wrong direction for that change to travel.
    /// </remarks>
    /// <param name="loggers">Where the renderer reports what it drew (D83).</param>
    public static Device3D Create(nint handle, int width, int height, ILoggerFactory loggers)
    {
        if (handle == 0)
        {
            throw new ArgumentException(
                "A swap chain cannot be bound to a null window handle.", nameof(handle));
        }

        // A zero or negative dimension reaches DXGI as a huge unsigned value after the cast
        // below, which fails as an opaque HRESULT rather than as a statement about the argument.
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "Width must be positive.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), height, "Height must be positive.");
        }

        D3D11 d3d = D3D11.GetApi(null);

        SwapChainDesc description = new()
        {
            BufferDesc = new ModeDesc
            {
                Width = (uint)width,
                Height = (uint)height,

                // The presentation format, not the working one. Lighting and blending happen
                // before this in linear space; sRGB here is what makes the result look right on
                // a display rather than washed out.
                Format = Format.FormatB8G8R8A8Unorm,
            },
            SampleDesc = new SampleDesc(count: 1, quality: 0),
            BufferUsage = DXGI.UsageRenderTargetOutput,
            BufferCount = BufferCount,
            OutputWindow = handle,
            Windowed = true,
            SwapEffect = SwapEffect.FlipDiscard,

            // **Required for exclusive full screen, and it has to be set at creation.** Without
            // it DXGI refuses a mode change, so the swap chain can only ever be a window. The same
            // value must then be passed to every ResizeBuffers call or the buffers come back
            // without the capability.
            Flags = (uint)SwapChainFlag.AllowModeSwitch,
        };

        ComPtr<ID3D11Device> device = default;
        ComPtr<ID3D11DeviceContext> context = default;
        ComPtr<IDXGISwapChain> swapChain = default;

        SilkMarshal.ThrowHResult(d3d.CreateDeviceAndSwapChain(
            pAdapter: default(ComPtr<IDXGIAdapter>),
            DriverType: D3DDriverType.Hardware,
            Software: 0,
            Flags: 0u,
            pFeatureLevels: null,
            FeatureLevels: 0u,
            SDKVersion: D3D11.SdkVersion,
            pSwapChainDesc: &description,
            ppSwapChain: ref swapChain,
            ppDevice: ref device,
            pFeatureLevel: null,
            ppImmediateContext: ref context));

        Device3D created = new(loggers, d3d, device, context, swapChain)
        {
            _width = width,
            _height = height,
        };
        created.CreateBackBufferView();
        return created;
    }

    /// <summary>Clears the back buffer and presents it.</summary>
    /// <param name="red">Clear colour, red channel.</param>
    /// <param name="green">Clear colour, green channel.</param>
    /// <param name="blue">Clear colour, blue channel.</param>
    public void ClearAndPresent(float red, float green, float blue) =>
        DrawAndPresent(red, green, blue, []);

    /// <summary>Uploads every entity model's geometry, in model space.</summary>
    /// <param name="models">The packed set.</param>
    /// <exception cref="ArgumentNullException"><paramref name="models"/> is null.</exception>
    /// <remarks>
    /// Called when the set grows, which stops happening within a few seconds of playback: a demo
    /// shows most of its models early and none of them twice.
    /// </remarks>
    public void UploadModels(EntityModelSet models)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(models);

        // **Created here rather than skipped, like every other entry point.** This read
        // "if (_world is null) return;", and MainForm calls it only when a model is packed for the
        // first time - which happens once per model, ever. Loading a demo from the command line
        // packs the first models before anything has drawn, so the renderer did not exist yet, the
        // upload was skipped, and nothing ever asked again: 47 instances drawn per frame out of an
        // empty buffer, with every count in the log looking correct.
        _world ??= WorldRenderer.Create(_device, _loggers);

        // **The seam where a shared pack becomes per-model meshes** (D86). `EntityModelSet` keeps
        // every model's vertices in one list and records batch offsets into it; a static mesh per
        // model wants its own vertices with its offsets rebased to zero. Doing that here means the
        // renderer never learns that a shared list existed.
        //
        // **A model's vertices are contiguous**, which is what makes a slice correct rather than a
        // gather: `EntityModelSet.Add` takes one model, groups its corners by material, and appends
        // every group before moving to the next model. So the span from its lowest batch start to
        // its highest end contains that model and nothing else.
        // **Cached per path, because slicing is O(total) and would undo half the fix.** The buffers
        // below are created once each, but this loop was rebuilding every model's vertex array and
        // rebasing every batch on every call — so the GPU work became O(added) while the CPU work
        // stayed O(the whole set). Measured: uploads fell from 193-231 ms to 35-53 ms rather than to
        // nothing, and the residue was exactly this.
        //
        // Safe for the same reason the renderer's own skip is: a model path maps to fixed geometry
        // for the life of a map, and `ClearWorld` empties this with the buffers it feeds.
        Dictionary<string, PackedModel> packed = new(StringComparer.OrdinalIgnoreCase);

        foreach (string path in models.Paths)
        {
            if (_packedModels.TryGetValue(path, out PackedModel? already))
            {
                packed[path] = already;
                continue;
            }

            IReadOnlyList<IReadOnlyList<WorldBatch>> frames = models.AllFrames(path);

            int lowest = int.MaxValue;
            int highest = 0;

            foreach (IReadOnlyList<WorldBatch> frame in frames)
            {
                foreach (WorldBatch batch in frame)
                {
                    lowest = Math.Min(lowest, batch.FirstVertex);
                    highest = Math.Max(highest, batch.FirstVertex + batch.VertexCount);
                }
            }

            if (lowest == int.MaxValue || highest <= lowest)
            {
                // No geometry packed for this path. Recorded with empty vertices rather than
                // skipped, so its batches still reach the renderer and the "posed but no geometry"
                // report stays the one that fires.
                //
                // **Deliberately NOT cached**, because this is the one state that changes: a model
                // with nothing packed yet may have geometry on a later frame, and caching the empty
                // answer would make that permanent.
                packed[path] = new PackedModel([], frames);
                continue;
            }

            WorldVertex[] own = new WorldVertex[highest - lowest];

            for (int at = 0; at < own.Length; at++)
            {
                own[at] = models.Vertices[lowest + at];
            }

            List<IReadOnlyList<WorldBatch>> rebased = [];

            foreach (IReadOnlyList<WorldBatch> frame in frames)
            {
                List<WorldBatch> local = [];

                foreach (WorldBatch batch in frame)
                {
                    local.Add(batch with { FirstVertex = batch.FirstVertex - lowest });
                }

                rebased.Add(local);
            }

            PackedModel built = new(own, rebased);

            _packedModels[path] = built;
            packed[path] = built;
        }

        _world.UploadModels(_device, packed);
    }

    /// <summary>Writes the next presented frame to a PNG.</summary>
    /// <param name="path">Where to write it.</param>
    /// <exception cref="ArgumentException"><paramref name="path"/> is null or blank.</exception>
    /// <remarks>
    /// **A picture from the renderer that actually runs, rather than one that resembles it.** The
    /// offscreen target exists so tests can draw without a window, and it drifted from the viewer
    /// the moment either gained an argument the other did not — decals were added to the window and
    /// not to the test, so the test kept passing on a map with none, and its pictures were then
    /// read as evidence about the window.
    ///
    /// Capturing here cannot drift, because there is nothing to keep in step: this is the swap
    /// chain the user is looking at. The cost is that it needs a real window, so the test that uses
    /// it is a UI test and takes the desktop.
    ///
    /// Taken after Present rather than before, so what lands in the file is exactly what was shown.
    /// </remarks>
    public void CaptureNextFrame(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        _captureTo = path;
    }

    private void SaveBackBuffer(string path)
    {
        ComPtr<ID3D11Texture2D> back = default;

        try
        {
            SilkMarshal.ThrowHResult(_swapChain.GetBuffer(0, out back));

            Texture2DDesc description = default;
            back.GetDesc(ref description);

            description.Usage = Usage.Staging;
            description.BindFlags = 0;
            description.CPUAccessFlags = (uint)CpuAccessFlag.Read;
            description.MiscFlags = 0;

            ComPtr<ID3D11Texture2D> staging = default;

            try
            {
                SilkMarshal.ThrowHResult(
                    _device.CreateTexture2D(in description, null, ref staging));

                _context.CopyResource(staging, back);

                MappedSubresource mapped = default;
                SilkMarshal.ThrowHResult(_context.Map(staging, 0, Map.Read, 0, ref mapped));

                try
                {
                    WritePng(_render, path, (int)description.Width, (int)description.Height, mapped);

                    // **A capture said nothing at all until 2026-08-20.** Pressing F12 and getting
                    // no file is indistinguishable from pressing it and missing the key, which is
                    // the silent fallback this project bans everywhere else — and it cost a
                    // capture run that reported success and produced nothing. The size is here
                    // because a zero-byte PNG is the other way this fails quietly.
                    // Guarded: `new FileInfo(path).Length` is a filesystem call, which is exactly
                    // the kind of argument CA1873 exists to keep out of a disabled log.
                    if (_render.IsEnabled(LogLevel.Information))
                    {
                        _render.LogInformation(
                            "wrote {File}, {Width}x{Height}, {Kilobytes} KB",
                            Path.GetFileName(path),
                            description.Width,
                            description.Height,
                            new FileInfo(path).Length / 1024);
                    }
                }
                finally
                {
                    _context.Unmap(staging, 0);
                }
            }
            finally
            {
                staging.Dispose();
            }
        }
        catch (Exception failure) when (failure is InvalidOperationException or System.IO.IOException)
        {
            // A capture that fails costs a picture, not the frame the user is watching.
            _render.LogWarning(failure, "capturing the viewport to {Path}", path);
        }
        finally
        {
            back.Dispose();
        }
    }

    private static void WritePng(
        ILogger render, string path, int width, int height, MappedSubresource mapped)
    {
        // Packed tightly into RGBA for PngWriter, which is this project's own encoder. It replaced
        // System.Drawing here because that assembly is Windows-only by design in modern .NET and
        // was the one thing keeping this layer on the net10.0-windows framework (D61).
        byte[] rgba = new byte[width * height * 4];

        for (int y = 0; y < height; y++)
        {
            // RowPitch, not width * 4: the driver pads rows to its own alignment.
            byte* row = (byte*)mapped.PData + ((uint)y * mapped.RowPitch);

            for (int x = 0; x < width; x++)
            {
                byte* pixel = row + (x * 4);
                int at = ((y * width) + x) * 4;

                // The back buffer is B8G8R8A8, so the bytes arrive blue first and are swapped here.
                rgba[at] = pixel[2];
                rgba[at + 1] = pixel[1];
                rgba[at + 2] = pixel[0];
                rgba[at + 3] = 255;
            }
        }

        PngWriter.Write(path, width, height, rgba);

        render.LogInformation("captured the viewport to {Path}", path);
    }

    /// <summary>Clears, draws a set of points, and presents.</summary>
    /// <param name="red">Clear colour, red channel.</param>
    /// <param name="green">Clear colour, green channel.</param>
    /// <param name="blue">Clear colour, blue channel.</param>
    /// <param name="points">Points in normalised device coordinates.</param>
    /// <exception cref="ObjectDisposedException">The device has been disposed.</exception>
    /// <remarks>
    /// The same <see cref="PointRenderer"/> the offscreen tests drive, given the swap chain's
    /// render target instead of a texture. That is the point of the renderer not owning a target:
    /// what the tests measure and what the window shows are the same code, not two copies that
    /// agree until one is changed.
    /// </remarks>
    public void DrawAndPresent(
        float red, float green, float blue, IReadOnlyList<ScenePoint> points) =>
        DrawFrame(red, green, blue, [], [], points);

    /// <summary>Draws the first-person models in their own pass, as the engine does.</summary>
    /// <param name="viewmodels">The posed arms and weapon, or null when there are none.</param>
    /// <param name="camera">The viewmodel projection, or null to skip the pass.</param>
    /// <remarks>
    /// **A viewmodel is not drawn with the world's camera and cannot be made visible by moving it.**
    /// <c>CViewRender::DrawViewModels</c> keeps the view's origin and angles and replaces the
    /// projection and the depth range:
    ///
    /// <code>
    /// viewModelSetup.zNear = viewRender.zNearViewmodel;   // 1, against the world's 7
    /// viewModelSetup.fov   = viewRender.fovViewmodel;     // viewmodel_fov, 54
    /// pRenderContext->DepthRange( 0.0f, 0.1f );
    /// </code>
    ///
    /// This project drew them in the world list instead. They packed, posed, instanced and appeared
    /// in the frame's own draw summary while being nowhere on screen, and three offsets were tried
    /// against that before the pass was read — <c>docs/findings/30-viewmodel-drawing.md</c>.
    ///
    /// **The depth range is what keeps a gun out of a wall.** Every viewmodel writes into the
    /// nearest tenth of the buffer, so it is in front of all world geometry without being moved.
    /// The world's camera is restored afterwards, because the next frame's map draw assumes it.
    /// </remarks>
    private void DrawViewmodels(IReadOnlyList<ModelInstance>? viewmodels, float[]? camera)
    {
        // **Every transition, never a sample — and the difference is the whole point.** The rate
        // limited lines below report the viewmodel pass once a second, which cannot see an event
        // shorter than a second. The symptom this was added for is a weapon that vanishes for a few
        // FRAMES while a sticky charges, so a one-second sample reports "drawing 2" on both sides of
        // the gap and the gap itself never happened as far as the log is concerned.
        //
        // A change-triggered line has no such blind spot and no spam problem either: it is silent
        // while the state holds and writes exactly one line per flip, however brief. That is the
        // right shape for any state whose CHANGES are the signal.
        //
        // Information, not Debug, because the previous version of this was invisible without
        // `developer 1` — which is how a blind instrument stays blind.
        int drawing = _world is null || camera is null ? -1 : viewmodels?.Count ?? 0;

        if (drawing != _viewmodelsDrawn)
        {
            if (_render.IsEnabled(LogLevel.Debug))
            {
                _render.LogDebug(
                    "{Message}",
                    $"viewmodel pass {(_viewmodelsDrawn < 0 ? "start" : "changed")}: " +
                    $"{Describe(_viewmodelsDrawn)} -> {Describe(drawing)}");
            }

            _viewmodelsDrawn = drawing;
        }

        // **Is the camera itself usable?** The owner reports that during a dropout the ARMS vanish
        // too, not only the weapon — so the whole pass produces nothing while this instrument
        // happily reports two models drawn with a camera present. A count and a null check cannot
        // see a camera whose MATRIX is degenerate: one NaN anywhere in it sends every vertex of
        // every model to nowhere the rasteriser will accept, which is exactly "the pass ran and
        // nothing appeared".
        //
        // Checked here rather than assumed, because "camera is not null" and "camera describes a
        // view" are different claims and only the first was ever tested.
        if (camera is not null)
        {
            bool sane = true;

            foreach (float value in camera)
            {
                sane &= float.IsFinite(value);
            }

            if (!sane != _viewmodelCameraBroken)
            {
                _viewmodelCameraBroken = !sane;

                // Guarded on the work: the join below formats sixteen floats.
                if (!_render.IsEnabled(LogLevel.Debug))
                {
                    return;
                }

                _render.LogDebug(
                    "{Message}",
                    $"viewmodel camera matrix {(sane ? "RECOVERED" : "went NON-FINITE")}: " +
                    string.Join(", ", camera.Select(each => each.ToString("0.###", CultureInfo.InvariantCulture))));
            }
        }

        if (_world is null || viewmodels is not { Count: > 0 } || camera is null)
        {
            // **Which of the three, because they are different faults.** No renderer, nothing to
            // draw, or no camera — and a pass that silently does nothing looks exactly like a pass
            // that drew something invisible, which is the confusion this whole feature has lived in.
            // Rate limited for the same reason as the line below: a first-person view with nothing
            // to draw reports this EVERY frame otherwise, and a fault that persists does not become
            // more true for being said a hundred times a second.
            if (ViewmodelReportIsDue())
            {
                // Debug rather than Information: per-frame detail, which is what `developer 1`
                // admits and `developer 0` does not (B191). Rate limiting reduced how often this
                // line is written; it did not stop a production run writing it at all.
                _render.LogDebug(
                    "viewmodel pass skipped: world {World}, instances {Instances}, camera {Camera}",
                    _world is not null,
                    viewmodels?.Count ?? -1,
                    camera is not null);
            }

            return;
        }

        // **Where they actually are, in world units.** Every earlier check confirmed the model was
        // packed, posed, instanced and listed — none of them said where it ended up, and "drawn
        // somewhere off screen" and "drawn nowhere" look identical from every one of them.
        // Guarded: the join below walks every viewmodel and formats nine numbers each, which is
        // exactly the work CA1873 keeps out of a disabled log.
        //
        // **And rate limited, because the guard above only asks whether anyone is listening.**
        // Measured 2026-08-24: this printed 6,534 times in two minutes — once per frame — as part of
        // a log reaching 64,425 lines and 8.2 MB at roughly 1,280 writes a second. What it answers
        // is "where did the viewmodel end up", which is a question about a PLACE and does not need
        // a fresh answer sixty times a second.
        if (_render.IsEnabled(LogLevel.Debug) && ViewmodelReportIsDue())
        {
            _render.LogDebug(
                "viewmodel pass: drawing {Count} at {Where}",
                viewmodels.Count,
                string.Join(
                    ", ",
                    viewmodels.Select(instance =>
                        $"{System.IO.Path.GetFileNameWithoutExtension(instance.ModelPath)} " +
                        $"at ({instance.Matrix[12]:0.#}, {instance.Matrix[13]:0.#}, {instance.Matrix[14]:0.#}) " +

                        // **Where the model's own forward tip lands in the world.** Row-major, so a
                        // model-space point times the matrix is p.x*row0 + p.y*row1 + p.z*row2 +
                        // row3. The posed arms reach about 36 units along model +X, so this is the
                        // far end of them — and comparing it against the eye says whether the model
                        // is pointing where the camera is looking or somewhere else entirely.
                        $"tip36 ({(36f * instance.Matrix[0]) + instance.Matrix[12]:0.#}, " +
                        $"{(36f * instance.Matrix[1]) + instance.Matrix[13]:0.#}, " +
                        $"{(36f * instance.Matrix[2]) + instance.Matrix[14]:0.#})")));
        }

        // **What the viewmodel pass tells the shader the eye is, said once** (B170). The reflection
        // is `reflect(-normalize(eyePosition - wpos), normal)`, and `eyePosition` is not passed in —
        // it is DERIVED from this pass's own camera by inverting it (`EyePosition.From`). Every
        // offscreen measurement of the reflection came back inside its material's tint, including on
        // the real weapon model at eye range, so what is left is the state this pass supplies that
        // no harness can reach.
        //
        // Compared against where the models actually are, which the line below prints: if the eye
        // and the weapon disagree, the reflection is being computed from a view direction that is
        // not the viewer's, and it would sample the cube somewhere arbitrary — which is the shape of
        // a wash rather than a highlight.
        if (!_reportedViewmodelEye)
        {
            _reportedViewmodelEye = true;

            _render.LogInformation(
                "{Message}",
                $"viewmodel pass eye: {(EyePosition.From(camera) is { } eye
                    ? $"({eye.X:0.#}, {eye.Y:0.#}, {eye.Z:0.#})"
                    : "NONE — the camera would not invert, so the shader reflects nothing")}");
        }

        // **The light each viewmodel instance actually carries into the draw** (B170). The scene
        // computes a healthy cube at the eye — luminance 0.2344 measured in that control room — and
        // the weapon still renders at about linear 0.004, roughly twenty times darker than TF2's in
        // the same place. Between those two numbers there is exactly one link nothing has reported:
        // whether the cube reaches the instance the renderer is handed.
        if (!_reportedViewmodelInstanceLight)
        {
            _reportedViewmodelInstanceLight = true;

            _render.LogInformation(
                "{Message}",
                "viewmodel instance light: " + string.Join(
                    "; ",
                    viewmodels.Select(instance =>
                        $"{System.IO.Path.GetFileNameWithoutExtension(instance.ModelPath)} " +
                        $"{(instance.Light is { } cube
                            ? $"cube +Z ({cube.PositiveZ.Red:0.###}, {cube.PositiveZ.Green:0.###}, " +
                              $"{cube.PositiveZ.Blue:0.###}), -Z ({cube.NegativeZ.Red:0.###}, " +
                              $"{cube.NegativeZ.Green:0.###}, {cube.NegativeZ.Blue:0.###})"
                            : "NO CUBE — the shader draws it at full brightness")}, " +
                        $"sun {(instance.Sun is null ? "none" : "reaching")}, " +
                        $"bones {instance.Bones?.Count ?? 0}")));
        }

        Viewport near = new(
            0f, 0f, _width, _height, ViewmodelPass.DepthMinimum, ViewmodelPass.DepthMaximum);

        _context.RSSetViewports(1, in near);

        // **The debug state travels with the camera, and omitting it is what B187 was.** This called
        // the same method with three arguments, and every remaining parameter is OPTIONAL — so the
        // viewmodel pass silently ran with `surfaceColours: false, specular: true,
        // fullbright: Off, debug: default` while the world around it ran with whatever the user had
        // chosen. `mat_drawflat`, `mat_luxels`, `mat_normalmaps`, `mat_bumpbasis` and
        // `mat_fullbright` therefore changed the world and left the weapon in hand alone.
        //
        // **In TF2 these are material-system overrides**, applied to everything drawn rather than to
        // a pass, so a viewmodel exempt from them is a departure nobody chose.
        //
        // **It cost more than the debug views themselves**: B170 is washed-out viewmodels, and the
        // tools built to diagnose exactly that could not be pointed at the thing that was wrong.
        //
        // Defaults on a parameter list are what made this invisible — the call compiled, ran, and
        // drew something plausible. Passing them explicitly is the whole fix.
        _world.SetCamera(
            _device,
            _context,
            camera,
            _worldCamera?.Colours ?? false,
            _specular,
            _fullbright,
            _debug,
            _phong);
        _context.OMSetDepthStencilState(_depthOn, 0);

        // **No depth clear, which is the engine's arrangement.** Source compresses the viewmodel
        // into the near tenth of the buffer instead, and only clears under Portal. Clearing was
        // tried here as a diagnostic and changed nothing, which is how depth was ruled out.

        foreach (ModelInstance instance in viewmodels)
        {
            // **Is the model collapsed rather than absent?** The pass draws it every frame and it is
            // still not on screen, so the remaining candidates are all about the POSE — and a
            // skinned model whose bones go degenerate occupies no pixels while reporting a perfectly
            // ordinary draw. The launcher is five bones merged onto the arms and only four of them
            // match by name, which is exactly the shape that produces one unset matrix.
            //
            // Transition-logged, like the pass count above, because the event is a few frames long:
            // a sample would sit on either side of it and see nothing.
            //
            // **The arms are passed as the reference, because "is it posed" was the wrong
            // question.** A weapon can have perfectly valid, non-collapsed bones and still be four
            // thousand units from the hands — which is a viewmodel that is simply not on screen, and
            // reads on screen as no viewmodel at all. Measured on the Iron Bomber, whose bones came
            // back `0 of 4 degenerate, span 31.11` and centred 4,400 units from the arms.
            ReportBonesIfDegenerate(instance, viewmodels[0]);

            if (instance.Bones is { Count: > 0 } bones)
            {
                _world.SetBones(_context, bones);
            }

            _world.DrawModel(
                _context,
                instance.ModelPath,
                instance.Matrix,
                _world.ModelBatches(instance.ModelPath, instance.Frame),
                instance.Light,
                instance.Sun,
                instance.Blend,
                instance.Bones?.Count ?? 0,
                instance.SkinSwap,

                // **Whole, because a viewmodel is drawn in one pass here.** This filtered to opaque
                // materials only, so a weapon's blended parts — a scope lens, a glow, a cloaked
                // spy's own hands — were dropped and nothing said so. Valve keeps two viewmodel
                // lists (`viewrender.cpp:1150` draws the translucent one with STUDIO_TRANSPARENCY);
                // this project has one, and drawing the whole model in it loses nothing where
                // filtering lost the blended half outright.
                ModelPass.EntireModel,
                instance.BodyParts,
                instance.Body,
                // Culled normally, per material, like everything else. Drawing both sides was tried
                // as a diagnostic and changed nothing, which ruled winding out.
                instance.Mirrored,

                // **The whole of B170.** A viewmodel is always skinned, so its matrix is identity
                // and its translation reads (0, 0, 0) — every weapon reflected the cubemap nearest
                // the MAP ORIGIN, thousands of units from the player, at whatever brightness that
                // cube happened to hold. This is where it stands instead.
                origin: instance.Origin,

                // **The lamps near the weapon**, which is the whole point for a viewmodel: it sits
                // at the player's eye, the position in the scene most reliably under whatever light
                // the player is walking beneath (B170).
                locals: instance.Locals);
        }

        // **Both of the pass's changes are put back, and forgetting the camera was a real defect.**
        // The world's camera constant is set when the VIEW changes rather than every frame, so a
        // pass that leaves its own projection behind is not corrected next frame — the whole map
        // then draws at the viewmodel's 54 degrees instead of the world's, which looks like a
        // zoom nobody asked for and is visible immediately.
        Viewport whole = new(0f, 0f, _width, _height, 0f, 1f);
        _context.RSSetViewports(1, in whole);

        ReapplyCamera();
    }

    /// <summary>When the viewmodel pass last reported, so it cannot report per frame.</summary>
    private long _viewmodelReportedAt;

    /// <summary>Whether a second has passed since the viewmodel pass last said anything.</summary>
    /// <remarks>
    /// **A rate limit rather than a level, because these lines are wanted by DEFAULT.** They answer
    /// "did the viewmodel draw, and where" — the questions this feature has spent its whole life
    /// failing to answer — so hiding them behind `developer 1` would take away the thing that made
    /// them worth writing. What was wrong was the frequency, not the level: 6,534 identical lines in
    /// two minutes, one per frame, in a log that reached 64,425 lines and 8.2 MB.
    ///
    /// One a second keeps the answer available and costs nothing measurable.
    /// </remarks>
    private bool ViewmodelReportIsDue()
    {
        long now = System.Diagnostics.Stopwatch.GetTimestamp();

        if (now - _viewmodelReportedAt < System.Diagnostics.Stopwatch.Frequency)
        {
            return false;
        }

        _viewmodelReportedAt = now;
        return true;
    }

    /// <summary>The last world camera set, so the viewmodel pass can put it back.</summary>
    /// <remarks>Carried a `HeightCut` until 2026-08-26 (B213).</remarks>
    private (float[] Matrix, bool Colours)? _worldCamera;

    /// <summary>Re-sends the remembered world camera with the CURRENT debug modes.</summary>
    /// <remarks>
    /// **Two bugs lived in the three lines this replaces, and they had the same cause.**
    ///
    /// The restore after the viewmodel pass passed only the matrix, the category switch and the
    /// height cut — so specular, fullbright and the debug views were reset to their defaults every
    /// time a viewmodel drew. A restore that does not restore everything is the same failure as a
    /// pass that does not establish its own state (B154), from the other end.
    ///
    /// And the mode setters cleared `_worldCamera` to force an update, which does the opposite:
    /// forgetting the camera means the restore is skipped, so a mode change reached the GPU only
    /// when something else happened to call SetCamera. The owner saw exactly that — a mode would
    /// appear "if u move the camera or disable or enable reflections", and immediately when it did,
    /// so it was never a loading delay. Reflections looked like an exception only because its
    /// handler also rebuilt the world.
    ///
    /// The modes are read from the fields rather than captured, so there is one place they come
    /// from and adding another cannot be forgotten here.
    /// </remarks>
    private void ReapplyCamera()
    {
        if (_world is null || _worldCamera is not { } restore)
        {
            return;
        }

        _world.SetCamera(
            _device,
            _context,
            restore.Matrix,
            restore.Colours,
            _specular,
            _fullbright,
            _debug,
            _phong);
    }

    /// <summary>Gives the HUD its glyph atlas, replacing whichever one it had.</summary>
    /// <param name="pixels">RGBA, <c>width * height * 4</c> bytes, row-major.</param>
    /// <param name="width">Atlas width in pixels.</param>
    /// <param name="height">Atlas height in pixels.</param>
    /// <exception cref="ObjectDisposedException">The device has been disposed.</exception>
    /// <remarks>
    /// **Separate from drawing because an atlas is built once and drawn every frame.** Rasterising
    /// a hundred glyphs and packing them is startup work; uploading it per frame would be the same
    /// mistake as re-decompressing a lump per resize
    /// (`docs/memory/per-item-apis-hide-quadratic-reads.md`).
    ///
    /// The renderer is created on the first call rather than with the device, so a session that
    /// never turns a HUD element on never compiles the shaders.
    /// </remarks>
    public void SetHudAtlas(ReadOnlySpan<byte> pixels, int width, int height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _hud ??= HudRenderer.Create(_device);
        _hud.SetAtlas(_device, pixels, width, height);
    }

    /// <summary>Clears, draws the map and the players, and presents.</summary>
    /// <param name="red">Clear colour, red channel.</param>
    /// <param name="green">Clear colour, green channel.</param>
    /// <param name="blue">Clear colour, blue channel.</param>
    /// <param name="mapFill">Filled map surfaces in clip space, three corners per triangle.</param>
    /// <param name="mapLines">Debug line segments in WORLD units, drawn through the world camera.</param>
    /// <param name="points">Player positions in clip space.</param>
    /// <exception cref="ObjectDisposedException">The device has been disposed.</exception>
    /// <param name="models">Posed entity models, or null to draw none.</param>
    /// <param name="viewmodels">
    /// The first-person arms and weapon, drawn after the world in their own pass.
    /// </param>
    /// <param name="viewmodelCamera">
    /// The projection that pass uses, or null when there is nothing to draw in it.
    /// </param>
    /// <param name="hud">
    /// HUD quads in screen pixels, or null to draw none. Requires <see cref="SetHudAtlas"/>.
    /// </param>
    /// <remarks>
    /// The map goes down first so the players draw over it. There is no depth buffer and none is
    /// wanted: for a flat overhead view the draw order IS the layering, and it is one fewer
    /// resource to resize when the window changes.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1062:Validate arguments of public methods",
        Justification =
            "viewmodels is OPTIONAL - nullable with a default of null - so throwing on null would " +
            "break its contract rather than protect it. The only use is DrawViewmodels, which " +
            "handles null explicitly and logs which of its three preconditions failed. The rule " +
            "fires because it does not follow the null check across the private call. Raised when " +
            "this type became public at the Render seam (D61); it was internal before and the rule " +
            "did not apply.")]
    public void DrawFrame(
        float red,
        float green,
        float blue,
        IReadOnlyList<(float X, float Y, float Shade)> mapFill,
        IReadOnlyList<((float X, float Y, float Z) From, (float X, float Y, float Z) To)> mapLines,
        IReadOnlyList<ScenePoint> points,
        IReadOnlyList<ModelInstance>? models = null,
        IReadOnlyList<ModelInstance>? viewmodels = null,
        float[]? viewmodelCamera = null,
        IReadOnlyList<HudQuad>? hud = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(mapLines);
        ArgumentNullException.ThrowIfNull(mapFill);

        float* colour = stackalloc float[4] { red, green, blue, 1f };
        _context.ClearRenderTargetView(_backBufferView, colour);

        if (_depthView.Handle is not null)
        {
            _context.ClearDepthStencilView(_depthView, (uint)ClearFlag.Depth, 1f, 0);
        }

        if (mapFill.Count > 0 || mapLines.Count > 0 || points.Count > 0 ||
            _world is { HasMap: true })
        {
            _points ??= PointRenderer.Create(_device);

            Viewport viewport = new(0f, 0f, _width, _height, 0f, 1f);
            _context.RSSetViewports(1, in viewport);
            _context.OMSetRenderTargets(1u, _backBufferView.GetAddressOf(), _depthView);

            // **The textured world replaces the flat fill when it is available.** Drawing both
            // would put a shaded grey slab over the map's own textures.
            if (_world is { HasMap: true })
            {
                _context.OMSetDepthStencilState(_depthOn, 0);
                _world.Draw(_context);

                // **After the map, and through the depth buffer**, so a model behind a wall is
                // hidden by it rather than by draw order. The map's own identity matrix is set at
                // the top of Draw each frame, which is what stops these leaving their transform
                // behind for the world.

                // **Depth writing is turned back ON, because the world's last pass turned it off.**
                // DrawTranslucent sets a read-only depth state — correct for glass, which must not
                // stop what is behind it from drawing — and never restores it, so every model after
                // it inherited a state where nothing writes depth.
                //
                // That single leak produced every model complaint in this session. WITHIN a model
                // its own triangles stop occluding each other, so a player's eyes draw through the
                // back of his head and the back of his head shows from the front. BETWEEN models,
                // submission order decides instead of distance, so a medkit draws over the medic
                // standing in front of it from every angle.
                //
                // Set here rather than restored inside DrawTranslucent deliberately: this is the
                // pass that requires it, and a pass that depends on a state should say so rather
                // than trust the last one to have tidied up.
                _context.OMSetDepthStencilState(_depthOn, 0);

                ReportRepeatedModels(models);

                // **Biggest first, which is what the engine does and why it does it.**
                // `DrawOpaqueRenderables` walks its size buckets from huge down to crate-sized
                // (`viewrender.cpp:4188`), so a large object fills the depth buffer early and
                // everything behind it fails the depth test before its pixels are shaded. It is
                // occlusion bought with a sort, which is why it belongs before any culling work
                // rather than after it.
                //
                // **Opaque only.** The translucent pass below must stay back-to-front by distance —
                // blending is order-dependent, and a size sort there would put a window in front of
                // what should show through it.
                IReadOnlyList<ModelInstance> opaque =
                    OpaqueBuckets.InDrawOrder(models ?? [], _frustum);

                ReportDrawOrder(models, opaque);

                foreach (ModelInstance instance in opaque)
                {
                    // **Which lists this model joins, and whether it is split** — Valve's
                    // GetRenderGroup and CollateRenderablesInLeaf, in RenderGroups. A model with no
                    // blended material joins this list alone and draws WHOLE; one with blended
                    // materials joins this list only if it declared $mostlyopaque, and then only its
                    // solid half is drawn here.
                    (bool joinsOpaque, _, bool twoPass) = Classify(instance);

                    if (!joinsOpaque)
                    {
                        continue;
                    }

                    if (instance.Bones is { Count: > 0 } bones)
                    {
                        _world.SetBones(_context, bones);
                    }

                    ReportBodySelection(instance, _world.ModelBatches(instance.ModelPath, instance.Frame));

                    _world.DrawModel(
                        _context,
                        instance.ModelPath,
                        instance.Matrix,
                        _world.ModelBatches(instance.ModelPath, instance.Frame),
                        instance.Light,
                        instance.Sun,
                        instance.Blend,
                        instance.Bones?.Count ?? 0,
                        instance.SkinSwap,
                        twoPass ? ModelPass.OpaqueOnly : ModelPass.EntireModel,
                        instance.BodyParts,
                        instance.Body,
                        instance.Mirrored,

                        // Where the model stands, for its cubemap. Its matrix cannot say, because a
                        // skinned model's placement is in its bones (B170).
                        origin: instance.Origin,

                        // Valve's class colour for a brush entity, in the category view (B219).
                        tint: instance.Tint,

                        // The lamps near this model, which its ambient cube no longer carries.
                        locals: instance.Locals);
                }

                // **The see-through parts of models, after every solid one.** A hologram, a glass
                // visor and a cloaked spy all have to blend against what is behind them, so they
                // can only be drawn once that has been drawn — which is the same reason the world
                // keeps its translucent surfaces to the end.
                //
                // Depth is still TESTED so a hologram behind a wall stays hidden, and no longer
                // WRITTEN so it does not erase whatever is meant to show through it.
                _context.OMSetDepthStencilState(_depthReadOnly, 0);

                // **Back to front by the camera, never in input order** (the outside audit's
                // finding 2). This loop used to draw in scene order under a comment that defended
                // it with the sort's own argument — "blending is order-dependent" is exactly why
                // the order must come from the camera. The engine sorts translucent entries
                // ascending along the view axis (`CClientLeafSystem::SortEntities`,
                // `clientleafsystem.cpp:1758`) and walks the list backwards
                // (`viewrender.cpp:4577`), so the farthest blends first; this collects the
                // survivors, sorts them the same way, and walks them the same way.
                _translucentDraw.Clear();

                foreach (ModelInstance instance in models ?? [])
                {
                    // Culled with the same frustum as the opaque pass: the engine culls in the
                    // leaf system before it splits opaque from translucent, so both passes see
                    // the same visible set.
                    if (Culled(instance))
                    {
                        continue;
                    }

                    // **The other half of the same decision.** A model with no blended material is
                    // absent from this pass entirely — it was drawn whole above — where before every
                    // model was walked here and filtered batch by batch to nothing.
                    (_, bool joinsTranslucent, bool twoPass) = Classify(instance);

                    if (!joinsTranslucent)
                    {
                        continue;
                    }

                    _translucentDraw.Add((
                        TranslucentOrder.Along(instance, _translucentEye, _translucentForward),
                        (instance, twoPass)));
                }

                TranslucentOrder.Sort(_translucentDraw);

                for (int at = _translucentDraw.Count - 1; at >= 0; at--)
                {
                    (ModelInstance instance, bool twoPass) = _translucentDraw[at].Entry;

                    if (instance.Bones is { Count: > 0 } bones)
                    {
                        _world.SetBones(_context, bones);
                    }

                    _world.DrawModel(
                        _context,
                        instance.ModelPath,
                        instance.Matrix,
                        _world.ModelBatches(instance.ModelPath, instance.Frame),
                        instance.Light,
                        instance.Sun,
                        instance.Blend,
                        instance.Bones?.Count ?? 0,
                        instance.SkinSwap,
                        twoPass ? ModelPass.TranslucentOnly : ModelPass.EntireModel,
                        instance.BodyParts,
                        instance.Body,
                        instance.Mirrored,

                        // Where the model stands, for its cubemap. Its matrix cannot say, because a
                        // skinned model's placement is in its bones (B170).
                        origin: instance.Origin,

                        // Valve's class colour for a brush entity, in the category view (B219).
                        tint: instance.Tint,

                        // The lamps near this model, which its ambient cube no longer carries.
                        locals: instance.Locals);
                }

                WorldRenderer.ResetBlend(_context);

                DrawViewmodels(viewmodels, viewmodelCamera);
            }
            else
            {
                _points.DrawTriangles(_device, _context, mapFill, (0.30f, 0.34f, 0.42f));
            }

            // **The leaf box is depth TESTED, and the player markers are not.** That split is
            // Valve's, not ours: every debug overlay in the SDK carries its own `noDepthTest` flag
            // (`ndebugoverlay.h:24`) rather than the drawing code deciding for all of them.
            //
            // A leaf box describes GEOMETRY — it is the volume the engine culls and traces against —
            // so it has to be occluded by that geometry or it says nothing about where the wall is.
            // A player marker is the opposite: it stands in for somebody you cannot see, and hiding
            // it behind the wall they are behind would defeat the whole point of drawing it.
            if (mapLines.Count > 0 && _worldCamera is { } lineCamera)
            {
                _worldLines ??= WorldLineRenderer.Create(_device);

                _context.OMSetDepthStencilState(_depthReadOnly, 0);

                _worldLines.Draw(
                    _device, _context, mapLines, lineCamera.Matrix, 0.55f, 0.62f, 0.74f);
            }

            // Players are annotations on the world, so they ignore its depth.
            _context.OMSetDepthStencilState(_depthOff, 0);

            _points.Draw(_device, _context, points);
        }

        // **The HUD is drawn OUTSIDE the block above, and that is deliberate.** Everything in there
        // is skipped when there is no map and nothing to plot — which is exactly the state the frame
        // rate meter is most wanted in, since a viewer sitting on an empty viewport is one that may
        // be struggling to load something.
        //
        // Last, so it is over everything, with depth off because a HUD is not in the world. Before
        // Present, so it lands in the presented frame and therefore in an F12 capture, which reads
        // the back buffer afterwards.
        if (hud is { Count: > 0 } && _hud is { HasAtlas: true })
        {
            Viewport hudViewport = new(0f, 0f, _width, _height, 0f, 1f);
            _context.RSSetViewports(1, in hudViewport);
            _context.OMSetRenderTargets(1u, _backBufferView.GetAddressOf(), _depthView);
            _context.OMSetDepthStencilState(_depthOff, 0);

            _hud.Draw(_device, _context, hud, _width, _height);

            // The HUD sets an alpha blend and the world expects none, so it is put back rather than
            // left for whatever draws first next frame to discover.
            WorldRenderer.ResetBlend(_context);
        }

        // **Synced to the display, which is also what paces the render loop.** The loop draws for
        // as long as Windows has nothing else for the thread, so without a sync interval it spins
        // a core producing frames no one can see. Blocking here until the next vertical blank
        // costs nothing, removes tearing, and hands the pacing to the one clock that matches what
        // the user is looking at.
        //
        // It does not pace playback: the clock is told how long the frame took, so a 60 Hz display
        // and a 144 Hz one play the same demo at the same speed.
        // **Asked for, not guaranteed.** A driver set to force vertical sync off ignores this
        // entirely - measured at about 600 frames a second with an interval of one - which is why
        // the frame limit in MainForm exists and is the ceiling that actually holds.
        SilkMarshal.ThrowHResult(
            _swapChain.Present(SyncInterval: VerticalSync ? 1u : 0u, Flags: 0u));

        if (_captureTo is { } file)
        {
            _captureTo = null;
            SaveBackBuffer(file);
        }
    }

    /// <summary>Rebuilds the back buffer at a new size.</summary>
    /// <param name="width">New width in pixels.</param>
    /// <param name="height">New height in pixels.</param>
    /// <remarks>
    /// The view has to be released before <c>ResizeBuffers</c>, because the swap chain cannot
    /// resize a buffer something still holds a reference to. Skipping that release fails with a
    /// generic E_INVALIDARG that says nothing about the cause.
    /// </remarks>
    public void Resize(int width, int height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (width <= 0 || height <= 0)
        {
            // Minimising reports a zero-sized framebuffer, which is not an error and must not
            // reach ResizeBuffers.
            return;
        }

        _backBufferView.Dispose();
        _backBufferView = default;
        ReleaseDepth();

        SilkMarshal.ThrowHResult(_swapChain.ResizeBuffers(
            BufferCount,
            (uint)width,
            (uint)height,
            Format.FormatB8G8R8A8Unorm,
            (uint)SwapChainFlag.AllowModeSwitch));

        // Kept in step with the swap chain, because the viewport passed to the rasteriser comes
        // from here. A stale size draws into a rectangle that no longer matches the buffer, which
        // scales and clips silently rather than failing.
        _width = width;
        _height = height;

        CreateBackBufferView();
    }

    /// <summary>Uploads a map's textured geometry, replacing anything already there.</summary>
    /// <param name="world">The triangles and their material batches.</param>
    /// <param name="assets">The map's textures and lightmap atlas.</param>
    /// <exception cref="ObjectDisposedException">The device has been disposed.</exception>
    /// <remarks>
    /// Once per map. The geometry does not move, the lighting is baked and the textures do not
    /// change, so a frame afterwards is a couple of hundred draw calls over resident resources.
    /// </remarks>
    public void UploadWorld(MapWorld world, MapAssets assets)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(assets);

        _world ??= WorldRenderer.Create(_device, _loggers);
        _world.UploadMap(_device, _context, world.Vertices, world.Batches, assets);
    }

    /// <summary>Uploads a map's textures, without touching its geometry.</summary>
    /// <param name="assets">The map's textures and lightmap atlas.</param>
    /// <exception cref="ObjectDisposedException">The device has been disposed.</exception>
    public void UploadWorldTextures(MapAssets assets)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _world ??= WorldRenderer.Create(_device, _loggers);
        _world.UploadTextures(_device, _context, assets);
    }

    /// <summary>Uploads a map's projected geometry, keeping the textures already resident.</summary>
    /// <param name="world">The triangles and their material batches.</param>
    /// <exception cref="ObjectDisposedException">The device has been disposed.</exception>
    /// <remarks>
    /// **This is the resize path.** The projection is baked into the vertices, so a viewport that
    /// changes size needs new vertices - and nothing else. Re-uploading the textures alongside them
    /// cost 208 texture creations and mip chains per resize.
    /// </remarks>
    public void UploadWorldGeometry(MapWorld world)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _world ??= WorldRenderer.Create(_device, _loggers);
        _world.UploadGeometry(
            _device, world.Vertices, world.Batches, world.Decals, world.Props);
    }

    /// <summary>How this map decides what of the world to draw, or null to draw all of it.</summary>
    private WorldCulling? _culling;

    /// <summary>Gives the device the map's visibility, or takes it away.</summary>
    /// <param name="culling">The map's culling, or null for a map that cannot be culled.</param>
    /// <exception cref="ObjectDisposedException">The device has been disposed.</exception>
    /// <remarks>
    /// **Set beside the geometry it culls, because the two describe one map.** A stale culling
    /// object paired with a new map's vertex buffer would name face spans that belong to somewhere
    /// else entirely — runs at plausible offsets into the wrong geometry, which draws a scrambled
    /// map rather than failing.
    ///
    /// **The visible runs are dropped here rather than recomputed**, so a map change cannot leave
    /// the previous map's runs standing until the camera next moves.
    /// </remarks>
    public void SetWorldCulling(WorldCulling? culling)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _culling = culling;
        _reportedWorldCull = false;

        if (_world is not null)
        {
            _world.VisibleBatches = null;
        }
    }

    /// <summary>Sets the view the world is drawn through, and the volume it culls against.</summary>
    /// <param name="camera">The camera the frame is seen through.</param>
    /// <param name="surfaceColours">Whether to draw flat category colours instead of textures.</param>
    /// <exception cref="ArgumentNullException"><paramref name="camera"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">The device has been disposed.</exception>
    /// <remarks>
    /// **The projection and the cull come from one camera, on purpose.** Deriving them separately
    /// invites the two to disagree, and a frustum that describes a camera nobody is looking through
    /// removes exactly the geometry in front of the viewer — which reads as a rendering fault
    /// rather than as a stale camera. Taking the camera itself makes the pair a single decision.
    ///
    /// **Set on a view CHANGE rather than per frame**, like the matrix it replaces: the frustum is
    /// a function of the camera and nothing else, so recomputing it while nothing moved would be
    /// six planes of arithmetic for the same six planes.
    /// </remarks>
    public void SetCamera(FreeCamera camera, bool surfaceColours = false)
    {
        ArgumentNullException.ThrowIfNull(camera);

        _frustum = camera.Frustum();

        // For the translucent back-to-front sort: the same two values the engine hands
        // `SortEntities` as vecRenderOrigin and vecRenderForward, read from the same camera the
        // frustum came from — one camera or the cull lies, and so would the sort.
        _translucentEye = camera.Origin;
        _translucentForward = camera.Basis().Forward;

        SetCamera(camera.ToMatrix(), surfaceColours);

        // **The world's own cull, from the same camera and in the same call.** Null when the map
        // carried no visibility data, which the renderer reads as "draw the batches you already
        // had" — see WorldRenderer.VisibleBatches for why that is not the same as an empty list.
        //
        // **Only when the view actually MOVED, and skipping this cost 40% of the frame rate.**
        // `MainForm.PlaceCamera` uploads the camera every frame whether or not it changed, so the
        // first version of this walked the whole BSP and every face span sixty times a second to
        // produce the same answer. Measured on the UI suite: 274 frames a second before, 149 after,
        // with per-frame DRAWING time unchanged at ~1 ms — the cost was entirely in this call and
        // therefore invisible to the drawing timer.
        //
        // The engine builds its world lists once per view for exactly this reason; a view that has
        // not changed has the same lists. `FreeCamera` is a class without value equality, so the
        // comparison is on the values the answer depends on and nothing else.
        (( float X, float Y, float Z) Origin, (float Pitch, float Yaw, float Roll) Angles, float Fov, float Near, float Far,
         float Aspect) view =
            ((camera.Origin.X, camera.Origin.Y, camera.Origin.Z),
             camera.Angles, camera.FieldOfView, camera.NearZ, camera.FarZ, camera.Aspect);

        if (_world is not null && (_culledFor != view || _world.VisibleBatches is null))
        {
            _culledFor = view;

            _world.VisibleBatches = _culling?.Batches(
                camera.Origin.X, camera.Origin.Y, camera.Origin.Z, _frustum);

            ReportWorldCull();
        }
    }


    /// <summary>Sets the view the world is drawn through, without a cull volume.</summary>
    /// <param name="matrix">Sixteen floats, row major.</param>
    /// <param name="surfaceColours">Whether to draw flat category colours instead of textures.</param>
    /// <exception cref="ObjectDisposedException">The device has been disposed.</exception>
    /// <remarks>
    /// **The resize path, now.** Geometry is uploaded in world coordinates and stays; a viewport
    /// change rewrites one 64-byte buffer instead of rebuilding every vertex.
    ///
    /// **This overload leaves the frustum alone rather than clearing or inventing one.** A matrix
    /// can be decomposed back into six planes, and doing so would be a SECOND derivation of the
    /// camera — the thing <see cref="SetCamera(FreeCamera, bool)"/> exists to prevent. A caller
    /// that only ever uses this overload culls nothing, which is slower and never wrong; the
    /// viewmodel pass, which sets a camera of its own and restores the world's, relies on that.
    /// </remarks>
    public void SetCamera(float[] matrix, bool surfaceColours = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _world ??= WorldRenderer.Create(_device, _loggers);

        // Applied here rather than only in the setter, because the renderer is built lazily and a
        // toggle flipped before the first map would otherwise be silently forgotten.
        _world.Wireframe = _wireframe;
        _world.DrawWorld = _drawWorld;
        _world.DrawEntities = _drawEntities;

        _world.SetCamera(
            _device, _context, matrix, surfaceColours, _specular, _fullbright, _debug, _phong);

        // Remembered so the viewmodel pass can put it back. The world's camera is set on a view
        // CHANGE rather than per frame, so anything that overwrites it has to restore it or the
        // map keeps the wrong projection until the user next moves.
        _worldCamera = (matrix, surfaceColours);
    }

    /// <summary>Whether the world draws in wireframe — Valve's <c>mat_wireframe</c>.</summary>
    /// <remarks>
    /// **Separates "never drawn" from "drawn and invisible", which nothing else here can.** Both
    /// produce an absent surface, and every other instrument in this renderer answers only one of
    /// them: a face count says what was submitted, a material ledger says what it was submitted
    /// with, and neither says whether an edge reached the screen.
    /// </remarks>
    public bool Wireframe
    {
        get => _wireframe;

        set
        {
            _wireframe = value;

            if (_world is not null)
            {
                _world.Wireframe = value;
            }
        }
    }

    private bool _wireframe;

    /// <summary>Whether cubemap reflections are added — Valve's <c>mat_specular</c>.</summary>
    /// <remarks>
    /// **A surface reflecting the sky at full strength IS the sky.** That is not a figure of
    /// speech: an opaque prop whose envmap term dominates draws in the background's own colour and
    /// reads as missing geometry, which is why this needs a switch rather than a code reading.
    /// Valve's own note for the same switch is "If mat_specular 0, then get rid of envmap".
    /// </remarks>
    public bool Specular
    {
        get => _specular;

        set
        {
            _specular = value;

            // Re-sent immediately rather than left for the next SetCamera. Clearing the remembered
            // camera was the previous approach and it did the opposite of what it read as: it
            // forgot the camera, so nothing re-sent anything and the change waited for an unrelated
            // event.
            ReapplyCamera();
        }
    }

    private bool _specular = true;

    /// <summary>Whether materials declaring <c>$phong</c> get their highlight — <c>mat_phong</c>.</summary>
    /// <remarks>
    /// **Valve's convar, default 1**, read from the game's own shipped list: <c>mat_phong : 1 : :</c>.
    /// It carries no flags and no help text there, which is why the description here is this
    /// project's own rather than a quotation.
    ///
    /// **The switch exists because the term is large and additive.** `$phongboost` is described by
    /// Valve as an "overbrightening factor (specular mask channel should be authored to account for
    /// this)", so a material pairs a big boost with a small mask — and anything that reads the mask
    /// wrong lands the boost raw. Measured on one `cp_process_final` material, the highlight was
    /// roughly three quarters of the surface's whole brightness, so "is phong doing this" is a
    /// question worth being able to answer by looking (B170).
    ///
    /// Re-sent immediately for the same reason <see cref="Specular"/> is: this is a shader constant
    /// the world camera carries, and the camera is set on a view CHANGE rather than per frame.
    /// </remarks>
    public bool Phong
    {
        get => _phong;

        set
        {
            _phong = value;
            ReapplyCamera();
        }
    }

    private bool _phong = true;

    /// <summary>Which <c>mat_fullbright</c> substitution the world draws with.</summary>
    /// <remarks>
    /// **The two non-zero states answer opposite questions**, which is why Valve has both and why
    /// this is not a boolean: 1 removes the lighting and asks "is that dark patch a shadow or a
    /// missing texture", 2 removes the albedo and asks "is that shape in the lighting or painted
    /// into the texture". See <see cref="Fullbright"/>.
    /// </remarks>
    public Fullbright Fullbright
    {
        get => _fullbright;

        set
        {
            _fullbright = value;
            ReapplyCamera();
        }
    }

    private Fullbright _fullbright = Fullbright.Off;

    /// <summary>Whether world surfaces and their overlays draw — Valve's <c>r_drawworld</c>.</summary>
    public bool DrawWorld
    {
        get => _drawWorld;

        set
        {
            _drawWorld = value;

            if (_world is not null)
            {
                _world.DrawWorld = value;
            }
        }
    }

    private bool _drawWorld = true;

    /// <summary>Whether static props and models draw — Valve's <c>r_drawentities</c>.</summary>
    public bool DrawEntities
    {
        get => _drawEntities;

        set
        {
            _drawEntities = value;

            if (_world is not null)
            {
                _world.DrawEntities = value;
            }
        }
    }

    private bool _drawEntities = true;

    /// <summary>Valve's per-surface debug visualisations — see <see cref="DebugModes"/>.</summary>
    public DebugModes Debug
    {
        get => _debug;

        set
        {
            _debug = value;
            ReapplyCamera();
        }
    }

    private DebugModes _debug = DebugModes.None;

    /// <summary>Whether a map's textures are resident.</summary>
    public bool HasWorldTextures => _world?.HasTextures ?? false;

    /// <summary>How many times a map's textures have been decoded and uploaded.</summary>
    public int TextureUploads => _world?.TextureUploads ?? 0;

    /// <summary>Forgets any uploaded map.</summary>
    /// <summary>Whether packed model geometry is still on the device (B148, B219).</summary>
    /// <remarks>
    /// **The authoritative answer, which is why the scene asks instead of remembering.**
    /// <see cref="ClearWorld"/> empties `_packedModels` along with the buffers they feed, and it has
    /// three callers — only one of which used to be paired with a reset on the other side.
    /// </remarks>
    public bool HasModels => _packedModels.Count > 0;

    public void ClearWorld()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _world?.Dispose();
        _world = null;

        // The renderer's buffers went with it, so the slices that fed them must go too — otherwise
        // a path from the previous map resolves to geometry nothing holds any more.
        _packedModels.Clear();
    }

    /// <summary>Whether a textured map is loaded.</summary>
    public bool HasWorld => _world?.HasMap ?? false;

    /// <summary>Whether the swap chain currently owns the display exclusively.</summary>
    public bool IsExclusiveFullScreen { get; private set; }

    /// <summary>Takes or releases exclusive control of the display.</summary>
    /// <param name="enabled">Whether to take the display.</param>
    /// <returns>Whether the request succeeded.</returns>
    /// <exception cref="ObjectDisposedException">The device has been disposed.</exception>
    /// <remarks>
    /// **Exclusive full screen is a real mode change, unlike a borderless window that merely
    /// covers the screen.** The swap chain owns the output, the display may switch mode, and
    /// presentation skips the desktop compositor.
    ///
    /// **It is allowed to fail, and failure is not an error here.** DXGI returns
    /// DXGI_ERROR_NOT_CURRENTLY_AVAILABLE when another application already holds the output, when
    /// the device is WARP, or when the window is not in a state it will accept — none of which is
    /// a defect in this program. The caller falls back to borderless and says so, because a viewer
    /// that refuses to go full screen at all is worse than one that goes full screen differently.
    /// </remarks>
    public bool SetExclusiveFullScreen(bool enabled)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (IsExclusiveFullScreen == enabled)
        {
            return true;
        }

        // Silk exposes SetFullscreenState on IDXGISwapChain1 rather than on the base interface,
        // so the swap chain is queried for it. Every swap chain created through
        // CreateDeviceAndSwapChain on a DXGI 1.2 runtime supports it; a machine old enough not to
        // is a machine that gets borderless.
        if (_swapChain.QueryInterface(out ComPtr<IDXGISwapChain1> queried) < 0)
        {
            return false;
        }

        // Null output: let DXGI pick the output the window is on, rather than naming one.
        int result = queried.SetFullscreenState(new Silk.NET.Core.Bool32(enabled), (IDXGIOutput*)null);
        queried.Dispose();

        if (result < 0)
        {
            return false;
        }

        IsExclusiveFullScreen = enabled;
        return true;
    }

    /// <summary>Which alternatives a model offers the draw, and which one it was told to show.</summary>
    /// <param name="instance">The model about to be drawn.</param>
    /// <param name="batches">The runs it will draw from.</param>
    /// <remarks>
    /// **The last unmeasured hop in B73.** The model offers four capture-point signs, the demo says
    /// which each point wants, and the packer keeps all four in separate tagged batches — every one
    /// of those was measured and every one was right, while the picture kept showing "?" on all
    /// three points. Three correct measurements only prove the fault is in the hop nobody looked at,
    /// and this is that hop: what the draw call actually receives.
    ///
    /// Reported once per model rather than per frame, because this runs sixty times a second.
    /// </remarks>
    private void ReportBodySelection(ModelInstance instance, IReadOnlyList<WorldBatch> batches)
    {
        if (!_reportedBodies.Add($"{instance.ModelPath}#{instance.Body}#{instance.Frame}"))
        {
            return;
        }

        int alternatives = batches
            .Select(batch => batch.BodyModel)
            .Distinct()
            .Count();

        // **Every model, not only the interesting ones.** The first version of this reported only
        // models with a choice to make, and it printed nothing at all for the run — which is the
        // one outcome that cannot be read, because "no model had alternatives" and "the model was
        // never drawn" produce the same silence. Naming everything drawn separates them: if the
        // hologram is absent from this list, the body number was never the problem.
        // **How many batches the body number actually keeps**, which is the question the earlier
        // version of this line could not answer. It reported what was available and not what
        // survived the filter, so a selection that kept everything looked identical to one that
        // kept a third — and "the BLU point draws every beam at once" is exactly that difference.
        //
        // Uses the renderer's own predicate rather than repeating its arithmetic here: a log that
        // computes the answer a second way can disagree with the draw, and then it is evidence
        // about itself.
        int kept = instance.BodyParts is { Count: > 0 } parts
            ? batches.Count(batch => WorldRenderer.Shows(parts, batch.BodyPart, batch.BodyModel, instance.Body))
            : batches.Count;

        // **Which materials the kept batches use, and which pass each lands in.** RED and neutral
        // are right and BLU is not, on one model with the same three meshes per team and a
        // selection measured as keeping three of nine for every one of them — so the difference is
        // ours and it is downstream of the choice. Naming the materials makes red and blue directly
        // comparable, which is the whole value of having a control that works.
        string drawn = _world is null
            ? "no renderer"
            : string.Join(
                ", ",
                batches
                    .Where(batch => instance.BodyParts is not { Count: > 0 } parts ||
                        WorldRenderer.Shows(parts, batch.BodyPart, batch.BodyModel, instance.Body))
                    .Select(batch =>
                        $"{batch.MaterialIndex}:{_world.DescribeMaterial(batch.MaterialIndex)}" +
                        $"/{_world.DescribeTexture(batch.MaterialIndex)}" +
                        $"@{batch.FirstVertex}+{batch.VertexCount}"));

        _render.LogInformation(
            "drawing {Model}: body {Body}, {Parts} parts, drawing {Kept} of {Batches} batches " +
            "spanning {Alternatives} alternatives — kept [{Drawn}]",
            instance.ModelPath,
            instance.Body,
            instance.BodyParts?.Count.ToString(CultureInfo.InvariantCulture) ?? "NO",
            kept,
            batches.Count,
            alternatives,
            drawn);
    }

    /// <summary>Models already reported on, so the log carries one line each.</summary>
    private readonly HashSet<string> _reportedBodies = [];

    /// <summary>Whether the repeated-model census has been written.</summary>
    private bool _reportedRepeats;

    /// <summary>The volume the draw culls against, or an unbuilt one that culls nothing.</summary>
    /// <remarks>
    /// **Unbuilt until a camera arrives, and unbuilt draws everything.** The safe direction: a
    /// viewer drawing more than it needs is slow, and one drawing nothing is a black screen that
    /// reads as a much deeper fault.
    /// </remarks>
    /// <summary>The view this frame is being drawn through.</summary>
    /// <remarks>
    /// **Exposed so the entity cull uses the SAME frustum as the world cull and the draw** (B254).
    /// Built once in <c>SetCamera</c> from the camera itself; a caller that rebuilt one from the
    /// camera's numbers would be a second derivation, free to disagree
    /// (`docs/memory/one-camera-or-the-cull-lies.md`).
    ///
    /// Unbuilt before the first camera is set, and an unbuilt frustum culls nothing.
    /// </remarks>
    public ViewFrustum Frustum => _frustum;

    /// <summary>Which leaves the world cull accepted this view, for the entity cull (B254).</summary>
    /// <remarks>
    /// Empty when the map carries no visibility data or no cull has run, and an empty span culls
    /// nothing — the safe direction, as everywhere else in this path.
    /// </remarks>
    public ReadOnlySpan<bool> VisibleByLeaf =>
        _culling is { } culling ? culling.VisibleByLeaf : default;

    private ViewFrustum _frustum;

    /// <summary>The view origin and forward axis the translucent sort measures against.</summary>
    /// <remarks>
    /// Captured in <see cref="SetCamera(FreeCamera, bool)"/> from the same camera as
    /// <see cref="_frustum"/> — the engine's <c>vecRenderOrigin</c> and <c>vecRenderForward</c>,
    /// which <c>SortEntities</c> takes from the view being rendered.
    /// </remarks>
    private (float X, float Y, float Z) _translucentEye;

    private (float X, float Y, float Z) _translucentForward = (1f, 0f, 0f);

    /// <summary>The translucent pass's reusable sort buffer: survivors with their view distance.</summary>
    private readonly List<(float Along, (ModelInstance Instance, bool TwoPass) Entry)>
        _translucentDraw = [];

    /// <summary>The view the current visible set was computed for, so a still camera pays nothing.</summary>
    private ((float X, float Y, float Z) Origin, (float Pitch, float Yaw, float Roll) Angles,
             float Fov, float Near, float Far, float Aspect)? _culledFor;

    /// <summary>Whether the opaque draw order has been reported.</summary>
    private bool _reportedDrawOrder;

    /// <summary>Whether the world cull has said what it kept, for this map.</summary>
    private bool _reportedWorldCull;

    /// <summary>Writes, once per map, how much of the world this eye can see.</summary>
    /// <remarks>
    /// **The world cull is even less visible than the model cull, and that is saying something.**
    /// It changes no pixel: the surfaces it removes are ones the depth buffer or the back-face test
    /// would have discarded anyway. Its entire effect is work not done, so nothing that looks at the
    /// output — a screenshot, a pixel assertion, a unit test — can tell whether it ran.
    ///
    /// **The three numbers separate the three ways it can quietly do nothing.** A map with no
    /// visibility data reports that in words rather than looking like a cull that kept everything; a
    /// leaf count equal to the map's total means the frustum or the PVS is inert; and runs equal to
    /// the uploaded batch count means the gather kept every face.
    /// </remarks>
    private void ReportWorldCull()
    {
        if (_reportedWorldCull || _world is null)
        {
            return;
        }

        if (_culling is null || !_culling.CanCull)
        {
            _reportedWorldCull = true;

            _render.LogInformation(
                "world cull: not available for this map, drawing every batch");

            return;
        }

        if (_world.VisibleBatches is not { } visible)
        {
            return;
        }

        _reportedWorldCull = true;

        _render.LogInformation(
            "world cull: {Leaves} of {TotalLeaves} leaves, {Corners} of {TotalCorners} corners, "
            + "{Runs} runs against {Batches} batches, {Unreachable} spans have no leaf and are "
            + "boxed instead",
            _culling.LeafCount,
            _culling.TotalLeaves,
            _culling.Corners.Drawn,
            _culling.Corners.Total,
            visible.Count,
            _world.BatchCount,
            _culling.UnreachableSpans);
    }

    /// <summary>Whether the viewmodel pass has said where it thinks the eye is (B170).</summary>
    private bool _reportedViewmodelEye;

    /// <summary>Whether the light the viewmodel instances carry has been reported (B170).</summary>
    private bool _reportedViewmodelInstanceLight;

    /// <summary>Every model drawn more than once, with the body number of each instance.</summary>
    /// <param name="models">This frame's instances.</param>
    /// <remarks>
    /// **Because the remaining suspect is a second instance, not a second mesh.** Everything about
    /// the capture point hologram measured symmetric across the three teams — the same three meshes
    /// per alternative, the same three of nine batches kept, and materials classified identically
    /// (two additive and one opaque for each). Nothing our chain does distinguishes BLU, yet BLU is
    /// the one drawing every beam.
    ///
    /// That rules out the shading and leaves the count. Five points on a five-point map should be
    /// five holograms carrying two RED, two BLU and one neutral at the start; anything else — a
    /// duplicate entity, or one point holding two — shows up here and nowhere else.
    /// </remarks>
    private void ReportRepeatedModels(IReadOnlyList<ModelInstance>? models)
    {
        if (_reportedRepeats || models is not { Count: > 0 })
        {
            return;
        }

        _reportedRepeats = true;

        foreach (IGrouping<string, ModelInstance> group in models
            .GroupBy(instance => instance.ModelPath)
            .Where(group => group.Count() > 1))
        {
            _render.LogInformation(
                "census {Model}: {Instances} instances, bodies {Bodies}",
                group.Key,
                group.Count(),
                string.Join(", ", group.Select(instance => instance.Body).Order()));
        }
    }

    /// <summary>Whether this instance lies entirely outside the view.</summary>
    /// <param name="instance">The model about to be drawn.</param>
    /// <returns>True when nothing of it can be seen.</returns>
    /// <remarks>
    /// **The same box the size bucket uses**, because the engine computes one box and does both
    /// with it. The opaque path gets this inside <see cref="OpaqueBuckets.InDrawOrder"/>, which
    /// culls and buckets in one pass; the translucent path has no sort to hang it on and calls it
    /// directly.
    /// </remarks>
    private bool Culled(ModelInstance instance)
    {
        // A model with no bounds is drawn rather than point-tested — see
        // WorldSpaceBounds.IsPlaced for what that cost.
        if (!_frustum.IsBuilt || !WorldSpaceBounds.IsPlaced(instance.WorldBounds))
        {
            return false;
        }

        (float MinX, float MinY, float MinZ, float MaxX, float MaxY, float MaxZ) box =
            instance.WorldBounds;

        return _frustum.Cull(box.MinX, box.MinY, box.MinZ, box.MaxX, box.MaxY, box.MaxZ);
    }

    /// <summary>Which render lists a model joins, and whether it is drawn in halves.</summary>
    /// <param name="instance">The model about to be drawn.</param>
    /// <returns>Whether it joins the opaque pass, the translucent pass, and whether it is split.</returns>
    /// <remarks>
    /// **<c>GetRenderGroup</c> then <c>SetRenderGroup</c> then <c>CollateRenderablesInLeaf</c>**, in
    /// that order and with those responsibilities — see <see cref="RenderGroups"/>, which is where
    /// the transcription and its citations live. Nothing is decided here; this is the wiring that
    /// asks.
    ///
    /// **What it replaced was every model drawn twice**, once filtered to its opaque materials and
    /// once to its blended ones, with nothing asking whether the engine would have split it. A
    /// model with no blended material was still walked in the translucent pass, batch by batch, to
    /// draw nothing.
    ///
    /// **The alpha is <see cref="RenderGroups.FullyOpaque"/> because nothing decodes it yet.**
    /// <c>m_clrRender</c> and <c>m_nRenderFX</c> are not read from the demo — <c>ComputeFxBlend</c>
    /// is a 210-line time-based switch over the render-FX kinds — so no entity here can be faded,
    /// cloaked or pulsing. That is a real gap and it is named rather than assumed: a fading two-pass
    /// model should stop being split, and until the alpha arrives none can fade. See RISKS.
    /// </remarks>
    private (bool Opaque, bool Translucent, bool TwoPass) Classify(ModelInstance instance)
    {
        if (_world is null)
        {
            return (true, false, false);
        }

        bool translucent = _world.IsTranslucent(
            _world.ModelBatches(instance.ModelPath, instance.Frame),
            instance.SkinSwap,
            instance.BodyParts,
            instance.Body);

        // **The alpha and the render mode are real now** (B221). These were `FullyOpaque` and
        // `Normal` from every caller because nothing decoded `m_clrRender`, `m_nRenderFX` or
        // `m_nRenderMode`; `EntityModels` runs `C_BaseEntity::ComputeFxBlend` per entity per frame
        // and the answer arrives on the instance.
        RenderGroup requested = RenderGroups.For(
            translucent, instance.TwoPass, isBrushModel: false, instance.Alpha, instance.RenderMode);

        (RenderGroup stored, bool twoPass) = RenderGroups.Store(requested);

        // **The same alpha, not `FullyOpaque` again.** `Lists` drops an invisible renderable
        // entirely — "Don't need to sort invisible stuff" — and 118 entities in a real match are
        // `kRenderNone`, which the engine does not draw at all.
        (bool opaque, bool blended) = RenderGroups.Lists(stored, twoPass, instance.Alpha);

        // **Logged when it CHANGES, not once per model, and the difference is the whole point.**
        // The symptom this was added for is a weapon that draws sometimes and not others, reported
        // around the moments a weapon's animation changes — so the question is not "what group is
        // this model in" but "did it move". A once-per-model line answers the first and is blind to
        // the second; a per-frame line buries it at sixty a second.
        //
        // The frame is carried because it is the input that varies: `Classify` resolves batches
        // through `ModelBatches(path, frame)`, so a model whose frame selects different batches can
        // legitimately classify differently from one frame to the next.
        // **Says when an entity is drawn at anything other than full alpha, once per model** (B221).
        // Without it "the fade path is wired" is unfalsifiable from a run: the classification line
        // below reports the GROUP, and a model can be opaque-grouped at alpha 200 exactly as it is
        // at 255. Guarded on the work as well as the write, and reported through `_faded` so a
        // model that fades every frame writes one line rather than sixty a second.
        if (instance.Alpha != RenderGroups.FullyOpaque &&
            _render.IsEnabled(LogLevel.Debug) &&
            _faded.Add(instance.ModelPath))
        {
            _render.LogDebug(
                "{Message}",
                $"{System.IO.Path.GetFileNameWithoutExtension(instance.ModelPath)} drawn at alpha " +
                $"{instance.Alpha} of 255, render mode {instance.RenderMode}, group {requested}");
        }

        _classified.TryGetValue(
            instance.ModelPath,
            out (RenderGroup Group, bool Opaque, bool Translucent, int Reported) was);

        bool moved = was.Group != requested || was.Opaque != opaque || was.Translucent != blended;

        // **Capped, because an unbounded log is its own defect.** A model that alternates every
        // frame is exactly the case worth seeing and exactly the case that would write sixty lines a
        // second; the first few carry the whole finding and the rest are weight. The cap is per
        // MODEL, so a second model flipping is still reported.
        if (moved && _classified.ContainsKey(instance.ModelPath) &&
            _render.IsEnabled(LogLevel.Debug))
        {
            _render.LogDebug(
                "{Message}",
                $"{System.IO.Path.GetFileNameWithoutExtension(instance.ModelPath)} changed render " +
                $"group: {was.Group} (opaque {was.Opaque}, translucent {was.Translucent}) -> " +
                $"{requested} (opaque {opaque}, translucent {blended}) at frame {instance.Frame}");
        }

        _classified[instance.ModelPath] =
            (requested, opaque, blended, moved ? was.Reported + 1 : was.Reported);

        return (opaque, blended, twoPass);
    }

    /// <summary>How many viewmodels the last frame drew: −1 for no pass at all.</summary>
    private int _viewmodelsDrawn = -2;

    /// <summary>Whether the viewmodel camera's matrix was last seen non-finite.</summary>
    private bool _viewmodelCameraBroken;

    /// <summary>Reports a viewmodel whose bones stop describing a pose, and when they recover.</summary>
    /// <param name="instance">The viewmodel about to be drawn.</param>
    /// <param name="arms">The first viewmodel prop, which the weapon is merged onto.</param>
    /// <remarks>
    /// **Three ways a bone can fail to place anything, and they are not the same fault.** A matrix of
    /// all zeros collapses its vertices onto the model origin; a non-finite one sends them nowhere
    /// the rasteriser will accept; and a zero-length basis flattens the model into a plane. All
    /// three draw perfectly and cover no pixels, which is indistinguishable on screen from a model
    /// that was never submitted — the difference this whole search has turned on.
    ///
    /// **Reported on CHANGE only.** A weapon that is fine for a minute and wrong for four frames
    /// writes two lines; a weapon that is always wrong writes one. Neither writes per frame.
    /// </remarks>
    private void ReportBonesIfDegenerate(ModelInstance instance, ModelInstance arms)
    {
        int bad = 0;

        // The span the bones cover, which is what says whether the model has any SIZE. A matrix can
        // be finite and non-zero and still collapse its vertices, so "not degenerate" is not the
        // same claim as "occupies space" — the first version of this checked only the first and is
        // why a confirmed reproduction came back clean.
        float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;

        if (instance.Bones is { Count: > 0 } bones)
        {
            foreach (float[] bone in bones)
            {
                bool finite = true;
                bool anySet = false;

                foreach (float value in bone)
                {
                    finite &= float.IsFinite(value);
                    anySet |= value != 0f;
                }

                // **A basis row of zero length flattens the model onto a plane or a point**, and
                // every value in it is a perfectly ordinary finite number. Rows 0, 1 and 2 of a
                // 3x4 row-major matrix are the axes; index 3, 7, 11 are the translation.
                bool collapsed = false;

                if (finite && bone.Length >= 12)
                {
                    for (int row = 0; row < 3; row++)
                    {
                        float a = bone[(row * 4) + 0];
                        float b = bone[(row * 4) + 1];
                        float c = bone[(row * 4) + 2];

                        collapsed |= (a * a) + (b * b) + (c * c) < 1e-8f;
                    }
                }

                if (!finite || !anySet || collapsed)
                {
                    bad++;
                }

                if (finite && bone.Length >= 12)
                {
                    minX = MathF.Min(minX, bone[3]);
                    maxX = MathF.Max(maxX, bone[3]);
                    minY = MathF.Min(minY, bone[7]);
                    maxY = MathF.Max(maxY, bone[7]);
                    minZ = MathF.Min(minZ, bone[11]);
                    maxZ = MathF.Max(maxZ, bone[11]);
                }
            }
        }

        // **Bucketed rather than reported raw, because a viewmodel's bones move every frame.** What
        // is wanted is not the number but whether it left the band a drawable weapon lives in: a
        // span of nought is a model collapsed to a point, and a huge one is a model whose bones have
        // been scattered. Anything between is ordinary and says nothing.
        float span = maxX < minX
            ? 0f
            : MathF.Max(maxX - minX, MathF.Max(maxY - minY, maxZ - minZ));

        string band = span switch
        {
            < 0.01f => "COLLAPSED",
            > 4096f => "SCATTERED",
            _ => "normal",
        };

        (float X, float Y, float Z) centre =
            ((minX + maxX) / 2f, (minY + maxY) / 2f, (minZ + maxZ) / 2f);

        // **How far the weapon is from the hands, which is the question that matters.** A viewmodel
        // weapon is merged onto the arms, so it lives within a few tens of units of them. A hundred
        // is generous — a weapon further away than the arms are long is not on screen, whatever its
        // bones look like in isolation.
        //
        // The arms are their own reference and always read `here`, which is the control: if that
        // ever says `AWAY`, the fault is in the measurement rather than in the weapon.
        string place = "here";

        if (!string.Equals(instance.ModelPath, arms.ModelPath, StringComparison.Ordinal) &&
            Centre(arms) is { } where)
        {
            float dx = centre.X - where.X;
            float dy = centre.Y - where.Y;
            float dz = centre.Z - where.Z;

            place = MathF.Sqrt((dx * dx) + (dy * dy) + (dz * dz)) > 100f ? "AWAY" : "here";
        }

        _boneState.TryGetValue(instance.ModelPath, out (int Bad, string Band, string Place) was);

        if (bad == was.Bad && band == (was.Band ?? string.Empty) &&
            place == (was.Place ?? string.Empty))
        {
            return;
        }

        _boneState[instance.ModelPath] = (bad, band, place);

        if (!_render.IsEnabled(LogLevel.Debug))
        {
            return;
        }

        _render.LogDebug(
            "{Message}",
            $"{System.IO.Path.GetFileNameWithoutExtension(instance.ModelPath)} bones changed: " +
            $"{was.Bad} degenerate -> {bad} of {instance.Bones?.Count ?? 0}, " +
            $"span {span:0.##} ({was.Band ?? "(first)"} -> {band}), " +
            $"placement {was.Place ?? "(first)"} -> {place}, " +
            $"centre ({centre.X:0.#}, {centre.Y:0.#}, {centre.Z:0.#}), " +
            $"frame {instance.Frame}");
    }

    /// <summary>The centre of a model's posed bones, or null when it has none.</summary>
    private static (float X, float Y, float Z)? Centre(ModelInstance instance)
    {
        if (instance.Bones is not { Count: > 0 } bones)
        {
            return null;
        }

        float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;

        foreach (float[] bone in bones)
        {
            if (bone.Length < 12 || !float.IsFinite(bone[3]))
            {
                continue;
            }

            minX = MathF.Min(minX, bone[3]);
            maxX = MathF.Max(maxX, bone[3]);
            minY = MathF.Min(minY, bone[7]);
            maxY = MathF.Max(maxY, bone[7]);
            minZ = MathF.Min(minZ, bone[11]);
            maxZ = MathF.Max(maxZ, bone[11]);
        }

        return maxX < minX
            ? null
            : ((minX + maxX) / 2f, (minY + maxY) / 2f, (minZ + maxZ) / 2f);
    }

    /// <summary>Each viewmodel's last reported bone state: degenerate count, span band, placement.</summary>
    private readonly Dictionary<string, (int Bad, string Band, string Place)> _boneState = [];


    /// <summary>How a viewmodel count reads in the log.</summary>
    private static string Describe(int drawing) => drawing switch
    {
        -2 => "(first frame)",
        -1 => "NO PASS (no world or no first-person camera)",
        0 => "NOTHING (the scene supplied no viewmodel)",
        _ => $"{drawing} drawn",
    };

    /// <summary>The last render group each model classified into, to report a change.</summary>
    private readonly Dictionary<string, (RenderGroup Group, bool Opaque, bool Translucent, int Reported)>
        _classified = [];

    /// <summary>Models already reported as drawn below full alpha, so each says so once.</summary>
    /// <remarks>
    /// **Not capped by a count** — the project's rule is that a diagnostic is never bounded by a
    /// report limit, because the run that matters is the one where the interesting thing happens
    /// late. Bounded by IDENTITY instead: one line per model path, however many frames it fades for.
    /// </remarks>
    private readonly HashSet<string> _faded = [];

    /// <summary>Writes, once, what the cull kept and how it spread across Valve's size buckets.</summary>
    /// <param name="offered">Every instance the scene produced, before culling.</param>
    /// <param name="ordered">What survived, in the order it is about to be drawn.</param>
    /// <remarks>
    /// **Because neither the sort nor the cull is visible in the picture, and an invisible step is
    /// one that can quietly stop happening.** Measured before this line existed: removing
    /// <see cref="OpaqueBuckets.InDrawOrder"/> from the draw loop left all 566 rendering tests
    /// green. Both steps change a frame rate rather than an image, so nothing that looks at the
    /// output can see them either.
    ///
    /// **Three numbers, each distinguishing a different silent failure.**
    ///
    /// * The offered count against the kept count says whether the cull ran at all. Equal counts
    ///   mean an unbuilt frustum, which is the state a caller reaches by setting the camera through
    ///   the float-matrix overload — legal, and indistinguishable from "everything is on screen"
    ///   without this line.
    /// * The bucket spread says whether <see cref="ModelInstance.WorldBounds"/> arrived. A zero box
    ///   buckets as the smallest, so an unset one reads as `0/0/0/N` — and would also be culled by
    ///   nothing, since a degenerate box straddles every plane it touches.
    /// * The kept count being ZERO on a map with models is the frustum pointing the wrong way, which
    ///   is the failure that produces a black screen.
    ///
    /// One line per device, like the repeated-model census beside it: this is a wiring check, and a
    /// per-frame version would be tens of thousands of lines saying the same thing.
    /// </remarks>
    private void ReportDrawOrder(
        IReadOnlyList<ModelInstance>? offered, IReadOnlyList<ModelInstance> ordered)
    {
        if (_reportedDrawOrder || offered is not { Count: > 0 })
        {
            return;
        }

        _reportedDrawOrder = true;

        int[] perBucket = new int[OpaqueBuckets.Count];

        foreach (ModelInstance instance in ordered)
        {
            perBucket[OpaqueBuckets.BucketFor(
                WorldSpaceBounds.LongestAxisOf(instance.WorldBounds))]++;
        }

        // **Says whether the FRUSTUM WAS BUILT, not only how many survived it** (B241). The count
        // alone cannot distinguish a cull that ran and kept everything from a cull that never ran:
        // `Culled` returns false outright when `_frustum.IsBuilt` is false, so an unbuilt frustum
        // and a scene entirely on screen produce the identical line.
        //
        // A UI test asserted `kept < offered` for exactly this and passed for months on a scene
        // that happened to have models behind the camera — until placing parented props correctly
        // brought them back into view and it read 20 of 20. The number it wanted was never the
        // count; it was this flag.
        _render.LogInformation(
            "opaque draw order: {Kept} of {Offered} models kept, frustum {Frustum}, buckets {Buckets}",
            ordered.Count,
            offered.Count,
            _frustum.IsBuilt ? "built" : "UNBUILT",
            string.Join('/', perBucket));

        // **Which models were dropped, by name and by box.** A count says the cull ran; it cannot
        // say whether it ate something it should not have. The owner watched badlands roller doors
        // show the wall behind them, and no log anywhere could answer "was the door culled" — which
        // is the first question and was unanswerable.
        HashSet<string> kept = [];

        foreach (ModelInstance instance in ordered)
        {
            kept.Add(instance.ModelPath);
        }

        foreach (ModelInstance instance in offered)
        {
            if (kept.Contains(instance.ModelPath))
            {
                continue;
            }

            (float MinX, float MinY, float MinZ, float MaxX, float MaxY, float MaxZ) box =
                instance.WorldBounds;

            _render.LogInformation(
                "  culled {Model}: world box {World}",
                instance.ModelPath,
                $"({box.MinX:0},{box.MinY:0},{box.MinZ:0})..({box.MaxX:0},{box.MaxY:0},{box.MaxZ:0})");

            kept.Add(instance.ModelPath);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        // **Mandatory, and not merely tidy.** Releasing a swap chain while it holds the display
        // exclusively is undefined: DXGI documents that the swap chain must be returned to windowed
        // mode first, and in practice it hangs or leaves the display in the switched mode.
        if (IsExclusiveFullScreen)
        {
            if (_swapChain.QueryInterface(out ComPtr<IDXGISwapChain1> windowed) >= 0)
            {
                windowed.SetFullscreenState(new Silk.NET.Core.Bool32(false), (IDXGIOutput*)null);
                windowed.Dispose();
            }

            IsExclusiveFullScreen = false;
        }

        _world?.Dispose();
        _hud?.Dispose();
        _points?.Dispose();
        _worldLines?.Dispose();
        ReleaseDepth();
        _depthOn.Dispose();
        _depthOff.Dispose();
        _backBufferView.Dispose();
        _swapChain.Dispose();
        _context.Dispose();
        _device.Dispose();
        _d3d.Dispose();
        _disposed = true;
    }

    private void ReleaseDepth()
    {
        if (_depthView.Handle is not null)
        {
            _depthView.Dispose();
            _depthView = default;
        }

        if (_depthBuffer.Handle is not null)
        {
            _depthBuffer.Dispose();
            _depthBuffer = default;
        }
    }

    private void CreateBackBufferView()
    {
        SilkMarshal.ThrowHResult(_swapChain.GetBuffer(0u, out ComPtr<ID3D11Texture2D> buffer));

        // **An sRGB view over a UNORM buffer, which is how the hardware applies gamma on write.**
        // Colour arithmetic belongs in linear space - the engine multiplies linear lightmaps by
        // linear albedo and lets the target encode the result once (B54). A flip-model swap chain
        // may not itself be sRGB, so the conversion is asked for here, on the view.
        RenderTargetViewDesc description = new()
        {
            Format = Silk.NET.DXGI.Format.FormatB8G8R8A8UnormSrgb,
            ViewDimension = RtvDimension.Texture2D,
        };

        ComPtr<ID3D11RenderTargetView> view = default;
        SilkMarshal.ThrowHResult(_device.CreateRenderTargetView(buffer, in description, ref view));

        buffer.Dispose();
        _backBufferView = view;

        CreateDepthView();

        _context.OMSetRenderTargets(
            1u, _backBufferView.GetAddressOf(), _depthView);
    }

    /// <summary>Creates a depth buffer matching the back buffer.</summary>
    /// <remarks>
    /// **24-bit fixed point, because that is what the engine uses, and depth constants only mean
    /// something relative to a format (D48).** This was <c>D32_FLOAT</c>, and the difference is not
    /// cosmetic: D3D11 applies a rasteriser's <c>DepthBias</c> as <c>DepthBias × r</c>, where for a
    /// UNORM buffer <c>r</c> is the fixed <c>1 / 2^24</c> and for a FLOAT buffer it is
    /// <c>2^(exponent(max depth in the primitive) − 23)</c> — data-dependent, and roughly double
    /// near a depth of 1.
    ///
    /// So every depth constant in this renderer meant something other than what it said. The decal
    /// bias was the plain case: a <c>SetDecalBias</c> method computed <c>2^24 / worldRange</c> and
    /// called the result "about one world unit", which is the arithmetic for a 24-bit fixed-point
    /// buffer. Against a float buffer it was neither one unit nor any fixed distance, and the wall
    /// stripes were tuned around it. That method is gone — see B135; it also overwrote the state
    /// built at load, which made every later experiment on the constant measure nothing.
    ///
    /// **The projection already matched and this was the last piece that did not.** The near plane
    /// is the engine's own <c>VIEW_NEARZ</c> of 7, the field of view its <c>CViewSetup</c> default
    /// of 75, and the viewmodel pass mirrors Source's separate near plane of 1. Leaving the buffer
    /// format different meant every depth comparison against the game carried a silent translation
    /// step — which is a debugging cost paid on every future depth question, not just this one.
    ///
    /// **The trade, stated so it is not rediscovered as a defect:** this forecloses reversed-Z,
    /// which pairs float precision with a projection's distribution and would beat both options in
    /// the far field. Parity was chosen over it deliberately. The eight stencil bits are unused.
    /// </remarks>
    /// <summary>The depth buffer format, matching the engine's (D48).</summary>
    /// <remarks>
    /// **Named rather than repeated, because two buffers have to agree and a test has to be able to
    /// ask.** <see cref="OffscreenTarget"/> builds its depth buffer from this same constant: a
    /// D3D11 rasteriser's <c>DepthBias</c> is scaled by a factor the FORMAT decides — a fixed 1/2²⁴
    /// for UNORM, data-dependent for FLOAT — so a capture on one format and a window on the other
    /// place markings differently, and the capture is what tests and screenshots are read from.
    ///
    /// It was two literals in two files until the conformance sweep, and the test that checked they
    /// agreed did it by reading both files as TEXT. That instrument passes on a comment and fails
    /// on a rename, neither of which is the question.
    /// </remarks>
    internal const Format DepthFormat = Format.FormatD24UnormS8Uint;

    private void CreateDepthView()
    {
        Texture2DDesc description = new()
        {
            Width = (uint)_width,
            Height = (uint)_height,
            MipLevels = 1,
            ArraySize = 1,
            Format = DepthFormat,
            SampleDesc = new SampleDesc(1, 0),
            Usage = Usage.Default,
            BindFlags = (uint)BindFlag.DepthStencil,
        };

        ComPtr<ID3D11Texture2D> buffer = default;
        SilkMarshal.ThrowHResult(_device.CreateTexture2D(
            in description, ref Unsafe.NullRef<SubresourceData>(), ref buffer));

        ComPtr<ID3D11DepthStencilView> view = default;
        SilkMarshal.ThrowHResult(_device.CreateDepthStencilView(
            buffer, ref Unsafe.NullRef<DepthStencilViewDesc>(), ref view));

        _depthBuffer = buffer;
        _depthView = view;

        if (_depthOn.Handle is null)
        {
            _depthOn = DepthState(enabled: true);
            _depthReadOnly = DepthState(enabled: true, writes: false);
            _depthOff = DepthState(enabled: false);
        }
    }

    /// <summary>Builds a depth-stencil state.</summary>
    /// <remarks>
    /// The world draws with depth testing so a roof hides the floor under it. The map outline and
    /// the players draw with it OFF, because they are annotations on the world rather than part of
    /// it - a player marker must not disappear behind the roof they are standing on.
    /// </remarks>
    /// <param name="enabled">Whether depth is tested at all.</param>
    /// <param name="writes">Whether it is also written; ignored when depth is off.</param>
    private ComPtr<ID3D11DepthStencilState> DepthState(bool enabled, bool writes = true)
    {
        DepthStencilDesc description = new()
        {
            DepthEnable = new Silk.NET.Core.Bool32(enabled),
            DepthWriteMask = enabled && writes ? DepthWriteMask.All : DepthWriteMask.Zero,

            // LessEqual rather than Less for the read-only state: a blended pass redraws geometry
            // at exactly the depth an earlier pass wrote, and Less rejects all of it.
            DepthFunc = writes ? ComparisonFunc.Less : ComparisonFunc.LessEqual,
        };

        ComPtr<ID3D11DepthStencilState> state = default;
        SilkMarshal.ThrowHResult(_device.CreateDepthStencilState(in description, ref state));

        return state;
    }
}
