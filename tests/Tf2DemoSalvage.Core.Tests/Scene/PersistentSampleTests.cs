using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// Sampling is proportional to what changed, and indistinguishable from sampling everything —
/// B259 fix 3, stage C.
/// </summary>
/// <remarks>
/// **The engine never enumerates every entity per frame, and after stage C neither do we.**
/// `ProcessInterpolatedList` walks <c>g_InterpolationList</c> — *"Interpolate the minimal set of
/// entities that need it"* (`c_baseentity.cpp:3123`) — which an entity joins when a network update
/// latches a changed variable (`OnLatchInterpolatedVariables`, `:2832`) and leaves the moment its
/// interpolation has nothing further to do (<c>bNoMoreChanges</c>, `:2927`). Our translation: a
/// keyframe boundary is an update arriving, so every track can compute its own wake-up ticks ahead
/// of time, and between wakes a non-lerping track is not touched at all.
///
/// **Sabotage-verified 2026-09-01, and the coverage is PARTIAL — read this before trusting the
/// file.** Three manipulations, each restored: discarding `AdvanceSample`'s lerp re-sample
/// reddened four; removing `Motion`'s `Candidate(toTick)` reddened three, diverging exactly at the
/// destination-keyframe ticks (150 and 240); neutralising `PropsAt`'s backward-seek term reddened
/// `AfterASeekBackwards` ALONE, with every forward-only test green — which is the narrow result
/// that says the seek path is genuinely separable.
///
/// **The remaining four were then sabotaged too, and one of them could not fail.** Three are
/// sensitive: the hidden span reddens alone when the `Hidden: false` pattern is widened; the
/// track end reddens when `IndexAt`'s `tick &gt;= _endTick` is loosened (with collateral, because
/// `Motion` carries its OWN copy of that guard and desyncing the two freezes the sampler); the
/// visibility latch reddens when `AdvanceSample`'s resample loop is widened from `_lerping` to
/// `_props`, which is exactly the pre-fix instant-lerp defect.
///
/// **`SteppedAcrossARecorderTeamSwitch` was green under an inverted resync trigger** — it
/// asserted on the DOOR, whose own keyframe at tick 150 falls inside the sampled window, so its
/// wake fired regardless and `AdvanceSample` handed `DeriveSample` the current call's
/// `recorderTeam` anyway. Correct and broken predicted the same reading: the wrong INPUT, not a
/// weak assertion. It now asserts on the crate — one keyframe, no wake in the window, the resync
/// the only path to a fresh prop — and reddens under that same inversion.
///
/// **The contract these tests hold is equivalence**: a timeline stepped forward tick by tick must
/// answer exactly what a freshly built timeline answers cold, at every tick, through births,
/// deaths, hidden spans, held spans and seeks. The one deliberate divergence from the OLD
/// behaviour — a prop whose visibility arrives mid-segment joins the lerp at the next keyframe,
/// not instantly — is the engine's own rule and is asserted separately.
/// </remarks>
public sealed class PersistentSampleTests
{
    /// <summary>A cast of tracks covering every lifecycle a prop can have.</summary>
    /// <remarks>
    /// **Built fresh per call, never shared.** The differential below compares a STEPPED timeline
    /// against a FRESH one, and tracks now carry sampling state — handing both timelines the same
    /// track objects would let the fresh one read the stepped one's answers.
    ///
    /// Keyframe spacing of 3 on the mover is deliberate: it is smaller than the 7-tick
    /// interpolation delay, so lerp windows overlap and the wake arithmetic has no quiet gaps to
    /// hide in.
    /// </remarks>
    private static List<ScenePropTrack> Cast()
    {
        ScenePropTrack mover = new(entityIndex: 1, "models/props/cart.mdl");

        for (int tick = 0; tick <= 300; tick += 3)
        {
            mover.Add(tick, new ScenePose { X = tick * 2f, Yaw = tick % 360 });
        }

        ScenePropTrack door = new(entityIndex: 2, "models/props/door.mdl");

        door.Add(0, new ScenePose { X = 0f });
        door.Add(150, new ScenePose { X = 64f });

        ScenePropTrack crate = new(entityIndex: 3, "models/props/crate.mdl");

        crate.Add(0, new ScenePose { X = 10f, Y = 20f });

        ScenePropTrack latecomer = new(entityIndex: 4, "models/items/ammopack.mdl");

        latecomer.Add(200, new ScenePose { X = 5f });
        latecomer.Add(240, new ScenePose { X = 45f });
        latecomer.End(280);

        ScenePropTrack ghost = new(entityIndex: 5, "models/props/ghost.mdl");

        ghost.Add(0, new ScenePose { X = 1f });
        ghost.Add(100, new ScenePose { X = 1f, Hidden = true });
        ghost.Add(180, new ScenePose { X = 9f });

        ScenePropTrack ender = new(entityIndex: 6, "models/props/barrel.mdl");

        ender.Add(0, new ScenePose { X = 7f });
        ender.End(90);

        return [mover, door, crate, latecomer, ghost, ender];
    }

