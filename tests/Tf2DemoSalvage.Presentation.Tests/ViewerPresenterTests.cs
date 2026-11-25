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

    private (ViewerPresenter Loader, FakeLoadView View, FakePlaybackView Transport) Loader(string? spectateWho = null, ViewerSettings? settings = null) =>
        Loader(new SettingsPresenter(NullLoggerFactory.Instance, settings ?? new ViewerSettings(), Path.Combine(_folder, "settings.cfg")), spectateWho);

    private (ViewerPresenter Loader, FakeLoadView View, FakePlaybackView Transport) Loader(
        SettingsPresenter settingsOwner, string? spectateWho = null, Microsoft.Extensions.Logging.ILoggerFactory? loggers = null)
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
            loggers ?? NullLoggerFactory.Instance,
            view,
            () => view.Window,
            () => spectateWho,
            maps,
            settingsOwner,
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
            result.Message.ShouldEndWith("(" + ViewerPresenter.MapNotFound + ")");
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

    /// <remarks>
    /// **The install holds the HUD choice the HUD and the level's preload both read** (B779 review 20 item 1;
    /// `GameContent.ChosenHud`): a choice made before the install opens reaches it when it opens, and one made after reaches
    /// the open install. The control: a different choice after the load replaced the first.
    /// </remarks>
    [Test]
    public void SetChosenHud_BeforeAndAfterTheInstallOpens_IsTheInstallsChoice()
    {
        WriteMap();
        (ViewerPresenter loader, _, _) = Loader();

        using (loader)
        {
            loader.SetChosenHud("custom/before");
            loader.LoadDemo(WriteDemo("hud")).Loaded.ShouldBeTrue();
            GameContent game = loader.Game.ShouldNotBeNull();
            game.ChosenHud.ShouldBe("custom/before", "chosen before the install opened");

            loader.SetChosenHud("custom/after");

            game.ChosenHud.ShouldBe("custom/after");
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

            // The last status keeps both reasons the read gave, rather than a bare "(map not found)" over them (B556).
            // The map's own reason first, so a clipped status bar still shows it, then the install.
            result.Message.ShouldContain("(Map " + Map + " is not installed; " + ViewerPresenter.NoGameInstalled + ")");
            result.Message.ShouldNotContain(ViewerPresenter.MapNotFound);
            loader.Demo.ShouldNotBeNull();
            loader.Map.ShouldBeNull();
            view.Names.ShouldContain("Status: " + ViewerPresenter.NoGameInstalled, "the missing install is mentioned, and the load carries on (B211)");
            view.Names.ShouldContain("Status: Map " + Map + " is not installed");
            view.Names.ShouldNotContain(nameof(ILoadView.MapRead));
            view.Names.ShouldContain(nameof(ILoadView.ClearWorld), "the old map goes whether or not a new one comes");
        }
    }

    /// <remarks>
    /// A map that is here but cannot be read says why at the read, and the load's last status still says so after it
    /// (B556): the bare "(map not found)" that replaced it would send the reader to download a map they already have.
    /// </remarks>
    [Test]
    public void LoadDemo_AMapThatCannotBeRead_EndsSayingWhy()
    {
        File.WriteAllBytes(Path.Combine(_folder, "maps", Map + ".bsp"), [0x42, 0x41, 0x44]);
        (ViewerPresenter loader, _, _) = Loader();

        using (loader)
        {
            DemoLoadResult result = loader.LoadDemo(WriteDemo("unreadable"));

            result.Loaded.ShouldBeTrue("a demo plays without its map (B201)");
            result.Message.ShouldContain("Map " + Map + " could not be read: ");
            result.Message.ShouldNotContain(ViewerPresenter.MapNotFound);
        }
    }

    /// <remarks>Each load says only its own map's reasons: a second load whose map reads leaves nothing of the first's.</remarks>
    [Test]
    public void LoadDemo_AfterAMaplessLoad_ANextLoadWithItsMapSaysNothingOfTheFirst()
    {
        (ViewerPresenter loader, _, _) = Loader();

        using (loader)
        {
            loader.LoadDemo(WriteDemo("first", map: "ctf_nowhere")).Message.ShouldContain("ctf_nowhere is not installed");

            WriteMap();
            DemoLoadResult second = loader.LoadDemo(WriteDemo("second"));

            second.Message.ShouldNotContain("ctf_nowhere");
            second.Message.ShouldNotContain(ViewerPresenter.NoGameInstalled);
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

    /// <remarks>
    /// **One map read at a time** (B779 review 22 item 1): a second load asked while the first is reading its map — the
    /// user opening demo B mid-read — waits for the first's read, precache and `Apply` before it clears the map or touches
    /// the entity set, as the engine loads one level at a time. Here demo B is started from inside A's read (its `MapRead`),
    /// and A's read holds until B has either queued behind it or cleared the world. **And A's last marshal — the one that
    /// lowers the barrier and runs its `Apply` — begins with B still waiting on it** (review 23 item 5): the queue is
    /// released in that marshal, so B cannot clear the world before A's `Apply`. Then the window's barrier calls come in
    /// two whole runs, A's then B's, each behind the barrier. The controls: B did start a read of its own (two clears), and
    /// A was superseded by B.
    /// </remarks>
    [Test]
    public async Task LoadDemoAsync_ASecondLoadDuringAMapRead_WaitsForTheFirstRead()
    {
        WriteMap();
        (ViewerPresenter loader, FakeLoadView view, _) = Loader();
        string first = WriteDemo("first");
        string second = WriteDemo("second");
        view.Loads = 2;
        Task<DemoLoadResult>? secondLoad = null;
        int clearsWhenQueued = -1;
        bool aReportedModels = false;
        bool? bWaitingAtALastMarshal = null;

        using (loader)
        {
            // A's last marshal is the first after its models report: the precaches between them ask the window nothing.
            view.Marshalling = () =>
            {
                if (aReportedModels && bWaitingAtALastMarshal is null)
                {
                    bWaitingAtALastMarshal = loader.MapReadWaiting;
                }
            };
            view.Asked = name =>
            {
                if (name == "Report: Loading models and sounds" && secondLoad is not null)
                {
                    aReportedModels = true;
                }

                if (name != nameof(ILoadView.MapRead) || secondLoad is not null)
                {
                    return;
                }

                secondLoad = loader.LoadDemoAsync(second);
                SpinWait.SpinUntil(
                    () => loader.MapReadWaiting || view.Names.Count(static n => n == nameof(ILoadView.ClearWorld)) > 1,
                    TimeSpan.FromSeconds(30))
                    .ShouldBeTrue("B queued behind A's read, or cleared the world beside it");
                clearsWhenQueued = view.Names.Count(static n => n == nameof(ILoadView.ClearWorld));
            };

            DemoLoadResult firstResult = await loader.LoadDemoAsync(first).ConfigureAwait(false);
            DemoLoadResult secondResult = await secondLoad.ShouldNotBeNull().ConfigureAwait(false);
            view.Asked = null;
            view.Marshalling = null;

            clearsWhenQueued.ShouldBe(1, "B did not clear the world while A read");
            bWaitingAtALastMarshal.ShouldBe(true, "B still waited on A when A's Apply marshal began");
            List<(string Name, bool OnUi, bool Reading)> barrier =
            [
                .. view.Calls.Where(static call => call.Name is nameof(ILoadView.ClearWorld) or nameof(ILoadView.MapRead)
                    || call.Name == "Report: Loading models and sounds"),
            ];
            barrier.Select(static call => call.Name).ShouldBe(
            [
                nameof(ILoadView.ClearWorld), nameof(ILoadView.MapRead), "Report: Loading models and sounds",
                nameof(ILoadView.ClearWorld), nameof(ILoadView.MapRead), "Report: Loading models and sounds",
            ]);
            barrier.ShouldAllBe(static call => call.Reading, "every barrier call behind the barrier");
            (firstResult.Loaded, secondResult.Loaded).ShouldBe((false, true), "the control: A was superseded by B");
        }
    }

    private const string DoorsA = "models/hud_a/doors.mdl";
    private const string DoorsB = "models/hud_b/doors.mdl";

    /// <summary>A HUD folder whose `HudMatchStatus.res` names these doors.</summary>
    private string HudWithDoors(string name, string doors)
    {
        string ui = Directory.CreateDirectory(Path.Combine(_folder, name, "resource", "UI")).FullName;
        File.WriteAllText(
            Path.Combine(ui, "HudMatchStatus.res"),
            $$"""
            "Resource/UI/HudMatchStatus.res" { "MatchDoors" { "model" { "modelname" "{{doors}}" } } }
            """);
        return Path.Combine(_folder, name);
    }

    /// <remarks>
    /// **A load's two model lists read the HUD it began with, and only while it runs** (B779 review 23 items 1 and 3):
    /// every map read fixes the HUD view at its start — a map that reads, or one whose file fails to — and lets it go once
    /// its `Apply` has run. Here HUD A chosen, a load, and HUD B chosen when the load reports its models (between `Needed` at
    /// the map read and `ToPack` at the precache): both lists, asked then, name A's doors and not B's. After the load the
    /// lists read B's. The control: the HUD did change mid-load (B is chosen at the end).
    /// </remarks>
    /// <param name="map">The demo's map: the one written here, or one whose file is held open so its read throws.</param>
    [TestCase(Map)]
    [TestCase("pl_locked")]
    public async Task LoadDemoAsync_AHudChangedMidLoad_BothListsReadTheHudTheLoadBeganWith(string map)
    {
        WriteMap();
        string locked = Path.Combine(_folder, "maps", "pl_locked.bsp");
        await File.WriteAllBytesAsync(locked, new byte[] { 1, 2, 3, 4 }).ConfigureAwait(false);
        using FileStream held = new(locked, FileMode.Open, FileAccess.Read, FileShare.None);
        string hudA = HudWithDoors("hud_a", DoorsA);
        string hudB = HudWithDoors("hud_b", DoorsB);
        (ViewerPresenter loader, FakeLoadView view, _) = Loader();
        view.Loads = 2;

        using (loader)
        {
            (await loader.LoadDemoAsync(WriteDemo("warm")).ConfigureAwait(false)).Loaded.ShouldBeTrue();
            GameContent game = loader.Game.ShouldNotBeNull();
            loader.SetChosenHud(hudA);
            (bool NeededA, bool NeededB, bool ToPackA, bool ToPackB)? midLoad = null;
            view.Asked = name =>
            {
                if (name == "Report: Loading models and sounds")
                {
                    loader.SetChosenHud(hudB);
                    HashSet<string> needed = DemoModels.Needed(null, game);
                    HashSet<string> toPack = DemoModels.ToPack(null, game);
                    midLoad = (needed.Contains(DoorsA), needed.Contains(DoorsB), toPack.Contains(DoorsA), toPack.Contains(DoorsB));
                }
            };

            await loader.LoadDemoAsync(WriteDemo("changed", map)).ConfigureAwait(false);
            view.Asked = null;

            midLoad.ShouldBe((true, false, true, false));
            game.ChosenHud.ShouldBe(hudB, "the control: the HUD changed mid-load");
            DemoModels.Needed(null, game).Contains(DoorsB).ShouldBeTrue("after the load, the HUD as it is now");
        }
    }

    /// <remarks>
    /// **The barrier does not stay up when the load's first step fails** (B779 review 23 item 4): raising it, dropping the
    /// skip and clearing the map are one marshal, inside the region that lowers it. Here the window's clear throws an I/O
    /// error: the load ends saying it could not open the demo, and the barrier is down. The control: the clear was asked.
    /// </remarks>
    [Test]
    public async Task LoadDemoAsync_AClearThatThrows_LeavesTheBarrierDown()
    {
        WriteMap();
        (ViewerPresenter loader, FakeLoadView view, _) = Loader();
        bool cleared = false;
        view.Asked = name =>
        {
            if (name == nameof(ILoadView.ClearWorld))
            {
                cleared = true;
                throw new IOException("a clear that fails");
            }
        };

        using (loader)
        {
            DemoLoadResult result = await loader.LoadDemoAsync(WriteDemo("clear")).ConfigureAwait(false);
            view.Asked = null;

            cleared.ShouldBeTrue("the control");
            result.Loaded.ShouldBeFalse();
            loader.ReadingMap.ShouldBeFalse();
        }
    }

    /// <remarks>
    /// **A read that throws lowers the barrier and lets its fixed HUD go** (B779 review 23 item 6, review 24 item 4): the
    /// worker's failure leaves through the region that raised it. Here HUD A chosen, and HUD B chosen as the report after
    /// the map read throws: the load throws it on, the barrier is down, and the lists read B's doors. The controls: the
    /// barrier was up when it threw, and the lists read A's then.
    /// </remarks>
    [Test]
    public async Task LoadDemoAsync_AReadThatThrows_LowersTheBarrierAndLetsItsHudGo()
    {
        WriteMap();
        string hudA = HudWithDoors("hud_a", DoorsA);
        string hudB = HudWithDoors("hud_b", DoorsB);
        (ViewerPresenter loader, FakeLoadView view, _) = Loader();
        bool? upWhenThrown = null;
        bool? aWhenThrown = null;
        loader.SetChosenHud(hudA);
        view.Asked = name =>
        {
            if (name == "Report: Loading models and sounds")
            {
                upWhenThrown = loader.ReadingMap;
                loader.SetChosenHud(hudB);
                aWhenThrown = DemoModels.Needed(null, loader.Game.ShouldNotBeNull()).Contains(DoorsA);
                throw new InvalidOperationException("a read that fails");
            }
        };

        using (loader)
        {
            await Should.ThrowAsync<InvalidOperationException>(() => loader.LoadDemoAsync(WriteDemo("throws"))).ConfigureAwait(false);
            view.Asked = null;

            (upWhenThrown, aWhenThrown).ShouldBe(((bool?)true, (bool?)true), "the controls");
            loader.ReadingMap.ShouldBeFalse();
            DemoModels.Needed(null, loader.Game.ShouldNotBeNull()).Contains(DoorsB).ShouldBeTrue("the load let its HUD go");
        }
    }

    private static readonly WorldUpload Lost = new(Uploaded: false, Problem: "Reloading the map…", NeedsReload: true);

    /// <remarks>
    /// **While a map reads, only the read's own projection projects; any other marks the world, and the mark outlives the
    /// load** (B779 reviews 27-28). The window's `OpenDevice` projects with no barrier check; here it is `ProjectMap` during
    /// a second load's map read, with the world's mark cleared first as the frame does. It projects nothing and marks the
    /// world. The read's own projection, which says so (`fromTheRead`), runs (the control: it leaves the mark clear).
    /// **After the load the mark is still set** — Grok's claim that the read's completion clears it is false. Then, with
    /// the mark cleared as the frame clears it, an upload answer of "lost" (supplied here: no synthetic map loses its world
    /// on its own) reads the map again and leaves no mark.
    /// </remarks>
    [Test]
    public async Task ProjectMap_NotTheReadsOwnWhileAMapReads_MarksTheWorldAndTheMarkOutlivesTheLoad()
    {
        WriteMap();
        (ViewerPresenter loader, FakeLoadView view, _) = Loader();
        view.Loads = 2;

        using (loader)
        {
            (await loader.LoadDemoAsync(WriteDemo("warm")).ConfigureAwait(false)).Loaded.ShouldBeTrue();
            bool asked = false;
            bool? barrierUp = null;
            bool? markedByTheReadsOwn = null;
            bool? markedByTheWindow = null;
            int clearsBefore = 0;
            int clearsAfter = 0;
            view.Asked = name =>
            {
                if (name != nameof(ILoadView.MapRead) || asked)
                {
                    return;
                }

                asked = true;
                barrierUp = loader.ReadingMap;
                clearsBefore = view.Names.Count(static n => n == nameof(ILoadView.ClearWorld));

                loader.WorldProjected();
                loader.ProjectMap(surfaceColours: false, (800, 600), fromTheRead: true);
                markedByTheReadsOwn = loader.WorldNeedsProjecting;

                // The window's: the same call without the signal, on any thread — this one included (review 28 item 1).
                loader.WorldProjected();
                loader.ProjectMap(surfaceColours: false, (800, 600));
                markedByTheWindow = loader.WorldNeedsProjecting;
                clearsAfter = view.Names.Count(static n => n == nameof(ILoadView.ClearWorld));
            };

            (await loader.LoadDemoAsync(WriteDemo("second")).ConfigureAwait(false)).Loaded.ShouldBeTrue();
            view.Asked = null;

            barrierUp.ShouldBe(true, "the control: a worker was reading");
            markedByTheReadsOwn.ShouldBe(false, "the control: the read's own projection runs");
            markedByTheWindow.ShouldBe(true, "the window's projection only marks the world");
            clearsAfter.ShouldBe(clearsBefore, "nothing read beside the worker");
            loader.WorldNeedsProjecting.ShouldBeTrue("the mark outlives the load");

            // The first frame after the load: the mark cleared, then the upload's answer for a world the lost device took.
            int clearsBeforeFrame = view.Names.Count(static n => n == nameof(ILoadView.ClearWorld));
            loader.WorldProjected();
            loader.FollowUpload(Lost, loader.Map.ShouldNotBeNull());

            view.Names.Count(static n => n == nameof(ILoadView.ClearWorld)).ShouldBe(clearsBeforeFrame + 1, "the map read again");
            loader.WorldNeedsProjecting.ShouldBeFalse("the reload leaves no mark");
        }
    }

    /// <remarks>
    /// **A lost world waits for every load in flight, without looping, and says nothing** (B779 reviews 27-29): a load
    /// still decoding, or queued and not yet raised — a superseded load's gap is either — would have the reload read the
    /// old demo's map only for the next load to clear it. Here the upload's answer "lost" twice, as two frames give it,
    /// while a second load decodes (at its decode report) or at the start of its raising marshal: nothing is read, the
    /// world is **not** marked again (a mark would have the next frame project and lose it again, a loop for the whole
    /// decode), and the status is the load's own. After the load the world is marked once, and the same answer then reads
    /// the map. The control: the barrier was down at the answers.
    /// </remarks>
    /// <param name="whileDecoding">Whether the answer comes before the load is queued — decoded, the map not yet fetched (else once it is queued).</param>
    [TestCase(true)]
    [TestCase(false)]
    public async Task FollowUpload_AWorldLostWhileALoadIsInFlight_WaitsUnmarkedAndSilentThenReloadsOnce(bool whileDecoding)
    {
        WriteMap();
        (ViewerPresenter loader, FakeLoadView view, _) = Loader();
        view.Loads = 2;

        using (loader)
        {
            (await loader.LoadDemoAsync(WriteDemo("warm")).ConfigureAwait(false)).Loaded.ShouldBeTrue();
            LoadedMap map = loader.Map.ShouldNotBeNull();
            bool armed = false;
            bool asked = false;
            bool? barrierUp = null;
            bool? marked = null;
            string? status = null;
            int clearsBefore = 0;
            int clearsAfter = 0;

            void Lose()
            {
                asked = true;
                barrierUp = loader.ReadingMap;
                clearsBefore = view.Names.Count(static n => n == nameof(ILoadView.ClearWorld));
                bool markedOnce = false;

                // Two frames: each clears the mark, projects, and is told the world is lost.
                for (int frame = 0; frame < 2; frame++)
                {
                    loader.WorldProjected();
                    loader.FollowUpload(Lost, map);
                    markedOnce |= loader.WorldNeedsProjecting;
                }

                marked = markedOnce;
                status = view.Status;
                clearsAfter = view.Names.Count(static n => n == nameof(ILoadView.ClearWorld));
            }

            // Before the queue: at the report that the decode is done and the map comes next — counted, not yet queued (the
            // test window notes the decode's own reports by count, so a late progress post can take the decode report's
            // place; this report is always noted). Once queued: the first marshal after it, the raise.
            view.Asked = name =>
            {
                if (name != "Report: Loading the map")
                {
                    return;
                }

                if (whileDecoding && !asked)
                {
                    Lose();
                }
                else if (!whileDecoding)
                {
                    armed = true;
                }
            };
            view.Marshalling = () =>
            {
                if (armed && !asked)
                {
                    Lose();
                }
            };

            (await loader.LoadDemoAsync(WriteDemo("queued")).ConfigureAwait(false)).Loaded.ShouldBeTrue();
            view.Asked = null;
            view.Marshalling = null;

            (asked, barrierUp).ShouldBe((true, (bool?)false), "the control: the barrier was down");
            clearsAfter.ShouldBe(clearsBefore, "nothing read while the load was in flight");
            marked.ShouldBe(false, "the frames waited rather than looping");
            status.ShouldNotBe(Lost.Problem, "the load's own status stays");

            loader.LoadsInFlight.ShouldBe(0);
            loader.WorldNeedsProjecting.ShouldBeTrue("the last load to finish marked the world once");
            int clearsBeforeReload = view.Names.Count(static n => n == nameof(ILoadView.ClearWorld));
            loader.WorldProjected();
            loader.FollowUpload(Lost, loader.Map.ShouldNotBeNull());
            view.Names.Count(static n => n == nameof(ILoadView.ClearWorld)).ShouldBe(clearsBeforeReload + 1, "then the reload reads the map");

            // **And the pending reload is spent** (review 30 item 3): a plain load after it, with no world lost, ends with
            // the world unmarked — the last load marks only when a reload is pending, and this one was taken.
            loader.WorldProjected();
            view.Loads = 3;
            (await loader.LoadDemoAsync(WriteDemo("plain")).ConfigureAwait(false)).Loaded.ShouldBeTrue();
            loader.WorldNeedsProjecting.ShouldBeFalse("no reload pending, no mark");
        }
    }

    /// <remarks>
    /// **The last load marks a pending reload before the window's step** (B779 review 30 item 2): hiding the loading
    /// overlay can throw, and a throw there must not leave the reload pending with the world unmarked. Here a world lost
    /// during a load, and the overlay's hide throwing at its end: the load throws it on, and the world is marked. The
    /// control: the world was unmarked while the load ran.
    /// </remarks>
    [Test]
    public async Task LoadDemoAsync_AHideThatThrowsWithAReloadPending_StillMarksTheWorld()
    {
        WriteMap();
        (ViewerPresenter loader, FakeLoadView view, _) = Loader();
        view.Loads = 2;

        using (loader)
        {
            (await loader.LoadDemoAsync(WriteDemo("warm")).ConfigureAwait(false)).Loaded.ShouldBeTrue();
            LoadedMap map = loader.Map.ShouldNotBeNull();
            bool? markedDuring = null;
            view.Asked = name =>
            {
                if (name == "Report: Loading the map" && markedDuring is null)
                {
                    loader.WorldProjected();
                    loader.FollowUpload(Lost, map);
                    markedDuring = loader.WorldNeedsProjecting;
                }

                if (name == nameof(ILoadView.HideLoading))
                {
                    throw new InvalidOperationException("a hide that fails");
                }
            };

            await Should.ThrowAsync<InvalidOperationException>(() => loader.LoadDemoAsync(WriteDemo("second"))).ConfigureAwait(false);
            view.Asked = null;

            markedDuring.ShouldBe(false, "the control: unmarked while the load ran");
            loader.WorldNeedsProjecting.ShouldBeTrue();
        }
    }

    /// <remarks>
    /// **A load is counted inside the region that counts it down** (B779 review 29 item 3): a decode report that throws,
    /// a file that is no demo, and a read that throws each leave no load counted — a count left at one would hold a lost
    /// world's reload for ever. The control: each load did fail.
    /// </remarks>
    /// <param name="failure">Where the load fails.</param>
    [TestCase("report")]
    [TestCase("decode")]
    [TestCase("read")]
    public async Task LoadDemoAsync_ALoadThatFails_LeavesNoLoadCounted(string failure)
    {
        WriteMap();
        (ViewerPresenter loader, FakeLoadView view, _) = Loader();
        string path = WriteDemo("fails");

        if (failure == "decode")
        {
            await File.WriteAllTextAsync(path, "not a demo").ConfigureAwait(false);
        }

        view.Asked = name =>
        {
            if (failure == "report" && name == "Report: " + FakeLoadView.Decoding)
            {
                throw new IOException("a report that fails");
            }

            if (failure == "read" && name == "Report: Loading models and sounds")
            {
                throw new InvalidOperationException("a read that fails");
            }
        };

        using (loader)
        {
            bool failed;

            try
            {
                failed = !(await loader.LoadDemoAsync(path).ConfigureAwait(false)).Loaded;
            }
            catch (InvalidOperationException)
            {
                failed = true;
            }

            view.Asked = null;

            failed.ShouldBeTrue("the control: the load failed");
            loader.LoadsInFlight.ShouldBe(0);
        }
    }

    /// <remarks>
    /// **A lost world with no demo says nothing** (B779 review 27 item 5): "Reloading the map…" would overwrite the status —
    /// often why the load failed — and nothing reloads with no demo. The control: the status the window had is kept.
    /// </remarks>
    [Test]
    public void FollowUpload_AWorldLostWithNoDemo_LeavesTheStatus()
    {
        (ViewerPresenter loader, FakeLoadView view, _) = Loader();

        using (loader)
        {
            view.Status = "Could not open broken.dem";

            loader.FollowUpload(Lost, SyntheticMap.Read());

            view.Status.ShouldBe("Could not open broken.dem");
        }
    }

    /// <remarks>
    /// **The reload after a lost world precaches the open demo again and keeps its schedule** (B814): the reload resets
    /// the packed models (level shutdown) and the sound cache's list (the map read), so it runs the demo's precache again,
    /// as its load did; the schedule is the demo's (B228) and is kept. The control: a reload with no demo open runs none.
    /// What this can see: that both precaches ran again (their log lines; the fixture's install is empty, so neither packs
    /// nor decodes anything) and that the schedule is the same object.
    /// </remarks>
    [Test]
    public void LoadMap_WithADemoOpen_PrecachesTheDemoAgain()
    {
        WriteMap();
        using RecordingLoggers log = new();
        (ViewerPresenter loader, _, _) = Loader(
            new SettingsPresenter(NullLoggerFactory.Instance, new ViewerSettings(), Path.Combine(_folder, "settings.cfg")),
            loggers: log);

        using (loader)
        {
            loader.LoadMap(Map);
            int withNoDemo = log.Count("precached models in ");
            withNoDemo.ShouldBe(0, "the control: no demo open, no demo precache");

            loader.LoadDemo(WriteDemo("open")).Loaded.ShouldBeTrue();
            int afterLoad = log.Count("precached models in ");
            int soundsAfterLoad = log.Count(" sounds in ");
            SoundSchedule? schedule = loader.SoundSchedule;
            afterLoad.ShouldBe(1);
            soundsAfterLoad.ShouldBe(1, "the control: the load precached sounds once");
            schedule.ShouldNotBeNull("the control: the load made the demo's schedule");

            loader.LoadMap(Map);

            log.Count("precached models in ").ShouldBe(afterLoad + 1);
            log.Count(" sounds in ").ShouldBe(soundsAfterLoad + 1, "the sounds precached again");
            // The demo's schedule survives the reload (B228). Without an install the effect sounds schedule nothing, so this
            // holds the demo's own schedule (`DemoSystems.Open`); the install's replacement is kept off the reload by
            // `LoadMap` not calling `AddEffectSounds` at all.
            loader.SoundSchedule.ShouldBeSameAs(schedule);
        }
    }

    /// <remarks>
    /// **A synchronous load lets its fixed HUD view go** (B779 review 24 item 4): `LoadMap` and `LoadDemo` each end the load
    /// they began, so afterwards the lists read the HUD as it is now. HUD A chosen, the load, HUD B chosen during its map
    /// read; after it, the lists read B's doors. The control: during the read, they read A's.
    /// </remarks>
    /// <param name="loadMap">Whether the route is `LoadMap` (else `LoadDemo`).</param>
    [TestCase(true)]
    [TestCase(false)]
    public void LoadMapAndLoadDemo_AfterTheirRead_LetTheLoadsHudGo(bool loadMap)
    {
        WriteMap();
        string hudA = HudWithDoors("hud_a", DoorsA);
        string hudB = HudWithDoors("hud_b", DoorsB);
        (ViewerPresenter loader, FakeLoadView view, _) = Loader();

        using (loader)
        {
            loader.LoadDemo(WriteDemo("warm")).Loaded.ShouldBeTrue();
            GameContent game = loader.Game.ShouldNotBeNull();
            loader.SetChosenHud(hudA);
            bool? duringA = null;
            view.Asked = name =>
            {
                if (name == nameof(ILoadView.MapRead))
                {
                    loader.SetChosenHud(hudB);
                    duringA = DemoModels.Needed(null, game).Contains(DoorsA);
                }
            };

            if (loadMap)
            {
                loader.LoadMap(Map);
            }
            else
            {
                loader.LoadDemo(WriteDemo("sync")).Loaded.ShouldBeTrue();
            }

            view.Asked = null;

            duringA.ShouldBe(true, "the control: during the read, the load's HUD");
            DemoModels.Needed(null, game).Contains(DoorsB).ShouldBeTrue("after it, the HUD as it is now");
        }
    }

    /// <remarks>
    /// **The launch line's `--hud` wins over the remembered choice, which wins over stock** (D193; B779 review 21 item 10):
    /// `ReadLaunch` hands the winner to the install the HUD and the preload read. Two presenters with the same remembered
    /// HUD: one launched with `--hud`, one without. The control: the remembered one reaches the install when no flag is given.
    /// </remarks>
    [Test]
    public void ReadLaunch_AHudFlagAndARememberedHud_TheFlagReachesTheInstall()
    {
        WriteMap();

        foreach ((string[] arguments, string expected) in (ValueTuple<string[], string>[])
            [(["a.dem", "--hud", "custom/flag"], "custom/flag"), (["a.dem"], "custom/remembered")])
        {
            (ViewerPresenter loader, _, _) = Loader(settings: new ViewerSettings { ChosenHud = "custom/remembered" });

            using (loader)
            {
                loader.ReadLaunch(arguments);
                loader.LoadDemo(WriteDemo("launch")).Loaded.ShouldBeTrue();

                loader.Game.ShouldNotBeNull().ChosenHud.ShouldBe(expected, string.Join(' ', arguments));
            }
        }
    }

    /// <remarks>
    /// **No moment is shown while a map reads** (B779 review 21 item 1): a scrub reaches `ShowMoment` from the UI thread
    /// outside the frame (review 22 item 5), and a moment built then would pack models into the entity set
    /// while the worker binds its loader and precaches into it. The engine's client draws no world during a level load.
    /// Here a demo already open, a second load, and a `ShowMoment` asked when the map read reports itself (behind the
    /// barrier). The controls: the barrier was up at that moment, and the same ask after the load shows the tick.
    /// </remarks>
    [Test]
    public async Task ShowMoment_WhileAMapReads_ShowsNothing()
    {
        WriteMap();
        (ViewerPresenter loader, FakeLoadView view, _) = Loader();
        Questions asked = new();

        using (loader)
        {
            loader.AttachWindow(asked.Window(loader), new FpsPanelTests.SolidGdi());
            view.Loads = 2;
            (await loader.LoadDemoAsync(WriteDemo("first")).ConfigureAwait(false)).Loaded.ShouldBeTrue();
            int? shownDuringRead = null;
            bool? barrierUp = null;
            view.Asked = name =>
            {
                if (name == nameof(ILoadView.MapRead))
                {
                    barrierUp = loader.ReadingMap;
                    int before = asked.Shown.Count;
                    loader.ShowMoment(12d);
                    shownDuringRead = asked.Shown.Count - before;
                }
            };

            (await loader.LoadDemoAsync(WriteDemo("second")).ConfigureAwait(false)).Loaded.ShouldBeTrue();
            view.Asked = null;

            barrierUp.ShouldBe(true, "the control: the ask came behind the barrier");
            shownDuringRead.ShouldBe(0);
            int after = asked.Shown.Count;
            loader.ShowMoment(12d);
            asked.Shown.Count.ShouldBeGreaterThan(after, "the control: after the load the same ask shows");
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
    public void PlaySoundsOver_WithTheDuckerConVarsSet_DucksTheSinkOnceAFrameWithThem()
    {
        // `S_Update` steps the ducker once a frame, after the frame's starts (B719); the sink measures and applies it.
        WriteMap();
        (ViewerPresenter loader, _, _) = Loader(settings: ViewerSettings.Parse(
            "snd_ducking_off 0\nsnd_ducktovolume 0.3\nsnd_duckerattacktime 0.25\nsnd_duckerreleasetime 4\nsnd_duckerthreshold 0.5"));
        CountingSink sink = new();

        using (loader)
        {
            loader.LoadDemo(WriteDemo("ducked")).Loaded.ShouldBeTrue();

            loader.PlaySoundsOver(sink, () => Ears, static () => null, tick: 100);
            loader.PlaySoundsOver(sink, () => Ears, static () => null, tick: 101);

            sink.Ducks.Count.ShouldBe(2, "once a frame");
            sink.Ducks[0].Ducking.ShouldBe(new SpeechDucking(Off: false, ToVolume: 0.3f, AttackTime: 0.25f, ReleaseTime: 4f, Threshold: 0.5f));
            sink.Ducks.ShouldAllBe(duck => duck.FrameTime >= 0f, "a frame's own wall-clock length");
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
                "WorldFog none", "SkyFog none", "MonitorCameras 0", "Tonemap default",
                "ExposureSeconds 0.25", "ObserverTarget nobody",
                nameof(IViewerDevice.SetCamera),
                "DrawsMonitor read True",
                "posed",
                nameof(IViewerDevice.FinishDrawBeforeEffects),
                nameof(IFrameDevice.SetParticles),
            ]);
            device.Camera.SurfaceColours.ShouldBeTrue();
            device.Camera.Camera.ShouldNotBeNull();
        }
    }

    /// <remarks>
    /// **Each camera upload hands the device the frame's freeze-frame decision** (B738): `RenderView` follows `SetUpViews`,
    /// and the freeze cam's `FreezeFrame` call was made in `CalcView`. This demo has no freeze cam, so the decision is to draw
    /// as ever; the chain from a freeze cam to a take and a hold is `SpectatorViewObserverTests.After_*`.
    /// </remarks>
    [Test]
    public void UploadCameraOver_AFrame_HandsTheDeviceItsFreezeFrameDecision()
    {
        WriteMap();
        (ViewerPresenter loader, _, _) = Loader();
        Questions asked = new();
        FakeViewerDevice device = new();

        using (loader)
        {
            loader.AttachWindow(asked.Window(loader), new FpsPanelTests.SolidGdi());
            loader.LoadDemo(WriteDemo("camera")).Loaded.ShouldBeTrue();

            loader.UploadCameraOver(device, new FakeFrameDevice(), flyTicks: 0, exposureSeconds: 0.25f, surfaceColours: false);

            device.FreezeFrameSets.ShouldBe(1);
            device.FreezeFrame.ShouldBe(FreezeFrameDraw.Normal);
        }
    }

    /// <remarks>
    /// **Rung 3's first step (B581, finding 98 §9): SetCamera AND the pose overlap the frame in flight; the effects do
    /// not.** The cull writes its own view half, and the pose now writes its own pose half (instance lists, matrices,
    /// skinning, face streams; `PoseHalvesConformanceTests` in Scene.Tests), so both may run while the previous frame draws
    /// from the other halves. The particles, decals and lightmaps still hand the device what it reads in place, so the
    /// drain, <see cref="IViewerDevice.FinishDrawBeforeEffects"/>, sits after the pose and before them. Deterministic:
    /// the in-flight draw is held on a gate only the drain opens. Before the move this read `posed in flight False`.
    /// </remarks>
    [Test]
    public void UploadCameraOver_InModeTwoWithAFrameInFlight_OverlapsThePoseButNotTheEffects()
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
                FakeFrameDevice effects = new() { Told = call => device.Mark($"{call} in flight {queue.InFlight}") };
                loader.UploadCameraOver(device, effects, flyTicks: 0, exposureSeconds: 0f, surfaceColours: false);

                device.Set.ShouldContain("SetCamera in flight True", "the control: the frame was in flight at the cull");
                device.Set.ShouldContain("posed in flight True");
                device.Set.ShouldContain($"{nameof(IFrameDevice.SetParticles)} in flight False");
            }
            finally
            {
                gate.Set();
            }
        }
    }

    /// <remarks>
    /// **The pose itself reads the hand-off count of the device the frame poses over** (B581 rung 3, review): the scene's
    /// source points at that device before the pose runs, so the count read inside the pose is the device's — and a
    /// seek's pose, which passes no device, reads the same one. A moment is built, so the pose really runs and the read
    /// is the scene's, not the test's.
    /// </remarks>
    [Test]
    public void UploadCameraOver_WithAMomentBuilt_PosesWithTheHandOffCountOfThatDevice()
    {
        WriteMap();
        (ViewerPresenter loader, _, _) = Loader();
        Questions asked = new();
        FakeViewerDevice device = new() { HandOffs = 7 };

        using (loader)
        {
            loader.AttachWindow(asked.Window(loader), new FpsPanelTests.SolidGdi());
            loader.LoadDemo(WriteDemo("camera")).Loaded.ShouldBeTrue();
            loader.Show(12d, playing: false);

            loader.Posed = () => device.Mark("posed");
            loader.UploadCameraOver(device, new FakeFrameDevice(), flyTicks: 0, exposureSeconds: 0f, surfaceColours: false);

            List<string> set = [.. device.Set];
            int monitor = set.FindIndex(line => line.StartsWith("DrawsMonitor read", StringComparison.Ordinal));
            int counted = set.IndexOf("HandOffs read 7");
            int posed = set.IndexOf("posed");

            counted.ShouldBeGreaterThan(monitor, "the pose did not read this device's count after its own inputs");
            counted.ShouldBeLessThan(posed);
            monitor.ShouldBeGreaterThanOrEqualTo(0, "the control: the pose's inputs were read");
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
    public void DrawSceneOver_WithAnOverlay_SetsTheOverlayMonitorModelsAndWaterEntriesThenDrawsWithThatOverlay()
    {
        WriteMap();
        (ViewerPresenter loader, _, _) = Loader();
        FakeViewerDevice device = new();
        FrameOverlay overlay = new(new Tf2DemoSalvage.Scene.Hud.VguiDrawList(static _ => (0, 0)));

        using (loader)
        {
            loader.LoadDemo(WriteDemo("draw")).Loaded.ShouldBeTrue();

            loader.DrawSceneOver(device, 0.1f, 0.2f, 0.3f, leafVis: false, overlay);

            device.Set.ShouldBe(["ScreenOverlay none", "MonitorOnlyModels 0", "WaterViewEntries 0", nameof(IViewerDevice.DrawScene)]);
            device.Drawn.Vgui.ShouldBeSameAs(overlay.List);
            (device.Drawn.Red, device.Drawn.Green, device.Drawn.Blue).ShouldBe((0.1f, 0.2f, 0.3f));
            device.Drawn.Lines.ShouldBe(0, "mat_leafvis is off");
            device.Drawn.ViewmodelCamera.ShouldBeNull("nobody's eyes: no viewmodel pass");
            device.DetailFovFactor.ShouldBe(1f, "B580: the factor is handed over each frame; an unzoomed demo's is 1");
        }
    }

    /// <remarks>
    /// B720: a `mat_bumpmap` or `mat_phong` change makes `CMaterialSystem::OverrideConfig` (`materialsystem.dll`
    /// `FUN_180018fd0`) reload every material and model, and the engine's restore removes every model's decals
    /// (`engine.dll` `FUN_1800f7b40`, `RemoveAllDecalsFromAllModels`). A set that changes nothing calls no
    /// `OverrideConfig` reload, so the decal placed after the change stays.
    /// </remarks>
    [Test]
    public void ApplyMaterialSwitches_AChangeThenTheSameValue_RemovesTheModelDecalsOnlyOnTheChange()
    {
        SettingsPresenter owner = new(NullLoggerFactory.Instance, new ViewerSettings(), Path.Combine(_folder, "settings.cfg"));
        (ViewerPresenter loader, _, _) = Loader(owner);

        using (loader)
        {
            ModelDecals pool = loader.Models.Decals.ShouldNotBeNull();
            PlaceAPropDecal(pool);
            loader.ApplyMaterialSwitches();
            pool.Count.ShouldBe(1, "the control: nothing changed, so nothing reloaded");

            owner.SetConVar(ViewerSettings.BumpMapCommand, "0");
            loader.ApplyMaterialSwitches();
            pool.Count.ShouldBe(0, "mat_bumpmap 0 reloaded every model");

            PlaceAPropDecal(pool);
            owner.SetConVar(ViewerSettings.BumpMapCommand, "0");
            loader.ApplyMaterialSwitches();
            pool.Count.ShouldBe(1, "mat_bumpmap 0 again is no change");
            loader.MaterialSwitches.ShouldBe(new MaterialSwitches(BumpMapping: false, Phong: true));
        }
    }

    /// <remarks>
    /// B720, D69: `mat_bumpmap 0` set at run time — a key, the console, an exec'd cfg — reaches the device as the config
    /// at attach does, and both switches are handed over together, as `OverrideConfig` takes the whole config.
    /// </remarks>
    [Test]
    public void ApplyMaterialSwitches_BumpMapSetAtRunTime_ReachesTheDevice()
    {
        SettingsPresenter owner = new(NullLoggerFactory.Instance, new ViewerSettings(), Path.Combine(_folder, "settings.cfg"));
        (ViewerPresenter loader, _, _) = Loader(owner);
        FakeViewerDevice device = new();

        using (loader)
        {
            loader.AttachOver(device, static () => { }, static _ => { });
            int attached = device.Set.Count;

            owner.SetConVar(ViewerSettings.BumpMapCommand, "0");
            loader.ApplyMaterialSwitches();

            device.Set.Skip(attached).ShouldBe(["BumpMap False", "Phong True"]);
        }
    }

    /// <remarks>
    /// B720: a config saying `mat_bumpmap 0` and `mat_phong 0` gives the models both when the device is attached: TF2's
    /// models load under the config in force, and the viewer's start from TF2's defaults, both on.
    /// </remarks>
    [Test]
    public void AttachOver_AConfigWithBothSwitchesOff_GivesTheModelsBoth()
    {
        (ViewerPresenter loader, _, _) = Loader(settings: new ViewerSettings { BumpMap = false, Phong = false });

        using (loader)
        {
            loader.MaterialSwitches.ShouldBe(MaterialSwitches.Default, "the control: before the attach, TF2's defaults");

            loader.AttachOver(new FakeViewerDevice(), static () => { }, static _ => { });

            loader.MaterialSwitches.ShouldBe(new MaterialSwitches(BumpMapping: false, Phong: false));
        }
    }

    // A static prop's decal on one triangle, as `DecalEffectsTests` places one, with its control.
    private static void PlaceAPropDecal(ModelDecals pool)
    {
        WorldVertex[] face =
        [
            new(0f, -100f, -100f, 0f, 0f, 0f, 0f, 1f, NormalX: 1f, NormalY: 0f, NormalZ: 0f),
            new(0f, 100f, -100f, 0f, 0f, 0f, 0f, 1f, NormalX: 1f, NormalY: 0f, NormalZ: 0f),
            new(0f, 0f, 100f, 0f, 0f, 0f, 0f, 1f, NormalX: 1f, NormalY: 0f, NormalZ: 0f),
        ];

        pool.AddClipped(DecalReplay.StaticPropKey(3), face, System.Numerics.Vector3.Zero, new System.Numerics.Vector3(-1.1f, 0f, 0f), 4f, 1)
            .ShouldBeTrue("the control: the prop took its decal");
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

        public List<(SpeechDucking Ducking, float FrameTime)> Ducks { get; } = [];

        public void Update(SpeechDucking ducking, float frameTime) => Ducks.Add((ducking, frameTime));
    }
}
