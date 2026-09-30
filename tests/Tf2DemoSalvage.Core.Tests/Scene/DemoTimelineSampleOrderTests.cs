using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// A sample's answer is a function of its tick and its parameters alone, never of which call came
/// before it (RISKS B438).
/// </summary>
/// <remarks>
/// **`PropsAt` keeps state between calls** — the wake queue, the lerp list, the tick it last sampled,
/// and each track's served prop — so it can pay only for what changed (B259, stage C). That state is
/// a cache, and a cache must never change an answer: whatever order the ticks are asked in, and by
/// however many callers, each must get what a timeline built fresh answers cold.
///
/// **Two instruments, for two faults that look alike.** The single-threaded tests below interleave
/// callers explicitly, so an answer that depends on the previous query shows without any timing.
/// The last test runs callers on real threads against one shared timeline, which is how the corpus
/// suite uses `TimelineCache`; its control runs the same schedules with a timeline per thread, on a
/// cast whose stepping is already exact, so the only variable it isolates is the sharing.
///
/// **Each comparison is against a fresh timeline with fresh tracks.** A track carries sampling state
/// of its own (`Live`, `Lerping`), so a "fresh" timeline handed the same track objects would read the
/// other one's answers — `PersistentSampleTests` says the same.
/// </remarks>
public sealed class DemoTimelineSampleOrderTests
{
    /// <summary>A shared empty list, so two samples compare their pose parameters by value.</summary>
    private static readonly float[] NoParameters = [];

    /// <summary>
    /// A prop that never moves while its animation plays — a cycle stated every ten ticks.
    /// </summary>
    /// <remarks>
    /// **The origin settles while the cycle does not**, which is the whole point of the cast: the
    /// sampler's own interpolation history is identical from entry to entry while the animation
    /// history carries a new value each time.
    /// </remarks>
    private static List<ScenePropTrack> AnimatingInPlace()
    {
        ScenePropTrack spinner = new(entityIndex: 1, "models/props_gameplay/spinner.mdl");

        for (int tick = 0; tick <= 90; tick += 10)
        {
            spinner.Add(tick, new ScenePose { X = 64f, Cycle = tick / 100f });
        }

        return [spinner];
    }

    /// <summary>
    /// **The user's interleave: tick A, tick B, tick A — and the second A must answer as the first
    /// did.** Two callers share one timeline; neither knows the other exists.
    /// </summary>
    /// <remarks>
    /// A caller asking tick 14 cold gets the cycle blended six-tenths of the way from 0.0 to 0.1 —
    /// the target is `14 - 8 = 6` on changetimes 0 and 10. Another caller then asks tick 10. Asked
    /// tick 14 again, the first caller must get the same 0.06, because nothing about tick 14 changed:
    /// only somebody else's query did.
    /// </remarks>
    [Test]
    public void PropsAt_TwoCallersInterleavedOnOneTimeline_EachGetsTheFreshAnswer()
    {
        DemoTimeline shared = DemoTimeline.ForTracks(AnimatingInPlace());

        List<SceneProp> first = [];
        List<SceneProp> other = [];
        List<SceneProp> again = [];

        shared.PropsAt(14d, first);
        shared.PropsAt(10d, other);
        shared.PropsAt(14d, again);

        ShouldMatch(first, Fresh(AnimatingInPlace, 14d), "at tick 14, asked first");
        ShouldMatch(other, Fresh(AnimatingInPlace, 10d), "at tick 10, asked by the other caller");
        ShouldMatch(again, Fresh(AnimatingInPlace, 14d), "at tick 14, asked again after tick 10");

        // The prediction itself, not only the agreement: changetimes 0 and 10, target 6.
        again.Single().Pose.Cycle.ShouldBe(0.06f, 1e-6f, "the same query must give the same answer");
    }

