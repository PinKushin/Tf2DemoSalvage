using System;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>What a mindist's fire routine did.</summary>
public enum IvpFireOutcome
{
    /// <summary>The minimize left flags <c>0xc000</c> set, and nothing more was done.</summary>
    Frozen,

    /// <summary>The pair went back to the scheduler.</summary>
    Rescheduled,

    /// <summary>The mindist's <c>+0x40</c> ran: the pair collided.</summary>
    Collided,
}

/// <summary>
/// What a queued mindist does when its event fires — <c>FUN_1800992e0</c>, slot 1 of both mindist vtables (B369).
/// </summary>
/// <remarks>
/// **Read from the disassembly** (`docs/findings/51`, *When a queued mindist fires*, and *The event queue, and where the
/// far branch hands a pair off*). **The profiler marks around it are not carried**, and the collision itself — the
/// mindist's `+0x40`, `FUN_18008ecb0` for a plain mindist — is handed in, its friction-system machinery being unported.
/// </remarks>
public static class IvpMindistFire
{
    private const int FrozenBits = 0xc000;

    private const int FeatureChangeBits = 0xF;

    /// <summary>Handles a mindist's event when it fires, as <c>FUN_1800992e0</c> does.</summary>
    /// <param name="mindist">The pair.</param>
    /// <param name="minimize">The minimize, <c>FUN_180095cb0</c>.</param>
    /// <param name="reschedule">The scheduler, <c>FUN_180099380(mindist, 0, mode)</c>.</param>
    /// <param name="collide">
    /// The plain mindist's <c>+0x40</c>, <c>FUN_18008ecb0</c>, unported and handed in — what the mindist's own slot 8
    /// (<see cref="IvpMindist.Collide"/>) runs, or does not.
    /// </param>
    /// <returns>What was done.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **The flags are read after the minimize.** A kind — the flags' low byte, written when the event was queued — with low
    /// bits set is a feature change and reschedules in mode 1. Otherwise `(float)(0.1·d + margin)` over the length
    /// (`COMISS` then `JBE`, so a tie and a NaN reschedule in mode 2) collides.
    /// </remarks>
    public static IvpFireOutcome Handle(
        IvpMindist mindist, Action<IvpMindist> minimize, Action<IvpRecheck> reschedule, Action<IvpMindist> collide)
    {
        ArgumentNullException.ThrowIfNull(mindist);
        ArgumentNullException.ThrowIfNull(minimize);
        ArgumentNullException.ThrowIfNull(reschedule);
        ArgumentNullException.ThrowIfNull(collide);

        minimize(mindist);

        int flags = mindist.Flags;

        if ((flags & FrozenBits) != 0)
        {
            return IvpFireOutcome.Frozen;
        }

        if ((flags & FeatureChangeBits) != 0)
        {
            reschedule(IvpRecheck.AfterFeatureChange);
            return IvpFireOutcome.Rescheduled;
        }

        float threshold = IvpCollisionTolerance.EdgeTargetScale + IvpCollisionTolerance.MarginFor(mindist.MarginClass);

        if (threshold > mindist.Length)
        {
            mindist.Collide(collide);
            return IvpFireOutcome.Collided;
        }

        reschedule(IvpRecheck.AfterMiss);
        return IvpFireOutcome.Rescheduled;
    }
}
