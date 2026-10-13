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
public sealed class LoadPresenterTests
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

    private (LoadPresenter Loader, FakeLoadView View, FakePlaybackView Transport) Loader()
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

        LoadPresenter loader = new(
            view,
            NullLoggerFactory.Instance,
            new FrameLedger(),
            new ViewmodelScene(),
            new SpectatorView(NullLogger.Instance),
            maps,
            static () => new ViewerSettings(),
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
        (LoadPresenter loader, FakeLoadView view, FakePlaybackView transport) = Loader();
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
                nameof(ILoadView.DemoOpening),
                nameof(ILoadView.DemoOpened),
                nameof(ILoadView.ClearWorld),
                nameof(ILoadView.MapReading),
                nameof(ILoadView.MapRead),
                "Status: " + result.Message,
                nameof(ILoadView.DemoShown),
            ]);

            // The demo's systems open BETWEEN the two: the transport has no length at the first and the demo's at the second.
            lengthWhen[nameof(ILoadView.DemoOpening)].ShouldBe(-1);
            lengthWhen[nameof(ILoadView.DemoOpened)].ShouldBe(12345);
        }
    }

    [Test]
    public void LoadDemo_ADemoWithItsMap_HoldsTheDemoItsMapAndTheInstall()
    {
        WriteMap();
        (LoadPresenter loader, _, FakePlaybackView transport) = Loader();

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
        (LoadPresenter loader, FakeLoadView view, _) = Loader();

        using (loader)
        {
            DemoLoadResult result = loader.LoadDemo(WriteDemo("mapless"));

            result.Loaded.ShouldBeTrue("a demo plays without its map (B201)");
            result.Message.ShouldEndWith("(map not found)");
            loader.Demo.ShouldNotBeNull();
            loader.Map.ShouldBeNull();
            view.Names.ShouldContain("Status: " + LoadPresenter.NoGameInstalled, "the missing install is mentioned, and the load carries on (B211)");
            view.Names.ShouldContain("Status: Map " + Map + " is not installed");
            view.Names.ShouldNotContain(nameof(ILoadView.MapRead));
            view.Names.ShouldContain(nameof(ILoadView.ClearWorld), "the old map goes whether or not a new one comes");
        }
    }

    [Test]
    public void LoadDemo_AFileThatIsNoDemo_ClosesTheOpenDemoAndSaysWhy()
    {
        WriteMap();
        (LoadPresenter loader, FakeLoadView view, FakePlaybackView transport) = Loader();
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
        (LoadPresenter loader, FakeLoadView view, _) = Loader();

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
                (nameof(ILoadView.MapReading), false, true),
                (nameof(ILoadView.MapRead), false, true),
                ("Report: Loading models and sounds", true, true),
                (nameof(ILoadView.DemoOpening), true, false),
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
        (LoadPresenter loader, FakeLoadView view, _) = Loader();
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
            view.Names.Count(static name => name == nameof(ILoadView.DemoOpening)).ShouldBe(1, "the overtaken demo was never opened");
        }
    }

    [Test]
    public void PlaySounds_WithNoDeviceOpened_NeverAsksForTheListener()
    {
        (LoadPresenter loader, _, _) = Loader();
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

    [Test]
    public void PlaySoundsOver_WithNoListener_AsksForNoEyesAndStepsNothing()
    {
        WriteMap();
        (LoadPresenter loader, _, _) = Loader();
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
        (LoadPresenter loader, _, _) = Loader();
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

    private static readonly FreeCamera Ears = new() { Origin = (0f, 0f, 0f), Angles = (0f, 0f, 0f) };

    /// <summary>A sink that counts the sound presenter's steps: each reclaims finished voices exactly once.</summary>
    private sealed class CountingSink : IAudioSink
    {
        public int Reclaims { get; private set; }

        public void Play(SoundSample sample, float leftPan, float rightPan, float gain, float pitch, int entity, int channel)
        {
            // The written demo carries no sound; nothing is played.
        }

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

        public void Silence(int entity, int channel, SoundSample sample)
        {
            // As above.
        }
    }
}
