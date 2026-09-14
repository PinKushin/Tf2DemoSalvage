using System;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// The two searches IVP's time-of-impact routines run over a step: <c>FUN_1800b6210</c>, conservative
/// advancement on a 200-per-second lattice, and <c>FUN_1800b6590</c>, which brackets the target and closes on
/// it by regula falsi (B369).
/// </summary>
/// <remarks>
/// **Carried in the disassembly's order, narrowings included** (`docs/findings/51`, *The root finder* and *The
/// refining finder*). Every comparison is written so that NaN takes the branch the engine's `COMISD` and
/// jump pair takes: unordered sets the carry flag, so `JBE` and `JC` are taken and `JA` and `JNC` are not.
///
/// **Units are metres and seconds**, matching the engine's own. The `1e-8` serves as both a time slack and a
/// distance tolerance; the distance one is the engine's own value, carried directly.
/// </remarks>
public static class IvpRootFinder
{
    /// <summary><c>DAT_1800feb78</c>.</summary>
    private const double TicksPerSecond = 200d;

    /// <summary><c>DAT_1800fd748</c>, a float.</summary>
    private const float SecondsPerTick = 0.005f;

    /// <summary>The refinement's give-up tick total, compared for equality (<c>CMP R12D, 0x14</c>).</summary>
    private const int LastTick = 20;

    /// <summary><c>DAT_1800fb100</c> added to a remaining time.</summary>
    private const double TimeSlack = 1e-8d;

    /// <summary><c>DAT_1800fb100</c> compared with a distance, which the engine measures in metres.</summary>
    private const double Convergence = 1e-8d;

    /// <summary><c>DAT_1800feb70</c>.</summary>
    private const double Blend = 0.375d;

    /// <summary>The blend is applied, and the cap checked, on passes whose iteration count has both low bits set.</summary>
    private const int BlendMask = 3;

    private const int IterationCap = 64;

    /// <summary><c>DAT_1800ea9b8</c>.</summary>
    private const double One = 1d;

    /// <summary>Advances conservatively through an interval — <c>FUN_1800b6210</c>.</summary>
    /// <param name="evaluator">What is measured.</param>
    /// <param name="target">The distance the search is about.</param>
    /// <param name="tolerance">The distance the conservative step aims short of.</param>
    /// <param name="start">The interval's start time.</param>
    /// <param name="end">The interval's end time.</param>
    /// <param name="first">The first body's motion cache.</param>
    /// <param name="second">The second body's motion cache.</param>
    /// <param name="distance">The distance at <paramref name="start"/> if already known; measured from slot 0 otherwise.</param>
    /// <param name="time">Written with the event's time when there is one, and left alone when there is not.</param>
    /// <returns>Whether there is an event.</returns>
    /// <exception cref="ArgumentNullException">An evaluator or cache is null.</exception>
    /// <remarks>
    /// **It marches only while the pair starts inside the target.** Starting outside hands the whole search to
    /// <see cref="Refine"/>. Inside, each lattice distance is compared with the distance at the START: at or
    /// below it is an event at the PREVIOUS lattice time; above the target hands the rest to the refinement.
    /// </remarks>
    public static bool Advance(
        IIvpDistanceEvaluator evaluator,
        double target,
        double tolerance,
        double start,
        double end,
        IvpMotionCache first,
        IvpMotionCache second,
        double? distance,
        ref double time)
    {
        ArgumentNullException.ThrowIfNull(evaluator);
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        double startDistance = distance ?? evaluator.Distance(first.At(0, start), second.At(0, start));

        if (startDistance > target)
        {
            return Refine(evaluator, target, start, end, 0, first, second, startDistance, ref time);
        }

        if ((float)(start - end) >= 0f)
        {
            return false;
        }

        double inverseSpeed = One / evaluator.ApproachSpeed;
        double current = start;
        double currentDistance = startDistance;
        int ticks = 0;

        while (true)
        {
            double step = (currentDistance - tolerance) * inverseSpeed;

            if ((double)(float)(current - end) + step > 0d)
            {
                step = (double)(float)(end - current) + TimeSlack;
            }
            else
            {
                // MAXSD against zero: zero wins a tie and a NaN.
                step = step > 0d ? step : 0d;
            }

            int stepTicks = Ticks(step);
            ticks += stepTicks;
            double next = LatticeTime(stepTicks, current);
            double measured = evaluator.Distance(first.At(ticks, next), second.At(ticks, next));

            if (measured > target)
            {
                bool past = (float)(next - end) > 0f;

                return !past && Refine(evaluator, target, next, end, ticks, first, second, measured, ref time);
            }

            if (measured > startDistance)
            {
                current = next;
                currentDistance = measured;

                if ((float)(next - end) >= 0f)
                {
                    return false;
                }

                continue;
            }

            time = current;
            return true;
        }
    }

