using System;
using System.Collections.Generic;
using System.IO;

namespace Tf2DemoSalvage.Viewer3D.GoldenTests;

/// <summary>
/// One committed TF2 screenshot and everything needed to take the same picture again, in TF2 and in the viewer (D219).
/// </summary>
/// <param name="File">The PNG under <c>Goldens/</c>, resampled to <see cref="GoldenMeasure.MeasuredWidth"/>.</param>
/// <param name="Map">The map TF2 had loaded — with <c>map</c>, never by playing a demo in it.</param>
/// <param name="Demo">The lcor demo the viewer side plays: recorded in the same TF2 session, on the same map.</param>
/// <param name="Camera">`spec_goto` in TF2 and <c>TF2VIEW_CAMERA</c> in the viewer: "x y z pitch yaw", the eye.</param>
/// <param name="ConVars">Set in TF2 at capture and passed to the viewer the same way.</param>
/// <param name="Shows">What is in the picture, for whoever has to re-take it.</param>
internal sealed record Golden(
    string File, string Map, string Demo, string Camera, IReadOnlyList<string> ConVars, string Shows)
{
    /// <summary>
    /// How every golden was captured. Field of view 90 (`fov_desired 90` in TF2, `demo_fov_override 90` in the viewer),
    /// 1280x720 windowed, as a spectator in `spec_mode 6` placed with `spec_goto`, `cl_drawhud 0`, through the tf2 MCP's
    /// screenshot tool, which writes nothing to Steam's library. Captured 2026-10-14 on the TF2 build current that day.
    /// </summary>
    /// <remarks>
    /// **How the PNG was made from the capture**: <see cref="GoldenMeasure.Resampled(string)"/> on the 1280x720 screenshot,
    /// once, to <see cref="GoldenMeasure.MeasuredWidth"/> wide (HighQualityBilinear, HighQuality pixel offset). A golden is
    /// therefore measured as it is, and only the viewer's frame is resampled at test time.
    /// </remarks>
    public static readonly IReadOnlyList<string> Common =
        ["+demo_fov_override", "90", "+cl_drawhud", "0", "+cl_showfps", "0"];

    /// <summary>cp_granary's BLU-side yard: containers, silos, a lit wall, sky. No particles; the spinning CP hologram is one small sprite.</summary>
    /// <remarks>
    /// **Level 0 is `_loaded`**: retaken 2026-10-15 after `map cp_granary` at level 0, because the first level-0 capture
    /// was a live switch from 2 and does not match a loaded map (D219). `cp_granary_yard_hdr0.png` is that superseded
    /// capture, read by nothing.
    /// </remarks>
    public static Golden Granary(int hdrLevel)
    {
        string file = hdrLevel switch
        {
            0 => "cp_granary_yard_hdr0_loaded.png",
            2 => "cp_granary_yard_hdr2.png",
            _ => throw new ArgumentOutOfRangeException(
                nameof(hdrLevel), hdrLevel,
                $"there is no cp_granary golden at mat_hdr_level {hdrLevel}: only levels 0 and 2 were captured (D219)."),
        };

        return new(
            file,
            "cp_granary",
            "golden_cp_granary2.dem",
            "-1982 -794 -294 -1 61",
            ["+mat_hdr_level", hdrLevel.ToString(System.Globalization.CultureInfo.InvariantCulture)],
            "BLU yard toward the silos and the middle point, at mat_hdr_level " + hdrLevel);
    }

    /// <summary>koth_lakeside_final's sun from the middle, looking up into it, with bloom (the B378 camera).</summary>
    public static readonly Golden LakesideSun = new(
        "koth_lakeside_sun_hdr2.png",
        "koth_lakeside_final",
        "golden_koth_lakeside.dem",
        "0 -152 600 -35 235",
        ["+mat_hdr_level", "2"],
        "the sun and its glow above the temple, mat_hdr_level 2, bloom on, mat_hdr_tonemapscale read 0.7");

    /// <summary>The committed image's path in the test output.</summary>
    public string ImagePath => Path.Combine(TestContext.CurrentContext.TestDirectory, "Goldens", File);

    /// <summary>The demo's path: lcor under the checkout this test was built from (a worktree needs its junction).</summary>
    /// <remarks>The checkout is found by walking up to `Tf2DemoSalvage.slnx`, so the build output's depth does not matter.</remarks>
    public string DemoPath => Path.Combine(CheckoutRoot(), "tools", "corpus", "local", Demo);

    private static string CheckoutRoot()
    {
        for (DirectoryInfo? at = new(TestContext.CurrentContext.TestDirectory); at is not null; at = at.Parent)
        {
            if (System.IO.File.Exists(Path.Combine(at.FullName, "Tf2DemoSalvage.slnx")))
            {
                return at.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            $"no Tf2DemoSalvage.slnx above {TestContext.CurrentContext.TestDirectory}, so lcor cannot be found.");
    }
}
