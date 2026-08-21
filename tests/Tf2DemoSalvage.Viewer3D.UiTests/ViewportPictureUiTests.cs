using System;
using System.IO;
using System.Linq;

using FlaUI.Core.Tools;

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
    /// <summary>Where the viewer writes its captures, beside its log.</summary>
    private static string ViewerFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Tf2DemoSalvage");

    /// <summary>The one viewer this assembly runs, with its demo already open.</summary>
    private static ViewerApplication _viewer => ViewerSession.App;

    [Test]
    public void F12WritesAPictureOfWhatTheViewerDrew()
    {
        string[] before = Shots();

        // Invoked through the menu item rather than by faking F12. Synthesized keys go to whichever
        // window holds the foreground, so the press lands wherever the tester happens to be looking
        // — measured, twice, as keystrokes arriving in a browser. UIA needs no focus at all.
        //
        // The menu item did not exist until this test needed it, and that is the finding rather
        // than an inconvenience: the screenshot had no route except a function key, so anyone
        // driving the viewer by keyboard navigation or a screen reader had no way to reach it.
        _viewer.InvokeMenuItem(MainForm.ViewMenuName, MainForm.ScreenshotItemName);

        // The capture happens on the next presented frame, so it arrives when the viewer next
        // draws rather than when the key is released.
        //
        // **Waited for by NAME, not by count, and the difference is a real failure.** The viewer
        // prunes its captures to the twenty most recent AFTER writing each one, so once the folder
        // is full a new picture replaces an old one and the count is identical before and after.
        // This waited on `Shots().Length > before.Length` and therefore timed out with "F12
        // produced no picture" against a picture sitting on disk — intermittent, because it only
        // starts happening once the folder reaches the cap, which a second capture test made
        // happen sooner.
        //
        // Same class as everything else caught here: the measurement was not faithful to the
        // variable. "A file that was not there before appeared" is the question; a count is not it.
        Retry.WhileFalse(
            () => Shots().Except(before).Any(),
            TimeSpan.FromSeconds(20),
            throwOnTimeout: true,
            timeoutMessage: "F12 produced no picture.");

        // First rather than Single: the prune can remove an old capture between the two listings,
        // and more than one new file is not a reason to fail a test about the newest one.
        string shot = Shots().Except(before).OrderBy(name => name, StringComparer.Ordinal).Last();

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

        // **And how much is in it, which brightness cannot say.** A wall a few feet from the camera
        // passes the assertion above without difficulty — 93 per cent of THIS capture's pixels are
        // lit, and planks are lit too — so counting lit pixels could not tell a view of the map from
        // a view of a surface. The owner could, by looking, which is how it was found.
        //
        // Measured before it was asserted: 18 distinct colours for the wall the first-person capture
        // used to be, 146 for this one.
        int colours = FrameStructure.Colours(picture);

        TestContext.Out.WriteLine($"STRUCTURE {Path.GetFileName(shot)}: {colours} distinct colours");

        colours.ShouldBeGreaterThan(
            40, "the capture is nearly one colour, so the viewer is not showing the map");
    }

    private static string[] Shots() =>
        Directory.Exists(ViewerFolder)
            ? [.. Directory.EnumerateFiles(ViewerFolder, "shot-*.png")]
            : [];
}