    /// <summary>
    /// **The same fault from playback's side**: stepped forward, a sentry-style pose parameter must
    /// blend every frame, not hold between packets.
    /// </summary>
    /// <remarks>
    /// The cycle here never changes, so the only moving quantity is the one pose parameter — the
    /// history <see cref="ScenePropTrack"/> keeps beside the cycle's (B382). Anything that decides a
    /// prop has settled by asking the other two histories freezes this one between updates.
    /// </remarks>
    [Test]
    public void PropsAt_SteppedWhileAStationaryPropsPoseParameterBlends_MatchesAFreshTimelineEverywhere()
    {
        static List<ScenePropTrack> Turning()
        {
            ScenePropTrack sentry = new(entityIndex: 1, "models/buildables/sentry1.mdl");

            for (int tick = 0; tick <= 90; tick += 10)
            {
                sentry.Add(tick, new ScenePose { X = 64f, PoseParameters = [tick / 100f] });
            }

            return [sentry];
        }

        DemoTimeline stepped = DemoTimeline.ForTracks(Turning());

        List<SceneProp> props = [];

        for (double tick = 0d; tick <= 100d; tick += 0.5)
        {
            stepped.PropsAt(tick, props);

            ShouldMatch(props, Fresh(Turning, tick), $"at tick {tick}");
        }
    }

    /// <summary>
    /// **A parent's interpolation turns on its children, so a child's update must reach it.**
    /// </summary>
    /// <remarks>
    /// `ShouldInterpolate`'s fourth clause (`c_baseentity.cpp:3029`): a mover that draws nothing
    /// itself is interpolated when something hanging off it is. The door here declares
    /// <c>kRenderNone</c>, and its grate is hidden until tick 102 — two ticks after the door's last
    /// update. Asked cold at tick 104, the grate is visible, so the door blends: target 96 of its
    /// 0-to-100 segment. A caller that asked tick 101 first must get the same.
    /// </remarks>
    [Test]
    public void PropsAt_AMoveChildShownBetweenItsParentsUpdates_MatchesAFreshTimeline()
    {
        static List<ScenePropTrack> DoorAndGrate()
        {
            ScenePropTrack door = new(entityIndex: 2, "*7");

            door.Add(0, new ScenePose { X = 0f, RenderMode = RenderModes.None });
            door.Add(100, new ScenePose { X = 100f, RenderMode = RenderModes.None });

            ScenePropTrack grate = new(entityIndex: 3, "models/props_gameplay/grate.mdl")
            {
                AttachedTo = 2,
            };

            grate.Add(0, new ScenePose { Hidden = true });
            grate.Add(102, new ScenePose { Hidden = false });

            return [door, grate];
        }

        DemoTimeline shared = DemoTimeline.ForTracks(DoorAndGrate());

        List<SceneProp> props = [];

        shared.PropsAt(101d, props);
        shared.PropsAt(104d, props);

        ShouldMatch(props, Fresh(DoorAndGrate, 104d), "at tick 104, asked after tick 101");

        props.Single(prop => prop.EntityIndex == 2).Pose.X
            .ShouldBe(96f, 1e-4f, "the grate is showing, so the door blends toward its update");
    }

    /// <summary>
    /// **Who the view is attached to is part of the question**, so changing it must not be served
    /// the answer to the old one.
    /// </summary>
    /// <remarks>
    /// `render->GetViewEntity()` is the one clause of `ShouldInterpolate` the caller supplies
    /// (B385). A <c>kRenderNone</c> mover holds its last stated position for everybody else and
    /// blends for the entity whose eyes the view is in: at tick 104, 96 of its 0-to-100 segment.
    /// </remarks>
    [Test]
    public void PropsAt_TheViewEntityChangedBetweenCalls_MatchesAFreshTimeline()
    {
        static List<ScenePropTrack> Mover()
        {
            ScenePropTrack mover = new(entityIndex: 2, "*9");

            mover.Add(0, new ScenePose { X = 0f, RenderMode = RenderModes.None });
            mover.Add(100, new ScenePose { X = 100f, RenderMode = RenderModes.None });

            return [mover];
        }

        DemoTimeline shared = DemoTimeline.ForTracks(Mover());

        List<SceneProp> props = [];

        shared.PropsAt(102d, props, viewEntity: null);
        shared.PropsAt(104d, props, viewEntity: 2);

        List<SceneProp> fresh = [];

        DemoTimeline.ForTracks(Mover()).PropsAt(104d, fresh, viewEntity: 2);

        ShouldMatch(props, fresh, "at tick 104 from the mover's eyes, asked after tick 102 from nobody's");

        props.Single().Pose.X.ShouldBe(96f, 1e-4f, "the view is in the mover, so it blends");
    }

