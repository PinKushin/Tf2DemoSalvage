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
        IReadOnlyList<ScenePoint> points)
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

        // No vertical sync yet. A demo viewer scrubbing through ticks wants frames as fast as it
        // can produce them while the camera is being dragged; pacing belongs with playback.
        SilkMarshal.ThrowHResult(_swapChain.Present(SyncInterval: 0u, Flags: 0u));
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
        _world.UploadGeometry(_device, world.Vertices, world.Batches);
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

        ComPtr<ID3D11RenderTargetView> view = default;
        SilkMarshal.ThrowHResult(_device.CreateRenderTargetView(
            buffer, (RenderTargetViewDesc*)null, ref view));

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
