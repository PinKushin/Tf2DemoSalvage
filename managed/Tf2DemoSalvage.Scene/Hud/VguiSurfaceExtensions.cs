using System;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>`IMatSystemSurface`'s helpers, which are compositions of <see cref="IVguiSurface"/> calls.</summary>
public static class VguiSurfaceExtensions
{
    /// <summary>`DrawColoredText` (vguimatsurface.dll 0x1800098e0): pen, colour, font, then `DrawPrintText`.</summary>
    /// <param name="surface">The surface.</param>
    /// <param name="font">The font handle.</param>
    /// <param name="x">X, relative to the current panel.</param>
    /// <param name="y">Y.</param>
    /// <param name="color">The colour.</param>
    /// <param name="text">The formatted text.</param>
    public static void DrawColoredText(this IVguiSurface surface, VguiFontAmalgam font, int x, int y, (byte Red, byte Green, byte Blue, byte Alpha) color, string text)
    {
        ArgumentNullException.ThrowIfNull(surface);

        surface.DrawSetTextPos(x, y);
        surface.DrawSetTextColor(color);
        surface.DrawSetTextFont(font);
        surface.DrawPrintText(text);
    }
}
