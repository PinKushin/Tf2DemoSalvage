using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// IVP's own simulation, assembled from the parts read out of <c>vphysics.dll</c> — the environment, its time manager, its
/// units and the PSI event that drives them (B369, D172).
/// </summary>
/// <remarks>
/// **This is the replacement <see cref="IvpEnvironment"/> is measured against**, not a second solver: every stage it runs was
/// read from the binary and ported on its own — <see cref="IvpPsiEvent"/> (<c>FUN_18008a020</c>),
/// <see cref="IvpPhysicsPipeline"/> (<c>FUN_180082560</c>), <see cref="IvpSimulationUnit"/> (<c>FUN_180075c80</c>),
/// <see cref="IvpIntegrator.StepCore"/> (<c>FUN_180099a00</c>) and the five controllers by priority.
///
/// **What it does NOT do yet**: collide. The narrow phase that would create contacts, and the constraint controller at priority
/// 405, are not wired in here — so a body added to this simulation falls, damps and spins as the engine's own step says, and
/// nothing stops it. That is deliberate: the drop is the measurement that has to match before anything else is switched over.
/// </remarks>
public sealed class IvpSimulation
{
    private readonly IvpUnitManager _units = new();
    private readonly IvpMindistManager _mindists = new();
    private readonly IvpMinList<IvpMindist> _queue = new();
    private readonly PhysicsTimeManager _time = new();
    private readonly IvpGravityController _gravity;
    private readonly Func<float> _random;
    private int _step;
    private int _marginDecay;

    /// <summary>Starts a simulation with one gravity controller, as an environment's <c>+0x0</c> holds one.</summary>
    /// <param name="environment">The environment: its step, limits, anomaly manager and materials.</param>
    /// <param name="gravity">The acceleration the gravity controller carries, in this project's own units.</param>
    /// <param name="random">
    /// The jitter the rest check's cadence takes — <c>FUN_18007d5c0</c>. A caller wanting a repeatable run passes a constant.
    /// </param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public IvpSimulation(IvpImpactEnvironment environment, (float X, float Y, float Z) gravity, Func<float> random)
    {
        Environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _random = random ?? throw new ArgumentNullException(nameof(random));
        _gravity = new IvpGravityController(gravity);
    }

    /// <summary>The environment every stage reads.</summary>
    public IvpImpactEnvironment Environment { get; }

    /// <summary>The unit lists — the time manager's active and sleeping chains.</summary>
    internal IvpUnitManager Units => _units;

    /// <summary>The time manager's own clock, in absolute seconds.</summary>
    public double Now => Environment.Now;

    /// <summary>How many units are awake.</summary>
    public int AwakeUnits => _units.Active.Count;

    /// <summary>Adds a body, in its own unit, driven by gravity — the shape a core's constructor gives it.</summary>
    /// <param name="core">The core.</param>
    /// <returns>The unit it was put in.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="core"/> is null.</exception>
    /// <remarks>
    /// **A core gets its unit from its own constructor** (<c>FUN_1800782d0</c>) and its gravity controller from the same call,
    /// which is why this is one method rather than two. *Two bodies joined by a constraint share a unit in the engine
    /// (<c>FUN_180074e40</c>, the merge); that merge is not carried, so each body here is its own unit.*
    /// </remarks>
    public IvpSimulationUnit Add(IvpRigidBody core)
    {
        ArgumentNullException.ThrowIfNull(core);

        IvpSimulationUnit unit = new();
        unit.Cores.Add(core);
        core.Unit = unit;
        core.Controllers.Add(_gravity);
        unit.RebuildEntries();
        _units.Active.Add(unit);

        return unit;
    }

    /// <summary>
    /// Files a constraint group on the bodies it joins, merging their units — the controller's own registration
    /// (<c>FUN_1800748b0</c>) plus the merge (<c>FUN_180074e40</c>).
    /// </summary>
    /// <param name="group">The group; every body its joints name must already have been added.</param>
    /// <returns>The unit that now holds every one of those bodies.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="group"/> is null.</exception>
    /// <exception cref="InvalidOperationException">A joint names a body this simulation does not hold.</exception>
    /// <remarks>
    /// **Joined bodies share one unit**, so the group is solved once per PSI however many joints it holds — which is what the
    /// engine's merge is for, and what a per-body controller would get wrong by solving it twice.
    /// </remarks>
    public IvpSimulationUnit Add(IvpConstraintGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);

