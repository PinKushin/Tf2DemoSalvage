using System;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>The fields of a core the pair scheduler reads.</summary>
/// <param name="Bounds">The fields the searches read too; the scheduler reads <c>+0x1dc</c> and <c>+0x254</c> of them.</param>
/// <param name="Velocity">The core's linear velocity, <c>+0x140</c>.</param>
/// <param name="RotationAxis">The unit axis <see cref="IvpCoreSpeedBound"/> writes beside the angular bound, <c>+0x1c0</c>.</param>
public readonly record struct IvpSchedulerCore(
    IvpCoreBounds Bounds,
    (float X, float Y, float Z) Velocity,
    (float X, float Y, float Z) RotationAxis);

/// <summary>The environment's fields the pair scheduler reads and writes (B369).</summary>
public sealed class IvpSchedulerEnvironment
{
    /// <summary>The PSI step, <c>env+0x108</c>, which every use narrows to float first.</summary>
    public required double Step { get; init; }

    /// <summary>The environment's time, <c>env+0x188</c>.</summary>
    public required double Now { get; init; }

    /// <summary>The time of the next PSI, <c>env+0x190</c>.</summary>
    public required double NextPsi { get; init; }

    /// <summary><c>DAT_18012d654</c>, <see cref="IvpCollisionTolerance.ClosingSpeedThreshold"/> for the gravity in force.</summary>
    public required float ClosingSpeedThreshold { get; init; }

    /// <summary>The time manager's queue, <c>tm+0x10</c>.</summary>
    public required IvpMinList<IIvpTimeEvent> Queue { get; init; }

    /// <summary>The time manager's base, <c>tm+0x28</c>, which a queued time is taken relative to.</summary>
    public required double QueueBase { get; init; }

    /// <summary>The looks since a margin class last decayed, <c>env+0x13c</c>.</summary>
    public int MarginDecayCounter { get; set; }
}

/// <summary>How the scheduler treats an event within a microsecond of now — its third argument, as its callers pass it.</summary>
/// <remarks><i>The names are INFERRED from the callers</i>: the fire routine passes 1 after a feature change and 2 after a miss.</remarks>
public enum IvpRecheck
{
    /// <summary>0: the event is queued at now.</summary>
    AtNow = 0,

    /// <summary>1: a collision kind rechecks a tenth of the gap over the bound ahead.</summary>
    AfterFeatureChange = 1,

    /// <summary>2: the gap over the bound ahead, whatever the kind.</summary>
    AfterMiss = 2,
}

/// <summary>What the scheduler did with a pair.</summary>
public enum IvpScheduleOutcome
{
    /// <summary>The pair is past its far threshold and was not asked to be removed.</summary>
    Far,

    /// <summary>The pair is past its far threshold and was filed with its objects' hull managers.</summary>
    Filed,

    /// <summary>The pair cannot close this PSI, or is parked.</summary>
    LeftAlone,

    /// <summary>The time of impact raised nothing.</summary>
    NoEvent,

    /// <summary>An event was queued.</summary>
    Queued,

    /// <summary>A recheck fell at or past the next PSI.</summary>
    Dropped,
}

/// <summary>
/// IVP's pair scheduler: whether a mindist can touch this PSI and, if so, when to look at it next —
/// <c>FUN_180099380(mindist, removeFar, recheckMode)</c> (B369).
/// </summary>
/// <remarks>
/// **Read from the disassembly, instruction by instruction** (`docs/findings/51`, *The scheduler's near branch and the
/// dispatch into the search*, *The event queue, and where the far branch hands a pair off* and *The hull manager, and how a
/// far pair is told to look again*). **A far pair asked to be removed is unfiled and filed with its objects' hull
/// managers**; the handler that takes it back when a hull passes it, `FUN_180097f00`, is ported as
/// <see cref="IvpMindistHull.HullPassed"/>. Units are metres and
/// seconds — the `1e-12` gap floor and the `1e-19` closing-speed floor are the engine's own, carried directly.
/// </remarks>
public static class IvpPairScheduler
{
    /// <summary><c>DAT_1800f5108</c>, <c>2.1f</c> widened: how many steps of the total bound a near pair may be from touching.</summary>
    private const double FarReach = 2.1f;

    /// <summary><c>DAT_1800fdf78</c>, <c>1.001f</c> widened.</summary>
    private const double AxisSlack = 1.001f;

    /// <summary><c>DAT_1800f4f20</c>, a closing speed in IVP's metres a second.</summary>
    private const double ClosingFloor = 1e-19d;

    /// <summary><c>DAT_1800eb140</c>: an event this close to now is rechecked instead.</summary>
    private const float NearNow = 1e-6f;

    /// <summary><c>DAT_1800f4f28</c>, a gap in IVP's metres.</summary>
    private const double GapFloor = 1e-12d;

