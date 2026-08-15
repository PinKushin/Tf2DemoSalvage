using System;
using System.IO;
using System.Linq;

using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;
using FlaUI.Core.Input;

namespace Tf2DemoSalvage.Viewer3D.UiTests;

/// <summary>
/// Leaves behind a picture of what the viewer actually drew.
/// </summary>
/// <remarks>
/// **This replaces a test that rendered the same scene through a parallel path**, and the reason is
/// worth keeping. The offscreen target existed so a picture could be produced without a window, and
/// it agreed with the viewer only for as long as nobody changed either one. It drifted twice:
/// decals were added to the window and not to it, and it had no depth buffer at all — so every draw
/// overwrote what came before in material-batch order, and dark surfaces painted over foliage.
///
/// Its pictures were then read as evidence about the viewer, and sent hunting for a rendering
/// defect that only the test had. The owner settled it by looking at the real window, where the
/// same map was fine.
///
/// **A capture from the swap chain cannot drift**, because there is nothing to keep in step: it is
/// the frame the user is looking at, read back after Present. The cost is that it needs a window,
/// so this is a UI test and takes the desktop.
/// </remarks>
[TestFixture]
public sealed class ViewportPictureUiTests
{
    private ViewerApplication _viewer = null!;

    /// <summary>Where the viewer writes its captures, beside its log.</summary>
    private static string ViewerFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Tf2DemoSalvage");

    /// <summary>A committed demo whose map ships with the game.</summary>
    private static string DemoPath => Path.GetFullPath(Path.Combine(
        TestContext.CurrentContext.TestDirectory,
        "..", "..", "..", "..", "..",
        "tools", "corpus", "demos", "tf2-2013-build1729296-pov-cp_badlands.dem"));

    [OneTimeSetUp]
    public void LaunchViewerWithADemo()
    {
        if (!File.Exists(DemoPath))
        {
            Assert.Ignore($"the corpus demo is not present at {DemoPath}");
            return;
        }

        _viewer = ViewerApplication.Launch(DemoPath);

        // Synchronised on the map appearing in the log rather than on a delay: loading reads a
        // hundred megabytes and decodes a couple of hundred textures, and how long that takes is a
        // property of the machine.
        Retry.WhileFalse(
            () => _viewer.Count("building the world") > 0,
            TimeSpan.FromSeconds(60),
            throwOnTimeout: true,
            timeoutMessage:
                $"The viewer never reported building a world. Log: {_viewer.LogPath ?? "NONE FOUND"} " +
                $"in {ViewerApplication.Folder}.");
    }

    [OneTimeTearDown]
    public void CloseViewer() => _viewer?.Dispose();

    [Test]
    public void F12WritesAPictureOfWhatTheViewerDrew()
    {
        string[] before = Shots();

        _viewer.Focus();
        Keyboard.Type(VirtualKeyShort.F12);

        // The capture happens on the next presented frame, so it arrives when the viewer next
        // draws rather than when the key is released.
        Retry.WhileFalse(
            () => Shots().Length > before.Length,
            TimeSpan.FromSeconds(20),
            throwOnTimeout: true,
            timeoutMessage: "F12 produced no picture.");

        string shot = Shots().Except(before).Single();

        TestContext.Out.WriteLine($"PICTURE {shot}");

        // **The picture is the deliverable and this is the guard around it.** A capture that wrote
        // a blank or truncated file would satisfy "a file appeared", so the assertion is that it is
        // a real image of the right size - the readback pads rows to the driver's alignment, and
        // getting that wrong produces a file that opens and is skewed.
        using System.Drawing.Bitmap picture = new(shot);

        picture.Width.ShouldBeGreaterThan(200, "the viewport should not be a sliver");
        picture.Height.ShouldBeGreaterThan(150);

        long lit = 0;

        for (int y = 0; y < picture.Height; y += 4)
        {
            for (int x = 0; x < picture.Width; x += 4)
            {
                System.Drawing.Color pixel = picture.GetPixel(x, y);

                if (pixel.R + pixel.G + pixel.B > 60)
                {
                    lit++;
                }
            }
        }

        long sampled = (long)(picture.Width / 4) * (picture.Height / 4);

        TestContext.Out.WriteLine($"PICTURE {lit} of {sampled} sampled pixels are lit");

        // A map fills a good part of the viewport. Nearly black means the world never drew, which
        // is the failure this whole exercise exists to notice.
        lit.ShouldBeGreaterThan(sampled / 20, "the viewer should be showing a map");
    }

    private static string[] Shots() =>
        Directory.Exists(ViewerFolder)
            ? [.. Directory.EnumerateFiles(ViewerFolder, "shot-*.png")]
            : [];
}
