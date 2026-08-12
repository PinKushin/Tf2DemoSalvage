using System;
using System.IO;
using System.Linq;

using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;

namespace Tf2DemoSalvage.Viewer3D.UiTests;

/// <summary>
/// Full screen with a map actually loaded.
/// </summary>
/// <remarks>
/// **The existing full-screen tests open no demo, so they have no map, so they could not see this.**
/// The viewer went to about one frame a second in full screen because every resize rebuilt the
/// world AND re-decoded and re-uploaded all 208 of the map's textures — and entering full screen
/// fires several resizes in a row. With no demo loaded there are no textures and no world, so the
/// fixed code and the broken code produce exactly the same observation. Wrong condition, not a
/// weak assertion: the fixture had to change, not the assertion.
///
/// **The instrument is the viewer's own log**, not a clock. A stopwatch here would measure the
/// machine — an unloaded developer box and a busy one disagree by more than the defect does. The
/// log records each texture upload and each world build, so the question becomes a count, and a
/// count is deterministic.
///
/// **These take over the desktop** like every test in this project, so they go through
/// <c>run-exclusive.ps1</c>.
/// </remarks>
[TestFixture]
public sealed class MapFullScreenUiTests
{
    /// <summary>What the viewer logs when it decodes and uploads a map's textures.</summary>
    private const string TextureUploadLine = "uploading textures";

    /// <summary>What it logs when it projects the world through the camera.</summary>
    private const string WorldBuildLine = "building the world";

    private ViewerApplication _viewer = null!;

    /// <summary>Where the viewer writes what it did.</summary>
    private static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Tf2DemoSalvage",
        "viewer.log");

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
            Assert.Ignore($"The corpus demo is not present at {DemoPath}.");
            return;
        }

        _viewer = ViewerApplication.Launch(DemoPath);

        // Synchronised on the world appearing in the log, not on a delay. Loading a map reads a
        // hundred megabytes and decodes two hundred textures, and how long that takes is a
        // property of the machine.
        Retry.WhileFalse(
            () => Count(WorldBuildLine) > 0 && Count(TextureUploadLine) > 0,
            TimeSpan.FromSeconds(60),
            throwOnTimeout: true,
            timeoutMessage: "The viewer never reported building a world; the map did not load.");
    }

    [OneTimeTearDown]
    public void CloseViewer() => _viewer?.Dispose();

    [Test]
    public void EnteringFullScreen_RebuildsTheWorldWithoutReUploadingTheTextures()
    {
        // **The measurement.** Going full screen must reproject the world - the camera projection
        // is baked into the vertices, so new viewport, new vertices - and must NOT touch the
        // textures, which do not depend on the camera at all.
        //
        // Both halves matter. Asserting only that textures were uploaded once would pass against a
        // viewer that had stopped resizing altogether, and asserting only that the world was
        // rebuilt would pass against the defect this exists for.
        int texturesBefore = Count(TextureUploadLine);
        int buildsBefore = Count(WorldBuildLine);

        texturesBefore.ShouldBe(1, "the map's textures should have been uploaded once at load");

        _viewer.Focus();
        Keyboard.Type(VirtualKeyShort.F11);

        System.Drawing.Rectangle screen = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;

        Retry.WhileFalse(
            () => _viewer.Find("Viewport").BoundingRectangle.Width >= screen.Width,
            TimeSpan.FromSeconds(20),
            throwOnTimeout: true,
            timeoutMessage: "The viewport never filled the screen.");

        // Wait for the resize to have been PROCESSED, not merely for the window to have moved.
        // Without this the count could be read before the rebuild it is meant to observe, and the
        // test would pass for the wrong reason on a fast machine.
        Retry.WhileFalse(
            () => Count(WorldBuildLine) > buildsBefore,
            TimeSpan.FromSeconds(20),
            throwOnTimeout: true,
            timeoutMessage: "Full screen never rebuilt the world.");

        Count(TextureUploadLine).ShouldBe(
            texturesBefore,
            "resizing must not re-decode and re-upload the map's textures");
    }

    [TearDown]
    public void ReturnToWindowed()
    {
        // The same leak the other fixture guards: a test that ends full screen hands the next one
        // a window already the size of the screen, and the failure lands on the wrong test.
        if (_viewer is null)
        {
            return;
        }

        System.Drawing.Rectangle screen = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;

        if (_viewer.Window.BoundingRectangle.Width < screen.Width)
        {
            return;
        }

        _viewer.Focus();
        Keyboard.Type(VirtualKeyShort.ESCAPE);

        Retry.WhileTrue(
            () => _viewer.Window.BoundingRectangle.Width >= screen.Width,
            TimeSpan.FromSeconds(10));
    }

    /// <summary>How many times the viewer has logged a line.</summary>
    /// <remarks>
    /// Opened shared, because the viewer holds the file open and appends to it while this reads.
    /// A plain <c>File.ReadAllText</c> throws an IOException here, intermittently, which reads as
    /// flake and is not.
    /// </remarks>
    private static int Count(string line)
    {
        if (!File.Exists(LogPath))
        {
            return 0;
        }

        try
        {
            using FileStream file = new(
                LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using StreamReader reader = new(file);

            return reader.ReadToEnd()
                .Split('\n')
                .Count(entry => entry.Contains(line, StringComparison.Ordinal));
        }
        catch (IOException)
        {
            // The viewer was mid-write. Reporting nothing lets the retry loop ask again, which is
            // the only correct answer available at this instant.
            return 0;
        }
    }
}
