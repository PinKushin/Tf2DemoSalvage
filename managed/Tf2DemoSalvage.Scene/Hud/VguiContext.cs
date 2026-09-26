using System;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>What `vgui2.dll` answers a panel: its scheme, and the screen it is sized against.</summary>
/// <param name="Scheme">The panel's scheme — colours and base settings.</param>
/// <param name="Borders">The scheme's borders.</param>
/// <param name="Fonts">The scheme's `Fonts` block.</param>
/// <param name="ScreenWide">`ISurface::GetScreenSize` wide.</param>
/// <param name="ScreenTall">`ISurface::GetScreenSize` tall — also the scheme's sizing height, as the HUD's scheme has no sizing panel.</param>
/// <param name="Language">The game's language, for font minimums.</param>
/// <param name="SchemeFonts">The scheme's font handles, or null where nothing draws text.</param>
public sealed record VguiContext(
    VguiScheme Scheme, VguiBorders Borders, KeyValuesTree Fonts, int ScreenWide, int ScreenTall, string Language, VguiSchemeFonts? SchemeFonts = null)
{
    /// <summary>`GetProportionalScaledValueEx`.</summary>
    /// <param name="value">A value authored at 480 tall.</param>
    /// <returns>The value at this screen.</returns>
    public int Scale(int value) => PanelLayout.ProportionalScaled(value, ScreenTall);

    /// <summary>`GetProportionalNormalizedValue`.</summary>
    /// <param name="value">A value at this screen.</param>
    /// <returns>The value at 480 tall.</returns>
    public int Normalize(int value) => PanelLayout.ProportionalNormalized(value, ScreenTall);

    /// <summary>`IScheme::GetFont`.</summary>
    /// <param name="name">The font's name.</param>
    /// <param name="proportional">Whether the proportional handle is asked for.</param>
    /// <returns>The handle, or null — handle 0 — when the scheme has none.</returns>
    public VguiFontAmalgam? GetFont(string name, bool proportional) => SchemeFonts?.GetFont(name, proportional);

    /// <summary>`vgui::surface()` as a panel measures with it outside `Paint` — text sizes in `ApplySchemeSettings` and `PerformLayout`.</summary>
    public IVguiSurface? Surface { get; init; }

    /// <summary>`g_pVGuiLocalize->Find` without the `#`, or null where nothing is localised.</summary>
    public Func<string, string?>? Localize { get; init; }

    /// <summary>The filesystem a panel's own `LoadControlSettings( "resource/UI/…" )` reads, or null where nothing does.</summary>
    public Func<string, byte[]?>? Read { get; init; }
}
