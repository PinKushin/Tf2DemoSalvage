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

    /// <summary>Wakes a unit — <c>FUN_1800758e0(unit, env)</c>.</summary>
    /// <param name="unit">The unit.</param>
    /// <param name="revive">
    /// A sleeping core's revive, <c>FUN_1800892b0</c> — see <see cref="Revive"/> — answering whether its contacts were rebuilt, which
    /// starts the walk again.
    /// </param>
    /// <returns><c>true</c> when the unit had been asleep.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// <code>
    /// every core of the unit, last first:  core+0x1 ≥ 8 and FUN_1800892b0(core) == 1 → start the walk again
    /// unit state == 8:  unlinked from the sleeping list (head env+0x10's +0x338, links +0x8/+0x10)
    ///                   state = 1;  pushed on the active list (env+0x10's +0x18);  its +0x8 = 0
    /// </code>
    /// </remarks>
    internal bool Wake(IvpSimulationUnit unit, Func<IvpRigidBody, bool> revive)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(revive);

        int index = unit.Cores.Count - 1;

        while (index >= 0)
        {
            IvpRigidBody core = unit.Cores[index];

            index = core.UnitState >= 8 && revive(core) ? unit.Cores.Count - 1 : index - 1;
        }

        if (unit.State != 8)
        {
            return false;
        }

        Sleeping.Remove(unit);
        unit.Woken();
        Active.Add(unit);

        return true;
    }

    /// <summary>A sleeping core brought back into the simulation — <c>FUN_1800892b0(core)</c>.</summary>
    /// <param name="core">The core.</param>
    /// <param name="environment">The clock, the PSI's end and the broad phase's refile.</param>
    /// <returns>Whether its contacts were rebuilt — always <c>false</c> here, see the remarks.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **Read from the decompiler and settled in the disassembly** (2026-09-16):
    /// <code>
    /// every object, last first:  +0x78 = 1
    /// a core stepped before (+0x1d0 != 0):  nothing kept;  else its velocity +0x140 and spin +0x130 are kept
    /// FUN_1800783c0:  +0x1 = 1;  +0x1d0 = now;  +0x200 = +0x208 = now
    ///                 every object, last first:  +0x78 = 1;  FUN_180073b00 — the core's +0x1 and the object's +0x78 held at 0x21
    ///                 while FUN_180098880 refiles it, then restored
    /// r = (float)(env+0x190 − now);  (double)r ≤ (double)1e-10f (COMISD, JBE — a NaN too) → 1e10f, else (float)(1.0 / r)
    /// FUN_180077670(core, {r, that}):  +0x1d8 = that;  +0x80 = 0;  +0x1a0 = +0x180;  +0x140 = 0;  +0x170 = 0;  +0x1dc = 0;
    ///                                  +0x254 = 0;  +0x1c0 = (1, 0, 0)
    /// the kept velocity and spin written back;  FUN_180086500(core) answered;  every object's listeners told
    /// </code>
    /// `FUN_1800783c0` also writes `+0x1d0` once from `now − (float)step` before overwriting it with now — a dead store.
    ///
    /// `FUN_180086500`, which builds a friction contact to every nearby movable core without a system of its own and answers whether
    /// it built one, needs the simulation's minimize and sides, so <c>IvpSimulation.Wake</c> runs it straight after this and hands its
    /// answer to the walk. *Not carried*: the listeners (`FUN_1800820c0`).
    /// </remarks>
    internal static bool Revive(IvpRigidBody core, IvpImpactEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(core);
        ArgumentNullException.ThrowIfNull(environment);

        for (int index = core.Objects.Count - 1; index >= 0; index--)
        {
            core.Objects[index].MovementState = 1;
        }

        bool neverStepped = core.LastStepped == 0d;
        (float X, float Y, float Z) velocity = core.Velocity;
        (float X, float Y, float Z) spin = core.AngularVelocity;

        double now = environment.Now;
        core.UnitState = 1;
        core.LastStepped = now;
        core.RestAnchorTime = now;
        core.SettleAnchorTime = now;

        for (int index = core.Objects.Count - 1; index >= 0; index--)
        {
            IvpCollisionObject collisionObject = core.Objects[index];
            collisionObject.MovementState = 1;

            int coreState = core.UnitState;
            core.UnitState = 0x21;
            collisionObject.MovementState = 0x21;
            environment.Refile?.Invoke(collisionObject);
            core.UnitState = coreState;
            collisionObject.MovementState = 1;
        }

        float remaining = (float)(environment.PsiEnd - now);
        core.InverseStep = !((double)remaining > (double)1e-10f) ? 1e10f : (float)(1d / remaining);
        core.AngularSpeedBound = 0f;
        core.WorkingOrientation = core.Orientation;
        core.Velocity = (0f, 0f, 0f);
        core.PreviousVelocity = (0f, 0f, 0f);
        core.LinearSpeed = 0f;
        core.SurfaceSpeedBound = 0f;
        core.RotationAxis = (1f, 0f, 0f);

        if (neverStepped)
        {
            core.Velocity = velocity;
            core.AngularVelocity = spin;
        }

        return false;
    }

    /// <summary>Puts a core on the environment's revive list — <c>FUN_180087e00(env, core)</c>.</summary>
    /// <param name="core">The core.</param>
    /// <param name="environment">The environment whose list it joins.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// <code>
    /// core+0x0 &amp; 4 clear:  the core appended to env+0x168 (count +0x162, grown through FUN_180072ba0);  core+0x0 |= 4
    /// </code>
    /// What <c>IPhysicsObject::Wake</c> does for an object in state <c>8</c> (<c>FUN_180073a30</c>); an awake object's wake only
    /// sets its core's two anchor times to now (<c>FUN_180078820</c>).
    /// </remarks>
    internal static void QueueRevive(IvpRigidBody core, IvpImpactEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(core);
        ArgumentNullException.ThrowIfNull(environment);

        if (core.ReviveQueued)
        {
            return;
        }

        environment.ReviveQueue.Add(core);
        core.ReviveQueued = true;
    }

    /// <summary>Revives every queued core — <c>FUN_180089210(env)</c>, the PSI's first act.</summary>
    /// <param name="environment">The environment whose list is drained.</param>
    /// <param name="wake">The unit wake, <c>FUN_1800758e0(unit, env)</c>.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// <code>
    /// every entry, last first:  FUN_180077c80(core);  core+0x0 &amp;= ~4
    /// FUN_180077c80(core):  core+0x0 &amp; 2 → nothing
    ///                       core+0x1 == 8 → FUN_1800758e0(core+0x1f8, core+0x10)        -- the unit woken
    ///                       else FUN_180075610(core+0x1f8): every core of the unit, last first, FUN_180078820 — +0x200 = +0x208 = now
    /// the list emptied (its heap block freed unless it is the inline one at +0x170)
    /// </code>
    /// </remarks>
    internal static void ReviveQueued(IvpImpactEnvironment environment, Action<IvpSimulationUnit> wake)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(wake);

        List<IvpRigidBody> queued = environment.ReviveQueue;

        for (int index = queued.Count - 1; index >= 0; index--)
        {
            IvpRigidBody core = queued[index];

            if (!core.Immovable && core.Unit is { } unit)
            {
                if (core.UnitState == 8)
                {
                    wake(unit);
                }
                else
                {
                    for (int member = unit.Cores.Count - 1; member >= 0; member--)
                    {
                        unit.Cores[member].RestAnchorTime = environment.Now;
                        unit.Cores[member].SettleAnchorTime = environment.Now;
                    }
                }
            }

            core.ReviveQueued = false;
        }

        queued.Clear();
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
    /// <param name="recheckInvalidMinimize">
    /// The minimize with no step budget, <c>FUN_180095ad0</c> — what phase 2's <c>FUN_180074240</c> calls on each object's
    /// invalid pairs. **Not <paramref name="minimize"/>**: the two routines are identical but for one stack constant (a step
    /// budget of 20 versus none), so passing the budgeted one here would let an invalid pair converge on a budget the engine
    /// never gives it (B369).
    /// </param>
    /// <param name="examine">The scheduler in mode 1, <c>FUN_180099380(mindist, 1, 1)</c>, phase 6's own call.</param>
    /// <param name="wake">The unit wake, <c>FUN_1800758e0</c>, which the revive list's drain calls for a sleeping core.</param>
    /// <param name="random">The jitter the rest check's cadence takes.</param>
    /// <param name="now">The environment's time, <c>env+0x188</c>.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// <code>
    /// profiler 1;  +0x1ac = 0;  +0x162 → FUN_180089210 (<see cref="IvpUnitManager.ReviveQueued"/>)
    ///              the env controllers at +0x158 last first, slot 0;  FUN_180098610(env+0x20)
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
    /// *Not carried in phase 1*: `FUN_180087e50` on <c>+0x58</c> (null in vphysics), the environment's own slot 10, and the
    /// <c>env+0x158</c> controller list.
    /// </remarks>
    public static void Psi(
        IvpImpactEnvironment environment,
        IvpUnitManager units,
        IvpMindistManager mindists,
        IvpMinList<IIvpTimeEvent> queue,
        Action<IvpMindist> minimize,
        Action<IvpMindist> recheckInvalidMinimize,
        Action<IvpMindist> examine,
        Action<IvpSimulationUnit> wake,
        Func<float> random,
        double now)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(wake);
        ArgumentNullException.ThrowIfNull(mindists);
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(minimize);
        ArgumentNullException.ThrowIfNull(recheckInvalidMinimize);
        ArgumentNullException.ThrowIfNull(examine);
        ArgumentNullException.ThrowIfNull(random);

        float step = (float)environment.Step;
        List<IvpRigidBody> pushed = [];
        List<IvpHullManager> due = [];

        environment.Phase = 0;

        if (environment.ReviveQueue.Count != 0)
        {
            IvpUnitManager.ReviveQueued(environment, wake);
        }

        mindists.RecheckEveryPsi(minimize, queue);

        int at = 0;

        while (at < units.Active.Count)
        {
            IvpSimulationUnit unit = units.Active[at];

            if (unit.Psi(environment, now, step, pushed, random, RecheckInvalid))
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

        // `FUN_180074240(object)`, which the unit PSI runs for every object of every core it simulated.
        void RecheckInvalid(IvpCollisionObject collisionObject) => collisionObject.RecheckInvalid(mindists, recheckInvalidMinimize);
    }
}
