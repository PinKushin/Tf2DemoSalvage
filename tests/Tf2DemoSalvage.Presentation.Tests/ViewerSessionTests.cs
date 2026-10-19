using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Tf2DemoSalvage.Content.Tests.Bsp;
using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Render;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>
/// The viewer without its window (D214, slice 9) [no engine counterpart: the idle loop, the capture countdown, the
/// measurement and the key dispatch are this viewer's], over a window with no controls, a transport with no slider, a
/// device that draws nothing and a process that is two strings and a number.
/// </summary>
/// <remarks>
/// **Every test here was impossible while the code was in the form**: reaching any of it needed a window, a message
/// pump and, for most, a GPU. The demos are written by the test (D213) and the map is the one empty BSP
/// <see cref="ViewerPresenterTests"/> uses.
/// </remarks>
[NonParallelizable]
public sealed class ViewerSessionTests
{
    private const string Map = "ctf_written_by_a_test";

    private string _folder = string.Empty;

    [SetUp]
    public void CreateFolder()
    {
        _folder = Path.Combine(Path.GetTempPath(), "tf2salvage-tests", Path.GetRandomFileName());
        _gameFolder = null;
        Directory.CreateDirectory(Path.Combine(_folder, "maps"));
        File.WriteAllBytes(Path.Combine(_folder, "maps", Map + ".bsp"), SyntheticBsp.Build(new Dictionary<int, byte[]>()));
    }

