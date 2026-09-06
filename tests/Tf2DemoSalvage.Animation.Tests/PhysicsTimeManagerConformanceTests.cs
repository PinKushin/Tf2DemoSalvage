using System.Collections.Generic;

using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// IVP's event loop: a queue drained in time order (B58, D142).
/// </summary>
/// <remarks>
/// **Predicted from `FUN_18008a110` in `vphysics.dll`, not from the code under test.** That function
/// is the whole of IVP's simulation control flow — the environment's two `simulate` entry points both
/// reach it — and the four behaviours asserted here are the ones a plausible reimplementation gets
/// wrong. Each would produce a corpse that moves rather than an error.
///
/// **The physics step is not special-cased in this loop**, which is the structural finding: it is an
/// event like any other, so IVP is event-driven internally even though the environment above it is
/// handed a fixed step.
/// </remarks>
public sealed class PhysicsTimeManagerConformanceTests
{
    /// <remarks>
    /// **The engine sets the clock BEFORE each fire, to that event's own time** — `FUN_180082460(env,
    /// fVar3 + base)` sits above the `(**(code **)(*plVar1 + 8))(plVar1, env)` call. So an event
    /// observes a clock that has walked forward to it, not one still at the start of the step and not
    /// one already at the end.
    ///
    /// **The first version of this test could not fail, and the shape of the mistake is worth
    /// keeping.** It recorded the fired times into one list and the clock values into another, then
    /// asserted on both — which reads as thorough and measures the wrong thing entirely. Two
    /// accumulators that never observe each other are blind to the ORDER of the two calls that fill
    /// them: swapping `setClock` and `Fire` left every assertion true. Sabotage caught it; no amount
    /// of strengthening either assertion would have.
    ///
    /// **The variable is "what did the event SEE", so the event has to read it.** `observed` below is
    /// written by the clock callback and read inside the fire, which is the only arrangement in which
    /// the two orders differ: fire-first leaves the first event looking at
    /// <see cref="double.NaN"/> and the second at 1.
    /// </remarks>
    [Test]
    public void DrainUntil_ForEachEvent_SetsTheClockToThatEventsTimeBeforeFiring()
    {
        PhysicsTimeManager manager = new();

        double clock = double.NaN;
        List<double> observed = [];
        List<double> ticks = [];

        void SetClock(double now)
        {
            clock = now;
            ticks.Add(now);
        }

        manager.Add(new PhysicsEvent(1f, _ => observed.Add(clock)));
        manager.Add(new PhysicsEvent(2f, _ => observed.Add(clock)));

        manager.DrainUntil(5d, SetClock);

        observed.ShouldBe(
            [1d, 2d], "each event reads a clock already walked forward to its own time");

        ticks.ShouldBe([1d, 2d, 5d], "and the clock is snapped to the target afterwards");
    }

    /// <remarks>
    /// **Earliest first, whatever order they were queued in.** The engine reads the queue's own
    /// head rather than iterating, so the order is a property of the queue and not of insertion.
    /// </remarks>
    [Test]
    public void DrainUntil_WithEventsQueuedOutOfOrder_FiresThemEarliestFirst()
    {
        PhysicsTimeManager manager = new();
        List<double> seen = [];

        manager.Add(new PhysicsEvent(3f, seen.Add));
        manager.Add(new PhysicsEvent(1f, seen.Add));
        manager.Add(new PhysicsEvent(2f, seen.Add));

        manager.DrainUntil(9d, _ => { });

        seen.ShouldBe([1d, 2d, 3d]);
    }