    /// <summary>How many threads sample at once.</summary>
    private const int Threads = 8;

    /// <summary>How many samples each thread takes.</summary>
    private const int SamplesPerThread = 400;

    /// <summary>How many copies of the lifecycle cast make up the crowd.</summary>
    private const int Copies = 20;

    /// <summary>The last tick a schedule reaches before it wraps back to the start.</summary>
    private const double LastSampledTick = 330d;

    /// <summary>
    /// **The second instrument: callers on real threads, one shared timeline, and a control.**
    /// </summary>
    /// <remarks>
    /// Every thread walks its own schedule — forward in steps of one and a half ticks from its own
    /// start, wrapping back to zero — and compares each answer with a cold timeline's. The crowd is
    /// `PersistentSampleTests`' lifecycle cast, twenty times over and phase-shifted, whose stepping is
    /// already known to be exact: so the control arm, a timeline per thread running the same
    /// schedules at the same time, must be clean, and anything the shared arm gets wrong is the
    /// sharing.
    ///
    /// **Dedicated threads released together by a barrier**, because this assembly runs its tests in
    /// parallel and a pool-scheduled loop can end up running its "parallel" work one item at a time.
    /// </remarks>
    [Test]
    public void PropsAt_ManyThreadsSharingOneTimeline_MatchATimelinePerThread()
    {
        List<List<SceneProp>> expected = [];

        for (double tick = 0d; tick <= LastSampledTick; tick += 0.5)
        {
            expected.Add(Fresh(Crowd, tick));
        }

        int control = Mismatches(_ => DemoTimeline.ForTracks(Crowd()), expected);

        control.ShouldBe(0, "a timeline per thread must answer as a cold one, or the harness is wrong");

        DemoTimeline shared = DemoTimeline.ForTracks(Crowd());

        int sharing = Mismatches(_ => shared, expected);

        sharing.ShouldBe(
            0,
            $"{sharing} of {Threads * SamplesPerThread} samples on the shared timeline differed from a "
            + "cold one, where the same schedules on a timeline each differed in none");
    }

