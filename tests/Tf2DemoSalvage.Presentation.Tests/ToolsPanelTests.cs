using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Render;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>
/// Composing the frame-rate readout: what it says, where it sits, and when it says nothing.
/// </summary>
/// <remarks>
/// **This was <c>MainForm.BuildHud</c>, and the name was the smaller of two problems.** Everything
/// it did was presentation — the meter's mode from settings, the map name in the line, Valve's panel
/// geometry, the quads — and the only thing that needed a window was the viewport's width in pixels
/// (B188, D90).
///
/// **The name is fixed too, and Valve's own arrangement is why.** The frame-rate meter is NOT a HUD
/// element in Source: it is <c>CFPSPanel : vgui::Panel</c> (<c>vgui_fpspanel.cpp:38</c>), created on
/// <c>PANEL_TOOLS</c> beside the net graph — <c>fps-&gt;Create( toolParent )</c>,
/// <c>vgui_int.cpp:209</c> — while the HUD panels go elsewhere. A method called <c>BuildHud</c> that
/// returns the fps meter would have had to become two things the moment a real HUD element existed.
/// The <c>HudQuad</c>/<c>HudRenderer</c> names in the Render project are a screen-space LAYER and
/// stay as they are.
///
/// The atlas is built by the frontend and handed in, because rasterising a font is the one part of
/// this that is genuinely platform work: ours is GDI, and a Linux port swaps it for FreeType (D90).
/// </remarks>
public sealed class ToolsPanelTests
{
    [Test]
    public void Quads_WhileTheMeterIsHidden_AreEmpty()
    {
        // `cl_showfps 0` is the ordinary state, so this is most frames of most runs.
        ToolsPanel overlay = new() { Mode = FpsMeter.Hidden };

        overlay.Quads(Atlas(), ViewportWidth, "cp_process_f12", OneSixtieth).ShouldBeEmpty();
    }

    [Test]
    public void Quads_WhileTheMeterIsShown_AreNotEmpty()
    {
        // **The control for the case above**, without which "hidden draws nothing" cannot be told
        // apart from "nothing ever draws". Two frames, because the meter deliberately draws nothing
        // on the first one after being shown — it has no frame duration to report yet.
        ToolsPanel overlay = new() { Mode = FpsMeter.Instantaneous };

        overlay.Quads(Atlas(), ViewportWidth, "cp_process_f12", OneSixtieth);

        overlay.Quads(Atlas(), ViewportWidth, "cp_process_f12", OneSixtieth).ShouldNotBeEmpty();
    }

    [Test]
    public void Quads_WithNoAtlasYet_AreEmptyRatherThanThrowing()
    {
        // The frame between switching the meter on and the atlas being rasterised. The frontend
        // builds that atlas, so a viewer whose font rasteriser failed keeps drawing the world.
        ToolsPanel overlay = new() { Mode = FpsMeter.Instantaneous };

        overlay.Quads(null, ViewportWidth, "cp_process_f12", OneSixtieth);

        Should.NotThrow(() => overlay.Quads(null, ViewportWidth, "cp_process_f12", OneSixtieth));
    }

    [Test]
    public void Quads_OnAWideViewport_SitFurtherRightThanOnANarrowOne()
    {
        // **Valve's placement, and it is a RIGHT edge rather than a fixed panel.**
        // `CFPSPanel::ComputeSize` puts the panel at `x = wide - FPS_PANEL_WIDTH` with its text at
        // panel-local (2, 2). Two widths, because one cannot tell "tracks the viewport" from
        // "always at some fixed x" — which is what a left-anchored readout would look like.
        ToolsPanel wide = Shown();
        ToolsPanel narrow = Shown();

        float wideLeft = LeftmostX(wide.Quads(Atlas(), 2560, "cp_process_f12", OneSixtieth));
        float narrowLeft = LeftmostX(narrow.Quads(Atlas(), 1280, "cp_process_f12", OneSixtieth));

        wideLeft.ShouldBeGreaterThan(narrowLeft);
    }

