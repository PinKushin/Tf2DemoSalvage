using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.SdkReference;
using Tf2DemoSalvage.Viewer3D.UiTests;

namespace Tf2DemoSalvage.Viewer3D.GoldenTests;

/// <summary>
/// The viewer's frame measured against a real TF2 screenshot of the same camera (D219).
/// </summary>
/// <remarks>
/// **Its own namespace on purpose**: `ViewerSession` is a SetUpFixture for <c>Tf2DemoSalvage.Viewer3D.UiTests</c> and would
/// launch the shared viewer, which these do not use — each is a headless `--shot`, like <see cref="CaptureUiTests"/>.
///
/// **Measured, never compared pixel for pixel** (<see cref="GoldenMeasure"/>). The numbers behind every tolerance below
/// are from 2026-10-14: two TF2 captures per golden in one session, two viewer captures per case.
///
/// - **TF2 against itself**: grid region means differ by at most 0.2 luma (granary, both levels); the lakeside pair is
///   byte-for-byte the same measure. That is the floor no tolerance can go under.
/// - **The viewer against itself**: identical to the printed digit, every measure.
/// - **The viewer against TF2**, 2026-10-15, both rendering 1280x720 (<see cref="SameSizeAsTf2"/>): granary regions at
///   most 0.5 luma apart at level 2, and one channel of a region 1.1. Level 0 and the lakeside sun are red:
///   see <see cref="GranaryYard_AtHdrLevel_RegionMeansMatchTf2"/> and
///   <see cref="LakesideSun_AtHdrLevel2WithBloom_MatchesTf2sBrightAreaAndFalloff"/>.
/// </remarks>
[NonParallelizable]
public sealed class GoldenImageUiTests
{
    /// <summary>How far a grid region's mean luma may sit from the golden's, in 0..255 luma.</summary>
    /// <remarks>
    /// **1.0: five times TF2's own spread (0.2) and twice the worst current gap (0.5).** It was 1.5 while the viewer drew
    /// 984x551 against TF2's 1280x720, which shifted region boundaries and gave a 0.9 gap. **Sabotage, 2026-10-14:**
    /// `LinearLightScale` times 1.05 in `Device3D` moved the worst region by 3.7 at level 0 and 5.2 at level 2, and both
    /// cases went red. The control, <see cref="GranaryYard_ViewerAtHdrLevel0AgainstTheLevel2Golden_IsOutsideTheTolerance"/>,
    /// measures 17.6 — so two scenes this alike are still far apart on this measure.
    /// </remarks>
    private const double RegionTolerance = 1.0;

    /// <summary>How far one channel of a grid region's mean may sit from the golden's, in 0..255.</summary>
    /// <remarks>
    /// **2.0: 1.8 times the worst current gap (1.1, a blue channel at level 2).** Luma alone cannot see a hue shift that
    /// keeps brightness. **Sabotage, 2026-10-15:** red +4 and blue −10 in `Device3D.WritePng`, which moves luma by 0.06,
    /// left the luma assertion green and turned this one red; see D219.
    /// </remarks>
    private const double ChannelTolerance = 2.0;

    private static readonly TimeSpan LongEnoughToBeAHang = TimeSpan.FromSeconds(180);