    /// <remarks>
    /// **The comparison is strictly less-than** — `fVar3 &lt; (float)(param_4 - base)` — so an event
    /// exactly at the target belongs to the NEXT call. Asserted with the boundary itself, because
    /// this is the input for which `&lt;` and `&lt;=` differ and nothing else distinguishes them.
    /// </remarks>
    [Test]
    public void DrainUntil_ForAnEventExactlyAtTheTarget_LeavesItForTheNextCall()
    {
        PhysicsTimeManager manager = new();
        int fired = 0;

        manager.Add(new PhysicsEvent(2f, _ => fired++));

        manager.DrainUntil(2d, _ => { }).ShouldBe(0);
        fired.ShouldBe(0, "an event ON the boundary is not due yet");
        manager.Count.ShouldBe(1, "and it is still queued");

        manager.DrainUntil(2.001d, _ => { }).ShouldBe(1, "a hair past it, and it fires");
    }

    /// <remarks>
    /// **The stop flag is tested AFTER each fire**, so an event can halt the rest of the step and
    /// whatever else was due stays queued. A loop that only re-tested the queue would run events the
    /// engine skipped.
    /// </remarks>
    [Test]
    public void DrainUntil_WhenAnEventStopsTheStep_LeavesTheRestQueued()
    {
        PhysicsTimeManager manager = new();
        int later = 0;

        manager.Add(new PhysicsEvent(1f, _ => manager.Stop()));
        manager.Add(new PhysicsEvent(2f, _ => later++));

        manager.DrainUntil(9d, _ => { }).ShouldBe(1);

        later.ShouldBe(0, "the second event was due and must not have run");
        manager.Count.ShouldBe(1, "it is still waiting for the next call");
    }

    /// <remarks>
    /// **The clock is snapped to the target unconditionally**, after the loop, even when nothing was
    /// due. Without it a caller's clock and the environment's drift apart across empty steps — and
    /// an empty step is the common case once a ragdoll has settled.
    /// </remarks>
    [Test]
    public void DrainUntil_WithNothingDue_StillSetsTheClockToTheTarget()
    {
        PhysicsTimeManager manager = new();
        List<double> clock = [];

        manager.DrainUntil(4d, clock.Add).ShouldBe(0);

        clock.ShouldBe([4d]);
    }

    /// <remarks>
    /// **`FUN_18008a020` walks the queue subtracting the current time**, so an event's time is only
    /// ever precise relative to the last rebase. The event below is at absolute 10 before and after;
    /// what changes is the number stored for it.
    /// </remarks>
    [Test]
    public void Rebase_MovesTheBaseAndRewritesEveryQueuedTime()
    {
        PhysicsTimeManager manager = new();
        List<double> seen = [];

        manager.Add(new PhysicsEvent(10f, seen.Add));

        manager.Rebase(4d);

        manager.Base.ShouldBe(4d);

        // Still absolute 10: the stored time became 6, measured from the new base of 4.
        manager.DrainUntil(11d, _ => { });

        seen.ShouldBe([10d], "the event fires at the same ABSOLUTE time it always would have");
    }

    /// <remarks>
    /// **The control that gives the rebase teeth, and the first version of it was wrong.** Asserting
    /// only that the event still fires proves nothing: it fires under both readings, just at
    /// different times. The input has to be one where correct and broken DIFFER — which is the
    /// "wrong condition" trap this project keeps meeting.
    ///
    /// A stored 10 rewritten to 6 against a base of 4 is absolute **10**. Left at 10 against that
    /// base it would be absolute **14**. So a drain to 10.1 fires it if the rebase rewrote the time
    /// and fires nothing if it did not, and a drain to 9.9 must not fire it either way.
    /// </remarks>
    [Test]
    public void Rebase_RewritesTheStoredTimeRatherThanOnlyMovingTheBase()
    {
        PhysicsTimeManager manager = new();
        List<double> seen = [];

        manager.Add(new PhysicsEvent(10f, seen.Add));
        manager.Rebase(4d);

        manager.DrainUntil(9.9d, _ => { }).ShouldBe(0, "still short of its absolute time");

        manager.DrainUntil(10.1d, _ => { }).ShouldBe(
            1, "un-rewritten it would sit at absolute 14 and this would fire nothing");

        seen.ShouldBe([10d]);
    }
}
