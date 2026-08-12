using System;
using System.IO;

using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;

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
    public void FullScreenHidesTheChromeAndEscapeBringsItBack()
    {
        // The full-screen transition is the one piece of this shell that unit tests can only
        // approximate: they assert where controls live, not whether the window actually resizes
        // or whether the key even reaches the form past a focused viewport panel.
        // Focus first: synthesized keys go to the foreground window, and the launched viewer is
        // not necessarily it. Without this the F11 lands in whatever was in front.
        _viewer.Focus();
        TestContext.Out.WriteLine(
            $"focused: hasFocus={_viewer.Window.Properties.HasKeyboardFocus.ValueOrDefault}, " +
            $"offscreen={_viewer.Window.Properties.IsOffscreen.ValueOrDefault}");

        AutomationElement viewport = _viewer.Find("Viewport");
        int windowedWidth = viewport.BoundingRectangle.Width;

        TestContext.Out.WriteLine($"windowed viewport width: {windowedWidth}");

        // Type, not Press: FlaUI's Press holds the key DOWN and never releases it, so a shortcut
        // that fires on the complete keystroke never sees one.
        Keyboard.Type(VirtualKeyShort.F11);
        Retry.WhileFalse(
            () => _viewer.Find("Viewport").BoundingRectangle.Width > windowedWidth,
            TimeSpan.FromSeconds(10));

        TestContext.Out.WriteLine(
            $"after F11: {_viewer.Find("Viewport").BoundingRectangle.Width}");

        _viewer.Find("Viewport").BoundingRectangle.Width
            .ShouldBeGreaterThan(windowedWidth, "the viewport did not grow on entering full screen");


        // Escape, not F11, so the second binding is covered too - it is the one a user reaches
        // for by habit and the one most likely to be missed by a form-level key handler.
        Keyboard.Type(VirtualKeyShort.ESCAPE);

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
}
