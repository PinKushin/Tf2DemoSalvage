using System;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// The pair of samples an interpolation runs between is chosen by CHANGETIME, not by arrival.
/// </summary>
/// <remarks>
/// **`GetInterpolationInfo` walks the history newest-first and compares CHANGETIMES**
/// (<c>interpolatedvar.h:815-845</c>):
///
/// <code>
/// float targettime = currentTime - interpolation_amount;
/// for ( int i = 0; i &lt; varHistory.Count(); i++ )
/// {
///     pInfo-&gt;older = i;
///     float older_change_time = m_VarHistory[ i ].changetime;
///     if ( targettime &lt; older_change_time ) { pInfo-&gt;newer = pInfo-&gt;older; continue; }
///     …
///     float dt = newer_change_time - older_change_time;
///     if ( dt &gt; 0.0001f )
///         pInfo-&gt;frac = ( targettime - older_change_time ) / dt;
/// </code>
///
/// **So `older` and `newer` are guaranteed to BRACKET the target in changetime**, whatever order the
/// entries arrived in: the walk keeps going while an entry's changetime is later than the target and
/// stops at the first one at or before it. A pair chosen by ARRIVAL adjacency has no such guarantee,
/// and two arrival-adjacent entries can share a changetime or carry a decreasing one — at which point
/// `dt` is zero or negative and the interpolation collapses.
///
/// **Measured on `tf2-2026-pub-pov-clean`, entity 9, 27,478 keyframes:** 26,064 apply away from their
/// arrival tick and **1,631 apply EARLIER than the keyframe before them**. Over thirty ticks around
/// tick 5010 the drawn position moved 265.7 units — an average of 0.89 units per tenth-tick step —
/// with a worst single-step jump of **32.3 units** and eighteen stalls. That is the owner's report:
/// *"the demos are kinda jittery and its not a FPS thing"*.
///
/// The shapes below are taken from that measurement rather than invented:
/// `5013 applied 5015` beside `5014 applied 5015`, and `5010 applied 5012` beside `5010 applied 5011`.
/// </remarks>
public sealed class InterpolationNeighbourConformanceTests
{
    /// <summary>The track's own render delay, taken from it rather than restated (B267).</summary>
    private static readonly int Delay =
        ScenePropTrack.DelayTicksFor(ScenePropTrack.Tf2TickInterval);

    [Test]
    public void At_TwoArrivalsSharingOneSimulationTime_KeepsMoving()
    {
        // **The measured shape: `5013 applied 5015` beside `5014 applied 5015`.** Arrival-adjacent,
        // one changetime. A pair chosen by arrival has dt = 0 and nothing to interpolate over; the
        // engine's walk skips past both to the entry before them and interpolates over a real span.
        ScenePropTrack track = Moving(
            (0, 0, 0f),
            (10, 10, 100f),
            (11, 12, 200f),
            (12, 12, 200f),
            (13, 14, 300f));

        // **Sampled at tick 11, STRICTLY inside the span from changetime 10 to changetime 12.**
        // Sampling at 10 was tried and asserts nothing: the target then sits exactly on the older
        // changetime, so `frac` is legitimately zero and the engine returns the older value too.
        ScenePose at = track.At(11d + Delay)!.Value;

        at.X.ShouldBeGreaterThan(100f, "a shared simulation time must not stall the interpolation");
        at.X.ShouldBeLessThan(200f);
    }

    [Test]
    public void At_ASimulationTimeThatGoesBackwards_StillBracketsTheMoment()
    {
        // **The measured shape: `5010 applied 5012` beside `5010 applied 5011`.** The later arrival
        // carries the EARLIER changetime, so an arrival-ordered pair runs backwards and `dt` is
        // negative. The engine cannot produce that: it compares changetimes to the target.
        ScenePropTrack track = Moving(
            (0, 0, 0f),
            (10, 12, 200f),
            (10, 11, 100f),
            (14, 14, 300f));

        // Tick 12, strictly between the surviving changetimes 11 and 14 — for the same reason as
        // above, a sample landing on a changetime asserts nothing.
        ScenePose at = track.At(12d + Delay)!.Value;

        at.X.ShouldBeGreaterThan(100f, "a backwards simulation time must not invert the span");
        at.X.ShouldBeLessThanOrEqualTo(300f);
    }

    [Test]
    public void At_AnOrdinaryTrack_IsUnchangedByTheNeighbourSearch()
    {
        // **The control, and the reason it is not redundant.** Every assertion above is about the
        // awkward case; without a clean track asserting an exact blend, a search that simply reached
        // further back would satisfy them all and quietly smear every ordinary interpolation.
        //
        // Sampling at `10 + Delay` draws tick 10, which is the 10-unit-per-tick span's start.
        ScenePropTrack track = Moving((0, 0, 0f), (10, 10, 100f), (20, 20, 200f));

        track.At(10d + Delay)!.Value.X.ShouldBe(100f, 0.01d);

        // And five ticks later is exactly half way to the next.
        track.At(15d + Delay)!.Value.X.ShouldBe(150f, 0.01d);
    }

    [Test]
    public void At_EveryStepAcrossAJitteryTrack_MovesByASimilarAmount()
    {
        // **The assertion that names the SYMPTOM rather than the mechanism.** The measured fault was
        // a 32-unit step among 0.89-unit steps, so this walks a track carrying both awkward shapes
        // and requires no step to exceed a small multiple of the average — which is what "not
        // jittery" means and what no per-pair assertion can state.
        ScenePropTrack track = Moving(
            (0, 0, 0f),
            (10, 10, 10f),
            (11, 12, 20f),
            (12, 12, 20f),
            (13, 14, 30f),
            (14, 15, 40f),
            (15, 15, 40f),
            (16, 17, 50f),
            (17, 18, 60f),
            (18, 18, 60f),
            (19, 20, 70f),
            (20, 21, 80f));

        float? last = null;
        float previousStep = 0f;
        float worst = 0f;
        float total = 0f;
        int steps = 0;

        for (int step = 0; step <= 100; step++)
        {
            double tick = 10d + Delay + (step / 10d);

            if (track.At(tick) is not { } pose)
            {
                continue;
            }

            if (last is { } before)
            {
                float moved = Math.Abs(pose.X - before);

                total += moved;
                steps++;

                if (steps > 1)
                {
                    worst = Math.Max(worst, Math.Abs(moved - previousStep));
                }

                previousStep = moved;
            }

            last = pose.X;
        }

        steps.ShouldBeGreaterThan(50);

        float average = total / steps;

        // **Four times the average, not two.** A keyframe boundary legitimately changes the drawn
        // speed — the entity really did accelerate — so the bound has to allow a genuine change of
        // pace while refusing a teleport. The measured fault was thirty-six times the average.
        worst.ShouldBeLessThan(
            average * 4f,
            $"no single step may jump: average {average}, worst change {worst}");
    }

    /// <summary>A track whose keyframes state an arrival tick, a simulation tick and an X.</summary>
    /// <remarks>
    /// **Arrival and simulation given separately, because that is the whole subject.** A fixture
    /// that let them agree could not express either measured shape, and a test written on one would
    /// pass against a search on either clock.
    /// </remarks>
    private static ScenePropTrack Moving(params (int Tick, int AppliedAt, float X)[] keyframes)
    {
        ScenePropTrack track = new(entityIndex: 3, "models/props/crate.mdl");

        foreach ((int tick, int appliedAt, float x) in keyframes)
        {
            track.Add(tick, new ScenePose { X = x }, appliedAt: appliedAt);
        }

        return track;
    }
}
