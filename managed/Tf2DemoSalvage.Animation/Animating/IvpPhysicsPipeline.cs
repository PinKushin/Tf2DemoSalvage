using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// The units the time manager holds — the active list at <c>manager+0x18</c> and the sleeping one at <c>+0x338</c> (B369, D172).
/// </summary>
/// <remarks>
/// **Read from the decompiler** (`docs/findings/51`): a unit is linked by its own <c>+0x8</c>/<c>+0x10</c> pointers, and
/// <c>FUN_1800758e0</c> moves one between the lists when a collision wakes it. The state byte says which list it is on:
/// <c>8</c> asleep, anything below that awake.
/// </remarks>
public sealed class IvpUnitManager
{
    /// <summary>The awake units — <c>manager+0x18</c>, walked head to tail by the PSI.</summary>
    internal List<IvpSimulationUnit> Active { get; } = [];

    /// <summary>The sleeping ones — <c>manager+0x338</c>.</summary>
    internal List<IvpSimulationUnit> Sleeping { get; } = [];

    /// <summary>Moves a sleeping unit back onto the active list — <c>FUN_1800758e0(unit, env)</c>'s own tail.</summary>
    /// <param name="unit">The unit; one already awake is left alone.</param>
    /// <returns><c>true</c> when it had been asleep.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="unit"/> is null.</exception>
    /// <remarks>
    /// <code>
    /// unit state == 8:  unlinked from the sleeping list (head env+0x10's +0x338, links +0x8/+0x10)
    ///                   state = 1;  pushed on the active list (env+0x10's +0x18);  its +0x8 = 0
    /// </code>
    /// *The per-core revive that precedes it is NOT carried*: `FUN_1800892b0` sets each object's state, re-integrates the core
    /// over the PSI's remainder around a saved velocity (`FUN_1800783c0`, `FUN_180077670`, `FUN_180086500` — all unread) and tells
    /// the environment's listeners. So a woken unit here resumes stepping at the next PSI rather than being caught up inside the
    /// wake.
    /// </remarks>
    internal bool Wake(IvpSimulationUnit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);

        if (unit.State != 8)
        {
            return false;
        }

        Sleeping.Remove(unit);
        unit.Woken();
        Active.Add(unit);

        return true;
    }
}

/// <summary>
/// The PSI's pipeline — <c>FUN_180082560(env)</c>, whose profiler markers number its phases (B369, D172).
/// </summary>
/// <remarks>
/// **Read from the decompiler** (`docs/findings/51`, *The pipeline*): phase 2 walks the awake units, phase 3 steps every core
/// they collected, phase 4 tells the hull managers the steps pushed, and phases 5 and 6 walk the mindist list — the minimize,
/// then the scheduler in mode 1.
///
/// *Phase 1, the reverse walk of <c>env+0x158</c> calling each entry's slot 0, is not carried*: what that list holds has not
/// been read, and it is not the unit controllers, which are phase 2's own business.
/// </remarks>
public static class IvpPhysicsPipeline
{
    /// <summary>Runs one PSI over every awake unit — <c>FUN_180082560</c>, phases 2 through 6.</summary>
    /// <param name="environment">The environment.</param>
    /// <param name="units">The unit lists.</param>
    /// <param name="mindists">The mindist manager phases 5 and 6 walk.</param>
    /// <param name="queue">The mindist queue those two phases take.</param>
    /// <param name="minimize">The minimize, <c>FUN_180095cb0</c>.</param>
    /// <param name="examine">The scheduler in mode 1, <c>FUN_180099380(mindist, 1, 1)</c>, phase 6's own call.</param>
    /// <param name="random">The jitter the rest check's cadence takes.</param>
    /// <param name="now">The environment's time, <c>env+0x188</c>.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// <code>
    /// profiler 1;  +0x1ac = 0;  the env controllers at +0x158 last first, slot 0;  FUN_180098610(env+0x20)
    /// profiler 2;  FUN_180075a90(env+0x10, env, buffer)   -- every awake unit's PSI, the cores collected
    /// profiler 3;  FUN_18009a590(env, buffer, second)     -- every collected core stepped, last first
    /// profiler 4;  +0x1ac = 2;  FUN_18009a690(env, second)
    /// profiler 5;  +0x1ac = 3;  FUN_1800983e0(env+0x20)
    /// profiler 6;  +0x1ac = 4;  FUN_1800985a0(env+0x20)
    /// profiler 7;  +0x1ac = 5
    /// </code>
    /// **A unit that falls asleep in phase 2 moves to the sleeping list before phase 3**, so its cores are still stepped this
    /// PSI — they were collected before the rest check ran.
    ///
    /// *Not carried in phase 1*: the two guarded calls (`FUN_180089210` on <c>env+0x162</c>, `FUN_180087e50` on <c>+0x58</c>),
    /// the environment's own slot 10, and the <c>env+0x158</c> controller list, none of which has been read.
    /// </remarks>
    public static void Psi(
        IvpImpactEnvironment environment,
        IvpUnitManager units,
        IvpMindistManager mindists,
        IvpMinList<IvpMindist> queue,
        Action<IvpMindist> minimize,
        Action<IvpMindist> examine,
        Func<float> random,
        double now)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(mindists);
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(minimize);
        ArgumentNullException.ThrowIfNull(examine);
        ArgumentNullException.ThrowIfNull(random);

        float step = (float)environment.Step;
        List<IvpRigidBody> pushed = [];
        List<IvpHullManager> due = [];

        environment.Phase = 0;
        mindists.RecheckEveryPsi(minimize, queue);

        int at = 0;

        while (at < units.Active.Count)
        {
            IvpSimulationUnit unit = units.Active[at];

            if (unit.Psi(environment, now, step, pushed, random))
            {
                units.Active.RemoveAt(at);
                units.Sleeping.Add(unit);
                continue;
            }

            at++;
        }

        for (int index = pushed.Count - 1; index >= 0; index--)
        {
            IvpIntegrator.StepCore(pushed[index], environment, now, step, due);
        }

        environment.Phase = 2;
        IvpHullManager.NotifyAll(due, environment.Limits.MaximumCollisionChecks, _ => 0);

        environment.Phase = 3;
        mindists.MinimizeExact(minimize, queue);

        environment.Phase = 4;
        mindists.ExamineExact(examine);

        environment.Phase = 5;
    }
}