    [Test]
    public void Quads_OnAViewportNarrowerThanValvesPanel_StayOnScreen()
    {
        // `wide - 300` is negative on a small window, which would put the whole readout off the
        // left edge. Valve's panel cannot be narrower than the screen it sits on; ours can, because
        // the viewport is a control rather than the display.
        LeftmostX(Shown().Quads(Atlas(), 200, "cp_process_f12", OneSixtieth))
            .ShouldBeGreaterThanOrEqualTo(0f);
    }

    [Test]
    public void Quads_WithNoMapOpen_StillDraw()
    {
        // The meter is switched on before a demo is loaded, and a readout that vanished then would
        // read as the meter being broken.
        Shown().Quads(Atlas(), ViewportWidth, null, OneSixtieth).ShouldNotBeEmpty();
    }

    [Test]
    public void Mode_SetToTheSameValueEveryFrame_DoesNotResetTheWatermarks()
    {
        // **Assigned every frame on purpose**, because the setter is what notices a transition into
        // being shown and resets the low/high watermarks. Assigning the same value must be a no-op
        // for that check, or the watermarks are cleared sixty times a second and the meter can
        // never report a spread.
        //
        // **Smoothed, and the observable is the SPREAD.** The first version asserted on `Average`
        // in instantaneous mode, where Valve sets `m_AverageFPS = -1` every paint — a proxy that
        // cannot respond to the manipulation, so it would have failed against correct code. The
        // watermarks bracket the instantaneous rate across frames, so alternating two frame
        // durations gives a low below a high only if they survive the assignment.
        ToolsPanel overlay = new() { Mode = FpsMeter.Smoothed };

        for (int frame = 0; frame < 8; frame++)
        {
            overlay.Mode = FpsMeter.Smoothed;
            overlay.Quads(Atlas(), ViewportWidth, "cp_process_f12", frame % 2 == 0 ? Fast : Slow);
        }

        FpsReading reading = overlay.Meter.Sample(Fast).ShouldNotBeNull();

        reading.Low.ShouldBeLessThan(reading.High);
    }

    [Test]
    public void Quads_WithOnlyThePositionShown_AreDrawnEvenThoughTheMeterIsOff()
    {
        // **`CFPSPanel::ShouldDraw` returns true when EITHER convar is on**, and this panel used to
        // return early the moment the meter had no reading. So `cl_showpos 1` with `cl_showfps 0`
        // — which is precisely how the owner asked to use it, as an instrument on an otherwise
        // clean screenshot — would have drawn nothing at all.
        ToolsPanel panel = new()
        {
            Mode = FpsMeter.Hidden,
            Position = new PositionReadout(
                PositionReadout.View, (1f, 2f, 3f), default, default, default, 0f),
        };

        panel.Quads(Atlas(), ViewportWidth, "cp_process_f12", OneSixtieth).ShouldNotBeEmpty();
    }

    [Test]
    public void Quads_WithBothShown_PutThePositionBelowTheFrameRate()
    {
        // **Valve walks ONE line counter across both readouts** — `int i = 0` in `Paint`,
        // incremented past the frame-rate line before the position lines are placed at
        // `2 + i * ( GetFontTall + 2 )`. So the position starts on the line after the meter, and
        // an implementation that composed the two independently would draw them on top of each
        // other.
        ToolsPanel panel = Shown();

        panel.Position = new PositionReadout(
            PositionReadout.View, (1f, 2f, 3f), default, default, default, 0f);

        IReadOnlyList<HudQuad> quads =
            panel.Quads(Atlas(), ViewportWidth, "cp_process_f12", OneSixtieth);

        // Four distinct rows: the frame rate, then pos, ang and vel.
        quads.Select(quad => quad.Y).Distinct().Count().ShouldBe(
            4, "the meter's line and the readout's three should each sit on their own row");
    }

