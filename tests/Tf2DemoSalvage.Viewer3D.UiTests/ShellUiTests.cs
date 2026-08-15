using System;
using System.IO;

using FlaUI.Core.AutomationElements;
using FlaUI.Core.WindowsAPI;
using FlaUI.Core.Tools;

namespace Tf2DemoSalvage.Viewer3D.UiTests;

/// <summary>
/// Drives the real viewer through UI Automation.
/// </summary>
/// <remarks>
/// **These launch the application and take over a desktop**, so they are excluded from the normal
/// test run and must go through <c>run-exclusive.ps1</c>. Two agents driving one desktop is not a
/// slow test, it is a click delivered into somebody else's window.
///
/// **One application for the whole fixture**, which NUnit's default lifetime gives for free.
/// Launching the viewer is the expensive part - a device, a swap chain and a window - and paying
/// it per test would dominate the runtime for no isolation gain, since nothing here mutates state
/// another test reads.
///
/// What these prove that the unit tests cannot: that the shell actually starts, that a Direct3D
/// device is created against a real adapter, and that the automation ids survive into the live UIA
/// tree. A form constructed in memory demonstrates none of those.
/// </remarks>
[TestFixture]
public sealed class ShellUiTests
{
    private ViewerApplication _viewer = null!;

    [OneTimeSetUp]
    public void LaunchViewer()
    {
        _viewer = ViewerApplication.Launch();
        TestContext.Out.WriteLine($"launched: window '{_viewer.Window.Title}'");
    }

    [OneTimeTearDown]
    public void CloseViewer() => _viewer?.Dispose();

    [TearDown]
    public void ReturnToWindowed()
    {
        // **A shared fixture needs its state put back, and full screen is the state that leaks.**
        // One application instance serves every test here, so a test that ends full screen hands
        // the next one a window already the size of the screen. That test then reads 1920 as its
        // "windowed" width, presses F11, waits for the viewport to GROW, and times out after ten
        // seconds - a failure attributed to the wrong test and looking exactly like flake.
        //
        // Observed for real: the suite failed once and passed on the next run with no change.
        // Retrying would have buried it.
        if (_viewer is null)
        {
            return;
        }

        System.Drawing.Rectangle screen = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;

        if (_viewer.Window.BoundingRectangle.Width < screen.Width)
        {
            return;
        }

        ViewerApplication.Log("still full screen after the test; pressing Escape");
        _viewer.PressKey(VirtualKeyShort.ESCAPE);

        Retry.WhileTrue(
            () => _viewer.Window.BoundingRectangle.Width >= screen.Width,
            TimeSpan.FromSeconds(10));
    }

    [Test]
    public void TheShellExposesEveryControlAutomationCanDrive()
    {
        // The ids are the contract between the application and every test that will ever drive
        // it. Checked in the LIVE tree rather than on a constructed form: a control can carry a
        // Name in code and still not surface as an AutomationId if it never gets a window handle.
        _viewer.Exists("Viewport").ShouldBeTrue("the Direct3D viewport is missing");
        _viewer.Exists("Playlist").ShouldBeTrue("the playlist is missing");
        _viewer.Exists("OpenButton").ShouldBeTrue("the Open button is missing");
        _viewer.Exists("OpenFolderButton").ShouldBeTrue("the Open folder button is missing");
        _viewer.Exists("PlayPauseButton").ShouldBeTrue("the play button is missing");
        _viewer.Exists("ScrubBar").ShouldBeTrue("the scrub bar is missing");
    }

    [Test]
    public void TheDeviceComesUpAgainstARealAdapter()
    {
        // Nothing below the UI can tell us this. The unit tests deliberately never create a
        // device, so until something launches the application on a machine with a GPU, "Direct3D
        // works" is untested - and a failure here reads as a driver or machine problem rather
        // than as a decode bug, which is why it is reported in the status bar.
        string status = _viewer.StatusText();
        TestContext.Out.WriteLine($"status bar reads: '{status}'");

        _viewer.StatusText().ShouldBe("Direct3D ready.");
    }

    [Test]
    public void PlaybackControlsAreDisabledUntilADemoIsOpen()
    {
        // A Play button that looks pressable with nothing loaded invites a click that does
        // nothing, and "nothing happened" is indistinguishable from a bug in playback.
        _viewer.Find("PlayPauseButton").IsEnabled.ShouldBeFalse();
        _viewer.Find("ScrubBar").IsEnabled.ShouldBeFalse();
    }

    [Test]
    public void TheActionRowSitsBelowThePlaybackControls()
    {
        // Asked for explicitly: the action buttons are operations on the demo as a whole, the
        // transport is about the moment being watched, so the actions belong underneath.
        //
        // Only checkable on a real window. WinForms docking order is decided at layout time, and
        // a form that was never shown has no layout - so this cannot be a unit test, and the
        // first attempt at the ordering shipped upside down for exactly that reason.
        // Checked AFTER a full-screen round trip, deliberately. Leaving full screen re-adds the
        // transport bar to the form, and a plain Add appends it - which flipped the docking order
        // against the action row and left the buttons above the play bar for the rest of the
        // session. Asserting only on a freshly launched window would have missed it entirely.
        // Relative to the window's own starting width, not to pixel constants: these run at CI's
        // 754x512 as well as on a developer's screen, and a hard-coded 1000 would be wrong on one
        // of them.
        int beforeFullScreen = _viewer.Find("Viewport").BoundingRectangle.Width;

        _viewer.PressKey(VirtualKeyShort.F11);
        Retry.WhileFalse(
            () => _viewer.Find("Viewport").BoundingRectangle.Width > beforeFullScreen,
            TimeSpan.FromSeconds(10));

        _viewer.PressKey(VirtualKeyShort.ESCAPE);
        Retry.WhileFalse(
            () => _viewer.Find("Viewport").BoundingRectangle.Width == beforeFullScreen,
            TimeSpan.FromSeconds(10));

        int playTop = _viewer.Find("PlayPauseButton").BoundingRectangle.Top;
        int openTop = _viewer.Find("OpenButton").BoundingRectangle.Top;

        ViewerApplication.Log($"play button top={playTop}, open button top={openTop}");

        openTop.ShouldBeGreaterThan(
            playTop, "the action row should sit below the playback controls");
    }

