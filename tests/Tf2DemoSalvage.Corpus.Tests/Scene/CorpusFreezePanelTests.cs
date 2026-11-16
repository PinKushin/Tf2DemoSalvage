using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// A real point-of-view demo's freeze cam drives the freeze panel's events, through the production chain (B748).
/// </summary>
/// <remarks>
/// **The output-level assertion** (<c>docs/memory/output-level-assertion-or-it-is-not-done.md</c>): the HUD's state for
/// each tick, as <see cref="HudStates.For"/> builds it from the timeline, through <see cref="FreezeEvents"/>. The recorder
/// of `etf2l-12025-pov-2020-07-21` dies at tick 2008 and is in the freeze cam from about 2023 to 2095. lcor; skips
/// without it.
/// </remarks>
public sealed class CorpusFreezePanelTests
{
    private const string PovDemo = "etf2l-12025-pov-2020-07-21";

    [Test]
    public void Advance_ARealFreezeCam_ShowsAPlayerKillerThenStartsThenHides()
    {
        DemoTimeline timeline = TimelineCache.For(Corpus.Demo(PovDemo));
        FreezeEvents events = new();
        List<(int Tick, SceneGameEvent Event, HudState State)> fired = [];

        for (int tick = 1990; tick <= 2130; tick++)
        {
            HudState state = HudStates.For(timeline, tick);

            fired.AddRange(events.Advance(state, reset: tick == 1990).Select(e => (tick, e, state)));
        }

        fired.Select(f => f.Event.Name).ShouldBe(["show_freezepanel", "freezecam_started", "hide_freezepanel"]);

        // The killer the card names is a player at that tick: the observer target `PostDataUpdate` reads (:905-909).
        (int showTick, SceneGameEvent shown, HudState at) = fired[0];
        at.Player(shown.GetInt("killer")).ShouldNotBeNull($"the killer named at tick {showTick} is a player");

        // `spec_freeze_traveltime` after the entry, and not a frame sooner.
        (int startTick, _, HudState started) = fired[1];
        (started.CurTime - at.CurTime).ShouldBeGreaterThanOrEqualTo(ObserverCamera.FreezeTravelTime);
        (HudStates.For(timeline, startTick - 1).CurTime - at.CurTime).ShouldBeLessThan(ObserverCamera.FreezeTravelTime);
    }

    [Test]
    public void PlayersAt_APointOfViewRecorder_CarriesHisDominatingMeArray()
    {
        // The nemesis input is local data, sent to the recorder alone: present for him on a POV demo (B748).
        DemoTimeline timeline = TimelineCache.For(Corpus.Demo(PovDemo));
        int recorder = timeline.RecorderEntityIndex.ShouldNotBeNull();

        timeline.PlayersAt(2050).Single(player => player.EntityIndex == recorder).DominatingMe.ShouldNotBeNull();
        timeline.PlayersAt(2050).Where(player => player.EntityIndex != recorder).ShouldAllBe(player => player.DominatingMe == null);
    }
}
