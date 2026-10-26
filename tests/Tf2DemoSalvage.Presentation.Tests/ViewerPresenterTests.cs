using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Content.Tests.Bsp;
using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Diagnostics;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>
/// The owner of a load (D214) [no engine counterpart: the worker, the barrier and the window it asks are this
/// viewer's, B146], over a demo and a map the test wrote and a window that writes down what it was asked.
/// </summary>
/// <remarks>
/// **What the move could have changed and still finished**: the order the window is asked in, which side of the
/// marshal each call is on, and whether the render barrier is up around the map read. A paused frame shows none of the
/// three, so they are held here; the load's log on three real demos holds what the load MAKES.
///
/// **The demo is a header and a stop command, the map a BSP header with no lumps, and there is no install** — the
/// provider is told the machine has no game folder and finds the map in the viewer's own maps folder, which is the
/// salvage case (B211).
/// </remarks>
[NonParallelizable]
public sealed class ViewerPresenterTests
{
    private const string Map = "ctf_written_by_a_test";

    private string _folder = string.Empty;

    [SetUp]
    public void CreateFolder()
    {
        _folder = Path.Combine(Path.GetTempPath(), "tf2salvage-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(Path.Combine(_folder, "maps"));
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

    private string WriteDemo(string name, string map = Map, int ticks = 12345)
    {
        DemoHeader header = new()
        {
            DemoProtocol = 3,
            NetworkProtocol = 24,
            ServerName = "test server",
            ClientName = "tester",
            MapName = map,
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

    private void WriteMap() =>
        File.WriteAllBytes(Path.Combine(_folder, "maps", Map + ".bsp"), SyntheticBsp.Build(new Dictionary<int, byte[]>()));

    private (ViewerPresenter Loader, FakeLoadView View, FakePlaybackView Transport) Loader(string? spectateWho = null, ViewerSettings? settings = null)
    {
        FakeLoadView view = new();
        FakePlaybackView transport = new();
#pragma warning disable CA2000 // The provider opens nothing until a map is fetched, and no test here fetches one.
        MapProvider maps = new(
            Path.Combine(_folder, "no-steam-library.vdf"),
            Path.Combine(_folder, "maps"),
            static () => throw new InvalidOperationException("no test here downloads a map"),
            gameFolder: static () => null);
#pragma warning restore CA2000

        ViewerPresenter loader = new(
            view,
            NullLoggerFactory.Instance,
            view,
            () => view.Window,
            () => spectateWho,
            maps,
            new SettingsPresenter(NullLoggerFactory.Instance, settings ?? new ViewerSettings()),
            new PlaybackPresenter(transport, new FakeElapsedTime()),
            static _ => null,
            CancellationToken.None);

        view.Reading = () => loader.ReadingMap;

        return (loader, view, transport);
    }

    [Test]
    public void LoadDemo_ADemoWithItsMap_AsksTheWindowInTheLoadsOrder()
    {
        WriteMap();
        // A name nobody has: the camera says so while the demo is OPENING, which is the one thing a window sees of that step.
        (ViewerPresenter loader, FakeLoadView view, FakePlaybackView transport) = Loader(spectateWho: "nobody by this name");
        Dictionary<string, int> lengthWhen = [];
        view.Asked = name => lengthWhen[name] = transport.DemoLength;

        using (loader)
        {
            DemoLoadResult result = loader.LoadDemo(WriteDemo("order"));

            result.Loaded.ShouldBeTrue(result.Message);

            // A map with no faces has nothing to draw, which is what the status line's words mean.
            result.Message.ShouldEndWith("(map not found)");
            view.Names.ShouldBe(
            [
                nameof(ICameraView.FailHeadless),
                nameof(ILoadView.DemoOpened),
                nameof(ILoadView.ClearWorld),
                nameof(ILoadView.MapRead),
                "Status: " + result.Message,
                nameof(ILoadView.DemoShown),
            ]);

            // The demo's systems open BETWEEN the two: the transport has no length at the first and the demo's at the second.
            lengthWhen[nameof(ICameraView.FailHeadless)].ShouldBe(-1);
            lengthWhen[nameof(ILoadView.DemoOpened)].ShouldBe(12345);
        }
    }

    [Test]
    public void LoadDemo_ADemoWithItsMap_HoldsTheDemoItsMapAndTheInstall()
    {
        WriteMap();
        (ViewerPresenter loader, _, FakePlaybackView transport) = Loader();

        using (loader)
        {
            loader.Demo.ShouldBeNull();
            loader.Map.ShouldBeNull();
            loader.Game.ShouldBeNull("nothing has asked for the install yet");

            loader.LoadDemo(WriteDemo("held")).Loaded.ShouldBeTrue();

            loader.Demo.ShouldNotBeNull().MapName.ShouldBe(Map);
            loader.Timeline.ShouldNotBeNull();
            loader.Map.ShouldNotBeNull().Problem.ShouldBeNull();
            loader.Game.ShouldNotBeNull().Archives.IsEmpty.ShouldBeTrue("the machine has no game folder");
            transport.DemoLength.ShouldBe(12345, "the demo's systems were opened with its length");
            loader.ReadingMap.ShouldBeFalse();
            loader.LoadsInFlight.ShouldBe(0, "a load on this thread is never in flight");
        }
    }

    [Test]
    public void LoadDemo_WithNoMapAnywhere_OpensTheDemoAndSaysTheMapIsMissing()
    {
        (ViewerPresenter loader, FakeLoadView view, _) = Loader();

        using (loader)
        {
            DemoLoadResult result = loader.LoadDemo(WriteDemo("mapless"));

            result.Loaded.ShouldBeTrue("a demo plays without its map (B201)");
            result.Message.ShouldEndWith("(map not found)");
            loader.Demo.ShouldNotBeNull();
            loader.Map.ShouldBeNull();
            view.Names.ShouldContain("Status: " + ViewerPresenter.NoGameInstalled, "the missing install is mentioned, and the load carries on (B211)");
            view.Names.ShouldContain("Status: Map " + Map + " is not installed");
            view.Names.ShouldNotContain(nameof(ILoadView.MapRead));
            view.Names.ShouldContain(nameof(ILoadView.ClearWorld), "the old map goes whether or not a new one comes");
        }
    }

    [Test]
    public void LoadDemo_AFileThatIsNoDemo_ClosesTheOpenDemoAndSaysWhy()
    {
        WriteMap();
        (ViewerPresenter loader, FakeLoadView view, FakePlaybackView transport) = Loader();
        string broken = Path.Combine(_folder, "broken.dem");
        File.WriteAllText(broken, "version https://git-lfs.github.com/spec/v1");

        using (loader)
        {
            loader.LoadDemo(WriteDemo("first")).Loaded.ShouldBeTrue();

            DemoLoadResult result = loader.LoadDemo(broken);

            result.Outcome.ShouldBe(DemoLoadOutcome.Failed);
            result.Message.ShouldStartWith("Could not open broken.dem: ");
            view.Status.ShouldBe(result.Message);
            loader.Demo.ShouldBeNull("the demo that was open is closed, not left under a failure's words");
            loader.Timeline.ShouldBeNull();
            transport.DemoLength.ShouldBe(0, "closing a demo is opening none (B228)");
        }
    }

    [Test]
    public async Task LoadDemoAsync_ADemoWithItsMap_ReadsTheMapOffTheWindowsThreadBehindTheBarrier()
    {
        WriteMap();
        (ViewerPresenter loader, FakeLoadView view, _) = Loader();

        using (loader)
        {
            string path = WriteDemo("async");

            DemoLoadResult result = await loader.LoadDemoAsync(path).ConfigureAwait(false);

            result.Loaded.ShouldBeTrue(result.Message);
            view.Calls.ShouldBe(
            [
                ("Status: Opening async.dem...", false, false),
                ("Report: " + FakeLoadView.Decoding, false, false),
                ("Report: Loading the map", true, false),
                (nameof(ILoadView.ClearWorld), true, true),
                (nameof(ILoadView.MapRead), false, true),
                ("Report: Loading models and sounds", true, true),
                (nameof(ILoadView.DemoOpened), true, false),
                ("Status: " + result.Message, true, false),
                (nameof(ILoadView.DemoShown), true, false),
                (nameof(ILoadView.HideLoading), true, false),
            ]);
            loader.LoadsInFlight.ShouldBe(0);
        }
    }

    [Test]
    public async Task LoadDemoAsync_ANewerLoad_SupersedesTheOlderAndTakesTheProgressDownOnce()
    {
        WriteMap();
        (ViewerPresenter loader, FakeLoadView view, _) = Loader();
        view.Loads = 2;

        using (loader)
        {
            Task<DemoLoadResult> older = loader.LoadDemoAsync(WriteDemo("older", ticks: 111));
            Task<DemoLoadResult> newer = loader.LoadDemoAsync(WriteDemo("newer", ticks: 222));

            loader.LoadsInFlight.ShouldBe(2);

            DemoLoadResult overtaken = await older.ConfigureAwait(false);
            DemoLoadResult shown = await newer.ConfigureAwait(false);

            overtaken.ShouldBe(DemoLoadResult.Superseded(Path.Combine(_folder, "older.dem")));
            shown.Loaded.ShouldBeTrue(shown.Message);
            loader.Demo.ShouldNotBeNull().LastTick.ShouldBe(222);
            loader.LoadsInFlight.ShouldBe(0);
            view.Names.Count(static name => name == nameof(ILoadView.HideLoading)).ShouldBe(1, "only when no load is left in flight");
            view.Names.Count(static name => name == nameof(ILoadView.DemoOpened)).ShouldBe(1, "the overtaken demo was never opened");
        }
    }

    [Test]
    public void PlaySounds_WithNoDeviceOpened_NeverAsksForTheListener()
    {
        (ViewerPresenter loader, _, _) = Loader();
        int asked = 0;

        using (loader)
        {
            loader.PlaySounds(
                () =>
                {
                    asked++;

                    return Ears;
                },
                () =>
                {
                    asked++;

                    return null;
                },
                tick: 100);

            asked.ShouldBe(0, "the camera is the window's to work out, and a silent machine never needs it");
        }
    }

    [TestCase(true, 0.4f)]
    [TestCase(false, 0f)]
    public void PlaySoundsOver_WithAVolume_SetsTheSinksMasterGain(bool focused, float expected)
    {
        WriteMap();
        (ViewerPresenter loader, _, _) = Loader(settings: ViewerSettings.Parse("volume 0.4"));
        CountingSink sink = new();

        using (loader)
        {
            loader.LoadDemo(WriteDemo("loud")).Loaded.ShouldBeTrue();

            loader.PlaySoundsOver(sink, () => Ears, static () => null, tick: 100, focused);

            sink.MasterGain.ShouldBe(expected, "volume, or silence unfocused under snd_mute_losefocus 1");
        }
    }

    [Test]
    public void MusicVolumes_FromAConfigBeforeAnyFrame_AreTheConfigsForEverySoundSystem()
    {
        (ViewerPresenter loader, _, _) = Loader(settings: ViewerSettings.Parse("snd_musicvolume 0.25"));

        using (loader)
        {
            loader.MusicVolumes.ShouldBe((0.25f, 0.25f), "the first music sound must not play at 1 (B545)");
        }
    }

    [Test]
    public void PlaySoundsOver_WithNoListener_AsksForNoEyesAndStepsNothing()
    {
        WriteMap();
        (ViewerPresenter loader, _, _) = Loader();
        CountingSink sink = new();
        int eyes = 0;

        using (loader)
        {
            loader.LoadDemo(WriteDemo("deaf")).Loaded.ShouldBeTrue();

            loader.PlaySoundsOver(
                sink,
                static () => null,
                () =>
                {
                    eyes++;

                    return null;
                },
                tick: 100);

            eyes.ShouldBe(0);
            sink.Reclaims.ShouldBe(0, "no ears, so the sound presenter is not stepped");
        }
    }

    [Test]
    public void PlaySoundsOver_WithAListenerAndADemo_AsksForTheEyesAfterTheCameraAndStepsTheSoundOnce()
    {
        WriteMap();
        (ViewerPresenter loader, _, _) = Loader();
        CountingSink sink = new();
        List<string> asked = [];

        using (loader)
        {
            loader.LoadDemo(WriteDemo("heard")).Loaded.ShouldBeTrue();

            loader.PlaySoundsOver(
                sink,
                () =>
                {
                    asked.Add("camera");

                    return Ears;
                },
                () =>
                {
                    asked.Add("eyes");

                    return null;
                },
                tick: 100);

            asked.ShouldBe(["camera", "eyes"]);
            sink.Reclaims.ShouldBe(1, "one step of the sound presenter, which reclaims finished voices once");
        }
    }

    [Test]
    public void InvalidateWorld_ThenProjected_AsksForOneProjection()
    {
        (ViewerPresenter loader, _, _) = Loader();

        using (loader)
        {
            loader.WorldProjected();
            loader.WorldNeedsProjecting.ShouldBeFalse("the control: nothing has moved the view");

            loader.InvalidateWorld();
            loader.WorldNeedsProjecting.ShouldBeTrue();

            loader.WorldProjected();
            loader.WorldNeedsProjecting.ShouldBeFalse();
        }
    }

    /// <summary>A window's questions, counted, over a fixed frame.</summary>
    private sealed class Questions
    {
        public List<double> Shown { get; } = [];

        public int Frames { get; private set; }

        public int Scores { get; private set; }

        public int Clock { get; private set; }

        public ViewerWindow Window(ViewerPresenter presenter) =>
            new(
                () =>
                {
                    Frames++;

                    return new WindowFrame(12, 12d, false, 1280, 720, 0.016f, 0d);
                },
                shown =>
                {
                    Shown.Add(shown);
                    presenter.Show(shown, playing: false);
                },
                static () => new KeyBindings(),
                static _ => null,
                static () => 5d,
                () =>
                {
                    Scores++;

                    return false;
                },
                () =>
                {
                    Clock++;

                    return 0.016;
                });
    }

    [Test]
    public void HasDemo_BeforeAndAfterALoadAndAfterAFailedOne_FollowsTheLoad()
    {
        WriteMap();
        (ViewerPresenter loader, _, _) = Loader();
        string broken = Path.Combine(_folder, "broken.dem");
        File.WriteAllText(broken, "version https://git-lfs.github.com/spec/v1");

        using (loader)
        {
            loader.HasDemo.ShouldBeFalse();
            loader.DemoPath.ShouldBeNull();
            loader.HasTimeline.ShouldBeFalse();

            string path = WriteDemo("asked");
            loader.LoadDemo(path).Loaded.ShouldBeTrue();

            loader.HasDemo.ShouldBeTrue();
            loader.DemoPath.ShouldBe(path);
            loader.HasTimeline.ShouldBeTrue();

            loader.LoadDemo(broken);

            loader.HasDemo.ShouldBeFalse();
            loader.DemoPath.ShouldBeNull();
        }
    }

    [Test]
    public void BuildOverlay_BeforeTheWindowIsAttached_SaysTheWiringIsMissing()
    {
        (ViewerPresenter loader, _, _) = Loader();

        using (loader)
        {
            Should.Throw<InvalidOperationException>(() => loader.BuildOverlay())
                .Message.ShouldContain("the window was not attached");
        }
    }

    [Test]
    public void ShowMoment_WithNoDemoOpen_ShowsNothingAndAsksTheWindowNothing()
    {
        (ViewerPresenter loader, _, _) = Loader();
        Questions asked = new();

        using (loader)
        {
            loader.AttachWindow(asked.Window(loader), new FpsPanelTests.SolidGdi());

            loader.ShowMoment(12d);

            asked.Shown.ShouldBeEmpty();
            asked.Frames.ShouldBe(0);
        }
    }

    [Test]
    public void ShowMoment_WithADemoOpen_ComesBackThroughTheWindowToShowTheTickAsked()
    {
        WriteMap();
        (ViewerPresenter loader, _, _) = Loader();
        Questions asked = new();

        using (loader)
        {
            loader.AttachWindow(asked.Window(loader), new FpsPanelTests.SolidGdi());
            loader.LoadDemo(WriteDemo("shown")).Loaded.ShouldBeTrue();

            loader.ShowMoment(12d);

            asked.Shown.ShouldNotBeEmpty();
            asked.Shown[^1].ShouldBe(12d, "the last tick shown is the one asked for; the ones before are the seek's");
            asked.Frames.ShouldBeGreaterThan(0, "the frame record is built from what the window says now");
        }
    }

    [Test]
    public void BuildOverlay_WithTheWindowAttached_AsksItForTheFrameTheScoreboardKeyAndTheClock()
    {
        WriteMap();
        (ViewerPresenter loader, _, _) = Loader();
        Questions asked = new();

        using (loader)
        {
            loader.AttachWindow(asked.Window(loader), new FpsPanelTests.SolidGdi());
            loader.LoadDemo(WriteDemo("overlay")).Loaded.ShouldBeTrue();
            int framesBefore = asked.Frames;

            loader.BuildOverlay();

            (asked.Frames - framesBefore).ShouldBeGreaterThan(0);
            asked.Scores.ShouldBe(1);
            asked.Clock.ShouldBe(1);
        }
    }

    [Test]
    public void ExportDemo_ThenCompileDemo_SayHowMuchAndRebuildTheSameBytes()
    {
        string demo = WriteDemo("round");
        string text = Path.Combine(_folder, "round.txt");
        string rebuilt = Path.Combine(_folder, "rebuilt.dem");

        string exported = ViewerPresenter.ExportDemo(demo, text);
        string compiled = ViewerPresenter.CompileDemo(text, rebuilt);

        exported.ShouldStartWith("Exported ");
        exported.ShouldEndWith(" commands to " + text);
        compiled.ShouldStartWith("Compiled ");
        compiled.ShouldEndWith($" commands to {rebuilt} ({new FileInfo(rebuilt).Length:N0} bytes)");
        File.ReadAllBytes(rebuilt).ShouldBe(File.ReadAllBytes(demo));
    }

    [Test]
    public void AttachDecodeLog_ASinkAndANoteTaker_GetCoresLossesAndItsNotesApart()
    {
        Action<string, string>? sinkBefore = DecodeLog.Sink;
        Action<string, string>? notesBefore = DecodeLog.Notes;
        List<string> lost = [];
        List<string> noted = [];

        try
        {
            ViewerPresenter.AttachDecodeLog((area, message) => lost.Add(area + ": " + message), (area, message) => noted.Add(area + ": " + message));

            DecodeLog.Lost("demo", "a packet");
            DecodeLog.Note("demo", "a count");

            lost.ShouldBe(["demo: a packet"]);
            noted.ShouldBe(["demo: a count"]);
        }
        finally
        {
            DecodeLog.Sink = sinkBefore;
            DecodeLog.Notes = notesBefore;
        }
    }

    /// <remarks>
    /// **The pose comes between the camera and the effects** (D221): `view->OnRenderStart()` places the view models
    /// (cdll_client_int.cpp:2184) before `ParticleMgr()->Simulate` reads any attachment (`:2234`). A spy on the pose marks it.
    /// </remarks>
    [Test]
    public void UploadCameraOver_AFrame_SetsTheDemosFeedsThenTheExposureThenTheCameraThenPosesThenTheEffects()
    {
        WriteMap();
        (ViewerPresenter loader, _, _) = Loader();
        Questions asked = new();
        FakeViewerDevice device = new();
        FakeFrameDevice effects = new() { Told = device.Mark };

        using (loader)
        {
            loader.AttachWindow(asked.Window(loader), new FpsPanelTests.SolidGdi());
            loader.LoadDemo(WriteDemo("camera")).Loaded.ShouldBeTrue();

            loader.Posed = () => device.Mark("posed");
            loader.UploadCameraOver(device, effects, flyTicks: 0, exposureSeconds: 0.25f, surfaceColours: true);

            device.Set.ShouldBe(
            [
                "WorldFog none", "SkyFog none", "MonitorCamera none", "Tonemap default",
                "ExposureSeconds 0.25", "ObserverTarget nobody",
                nameof(IViewerDevice.SetCamera),
                nameof(IViewerDevice.FinishDrawBeforePose),
                "DrawsMonitor read True",
                "posed",
                nameof(IFrameDevice.SetParticles),
            ]);
            device.Camera.SurfaceColours.ShouldBeTrue();
            device.Camera.Camera.ShouldNotBeNull();
        }
    }

    /// <remarks>
    /// **Rung 2's contract (B581, D228): SetCamera overlaps the frame in flight, the pose does not.** The cull writes only
    /// its own view half, so it may run while the previous frame draws from the other; the pose rewrites bones, flex and
    /// instance lists the draw reads, so the drain sits at its entry. Deterministic: the in-flight draw is held on a gate
    /// only the drain opens. The drain is <see cref="IViewerDevice.FinishDrawBeforePose"/>, at the pose's entry (finding 98
    /// §9 step 5); before it moved there, this read `posed in flight True`.
    /// </remarks>
    [Test]
    public void UploadCameraOver_InModeTwoWithAFrameInFlight_OverlapsSetCameraButNotThePose()
    {
        WriteMap();
        (ViewerPresenter loader, _, _) = Loader();
        Questions asked = new();
        using System.Threading.ManualResetEventSlim gate = new();
        using Render.QueuedRenderThread queue = new() { Mode = Render.MaterialThreadMode.QueuedThreaded };
        FakeViewerDevice device = new() { Queue = queue };

        queue.Waiting = _ => gate.Set();

        using (loader)
        {
            loader.AttachWindow(asked.Window(loader), new FpsPanelTests.SolidGdi());
            loader.LoadDemo(WriteDemo("camera")).Loaded.ShouldBeTrue();

            try
            {
                queue.Submit(gate.Wait);
                loader.Posed = () => device.Mark($"posed in flight {queue.InFlight}");
                loader.UploadCameraOver(device, new FakeFrameDevice(), flyTicks: 0, exposureSeconds: 0f, surfaceColours: false);

                device.Set.ShouldContain("SetCamera in flight True", "the control: the frame was in flight at the cull");
                device.Set.ShouldContain("posed in flight False");
            }
            finally
            {
                gate.Set();
            }
        }
    }

    /// <remarks>
    /// **`camera setcamera` is split by the device's own timers, carried** (B243): the parts the device measured inside
    /// its `SetCamera` reach the rate line under `camera setcamera` and the part's name, as the device timed them, never re-timed here.
    /// </remarks>
    [Test]
    public void UploadCameraOver_WithTheDevicesSetCameraParts_ReportsEachUnderSetCamera()
    {
        WriteMap();
        (ViewerPresenter loader, _, _) = Loader();
        Questions asked = new();
        long threeMs = System.Diagnostics.Stopwatch.Frequency * 3 / 1000;
        FakeViewerDevice device = new() { SetCameraParts = [("cull", threeMs), ("detail", threeMs * 2)] };
        FrameParts parts = new();

        using (loader)
        {
            loader.AttachWindow(asked.Window(loader), new FpsPanelTests.SolidGdi());
            loader.LoadDemo(WriteDemo("camera")).Loaded.ShouldBeTrue();
            loader.FrameParts = parts;

            loader.UploadCameraOver(device, new FakeFrameDevice(), flyTicks: 0, exposureSeconds: 0f, surfaceColours: false);
            parts.Frame();

            string report = parts.Report();
            report.ShouldContain("camera setcamera cull 3,");
            report.ShouldContain("camera setcamera detail 6");
        }
    }

    /// <remarks>A sidecar's camera is the one the device was handed, carried, never a second reading (D226, B243).</remarks>
    [Test]
    public void LastViewing_AfterAFrame_IsTheCameraTheDeviceWasGiven()
    {
        WriteMap();
        (ViewerPresenter loader, _, _) = Loader();
        Questions asked = new();
        FakeViewerDevice device = new();

        using (loader)
        {
            loader.AttachWindow(asked.Window(loader), new FpsPanelTests.SolidGdi());
            loader.LoadDemo(WriteDemo("camera")).Loaded.ShouldBeTrue();
            loader.LastViewing.ShouldBeNull("no frame has been drawn");

            loader.UploadCameraOver(device, new FakeFrameDevice(), flyTicks: 0, exposureSeconds: 0f, surfaceColours: false);

            loader.LastViewing.ShouldBeSameAs(device.Camera.Camera);
        }
    }

    /// <remarks>
    /// **The pose asks the device whether a monitor draws, before posing and once** (B511). What the answer does to the
    /// followed player is `MomentSceneTests.Pose_InFirstPersonWithAMonitorOrNot_BuildsTheFollowedPlayerOnlyForOne`; this is
    /// the hop between the two, both ways.
    /// </remarks>
    [TestCase(true)]
    [TestCase(false)]
    public void UploadCameraOver_AFrame_AsksWhetherAMonitorDrawsBeforePosing(bool monitor)
    {
        WriteMap();
        (ViewerPresenter loader, _, _) = Loader();
        Questions asked = new();
        FakeViewerDevice device = new() { DrawsMonitor = monitor };

        using (loader)
        {
            loader.AttachWindow(asked.Window(loader), new FpsPanelTests.SolidGdi());
            loader.LoadDemo(WriteDemo("monitor")).Loaded.ShouldBeTrue();

            loader.Posed = () => device.Mark("posed");
            loader.UploadCameraOver(device, new FakeFrameDevice(), flyTicks: 0, exposureSeconds: 0f, surfaceColours: false);

            int read = device.Set.ToList().IndexOf($"DrawsMonitor read {monitor}");
            read.ShouldBeGreaterThanOrEqualTo(0);
            device.Set.ToList().IndexOf("posed").ShouldBe(read + 1);
            device.Set.Count(line => line.StartsWith("DrawsMonitor read", StringComparison.Ordinal)).ShouldBe(1);
        }
    }

    [Test]
    public void DrawSceneOver_WithAnOverlay_SetsTheScreenOverlayAndMonitorModelsThenDrawsWithThatOverlay()
    {
        WriteMap();
        (ViewerPresenter loader, _, _) = Loader();
        FakeViewerDevice device = new();
        FrameOverlay overlay = new(new Tf2DemoSalvage.Scene.Hud.VguiDrawList(static _ => (0, 0)));

        using (loader)
        {
            loader.LoadDemo(WriteDemo("draw")).Loaded.ShouldBeTrue();

            loader.DrawSceneOver(device, 0.1f, 0.2f, 0.3f, leafVis: false, overlay);

            device.Set.ShouldBe(["ScreenOverlay none", "MonitorOnlyModels 0", nameof(IViewerDevice.DrawScene)]);
            device.Drawn.Vgui.ShouldBeSameAs(overlay.List);
            (device.Drawn.Red, device.Drawn.Green, device.Drawn.Blue).ShouldBe((0.1f, 0.2f, 0.3f));
            device.Drawn.Lines.ShouldBe(0, "mat_leafvis is off");
            device.Drawn.ViewmodelCamera.ShouldBeNull("nobody's eyes: no viewmodel pass");
            device.DetailFovFactor.ShouldBe(1f, "B580: the factor is handed over each frame; an unzoomed demo's is 1");
        }
    }

    private static readonly FreeCamera Ears = new() { Origin = (0f, 0f, 0f), Angles = (0f, 0f, 0f) };

    /// <summary>A sink that counts the sound presenter's steps: each reclaims finished voices exactly once.</summary>
    private sealed class CountingSink : IAudioSink
    {
        public float MasterGain { get; set; } = 1f;

        public int Reclaims { get; private set; }

        // The written demo carries no sound; nothing is played.
        public int Play(SoundSample sample, float leftPan, float rightPan, float gain, float pitch, int entity, int channel) => 0;

        public bool SetVoiceGain(int voice, float gain) => false;

        public bool SetVoicePitch(int voice, float pitch) => false;

        public bool SetGain(int entity, int channel, float gain) => false;

        public bool SetPitch(int entity, int channel, float pitch) => false;

        public void SilenceAll()
        {
            // Nothing is held to silence.
        }

        public int Reclaim()
        {
            Reclaims++;

            return 0;
        }

        public void Silence(int entity, int channel)
        {
            // Nothing is tracked per channel here.
        }

        public int Silence(int entity, int channel, SoundSample sample) => 0;
    }
}
