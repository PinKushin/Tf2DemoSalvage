using System;
using System.IO;

using FlaUI.Core.AutomationElements;
using FlaUI.Core.WindowsAPI;
using FlaUI.Core.Tools;

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

    /// <summary>What it logs when it projects the world through the camera, once per map.</summary>
    private const string WorldBuildLine = "building the world";

    /// <summary>What it logs on every viewport change, which is now the whole cost of one.</summary>
    private const string CameraLine = "camera set for a";

    /// <summary>What it logs once the opening seek has landed and the session is ready.</summary>
    private const string OpeningLine = "opening state applied";

    /// <summary>The one viewer this assembly runs, with its demo already open.</summary>
    private static ViewerApplication _viewer => ViewerSession.App;

    [Test]
    public void EnteringFullScreen_RepointsTheCameraWithoutRebuildingOrReuploading()
    {
        // **The measurement, and it inverted when the camera became a matrix.** This test used to
        // demand that going full screen REBUILD the world, because the projection was baked into
        // every vertex and a new viewport meant new vertices. That is the design the camera matrix
        // removed: the vertices are in map coordinates now and never move, so a resize uploads
        // sixty-four bytes and the world is built exactly once per map.
        //
        // Left as it was, the test failed against the improvement - it waited twenty seconds for a
        // rebuild that correctly never comes, and the failure read as "full screen is broken" while
        // the window in front of the tester was plainly full screen. A test that encodes the old
        // design does not become right by continuing to pass elsewhere.
        //
        // Three counts, and all three are needed. The camera must be repointed, or nothing happened
        // at all. The world must NOT be rebuilt, which is the fix. The textures must NOT be
        // re-uploaded, which is the older fix underneath it.
        //
        // **After the opening seek, not during it.** The map is built seconds before the opening state
        // is applied, so run first in a session this fixture went full screen mid-load, and the shared
        // viewer then sounded no footstep at the opening tick - the control `PausedSoundUiTests` needs
        // (2026-10-13, twice). A fixture establishes its own state rather than inheriting the order.
        Retry.WhileFalse(
            () => Count(OpeningLine) > 0,
            TimeSpan.FromSeconds(60),
            throwOnTimeout: true,
            timeoutMessage: "The viewer never applied its opening state.");

        int texturesBefore = Count(TextureUploadLine);
        int buildsBefore = Count(WorldBuildLine);
        int camerasBefore = Count(CameraLine);

        texturesBefore.ShouldBe(1, "the map's textures should have been uploaded once at load");
        buildsBefore.ShouldBe(1, "the world should have been built once at load");

        // **Through the automation pattern, never through synthesized input.** Keyboard.Type and
        // Window.Click send real system input, which goes to whatever holds the FOREGROUND — so a
        // test using them is racing the person at the machine and will lose. Measured twice here:
        // key presses and clicks landing in a browser mid-run.
        //
        // Nothing about this needs focus. UIA invokes the menu item directly, the same way a screen
        // reader would, and the application does not have to be in front for it to arrive. If a
        // control cannot be driven that way, that is an accessibility defect in the application
        // worth fixing rather than a reason to reach for the mouse — every route this suite needs
        // is one a keyboard-only user needs too.
        // Found once and polled by property: a whole-tree `Find` per poll can outlast UIA's 2 s timeout.
        AutomationElement viewport = _viewer.Find("Viewport");

        // F11 is this item's own shortcut key, so the item is the same action without real input.
        _viewer.InvokeMenuItem(MainForm.ViewMenuName, MainForm.FullScreenItemName);

        System.Drawing.Rectangle screen = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;

        Retry.WhileFalse(
            () => viewport.BoundingRectangle.Width >= screen.Width,
            TimeSpan.FromSeconds(20),
            throwOnTimeout: true,
            timeoutMessage: "The viewport never filled the screen.");

        // Wait for the resize to have been PROCESSED, not merely for the window to have moved.
        // Without this the counts below could be read before the viewer has handled the resize at
        // all, and "nothing was rebuilt" would be true for the wrong reason on a fast machine.
        Retry.WhileFalse(
            () => Count(CameraLine) > camerasBefore,
            TimeSpan.FromSeconds(20),
            throwOnTimeout: true,
            timeoutMessage: "Full screen never repointed the camera.");

        Count(WorldBuildLine).ShouldBe(
            buildsBefore,
            "a resize must not rebuild the world; the camera is a matrix");

        Count(TextureUploadLine).ShouldBe(
            texturesBefore,
            "resizing must not re-decode and re-upload the map's textures");
    }

    private System.Drawing.Rectangle _found;

    [SetUp]
    public void RememberTheWindow() => _found = _viewer.Window.BoundingRectangle;

    // The same leak the other fixture guards: a test that ends full screen hands the next one
    // a window already the size of the screen, and the failure lands on the wrong test.
    [TearDown]
    public void ReturnToWindowed() => _viewer.RestoreWindow(_found);

    /// <summary>How many times this run's viewer log contains a line.</summary>
    private static int Count(string line) => _viewer.Count(line);
}
