using System;
using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// The two searches IVP's time-of-impact routines run over a step: conservative advancement on a
/// 200-per-second lattice, and a refinement that brackets and closes on the target (B369).
/// </summary>
/// <remarks>
/// **Read from the disassembly of `vphysics.dll`** — `docs/findings/51`, *The root finder* and *The refining
/// finder*.
///
/// <code>
///   FUN_1800b6590 (Refine)
///     d ≤ target                                    → event at start
///     (float)(start − end) + (d − target)·inv > 0   → no event
///     march: step = 2·(d − target)·inv, re-derived each time; ticks = max(1, (int)(step·200));
///            t += (double)((float)ticks · 0.005f); at or under target → regula falsi;
///            still above at a tick total of 20 → no event
///     regula falsi: 0.375 blend on passes with i &amp; 3 == 3, cap checked there alone, past 64 → t_a;
///            |d − target| &lt; 1e-8 (metres) → done
///   FUN_1800b6210 (Advance)
///     d0 &gt; target → Refine at once
///     march: step = max(0, (d − tolerance)·(1 / speed)), clamped to the end;
///            d &gt; target → Refine from here; d ≤ d0 → event at the PREVIOUS lattice time
/// </code>
///
/// **The distances here are in metres**, matching the engine's own, so the `1e-8` convergence is the engine's
/// value, uncarried.
/// </remarks>
public sealed class IvpRootFinderConformanceTests
{
    private const double Untouched = 99d;

    private const double Target = 5d;

    private static IvpRigidBody Body(double height, float verticalSpeed) => new()
    {
        Position = (0d, 0d, height),
        PreviousVelocity = (0f, 0f, verticalSpeed),
        Orientation = (0f, 0f, 0f, 1f),
        WorkingOrientation = (0f, 0f, 0f, 1f),
        LastStepped = 0d,
        InverseStep = 66f,
    };

    private static IvpMotionCache Moving(IvpRigidBody body) =>
        new(body, IvpMatrix.FromRotation(body.Orientation, body.Position), resting: false);

    private static IvpMotionCache Floor()
    {
        IvpRigidBody floor = Body(0d, 0f);
        return new IvpMotionCache(floor, IvpMatrix.FromRotation(floor.Orientation, floor.Position), resting: true);
    }

    /// <summary>The height of the first body above the second, with a stated bound on how fast it can close.</summary>
    private sealed class Height(double approachSpeed) : IIvpDistanceEvaluator
    {
        public int Evaluations { get; private set; }

        public double ApproachSpeed => approachSpeed;

        public double InverseApproachSpeed => 1d / approachSpeed;

        public double Distance(IvpMatrix first, IvpMatrix second)
        {
            Evaluations++;
            return first.Translation.Z - second.Translation.Z;
        }
    }

    /// <summary>
    /// A distance chosen per lattice tick. The first body rises from zero at 100 units a second, so its height
    /// names the time and the time names the tick.
    /// </summary>
    private sealed class ByTick(double approachSpeed, params double[] distances) : IIvpDistanceEvaluator
    {
        public double ApproachSpeed => approachSpeed;

        public double InverseApproachSpeed => 1d / approachSpeed;

        public double Distance(IvpMatrix first, IvpMatrix second) =>
            distances[(int)Math.Round(first.Translation.Z / 100d / 0.005d)];
    }

    /// <summary>
    /// One above the target at the start, far below at the march's first tick, and one above at every
    /// regula falsi estimate after — so nothing ever converges, and every estimate evaluated becomes the new
    /// time above. Records the height it last measured the first body at.
    /// </summary>
    /// <remarks>
    /// **−45 makes each estimate a fifty-first of the bracket**, `(5 − 6) / (−45 − 6)`, the fraction that keeps
    /// the last unevaluated estimate furthest from the last evaluated one after 67 passes — about a hundredth
    /// of a millimetre of height, tens of float steps, where a half would have collapsed the bracket into
    /// adjacent doubles.
    /// </remarks>
    private sealed class NeverConverging : IIvpDistanceEvaluator
    {
        public int Evaluations { get; private set; }

        public double LastHeight { get; private set; }

        public double ApproachSpeed => 1e6d;

        public double InverseApproachSpeed => 1d / 1e6d;

        public double Distance(IvpMatrix first, IvpMatrix second)
        {
            Evaluations++;
            LastHeight = first.Translation.Z;
            return Evaluations == 2 ? -45d : 6d;
        }
    }

