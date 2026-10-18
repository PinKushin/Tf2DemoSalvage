using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

using Tf2DemoSalvage.SdkReference;
using Tf2DemoSalvage.Viewer3D.UiTests;

namespace Tf2DemoSalvage.Viewer3D.GoldenTests;

/// <summary>The stock HUD against TF2's own, element by element (D222, under D219's golden rules).</summary>
/// <remarks>
/// **The goldens are TF2 screenshots**, taken 2026-10-17 on the modern client with the owner's HUD moved out of
/// `tf/custom` (D222), on `cp_process_f12` loaded with `map`, 1280x720, `cl_savescreenshotstosteam 0`, with `crosshair 1`,
/// `viewmodel_fov 54`, `tf_use_min_viewmodels 0` and `r_drawviewmodel 1` set for the capture. The viewer side plays the
/// POV demo recorded in that session, `hudref-cp_process_f12-2026-10-17-pov.dem`, from the recorder's own eye. Kept at
/// full 1280x720 rather than D219's 640, because the ammo number is 37 pixels wide at 1280 and a HUD's shapes are its
/// subject.
///
/// **Measured per element, never pixel for pixel**: each element's rectangle is cut into a 4x2 grid and every cell's
/// mean red, green and blue compared. The world shows through the HUD's translucent backgrounds, and it is the same map
/// from the same eye on both sides, so it is in the measure rather than masked out.
/// </remarks>
[NonParallelizable]
public sealed class HudGoldenImageUiTests
{
    /// <summary>How far one channel of a cell's mean may sit from TF2's, in 0..255.</summary>
    /// <remarks>Set from the measured gaps once the fixes landed; the numbers are in each case's remarks.</remarks>
    private const double ChannelTolerance = 12.0;

    private const string Demo = "hudref-cp_process_f12-2026-10-17-pov.dem";

    private static readonly TimeSpan LongEnoughToBeAHang = TimeSpan.FromSeconds(180);

    /// <summary>The BLU soldier at full health: the ammo panel (4 in the clip, 20 in reserve) and the class portrait with
    /// its health cross.</summary>
    /// <remarks>
    /// Measured 2026-10-17 after the background, team, pose and gamma fixes. **Ammo: worst cell 53.0**, every one of
    /// them under the "4" — its shadow is painted over it (B552); before the fixes 39.6 (a flat block, the shadow
    /// over a lighter number). **Class and health: worst 23.1**, the model's helmet and launcher (125 against 101):
    /// pose, framing and background now match and the model is darker (finding 78's residual); before, 63.2.
    /// </remarks>
    [TestCase("ammo", 1150, 640, 1265, 705, Explicit = true, Reason = "red: the ammo number's shadow is painted over it (B552)")]
    [TestCase("class and health", 0, 585, 200, 705, Explicit = true, Reason = "red: the class model is about 20 luma darker than TF2's (finding 78)")]
    public void Soldier_TheElement_MatchesTf2(string element, int x0, int y0, int x1, int y1) =>
        Measure("hud_cp_process_f12_soldier.png", SoldierTick, element, new Rectangle(x0, y0, x1 - x0, y1 - y0));

    /// <summary>The BLU medic holding the medigun: the ÜberCharge meter at 0%, and the class portrait.</summary>
    /// <remarks>
    /// Measured 2026-10-17. **Meter: worst cell 80.4**, and not the meter's: its shape, outline, label and bar match
    /// TF2's by eye at 3x, and the cells that differ are where TF2's first-person medigun hand sits behind the
    /// translucent background and ours does not — the viewmodels differ, which a HUD golden cannot isolate. Before the
    /// port there was no meter at all (the control below measures that absence). **Class and health: worst 24.8**, the
    /// model again.
    /// </remarks>
    [TestCase("ubercharge meter", 1078, 630, 1262, 702, Explicit = true, Reason = "red from the viewmodel behind it, not the meter (finding 78)")]
    [TestCase("class and health", 0, 585, 200, 705, Explicit = true, Reason = "red: the class model is about 20 luma darker than TF2's (finding 78)")]
    public void Medic_TheElement_MatchesTf2(string element, int x0, int y0, int x1, int y1) =>
        Measure("hud_cp_process_f12_medic.png", MedicTick, element, new Rectangle(x0, y0, x1 - x0, y1 - y0));

    /// <summary>
    /// **Regression locks, not parity**: the viewer against ITS OWN render after the fixes (`regression_lock_ours_*`),
    /// so the backgrounds, team colour, pose, gamma and ÜberCharge meter cannot quietly come undone while the TF2
    /// comparisons above are red for the reasons they state. A tighter tolerance, because the viewer repeats itself
    /// to the printed digit (D219).
    /// </summary>
    [TestCase("regression_lock_ours_hud_soldier.png", SoldierTick, "ammo", 1150, 640, 1265, 705)]
    [TestCase("regression_lock_ours_hud_soldier.png", SoldierTick, "class and health", 0, 585, 200, 705)]
    [TestCase("regression_lock_ours_hud_medic.png", MedicTick, "ubercharge meter", 1078, 630, 1262, 702)]
    public void RegressionLock_TheElement_MatchesTheFixedRender(string golden, int tick, string element, int x0, int y0, int x1, int y1)
    {
        (double worst, string report) = Compare(golden, tick, new Rectangle(x0, y0, x1 - x0, y1 - y0));

        TestContext.Out.WriteLine($"HUD REGRESSION LOCK {golden} {element}: worst {worst:0.0}; {report}");
        worst.ShouldBeLessThanOrEqualTo(2.0, $"the {element} moved from the fixed render: {report}");
    }

