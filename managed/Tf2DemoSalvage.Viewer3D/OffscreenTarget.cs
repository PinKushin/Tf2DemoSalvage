using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Viewer3D;

/// <summary>
/// A render target in memory, with the pixels readable back on the CPU.
/// </summary>
/// <remarks>
/// **This is what makes rendering testable.** Draw into a texture, copy it to a staging resource,
/// read the bytes: "a dot appears at this pixel" becomes an exact assertion instead of something a
/// person squints at. No window, no swap chain, no desktop — so it runs anywhere the ordinary unit
/// tests do.
///
/// It lives in the application rather than the test project because it needs the same Direct3D
/// types the renderer does, and because a debugging session benefits from being able to dump a
/// frame without a display attached.
///
/// **Falls back to WARP**, Direct3D's software rasteriser, when no hardware adapter answers. That
/// is what lets these tests run on a CI runner, and WARP is a reference implementation — a
/// difference between it and a GPU is a bug worth knowing about, not noise to design around.
/// </remarks>
internal sealed unsafe class OffscreenTarget : IDisposable
{
    private readonly D3D11 _d3d;
    private readonly int _width;
    private readonly int _height;

    private ComPtr<ID3D11Device> _device;
    private ComPtr<ID3D11DeviceContext> _context;
    private ComPtr<ID3D11Texture2D> _texture;
    private ComPtr<ID3D11Texture2D> _staging;
    private ComPtr<ID3D11Texture2D> _depthTexture;
    private ComPtr<ID3D11DepthStencilView> _depthView;
    private ComPtr<ID3D11RenderTargetView> _view;
    private PointRenderer? _points;
    private WorldRenderer? _world;

    private OffscreenTarget(
        D3D11 d3d,
        int width,
        int height,
        ComPtr<ID3D11Device> device,
        ComPtr<ID3D11DeviceContext> context,
        ComPtr<ID3D11Texture2D> texture,
        ComPtr<ID3D11Texture2D> staging,
        ComPtr<ID3D11RenderTargetView> view,
        ComPtr<ID3D11Texture2D> depthTexture,
        ComPtr<ID3D11DepthStencilView> depthView)
    {
        _d3d = d3d;
        _width = width;
        _height = height;
        _device = device;
        _context = context;
        _texture = texture;
        _staging = staging;
        _view = view;
        _depthTexture = depthTexture;
        _depthView = depthView;
    }

    /// <summary>Creates a target, or returns null if no Direct3D 11 device can be made at all.</summary>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    /// <returns>The target, or <c>null</c> when even WARP is unavailable.</returns>
    /// <remarks>
    /// Null rather than an exception because "this machine has no Direct3D" is a reason to skip a
    /// test, not to fail one. A missing GPU is a property of the machine; a wrongly drawn pixel is
    /// a property of the code.
    /// </remarks>
    public static OffscreenTarget? TryCreate(int width, int height)
    {
        D3D11 d3d = D3D11.GetApi(null);

        ComPtr<ID3D11Device> device = default;
        ComPtr<ID3D11DeviceContext> context = default;

        // Hardware first, then WARP. WARP is a full software implementation of the same feature
        // levels, so the same assertions hold on a machine with no adapter.
        foreach (D3DDriverType driver in new[] { D3DDriverType.Hardware, D3DDriverType.Warp })
        {
            int result = d3d.CreateDevice(
                default(ComPtr<IDXGIAdapter>),
                driver,
                0,
                0u,
                null,
                0u,
                D3D11.SdkVersion,
                ref device,
                null,
                ref context);

            if (result >= 0)
            {
                return Build(d3d, width, height, device, context);
            }
        }

        d3d.Dispose();
        return null;
    }

    /// <summary>Playback time handed to material proxies, which are functions of it.</summary>
    /// <remarks>
    /// Settable here so a test can draw the same geometry at two times and compare — which is the
    /// only way to observe a proxy from outside, since its whole effect is that the picture differs
    /// between them.
    /// </remarks>
    public double Seconds { get; set; }