    /// <summary>What a freshly built timeline answers at one tick, knowing nothing else.</summary>
    private static List<SceneProp> Fresh(double tick, int? viewEntity)
    {
        DemoTimeline cold = DemoTimeline.ForTracks(Cast());

        List<SceneProp> props = [];

        cold.PropsAt(tick, props, viewEntity);

        return props;
    }

    private static void ShouldMatchAFreshSample(
        List<SceneProp> stepped, double tick, int? viewEntity)
    {
        List<SceneProp> fresh = Fresh(tick, viewEntity);

        stepped.Count.ShouldBe(fresh.Count, $"prop count diverged at tick {tick}");

        for (int i = 0; i < stepped.Count; i++)
        {
            stepped[i].ShouldBe(fresh[i], $"prop {fresh[i].EntityIndex} diverged at tick {tick}");
        }
    }

    /// <remarks>
    /// **The differential that makes stage C safe to build at all.** Whatever the sampling keeps
    /// between calls, the answers must be the ones a stateless walk produces — at every half tick,
    /// through the mover's overlapping lerp windows, the door's long hold, the ghost's hidden
    /// span, the latecomer's birth and the two deaths. A wake tick missing from the schedule
    /// leaves a stale pose that a cold timeline does not have, and this is the test that sees it.
    /// </remarks>
    [Test]
    public void PropsAt_SteppedForwardHalfATickAtATime_MatchesAFreshTimelineEverywhere()
    {
        DemoTimeline stepped = DemoTimeline.ForTracks(Cast());

        List<SceneProp> props = [];

        for (double tick = 0d; tick <= 320d; tick += 0.5)
        {
            stepped.PropsAt(tick, props);

            ShouldMatchAFreshSample(props, tick, viewEntity: null);
        }
    }

    /// <remarks>
    /// **The same differential with a mover that HOLDS**, because holding and blending have different
    /// wake schedules and only a track off the list exercises the first. The refusal is a render mode
    /// on the cast rather than a set handed to the sampler (B385): `ShouldDraw`'s own first test, so a
    /// held track here is held for the reason the engine holds one.
    /// </remarks>
    [Test]
    public void PropsAt_SteppedForwardWithATrackThatHolds_MatchesAFreshTimelineEverywhere()
    {
        DemoTimeline stepped = DemoTimeline.ForTracks(Refusing());

        List<SceneProp> props = [];

        for (double tick = 0d; tick <= 320d; tick += 0.5)
        {
            stepped.PropsAt(tick, props);

            ShouldMatchAFreshSampleOf(Refusing, props, tick);
        }
    }

    /// <summary>The cast with the door and the ender declaring <c>kRenderNone</c> throughout.</summary>
    /// <remarks>
    /// **The door especially**, because its two keyframes 150 ticks apart are the long held span the
    /// wake arithmetic has the most room to get wrong — and because a `func_door` at `kRenderNone`
    /// carrying a visible prop is the pairing B385 was about.
    /// </remarks>
    private static List<ScenePropTrack> Refusing()
    {
        List<ScenePropTrack> cast = Cast();

        ScenePropTrack door = new(entityIndex: 2, "models/props/door.mdl");

        door.Add(0, new ScenePose { X = 0f, RenderMode = RenderModes.None });
        door.Add(150, new ScenePose { X = 64f, RenderMode = RenderModes.None });

        cast[1] = door;

        return cast;
    }

    /// <summary>The differential against a cold timeline built from a named cast.</summary>
    private static void ShouldMatchAFreshSampleOf(
        Func<List<ScenePropTrack>> cast, List<SceneProp> stepped, double tick)
    {
        List<SceneProp> fresh = [];

        DemoTimeline.ForTracks(cast()).PropsAt(tick, fresh);

        stepped.Count.ShouldBe(fresh.Count, $"prop count diverged at tick {tick}");

        for (int i = 0; i < stepped.Count; i++)
        {
            stepped[i].ShouldBe(fresh[i], $"prop {fresh[i].EntityIndex} diverged at tick {tick}");
        }
    }

