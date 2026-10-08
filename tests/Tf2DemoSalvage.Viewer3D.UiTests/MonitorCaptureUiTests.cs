using System;
using System.Drawing;
using System.IO;

namespace Tf2DemoSalvage.Viewer3D.UiTests;

/// <summary>
/// The <c>point_camera</c> monitor on a real recording: <c>koth_boardwalk</c>'s mirror shows the camera's view (B511).
/// </summary>
/// <remarks>
/// **The specimen is `b511_boardwalk_mirror.dem`** (lcor), a POV demo recorded on the live client standing at
/// (-1168 700 40) facing the stage's three mirrors, whose <c>sideshow_mirror</c> material is
/// <c>UnLitGeneric $basetexture _rt_Camera</c>. Its <c>mirror_camera</c> (FOV 45, black fog 192-1024) is active and in
/// the recorder's PVS throughout.
///
/// **The property that differs** (<c>docs/memory/a-picture-is-assertable.md</c>): the mirror shows the soldier the camera
/// sees — red — where with <c>cl_drawmonitors 0</c> the same pixels are the target's untouched black. The control is
/// the same capture with the pass off, so the difference can only have come through <c>_rt_Camera</c>.
/// </remarks>
public sealed class MonitorCaptureUiTests
{
    private const string DemoName = "b511_boardwalk_mirror.dem";

    /// <summary>A tick where the recorder faces the mirrors squarely (yaw 90).</summary>
    private const int ShotTick = 1900;

    private static readonly TimeSpan LongEnoughToBeAHang = TimeSpan.FromSeconds(240);

    private static string DemoPath => Path.GetFullPath(Path.Combine(
        TestContext.CurrentContext.TestDirectory,
        "..", "..", "..", "..", "..",
        "tools", "corpus", "local", DemoName));

    [Test]
    public void Capture_BoardwalksMirrorWithMonitorsOnAndOff_ShowsTheCamerasViewOnlyWhenOn()
    {
        if (!File.Exists(DemoPath))
        {
            Assert.Ignore($"The lcor specimen is not present at {DemoPath}.");
            return;
        }

        (double Red, double Green) on = MirrorMean();
        (double Red, double Green) off = MirrorMean("+cl_drawmonitors", "0");

        TestContext.Out.WriteLine($"MIRROR ON R {on.Red:F1} G {on.Green:F1}; OFF R {off.Red:F1} G {off.Green:F1}");

        // Off: the target nothing drew into — black, under the additive glass layer's own faint grey (measured R 19, G 21).
        (off.Red - off.Green).ShouldBeLessThan(5, "with the pass off the mirror still shows something red");

        // On: the red soldier's torso the camera sees (measured R 83, G 46 on a hand capture).
        on.Red.ShouldBeGreaterThan(off.Red + 40, "the mirror does not show the camera's view");
        on.Red.ShouldBeGreaterThan(on.Green + 20, "the mirror's picture is not the red soldier in front of it");
    }

    /// <summary>The mean red and green of the middle mirror's lower half, where the soldier's body is.</summary>
    private static (double Red, double Green) MirrorMean(params string[] extra)
    {
        string folder = Path.Combine(Path.GetTempPath(), "tf2ds-shot", Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(folder);

        string png = Path.Combine(folder, "monitor.png");

        try
        {
            (int exit, string errors) = CaptureUiTests.Capture(DemoPath, png, LongEnoughToBeAHang, ShotTick, extra);

            exit.ShouldBe(0, $"standard error:{Environment.NewLine}{errors}");

            using Bitmap picture = new(png);

            // The middle mirror is 0.44-0.56 across and 0.31-0.74 down; the soldier's torso in it sits just under the
            // crosshair.
            double red = 0;
            double green = 0;
            int count = 0;

            for (int y = (int)(picture.Height * 0.53); y < (int)(picture.Height * 0.59); y++)
            {
                for (int x = (int)(picture.Width * 0.47); x < (int)(picture.Width * 0.53); x++)
                {
                    Color pixel = picture.GetPixel(x, y);

                    red += pixel.R;
                    green += pixel.G;
                    count++;
                }
            }

            return (red / count, green / count);
        }
        finally
        {
            CaptureUiTests.Clean(folder);
        }
    }
}