    /// <summary>cp_granary's yard: every cell of a 4x3 grid within <see cref="RegionTolerance"/> of TF2's.</summary>
    /// <remarks>
    /// **Levels 0 and 2 only.** Level 1 was captured by switching `mat_hdr_level` from 2 to 1 on the loaded map, and
    /// TF2's frame did not change at all (every region equal to level 2's to 0.1), while the viewer's level 1 is ~11 luma
    /// darker in the sky. Which is right needs a level-1 capture taken after a map LOAD at level 1; until then there is
    /// no level-1 golden, and no level-1-minus-level-0 bloom delta. Level 0 was first a live switch too, and it was NOT
    /// what a loaded map draws (worst region 7.5 apart); its golden was retaken after a load (D219).
    ///
    /// **Level 0 is red against the loaded golden, so it is explicit.** 2026-10-15: worst region 7.6 luma (59.8 against
    /// 67.4, right of centre) and worst channel 9.0; the viewer still matches the SWITCHED capture to 0.3. So the viewer's
    /// level 0 draws what TF2 draws after a live switch, not after a load — the light shafts on the right-hand wall are
    /// missing. Why is not yet read from the engine.
    /// </remarks>
    [TestCase(0, Explicit = true, Reason = "red: the viewer's level 0 matches TF2's live switch, not a map loaded at 0 (D219)")]
    [TestCase(2)]
    public void GranaryYard_AtHdrLevel_RegionMeansMatchTf2(int hdrLevel)
    {
        Golden golden = Golden.Granary(hdrLevel);
        (GoldenMeasure tf2, GoldenMeasure viewer) = Shoot(golden, golden);

        (double luma, string lumaReport) = Worst(tf2.RegionMeans(4, 3), viewer.RegionMeans(4, 3));
        (double chroma, string chromaReport) = Worst(tf2.RegionChannelMeans(4, 3), viewer.RegionChannelMeans(4, 3));

        TestContext.Out.WriteLine($"GOLDEN {golden.File} TF2/viewer luma: {lumaReport}");
        TestContext.Out.WriteLine($"GOLDEN {golden.File} TF2/viewer r g b: {chromaReport}");

        Assert.Multiple(() =>
        {
            luma.ShouldBeLessThanOrEqualTo(
                RegionTolerance, $"a region of {golden.Shows} is off TF2's by more than {RegionTolerance} luma: {lumaReport}");
            chroma.ShouldBeLessThanOrEqualTo(
                ChannelTolerance, $"a channel of a region of {golden.Shows} is off TF2's by more than {ChannelTolerance}: {chromaReport}");
        });
    }

    /// <summary>
    /// The control: the viewer's level-0 frame against the level-2 golden is OUTSIDE the tolerance, so the measure can
    /// tell two scenes apart rather than passing anything with the same layout.
    /// </summary>
    [Test]
    public void GranaryYard_ViewerAtHdrLevel0AgainstTheLevel2Golden_IsOutsideTheTolerance()
    {
        (GoldenMeasure tf2, GoldenMeasure viewer) = Shoot(Golden.Granary(2), Golden.Granary(0));

        double[] expected = tf2.RegionMeans(4, 3);
        double[] actual = viewer.RegionMeans(4, 3);
        (double luma, string report) = Worst(expected, actual);

        TestContext.Out.WriteLine($"GOLDEN control hdr2 golden / viewer hdr0 luma: {report}");

        Assert.Multiple(() =>
        {
            luma.ShouldBeGreaterThan(RegionTolerance, $"level 0 and level 2 measure alike, so the measure cannot separate them: {report}");

            // **Not a degenerate frame**: a black frame or a failed load is also "far". Level 0 is the same scene without
            // HDR, so it keeps the yard's layout (its regions spread at least half as wide as the golden's, 112.7 luma) and
            // is darker, never brighter, region by region.
            (actual.Max() - actual.Min()).ShouldBeGreaterThan(
                (expected.Max() - expected.Min()) / 2, $"the level-0 frame has lost the yard's layout: {report}");
            expected.Zip(actual, (e, a) => a - e).Max().ShouldBeLessThanOrEqualTo(
                RegionTolerance, $"a level-0 region is brighter than level 2's: {report}");
        });
    }

    /// <summary>koth_lakeside's sun at level 2 with bloom: the bright area and the falloff around it, against TF2's.</summary>
    /// <remarks>
    /// **Still red after B537 merged**, so still explicit. Rerun 2026-10-15 at 1280x720, TF2 then the viewer: luma ≥ 250
    /// is 2.39% against 1.56% (the disc is now SMALLER), and the rings at 0.04..0.18 of the width read 251.3 244.7 234.8
    /// 224.1 213.3 202.1 192.9 180.0 against 249.0 237.5 224.0 210.3 197.5 185.2 174.8 161.3 — the glow falls off
    /// faster than TF2's, up to 18.7 luma at the outermost ring.
    ///
    /// Before B537, measured 2026-10-14 at 984x551, TF2 then the viewer: pixels at luma ≥ 250 are 2.39% of the frame against
    /// 3.17%; the rings at 0.08..0.20 of the width read 234.8 224.1 213.3 202.1 192.9 180.0 against 237.0 220.4 205.5
    /// 191.7 179.2 164.1 — the viewer's disc is larger and its glow falls off faster. With `mat_disable_bloom 1` the two
    /// are 1.54% against 1.41%, so the excess is bloom's.
    ///
    /// **Tolerances**: the two TF2 captures are identical, so TF2's spread gives no width. The bright fraction may be 10%
    /// off TF2's, relative, and a ring 3 luma. The `mat_disable_bloom 1` numbers above were a one-off measurement on
    /// 2026-10-14, not a test here; they differed by 8% and 2..21 luma, so this asks for better than the frame without
    /// bloom did then.
    /// </remarks>
    [Test]
    [Explicit("red after B537: the viewer's sun disc is smaller and its glow falls off faster than TF2's (D219)")]
    public void LakesideSun_AtHdrLevel2WithBloom_MatchesTf2sBrightAreaAndFalloff()
    {
        (GoldenMeasure tf2, GoldenMeasure viewer) = Shoot(Golden.LakesideSun, Golden.LakesideSun);

        double expectedBright = tf2.BrightFraction(250);
        double actualBright = viewer.BrightFraction(250);
        (double X, double Y) sun = tf2.BrightCentroid(250);
        double[] expected = tf2.RadialProfile(sun, 0.02, 10);
        double[] actual = viewer.RadialProfile(sun, 0.02, 10);
        string report = Report(expected, actual);

        TestContext.Out.WriteLine($"GOLDEN sun bright250 TF2 {expectedBright:0.00000} viewer {actualBright:0.00000}; rings {report}");

        Assert.Multiple(() =>
        {
            actualBright.ShouldBe(expectedBright, expectedBright * 0.10, "the sun's saturated area is not TF2's");
            expected.Zip(actual, (e, a) => Math.Abs(e - a)).Max().ShouldBeLessThanOrEqualTo(
                3.0, $"the sun's falloff is not TF2's: {report}");
        });
    }

