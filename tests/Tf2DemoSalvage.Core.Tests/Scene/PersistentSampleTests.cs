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
    private static List<SceneProp> Fresh(double tick, IReadOnlySet<int>? interpolate)
    {
        DemoTimeline cold = DemoTimeline.ForTracks(Cast());

        List<SceneProp> props = [];

        cold.PropsAt(tick, props, interpolate);

        return props;
    }

    private static void ShouldMatchAFreshSample(
        List<SceneProp> stepped, double tick, IReadOnlySet<int>? interpolate)
    {
        List<SceneProp> fresh = Fresh(tick, interpolate);

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

            ShouldMatchAFreshSample(props, tick, interpolate: null);
        }
    }

    /// <remarks>
    /// Same differential under an interpolation set, because the set chooses <c>Held</c> over
    /// <c>At</c> per track and the two have different wake schedules. The set is FIXED for the
    /// whole run: what happens when it changes mid-flight is the engine-semantics test below,
    /// not this one.
    /// </remarks>
    [Test]
    public void PropsAt_SteppedForwardWithAFixedInterpolationSet_MatchesAFreshTimelineEverywhere()
    {
        DemoTimeline stepped = DemoTimeline.ForTracks(Cast());

        HashSet<int> blending = [1, 4, 5];

        List<SceneProp> props = [];

        for (double tick = 0d; tick <= 320d; tick += 0.5)
        {
            stepped.PropsAt(tick, props, blending);

            ShouldMatchAFreshSample(props, tick, blending);
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

            ShouldMatchAFreshSample(props, tick, interpolate: null);
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

        ShouldMatchAFreshSample(props, 290d, interpolate: null);

        stepped.PropsAt(291d, props);

        ShouldMatchAFreshSample(props, 291d, interpolate: null);
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
    /// team 2 until tick 150 and team 3 after; a team-2 door must flip from friendly to enemy.
    /// </remarks>
    [Test]
    public void PropsAt_SteppedAcrossARecorderTeamSwitch_RebuildsOfRecordersTeam()
    {
        List<ScenePropTrack> cast = Cast();

        cast[1].TeamNumber = 2;

        DemoTimeline stepped = DemoTimeline.ForTracks(
            cast,
            [
                new TimelineFrame(0, [], RecorderTeam: 2),
                new TimelineFrame(150, [], RecorderTeam: 3),
            ]);

        List<SceneProp> props = [];

        stepped.PropsAt(100d, props);

        props.Single(prop => prop.EntityIndex == 2).OfRecordersTeam
            .ShouldBeTrue("at tick 100 the recorder is on team 2, the door's own side");

        stepped.PropsAt(200d, props);

        props.Single(prop => prop.EntityIndex == 2).OfRecordersTeam
            .ShouldBeFalse("from tick 150 the recorder is on team 3, so the door is enemy");
    }

    /// <remarks>
    /// **The engine's join rule, and the one place stage C is ALLOWED to differ from the old
    /// per-frame recomputation.** `OnLatchInterpolatedVariables` consults `ShouldInterpolate()`
    /// when an UPDATE arrives (`c_baseentity.cpp:2832`) — an entity that becomes visible between
    /// updates keeps its held value until the next update re-latches it. Our updates are
    /// keyframes: a prop granted visibility mid-segment therefore holds until the segment's next
    /// boundary rather than starting to lerp on the very next frame. The old code lerped
    /// immediately, which is the divergence this test pins down.
    /// </remarks>
    [Test]
    public void PropsAt_VisibilityGrantedMidSegment_JoinsTheLerpAtTheNextKeyframeNotInstantly()
    {
        ScenePropTrack mover = new(entityIndex: 1, "models/props/cart.mdl");

        mover.Add(0, new ScenePose { X = 0f });
        mover.Add(100, new ScenePose { X = 1000f });

        DemoTimeline stepped = DemoTimeline.ForTracks([mover]);

        HashSet<int> nobody = [];
        HashSet<int> theMover = [1];

        List<SceneProp> props = [];

        // Held, and parked: the wake at tick 100 consulted the set and found the mover excluded.
        stepped.PropsAt(100.5d, props, nobody);

        props.Single().Pose.X.ShouldBe(0f, "excluded from interpolation, the prop holds");

        // Visibility arrives BETWEEN keyframes. The engine would not re-latch here and neither do
        // we: the pose stays held until the next boundary.
        stepped.PropsAt(101d, props, theMover);

        props.Single().Pose.X.ShouldBe(
            0f,
            "visibility between updates does not join the lerp - the engine consults "
            + "ShouldInterpolate when an update latches, not per frame");

        // The window for the 0->100 segment closes at tick 107 (keyframe plus the 7-tick sampling
        // delay), which is this track's next boundary: from there the sampled pose is the final
        // keyframe's.
        stepped.PropsAt(108d, props, theMover);

        props.Single().Pose.X.ShouldBe(1000f, "from the next boundary the new pose is served");
    }
}