    /// <summary>Runs every thread's schedule against the timeline it is handed, counting wrong answers.</summary>
    /// <param name="timelineFor">The timeline thread <c>n</c> samples.</param>
    /// <param name="expected">The cold answer at each half tick from zero.</param>
    /// <returns>How many samples differed from their cold answer.</returns>
    private static int Mismatches(Func<int, DemoTimeline> timelineFor, List<List<SceneProp>> expected)
    {
        int wrong = 0;

        using Barrier start = new(Threads);

        Task[] threads = new Task[Threads];

        for (int thread = 0; thread < Threads; thread++)
        {
            int self = thread;
            DemoTimeline timeline = timelineFor(self);

            threads[thread] = Task.Factory.StartNew(
                () =>
                {
                    List<SceneProp> props = [];

                    // Each thread starts somewhere different, so the shared sample is asked for ticks
                    // on both sides of the last one it served.
                    double tick = (self * 37) % 320;

                    start.SignalAndWait();

                    for (int sample = 0; sample < SamplesPerThread; sample++)
                    {
                        timeline.PropsAt(tick, props);

                        if (!Same(props, expected[(int)(tick * 2d)]))
                        {
                            Interlocked.Increment(ref wrong);
                        }

                        tick = tick + 1.5d > LastSampledTick ? 0d : tick + 1.5d;
                    }
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }

        Task.WaitAll(threads);

        return wrong;
    }

    /// <summary>`PersistentSampleTests`' lifecycle cast, many times over, each copy shifted in time.</summary>
    /// <remarks>
    /// A mover whose lerp windows overlap, a door with a long hold, a crate that never changes, a
    /// latecomer that is born and dies, a ghost with a hidden span, and a barrel that ends — so a
    /// sample has births, deaths, hidden spans and lerps to get wrong. The shift spreads the wakes
    /// so that no two copies change on the same tick.
    /// </remarks>
    private static List<ScenePropTrack> Crowd()
    {
        List<ScenePropTrack> crowd = [];

        for (int copy = 0; copy < Copies; copy++)
        {
            int shift = copy * 3;
            int first = (copy * 6) + 1;

            ScenePropTrack mover = new(first, "models/props/cart.mdl");

            for (int tick = shift; tick <= 300 + shift; tick += 3)
            {
                mover.Add(tick, new ScenePose { X = tick * 2f, Yaw = tick % 360 });
            }

            ScenePropTrack door = new(first + 1, "models/props/door.mdl");

            door.Add(shift, new ScenePose { X = 0f });
            door.Add(150 + shift, new ScenePose { X = 64f });

            ScenePropTrack crate = new(first + 2, "models/props/crate.mdl");

            crate.Add(shift, new ScenePose { X = 10f, Y = 20f });

            ScenePropTrack latecomer = new(first + 3, "models/items/ammopack.mdl");

            latecomer.Add(200 + shift, new ScenePose { X = 5f });
            latecomer.Add(240 + shift, new ScenePose { X = 45f });
            latecomer.End(280 + shift);

            ScenePropTrack ghost = new(first + 4, "models/props/ghost.mdl");

            ghost.Add(shift, new ScenePose { X = 1f });
            ghost.Add(100 + shift, new ScenePose { X = 1f, Hidden = true });
            ghost.Add(180 + shift, new ScenePose { X = 9f });

            ScenePropTrack ender = new(first + 5, "models/props/barrel.mdl");

            ender.Add(shift, new ScenePose { X = 7f });
            ender.End(90 + shift);

            crowd.AddRange([mover, door, crate, latecomer, ghost, ender]);
        }

        return crowd;
    }

    /// <summary>What a timeline built from nothing answers at one tick.</summary>
    private static List<SceneProp> Fresh(Func<List<ScenePropTrack>> cast, double tick)
    {
        List<SceneProp> props = [];

        DemoTimeline.ForTracks(cast()).PropsAt(tick, props);

        return props;
    }

    /// <summary>Whether two samples agree prop for prop, pose parameters compared by value.</summary>
    private static bool Same(List<SceneProp> sampled, List<SceneProp> fresh) =>
        sampled.Count == fresh.Count &&
        sampled.Zip(fresh).All(pair =>
            Comparable(pair.First) == Comparable(pair.Second) &&
            pair.First.Pose.PoseParameters.SequenceEqual(pair.Second.Pose.PoseParameters));

    /// <summary>The same assertion as <see cref="Same"/>, saying which prop and which tick when it fails.</summary>
    private static void ShouldMatch(List<SceneProp> sampled, List<SceneProp> fresh, string when)
    {
        sampled.Count.ShouldBe(fresh.Count, $"prop count {when}");

        for (int index = 0; index < fresh.Count; index++)
        {
            Comparable(sampled[index]).ShouldBe(Comparable(fresh[index]), $"prop {fresh[index].EntityIndex} {when}");

            sampled[index].Pose.PoseParameters.ShouldBe(
                fresh[index].Pose.PoseParameters,
                $"pose parameters of prop {fresh[index].EntityIndex} {when}");
        }
    }

    /// <summary>
    /// A prop with its pose parameters swapped for one shared list, since a blend allocates its own
    /// and a record compares a list by reference.
    /// </summary>
    private static SceneProp Comparable(SceneProp prop) =>
        prop with { Pose = prop.Pose with { PoseParameters = NoParameters } };
}