    [Test]
    public void Quads_WithOnlyThePositionShown_StartOnTheTopLine()
    {
        // **The control for the stacking, and it is what makes the count above mean something.**
        // With the meter off the counter has not been incremented, so the position starts at the
        // top — an implementation that reserved a row for a meter nobody switched on would leave a
        // gap, and the test above cannot see the difference.
        ToolsPanel alone = new() { Mode = FpsMeter.Hidden, Position = Somewhere };

        IReadOnlyList<HudQuad> quads =
            alone.Quads(Atlas(), ViewportWidth, "cp_process_f12", OneSixtieth);

        // **Asserted as a ROW COUNT and a top edge, which the first draft did not do.** It compared
        // the lowest quad of this case against the meter case and expected "three lines end higher
        // than four" — and that survived a sabotage which put all three position lines on ONE row,
        // because three-on-one-row still ends higher than four-on-two. A comparison between two
        // cases is not a measurement of either.
        quads.Select(quad => quad.Y).Distinct().Count().ShouldBe(
            3, "pos, ang and vel each get their own row");

        quads.Min(quad => quad.Y).ShouldBe(
            Shown().Quads(Atlas(), ViewportWidth, "cp_process_f12", OneSixtieth).Min(q => q.Y),
            "with no meter drawn the readout starts on the panel's own top line, not below a gap");
    }

    /// <summary>A visible readout, for tests about placement rather than about content.</summary>
    private static PositionReadout Somewhere => new(
        PositionReadout.View, (1802f, -679f, 373f), (0f, 90f, 0f), default, default, 0f);

    /// <summary>A meter already past its first frame, so it has something to report.</summary>
    private static ToolsPanel Shown()
    {
        ToolsPanel overlay = new() { Mode = FpsMeter.Instantaneous };

        overlay.Quads(Atlas(), ViewportWidth, "cp_process_f12", OneSixtieth);

        return overlay;
    }

    /// <summary>The x of the leftmost quad, which is where the readout starts.</summary>
    private static float LeftmostX(IReadOnlyList<HudQuad> quads)
    {
        quads.ShouldNotBeEmpty("a placement assertion over no quads measures nothing");

        return quads.Min(quad => quad.X);
    }

    private const int ViewportWidth = 1920;

    private const double OneSixtieth = 1d / 60d;

    /// <summary>A quick frame, for the high watermark.</summary>
    private const double Fast = 1d / 120d;

    /// <summary>A slow one, for the low. Far enough apart that the two cannot round together.</summary>
    private const double Slow = 1d / 30d;

    /// <summary>An atlas covering the digits and letters the readout uses.</summary>
    private static GlyphAtlas Atlas() => GlyphAtlas.Build(new BlockRasteriser(), Font, Characters);

    private static readonly SchemeFont Font = new() { Name = "Tahoma", Tall = 12 };

    private const string Characters =
        "0123456789 fpsminaxABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_.:/";

    /// <summary>
    /// A rasteriser that gives every glyph the same solid block, so no font is involved.
    /// </summary>
    /// <remarks>
    /// **The real one is GDI and therefore Windows** — the whole reason the atlas is an argument
    /// rather than something this type builds. What is being measured here is placement and
    /// presence, and a block answers both without pinning the test to a platform or to whichever
    /// fonts a machine happens to have.
    /// </remarks>
    private sealed class BlockRasteriser : IGlyphRasteriser
    {
        private const int Side = 6;

        public int LineHeight(SchemeFont font) => Side;

        public RasterisedGlyph Rasterise(SchemeFont font, char character) =>
            new()
            {
                // RGBA, four bytes a pixel — opaque white, so a quad is produced for every
                // character rather than the atlas treating it as inkless.
                Pixels = Enumerable.Repeat((byte)0xFF, Side * Side * 4).ToArray(),
                Metrics = new GlyphMetrics(Side, Side, LeftBearing: 0, TopBearing: 0, Advance: Side),
            };
    }
}
