using System;
using System.Drawing;
using System.IO;
using System.Linq;

using FlaUI.Core.Tools;

namespace Tf2DemoSalvage.Viewer3D.UiTests;

/// <summary>
/// <c>cl_showpos</c> reaches the screen when the menu asks for it.
/// </summary>
/// <remarks>
/// **The third test level, and only it can fail when the wiring is absent**
/// (<c>docs/memory/three-test-levels-and-the-third-is-missing.md</c>). `PositionReadoutConformance
/// Tests` proves the three lines say what the engine says, and `ToolsPanelTests` proves they stack
/// below the frame rate. Neither can tell whether the menu item reaches the settings, whether the
/// settings reach `ReadPosition`, whether that reaches the panel, or whether the panel's quads
/// reach the frame — six links, every one of which is the kind that has shipped silently here
/// before.
///
/// **Asserted on the PICTURE as well as on the log**, and the pair is the point. The log line
/// proves the menu reached the form; only the capture proves anything was drawn. A readout that was
/// composed perfectly and never submitted would satisfy the first assertion completely.
///
/// **It restores what it changed** (<c>docs/memory/a-shared-viewer-test-restores-what-it-changed.md</c>).
/// One viewer serves this whole assembly, so a test that left the readout on would put three lines
/// of white text into every later capture — including the ones another test counts colours in.
/// </remarks>
public sealed class PositionReadoutUiTests
{
    /// <summary>The one viewer this assembly runs, with its demo already open.</summary>
    private static ViewerApplication Viewer => ViewerSession.App;

    /// <summary>Where a viewer launched by this suite writes its captures.</summary>
    private static string ViewerFolder => ViewerApplication.CaptureFolder;

    [Test]
    public void Toggle_ThePositionMenuItem_DrawsTheReadoutOverTheViewport()
    {
        ViewerSession.RequireTheGame();

        // **Off first, so the "before" picture is a known state.** This assembly shares one viewer
        // and the tests do not run in a fixed order, so the readout may already be on from an
        // earlier case — and a before/after comparison that starts in the wrong state measures
        // nothing.
        SetReadout(on: false);

        long before = WhitePixelsTopRight();

        SetReadout(on: true);

        try
        {
            // The menu reached the form. `SetPositionReadout` logs the convar and its value, in
            // the same shape the frame-rate meter beside it does.
            Retry.WhileFalse(
                () => Viewer.Count("cl_showpos 1") > 0,
                TimeSpan.FromSeconds(10),
                throwOnTimeout: true,
                timeoutMessage: "the Position menu item never reached SetPositionReadout.");

            long after = WhitePixelsTopRight();

            TestContext.Out.WriteLine($"POSITION white pixels: {before} -> {after}");

            // **Three lines of white text is a lot of near-white pixels in a corner that had few.**
            // Compared rather than thresholded, because what sits behind the readout is whatever
            // the map happens to show there and no absolute number describes it.
            after.ShouldBeGreaterThan(
                before,
                "switching the readout on must put text in the top right of the frame; the log "
                + "says the setting arrived, so a picture unchanged means it never reached a quad");
        }
        finally
        {
            SetReadout(on: false);
        }
    }

    /// <summary>Sets the readout's menu item, waiting for the viewer to say it took effect.</summary>
    /// <param name="on">Whether it should be drawn.</param>
    /// <remarks>
    /// **Reads the item's state before invoking it**, because the menu item is a CHECK item: an
    /// unconditional invoke toggles, so asking for "off" twice would turn it on. The UI suite drives
    /// through UIA rather than by faking the shortcut, for the reason `ViewportPictureUiTests`
    /// records — a synthesized key goes to whichever window holds the foreground.
    /// </remarks>
    private static void SetReadout(bool on)
    {
        if (Viewer.IsMenuItemChecked(MainForm.ViewMenuName, PositionItemName) == on)
        {
            return;
        }

        Viewer.InvokeMenuItem(MainForm.ViewMenuName, PositionItemName);
    }

    /// <summary>How many near-white pixels sit in the band the readout is drawn in.</summary>
    /// <remarks>
    /// **The top right, because that is where <c>CFPSPanel::ComputeSize</c> puts the panel** —
    /// `x = wide - FPS_PANEL_WIDTH`, `y = 0`. Sampling the whole frame would drown three lines of
    /// text in a map.
    ///
    /// **Near-white rather than lit.** The frame-rate line above is coloured by `GetFPSColor` and
    /// the map behind is anything at all; the position lines are drawn flat `255, 255, 255`, so a
    /// high threshold on all three channels is the measurement faithful to what changed.
    /// </remarks>
    private static long WhitePixelsTopRight()
    {
        string shot = Capture();

        using Bitmap picture = new(shot);

        int left = Math.Max(0, picture.Width - PanelWidth);
        int bottom = Math.Min(picture.Height, BandHeight);

        long white = 0;

        for (int y = 0; y < bottom; y++)
        {
            for (int x = left; x < picture.Width; x++)
            {
                Color pixel = picture.GetPixel(x, y);

                if (pixel.R > NearWhite && pixel.G > NearWhite && pixel.B > NearWhite)
                {
                    white++;
                }
            }
        }

        return white;
    }

    /// <summary>Takes a capture through the menu and returns the file it wrote.</summary>
    /// <remarks>
    /// **Waited on by NAME rather than by count**, which `ViewportPictureUiTests` records as a real
    /// intermittent failure: the viewer prunes captures to the newest twenty AFTER writing one, so
    /// once the folder is full the count is identical before and after.
    /// </remarks>
    private static string Capture()
    {
        string[] before = Shots();

        Viewer.InvokeMenuItem(MainForm.ViewMenuName, MainForm.ScreenshotItemName);

        Retry.WhileFalse(
            () => Shots().Except(before).Any(),
            TimeSpan.FromSeconds(20),
            throwOnTimeout: true,
            timeoutMessage: "the screenshot menu item produced no picture.");

        return Shots().Except(before).OrderBy(name => name, StringComparer.Ordinal).Last();
    }

    private static string[] Shots() =>
        Directory.Exists(ViewerFolder)
            ? [.. Directory.EnumerateFiles(ViewerFolder, "shot-*.png")]
            : [];

    /// <summary>The item's accessible name, which is how the UI suite addresses a menu item.</summary>
    private const string PositionItemName = "Position";

    /// <summary><c>FPS_PANEL_WIDTH</c>, the band the readout is right-aligned into.</summary>
    private const int PanelWidth = 300;

    /// <summary>Four lines of text and the inset, generously.</summary>
    private const int BandHeight = 80;

    /// <summary>How bright every channel must be to count as the readout's white.</summary>
    private const int NearWhite = 230;
}
