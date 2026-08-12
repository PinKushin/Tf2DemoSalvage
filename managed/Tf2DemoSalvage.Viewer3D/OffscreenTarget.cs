using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;

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
    private ComPtr<ID3D11RenderTargetView> _view;
    private PointRenderer? _points;

    private OffscreenTarget(
        D3D11 d3d,
        int width,
        int height,
        ComPtr<ID3D11Device> device,
        ComPtr<ID3D11DeviceContext> context,
        ComPtr<ID3D11Texture2D> texture,
        ComPtr<ID3D11Texture2D> staging,
        ComPtr<ID3D11RenderTargetView> view)
    {
        _d3d = d3d;
        _width = width;
        _height = height;
        _device = device;
        _context = context;
        _texture = texture;
        _staging = staging;
        _view = view;
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
        _view.Dispose();
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

        return new OffscreenTarget(d3d, width, height, device, context, texture, staging, view);
    }
}