    /// <summary>Returns a scripted distance per call, ignoring where it is asked — for pinning the convergence loop's iteration count.</summary>
    private sealed class Scripted(double approachSpeed, params double[] distances) : IIvpDistanceEvaluator
    {
        public int Evaluations { get; private set; }

        public double ApproachSpeed => approachSpeed;

        public double InverseApproachSpeed => 1d / approachSpeed;

        public double Distance(IvpMatrix first, IvpMatrix second) => distances[Evaluations++];
    }

    /// <remarks>
    /// **At or under the target already: the event is the start**, and the time is written.
    /// </remarks>
    [Test]
    public void Refine_AlreadyAtTheTarget_IsAnEventAtTheStart()
    {
        double time = Untouched;

        IvpRootFinder.Refine(new Height(200d), Target, 0d, 0.1d, 0, Moving(Body(5d, -100f)), Floor(), null, ref time)
            .ShouldBeTrue();

        time.ShouldBe(0d);
    }

    /// <remarks>
    /// **Too far to close inside the interval at the stated bound:** five units at ten a second is half a
    /// second, against an interval of a tenth. No event, and the out time is left as it was.
    /// </remarks>
    [Test]
    public void Refine_TooFarToCloseWithinTheInterval_IsNoEventAndLeavesTheTime()
    {
        double time = Untouched;

        IvpRootFinder.Refine(new Height(10d), Target, 0d, 0.1d, 0, Moving(Body(10d, 0f)), Floor(), null, ref time)
            .ShouldBeFalse();

        time.ShouldBe(Untouched);
    }

    /// <remarks>
    /// **A pair that crosses inside the interval is found at the crossing.** Ten units up, falling at 100 a
    /// second, reaches five at 0.05.
    /// </remarks>
    [Test]
    public void Refine_APairCrossingTheTargetInsideTheInterval_FindsTheCrossing()
    {
        double time = Untouched;

        IvpRootFinder.Refine(new Height(200d), Target, 0d, 0.1d, 0, Moving(Body(10d, -100f)), Floor(), null, ref time)
            .ShouldBeTrue();

        time.ShouldBe(0.05d, 1e-8d);
    }

    /// <remarks>
    /// **Twenty ticks and no crossing is no event.** A body that never moves, with a bound so loose each step
    /// is one tick, is evaluated once at the start and once at each of ticks 1 through 20: 21 evaluations,
    /// then it gives up although the interval runs to 0.2.
    /// </remarks>
    [Test]
    public void Refine_StillAboveTheTargetAtTwentyTicks_GivesUp()
    {
        Height evaluator = new(1e6d);
        double time = Untouched;

        IvpRootFinder.Refine(evaluator, Target, 0d, 0.2d, 0, Moving(Body(10d, 0f)), Floor(), null, ref time)
            .ShouldBeFalse();

        evaluator.Evaluations.ShouldBe(21);
    }

    /// <remarks>
    /// **The step is re-derived from each distance and doubled once.** Five units above at a bound of 500 is
    /// `2 × 5 / 500` = 0.02, four ticks, every time: ticks 4, 8, 12, 16 and 20 — six evaluations with the
    /// start. A step that kept doubling would take 4 then 8 and overrun the cache at 28; one never doubled
    /// would take two ticks and evaluate eleven times.
    /// </remarks>
    [Test]
    public void Refine_AConstantDistance_StepsTheSameNumberOfTicksEachTime()
    {
        Height evaluator = new(500d);
        double time = Untouched;

        IvpRootFinder.Refine(evaluator, Target, 0d, 0.2d, 0, Moving(Body(10d, 0f)), Floor(), null, ref time)
            .ShouldBeFalse();

        evaluator.Evaluations.ShouldBe(6);
    }

    /// <remarks>
    /// **The cap is checked only on passes with both low bits set, and past it the answer is the last time
    /// still ABOVE the target.** One evaluation at the start, one at the march's first tick, then regula falsi
    /// evaluates on passes 0 through 66 and stops at pass 67 — the first pass past 64 that checks: 69 in all.
    /// A cap checked on every pass would stop at 65 and total 67. Every estimate here is above the target, so
    /// the last one evaluated IS `t_a`, and the answer's height is exactly the height last measured; returning
    /// the unevaluated estimate of pass 67 instead lands a fifty-first of the bracket further on.
    /// </remarks>
    [Test]
    public void Refine_ADistanceThatNeverConverges_StopsAtPassSixtySevenOnTheLastTimeAbove()
    {
        IvpRigidBody body = Body(0d, 100f);
        NeverConverging evaluator = new();
        double time = Untouched;

        IvpRootFinder.Refine(evaluator, Target, 0d, 0.1d, 0, Moving(body), Floor(), null, ref time).ShouldBeTrue();

        evaluator.Evaluations.ShouldBe(69);
        Moving(body).Fresh(time).Translation.Z.ShouldBe(evaluator.LastHeight);
    }