    /// <remarks>
    /// **A seek is the one thing the engine cannot do and this project must** (D131: any state
    /// that survives across frames has to be invalidated by a scrub). Backwards lands mid-lerp of
    /// the mover and mid-hold of the door; the answers must be cold-start answers, and stepping
    /// onward from the landing must stay equivalent too.
    /// </remarks>
    [Test]
    public void PropsAt_AfterASeekBackwards_MatchesAFreshTimelineFromThereOn()
    {
        DemoTimeline stepped = DemoTimeline.ForTracks(Cast());

        List<SceneProp> props = [];

        for (double tick = 0d; tick <= 250d; tick += 1d)
        {
            stepped.PropsAt(tick, props);
        }

        for (double tick = 60d; tick <= 320d; tick += 1d)
        {
            stepped.PropsAt(tick, props);

            ShouldMatchAFreshSample(props, tick, viewEntity: null);
        }
    }

    /// <remarks>
    /// A forward jump crosses many wake ticks in one call — the latecomer's whole life fits
    /// inside this one — and each must be processed rather than skipped, in order.
    /// </remarks>
    [Test]
    public void PropsAt_AfterAForwardJumpOverManyEvents_MatchesAFreshTimeline()
    {
        DemoTimeline stepped = DemoTimeline.ForTracks(Cast());

        List<SceneProp> props = [];

        stepped.PropsAt(5d, props);
        stepped.PropsAt(290d, props);

        ShouldMatchAFreshSample(props, 290d, viewEntity: null);

        stepped.PropsAt(291d, props);

        ShouldMatchAFreshSample(props, 291d, viewEntity: null);
    }

    /// <remarks>
    /// **The ended track is the regression that already shipped once**: held poses served past
    /// `End`, `selected` going 566 to 850. Stepping across both deaths, the props must vanish at
    /// their tick and stay gone.
    /// </remarks>
    [Test]
    public void PropsAt_SteppedAcrossATracksEnd_DropsThePropAtItsEndTick()
    {
        DemoTimeline stepped = DemoTimeline.ForTracks(Cast());

        List<SceneProp> props = [];

        stepped.PropsAt(89d, props);

        props.Any(prop => prop.EntityIndex == 6).ShouldBeTrue("alive one tick before its end");

        stepped.PropsAt(90d, props);

        props.Any(prop => prop.EntityIndex == 6).ShouldBeFalse("gone from its end tick");

        stepped.PropsAt(291d, props);

        props.Any(prop => prop.EntityIndex == 6).ShouldBeFalse("still gone much later");
    }

    /// <remarks>
    /// The hidden span, stepped across rather than sampled cold: present, absent, present again —
    /// with the sampling delay meaning the transitions land seven ticks after the keyframes that
    /// state them.
    /// </remarks>
    [Test]
    public void PropsAt_SteppedAcrossAHiddenSpan_RemovesThePropAndBringsItBack()
    {
        DemoTimeline stepped = DemoTimeline.ForTracks(Cast());

        List<SceneProp> props = [];

        bool wasPresent = false;
        bool wasAbsent = false;
        bool cameBack = false;

        for (double tick = 0d; tick <= 250d; tick += 1d)
        {
            stepped.PropsAt(tick, props);

            bool present = props.Any(prop => prop.EntityIndex == 5);

            if (present && !wasAbsent)
            {
                wasPresent = true;
            }
            else if (!present && wasPresent)
            {
                wasAbsent = true;
            }
            else if (present && wasAbsent)
            {
                cameBack = true;
            }
        }

        wasPresent.ShouldBeTrue("the ghost starts visible");
        wasAbsent.ShouldBeTrue("the hidden keyframe removes it");
        cameBack.ShouldBeTrue("the later keyframe restores it");
    }