    /// <summary>
    /// Draws world geometry through the real world shader, for tests with no screen.
    /// </summary>
    /// <param name="vertices">Triangle corners in world coordinates.</param>
    /// <param name="batches">Material runs.</param>
    /// <param name="matrix">Camera matrix, sixteen floats row major.</param>
    /// <param name="assets">Textures to bind; the shader clips on their alpha.</param>
    /// <param name="surfaceColours">Draw flat category colours instead of textures.</param>
    /// <param name="heightCut">Discard anything above this height, 0 to 1.</param>
    /// <param name="detail">Combine each material's detail texture; false renders without.</param>
    /// <param name="bumped">Light bumped surfaces directionally; false uses the flat lightmap.</param>
    /// <param name="decals">Overlay runs, drawn over the world with a depth bias.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **The renderer's own shader, not a copy of it.** Everything this project invents rather than
    /// reads from Valve - the camera matrix, the category view, the height cut - has no source to
    /// check against, so the only way to know it works is to draw it and look at the pixels. Doing
    /// that offscreen is what makes "look at the pixels" something a test can do.
    ///
    /// The height cut in particular went three rounds of "it does not work" with no way to tell
    /// whether the constant reached the shader, the depth values were wrong, or the key never
    /// arrived.
    /// </remarks>
    public void DrawWorld(
        IReadOnlyList<WorldVertex> vertices,
        IReadOnlyList<WorldBatch> batches,
        float[] matrix,
        MapAssets assets,
        bool surfaceColours = false,
        float heightCut = 0f,
        bool detail = true,
        bool bumped = true,
        IReadOnlyList<WorldBatch>? decals = null)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(batches);
        ArgumentNullException.ThrowIfNull(matrix);
        ArgumentNullException.ThrowIfNull(assets);

        _world ??= WorldRenderer.Create(_device);
        _world.DrawDetail = detail;
        _world.DrawBumped = bumped;
        _world.Seconds = Seconds;

        // **Textures first, because the shader clips on their alpha.** With none bound the sample
        // returns zero and every fragment is discarded - which reads as "the geometry is wrong".
        _world.UploadTextures(_device, _context, assets);
        _world.UploadGeometry(_device, vertices, batches, decals);
        _world.SetCamera(_device, _context, matrix, surfaceColours, heightCut);

        Viewport viewport = new(0f, 0f, _width, _height, 0f, 1f);

        _context.RSSetViewports(1, in viewport);
        _context.ClearDepthStencilView(_depthView, (uint)ClearFlag.Depth, 1f, 0);
        _context.OMSetRenderTargets(1u, _view.GetAddressOf(), _depthView);