    /// <summary><c>DAT_1800fd578</c>, <c>0.1f</c> widened: the share of the gap a close recheck covers.</summary>
    private const double CloseRecheckShare = 0.1f;

    /// <summary><c>DAT_18012d670</c>, set to <c>1.0</c> at runtime beside the tolerance block.</summary>
    private const double StepScale = 1d;

    /// <summary><c>DAT_1800fdf60</c>, <c>1e-7f</c> widened.</summary>
    private const double CloseRecheckStepShare = 1e-7f;

    /// <summary><c>DAT_1800fdf68</c>, <c>1e-5f</c> widened.</summary>
    private const double CloseFloorStepShare = 1e-5f;

    /// <summary><c>DAT_1800f50f8</c>, <c>1e-4f</c> widened.</summary>
    private const double RecheckStepShare = 1e-4f;

    /// <summary><c>DAT_1800f5100</c>, <c>0.001f</c> widened.</summary>
    private const double FloorStepShare = 0.001f;

    private const int ParkedMask = 0x3000;
    private const int Parked = 0x1000;
    private const int MarginClassMask = 0x3fc00000;
    private const int MarginClassShift = 22;
    private const int KindMask = 0xFF;
    private const int FeatureChangeBits = 0xF;

    /// <summary>Examines a pair, as <c>FUN_180099380</c> does.</summary>
    /// <param name="mindist">The pair; its queue slot, flags and margin class are written.</param>
    /// <param name="first">Synapse record 0's core.</param>
    /// <param name="second">Synapse record 1's core.</param>
    /// <param name="environment">The step, the times, the threshold, the queue and the decay counter.</param>
    /// <param name="removeFar">
    /// The second argument: set, a far pair is unfiled from this manager and filed with these objects' hull managers; null,
    /// it is left where it is.
    /// </param>
    /// <param name="recheck">The third argument: what an event within a microsecond of now becomes.</param>
    /// <param name="timeOfImpact">
    /// The table's routine for the pair's kinds, handed the context and the mindist state after the margin class decays —
    /// <see cref="IvpImpactDispatch.Search"/> over the pair's sides.
    /// </param>
    /// <returns>What was done.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// The flags name a synapse record past the two, or a far pair asked to be removed is not exact.
    /// </exception>
    public static IvpScheduleOutcome Examine(
        IvpMindist mindist,
        IvpSchedulerCore first,
        IvpSchedulerCore second,
        IvpSchedulerEnvironment environment,
        IvpFarFiling? removeFar,
        IvpRecheck recheck,
        Func<IvpImpactContext, IvpMindistState, IvpImpact> timeOfImpact)
    {
        ArgumentNullException.ThrowIfNull(mindist);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(timeOfImpact);

        if ((mindist.Flags & 0x200) != 0)
        {
            throw new InvalidOperationException("A mindist's flags name a synapse record past its two.");
        }

        IvpSchedulerCore coreA = mindist.SynapseA == 0 ? first : second;
        IvpSchedulerCore coreB = mindist.SynapseA == 0 ? second : first;

        double surface = coreB.Bounds.SurfaceSpeedBound + coreA.Bounds.SurfaceSpeedBound;
        double totalBound = (double)coreA.Bounds.LinearSpeed + surface + (double)coreB.Bounds.LinearSpeed;

        if (mindist.QueueSlot is int queued)
        {
            environment.Queue.Remove(queued);
            mindist.QueueSlot = null;
        }

        float margin = IvpCollisionTolerance.MarginFor(mindist.MarginClass);
        double length = mindist.Length;

        if (length > ((double)(float)environment.Step * totalBound * FarReach) + margin)
        {
            if (removeFar is not IvpFarFiling filing)
            {
                return IvpScheduleOutcome.Far;
            }

            filing.Manager.Unlink(mindist, environment.Queue);
            float gap = (float)((double)mindist.Length - (double)margin);
            IvpMindistHull.FileFar(mindist, filing, first.Bounds, second.Bounds, environment.Now, gap);
            return IvpScheduleOutcome.Filed;
        }

        double closing = ClosingSpeed(mindist.Normal, coreA, coreB);

        // COMISD then JNC, then COMISD then JC: searched when at or over either floor, and never for a NaN.
        bool closingEnough = closing >= environment.ClosingSpeedThreshold || closing >= ClosingFloor;

        if (!closingEnough)
        {
            return IvpScheduleOutcome.LeftAlone;
        }

        double now = environment.Now;
        double end = environment.NextPsi;

        if (length >= ((double)(float)(end - now) * closing) + margin || (mindist.Flags & ParkedMask) == Parked)
        {
            return IvpScheduleOutcome.LeftAlone;
        }

        DecayMarginClass(mindist, environment);

        IvpImpact impact = timeOfImpact(
            new IvpImpactContext(ApproachSpeed: closing, TotalBound: totalBound, Start: now, End: end),
            new IvpMindistState(
                mindist.ExtraRadius, mindist.Length, mindist.MarginClass, mindist.Normal));

        if (impact.Event is not int kind)
        {
            return IvpScheduleOutcome.NoEvent;
        }

        double time = impact.Time;
        bool clearOfNow = (float)(time - now) >= NearNow;

        if (!clearOfNow)
        {
            if (recheck == IvpRecheck.AtNow)
            {
                time = now;
            }
            else
            {
                time = Recheck(length, totalBound, (float)environment.Step, now, recheck != IvpRecheck.AfterMiss && (kind & FeatureChangeBits) == 0);

                if ((float)(time - end) >= 0f)
                {
                    return IvpScheduleOutcome.Dropped;
                }
            }
        }

        mindist.QueueSlot = environment.Queue.Add(mindist, (float)(time - environment.QueueBase));
        mindist.Flags = (mindist.Flags & ~KindMask) | (kind & KindMask);
        return IvpScheduleOutcome.Queued;
    }

