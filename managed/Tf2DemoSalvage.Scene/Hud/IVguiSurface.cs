namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>The part of `vgui::ISurface` a panel draws through.</summary>
/// <remarks>
/// Coordinates are relative to the panel made current, as `ISurface`'s are: `PushMakeCurrent( panel, useInset )` sets the
/// origin to the panel's absolute position (plus its inset when asked) and the clip to its clip rectangle. The alpha
/// multiplier scales every colour's alpha. The adapter that draws — and clips — is the renderer's; this is the portable half.
/// </remarks>
public interface IVguiSurface
{
    /// <summary>`DrawGetAlphaMultiplier` / `DrawSetAlphaMultiplier`.</summary>
    public float AlphaMultiplier { get; set; }

    /// <summary>`PushMakeCurrent`.</summary>
    /// <param name="panel">The panel to draw in.</param>
    /// <param name="useInset">Whether the origin moves in by the panel's left and top inset.</param>
    public void PushMakeCurrent(VguiPanel panel, bool useInset);

    /// <summary>`PopMakeCurrent`.</summary>
    /// <param name="panel">The panel pushed.</param>
    public void PopMakeCurrent(VguiPanel panel);

    /// <summary>`DrawSetColor`.</summary>
    /// <param name="color">The colour.</param>
    public void DrawSetColor((byte Red, byte Green, byte Blue, byte Alpha) color);

    /// <summary>`DrawFilledRect`, far edges exclusive.</summary>
    /// <param name="x0">Left.</param>
    /// <param name="y0">Top.</param>
    /// <param name="x1">Right.</param>
    /// <param name="y1">Bottom.</param>
    public void DrawFilledRect(int x0, int y0, int x1, int y1);

    /// <summary>`DrawFilledRectFade`: the colour's alpha scaled from <paramref name="alpha0"/> to <paramref name="alpha1"/> / 255.</summary>
    /// <param name="x0">Left.</param>
    /// <param name="y0">Top.</param>
    /// <param name="x1">Right.</param>
    /// <param name="y1">Bottom.</param>
    /// <param name="alpha0">Alpha at the start, 0 to 255.</param>
    /// <param name="alpha1">Alpha at the end, 0 to 255.</param>
    /// <param name="horizontal">Whether the fade runs left to right rather than top to bottom.</param>
    public void DrawFilledRectFade(int x0, int y0, int x1, int y1, int alpha0, int alpha1, bool horizontal);

    /// <summary>`DrawOutlinedRect`.</summary>
    /// <param name="x0">Left.</param>
    /// <param name="y0">Top.</param>
    /// <param name="x1">Right.</param>
    /// <param name="y1">Bottom.</param>
    public void DrawOutlinedRect(int x0, int y0, int x1, int y1);

    /// <summary>`DrawSetTexture` of the texture `DrawSetTextureFile` named — a material path under `materials/`.</summary>
    /// <param name="texture">The material, such as `vgui/hud/8x800corner1`.</param>
    public void DrawSetTexture(string texture);

    /// <summary>`DrawTexturedRect`: the whole texture.</summary>
    /// <param name="x0">Left.</param>
    /// <param name="y0">Top.</param>
    /// <param name="x1">Right.</param>
    /// <param name="y1">Bottom.</param>
    public void DrawTexturedRect(int x0, int y0, int x1, int y1);

    /// <summary>`DrawTexturedSubRect`: part of the texture.</summary>
    /// <param name="x0">Left.</param>
    /// <param name="y0">Top.</param>
    /// <param name="x1">Right.</param>
    /// <param name="y1">Bottom.</param>
    /// <param name="s0">Left texture coordinate.</param>
    /// <param name="t0">Top texture coordinate.</param>
    /// <param name="s1">Right texture coordinate.</param>
    /// <param name="t1">Bottom texture coordinate.</param>
    public void DrawTexturedSubRect(int x0, int y0, int x1, int y1, float s0, float t0, float s1, float t1);

    /// <summary>`DrawGetTextureSize`.</summary>
    /// <param name="texture">The material.</param>
    /// <returns>Its size in texels; zero when it did not load.</returns>
    public (int Wide, int Tall) DrawGetTextureSize(string texture);

    /// <summary>`DrawSetTextFont`.</summary>
    /// <param name="font">The font handle.</param>
    public void DrawSetTextFont(VguiFontAmalgam font);

    /// <summary>`DrawSetTextColor`: alpha is scaled by the multiplier when set.</summary>
    /// <param name="color">The colour.</param>
    public void DrawSetTextColor((byte Red, byte Green, byte Blue, byte Alpha) color);

    /// <summary>`DrawSetTextPos`, relative to the panel made current.</summary>
    /// <param name="x">X.</param>
    /// <param name="y">Y.</param>
    public void DrawSetTextPos(int x, int y);

    /// <summary>`DrawGetTextPos`.</summary>
    /// <returns>The pen.</returns>
    public (int X, int Y) DrawGetTextPos();

    /// <summary>`DrawUnicodeChar`.</summary>
    /// <param name="character">The character.</param>
    /// <param name="drawType">`FontDrawType_t`.</param>
    public void DrawUnicodeChar(char character, VguiFontDrawType drawType = VguiFontDrawType.Default);

    /// <summary>`DrawPrintText`.</summary>
    /// <param name="text">The text.</param>
    /// <param name="drawType">`FontDrawType_t`.</param>
    public void DrawPrintText(string text, VguiFontDrawType drawType = VguiFontDrawType.Default);

    /// <summary>`GetFontTall`.</summary>
    /// <param name="font">The handle.</param>
    /// <returns>Its height.</returns>
    public int GetFontTall(VguiFontAmalgam font);

    /// <summary>`GetCharABCwide`.</summary>
    /// <param name="font">The handle.</param>
    /// <param name="character">The character.</param>
    /// <returns>Leading, glyph and trailing widths.</returns>
    public (int A, int B, int C) GetCharAbcWide(VguiFontAmalgam font, char character);

    /// <summary>`GetCharacterWidth`.</summary>
    /// <param name="font">The handle.</param>
    /// <param name="character">The character.</param>
    /// <returns>The advance; 0 for a control character.</returns>
    public int GetCharacterWidth(VguiFontAmalgam font, char character);
}

/// <summary>`FontDrawType_t`.</summary>
public enum VguiFontDrawType
{
    /// <summary>`FONT_DRAW_DEFAULT`: additive when the font is.</summary>
    Default,

    /// <summary>`FONT_DRAW_NONADDITIVE`.</summary>
    NonAdditive,

    /// <summary>`FONT_DRAW_ADDITIVE`.</summary>
    Additive,
}
