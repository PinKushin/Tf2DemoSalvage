using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;

namespace Tf2DemoSalvage.Viewer3D;

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
internal sealed unsafe class Device3D : IDisposable
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
    private bool _disposed;

    private Device3D(
        D3D11 d3d,
        ComPtr<ID3D11Device> device,
        ComPtr<ID3D11DeviceContext> context,
        ComPtr<IDXGISwapChain> swapChain)
    {
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
    public static Device3D Create(nint handle, int width, int height)
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

        Device3D created = new(d3d, device, context, swapChain)
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

    /// <summary>Sizes the decal depth bias for the map's height range.</summary>
    /// <param name="worldRange">Highest world height minus lowest, in units.</param>
    /// <exception cref="ObjectDisposedException">The device has been disposed.</exception>
    public void SetDecalBias(float worldRange)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _world ??= WorldRenderer.Create(_device);
        _world.SetDecalBias(_device, worldRange);
    }

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
        _world ??= WorldRenderer.Create(_device);

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
                    WritePng(path, (int)description.Width, (int)description.Height, mapped);
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
            ViewerLog.Warn("render", $"capturing the viewport to {path}", failure);
        }
        finally
        {
            back.Dispose();
        }
    }

    private static void WritePng(string path, int width, int height, MappedSubresource mapped)
    {
        string? folder = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));

        if (folder is not null)
        {
            System.IO.Directory.CreateDirectory(folder);
        }

        using System.Drawing.Bitmap bitmap = new(
            width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

        for (int y = 0; y < height; y++)
        {
            // RowPitch, not width * 4: the driver pads rows to its own alignment.
            byte* row = (byte*)mapped.PData + ((uint)y * mapped.RowPitch);

            for (int x = 0; x < width; x++)
            {
                byte* pixel = row + (x * 4);

                // The back buffer is B8G8R8A8, so the bytes arrive blue first.
                bitmap.SetPixel(
                    x, y, System.Drawing.Color.FromArgb(255, pixel[2], pixel[1], pixel[0]));
            }
        }

        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);

        ViewerLog.Write("render", $"captured the viewport to {path}");
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

    /// <summary>Clears, draws the map and the players, and presents.</summary>
    /// <param name="red">Clear colour, red channel.</param>
    /// <param name="green">Clear colour, green channel.</param>
    /// <param name="blue">Clear colour, blue channel.</param>
    /// <param name="mapFill">Filled map surfaces in clip space, three corners per triangle.</param>
    /// <param name="mapLines">Map outline in clip space.</param>
    /// <param name="points">Player positions in clip space.</param>
    /// <exception cref="ObjectDisposedException">The device has been disposed.</exception>
    /// <param name="models">Posed entity models, or null to draw none.</param>
    /// <remarks>
    /// The map goes down first so the players draw over it. There is no depth buffer and none is
    /// wanted: for a flat overhead view the draw order IS the layering, and it is one fewer
    /// resource to resize when the window changes.
    /// </remarks>
    public void DrawFrame(
        float red,
        float green,
        float blue,
        IReadOnlyList<(float X, float Y, float Shade)> mapFill,
        IReadOnlyList<((float X, float Y) From, (float X, float Y) To)> mapLines,
        IReadOnlyList<ScenePoint> points,
        IReadOnlyList<ModelInstance>? models = null)
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
                        instance.SkinSwap);
                }
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

        _world ??= WorldRenderer.Create(_device);
        _world.UploadMap(_device, _context, world.Vertices, world.Batches, assets);
    }

    /// <summary>Uploads a map's textures, without touching its geometry.</summary>
    /// <param name="assets">The map's textures and lightmap atlas.</param>
    /// <exception cref="ObjectDisposedException">The device has been disposed.</exception>
    public void UploadWorldTextures(MapAssets assets)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _world ??= WorldRenderer.Create(_device);
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

        _world ??= WorldRenderer.Create(_device);
        _world.UploadGeometry(_device, world.Vertices, world.Batches, world.Decals);
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

        _world ??= WorldRenderer.Create(_device);
        _world.SetCamera(_device, _context, matrix, surfaceColours, heightCut);
    }

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
    private void CreateDepthView()
    {
        Texture2DDesc description = new()
        {
            Width = (uint)_width,
            Height = (uint)_height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.FormatD32Float,
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
            _depthOff = DepthState(enabled: false);
        }
    }

    /// <summary>Builds a depth-stencil state.</summary>
    /// <remarks>
    /// The world draws with depth testing so a roof hides the floor under it. The map outline and
    /// the players draw with it OFF, because they are annotations on the world rather than part of
    /// it - a player marker must not disappear behind the roof they are standing on.
    /// </remarks>
    private ComPtr<ID3D11DepthStencilState> DepthState(bool enabled)
    {
        DepthStencilDesc description = new()
        {
            DepthEnable = new Silk.NET.Core.Bool32(enabled),
            DepthWriteMask = enabled ? DepthWriteMask.All : DepthWriteMask.Zero,
            DepthFunc = ComparisonFunc.Less,
        };

        ComPtr<ID3D11DepthStencilState> state = default;
        SilkMarshal.ThrowHResult(_device.CreateDepthStencilState(in description, ref state));

        return state;
    }
}
