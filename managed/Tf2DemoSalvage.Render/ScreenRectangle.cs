namespace Tf2DemoSalvage.Render;

/// <summary>
/// The texture coordinates <c>DrawScreenSpaceRectangle</c> gives a rectangle's edges, which a full-screen draw of ours
/// interpolates between.
/// </summary>
/// <remarks>
/// **Read in disassembly** of TF2's <c>materialsystem.dll</c> (2026-11-10), tier2's <c>renderutils.cpp</c> linked in at
/// <c>0x18007e980</c>. The arguments say which texel shows at the first and at the last pixel
/// (<c>imaterialsystem.h:1436-1447</c>); the body widens that by half a texel step on each side so the quad's edges, at
/// <c>dest - 0.5</c> and <c>dest + size - 0.5</c>, carry
/// <c>u = ( x0 + 0.5 - step / 2 ) / texWidth</c> and <c>( x1 + 0.5 + step / 2 ) / texWidth</c>, with
/// <c>step = ( x1 - x0 ) / ( width - 1 )</c>, and 0 when the width is one pixel. The same down the screen.
/// </remarks>
internal static class ScreenRectangle
{
    /// <summary>The coordinates at the rectangle's left, top, right and bottom edges.</summary>
    /// <param name="width">The rectangle's width in pixels.</param>
    /// <param name="height">Its height in pixels.</param>
    /// <param name="x0">The texel at the first column.</param>
    /// <param name="y0">The texel at the first row.</param>
    /// <param name="x1">The texel at the last column.</param>
    /// <param name="y1">The texel at the last row.</param>
    /// <param name="textureWidth">The width the texels are counted against.</param>
    /// <param name="textureHeight">The height.</param>
    /// <returns>The edges' coordinates.</returns>
    public static (float Left, float Top, float Right, float Bottom) Edges(
        int width, int height, float x0, float y0, float x1, float y1, int textureWidth, int textureHeight)
    {
        float stepX = width < 2 ? 0f : (x1 - x0) / (width - 1);
        float stepY = height < 2 ? 0f : (y1 - y0) / (height - 1);

        return (
            (x0 + 0.5f - (stepX * 0.5f)) * (1f / textureWidth),
            (y0 + 0.5f - (stepY * 0.5f)) * (1f / textureHeight),
            (x1 + 0.5f + (stepX * 0.5f)) * (1f / textureWidth),
            (y1 + 0.5f + (stepY * 0.5f)) * (1f / textureHeight));
    }
}
