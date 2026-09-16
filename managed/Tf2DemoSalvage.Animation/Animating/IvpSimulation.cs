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

    /// <summary>Runs every PSI due before an absolute time.</summary>
    /// <param name="target">The absolute time to simulate to.</param>
    /// <returns>How many PSIs fired.</returns>
    public int Advance(double target) => _time.DrainUntil(target, now => Environment.Now = now);

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
    /// <remarks>*The scheduler itself is ported (<see cref="IvpPairScheduler.Examine"/>) and is not wired in yet*: it files a
    /// pair far and queues its event, which needs the hull managers this simulation does not yet fill.</remarks>
    private static void Examine(IvpMindist mindist)
    {
        // The re-examine is where a pair goes back to far; until the hull filing is wired in, a watched pair stays exact.
    }
}