        _world.Draw(_context);
    }

    /// <summary>Draws one posed model through the model path, offscreen.</summary>
    /// <param name="vertices">The model's triangles, in model space.</param>
    /// <param name="batches">Its runs over those vertices.</param>
    /// <param name="camera">The view-projection matrix.</param>
    /// <param name="model">The model matrix, row-vector, translation in row three.</param>
    /// <param name="assets">The map's materials, which the model's own continue.</param>
    /// <param name="light">The ambient cube reaching it, or null for none.</param>
    /// <param name="bothSides">Draw every face regardless of winding, as <c>$nocull</c> does.</param>
    /// <param name="sun">The sun reaching it, or null for a model in shade.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **The model path is not the world path and the difference has hidden a defect.** Every
    /// offscreen test so far drew through <see cref="DrawWorld"/>, so nothing exercised
    /// <c>DrawModel</c>'s own texture binding — which bound four slots of five and left the
    /// reflection to whatever the previous draw had left there. That was invisible for as long as
    /// no model material resolved a cubemap, and stopped being invisible the moment one did.
    /// </remarks>
    public void DrawModelPose(
        IReadOnlyList<WorldVertex> vertices,
        IReadOnlyList<WorldBatch> batches,
        float[] camera,
        float[] model,
        MapAssets assets,
        AmbientCube? light = null,
        bool bothSides = false,
        SunLight? sun = null)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(batches);
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(assets);

        const string Posed = "offscreen/posed.mdl";

        _world ??= WorldRenderer.Create(_device);
        _world.Seconds = Seconds;

        _world.UploadTextures(_device, _context, assets);
        _world.UploadModels(
            _device,
            vertices,
            new Dictionary<string, IReadOnlyList<IReadOnlyList<WorldBatch>>>(StringComparer.Ordinal)
            {
                [Posed] = new[] { batches },
            });

        _world.SetCamera(_device, _context, camera);

        Viewport viewport = new(0f, 0f, _width, _height, 0f, 1f);

        _context.RSSetViewports(1, in viewport);
        _context.ClearDepthStencilView(_depthView, (uint)ClearFlag.Depth, 1f, 0);
        _context.OMSetRenderTargets(1u, _view.GetAddressOf(), _depthView);

        _world.DrawModel(
            _context, model, _world.ModelBatches(Posed), light, sun, bothSides: bothSides);
    }

    /// <summary>
    /// Saves what has been drawn as a PNG, so a person can look at what a test verified.
    /// </summary>
    /// <param name="path">Where to write it; the folder is created if needed.</param>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty.</exception>
    /// <remarks>
    /// **A test that renders can leave the picture behind, and it should.** Every assertion here
    /// reduces an image to a number - "this pixel is not black" - and the number is what fails,
    /// while the image is what explains. Writing both costs one file and turns a red test into
    /// something anyone can diagnose without rebuilding.
    ///
    /// It also answers the thing this project kept lacking: a way to see the renderer's output
    /// without a screen, a window, or a working screenshot tool.
    /// </remarks>
    public void SavePng(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string? folder = Path.GetDirectoryName(Path.GetFullPath(path));

        if (folder is not null)
        {
            Directory.CreateDirectory(folder);
        }

        using Bitmap bitmap = new(_width, _height, PixelFormat.Format32bppArgb);

        for (int y = 0; y < _height; y++)
        {
            for (int x = 0; x < _width; x++)
            {
                (int red, int green, int blue) = PixelAt(x, y);

                bitmap.SetPixel(x, y, Color.FromArgb(255, red, green, blue));
            }
        }

        bitmap.Save(path, ImageFormat.Png);
    }

    /// <summary>Fills the target with one colour.</summary>
    /// <param name="red">Red channel, 0 to 1.</param>
    /// <param name="green">Green channel, 0 to 1.</param>
    /// <param name="blue">Blue channel, 0 to 1.</param>
    public void Clear(float red, float green, float blue)
    {
        Span<float> colour = [red, green, blue, 1f];

        fixed (float* first = colour)
        {
            _context.ClearRenderTargetView(_view, first);
        }
    }

    /// <summary>Draws points into the target.</summary>
    /// <param name="points">Points in normalised device coordinates.</param>
    public void Draw(IReadOnlyList<ScenePoint> points)
    {
        _points ??= PointRenderer.Create(_device);

        Viewport viewport = new(0f, 0f, _width, _height, 0f, 1f);
        _context.RSSetViewports(1, in viewport);
        _context.OMSetRenderTargets(1, ref _view, ref Unsafe.NullRef<ID3D11DepthStencilView>());

        _points.Draw(_device, _context, points);
    }

    /// <summary>Draws line segments into the target.</summary>
    /// <param name="segments">Segments in normalised device coordinates.</param>
    /// <param name="red">Line colour, red channel.</param>
    /// <param name="green">Line colour, green channel.</param>
    /// <param name="blue">Line colour, blue channel.</param>
    public void DrawLines(
        IReadOnlyList<((float X, float Y) From, (float X, float Y) To)> segments,
        float red = 1f,
        float green = 1f,
        float blue = 1f)
    {
        _points ??= PointRenderer.Create(_device);

        Viewport viewport = new(0f, 0f, _width, _height, 0f, 1f);
        _context.RSSetViewports(1, in viewport);
        _context.OMSetRenderTargets(1, ref _view, ref Unsafe.NullRef<ID3D11DepthStencilView>());

        _points.DrawLines(_device, _context, segments, red, green, blue);
    }

    /// <summary>Reads one pixel back from the GPU.</summary>
    /// <param name="x">Column, from the left.</param>
    /// <param name="y">Row, from the TOP — image order, not clip space.</param>
    /// <returns>The red, green and blue bytes.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The coordinates are outside the target.</exception>
    public (int Red, int Green, int Blue) PixelAt(int x, int y)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(x, _width);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, _height);

        // A render target cannot be read by the CPU directly; it is copied into a staging
        // resource, which exists for exactly this.
        _context.CopyResource(_staging, _texture);

        MappedSubresource mapped = default;
        SilkMarshal.ThrowHResult(_context.Map(_staging, 0, Map.Read, 0, ref mapped));

        try
        {
            // RowPitch, not width * 4: the driver pads rows to its own alignment, and assuming
            // otherwise reads the wrong pixel on every row but the first.
            byte* row = (byte*)mapped.PData + ((uint)y * mapped.RowPitch);
            byte* pixel = row + (x * 4);

            // The target is B8G8R8A8, so the bytes arrive blue first.
            return (pixel[2], pixel[1], pixel[0]);
        }
        finally
        {
            _context.Unmap(_staging, 0);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _points?.Dispose();
        _world?.Dispose();
        _view.Dispose();
        _depthView.Dispose();
        _depthTexture.Dispose();
        _staging.Dispose();
        _texture.Dispose();
        _context.Dispose();
        _device.Dispose();
        _d3d.Dispose();
    }

    private static OffscreenTarget Build(
        D3D11 d3d,
        int width,
        int height,
        ComPtr<ID3D11Device> device,
        ComPtr<ID3D11DeviceContext> context)
    {
        Texture2DDesc description = new()
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.FormatB8G8R8A8Unorm,
            SampleDesc = new SampleDesc(1, 0),
            Usage = Usage.Default,
            BindFlags = (uint)BindFlag.RenderTarget,
        };

        ComPtr<ID3D11Texture2D> texture = default;
        SilkMarshal.ThrowHResult(device.CreateTexture2D(
            in description, ref Unsafe.NullRef<SubresourceData>(), ref texture));

        Texture2DDesc stagingDescription = description with
        {
            Usage = Usage.Staging,
            BindFlags = 0,
            CPUAccessFlags = (uint)CpuAccessFlag.Read,
        };

        ComPtr<ID3D11Texture2D> staging = default;
        SilkMarshal.ThrowHResult(device.CreateTexture2D(
            in stagingDescription, ref Unsafe.NullRef<SubresourceData>(), ref staging));

        ComPtr<ID3D11RenderTargetView> view = default;
        SilkMarshal.ThrowHResult(device.CreateRenderTargetView(
            texture, ref Unsafe.NullRef<RenderTargetViewDesc>(), ref view));

        // **A depth buffer, because without one this target was not drawing the same picture.**
        // The window has one; this did not, so every draw simply overwrote what came before in
        // material-batch order. A dark surface batched late painted over a tree batched early, and
        // the result was black blobs sitting on top of foliage - in the TEST's pictures only. Those
        // pictures were then read as evidence about the viewer, which does not have the problem.
        // **The same format as the window's, and that matters more than it looks (D48).** A depth
        // bias is scaled by a factor the FORMAT decides, so an offscreen capture on a float buffer
        // and a window on a fixed-point one would place decals differently — and the capture is
        // what tests photograph. A picture that disagrees with the viewer for a reason nobody can
        // see is worse than no picture, which is the same trap the comment above records.
        Texture2DDesc depthDescription = description with
        {
            Format = Format.FormatD24UnormS8Uint,
            BindFlags = (uint)BindFlag.DepthStencil,
        };

        ComPtr<ID3D11Texture2D> depthTexture = default;
        SilkMarshal.ThrowHResult(device.CreateTexture2D(
            in depthDescription, ref Unsafe.NullRef<SubresourceData>(), ref depthTexture));

        ComPtr<ID3D11DepthStencilView> depthView = default;
        SilkMarshal.ThrowHResult(device.CreateDepthStencilView(
            depthTexture, ref Unsafe.NullRef<DepthStencilViewDesc>(), ref depthView));

        return new OffscreenTarget(
            d3d, width, height, device, context, texture, staging, view, depthTexture, depthView);
    }
}
