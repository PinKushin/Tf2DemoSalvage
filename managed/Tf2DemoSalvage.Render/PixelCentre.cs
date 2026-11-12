using System;
using Silk.NET.Direct3D11;

namespace Tf2DemoSalvage.Render;

/// <summary>
/// Where TF2's rasteriser puts a pixel's centre: on its integer coordinate, as Direct3D 9 does, half a pixel from
/// Direct3D 11's (B686).
/// </summary>
/// <remarks>
/// **Read:** shaderapidx9 passes the viewport to <c>IDirect3DDevice9::SetViewport</c> as integers and unchanged
/// (<c>FUN_180015f10</c>, the only call through vtable <c>+0x178</c> in TF2 x64's <c>shaderapidx9.dll</c>), and nothing
/// moves a 3D vertex to compensate. DXVK reproduces D3D9 by offsetting every viewport by exactly that half pixel
/// (<c>D3D9DeviceEx::BindViewportAndScissor</c>, <c>cf = 0.5f</c>, dxvk <c>a248e216</c>); Valve's GL layer does it in clip
/// space (<c>cglmprogram.h:240-242</c>). So a 3D view's viewport is offset here the same way, and a draw placed in screen
/// space, which Source places for D3D9 by hand (<c>DrawScreenSpaceRectangle</c>'s <c>dest - 0.5</c>,
/// <see cref="ScreenRectangle"/>), keeps the unshifted one.
/// </remarks>
internal static class PixelCentre
{
    /// <summary>How far D3D9's pixel centre sits from Direct3D 11's, in pixels.</summary>
    public const float Offset = 0.5f;

    /// <summary>A 3D view's viewport: the rectangle the engine would give D3D9, rasterised as D3D9 does.</summary>
    /// <param name="x">The left edge, in pixels.</param>
    /// <param name="y">The top edge.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <param name="minDepth">The depth range's near end.</param>
    /// <param name="maxDepth">Its far end.</param>
    /// <returns>The viewport, offset half a pixel right and down.</returns>
    public static Viewport Scene(float x, float y, float width, float height, float minDepth = 0f, float maxDepth = 1f) =>
        new(x + Offset, y + Offset, width, height, minDepth, maxDepth);

    /// <summary>The same rectangle for a draw placed in screen space, whatever offset the bound one carries.</summary>
    /// <param name="bound">The viewport bound, a 3D view's or not.</param>
    /// <returns>It on whole pixels.</returns>
    public static Viewport Screen(Viewport bound) =>
        bound with { TopLeftX = MathF.Floor(bound.TopLeftX), TopLeftY = MathF.Floor(bound.TopLeftY) };
}