    /// <summary>The closing speed along the normal, bounded for both cores' turning.</summary>
    /// <remarks>
    /// `√(1.001f − (n·axisB)²)·B+0x254 + √(1.001f − (n·axisA)²)·A+0x254 + ((double)(n·vB) − (double)(n·vA))`, every dot in
    /// float with its `y` and `x` terms added before `z`.
    /// </remarks>
    private static double ClosingSpeed((float X, float Y, float Z) normal, IvpSchedulerCore coreA, IvpSchedulerCore coreB)
    {
        float alongB = (normal.Y * coreB.Velocity.Y) + (normal.X * coreB.Velocity.X) + (normal.Z * coreB.Velocity.Z);
        float alongA = (normal.Y * coreA.Velocity.Y) + (normal.X * coreA.Velocity.X) + (normal.Z * coreA.Velocity.Z);
        double linear = (double)alongB - (double)alongA;

        float axisA = (normal.Y * coreA.RotationAxis.Y) + (normal.X * coreA.RotationAxis.X) + (normal.Z * coreA.RotationAxis.Z);
        float axisB = (normal.Y * coreB.RotationAxis.Y) + (normal.X * coreB.RotationAxis.X) + (normal.Z * coreB.RotationAxis.Z);

        double sideA = Math.Sqrt(AxisSlack - ((double)axisA * axisA));
        double sideB = Math.Sqrt(AxisSlack - ((double)axisB * axisB));

        return (sideB * coreB.Bounds.SurfaceSpeedBound) + (sideA * coreA.Bounds.SurfaceSpeedBound) + linear;
    }

    /// <summary>Counts a look and, every fourth, lowers a margin class over zero by one.</summary>
    /// <remarks>
    /// **The counter is the environment's, not the pair's**: every classed pair's look advances it, and whichever pair's look
    /// finds its old value over two has its class decremented and zeroes it.
    /// </remarks>
    private static void DecayMarginClass(IvpMindist mindist, IvpSchedulerEnvironment environment)
    {
        int flags = mindist.Flags;

        if ((flags & MarginClassMask) == 0)
        {
            return;
        }

        int looks = environment.MarginDecayCounter;
        environment.MarginDecayCounter = looks + 1;

        if (looks > 2)
        {
            int lowered = (int)((((uint)flags >> MarginClassShift) - 1u) << MarginClassShift);
            mindist.Flags = ((lowered ^ flags) & MarginClassMask) ^ flags;
            environment.MarginDecayCounter = 0;
        }
    }

    /// <summary>The time a near-now event is looked at again instead.</summary>
    /// <param name="length">The mindist's length.</param>
    /// <param name="totalBound">The pair's total bound.</param>
    /// <param name="step">The PSI step, narrowed.</param>
    /// <param name="now">The environment's time.</param>
    /// <param name="close">Mode 1 on a kind whose low bits are clear.</param>
    /// <returns>
    /// Over the gap floor, `(gap·0.1f)/bound + now + 1e-7f·step` when close and `gap/bound + now + 1e-4f·step` otherwise;
    /// under it or NaN, `1e-5f·step + now` and `0.001f·step + now`.
    /// </returns>
    private static double Recheck(double length, double totalBound, double step, double now, bool close)
    {
        double gap = length - (double)IvpCollisionTolerance.Epsilon;
        bool overFloor = gap >= GapFloor;

        if (close)
        {
            return overFloor
                ? (gap * CloseRecheckShare / totalBound) + now + (StepScale * CloseRecheckStepShare * step)
                : (StepScale * CloseFloorStepShare * step) + now;
        }

        return overFloor
            ? (gap / totalBound) + now + (StepScale * RecheckStepShare * step)
            : (StepScale * FloorStepShare * step) + now;
    }
}