    /// <remarks>
    /// **The recorder switching sides must reach every prop already sampled.** `OfRecordersTeam`
    /// is baked into the prop when it is built, so a persistent sample that survives the switch
    /// serves the OLD side — a spawn wall drawn to the team that spawns behind it. The frames say
    /// team 2 until tick 150 and team 3 after; a team-2 prop must flip from friendly to enemy.
    ///
    /// **Asserted on the CRATE, and the reason is that this test could not fail when it asserted
    /// on the door.** Found by sabotage: inverting the `recorderTeam != _sampledTeam` resync
    /// trigger left this test green. The door has its own keyframe at tick 150 — inside the
    /// sampled window — so its wake fires anyway, and `AdvanceSample` passes the CURRENT call's
    /// `recorderTeam` into `DeriveSample`. The door's team bit was therefore refreshed as an
    /// incidental side effect of its own pose geometry, whichever way the trigger went.
    ///
    /// The crate is a single-keyframe track: `NeverChanges`, no wake inside the window, nothing
    /// else that can rebuild it. The team-switch resync is the only path to a fresh prop, which
    /// is what makes the observation attributable to the manipulation
    /// (`docs/memory/instrument-bugs-outnumber-decoder-bugs.md` — a condition where correct and
    /// broken predict the same reading is not a weak assertion, it is the wrong input).
    /// </remarks>
    [Test]
    public void PropsAt_SteppedAcrossARecorderTeamSwitch_RebuildsOfRecordersTeam()
    {
        List<ScenePropTrack> cast = Cast();

        // The crate — entity 3, one keyframe, alive throughout, no wake in [100, 200].
        cast[2].TeamNumber = 2;

        DemoTimeline stepped = DemoTimeline.ForTracks(
            cast,
            [
                new TimelineFrame(0, [], RecorderTeam: 2),
                new TimelineFrame(150, [], RecorderTeam: 3),
            ]);

        List<SceneProp> props = [];

        stepped.PropsAt(100d, props);

        props.Single(prop => prop.EntityIndex == 3).OfRecordersTeam
            .ShouldBeTrue("at tick 100 the recorder is on team 2, the crate's own side");

        stepped.PropsAt(200d, props);

        props.Single(prop => prop.EntityIndex == 3).OfRecordersTeam
            .ShouldBeFalse("from tick 150 the recorder is on team 3, so the crate is enemy");
    }

    /// <remarks>
    /// **The engine's join rule, restated through the cause rather than through a set** (B385).
    /// `OnLatchInterpolatedVariables` consults `ShouldInterpolate()` when an UPDATE arrives
    /// (`c_baseentity.cpp:2832`), and `UpdateVisibility` runs on a data update too — so visibility
    /// cannot arrive between updates at all. Our updates are keyframes, and a render mode is carried
    /// on one: a mover that stops declaring `kRenderNone` therefore joins the lerp at exactly the
    /// keyframe that says so, and not before.
    ///
    /// **This test used to hand the sampler a set and change it mid-flight**, which was a manipulation
    /// of a parameter rather than of the world. That parameter is gone: the renderer's set was wrong
    /// for three whole populations (see `InterpolationListTests`), and a mid-segment visibility change
    /// was a state the engine cannot reach.
    /// </remarks>
    [Test]
    public void PropsAt_WhenAMoversRenderModeStopsRefusing_JoinsTheLerpAtThatKeyframe()
    {
        ScenePropTrack mover = new(entityIndex: 1, "models/props/cart.mdl");

        mover.Add(0, new ScenePose { X = 0f, RenderMode = RenderModes.None });
        mover.Add(100, new ScenePose { X = 1000f, RenderMode = RenderModes.None });

        // **A third keyframe, so there IS a next latch to join at** (B370). With only two, and `Held` no
        // longer subtracting the interpolation delay, every sample after tick 100 reads 1000 whether the
        // mover joins the lerp or not — the test would pass without discriminating anything.
        mover.Add(200, new ScenePose { X = 2000f });

        DemoTimeline stepped = DemoTimeline.ForTracks([mover]);

        List<SceneProp> props = [];

        // Held, and parked: at the wake at tick 100 the newest stated pose declares `kRenderNone`, so
        // `ShouldDraw` refuses and there is nothing hanging off this mover to force it back on.
        //
        // **1000, not 0, and that is the last pose STATED** (B370). `Held` used to subtract the
        // interpolation delay and call the keyframe at tick 0 "last stated"; `cl_interp` belongs to
        // `CInterpolatedVar` and an entity off `g_InterpolationList` never reaches one, so its origin is
        // whatever the last update assigned, read live.
        stepped.PropsAt(100.5d, props);

        props.Single().Pose.X.ShouldBe(1000f, "refused by its render mode, the prop holds where stated");

        stepped.PropsAt(101d, props);

        props.Single().Pose.X.ShouldBe(
            1000f,
            "still refused, because the mode is a property of the newest stated pose and no "
            + "keyframe between 100 and 200 changes it");

        // The keyframe at tick 200 draws normally, and from there the mover is blended: sampled one
        // interpolation delay past it, the pose is strictly inside the 1000-to-2000 segment.
        stepped.PropsAt(208d, props);

        float joined = props.Single().Pose.X;

        joined.ShouldBeGreaterThan(1000f, "from the latch that draws, it interpolates rather than holding");
        joined.ShouldBeLessThanOrEqualTo(2000f);
    }
}
