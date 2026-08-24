using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Render;

/// <summary>Turns a laid-out string into quads the HUD can draw.</summary>
/// <remarks>
/// **The join between the portable half and the device half** (D84). `TextLayout` decides where each
/// glyph goes and knows nothing about Direct3D; <see cref="HudRenderer"/> draws rectangles and knows
/// nothing about text. This is the twenty lines between them, and it lives here because `Render`
/// already references `Content` while nothing references `Render` but the shell.
///
/// **Not a method on the renderer**, so a caller can build a whole frame's worth of HUD — several
/// lines, several colours — and hand it over as one list for one draw call. VGUI does the same
/// thing: `DrawColoredText` accumulates into the surface's batch rather than issuing a draw per
/// string.
/// </remarks>
public static class HudText
{
    /// <summary>Lays a string out and returns its quads.</summary>
    /// <param name="atlas">The font's atlas.</param>
    /// <param name="text">The string to draw.</param>
    /// <param name="x">Left edge of the line, in screen pixels.</param>
    /// <param name="y">Top edge of the line, in screen pixels.</param>
    /// <param name="red">Tint, red channel.</param>
    /// <param name="green">Tint, green channel.</param>
    /// <param name="blue">Tint, blue channel.</param>
    /// <param name="alpha">Opacity.</param>
    /// <returns>One quad per glyph that the atlas carries.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **A glyph with no ink still produces no quad**, because the atlas gives a space a zero-sized
    /// rectangle and drawing it would be a degenerate triangle pair. The pen still advances, which
    /// is <see cref="TextLayout"/>'s business rather than this method's.
    /// </remarks>
    public static IReadOnlyList<HudQuad> Quads(
        GlyphAtlas atlas,
        string text,
        int x,
        int y,
        byte red,
        byte green,
        byte blue,
        byte alpha = 255)
    {
        ArgumentNullException.ThrowIfNull(atlas);
        ArgumentNullException.ThrowIfNull(text);

        List<HudQuad> quads = [];

        foreach (PlacedGlyph placed in TextLayout.Place(atlas, text, x, y))
        {
            if (placed.Glyph.Width <= 0 || placed.Glyph.Height <= 0)
            {
                continue;
            }

            quads.Add(new HudQuad(
                placed.X,
                placed.Y,
                placed.Glyph.Width,
                placed.Glyph.Height,
                placed.Glyph.X,
                placed.Glyph.Y,
                red,
                green,
                blue,
                alpha));
        }

        return quads;
    }
}
