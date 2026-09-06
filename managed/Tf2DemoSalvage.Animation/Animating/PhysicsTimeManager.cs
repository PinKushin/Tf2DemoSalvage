using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>One thing the simulation has scheduled — IVP's <c>IVP_Time_Event</c>.</summary>
/// <param name="Time">
/// When it fires, as a FLOAT measured from the queue's base rather than an absolute time.
/// </param>
/// <param name="Fire">What it does when it fires.</param>
/// <remarks>
/// **The float is the point, not an economy.** IVP keeps absolute time as a double and every
/// scheduled event's time as a float offset from a base it periodically moves forward — see
/// <see cref="PhysicsTimeManager.Rebase"/>. Storing absolute float times instead drifts from the
/// engine the longer a session runs, and shows up as jitter rather than as a bug.
/// </remarks>
public sealed record PhysicsEvent(float Time, Action<double> Fire);

/// <summary>
/// IVP's time manager: a queue of events drained in time order (B58, D142).
/// </summary>
/// <remarks>
/// **Named for the engine's own class, `IVP_Time_Manager`**, rather than for what it holds. The
/// first name here was `PhysicsEventQueue`, which the analysers reject (CA1711) and which was worse
/// anyway: it describes the data structure where the engine describes the job. The queue is a
/// detail; being the thing that decides when everything happens is not.
/// </remarks>
/// <remarks>
/// **Transcribed from `FUN_18008a110` in `vphysics.dll`**, which is the loop the environment's
/// `simulate_dtime` and its fixed-step sibling both reach through
/// `FUN_180089f30(timeManager, env, targetTime)`. The whole of IVP's simulation is this: a priority
/// queue drained by time, each event firing through its own vtable slot 1. **The physics step is not
/// a special case in the loop** — it is an event like any other, which is what makes IVP
/// event-driven internally even though the environment above it is handed a fixed step.
///
/// <code>
///   lVar2 = *(longlong *)(param_2 + 0x10);            // the queue
///   fVar3 = *(float *)(lVar2 + 0x10);                 // earliest queued time
///   if (fVar3 &lt; (float)(param_4 - *(double *)(param_2 + 0x28))) {
///     do {
///       plVar1 = ...;                                 // pop the earliest
///       FUN_1800ab1b0(lVar2, *(uint *)(plVar1 + 1));  // unlink it
///       *(undefined4 *)(plVar1 + 1) = 0xffff;         // mark "not queued"
///       *(double *)(param_2 + 0x20) = (double)fVar3;  // the manager's own clock
///       FUN_180082460(param_3, (double)fVar3 + *(double *)(param_2 + 0x28));
///       (**(code **)(*plVar1 + 8))(plVar1, param_3);  // FIRE
///       if (*(int *)(*(longlong *)(param_2 + 8) + 8) == 1) break;
///       ...
///     } while (fVar3 &lt; (float)(param_4 - *(double *)(param_2 + 0x28)));
///   }
///   FUN_180082460(param_3, param_4);                  // snap to the target
/// </code>
///
/// **Four behaviours here that a plausible reimplementation gets wrong**, and each is why this is
/// transcribed rather than written:
///
/// - **The clock is set BEFORE the event fires**, to that event's own time. Events therefore observe
///   a clock walking forward inside one call rather than jumping at the end of it.
/// - **The stop flag is tested AFTER each fire**, so an event can halt the remainder of a step. A
///   loop that only re-tested the queue would run events the engine skipped.
/// - **The clock is snapped to the target afterwards regardless**, so time ends exactly where the
///   caller asked even when the last event fired earlier — and even when nothing fired at all.
/// - **The comparison is strictly less-than**, against the target expressed in base-relative float.
///   An event exactly at the target belongs to the NEXT call.
/// </remarks>
public sealed class PhysicsTimeManager
{
    private readonly List<PhysicsEvent> _events = [];

    /// <summary>Absolute time the base is measured from — <c>timeManager+0x28</c>.</summary>
    public double Base { get; private set; }

    /// <summary>The manager's own clock — <c>timeManager+0x20</c>, set per event fired.</summary>
    public double Now { get; private set; }

    /// <summary>How many events are waiting.</summary>
    public int Count => _events.Count;

