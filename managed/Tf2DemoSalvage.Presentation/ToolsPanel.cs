using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Render;

namespace Tf2DemoSalvage.Presentation;

/// <summary>Valve's tools panel — the frame rate and the position readout — composed into quads.</summary>
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
/// **This was <c>FpsOverlay</c> until <c>cl_showpos</c> arrived, and the rename is not tidiness.**
/// One panel draws both readouts in the engine: <c>CFPSPanel::ShouldDraw</c> returns true when
/// EITHER convar is on, and <c>Paint</c> walks a single line counter <c>i</c> across them, so with
/// both switched on the position starts on the line after the frame rate. Splitting them into two
/// objects would have put that shared counter in the caller — which for this project is the Form,
/// and a Form deciding where a readout stacks is exactly the MVP violation D55 names.
///
/// **The atlas is handed in rather than built here**, because rasterising a font is the one part of
/// this that is genuinely platform work: ours is GDI and a Linux port swaps it for FreeType (D84,
/// D90). Everything else — the mode, the sampling, the text, Valve's placement — is presentation and
/// needs no window.
/// </remarks>
public sealed class ToolsPanel
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

    /// <summary>What the position readout should say, or a hidden one.</summary>
    /// <remarks>
    /// **Assigned every frame like <see cref="Mode"/>**, and for the same reason: the camera and the
    /// watched player move, so the readout is a value describing this frame rather than a setting.
    /// A hidden one — <c>Mode</c> zero, which is <c>cl_showpos</c>'s own default — contributes
    /// nothing and is the state a viewer that never switches it on stays in.
    /// </remarks>
    public PositionReadout Position { get; set; }

    /// <summary>Whether a glyph atlas is worth rasterising this frame.</summary>
    /// <remarks>
    /// Asked by the frontend, which owns the rasteriser. Building an atlas for a meter nobody has
    /// switched on is a font rasterisation per character for nothing.
    ///
    /// **Either readout wants it**, which is <c>CFPSPanel::ShouldDraw</c>'s own test:
    /// <c>if ( ( !cl_showfps.GetInt() || ... ) &amp;&amp; ( !cl_showpos.GetInt() ) ) return false;</c>
    /// </remarks>
    public bool NeedsAtlas => Mode != FpsMeter.Hidden || Position.Visible;

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
        // **Sampled unconditionally, before the atlas check.** `FpsMeter` owns the "first frame
        // after being shown draws nothing" rule and the watermarks, and skipping the sample when
        // there is nothing to draw would hand it a frame duration covering however long that was.
        FpsReading? reading = Meter.Sample(lastFrameSeconds);

        if (atlas is null)
        {
            return [];
        }

        List<HudQuad> quads = [];

        // **Clamped at zero, which Valve's panel never has to be.** `wide - 300` is negative on a
        // window narrower than the panel, and VGUI's is the size of the screen. Ours is a control,
        // so a small window would otherwise put the whole readout off the left edge.
        int right = Math.Max(0, viewportWidth - PanelWidth) + Margin;

        // `int i = 0;` in `CFPSPanel::Paint`, incremented past each line that is drawn, with the
        // next at `2 + i * ( GetFontTall( m_hFont ) + 2 )`. One counter across both readouts, so
        // the position starts below the frame rate when both are on.
        int line = 0;

        if (reading is { } meter)
        {
            // `V_GetFileName( engine->GetLevelName() )` keeps the extension, so TF2 shows
            // `cp_process_f12.bsp`. Ours is stored without one, so it is put back rather than the
            // line quietly differing from the game's.
            string map = mapName is { Length: > 0 } named ? named + ".bsp" : NoMap;

            (byte Red, byte Green, byte Blue) colour = meter.Colour;

            quads.AddRange(HudText.Quads(
                atlas,
                meter.Text(map),
                right,
                At(atlas, line++),
                colour.Red,
                colour.Green,
                colour.Blue));
        }

        // **White, flatly.** The frame-rate line is coloured by `GetFPSColor` against a threshold;
        // the position lines are drawn `255, 255, 255, 255` with no condition at all
        // (`vgui_fpspanel.cpp:331`), so nothing here reads a value to pick a colour.
        foreach (string text in Position.Lines)
        {
            quads.AddRange(HudText.Quads(atlas, text, right, At(atlas, line++), 255, 255, 255));
        }

        return quads;
    }

    /// <summary>Where line <paramref name="index"/> sits, as <c>CFPSPanel::Paint</c> stacks them.</summary>
    /// <remarks>
    /// <c>2 + i * ( vgui::surface()-&gt;GetFontTall( m_hFont ) + 2 )</c>. The gap is the same two
    /// pixels as the inset, which is why one constant serves both.
    /// </remarks>
    private static int At(GlyphAtlas atlas, int index) =>
        Margin + (index * (atlas.LineHeight + Margin));

    /// <summary>Valve's panel-local text inset, in pixels.</summary>
    private const int Margin = 2;

    /// <summary><c>FPS_PANEL_WIDTH</c>, the width the readout is right-aligned against.</summary>
    private const int PanelWidth = 300;

    /// <summary>What the line says in place of a map name when none is open.</summary>
    private const string NoMap = "no map";
}