    [Test]
    public void FullScreenHidesTheChromeAndEscapeBringsItBack()
    {
        // The full-screen transition is the one piece of this shell that unit tests can only
        // approximate: they assert where controls live, not whether the window actually resizes
        // or whether the key even reaches the form past a focused viewport panel.
        // **No focus, and no synthesized input.** This used to focus the window and press F11,
        // because synthesized keys go to the FOREGROUND window and the launched viewer is not
        // necessarily it. That is true, and it is the reason not to use them at all: a test that
        // needs the foreground is a test that fights the person at the machine for it, and twice
        // here the keystrokes went into a browser. Invoking the menu item through UIA needs no
        // focus, no window ordering and no real input.
        //
        // Escape's binding is not covered here any more, and does not need to be: FullScreenTests
        // exercises ProcessCmdKey directly, without a display, which is a better place to ask
        // whether a key is handled than a launched application is.

        AutomationElement viewport = _viewer.Find("Viewport");
        int windowedWidth = viewport.BoundingRectangle.Width;

        TestContext.Out.WriteLine($"windowed viewport width: {windowedWidth}");

        // Type, not Press: FlaUI's Press holds the key DOWN and never releases it, so a shortcut
        // that fires on the complete keystroke never sees one.
        _viewer.PressKey(VirtualKeyShort.F11);
        Retry.WhileFalse(
            () => _viewer.Find("Viewport").BoundingRectangle.Width > windowedWidth,
            TimeSpan.FromSeconds(10));

        TestContext.Out.WriteLine(
            $"after F11: {_viewer.Find("Viewport").BoundingRectangle.Width}");

        _viewer.Find("Viewport").BoundingRectangle.Width
            .ShouldBeGreaterThan(windowedWidth, "the viewport did not grow on entering full screen");


        // Escape, not F11, so the second binding is covered too - it is the one a user reaches
        // for by habit and the one most likely to be missed by a form-level key handler.
        _viewer.PressKey(VirtualKeyShort.ESCAPE);

        Retry.WhileFalse(
            () => Math.Abs(_viewer.Find("Viewport").BoundingRectangle.Width - windowedWidth) < 2,
            TimeSpan.FromSeconds(10));

        TestContext.Out.WriteLine(
            $"after Escape: {_viewer.Find("Viewport").BoundingRectangle.Width}");

        // Exactly the width it started at. An earlier version allowed two pixels of slack and
        // would have passed against a real defect: leaving full screen restored the border style
        // but not the bounds, so the client area came back 16 pixels narrower - and lost another
        // 16 on every toggle. A tolerance chosen to "avoid flake" hides exactly that.
        _viewer.Find("Viewport").BoundingRectangle.Width
            .ShouldBe(windowedWidth, "Escape did not restore the original window size");
    }

    [Test]
    public void FullScreen_CoversTheWholeScreenAndDropsTheSidebar()
    {
        // Two defects found by looking at it, neither visible without a real window.
        //
        // **The taskbar stayed on top.** Full screen was a borderless MAXIMISED window, and a
        // maximised window is sized to the work area - the screen minus the taskbar - so it was a
        // big window rather than a full screen one.
        //
        // **The playlist's panel stayed docked.** The code hid the playlist and the search box
        // after those two moved inside a container, so 280 pixels of empty panel kept its place
        // and the viewport came out that much narrower.

        System.Drawing.Rectangle screen = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
        int windowedWidth = _viewer.Find("Viewport").BoundingRectangle.Width;

        _viewer.PressKey(VirtualKeyShort.F11);
        Retry.WhileFalse(
            () => _viewer.Find("Viewport").BoundingRectangle.Width > windowedWidth,
            TimeSpan.FromSeconds(10));

        System.Drawing.Rectangle window = _viewer.Window.BoundingRectangle;
        System.Drawing.Rectangle viewport = _viewer.Find("Viewport").BoundingRectangle;

        ViewerApplication.Log(
            $"screen={screen.Width}x{screen.Height} window={window.Width}x{window.Height} " +
            $"viewport={viewport.Width}x{viewport.Height}");

        window.Width.ShouldBe(screen.Width, "the window does not span the screen");
        window.Height.ShouldBe(screen.Height, "the window does not cover the taskbar");

        // The viewport takes the full width, which is the observable form of "no empty panel is
        // still docked". Asserting the panel's Visible would prove nothing - see the unit tests.
        viewport.Width.ShouldBe(screen.Width, "something is still docked beside the viewport");
        viewport.Height.ShouldBe(screen.Height, "something is still docked above or below the viewport");

        _viewer.PressKey(VirtualKeyShort.ESCAPE);
        Retry.WhileFalse(
            () => Math.Abs(_viewer.Find("Viewport").BoundingRectangle.Width - windowedWidth) < 2,
            TimeSpan.FromSeconds(10));
    }
}

