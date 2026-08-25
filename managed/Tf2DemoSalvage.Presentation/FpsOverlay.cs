using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Render;

namespace Tf2DemoSalvage.Presentation;

/// <summary>The frame-rate readout, composed into quads a renderer can draw.</summary>
/// <remarks>
/// **Not a HUD element, and Valve is the reason the distinction is worth keeping.** Source's frame
/// rate meter is <c>CFPSPanel : vgui::Panel</c> (<c>vgui_fpspanel.cpp:38</c>), created on
/// <c>PANEL_TOOLS</c> beside the net graph — <c>fps-&gt;Create( toolParent )</c>,
/// <c>vgui_int.cpp:209</c> — rather than a <c>CHudElement</c> on the HUD. This lived in
/// <c>MainForm</c> as <c>BuildHud</c>, which would have had to become two things the moment a real
/// HUD element existed.
///
/// The <c>HudQuad</c>/<c>HudRenderer</c> names in <c>Render</c> are a screen-space LAYER — the thing
/// both a meter and a future HUD draw through — and are correct as they stand.
///
/// **The atlas is handed in rather than built here**, because rasterising a font is the one part of
/// this that is genuinely platform work: ours is GDI and a Linux port swaps it for FreeType (D84,
/// D90). Everything else — the mode, the sampling, the text, Valve's placement — is presentation and
/// needs no window.
/// </remarks>
public sealed class FpsOverlay
{
    /// <summary>The meter itself, which owns the smoothing and the watermarks.</summary>
    public FpsMeter Meter { get; } = new();

    /// <summary>Which readout to show: hidden, instantaneous or smoothed.</summary>
    /// <remarks>
    /// **Assigned every frame rather than on a change event**, because the setter is what notices a
    /// transition into being shown and resets the watermarks — and assigning the same value is a
    /// no-op for that check. One place, so <c>cl_showfps</c> from a config, from a launch option and
    /// from the menu all arrive the same way.
    /// </remarks>
    public int Mode
    {
        get => Meter.Mode;
        set => Meter.Mode = value;
    }

    /// <summary>Whether a glyph atlas is worth rasterising this frame.</summary>
    /// <remarks>
    /// Asked by the frontend, which owns the rasteriser. Building an atlas for a meter nobody has
    /// switched on is a font rasterisation per character for nothing.
    /// </remarks>
    public bool NeedsAtlas => Mode != FpsMeter.Hidden;

    /// <summary>Composes this frame's readout.</summary>
    /// <param name="atlas">The glyphs, or null before the frontend has rasterised any.</param>
    /// <param name="viewportWidth">How wide the drawing surface is, in pixels.</param>
    /// <param name="mapName">The open map, without its extension, or null when none is.</param>
    /// <param name="lastFrameSeconds">How long the previous frame took.</param>
    /// <returns>Quads in screen pixels; empty when there is nothing to say.</returns>
    /// <remarks>
    /// **The meter is sampled every frame whatever its mode**, because <see cref="FpsMeter"/> owns
    /// the "first frame after being shown draws nothing" rule and the watermarks that go with it.
    /// Sampling only while visible would hand it a frame duration covering however long it was off.
    ///
    /// **Top right, as <c>CFPSPanel::ComputeSize</c> places it**: <c>x = wide - FPS_PANEL_WIDTH</c>
    /// with the text at panel-local (2, 2), so two pixels in from a panel 300 wide. Reproduced as a
    /// right edge rather than a fixed 300-pixel panel, because ours has no panel to size — the
    /// effect is the same for any line shorter than 300 and better for a longer one, which would
    /// otherwise run off Valve's panel.
    /// </remarks>
    public IReadOnlyList<HudQuad> Quads(
        GlyphAtlas? atlas, int viewportWidth, string? mapName, double lastFrameSeconds)
    {
        FpsReading? reading = Meter.Sample(lastFrameSeconds);

        if (reading is not { } meter || atlas is null)
        {
            return [];
        }

        // `V_GetFileName( engine->GetLevelName() )` keeps the extension, so TF2 shows
        // `cp_process_f12.bsp`. Ours is stored without one, so it is put back rather than the line
        // quietly differing from the game's.
        string map = mapName is { Length: > 0 } named ? named + ".bsp" : NoMap;

        (byte Red, byte Green, byte Blue) colour = meter.Colour;

        // **Clamped at zero, which Valve's panel never has to be.** `wide - 300` is negative on a
        // window narrower than the panel, and VGUI's is the size of the screen. Ours is a control,
        // so a small window would otherwise put the whole readout off the left edge.
        int right = Math.Max(0, viewportWidth - PanelWidth) + Margin;

        return HudText.Quads(
            atlas, meter.Text(map), right, Margin, colour.Red, colour.Green, colour.Blue);
    }

    /// <summary>Valve's panel-local text inset, in pixels.</summary>
    private const int Margin = 2;

    /// <summary><c>FPS_PANEL_WIDTH</c>, the width the readout is right-aligned against.</summary>
    private const int PanelWidth = 300;

    /// <summary>What the line says in place of a map name when none is open.</summary>
    private const string NoMap = "no map";
}