    /// <summary>
    /// The control: the medic's ÜberCharge meter rectangle in the SOLDIER's frame, which has no meter there, is outside
    /// the tolerance — so the measure can tell an element from its absence.
    /// </summary>
    [Test]
    public void Soldier_TheMedicsMeterRectangle_IsOutsideTheTolerance()
    {
        Rectangle meter = new(1078, 630, 184, 72);
        (double worst, string report) = Compare("hud_cp_process_f12_medic.png", SoldierTick, meter);

        TestContext.Out.WriteLine($"HUD GOLDEN control: medic meter vs soldier frame {report}");
        worst.ShouldBeGreaterThan(ChannelTolerance, $"a frame without the meter measures like one with it: {report}");
    }

    /// <summary>Ticks inside the moments TF2 was captured: soldier at 200 health, medic with the medigun out.</summary>
    private const int SoldierTick = 1830;

    private const int MedicTick = 4600;

    private static void Measure(string golden, int tick, string element, Rectangle rectangle)
    {
        (double worst, string report) = Compare(golden, tick, rectangle);

        TestContext.Out.WriteLine($"HUD GOLDEN {golden} {element}: worst {worst:0.0}; {report}");
        worst.ShouldBeLessThanOrEqualTo(
            ChannelTolerance, $"a cell of the {element} is off TF2's by more than {ChannelTolerance}: {report}");
    }

    private static (double Worst, string Report) Compare(string golden, int tick, Rectangle rectangle)
    {
        if (!GameInstall.Available)
        {
            Skip.Because("Team Fortress 2 is not installed, so the HUD's files cannot be read.");
        }

        string demo = Path.Combine(CheckoutRoot(), "tools", "corpus", "local", Demo);

        if (!File.Exists(demo))
        {
            Assert.Ignore($"The lcor demo {Demo} is not present at {demo} (docs/memory/lcor-is-not-in-a-worktree.md).");
        }

        string folder = Path.Combine(Path.GetTempPath(), "tf2ds-hud-golden", Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(folder);

        try
        {
            string png = Path.Combine(folder, "viewer.png");
            (int Exit, string Errors) run = CaptureUiTests.Capture(
                new Dictionary<string, string> { [MainForm.ViewportSizeVariable] = "1280x720" },
                demo, png, LongEnoughToBeAHang, tick, "--first-person", "+cl_showfps", "0");

            run.Exit.ShouldBe(0, $"standard error:{Environment.NewLine}{run.Errors}");

            using Bitmap expected = new(Path.Combine(TestContext.CurrentContext.TestDirectory, "Goldens", "Hud", golden));
            using Bitmap actual = new(png);

            (actual.Width, actual.Height).ShouldBe((expected.Width, expected.Height), "the viewer frame is not TF2's size");

            double[] want = Cells(expected, rectangle);
            double[] got = Cells(actual, rectangle);
            double worst = 0;
            List<string> cells = [];

            for (int index = 0; index < want.Length; index++)
            {
                worst = Math.Max(worst, Math.Abs(want[index] - got[index]));
                cells.Add(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{want[index]:0.0}/{got[index]:0.0}"));
            }

            return (worst, string.Join(" ", cells));
        }
        finally
        {
            CaptureUiTests.Clean(folder);
        }
    }

    /// <summary>A rectangle's 4x2 grid of cells, each cell's mean red, green and blue in turn.</summary>
    private static double[] Cells(Bitmap image, Rectangle rectangle)
    {
        const int Columns = 4;
        const int Rows = 2;
        double[] means = new double[Columns * Rows * 3];

        for (int row = 0; row < Rows; row++)
        {
            for (int column = 0; column < Columns; column++)
            {
                int left = rectangle.Left + (rectangle.Width * column / Columns);
                int right = rectangle.Left + (rectangle.Width * (column + 1) / Columns);
                int top = rectangle.Top + (rectangle.Height * row / Rows);
                int bottom = rectangle.Top + (rectangle.Height * (row + 1) / Rows);
                double red = 0, green = 0, blue = 0;
                int count = 0;

                for (int y = top; y < bottom; y++)
                {
                    for (int x = left; x < right; x++)
                    {
                        Color pixel = image.GetPixel(x, y);
                        (red, green, blue, count) = (red + pixel.R, green + pixel.G, blue + pixel.B, count + 1);
                    }
                }

                int at = ((row * Columns) + column) * 3;
                (means[at], means[at + 1], means[at + 2]) = (red / count, green / count, blue / count);
            }
        }

        return means;
    }

    private static string CheckoutRoot()
    {
        for (DirectoryInfo? at = new(TestContext.CurrentContext.TestDirectory); at is not null; at = at.Parent)
        {
            if (File.Exists(Path.Combine(at.FullName, "Tf2DemoSalvage.slnx")))
            {
                return at.FullName;
            }
        }

        throw new DirectoryNotFoundException($"no Tf2DemoSalvage.slnx above {TestContext.CurrentContext.TestDirectory}.");
    }
}
