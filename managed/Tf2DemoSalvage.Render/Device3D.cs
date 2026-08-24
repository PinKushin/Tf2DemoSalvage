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
public sealed unsafe class Device3D : IDisposable
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
    private WorldRenderer? _world;

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

        Dictionary<string, IReadOnlyList<IReadOnlyList<WorldBatch>>> batches =
            new(StringComparer.OrdinalIgnoreCase);

        foreach (string path in models.Paths)
        {
            batches[path] = models.AllFrames(path);
        }

        _world.UploadModels(_device, models.Vertices, batches);
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
        if (_world is null || viewmodels is not { Count: > 0 } || camera is null)
        {
            // **Which of the three, because they are different faults.** No renderer, nothing to
            // draw, or no camera — and a pass that silently does nothing looks exactly like a pass
            // that drew something invisible, which is the confusion this whole feature has lived in.
            _render.LogInformation(
                "viewmodel pass skipped: world {World}, instances {Instances}, camera {Camera}",
                _world is not null,
                viewmodels?.Count ?? -1,
                camera is not null);

            return;
        }

        // **Where they actually are, in world units.** Every earlier check confirmed the model was
        // packed, posed, instanced and listed — none of them said where it ended up, and "drawn
        // somewhere off screen" and "drawn nowhere" look identical from every one of them.
        // Guarded: the join below walks every viewmodel and formats nine numbers each, which is
        // exactly the work CA1873 keeps out of a disabled log.
        if (_render.IsEnabled(LogLevel.Information))
        {
            _render.LogInformation(
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

        Viewport near = new(
            0f, 0f, _width, _height, ViewmodelPass.DepthMinimum, ViewmodelPass.DepthMaximum);

        _context.RSSetViewports(1, in near);
        _world.SetCamera(_device, _context, camera);
        _context.OMSetDepthStencilState(_depthOn, 0);

        // **No depth clear, which is the engine's arrangement.** Source compresses the viewmodel
        // into the near tenth of the buffer instead, and only clears under Portal. Clearing was
        // tried here as a diagnostic and changed nothing, which is how depth was ruled out.

        foreach (ModelInstance instance in viewmodels)
        {
            if (instance.Bones is { Count: > 0 } bones)
            {
                _world.SetBones(_context, bones);
            }

            _world.DrawModel(
                _context,
                instance.Matrix,
                _world.ModelBatches(instance.ModelPath, instance.Frame),
                instance.Light,
                instance.Sun,
                instance.Blend,
                instance.Bones?.Count ?? 0,
                instance.SkinSwap,
                blended: false,
                instance.BodyParts,
                instance.Body,
                // Culled normally, per material, like everything else. Drawing both sides was tried
                // as a diagnostic and changed nothing, which ruled winding out.
                instance.Mirrored);
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

    /// <summary>The last world camera set, so the viewmodel pass can put it back.</summary>
    private (float[] Matrix, bool Colours, float HeightCut)? _worldCamera;

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
            restore.HeightCut,
            _specular,
            _fullbright,
            _debug);
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
    /// <param name="mapLines">Map outline in clip space.</param>
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
        IReadOnlyList<((float X, float Y) From, (float X, float Y) To)> mapLines,
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

                foreach (ModelInstance instance in models ?? [])
                {
                    if (instance.Bones is { Count: > 0 } bones)
                    {
                        _world.SetBones(_context, bones);
                    }

                    ReportBodySelection(instance, _world.ModelBatches(instance.ModelPath, instance.Frame));

                    _world.DrawModel(
                        _context,
                        instance.Matrix,
                        _world.ModelBatches(instance.ModelPath, instance.Frame),
                        instance.Light,
                        instance.Sun,
                        instance.Blend,
                        instance.Bones?.Count ?? 0,
                        instance.SkinSwap,
                        blended: false,
                        instance.BodyParts,
                        instance.Body,
                        instance.Mirrored);
                }

                // **The see-through parts of models, after every solid one.** A hologram, a glass
                // visor and a cloaked spy all have to blend against what is behind them, so they
                // can only be drawn once that has been drawn — which is the same reason the world
                // keeps its translucent surfaces to the end.
                //
                // Depth is still TESTED so a hologram behind a wall stays hidden, and no longer
                // WRITTEN so it does not erase whatever is meant to show through it.
                _context.OMSetDepthStencilState(_depthReadOnly, 0);

                foreach (ModelInstance instance in models ?? [])
                {
                    if (instance.Bones is { Count: > 0 } bones)
                    {
                        _world.SetBones(_context, bones);
                    }

                    _world.DrawModel(
                        _context,
                        instance.Matrix,
                        _world.ModelBatches(instance.ModelPath, instance.Frame),
                        instance.Light,
                        instance.Sun,
                        instance.Blend,
                        instance.Bones?.Count ?? 0,
                        instance.SkinSwap,
                        blended: true,
                        instance.BodyParts,
                        instance.Body,
                        instance.Mirrored);
                }

                WorldRenderer.ResetBlend(_context);

                DrawViewmodels(viewmodels, viewmodelCamera);
            }
            else
            {
                _points.DrawTriangles(_device, _context, mapFill, (0.30f, 0.34f, 0.42f));
            }

            // Outlines and players are annotations on the world, so they ignore its depth.
            _context.OMSetDepthStencilState(_depthOff, 0);

            _points.DrawLines(_device, _context, mapLines, 0.55f, 0.62f, 0.74f);
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

    /// <summary>Sets the view the world is drawn through.</summary>
    /// <param name="matrix">Sixteen floats, row major.</param>
    /// <param name="surfaceColours">Whether to draw flat category colours instead of textures.</param>
    /// <param name="heightCut">Discard anything above this height, from 0 (all) to 1 (nothing).</param>
    /// <exception cref="ObjectDisposedException">The device has been disposed.</exception>
    /// <remarks>
    /// **The resize path, now.** Geometry is uploaded in world coordinates and stays; a viewport
    /// change rewrites one 64-byte buffer instead of rebuilding every vertex.
    /// </remarks>
    public void SetCamera(float[] matrix, bool surfaceColours = false, float heightCut = 0f)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _world ??= WorldRenderer.Create(_device, _loggers);

        // Applied here rather than only in the setter, because the renderer is built lazily and a
        // toggle flipped before the first map would otherwise be silently forgotten.
        _world.Wireframe = _wireframe;
        _world.DrawWorld = _drawWorld;
        _world.DrawEntities = _drawEntities;

        _world.SetCamera(
            _device, _context, matrix, surfaceColours, heightCut, _specular, _fullbright, _debug);

        // Remembered so the viewmodel pass can put it back. The world's camera is set on a view
        // CHANGE rather than per frame, so anything that overwrites it has to restore it or the
        // map keeps the wrong projection until the user next moves.
        _worldCamera = (matrix, surfaceColours, heightCut);
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
    public void ClearWorld()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _world?.Dispose();
        _world = null;
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
