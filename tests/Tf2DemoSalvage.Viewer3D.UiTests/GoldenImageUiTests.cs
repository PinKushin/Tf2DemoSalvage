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
/// - **The viewer against TF2, at the time of writing**: granary regions at most 0.5 luma apart at level 0 and 0.9 at
///   level 2. The lakeside sun is NOT close — see <see cref="LakesideSun_AtHdrLevel2WithBloom_MatchesTf2sBrightAreaAndFalloff"/>.
/// </remarks>
[NonParallelizable]
public sealed class GoldenImageUiTests
{
    /// <summary>How far a grid region's mean luma may sit from the golden's, in 0..255 luma.</summary>
    /// <remarks>
    /// **1.5, and it is NOT derived from TF2's own spread alone** — that spread is 0.2, and a viewer frame is 984x551
    /// against TF2's 1280x720 (two rows fewer at the measured width), which moves a region's boundary by a fraction of
    /// a pixel row. 1.5 is about seven times TF2's spread and 1.7 times the worst current gap (0.9). **Sabotage,
    /// 2026-10-14:** `LinearLightScale` times 1.05 in `Device3D` moved the worst region by 3.7 at level 0 and 5.2 at
    /// level 2, and both cases went red.
    /// </remarks>
    private const double RegionTolerance = 1.5;

    private static readonly TimeSpan LongEnoughToBeAHang = TimeSpan.FromSeconds(180);

    /// <summary>cp_granary's yard: every cell of a 4x3 grid within <see cref="RegionTolerance"/> of TF2's.</summary>
    /// <remarks>
    /// **Levels 0 and 2 only.** Level 1 was captured by switching `mat_hdr_level` from 2 to 1 on the loaded map, and
    /// TF2's frame did not change at all (every region equal to level 2's to 0.1), while the viewer's level 1 is ~11 luma
    /// darker in the sky. Which is right needs a level-1 capture taken after a map LOAD at level 1; until then there is
    /// no level-1 golden, and no level-1-minus-level-0 bloom delta. Level 0 was also a live switch, from 2, and it did
    /// change the frame; it carries the same caveat.
    /// </remarks>
    [TestCase(0)]
    [TestCase(2)]
    public void GranaryYard_AtHdrLevel_RegionMeansMatchTf2(int hdrLevel)
    {
        Golden golden = Golden.Granary(hdrLevel);
        (GoldenMeasure tf2, GoldenMeasure viewer) = Shoot(golden);

        double[] expected = tf2.RegionMeans(4, 3);
        double[] actual = viewer.RegionMeans(4, 3);
        string report = Report(expected, actual);

        TestContext.Out.WriteLine($"GOLDEN {golden.File} regions TF2 vs viewer: {report}");

        expected.Zip(actual, (e, a) => Math.Abs(e - a)).Max().ShouldBeLessThanOrEqualTo(
            RegionTolerance, $"a region of {golden.Shows} is off TF2's by more than {RegionTolerance} luma: {report}");
    }

    /// <summary>koth_lakeside's sun at level 2 with bloom: the bright area and the falloff around it, against TF2's.</summary>
    /// <remarks>
    /// **Red on main, and it is the bloom agent's acceptance check (`fix/bloom-strength`)**, so it is explicit until that
    /// branch makes it pass. Measured 2026-10-14, TF2 then the viewer: pixels at luma ≥ 250 are 2.39% of the frame against
    /// 3.17%; the rings at 0.08..0.20 of the width read 234.8 224.1 213.3 202.1 192.9 180.0 against 237.0 220.4 205.5
    /// 191.7 179.2 164.1 — the viewer's disc is larger and its glow falls off faster. With `mat_disable_bloom 1` the two
    /// are 1.54% against 1.41%, so the excess is bloom's.
    ///
    /// **Tolerances**: the two TF2 captures are identical, so TF2's spread gives no width. The bright fraction may be 10%
    /// off TF2's, relative, and a ring 3 luma — the bloom-off control above already differs by 8% and 2..21 luma, so
    /// this asks for better than the frame without bloom does today.
    /// </remarks>
    [Test]
    [Explicit("red on main until fix/bloom-strength lands; that branch's acceptance check (D219)")]
    public void LakesideSun_AtHdrLevel2WithBloom_MatchesTf2sBrightAreaAndFalloff()
    {
        (GoldenMeasure tf2, GoldenMeasure viewer) = Shoot(Golden.LakesideSun);

        double expectedBright = tf2.BrightFraction(250);
        double actualBright = viewer.BrightFraction(250);
        (double X, double Y) sun = tf2.BrightCentroid(250);
        double[] expected = tf2.RadialProfile(sun, 0.02, 10);
        double[] actual = viewer.RadialProfile(sun, 0.02, 10);
        string report = Report(expected, actual);

        TestContext.Out.WriteLine($"GOLDEN sun bright250 TF2 {expectedBright:0.00000} viewer {actualBright:0.00000}; rings {report}");

        actualBright.ShouldBe(expectedBright, expectedBright * 0.10, "the sun's saturated area is not TF2's");
        expected.Zip(actual, (e, a) => Math.Abs(e - a)).Max().ShouldBeLessThanOrEqualTo(
            3.0, $"the sun's falloff is not TF2's: {report}");
    }

    /// <summary>Captures the viewer at a golden's camera and loads both sides for measuring.</summary>
    private static (GoldenMeasure Tf2, GoldenMeasure Viewer) Shoot(Golden golden)
    {
        if (!GameInstall.Available)
        {
            Skip.Because("Team Fortress 2 is not installed, so the map's textures and models cannot match a TF2 screenshot.");
        }

        if (!File.Exists(golden.DemoPath))
        {
            Assert.Ignore($"The lcor demo for this golden is not present at {golden.DemoPath}.");
        }

        string folder = Path.Combine(Path.GetTempPath(), "tf2ds-golden", Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(folder);

        try
        {
            string png = Path.Combine(folder, "viewer.png");
            (int Exit, string Errors) run = CaptureUiTests.Capture(
                new Dictionary<string, string> { ["TF2VIEW_CAMERA"] = golden.Camera },
                golden.DemoPath, png, LongEnoughToBeAHang, ShotTick, [.. Golden.Common, .. golden.ConVars]);

            run.Exit.ShouldBe(0, $"standard error:{Environment.NewLine}{run.Errors}");

            return (GoldenMeasure.Load(golden.ImagePath), GoldenMeasure.Load(png));
        }
        finally
        {
            CaptureUiTests.Clean(folder);
        }
    }

    /// <summary>A tick inside every golden demo; the camera is placed, so which one matters only for moving props.</summary>
    private const int ShotTick = 300;

    private static string Report(double[] expected, double[] actual) => string.Join(
        " ", expected.Zip(actual, (e, a) => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{e:0.0}/{a:0.0}")));
}