    /// <summary>Whether a fired event asked the rest of this step to be abandoned.</summary>
    /// <remarks>
    /// **The engine's is a field on another object** — `*(int *)(*(tm + 8) + 8) == 1` — read after
    /// every fire. Modelled as a flag an event may set, because that is what it is used for; the
    /// object it lives on is the same `static_object` whose vtable carries the loop.
    /// </remarks>
    public bool Stopped { get; private set; }

    /// <summary>Schedules an event.</summary>
    /// <param name="scheduled">The event, its time relative to <see cref="Base"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="scheduled"/> is null.</exception>
    public void Add(PhysicsEvent scheduled)
    {
        ArgumentNullException.ThrowIfNull(scheduled);

        _events.Add(scheduled);
    }

    /// <summary>Asks the rest of the current drain to be abandoned.</summary>
    public void Stop() => Stopped = true;

    /// <summary>Drains every event due before an absolute time, in time order.</summary>
    /// <param name="target">The absolute time to simulate to.</param>
    /// <param name="setClock">
    /// Sets the environment's clock. Called before each event fires with that event's ABSOLUTE time,
    /// and once more at the end with <paramref name="target"/> itself.
    /// </param>
    /// <returns>How many events fired.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="setClock"/> is null.</exception>
    public int DrainUntil(double target, Action<double> setClock)
    {
        ArgumentNullException.ThrowIfNull(setClock);

        Stopped = false;

        int fired = 0;

        while (Earliest() is { } next && next.Time < (float)(target - Base))
        {
            _events.Remove(next);

            // **The manager's clock, then the environment's, then the fire** — in that order, so an
            // event sees the time it was scheduled for rather than the time the step began.
            Now = next.Time;

            double absolute = next.Time + Base;

            setClock(absolute);
            next.Fire(absolute);

            fired++;

            // **After the fire, not before the next pop** — where the engine puts it, between the
            // dispatch and its re-read of the queue head.
            //
            // **It is currently an EQUIVALENT position, and that is worth saying out loud.** Moving
            // this test to the top of the body changes no observable behaviour, because the only
            // thing between the two placements is `Earliest()`, which is a pure read: sabotage
            // confirmed that the move reddens nothing, and no assertion could catch it, since no
            // input distinguishes them. It is kept where the engine has it rather than where it
            // happens to also work.
            //
            // **A refactor can make it load-bearing again.** The moment anything with side effects
            // runs between the fire and the next guard — a queue compaction, a deferred insert, a
            // second dispatch — the two positions diverge and this needs a test that can tell them
            // apart. There is no such test today.
            if (Stopped)
            {
                break;
            }
        }

        // **Unconditional.** Time ends where the caller asked even if nothing fired, which is what
        // keeps a caller's clock and the environment's from drifting apart across empty steps.
        setClock(target);

        return fired;
    }

    /// <summary>Moves the base forward, rewriting every queued time to stay relative to it.</summary>
    /// <param name="now">The new base, as an absolute time.</param>
    /// <remarks>
    /// **`FUN_18008a020`, and it is a real behaviour rather than housekeeping.** The engine walks the
    /// whole queue subtracting the current time from each entry:
    ///
    /// <code>
    ///   while ((int)uVar7 != 0xffff) { ... *(float *)(lVar1 + 8) -= (float)dVar2; }
    ///   *(undefined8 *)(tm + 0x28) = *(undefined8 *)(env + 0x198);
    /// </code>
    ///
    /// An event's time is therefore only ever precise RELATIVE to the last rebase. A transcription
    /// that kept absolute float times would agree with the engine at the start of a demo and drift
    /// from it later, which reads as jitter rather than as a clock fault.
    /// </remarks>
    public void Rebase(double now)
    {
        float shift = (float)(now - Base);

        for (int index = 0; index < _events.Count; index++)
        {
            _events[index] = _events[index] with { Time = _events[index].Time - shift };
        }

        Base = now;
    }

    /// <summary>The next event due, or null when nothing is queued.</summary>
    private PhysicsEvent? Earliest()
    {
        PhysicsEvent? earliest = null;

        foreach (PhysicsEvent scheduled in _events)
        {
            if (earliest is null || scheduled.Time < earliest.Time)
            {
                earliest = scheduled;
            }
        }

        return earliest;
    }
}