    /// <remarks>
    /// **The convergence floor is `1e-8` metres, `DAT_1800fb100`.** Above by `1` at the start (distance `6`, target
    /// `5`), the march's one step — doubled once to `0.01`, two ticks — measures `4`, at or under target, and
    /// brackets it. Regula falsi's first estimate is `(0.01 · −1) / (−2) = 0.005`; the scripted distance there is
    /// `5.0000001`, `1e-7` over the target — over the metre floor, so the correct code keeps iterating and a third
    /// scripted distance, exactly `5`, converges on a second estimate. The inches-scaled floor, `3.937e-7`, would
    /// let that same `1e-7` gap pass on the FIRST estimate, so a mutant multiplying the floor by
    /// <see cref="IvpTransform.InchesPerMetre"/> stops at two evaluations instead of three — the two estimates land
    /// within a float epsilon of each other, so the count catches it where the time would not.
    /// </remarks>
    [Test]
    public void Refine_AGapBetweenTheFloorAndItsInchesScaling_TakesAnExtraIterationOnlyAtTheMetreFloor()
    {
        Scripted evaluator = new(200d, 4d, 5.0000001d, 5d);
        double time = Untouched;

        IvpRootFinder.Refine(evaluator, Target, 0d, 0.1d, 0, Moving(Body(0d, 0f)), Floor(), 6d, ref time).ShouldBeTrue();

        evaluator.Evaluations.ShouldBe(3);
        time.ShouldBe(0.005d, 1e-6d);
    }

    /// <remarks>
    /// **Starting above the target, the advancing search hands everything to the refinement**, so it finds
    /// the same crossing at 0.05.
    /// </remarks>
    [Test]
    public void Advance_StartingAboveTheTarget_FindsTheCrossingThroughTheRefinement()
    {
        double time = Untouched;

        IvpRootFinder.Advance(
            new Height(200d), Target, 1d, 0d, 0.1d, Moving(Body(10d, -100f)), Floor(), null, ref time).ShouldBeTrue();

        time.ShouldBe(0.05d, 1e-8d);
    }

    /// <remarks>
    /// **Inside the target and closing: the event is the lattice time BEFORE the one that showed it**, which
    /// for the first step is the start. Four units up falling at 100: `(4 − 1) / 200` is three ticks, the
    /// distance there is 2.5, no more than the 4 it started at.
    /// </remarks>
    [Test]
    public void Advance_InsideTheTargetAndClosing_IsAnEventAtThePreviousLatticeTime()
    {
        double time = Untouched;

        IvpRootFinder.Advance(
            new Height(200d), Target, 1d, 0d, 0.1d, Moving(Body(4d, -100f)), Floor(), null, ref time).ShouldBeTrue();

        time.ShouldBe(0d);
    }

    /// <remarks>
    /// **The previous lattice time, not the current one and not the start.** Distances 4.0, 4.5, then 3.9 at
    /// one tick a step: tick 2 is at or below the starting 4.0, so the event is tick 1's time, `(double)0.005f`.
    /// </remarks>
    [Test]
    public void Advance_ADistanceFallingBelowTheStartAtTickTwo_IsAnEventAtTickOne()
    {
        double time = Untouched;

        IvpRootFinder.Advance(
            new ByTick(1e6d, 4.0d, 4.5d, 3.9d), Target, 1d, 0d, 0.1d, Moving(Body(0d, 100f)), Floor(), null, ref time)
            .ShouldBeTrue();

        time.ShouldBe((double)0.005f);
    }

    /// <remarks>
    /// **Every distance is compared with the one at the START, not the previous one.** 4.0, then 4.5, 4.2,
    /// 4.6, 4.8, 4.8: tick 2 dips below tick 1 but stays above the start, so the search carries on to the end
    /// of the interval and finds nothing. Comparing with the previous distance would stop at tick 2.
    /// </remarks>
    [Test]
    public void Advance_ADistanceThatDipsButStaysAboveTheStart_CarriesOnToNoEvent()
    {
        double time = Untouched;

        IvpRootFinder.Advance(
            new ByTick(1e6d, 4.0d, 4.5d, 4.2d, 4.6d, 4.8d, 4.8d), Target, 1d, 0d, 0.02d, Moving(Body(0d, 100f)), Floor(),
            null, ref time).ShouldBeFalse();

        time.ShouldBe(Untouched);
    }
}