    [TearDown]
    public void RemoveFolder()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException held)
        {
            // A disposable temp folder: a file still held open is reported and left, and fails no test.
            TestContext.Out.WriteLine($"left {_folder} behind: {held.Message}");
        }
    }

    private string WriteDemo(string name, int ticks = 12345)
    {
        DemoHeader header = new()
        {
            DemoProtocol = 3,
            NetworkProtocol = 24,
            ServerName = "test server",
            ClientName = "tester",
            MapName = Map,
            GameDirectory = "tf",
            PlaybackTimeSeconds = ticks * 0.015f,
            PlaybackTicks = ticks,
            PlaybackFrames = ticks,
            SignonLengthBytes = 0,
        };

        string path = Path.Combine(_folder, name + ".dem");
        File.WriteAllBytes(path, DemoWriter.Write(header, [new DemoCommand(DemoCommandType.Stop, 0, default)]));

        return path;
    }

    /// <summary>A session and everything it talks to.</summary>
    private sealed class Rig : IDisposable
    {
        public required ViewerSession Session { get; init; }

        public required FakeViewerWindow Window { get; init; }

        public required FakePlaybackView Transport { get; init; }

        public required RecordingLoggers Log { get; init; }

        public required StringWriter Out { get; init; }

        public required StringWriter Error { get; init; }

        public required SettingsPresenter Settings { get; init; }

        public List<int> ExitCodes { get; } = [];

        public Dictionary<string, string> Environment { get; } = new(StringComparer.Ordinal);

        public FakeRenderDevice Device { get; } = new();

        /// <summary>Gives the session the stand-in device, as a created one would be.</summary>
        public Rig WithDevice()
        {
            Session.OpenDevice(() => Device, 1280, 720).ShouldBeTrue();

            return this;
        }

        public void Dispose()
        {
            Session.Dispose();
            Out.Dispose();
            Error.Dispose();
            Log.Dispose();
        }
    }

    private Rig Build(params string[] arguments) => Build(new ViewerSettings { FrameRateLimit = 0 }, 0.5, arguments);

    private Rig Build(ViewerSettings settings, double frameSeconds = 0.5, params string[] arguments)
    {
        FakeViewerWindow window = new();
        FakePlaybackView transport = new();
        RecordingLoggers log = new();
        StringWriter output = new();
        StringWriter error = new();
        List<int> exits = [];
        Dictionary<string, string> environment = new(StringComparer.Ordinal);
#pragma warning disable CA2000 // The session disposes the provider; the rig disposes the session.
        MapProvider maps = new(
            Path.Combine(_folder, "no-steam-library.vdf"),
            Path.Combine(_folder, "maps"),
            static () => throw new InvalidOperationException("no test here downloads a map"),
            gameFolder: () => _gameFolder);
#pragma warning restore CA2000
        SettingsPresenter owner = new(log, settings, Path.Combine(_folder, "settings.cfg"));

        ViewerSession session = new(
            window,
            transport,
            log,
            arguments,
            new FpsPanelTests.SolidGdi(),
            new ViewerHost(output, error, exits.Add, name => environment.GetValueOrDefault(name), () => new FixedTime(frameSeconds)),
            maps,
            owner);

        window.SurfaceColoursTicked = session.SetSurfaceColours;

        Rig rig = new()
        {
            Session = session, Window = window, Transport = transport, Log = log, Out = output, Error = error, Settings = owner,
        };

        // The rig's lists are the ones the host writes to, so they are shared rather than copied.
        exits.Clear();
        _exits = exits;
        _environment = environment;

        return rig;
    }

    private List<int> _exits = [];

    /// <summary>The TF2 folder the session's configs are read from; null is no install.</summary>
    private string? _gameFolder;

    /// <summary>A session whose TF2 install carries this autoexec.cfg.</summary>
    private Rig BuildWithAutoexec(string autoexec)
    {
        _gameFolder = Path.Combine(_folder, "tf");
        Directory.CreateDirectory(Path.Combine(_gameFolder, "cfg"));
        File.WriteAllText(Path.Combine(_gameFolder, "cfg", "autoexec.cfg"), autoexec);

        return Build();
    }

    private Dictionary<string, string> _environment = [];

    private static KeyPress Key(
        string name,
        Func<string?, bool>? chord = null,
        FocusedWidget focus = FocusedWidget.None,
        bool escape = false,
        bool controlOrAlt = false,
        bool repeat = false) =>
        new(
            name,
            controlOrAlt,
            focus,
            chord ?? (binding => string.Equals(binding, name, StringComparison.OrdinalIgnoreCase)),
            escape,
            name,
            repeat);

    // ---- the render switches ----

    [Test]
    public void SetWireframe_OnThenOff_LogsTheCvarTellsTheDeviceAndAsksForARepaintEachTime()
    {
        using Rig rig = Build().WithDevice();
        int redraws = rig.Window.Count("Redraw");

        rig.Session.SetWireframe(true);
        rig.Device.Wireframe.ShouldBeTrue();
        rig.Session.SetWireframe(false);

        rig.Device.Wireframe.ShouldBeFalse();
        rig.Log.Lines.Where(static line => line.Contains("mat_wireframe", StringComparison.Ordinal))
            .ShouldBe(["[render] mat_wireframe 1", "[render] mat_wireframe 0"]);
        (rig.Window.Count("Redraw") - redraws).ShouldBe(2);
    }

    [Test]
    public void SetRenderSwitches_EachOne_ReachesItsOwnDevicePropertyUnderValvesName()
    {
        using Rig rig = Build().WithDevice();

        rig.Session.SetDrawWorld(false);
        rig.Session.SetDrawEntities(false);
        rig.Session.SetSpecular(false);
        rig.Session.SetPhong(false);

        (rig.Device.DrawWorld, rig.Device.DrawEntities, rig.Device.Specular, rig.Device.Phong).ShouldBe((false, false, false, false));
        rig.Device.Wireframe.ShouldBeFalse("no switch throws its neighbour");
        rig.Log.Lines.Where(static line => line.StartsWith("[render] r_draw", StringComparison.Ordinal) || line.StartsWith("[render] mat_", StringComparison.Ordinal))
            .ShouldBe(["[render] r_drawworld 0", "[render] r_drawentities 0", "[render] mat_specular 0", "[render] mat_phong 0"]);
    }

    [Test]
    public void SetWireframe_BeforeThereIsADevice_StillLogsAndRepaints()
    {
        using Rig rig = Build();

        rig.Session.SetWireframe(true);

        rig.Log.Count("mat_wireframe 1").ShouldBe(1);
        rig.Window.Count("Redraw").ShouldBe(1);
    }

    [Test]
    public void SetFullbright_LightingOnly_TicksItsItemLogsTheValueAndTellsTheDevice()
    {
        using Rig rig = Build().WithDevice();

        rig.Session.SetFullbright(Fullbright.LightingOnly);

        rig.Session.Fullbright.ShouldBe(Fullbright.LightingOnly);
        rig.Device.Fullbright.ShouldBe(Fullbright.LightingOnly);
        rig.Window.Told.ShouldContain("ShowFullbright 2");
        rig.Log.Count("[render] mat_fullbright 2").ShouldBe(1);
    }

    [Test]
    public void SetDebugMode_ASecondView_KeepsTheFirstAndHandsTheDeviceBoth()
    {
        using Rig rig = Build().WithDevice();

        rig.Session.SetDebugMode(static (modes, on) => modes with { Luxels = on }, true);
        rig.Session.SetDebugMode(static (modes, on) => modes with { DrawFlat = on }, true);

        rig.Device.Debug.ShouldBe(DebugModes.None with { Luxels = true, DrawFlat = true });

        rig.Session.SetDebugMode(static (modes, on) => modes with { Luxels = on }, false);

        rig.Device.Debug.ShouldBe(DebugModes.None with { DrawFlat = true });
        rig.Log.Count("[render] debug views: ").ShouldBe(3);
    }

    [Test]
    public void SetSurfaceColours_OnThenOff_WritesTheLegendOnceAndThenSaysOff()
    {
        using Rig rig = Build();

        rig.Session.SetSurfaceColours(true);
        rig.Session.SetSurfaceColours(false);

        rig.Log.Count("[render] surface colours on — grey-blue brushwork").ShouldBe(1);
        rig.Log.Count("[render] surface colours off").ShouldBe(1);
        rig.Window.Count("Redraw").ShouldBe(2);
    }

    [Test]
    public void SetFullScreenExclusive_WhileWindowed_TicksTheItemAndLeavesTheDeviceAlone()
    {
        using Rig rig = Build().WithDevice();

        rig.Session.SetFullScreenExclusive(true);

        rig.Session.FullScreenExclusive.ShouldBeTrue();
        rig.Window.Told.ShouldContain("ShowFullScreenExclusive True");
        rig.Device.Exclusive.ShouldBeEmpty("the mode is entered when the window goes full screen, not when it is chosen");
    }

    [Test]
    public void SetFullScreenExclusive_WhileFullScreenAndRefused_AsksTheDeviceAndSaysItWasRefused()
    {
        using Rig rig = Build().WithDevice();
        rig.Window.IsFullScreen = true;
        rig.Device.GrantsExclusive = false;

        rig.Session.SetFullScreenExclusive(true);

        rig.Device.Exclusive.ShouldBe([true]);
        rig.Window.Status.ShouldBe(SettingsPresenter.ExclusiveFullScreenRefused);
    }

    [Test]
    public void EnteredFullScreen_WithExclusiveChosen_TakesTheDisplayAndLeavingGivesItBack()
    {
        using Rig rig = Build(new ViewerSettings { FullScreenMode = FullScreenMode.Exclusive }).WithDevice();

        rig.Session.EnteredFullScreen();
        rig.Session.LeavingFullScreen();

        rig.Device.Exclusive.ShouldBe([true, false]);
        rig.Window.Told.ShouldNotContain("Status: " + SettingsPresenter.ExclusiveFullScreenRefused);
    }

    [Test]
    public void EnteredFullScreen_WithBorderlessChosen_AsksTheDeviceNothing()
    {
        using Rig rig = Build().WithDevice();

        rig.Session.EnteredFullScreen();

        rig.Device.Exclusive.ShouldBeEmpty();
    }

    [Test]
    public void EnteredFullScreen_ExclusiveRefused_SaysSoInTheStatus()
    {
        using Rig rig = Build(new ViewerSettings { FullScreenMode = FullScreenMode.Exclusive }).WithDevice();
        rig.Device.GrantsExclusive = false;

        rig.Session.EnteredFullScreen();

        rig.Window.Status.ShouldBe(SettingsPresenter.ExclusiveFullScreenRefused);
    }

    [Test]
    public void SetTextureQuality_ALevel_TicksItRemembersItAndSaysWhenItApplies()
    {
        using Rig rig = Build();

        rig.Session.SetTextureQuality(nameof(TextureQuality.Low));

        rig.Session.TextureQuality.ShouldBe(nameof(TextureQuality.Low));
        rig.Window.Told.ShouldContain("ShowTextureQuality " + nameof(TextureQuality.Low));
        rig.Window.Status.ShouldNotBeNullOrEmpty();
        rig.Window.Status.ShouldNotBe("No demo loaded.");
    }

    // ---- the device ----

    [Test]
    public void OpenDevice_WhenTheMachineGivesOne_SaysTheFrameRateLineThenTheSizeAndThatDirect3DIsReady()
    {
        using Rig rig = Build();

        rig.Session.HasDevice.ShouldBeFalse();
        rig.Session.OpenDevice(() => rig.Device, 800, 600).ShouldBeTrue();

        rig.Session.HasDevice.ShouldBeTrue();
        rig.Window.Status.ShouldBe("Direct3D ready.");
        string[] render = [.. rig.Log.Lines.Where(static line => line.StartsWith("[render] ", StringComparison.Ordinal))];
        render[^1].ShouldBe("[render] device created for a 800x600 viewport");
        render[^2].ShouldBe("[render] " + rig.Session.Settings.FrameRateLine);
    }

    [Test]
    public void OpenDevice_WhenTheMachineRefuses_SaysWhyInTheStatusLogsItAndAnswersFalse()
    {
        using Rig rig = Build();

        rig.Session.OpenDevice(static () => throw new InvalidOperationException("no adapter"), 800, 600).ShouldBeFalse();

        rig.Session.HasDevice.ShouldBeFalse();
        rig.Window.Status.ShouldBe("Direct3D unavailable: no adapter");
        rig.Log.Count("[render] creating the Direct3D device").ShouldBe(1);
        rig.Log.Count("device created for").ShouldBe(0);
    }

    [Test]
    public async Task OpenDevice_WithADemoAlreadyOpen_LeavesTheDemosStatusAlone()
    {
        using Rig rig = Build();
        (await rig.Session.Presenter.LoadDemoAsync(WriteDemo("first")).ConfigureAwait(false)).Loaded.ShouldBeTrue();
        string? before = rig.Window.Status;

        rig.Session.OpenDevice(() => rig.Device, 800, 600).ShouldBeTrue();

        rig.Window.Status.ShouldBe(before);
    }

    [Test]
    public void ViewportResized_ToARealSize_ResizesTheDeviceAndAnEmptyOneDoesNot()
    {
        using Rig rig = Build().WithDevice();

        rig.Window.Viewport = (640, 480);
        rig.Session.ViewportResized();
        rig.Window.Viewport = (0, 480);
        rig.Session.ViewportResized();
        rig.Window.Viewport = (640, 0);
        rig.Session.ViewportResized();

        rig.Device.Sizes.ShouldBe([(640, 480)]);
        rig.Session.Presenter.WorldNeedsProjecting.ShouldBeTrue("a new size is a new projection");
    }

    // ---- keys and the pointer ----

    [Test]
    public void KeyPressed_WhileATextBoxHasTheKeyboard_IsLeftToTheTextBox()
    {
        using Rig rig = Build();
        string reset = rig.Session.Bindings.KeyFor(ViewerAction.ResetSpeed).ShouldNotBeNull();

        rig.Session.KeyPressed(Key(reset, focus: FocusedWidget.Text)).ShouldBeFalse();

        rig.Transport.Speeds.ShouldBeEmpty();
    }

    [Test]
    public void KeyPressed_TheResetSpeedBinding_PutsTheTransportBackToRealTime()
    {
        using Rig rig = Build();
        string reset = rig.Session.Bindings.KeyFor(ViewerAction.ResetSpeed).ShouldNotBeNull();

        rig.Session.KeyPressed(Key(reset)).ShouldBeTrue();

        // ONCE (B547): the key also goes to the console, which raises the one-shot; `OnConsoleAction` must ignore it.
        rig.Transport.Speeds.ShouldBe([1d]);
    }

    [Test]
    public void KeyPressed_TheScreenshotBinding_CapturesAStampedFileInTheScreenshotFolder()
    {
        string shots = Path.Combine(_folder, "shots");
        using Rig rig = Build(new ViewerSettings { FrameRateLimit = 0, ScreenshotFolder = shots }).WithDevice();
        string screenshot = rig.Session.Bindings.KeyFor(ViewerAction.Screenshot).ShouldNotBeNull();

        rig.Session.KeyPressed(Key(screenshot)).ShouldBeTrue();

        string written = rig.Device.Captured.ShouldHaveSingleItem();
        Path.GetDirectoryName(written).ShouldBe(shots);
        Path.GetFileName(written).ShouldStartWith("shot-");
        Path.GetExtension(written).ShouldBe(".png");
        rig.Session.CaptureFolder.ShouldBe(shots);
    }

    [Test]
    public void KeyPressed_AKeyNothingIsBoundTo_IsNotTaken()
    {
        using Rig rig = Build().WithDevice();

        rig.Session.KeyPressed(Key("F24", static _ => false)).ShouldBeFalse();

        rig.Device.Captured.ShouldBeEmpty();
        rig.Transport.Speeds.ShouldBeEmpty();
    }

    [Test]
    public void KeyPressed_AKeyboardKeyBoundToToggle_RunsItAndTakesTheKey()
    {
        // B547: a keyboard key reached the console only in the free camera, and only for flight; a mouse button
        // always did. The engine's `Key_Event` gives every key to its binding.
        using Rig rig = BuildWithAutoexec("bind \"F7\" \"toggle cl_showpos\"\n");

        rig.Session.KeyPressed(Key("F7", static _ => false)).ShouldBeTrue("a key whose bind ran is the bind's");
        rig.Session.KeyUp("F7").ShouldBeTrue("the bind took the press, so the release is the bind's too");
        rig.Settings.Current.ShowPosition.ShouldBe(1);
        rig.Log.Count("cl_showpos 1 (set at run time)").ShouldBe(1);

        rig.Session.KeyPressed(Key("F7", static _ => false)).ShouldBeTrue();
        rig.Settings.Current.ShowPosition.ShouldBe(0);
    }

    [Test]
    public void KeyPressed_AnAutoRepeat_RunsNothingButIsStillTaken()
    {
        // The engine binds a key's press, not Windows' repeat; a held toggle key would otherwise flicker. The repeat is
        // still the bind's, so a viewer shortcut on the same key cannot fire on it either.
        using Rig rig = BuildWithAutoexec("bind \"F7\" \"toggle cl_showpos\"\n");

        rig.Session.KeyPressed(Key("F7", static _ => false)).ShouldBeTrue();
        // ONE repeat: an even number of extra toggles would land back on 1 and hide a repeat that ran.
        rig.Session.KeyPressed(Key("F7", static _ => false, repeat: true)).ShouldBeTrue();

        rig.Settings.Current.ShowPosition.ShouldBe(1);
    }

    [Test]
    public void KeyPressed_WithControlHeld_IsNotTheBareKeysBind()
    {
        // Source's `bind` has no modifier syntax, so CTRL+F7 is not F7: the viewer's own CTRL chords live there.
        using Rig rig = BuildWithAutoexec("bind \"F7\" \"toggle cl_showpos\"\n");

        rig.Session.KeyPressed(Key("F7", static _ => false, controlOrAlt: true)).ShouldBeFalse();

        rig.Settings.Current.ShowPosition.ShouldBe(0);
    }

    [Test]
    public void KeyPressed_AViewerDefaultKeyReboundByTheConfig_RunsTheConfigsBindInstead()
    {
        // D101: the user's config wins a collision. HOME is the viewer's reset-speed default.
        using Rig rig = BuildWithAutoexec("bind \"HOME\" \"toggle cl_showpos\"\n");

        rig.Session.KeyPressed(Key("HOME")).ShouldBeTrue();

        rig.Settings.Current.ShowPosition.ShouldBe(1);
        rig.Transport.Speeds.ShouldBeEmpty();
    }

    [Test]
    public void KeyUp_AKeyAFocusedControlKept_RunsNoReleaseHalf()
    {
        // The engine releases only what it pressed (its keys keep their own down state): typing `x` in the search box
        // must not run `-x`.
        using Rig rig = BuildWithAutoexec("alias +x \"\"\nalias -x \"cl_showpos 1\"\nbind \"x\" \"+x\"\n");

        rig.Session.KeyPressed(Key("x", focus: FocusedWidget.Text)).ShouldBeFalse();
        rig.Session.KeyUp("x").ShouldBeFalse();

        rig.Settings.Current.ShowPosition.ShouldBe(0);
    }

    [Test]
    public void KeyUp_AKeyTheConsolePressed_RunsItsReleaseHalf()
    {
        // The control on the test above: the same bind, pressed with nothing focused.
        using Rig rig = BuildWithAutoexec("alias +x \"\"\nalias -x \"cl_showpos 1\"\nbind \"x\" \"+x\"\n");

        rig.Session.KeyPressed(Key("x")).ShouldBeTrue();
        rig.Session.KeyUp("x").ShouldBeTrue();

        rig.Settings.Current.ShowPosition.ShouldBe(1);
    }

    [Test]
    public void KeyPressed_EscapeBoundByTheConfigInFullScreen_StillLeavesAndRunsNoBind()
    {
        // Escape is the game UI's, ahead of the bindings, and here it is the way out of full screen.
        using Rig rig = BuildWithAutoexec("bind \"ESCAPE\" \"toggle cl_showpos\"\n");
        rig.Window.IsFullScreen = true;

        rig.Session.KeyPressed(Key("ESCAPE", static _ => false, escape: true)).ShouldBeTrue();

        rig.Window.Told.ShouldContain("LeaveFullScreen");
        rig.Settings.Current.ShowPosition.ShouldBe(0);
    }

    [Test]
    public void KeyPressed_ARepeatOfABoundKeyWithControlTapped_IsStillTheBinds()
    {
        // Hold F7, tap CTRL: the repeats arrive as CTRL+F7, and must not become the viewer's CTRL chord.
        using Rig rig = BuildWithAutoexec("bind \"F7\" \"toggle cl_showpos\"\n");

        rig.Session.KeyPressed(Key("F7", static _ => false)).ShouldBeTrue();

        bool shortcutAsked = false;

        rig.Session.KeyPressed(Key("F7", _ => shortcutAsked = true, controlOrAlt: true, repeat: true)).ShouldBeTrue();

        shortcutAsked.ShouldBeFalse("no viewer shortcut may even be considered for the bind's repeat");
    }

    [Test]
    public void KeyPressed_ALoneAltBoundByTheConfig_IsTakenAndItsReleaseIsTheBindsToo()
    {
        // Its release is what the toolkit turns into menu mode (B542); the window consumes it when this says so.
        using Rig rig = BuildWithAutoexec("bind \"ALT\" \"toggle cl_showpos\"\n");

        rig.Session.KeyPressed(Key("ALT", static _ => false, controlOrAlt: true)).ShouldBeTrue();
        rig.Session.KeyUp("ALT").ShouldBeTrue();

        rig.Settings.Current.ShowPosition.ShouldBe(1);
    }

    [Test]
    public void KeyPressed_EscapeInFullScreen_LeavesItAndSaysTheKeyArrived()
    {
        using Rig rig = Build();
        rig.Window.IsFullScreen = true;

        rig.Session.KeyPressed(Key("ESCAPE", static _ => false, escape: true)).ShouldBeTrue();

        rig.Window.Told.ShouldContain("LeaveFullScreen");
        rig.Log.Count("[render] ESCAPE reached the form; full screen is True").ShouldBe(1);
    }

    [Test]
    public void KeyPressed_EscapeWhileWindowed_IsNotTakenButIsStillLogged()
    {
        using Rig rig = Build();

        rig.Session.KeyPressed(Key("ESCAPE", static _ => false, escape: true)).ShouldBeFalse();

        rig.Window.Told.ShouldNotContain("LeaveFullScreen");
        rig.Log.Count("[render] ESCAPE reached the form; full screen is False").ShouldBe(1);
    }

    [Test]
    public void PointerMoved_WithNoMapDrawn_TurnsNothingWhetherOrNotAButtonIsDown()
    {
        // The camera refuses a drag until a map with an outline is drawn, and the synthetic map has none: so this holds
        // the refusal, and that a refused drag leaves the world as projected. The turn itself is `CameraPresenter.Drag`'s,
        // tested there; the distance handed to it needs a drawn map and is not reached by a unit test.
        using Rig rig = Build();
        rig.Session.Presenter.WorldProjected();
        (float Pitch, float Yaw) before = rig.Session.Presenter.Camera.Free.Angles;

        rig.Session.PointerMoved(50, 60);
        rig.Session.PointerDown("MOUSE1", drags: true, 10, 10);
        rig.Session.PointerMoved(30, 10);
        rig.Session.PointerUp("MOUSE1");

        rig.Session.Presenter.WorldNeedsProjecting.ShouldBeFalse();
        rig.Session.Presenter.Camera.Free.Angles.ShouldBe(before);
    }

    [Test]
    public void PointerDown_ANonDragButton_StartsNoDrag()
    {
        using Rig rig = Build();
        rig.Session.Presenter.WorldProjected();

        rig.Session.PointerDown("MOUSE2", drags: false, 10, 10);
        rig.Session.PointerMoved(200, 10);

        rig.Session.Presenter.WorldNeedsProjecting.ShouldBeFalse();
    }

    // ---- the idle loop ----

    [Test]
    public void Idle_WithNoFrameLimit_DrawsUntilAMessageWaitsAndSleepsForNothing()
    {
        using Rig rig = Build();
        FakeIdleHost host = new();
        host.Queue.Enqueue(null);
        host.Queue.Enqueue(null);
        host.Queue.Enqueue(0x0200);

        rig.Session.Idle(host);

        host.Looks.ShouldBe(3, "one look after each frame, and the third finds the mouse move");
        host.Sleeps.ShouldBeEmpty();
        host.Waits.ShouldBeEmpty("nothing ended a burst before this one");
    }

    [Test]
    public void Idle_WithoutFocusAndANoFocusSleepSet_SleepsThatLongBeforeEachFrame()
    {
        using Rig rig = Build(new ViewerSettings { FrameRateLimit = 0, NoFocusSleep = 50 });
        rig.Window.IsActiveApp = false;
        FakeIdleHost host = new();
        host.Queue.Enqueue(null);
        host.Queue.Enqueue(0x0200);

        rig.Session.Idle(host);

        host.Sleeps.ShouldBe([50, 50]);
    }

    /// <remarks>
    /// **A measurement is never clamped by the focus sleep, from wherever the value came** — the saved settings (50 here) or a
    /// `.cfg` executed after launch (the `SetConVar` below, the console's route). Held where the sleep is READ, so nothing set
    /// later can put it back; said once, not once a frame. The control is the test above: the same settings, no `--measure`, sleep.
    /// </remarks>
    [Test]
    public void Idle_WhileMeasuringWithANoFocusSleepSet_NeverSleepsAndSaysSoOnce()
    {
        using Rig rig = Build(new ViewerSettings { FrameRateLimit = 0, NoFocusSleep = 50 }, 0.5, "--measure", "5");
        rig.Window.IsActiveApp = false;
        rig.Settings.SetConVar(ViewerSettings.NoFocusSleepCommand, "50");
        FakeIdleHost host = new();
        host.Queue.Enqueue(null);
        host.Queue.Enqueue(0x0200);

        rig.Session.Idle(host);

        host.Sleeps.ShouldBeEmpty();
        rig.Log.Count("engine_no_focus_sleep 50 ignored while measuring").ShouldBe(1);
    }

    [Test]
    public void SetPhong_FromTheMenu_IsWhatTheMenuReadsBack()
    {
        // The menu re-reads its checks on opening (D215); a Phong the menu set must be what it reads, or opening
        // View would put the check back.
        using Rig rig = Build(new ViewerSettings { Phong = true });

        rig.Session.SetPhong(false);

        rig.Session.Menu.Phong.ShouldBeFalse();
    }

    [Test]
    public void Idle_WhileTheApplicationIsActive_DoesNotSleep()
    {
        // CEngine::Frame (engine.dll FUN_18020cbd0) sleeps on `!IsActiveApp()` alone: one of our own dialogs holding
        // the keyboard is still the active application, so the window's focus is not consulted at all.
        using Rig rig = Build(new ViewerSettings { FrameRateLimit = 0, NoFocusSleep = 50 });
        rig.Window.IsActiveApp = true;
        FakeIdleHost host = new();
        host.Queue.Enqueue(null);
        host.Queue.Enqueue(0x0200);

        rig.Session.Idle(host);

        host.Sleeps.ShouldBeEmpty();
    }

    [Test]
    public void Idle_BeforeTheNextFrameIsDue_WaitsRatherThanDraws()
    {
        // A clock stuck at one millisecond: the first frame is always allowed, and at 10 fps the second is 99 ms away,
        // far enough to sleep rather than spin.
        using Rig rig = Build(new ViewerSettings { FrameRateLimit = 10 }, frameSeconds: 0.001);
        FakeIdleHost host = new();
        host.Queue.Enqueue(null);
        host.Queue.Enqueue(null);
        host.Queue.Enqueue(0x0200);

        rig.Session.Idle(host);

        host.Sleeps.ShouldBe([1, 1], "the first pass draws; the two after it wait a millisecond each");
    }

    [Test]
    public void Idle_AfterAPaintEndedTheLastBurst_WaitsForTrafficAndYieldsWhenItComes()
    {
        using Rig rig = Build();
        FakeIdleHost host = new();
        host.Queue.Enqueue(0x000F);
        rig.Session.Idle(host);
        int looksAfterFirst = host.Looks;
        host.Waits.ShouldBeEmpty("the first burst follows nothing, so it waits for nothing");
        host.Arrives = true;

        rig.Session.Idle(host);

        host.Waits.ShouldBe([FramePacer.TrafficWaitSeconds]);
        (host.Looks - looksAfterFirst).ShouldBe(1, "it looks once to learn what arrived, and draws nothing");
    }

    [Test]
    public void Idle_AfterAKeyEndedTheLastBurst_DoesNotWaitForTraffic()
    {
        using Rig rig = Build();
        FakeIdleHost host = new();
        host.Queue.Enqueue(0x0100);
        rig.Session.Idle(host);
        host.Arrives = true;

        rig.Session.Idle(host);

        host.Waits.ShouldBeEmpty("input is answered with a frame, not a wait");
    }

    // ---- startup ----

    [Test]
    public async Task WindowShown_WithOneDemoNamed_OpensItAndKeepsTheLoad()
    {
        string demo = WriteDemo("named", ticks: 2000);
        using Rig rig = Build(demo);

        rig.Session.Paths.ShouldBe([demo]);
        rig.Session.PlanStartup();
        rig.Session.WindowShown();
        DemoLoadResult result = await rig.Session.Loading.ConfigureAwait(false);

        result.Loaded.ShouldBeTrue(result.Message);
        rig.Session.Presenter.DemoPath.ShouldBe(demo);
        rig.Transport.DemoLength.ShouldBe(2000);
    }

    [Test]
    public void WindowShown_AMissingFileOnACaptureRun_SaysWhichOnStderrExitsOneAndCloses()
    {
        string absent = Path.Combine(_folder, "absent.dem");
        using Rig rig = Build(absent, "--shot", Path.Combine(_folder, "out.png"));

        rig.Session.PlanStartup();
        rig.Log.Count($"[demo] {absent} is not a file that exists; nothing was opened").ShouldBe(1);
        _exits.ShouldBeEmpty("nothing ends before the window is up");

        rig.Session.WindowShown();

        rig.Error.ToString().Trim().ShouldBe($"{absent} is not a file that exists; nothing was opened");
        _exits.ShouldBe([1]);
        rig.Window.Told.ShouldContain("Close posted");
        rig.Log.Count($"[viewer] {absent} is not a file that exists; nothing was opened; the headless run stops").ShouldBe(1);
    }

    [Test]
    public void WindowShown_AMissingFileForAPerson_LeavesTheWindowOpen()
    {
        using Rig rig = Build(Path.Combine(_folder, "absent.dem"));

        rig.Session.PlanStartup();
        rig.Session.WindowShown();

        rig.Error.ToString().ShouldBeEmpty();
        _exits.ShouldBeEmpty();
        rig.Window.Count("Close").ShouldBe(0);
    }

    [Test]
    public void WindowShown_SeveralPathsOnAMeasureRun_SaysACaptureTakesOneAndStops()
    {
        string first = WriteDemo("first");
        string second = WriteDemo("second");
        using Rig rig = Build(first, second, "--measure", "5");

        rig.Session.PlanStartup();
        rig.Session.WindowShown();

        rig.Log.Count("[demo] 2 paths listed in the playlist; none opened on its own").ShouldBe(1);
        rig.Error.ToString().Trim().ShouldBe(
            $"2 paths were given where a capture takes one demo ({first} | {second}); a path with a space in it must arrive as ONE argument");
        _exits.ShouldBe([1]);
        rig.Session.Presenter.HasDemo.ShouldBeFalse();
    }

    [Test]
    public void PlanStartup_WithNoPaths_PlansNothingAndSaysNothing()
    {
        using Rig rig = Build();

        rig.Session.PlanStartup();
        rig.Session.WindowShown();

        rig.Log.Count("[demo]").ShouldBe(0);
        rig.Session.Loading.Result.Outcome.ShouldBe(DemoLoadOutcome.Superseded);
    }

    [Test]
    public void LoadSelected_WithNothingSelected_AnswersThatNoDemoIsSelected()
    {
        using Rig rig = Build();

        DemoLoadResult result = rig.Session.LoadSelected(null).Result;

        result.Outcome.ShouldBe(DemoLoadOutcome.Superseded);
        result.Message.ShouldBe("No demo is selected.");
    }

    [Test]
    public async Task Activate_ARow_LoadsItAndKeepsTheLoadWhereItCanBeAwaited()
    {
        string demo = WriteDemo("row");
        using Rig rig = Build();

        rig.Session.Activate(demo);

        (await rig.Session.Loading.ConfigureAwait(false)).Loaded.ShouldBeTrue();
        rig.Session.Presenter.DemoPath.ShouldBe(demo);
    }

    [Test]
    public void ReportStartup_WithNothingOpening_LogsTheBootTimeAndAddsItToTheStatus()
    {
        using Rig rig = Build();

        rig.Session.ReportStartup(TimeSpan.FromMilliseconds(1234));

        rig.Log.Count("[viewer] startup: window shown 1234 ms after the process started").ShouldBe(1);
        rig.Window.Status.ShouldBe("No demo loaded. Started in 1.23 s.");
    }

    [Test]
    public async Task ReportStartup_WithADemoOpen_LogsButLeavesTheDemosStatus()
    {
        using Rig rig = Build();
        (await rig.Session.Presenter.LoadDemoAsync(WriteDemo("open")).ConfigureAwait(false)).Loaded.ShouldBeTrue();
        string? before = rig.Window.Status;

        rig.Session.ReportStartup(TimeSpan.FromSeconds(2));

        rig.Window.Status.ShouldBe(before);
        rig.Log.Count("startup: window shown 2000 ms").ShouldBe(1);
    }

    [Test]
    public void ApplyLogVerbosity_WithAListener_HandsItTheSettingsLevelAndSaysSo()
    {
        using Rig rig = Build(new ViewerSettings { Developer = 1 });
        List<Microsoft.Extensions.Logging.LogLevel> set = [];
        rig.Session.SetLogVerbosity = set.Add;

        rig.Session.ApplyLogVerbosity();

        set.ShouldBe([Microsoft.Extensions.Logging.LogLevel.Debug]);
        rig.Log.Count("[viewer] developer 1; the log accepts Debug and above").ShouldBe(1);
    }

    [Test]
    public void ApplyLogVerbosity_WithNobodyListening_SaysNothing()
    {
        using Rig rig = Build();

        rig.Session.ApplyLogVerbosity();

        rig.Log.Count("the log accepts").ShouldBe(0);
    }

    [Test]
    public void ShouldAskForGameFolder_ForAPersonWithNoInstall_SaysNothingWasFoundAndAsks()
    {
        using Rig rig = Build();

        rig.Session.ShouldAskForGameFolder().ShouldBeTrue();

        rig.Log.Count("[viewer] " + ViewerSession.GameFolderLog + ViewerSession.GameFolderNotFound).ShouldBe(1);
    }

    [Test]
    public void ShouldAskForGameFolder_OnACaptureRun_SaysWhatWasFoundAndDoesNotAsk()
    {
        using Rig rig = Build(WriteDemo("shot"), "--shot", Path.Combine(_folder, "out.png"));

        rig.Session.ShouldAskForGameFolder().ShouldBeFalse("a dialog nobody sees would hang the run");

        rig.Log.Count(ViewerSession.GameFolderLog + ViewerSession.GameFolderNotFound).ShouldBe(1);
    }

    [Test]
    public void GameFolderChosen_AFolderThatIsNotTf2_SaysSoAndRereadsNoBindings()
    {
        using Rig rig = Build();

        rig.Session.GameFolderChosen(_folder);

        rig.Window.Status.ShouldNotBeNull().ShouldStartWith(SettingsPresenter.NotAGameFolder);
        rig.Window.Told.ShouldNotContain("ShowBindings");
    }

    [Test]
    public void GameFolderDeclined_AtStartup_PutsTheSettingsOwnersWordsInTheStatus()
    {
        using Rig rig = Build();

        rig.Session.GameFolderDeclined();

        rig.Window.Status.ShouldNotBeNullOrEmpty();
        rig.Window.Status.ShouldNotBe("No demo loaded.");
        rig.Session.ShouldAskForGameFolder().ShouldBeFalse("declining once stops the asking");
    }

    // ---- what a load asks ----

    [Test]
    public void AutoPlay_ByTheFlagByTheVariableAndByNeither_SaysWhichAsked()
    {
        using Rig flagged = Build("--autoplay");
        ((ILoadView)flagged.Session).AutoPlay.ShouldBe((true, "--autoplay"));

        using Rig variable = Build();
        _environment[ViewerSession.AutoPlayVariable] = "1";
        ((ILoadView)variable.Session).AutoPlay.ShouldBe((true, ViewerSession.AutoPlayVariable + " is set"));

        using Rig neither = Build();
        ((ILoadView)neither.Session).AutoPlay.Play.ShouldBeFalse();
    }

    [Test]
    public async Task DemoOpened_WithATimescaleOnTheCommandLine_SetsTheTransportsSpeed()
    {
        using Rig rig = Build("+demo_timescale", "2");

        (await rig.Session.Presenter.LoadDemoAsync(WriteDemo("fast")).ConfigureAwait(false)).Loaded.ShouldBeTrue();

        rig.Transport.Speeds.ShouldBe([2d]);
    }

    [Test]
    public async Task DemoOpened_WithNoTimescaleAsked_LeavesTheSpeedAlone()
    {
        using Rig rig = Build();

        (await rig.Session.Presenter.LoadDemoAsync(WriteDemo("plain")).ConfigureAwait(false)).Loaded.ShouldBeTrue();

        rig.Transport.Speeds.ShouldBeEmpty();
    }

    [Test]
    public void LoadViewMembers_StatusLoadingAndTheWorld_GoOnToTheWindowAndTheDevice()
    {
        using Rig rig = Build().WithDevice();
        ILoadView load = rig.Session;
        ICameraView camera = rig.Session;

        load.Status = "reading";
        load.Status.ShouldBe("reading");
        load.ReportLoading("Decoding", 0.5);
        load.HideLoading();
        load.ClearWorld();
        camera.Say(null);
        load.Status.ShouldBe(string.Empty, "a null status reads back as empty");
        rig.Session.Presenter.WorldProjected();
        int redraws = rig.Window.Count("Redraw");
        camera.CameraChanged();

        rig.Window.Told.ShouldContain("ReportLoading Decoding");
        rig.Window.Told.ShouldContain("HideLoading");
        rig.Device.Cleared.ShouldBe(1);
        rig.Session.Presenter.WorldNeedsProjecting.ShouldBeTrue();
        (rig.Window.Count("Redraw") - redraws).ShouldBe(1);
    }

    [Test]
    public async Task Scrubbing_WithADemoOpen_ShowsTheMomentAndAsksForARepaint()
    {
        using Rig rig = Build();
        rig.Transport.Scrub(5);
        rig.Window.Count("Redraw").ShouldBe(0, "with no demo a scrub draws nothing");
        (await rig.Session.Presenter.LoadDemoAsync(WriteDemo("scrubbed")).ConfigureAwait(false)).Loaded.ShouldBeTrue();
        int redraws = rig.Window.Count("Redraw");

        rig.Transport.Scrub(40);

        (rig.Window.Count("Redraw") - redraws).ShouldBe(1);
    }

    // ---- the playlist ----

    [Test]
    public void AddToLibrary_AFolderOfTwoDemos_ShowsTwoRowsThenSaysTheCount()
    {
        WriteDemo("alpha");
        WriteDemo("beta");
        using Rig rig = Build();

        rig.Session.AddToLibrary(string.Empty, _folder);

        rig.Window.Told.TakeLast(2).ShouldBe(["ShowPlaylist 2", "Status: 2 demos."]);
        rig.Session.PlaylistRow(0).ShouldNotBeNull().Name.ShouldBe("alpha.dem");
        rig.Session.PlaylistRow(1).ShouldNotBeNull().Path.ShouldBe(Path.Combine(_folder, "beta.dem"));
        rig.Session.PlaylistRow(2).ShouldBeNull();
    }

    [Test]
    public void FilterLibrary_ToOneName_ShowsOneRowAndLeavesTheStatus()
    {
        WriteDemo("alpha");
        WriteDemo("beta");
        using Rig rig = Build();
        rig.Session.AddToLibrary(string.Empty, _folder);
        string? status = rig.Window.Status;

        rig.Session.FilterLibrary("bet");

        rig.Window.Told[^1].ShouldBe("ShowPlaylist 1");
        rig.Session.PlaylistRow(0).ShouldNotBeNull().Name.ShouldBe("beta.dem");
        rig.Window.Status.ShouldBe(status);
    }

    // ---- export and compile ----

    [Test]
    public void OfferExport_WithNoDemoOpen_SaysToOpenOneAndOffersNothing()
    {
        using Rig rig = Build();

        rig.Session.OfferExport().ShouldBeNull();

        rig.Window.Status.ShouldBe("Open a demo first.");
        rig.Session.FileWork.Result.ShouldBe("Open a demo first.");
        rig.Log.Count("[viewer] export: pressed with no demo open").ShouldBe(1);
    }

    [Test]
    public async Task Export_ThenCompile_SayWhatEachIsDoingThenWhatItDidAndRebuildTheSameBytes()
    {
        string demo = WriteDemo("round");
        string text = Path.Combine(_folder, "round.txt");
        string rebuilt = Path.Combine(_folder, "rebuilt.dem");
        using Rig rig = Build();
        (await rig.Session.Presenter.LoadDemoAsync(demo).ConfigureAwait(false)).Loaded.ShouldBeTrue();

        rig.Session.OfferExport().ShouldBe("round.txt");
        rig.Session.Export(text);
        string exported = await rig.Session.FileWork.ConfigureAwait(false);
        ViewerSession.OfferCompile(text).ShouldBe("round.dem");
        rig.Session.Compile(text, rebuilt);
        string compiled = await rig.Session.FileWork.ConfigureAwait(false);

        rig.Window.Told.ShouldContain("Status: Exporting round.dem...");
        rig.Window.Told.ShouldContain("Status: Compiling round.txt...");
        exported.ShouldStartWith("Exported ");
        compiled.ShouldStartWith("Compiled ");
        rig.Window.Status.ShouldBe(compiled);
        rig.Log.Count("[viewer] " + exported).ShouldBe(1);
        (await File.ReadAllBytesAsync(rebuilt).ConfigureAwait(false)).ShouldBe(await File.ReadAllBytesAsync(demo).ConfigureAwait(false));
    }

    [Test]
    public async Task Compile_AFileThatIsNotThere_SaysFailedRatherThanThrowing()
    {
        using Rig rig = Build();

        rig.Session.Compile(Path.Combine(_folder, "absent.txt"), Path.Combine(_folder, "out.dem"));
        string result = await rig.Session.FileWork.ConfigureAwait(false);

        result.ShouldStartWith("Failed: ");
        rig.Window.Status.ShouldBe(result);
    }

    // ---- the capture ----

    private static void Frames(ViewerSession session, int count)
    {
        for (int frame = 0; frame < count; frame++)
        {
            session.RenderFrame().ShouldBeTrue();
        }
    }

    [Test]
    public async Task RenderFrame_OnACaptureRun_SeeksToTheTickThenCapturesOnceAndCloses()
    {
        string shot = Path.Combine(_folder, "out.png");
        using Rig rig = Build(WriteDemo("shot"), "--shot", shot, "--tick", "40").WithDevice();
        rig.Session.PlanStartup();
        rig.Session.WindowShown();
        (await rig.Session.Loading.ConfigureAwait(false)).Loaded.ShouldBeTrue();

        Frames(rig.Session, 44);
        rig.Device.Captured.ShouldBeEmpty("forty frames settle before the opening state, and forty-five more before the shot");
        Frames(rig.Session, 60);

        rig.Transport.ShownTicks.ShouldContain(40);
        rig.Session.PlaybackPosition.ShouldBe(40d, "the clock too, not just the transport: a reprojection re-reads the moment from it");
        rig.Log.Count("[viewer] opening state applied at tick 40").ShouldBe(1);
        rig.Log.Count("[viewer] first frame after the opening state draws the camera placed at tick 40").ShouldBe(1);
        rig.Device.Captured.ShouldBe([shot]);
        rig.Window.Count("Close posted").ShouldBe(1);
    }

    [Test]
    public async Task RenderFrame_OnACaptureRunWhileThePostProcessingWalks_WaitsAndSaysWhatItWaitsOn()
    {
        using Rig rig = Build(WriteDemo("walks"), "--shot", Path.Combine(_folder, "out.png")).WithDevice();
        rig.Session.PlanStartup();
        rig.Session.WindowShown();
        (await rig.Session.Loading.ConfigureAwait(false)).Loaded.ShouldBeTrue();
        Frames(rig.Session, 44);
        rig.Device.PostProcessingSettled = false;

        Frames(rig.Session, 200);

        rig.Device.Captured.ShouldBeEmpty();
        rig.Log.Count("[viewer] capture waiting on post-processing: bloom 1.00 of 0.50").ShouldBeGreaterThan(0);
        rig.Log.Count("capture waiting on post-processing").ShouldBeLessThan(5, "said once a second, not once a frame");

        rig.Device.PostProcessingSettled = true;
        Frames(rig.Session, 60);

        rig.Device.Captured.Count.ShouldBe(1);
    }

    [Test]
    public void RenderFrame_OnACaptureRunWhoseDemoNeverComes_GivesUpAfterItsPatienceAndWritesNothing()
    {
        using Rig rig = Build("--shot", Path.Combine(_folder, "out.png")).WithDevice();

        Frames(rig.Session, 30_000);
        rig.Window.Count("Close").ShouldBe(0);
        Frames(rig.Session, 2);

        rig.Log.Count("[viewer] no demo loaded after 30000 frames, so --shot wrote nothing").ShouldBe(1);
        rig.Device.Captured.ShouldBeEmpty();
        rig.Window.Count("Close posted").ShouldBe(1);
    }

    [Test]
    public async Task RenderFrame_WithColoursAndFirstPersonAsked_TicksTheColoursItemWhenTheOpeningStateApplies()
    {
        using Rig rig = Build(WriteDemo("colours"), "--colours", "--tick", "7");
        rig.Session.PlanStartup();
        rig.Session.WindowShown();
        (await rig.Session.Loading.ConfigureAwait(false)).Loaded.ShouldBeTrue();

        Frames(rig.Session, 60);

        rig.Window.Told.ShouldContain("ShowSurfaceColours True");
        rig.Log.Count("surface colours on").ShouldBe(1, "the tick reports back as a click would");
        rig.Log.Count("opening state applied at tick 7").ShouldBe(1);
        rig.Window.Count("Close").ShouldBe(0, "no shot was asked for, so nothing closes");
    }

    [Test]
    public async Task RenderFrame_WithSeekThroughSet_VisitsThoseTicksBeforeItsOwn()
    {
        using Rig rig = Build(WriteDemo("through"), "--tick", "9");
        _environment["TF2VIEW_SEEK_THROUGH"] = "600 50";
        rig.Session.PlanStartup();
        rig.Session.WindowShown();
        (await rig.Session.Loading.ConfigureAwait(false)).Loaded.ShouldBeTrue();
        int shownBefore = rig.Transport.ShownTicks.Count;

        Frames(rig.Session, 60);

        rig.Transport.ShownTicks.Skip(shownBefore).Take(3).ShouldBe([600, 50, 9]);
        rig.Log.Lines.Where(static line => line.StartsWith("[viewer] opening state", StringComparison.Ordinal)).ShouldBe(
        [
            "[viewer] opening state sought through tick 600",
            "[viewer] opening state sought through tick 50",
            "[viewer] opening state applied at tick 9",
        ]);
    }

    [Test]
    public void CaptureViewport_ABlankPath_IsRefused()
    {
        using Rig rig = Build().WithDevice();

        Should.Throw<ArgumentException>(() => rig.Session.CaptureViewport(" "));

        rig.Device.Captured.ShouldBeEmpty();
    }

    // ---- the measurement ----

    [Test]
    public async Task RenderFrame_OnAMeasureRun_PrintsTheSecondsTheFramesAndTheirMeanThenClosesAtOnce()
    {
        // Every frame takes the fixed clock's half second, so two seconds of playback is four counted frames.
        using Rig rig = Build(WriteDemo("measured"), "--measure", "2", "--autoplay");
        rig.Session.PlanStartup();
        rig.Session.WindowShown();
        (await rig.Session.Loading.ConfigureAwait(false)).Loaded.ShouldBeTrue();

        for (int frame = 0; frame < 20 && rig.Window.Count("Close") == 0; frame++)
        {
            rig.Session.RenderFrame();
        }

        string printed = rig.Out.ToString();
        printed.ShouldContain("measured 2 seconds of playback, ");
        printed.ShouldContain("; 4 frames, mean 500.00 ms");
        printed.ShouldContain("  phases, mean ms: sound ");
        rig.Window.Told.ShouldContain("Close now");
        rig.Window.Count("Close posted").ShouldBe(0);
    }

    [Test]
    public async Task RenderFrame_OnAMeasureRunWithAnOpeningTick_CountsNoFrameBeforeTheSeekAndTheOneAfterIt()
    {
        // Finding 82: the opening seek and the frame whose interval holds its tail are not playback. Every frame is the
        // fixed clock's half second, so two seconds is four counted frames, and the close comes two frames after the seek.
        using Rig rig = Build(WriteDemo("opened"), "--measure", "2", "--autoplay", "--tick", "7");
        rig.Session.PlanStartup();
        rig.Session.WindowShown();
        (await rig.Session.Loading.ConfigureAwait(false)).Loaded.ShouldBeTrue();

        int frames = 0;
        int? appliedAt = null;

        for (; frames < 200 && rig.Window.Count("Close") == 0; frames++)
        {
            rig.Session.RenderFrame();
            appliedAt ??= rig.Log.Count("opening state applied at tick 7") > 0 ? frames : null;
        }

        appliedAt.ShouldNotBeNull();
        frames.ShouldBe(appliedAt.Value + 1 + 1 + 4, "the seek frame, the one after it, then four counted");
        rig.Out.ToString().ShouldContain("; 4 frames, mean 500.00 ms");
        rig.Out.ToString().ShouldContain(", between ");
    }

    [Test]
    public async Task RenderFrame_OnAMeasureRunWithThenSeek_PrintsPlaybackThenPausesSeeksAndMeasuresAgain()
    {
        using Rig rig = Build(WriteDemo("twice"), "--measure", "1", "--then-seek", "300", "--autoplay");
        rig.Session.PlanStartup();
        rig.Session.WindowShown();
        (await rig.Session.Loading.ConfigureAwait(false)).Loaded.ShouldBeTrue();

        for (int frame = 0; frame < 40 && rig.Window.Count("Close") == 0; frame++)
        {
            rig.Session.RenderFrame();
        }

        string printed = rig.Out.ToString();
        printed.ShouldContain("measured 1 seconds of playback, ");
        printed.ShouldContain("measured 1 seconds paused at 300 after the seek, ");
        printed.IndexOf("of playback", StringComparison.Ordinal).ShouldBeLessThan(printed.IndexOf("paused at 300", StringComparison.Ordinal));
        rig.Transport.Playing.ShouldBeFalse();
        rig.Transport.ShownTicks.ShouldContain(300);
        rig.Window.Count("Close now").ShouldBe(1);
    }

    [Test]
    public void RenderFrame_WithNoDemoOnAMeasureRun_NeverCountsAFrameAndNeverCloses()
    {
        using Rig rig = Build("--measure", "1");

        Frames(rig.Session, 20);

        rig.Out.ToString().ShouldBeEmpty();
        rig.Window.Count("Close").ShouldBe(0);
    }

    [Test]
    public void FullScreenChanging_ThenAFrame_SaysHowLongTheChangeTookOnce()
    {
        using Rig rig = Build();
        rig.Window.IsFullScreen = true;

        rig.Session.FullScreenChanging();
        Frames(rig.Session, 2);

        rig.Log.Lines.Count(static line => line.StartsWith("[render] full screen on took ", StringComparison.Ordinal) && line.EndsWith(" ms to the first frame at 1280x720", StringComparison.Ordinal))
            .ShouldBe(1);
    }

    // ---- shutdown ----

    [Test]
    public void ReleaseServices_Once_AnnouncesTheMapsThenTheShutdownTokenAndASecondTimeNothing()
    {
        using Rig rig = Build();
        List<string> announced = [];

        rig.Session.StopBackgroundWork();
        rig.Session.ReleaseDevice();
        rig.Session.ReleaseServices(announced.Add);
        rig.Session.ReleaseServices(announced.Add);

        announced.ShouldBe(["maps", "shutdown"]);
    }

    [Test]
    public void Dispose_Twice_ReleasesOnce()
    {
        Rig rig = Build().WithDevice();

        rig.Session.Dispose();
        Should.NotThrow(rig.Session.Dispose);
        rig.Session.HasDevice.ShouldBeFalse();
        rig.Dispose();
    }
}