        IvpConstraintController controller = new(group);
        IvpSimulationUnit? into = null;

        foreach (IvpRagdollJoint joint in group.Joints)
        {
            into = Join(joint.BodyA, controller, into);
            into = Join(joint.BodyB, controller, into);
        }

        return into ?? throw new InvalidOperationException("A constraint group with no joints has no unit to file on.");
    }

    private IvpSimulationUnit Join(IvpRigidBody core, IvpConstraintController controller, IvpSimulationUnit? into)
    {
        IvpSimulationUnit unit = core.Unit
            ?? throw new InvalidOperationException("A joint names a body this simulation does not hold.");

        // `FUN_1800748b0` appends without checking, and the rebuild finds the controller's entry rather than making a second, so
        // a body named by two joints of one group carries the controller twice and the entry holds it twice. That is the
        // engine's own shape; a de-duplicating guard here was this port's invention and is gone.
        core.Controllers.Add(controller);

        if (into is null)
        {
            unit.RebuildEntries();
            return unit;
        }

        if (!ReferenceEquals(unit, into))
        {
            into.Absorb(unit);
            _units.Active.Remove(unit);
            _units.Sleeping.Remove(unit);
        }
        else
        {
            into.RebuildEntries();
        }

        return into;
    }

    /// <summary>Queues the first PSI event, due at once — the time manager's own constructor does this for the engine.</summary>
    public void Start() =>
        IvpPsiEvent.Start(Environment, _time, _units, _mindists, _queue, Minimize, Examine, _random);

    /// <summary>Runs every PSI due before an absolute time, and every pair event those PSIs queued.</summary>
    /// <param name="target">The absolute time to simulate to.</param>
    /// <returns>How many PSIs fired.</returns>
    /// <remarks>
    /// **The engine keeps ONE queue** — the PSI event and every pair's event sit in the time manager's own min-list together, in
    /// time order. This port has two: <see cref="PhysicsTimeManager"/> for the PSI event and the mindist min-list the scheduler
    /// writes. So the pair events of a PSI are fired after it rather than interleaved with the next one by time. *A stated
    /// divergence, and the one to close when the two queues become one.*
    /// </remarks>
    public int Advance(double target)
    {
        int fired = 0;

        // **One PSI at a time, with the pair events drained after each** — the engine fires both from one queue in time order, so
        // a pair event due before the next PSI must fire before it. Draining once at the end of a long slice would instead find
        // only the last PSI's requeue, always in the future.
        while (Environment.Now < target)
        {
            double next = Environment.PsiEnd > Environment.Now && Environment.PsiEnd < target ? Environment.PsiEnd : target;

            // **The loop must always move the clock forward.** A slice that does not is how this spun forever once: the clock is
            // snapped to `next` by the drain, so a `next` at or behind now leaves the condition unchanged and nothing progresses.
            if (!(next > Environment.Now))
            {
                break;
            }

            fired += _time.DrainUntil(next, now => Environment.Now = now);
            FireDuePairs();
        }

        return fired;
    }

    /// <summary>How many pair events have fired — an instrument, not a field the engine keeps.</summary>
    public int PairEvents { get; private set; }

    /// <summary>Fires every queued pair event whose time has passed — the loop the time manager runs over its own queue.</summary>
    private void FireDuePairs()
    {
        while (_queue.TryFirst(out IvpMindist? mindist, out int slot))
        {
            if (_queue.ValueOf(slot) > Environment.Now)
            {
                return;
            }

            _queue.Remove(slot);
            mindist.QueueSlot = null;
            PairEvents++;

            IvpMindistFire.Handle(mindist, Minimize, recheck => Examine(mindist, recheck), Collide);
        }
    }

    /// <summary>A collided pair's real response — <c>FUN_18008ecb0</c>, through <see cref="IvpMindistCollide.Collide"/>.</summary>
    /// <remarks>**A sleeping unit is woken first**, as `FUN_180074360` does for an object whose own state is <c>8</c>.</remarks>
    private void Collide(IvpMindist mindist)
    {
        (IvpLedgeSide first, IvpLedgeSide second) = SidesOf(mindist);

        IvpCollisionObject firstObject = ObjectOf(mindist.HullRecord(0));
        IvpCollisionObject secondObject = ObjectOf(mindist.HullRecord(1));

        Wake(firstObject);
        Wake(secondObject);

        _ = IvpMindistCollide.Collide(
            mindist,
            firstObject,
            first,
            secondObject,
            second,
            Environment,
            Environment.Materials,
            _ => (first, second),
            Minimize,
            mindist => Examine(mindist, IvpRecheck.AfterMiss),
            Environment.Now);
    }

    /// <summary>Wakes the unit of an object about to collide, and counts it — <c>FUN_180074360</c>'s own gate.</summary>
    /// <remarks>
    /// *No test stages a collision against a unit that is ALREADY asleep*: a sleeping unit is not stepped, so its own speed stops
    /// reaching the scheduler and the pair reads far. <see cref="IvpUnitManager.Wake"/> is tested on its own; this call site is not.
    /// </remarks>
    private void Wake(IvpCollisionObject collisionObject)
    {
        if (collisionObject.Core?.Unit is { } unit && _units.Wake(unit))
        {
            Wakes++;
        }
    }

    /// <summary>How many sleeping units a collision has woken — an instrument, not a field the engine keeps.</summary>
    public int Wakes { get; private set; }

    private static IvpCollisionObject ObjectOf(IvpMindistHullRecord record) =>
        record.CollisionObject ?? throw new InvalidOperationException("A synapse record was never linked to an object.");

    /// <summary>Files a pair of objects as an exact mindist, so the pipeline's walks reach it.</summary>
    /// <param name="mindist">The pair.</param>
    /// <param name="first">Synapse record 0's object; its core must already have been added.</param>
    /// <param name="second">Synapse record 1's object.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **The broad phase that would find these pairs is not carried here yet**, so a caller names them. Everything after this —
    /// the minimize each PSI, the re-examine, and the collision itself — is the engine's own path.
    /// </remarks>
    public void Watch(IvpMindist mindist, IvpCollisionObject first, IvpCollisionObject second)
    {
        ArgumentNullException.ThrowIfNull(mindist);

        _mindists.LinkExact(mindist, first, second);
    }

    /// <summary>The two ledge sides a mindist's synapses stand on, at the cores' current transforms.</summary>
    /// <param name="mindist">The pair.</param>
    /// <returns>Record 0's side and record 1's.</returns>
    /// <exception cref="InvalidOperationException">A synapse's object has no core, or its core no ledge.</exception>
    /// <remarks>
    /// **Built fresh from each core's own transform**, which is what the engine's cache objects hold — see
    /// <see cref="IvpLedgeSide.FromLedge"/>. *A body with more than one ledge takes its first*: the ledge tree walk that would
    /// pick the right one belongs to the broad phase, which is not carried here.
    /// </remarks>
    public static (IvpLedgeSide First, IvpLedgeSide Second) SidesOf(IvpMindist mindist)
    {
        ArgumentNullException.ThrowIfNull(mindist);

        return (SideOf(mindist.HullRecord(0)), SideOf(mindist.HullRecord(1)));
    }

    private static IvpLedgeSide SideOf(IvpMindistHullRecord record)
    {
        IvpCollisionObject collisionObject = record.CollisionObject
            ?? throw new InvalidOperationException("A synapse record was never linked to an object.");

        IvpRigidBody core = collisionObject.Core
            ?? throw new InvalidOperationException("A watched object has no core.");

        if (core.Ledges.Count == 0)
        {
            throw new InvalidOperationException("A watched object's core has no ledge to stand a synapse on.");
        }

        return IvpLedgeSide.FromLedge(core.Ledges[0], core.CoreMatrix, core.Position);
    }

    /// <summary>The minimize the pipeline's two mindist walks take — <c>FUN_180095cb0</c>.</summary>
    private void Minimize(IvpMindist mindist)
    {
        (IvpLedgeSide first, IvpLedgeSide second) = SidesOf(mindist);

        _ = IvpMindistMinimize.Minimize(mindist, first, second, _step);
        _step++;
    }

    /// <summary>The scheduler in mode 1 the pipeline's last phase takes — <c>FUN_180099380(mindist, 1, 1)</c>.</summary>
    /// <remarks>
    /// **The pair's time of impact is searched over its own sides** (<see cref="IvpImpactDispatch.Search"/>), and the outcome is
    /// remembered as <see cref="LastOutcome"/> so a caller can see what the scheduler decided. *`removeFar` is null*: a pair past
    /// its far threshold is left exact rather than filed with the objects' hull managers, because that filing is the broad
    /// phase's and is not carried here.
    /// </remarks>
    private void Examine(IvpMindist mindist) => Examine(mindist, IvpRecheck.AfterFeatureChange);

    private void Examine(IvpMindist mindist, IvpRecheck recheck)
    {
        (IvpLedgeSide first, IvpLedgeSide second) = SidesOf(mindist);

        IvpRigidBody firstCore = CoreOf(mindist.HullRecord(0));
        IvpRigidBody secondCore = CoreOf(mindist.HullRecord(1));

        IvpSchedulerEnvironment scheduler = new()
        {
            Step = Environment.Step,
            Now = Environment.Now,
            NextPsi = Environment.PsiEnd,
            // `DAT_18012d654` for the gravity in force — the environment's own length, as `SetGravity` leaves it.
            ClosingSpeedThreshold = IvpCollisionTolerance.ClosingSpeedThreshold(Environment.GravityLength),
            Queue = _queue,

            // **Zero, so this queue holds ABSOLUTE times.** The engine's pair events live in the time manager's own queue and are
            // rebased with everything else each PSI (`FUN_18008a020`); this port's mindist queue is separate and is not, so a
            // relative time would be measured from a base that had moved. *The cost is the precision the rebase exists to protect:
            // a float time far from zero. It goes away when the two queues become one.*
            QueueBase = 0d,
            MarginDecayCounter = _marginDecay,
        };

        LastOutcome = IvpPairScheduler.Examine(
            mindist,
            Scheduled(firstCore),
            Scheduled(secondCore),
            scheduler,
            removeFar: null,
            recheck,
            (context, state) => IvpImpactDispatch.Search(
                context,
                state,
                mindist.Synapse(mindist.SynapseA),
                Searchable(first, firstCore),
                mindist.Synapse(mindist.SynapseA ^ 1),
                Searchable(second, secondCore)));

        _marginDecay = scheduler.MarginDecayCounter;
    }

    /// <summary>What the scheduler last decided about a watched pair — an instrument, not a field the engine keeps.</summary>
    public IvpScheduleOutcome? LastOutcome { get; private set; }

    /// <summary>One side as the time-of-impact searches read it — the ledge, the body's motion over the interval, its bounds.</summary>
    private static IvpSearchSide Searchable(IvpLedgeSide side, IvpRigidBody core) =>
        new(
            side.Points,
            side.Topology,
            new IvpMotionCache(core, core.CoreMatrix, resting: core.Immovable),
            IvpRangeManager.Bounds(core));

    /// <summary>A core as the scheduler reads it.</summary>
    /// <remarks>
    /// *Nothing here yet reaches the branch that reads <see cref="IvpRigidBody.Velocity"/> itself*: the far test answers on the
    /// bounds, which carry the speed already, and the near branch's own use of the raw vector arrives with the collision. A
    /// sabotage zeroing it therefore survives, deliberately recorded rather than papered over.
    /// </remarks>
    private static IvpSchedulerCore Scheduled(IvpRigidBody core) =>
        new(IvpRangeManager.Bounds(core), core.Velocity, core.RotationAxis);

    private static IvpRigidBody CoreOf(IvpMindistHullRecord record) =>
        (record.CollisionObject ?? throw new InvalidOperationException("A synapse record was never linked to an object."))
            .Core ?? throw new InvalidOperationException("A watched object has no core.");
}