    /// <summary>Brackets the target and closes on it — <c>FUN_1800b6590</c>.</summary>
    /// <param name="evaluator">What is measured.</param>
    /// <param name="target">The distance to find the moment of.</param>
    /// <param name="start">The time to search from.</param>
    /// <param name="end">The interval's end time.</param>
    /// <param name="ticks">The lattice tick total already reached, indexing the same caches.</param>
    /// <param name="first">The first body's motion cache.</param>
    /// <param name="second">The second body's motion cache.</param>
    /// <param name="distance">The distance at <paramref name="start"/> if already known; measured from slot 0 otherwise.</param>
    /// <param name="time">Written with the event's time when there is one, and left alone when there is not.</param>
    /// <returns>Whether there is an event.</returns>
    /// <exception cref="ArgumentNullException">An evaluator or cache is null.</exception>
    /// <remarks>
    /// **Each march step is twice `(distance − target) × evaluator+0x10`, re-derived from the latest distance** —
    /// not a step that keeps doubling. A distance still above the target at a tick total of exactly 20 gives
    /// up. Once bracketed, regula falsi runs on fresh transforms; past its cap it answers with the last time
    /// still above the target.
    /// </remarks>
    public static bool Refine(
        IIvpDistanceEvaluator evaluator,
        double target,
        double start,
        double end,
        int ticks,
        IvpMotionCache first,
        IvpMotionCache second,
        double? distance,
        ref double time)
    {
        ArgumentNullException.ThrowIfNull(evaluator);
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        double startDistance = distance ?? evaluator.Distance(first.At(0, start), second.At(0, start));
        bool above = startDistance > target;

        if (!above)
        {
            time = start;
            return true;
        }

        double inverseSpeed = evaluator.InverseApproachSpeed;
        double step = (startDistance - target) * inverseSpeed;

        if ((double)(float)(start - end) + step > 0d)
        {
            return false;
        }

        Bracket? bracket = March(evaluator, target, end, ticks, first, second, new Bracket(start, startDistance, 0d, 0d), step);

        if (!bracket.HasValue)
        {
            return false;
        }

        double found = RegulaFalsi(evaluator, target, first, second, bracket.Value);

        if ((float)(found - end) > 0f)
        {
            return false;
        }

        time = found;
        return true;
    }

    /// <summary>The refinement's march to the first lattice point at or under the target.</summary>
    private static Bracket? March(
        IIvpDistanceEvaluator evaluator,
        double target,
        double end,
        int ticks,
        IvpMotionCache first,
        IvpMotionCache second,
        Bracket above,
        double step)
    {
        double inverseSpeed = evaluator.InverseApproachSpeed;

        while (true)
        {
            step += step;

            if ((double)(float)(above.AboveTime - end) + step > 0d)
            {
                step = (double)(float)(end - above.AboveTime) + TimeSlack;
            }

            int stepTicks = Ticks(step);
            ticks += stepTicks;
            double next = LatticeTime(stepTicks, above.AboveTime);
            double measured = evaluator.Distance(first.At(ticks, next), second.At(ticks, next));
            bool stillAbove = measured > target;

            if (!stillAbove)
            {
                return above with { BelowTime = next, BelowDistance = measured };
            }

            if (ticks == LastTick)
            {
                return null;
            }

            above = above with { AboveTime = next, AboveDistance = measured };
            step = (measured - target) * inverseSpeed;

            if ((double)(float)(next - end) + step > 0d)
            {
                return null;
            }
        }
    }

    /// <summary>The refinement's regula falsi between a time above the target and one at or under it.</summary>
    private static double RegulaFalsi(
        IIvpDistanceEvaluator evaluator, double target, IvpMotionCache first, IvpMotionCache second, Bracket bracket)
    {
        int iteration = 0;

        while (true)
        {
            double estimate =
                ((double)(float)(bracket.BelowTime - bracket.AboveTime) * (target - bracket.AboveDistance)) /
                (bracket.BelowDistance - bracket.AboveDistance) + bracket.AboveTime;

            if ((iteration & BlendMask) == BlendMask)
            {
                if (iteration > IterationCap)
                {
                    return bracket.AboveTime;
                }

                estimate =
                    ((double)(float)(bracket.BelowTime - estimate) + (double)(float)(bracket.AboveTime - estimate)) *
                    Blend + estimate;
            }

            double measured = evaluator.Distance(first.Fresh(estimate), second.Fresh(estimate));
            double gap = Math.Abs(measured - target);

            if (gap < Convergence || double.IsNaN(gap))
            {
                return estimate;
            }

            iteration++;

            bracket = measured < target
                ? bracket with { BelowTime = estimate, BelowDistance = measured }
                : bracket with { AboveTime = estimate, AboveDistance = measured };
        }
    }

    /// <summary>A step as whole lattice ticks: <c>CVTTSD2SI</c> of <c>step × 200</c>, then <c>CMOVL</c> up to one.</summary>
    /// <remarks>
    /// For NaN the instruction gives <c>int.MinValue</c> and .NET gives zero; both become one. A step too large
    /// for an int cannot arrive, because both searches clamp it to what is left of the interval first.
    /// </remarks>
    private static int Ticks(double step)
    {
        int ticks = (int)(step * TicksPerSecond);
        return ticks < 1 ? 1 : ticks;
    }

    /// <summary>The time a step of whole ticks lands on: <c>(double)((float)ticks × 0.005f) + from</c>.</summary>
    private static double LatticeTime(int stepTicks, double from) =>
        (double)((float)stepTicks * SecondsPerTick) + from;

    /// <summary>The last time above the target and the first at or under it, with their distances.</summary>
    private readonly record struct Bracket(double AboveTime, double AboveDistance, double BelowTime, double BelowDistance);
}