    /// <summary>The 3D view's size, `TF2VIEW_VIEWPORT_SIZE`: the size TF2 rendered every golden at.</summary>
    /// <remarks>
    /// **The default 1280x720 WINDOW gave a 984x551 viewport** — the playlist and the bars take 296 by 169 at 100% display
    /// scaling — which is 1.786:1 against 16:9's 1.778. Measured 2026-10-15 at three sizes, worst granary region at level
    /// 2: 984x551 gave 1.0 luma, 1024x576 (16:9, smaller) gave 1.5, and 1280x720 gives 0.5. So the size matters as well
    /// as the aspect. The view is sized directly, so the chrome's size at other scalings does not matter.
    /// </remarks>
    private const string SameSizeAsTf2 = "1280x720";

    /// <summary>Captures the viewer as <paramref name="viewerSide"/> says and loads it beside <paramref name="golden"/>.</summary>
    private static (GoldenMeasure Tf2, GoldenMeasure Viewer) Shoot(Golden golden, Golden viewerSide)
    {
        if (!GameInstall.Available)
        {
            Skip.Because("Team Fortress 2 is not installed, so the map's textures and models cannot match a TF2 screenshot.");
        }

        if (!File.Exists(viewerSide.DemoPath))
        {
            Assert.Ignore($"The lcor demo for this golden is not present at {viewerSide.DemoPath}.");
        }

        string folder = Path.Combine(Path.GetTempPath(), "tf2ds-golden", Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(folder);

        try
        {
            string png = Path.Combine(folder, "viewer.png");
            (int Exit, string Errors) run = CaptureUiTests.Capture(
                new Dictionary<string, string>
                {
                    ["TF2VIEW_CAMERA"] = viewerSide.Camera,
                    [MainForm.ViewportSizeVariable] = SameSizeAsTf2,
                },
                viewerSide.DemoPath, png, LongEnoughToBeAHang, ShotTick, [.. Golden.Common, .. viewerSide.ConVars]);

            run.Exit.ShouldBe(0, $"standard error:{Environment.NewLine}{run.Errors}");

            using (System.Drawing.Bitmap frame = new(png))
            {
                (frame.Width, frame.Height).ShouldBe((1280, 720), "the viewer frame is not TF2's 1280x720");
            }

            return (GoldenMeasure.Load(golden.ImagePath), GoldenMeasure.Load(png));
        }
        finally
        {
            CaptureUiTests.Clean(folder);
        }
    }

    /// <summary>A tick inside every golden demo; the camera is placed, so which one matters only for moving props.</summary>
    private const int ShotTick = 300;

    /// <summary>The largest absolute difference between two measures, and both printed side by side.</summary>
    private static (double Worst, string Report) Worst(double[] expected, double[] actual) =>
        (expected.Zip(actual, (e, a) => Math.Abs(e - a)).Max(), Report(expected, actual));

    private static string Report(double[] expected, double[] actual) => string.Join(
        " ", expected.Zip(actual, (e, a) => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{e:0.0}/{a:0.0}")));
}
