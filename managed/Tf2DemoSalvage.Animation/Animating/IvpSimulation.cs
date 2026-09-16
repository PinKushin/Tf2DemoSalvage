using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;

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
    private readonly IvpMindistManager _mindists;
    private readonly IvpMinList<IvpMindist> _queue = new();
    private readonly PhysicsTimeManager _time = new();
    private readonly IvpGravityController _gravity;
    private readonly Func<float> _random;
    /// <summary>
    /// <c>env+0x1a0</c>. *Its starting value is NOT read*: one is interpolated, because a fresh cache holds zero and a pair made
    /// before the first clock set would otherwise be measured from a cache that was never placed.
    /// </summary>
    private int _timeCode = 1;
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
        Collisions = new IvpCollisionEnvironment
        {
            // vphysics' own filter asks the game's collision rules (`env+0x30`'s slot 0); a pair the rules do not name collides.
            Filter = (first, second) => ShouldCollide?.Invoke(first, second) ?? true,
            Step = environment.Step,
            Now = environment.Now,
            Psi = 1,

            // `FUN_1800977f0(env+0x20, m)`: a new pair of two ordinary objects is linked exact, and appended to the rechecked array
            // when either core asks for it. **Without this the pair creation makes a mindist nothing ever looks at.**
            BecomeExact = BecomeExact,

            // vphysics has no phantom objects in a demo's ragdolls; a pair with one would need `FUN_180097940`, unported.
            BecomePhantom = mindist => throw new NotSupportedException(
                "A phantom object's pair needs FUN_180097940, which is not ported; nothing in this project creates one."),
        };

        // **One manager**, the environment's own `+0x20`: the broad phase files pairs into it and the pipeline walks the same list.
        _mindists = Collisions.MindistManager;
        Collisions.Creators.Add(new IvpPairCreator());

        // What the impact environment reaches through a core's `+0x10`: the objects' caches, the unit merge and the broad phase.
        environment.ContactSides = ContactSides;
        environment.MergeUnits = MergeUnits;
        IvpCollisionEnvironment collisions = Collisions;
        environment.Refile = collisionObject => IvpBroadPhase.Refile(collisions, collisionObject);
    }

    /// <summary>The game's collision rules for a pair of objects — what vphysics' filter asks; null lets every pair collide.</summary>
    public Func<IvpCollisionObject, IvpCollisionObject, bool>? ShouldCollide { get; set; }

    /// <summary>The environment every stage reads.</summary>
    public IvpImpactEnvironment Environment { get; }

    /// <summary>The unit lists — the time manager's active and sleeping chains.</summary>
    internal IvpUnitManager Units => _units;

    /// <summary>The fields the broad phase reads and writes — an <c>IVP_Environment</c>'s own.</summary>
    internal IvpCollisionEnvironment Collisions { get; }

    /// <summary>The objects the broad phase files, one per body that has a ledge.</summary>
    internal List<IvpCollisionObject> Objects { get; } = [];

    /// <summary>
    /// Gives a body a collision object the broad phase can file — a node, a surface over its own ledge, and the environment.
    /// </summary>
    /// <param name="core">The core; it must already have been added and must carry a ledge.</param>
    /// <param name="material">The object's own material, <c>object+0xd0</c>, which a triangle of material index zero reads.</param>
    /// <returns>The object.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="core"/> or <paramref name="material"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The core has no ledge to stand a surface on.</exception>
    /// <remarks>
    /// **The surface is a single-ledge tree** (<see cref="PhysicsLedgeTree.SingleLedge"/>): a body with one ledge is what every TF2
    /// ragdoll element is, and a genuinely compound solid needs the real tree the world already builds.
    /// **<see cref="IvpCollisionObject.MovementState"/> is 1**, the moving state the broad phase's own filters read.
    /// </remarks>
    public IvpCollisionObject Collide(IvpRigidBody core, IIvpMaterial material)
    {
        ArgumentNullException.ThrowIfNull(core);

        if (core.Ledges.Count == 0)
        {
            throw new InvalidOperationException("A body with no ledge has no surface for the broad phase to query.");
        }

        return Collide(core, PhysicsLedgeTree.ForLedge(core.Ledges[0]), material);
    }

    /// <summary>Gives a body a collision object over a whole surface — a map's collide, whose tree holds many ledges.</summary>
    /// <param name="core">The core.</param>
    /// <param name="surface">The surface's ledge tree, as <see cref="PhysicsHull.Tree"/> reads it.</param>
    /// <param name="material">The object's own material, <c>object+0xd0</c>.</param>
    /// <returns>The object.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// *<see cref="IvpCollisionObject.MovementState"/> is 1 for a static object too*: what vphysics writes for one is not read, and
    /// a clear low three bits would give it the whole of a far pair's allowance where the moving side should take it.
    /// </remarks>
    public IvpCollisionObject Collide(IvpRigidBody core, PhysicsLedgeTree surface, IIvpMaterial material)
    {
        ArgumentNullException.ThrowIfNull(core);
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(material);

        // `FUN_180078b90` through the surface manager's slot `+0x10` (`18007aeb0`): the core's radius and deviation from the
        // surface's own header, each widened past the mass centre's distance from the core. *Which centre `dist` measures from is
        // INFERRED* — the core's, which sits at minus the object's offset in the object's frame; for a body whose object is placed
        // at its mass centre that distance is zero. The object's extra radius (`+0xe0`) is zero here.
        (float X, float Y, float Z) offset = core.ObjectOffset;
        double distance = IvpVector.Length(
            (surface.MassCenter.X + offset.X, surface.MassCenter.Y + offset.Y, surface.MassCenter.Z + offset.Z));

        // *A tree built for one ledge (`PhysicsLedgeTree.ForLedge`) has no surface header*, so it keeps the radius its body was given.
        if (surface.Radius > 0f)
        {
            core.Radius = (float)((double)surface.Radius + distance);
            core.Offset08 = (float)((double)(surface.Deviation * 0.004f * surface.Radius) + distance);
        }

        IvpCollisionObject collisionObject = new()
        {
            Core = core,
            Environment = Collisions,
            Surface = new IvpPolygonSurfaceManager(surface),
            MovementState = 1,
            Material = material,

            // **The friction core, `object+0xf0`.** The broad phase skips a pair whose two objects share one — two objects of the
            // same body — so leaving it unset makes every pair look like one body against itself.
            FrictionCore = core,
        };

        collisionObject.Node = new IvpOvNode(collisionObject);
        core.Objects.Add(collisionObject);
        Objects.Add(collisionObject);

        IvpBroadPhase.Refile(Collisions, collisionObject);

        return collisionObject;
    }

    /// <summary>Keeps the broad phase's own clock with the environment's.</summary>
    /// <remarks>
    /// **An object asks to be refiled itself**: its OV node is filed in its object's hull manager at the range it has left, and the
    /// node's slot 1 (<see cref="IvpOvNode.HullPassed"/>) runs the broad phase again when that range is spent. *A per-PSI refile
    /// looked harmless and was not — it reinstalled every filed pair's hull allowance each step, so no pair was ever told its hull
    /// had passed and two bodies drove through each other.*
    /// </remarks>
    private void SyncCollisionClock()
    {
        Collisions.Now = Environment.Now;
        Collisions.Step = Environment.Step;
        Collisions.Psi = _timeCode;
    }

    /// <summary>Sets the clock — <c>FUN_180082460</c>: <c>env+0x1a0 += 1; env+0x188 = time</c>.</summary>
    /// <remarks>
    /// **Every clock set is counted, the drain's final snap included**, and that count is the time code the minimize's
    /// once-per check and every object cache key on. *A private counter bumped per minimize stood in for it and was wrong both
    /// ways*: a cache keyed on it was not refreshed across PSIs that minimized nothing, and a pair minimized twice in one event
    /// was measured twice.
    /// </remarks>
    private void SetClock(double now)
    {
        Environment.Now = now;
        _timeCode++;
        SyncCollisionClock();
    }

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
    /// which is why this is one method rather than two. Bodies joined later share a unit through the merge (<c>FUN_180074e40</c>).
    /// </remarks>
    public IvpSimulationUnit Add(IvpRigidBody core)
    {
        ArgumentNullException.ThrowIfNull(core);

        IvpSimulationUnit unit = new();
        unit.Cores.Add(core);
        core.Unit = unit;
        core.Controllers.Add(_gravity);
        unit.RebuildEntries();

        // **A core is born asleep** — state 8, in a sleeping unit — and vphysics wakes a body it does not create asleep, which is
        // what revives the core (`FUN_1800892b0`) and gives it the state a collision reads.
        unit.Asleep();
        _units.Sleeping.Add(unit);
        Wake(unit);

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
            Merge(into, unit);
        }
        else
        {
            into.RebuildEntries();
        }

        return into;
    }

    /// <summary>One unit absorbs another, which leaves the time manager's lists — <c>FUN_180074e40</c>.</summary>
    private void Merge(IvpSimulationUnit into, IvpSimulationUnit other)
    {
        into.Absorb(other);
        _units.Active.Remove(other);
        _units.Sleeping.Remove(other);
    }

    /// <summary>A contact's filing merging its two movable cores' units — the tail of <c>FUN_180090e50</c>.</summary>
    private void MergeUnits(IvpRigidBody first, IvpRigidBody second)
    {
        if (first.Unit is { } into && second.Unit is { } other && !ReferenceEquals(into, other))
        {
            Merge(into, other);
        }
    }

    /// <summary>Queues the first PSI event, due at once — the time manager's own constructor does this for the engine.</summary>
    public void Start() =>
        IvpPsiEvent.Start(Environment, _time, _units, _mindists, _queue, Minimize, Examine, _random);

    /// <summary>Runs every PSI due before an absolute time, and every pair event those PSIs queued.</summary>
    /// <param name="target">The absolute time to simulate to.</param>
    /// <returns>How many PSIs fired.</returns>
    /// <remarks>
    /// **The engine keeps ONE queue** — the PSI event and every pair's event sit in the time manager's own min-list together, and
    /// each fires with the clock set to its own time. This port has two: <see cref="PhysicsTimeManager"/> for the PSI event and
    /// the mindist min-list the scheduler writes. **They are fired as one, one event at a time by time**: a PSI event only when it
    /// is due strictly before the earliest pair event, then that pair event at its own time, and the clock snapped to the target
    /// at the end. *Two wrong shapes came first*: firing a PSI's pair events after the next PSI, and draining every PSI up to the
    /// earliest pair event already queued — both left a pair event queued by one PSI waiting behind later ones, with the bodies
    /// stepped past the contact it was queued for. What remains different is a tie: a pair event at exactly a PSI's time fires
    /// first here, where the engine's min-list order decides.
    /// </remarks>
    public int Advance(double target)
    {
        int fired = 0;

        while (true)
        {
            bool pairDue = _queue.TryFirst(out _, out int slot) && _queue.ValueOf(slot) < target;

            if (_time.RunEarliest(pairDue ? _queue.ValueOf(slot) : target, SetClock))
            {
                fired++;
                continue;
            }

            if (!pairDue)
            {
                break;
            }

            FirePair();
        }

        SetClock(target);

        return fired;
    }

    /// <summary>How many pair events have fired — an instrument, not a field the engine keeps.</summary>
    public int PairEvents { get; private set; }

    /// <summary>Fires the earliest queued pair event — one turn of the time manager's loop over its own queue.</summary>
    private void FirePair()
    {
        if (!_queue.TryFirst(out IvpMindist? mindist, out int slot))
        {
            return;
        }

        double due = _queue.ValueOf(slot);
        _queue.Remove(slot);
        mindist.QueueSlot = null;
        PairEvents++;
        SetClock(due);

        IvpMindistFire.Handle(mindist, Minimize, recheck => Examine(mindist, recheck), Collide);
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
            // Each contact's own sides, synapse A first. *The mindist's record-order pair stood here for every contact, and swapped a
            // contact whose synapse A is record 1: its triangle was looked up on the other body and the record's normal came out
            // a hundredth long, so a cube landing flat on a slab gained speed on every push.*
            ContactSides,
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
        if (collisionObject.Core?.Unit is { } unit && Wake(unit))
        {
            Wakes++;
        }
    }

    /// <summary>Puts a unit to sleep at once — the game's forced sleep of a settled ragdoll, <c>IPhysicsObject::Sleep</c>.</summary>
    /// <param name="unit">The unit; one already asleep is left alone.</param>
    /// <remarks>
    /// *vphysics' `Sleep` → IVP's `disable_simulation` is not read*; this takes the unit down the same freeze its own rest check
    /// runs (<see cref="IvpSimulationUnit.Freeze"/>) and onto the sleeping list.
    /// </remarks>
    internal void Sleep(IvpSimulationUnit unit)
    {
        if (unit.State == 8)
        {
            return;
        }

        unit.Freeze(Environment, Environment.Now);
        _units.Active.Remove(unit);
        _units.Sleeping.Add(unit);
    }

    /// <summary>Wakes a unit, reviving its sleeping cores — <c>FUN_1800758e0(unit, env)</c>.</summary>
    /// <param name="unit">The unit.</param>
    /// <returns><c>true</c> when it had been asleep.</returns>
    internal bool Wake(IvpSimulationUnit unit) =>
        _units.Wake(unit, core => IvpUnitManager.Revive(core, Environment));

    /// <summary>How many sleeping units a collision has woken — an instrument, not a field the engine keeps.</summary>
    public int Wakes { get; private set; }

    /// <summary>How many mindists the environment holds alive — <c>env+0xb0</c>, which the pair creation keeps.</summary>
    public int Mindists => Collisions.LiveMindists;

    /// <summary>How many pairs are on the manager's exact list — the ones a PSI minimizes and can collide.</summary>
    public int ExactPairs => _mindists.Exact.Count;

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
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        // **A core carries its objects** (`core+0x68`, filled by `FUN_1800782d0`), and the per-core step advances each object's own
        // hull manager. An object not on its core's list would never have its hull advanced, so a pair filed at a hull value would
        // never be told.
        Register(first);
        Register(second);

        _mindists.LinkExact(mindist, first, second);
    }

    /// <summary>A pair becoming exact — <c>FUN_1800977f0</c>, for a new pair and for a far one whose hull passed.</summary>
    /// <remarks>
    /// *Linking it exact alone was tried first and was wrong twice over*: a far pair told its hull had passed was never
    /// minimized or examined again, so two bodies drove through each other with the pair still off every list.
    /// </remarks>
    private void BecomeExact(IvpMindist mindist)
    {
        IvpRigidBody firstCore = CoreOf(mindist.HullRecord(0));
        IvpRigidBody secondCore = CoreOf(mindist.HullRecord(1));

        _ = IvpMindistHull.BecomeExact(
            mindist,
            new IvpExactHandoff
            {
                Manager = _mindists,
                First = ObjectOf(mindist.HullRecord(0)),
                Second = ObjectOf(mindist.HullRecord(1)),
                Queue = _queue,
                FirstRechecked = firstCore.HasOffset58,
                SecondRechecked = secondCore.HasOffset58,
                FirstCoreState = firstCore.UnitState,
                SecondCoreState = secondCore.UnitState,
                Minimize = Minimize,
                Examine = removeFar => Examine(mindist, IvpRecheck.AtNow, removeFar),
            });
    }

    private static void Register(IvpCollisionObject collisionObject)
    {
        IvpRigidBody core = collisionObject.Core
            ?? throw new InvalidOperationException("A watched object has no core.");

        if (!core.Objects.Contains(collisionObject))
        {
            core.Objects.Add(collisionObject);
        }
    }

    /// <summary>The two ledge sides a mindist's synapses stand on, at the clock's time.</summary>
    /// <param name="mindist">The pair.</param>
    /// <returns>Record 0's side and record 1's.</returns>
    /// <exception cref="InvalidOperationException">A synapse's object has no core, or its core no ledge.</exception>
    /// <remarks>
    /// **Each side stands on its object's cache** (<see cref="IvpCollisionObject.CacheFor"/>), refreshed once per time code at the
    /// clock's own time — so a pair event between two PSIs measures the bodies where they are at the event. *The core's
    /// PSI-start matrix stood in for it and measured every pair event a PSI late.* **Each stands on its record's own ledge**, the one
    /// the pair creation named (<see cref="IvpMindist.Ledge"/>); a surface of many ledges measured on its first was a different body.
    /// </remarks>
    public (IvpLedgeSide First, IvpLedgeSide Second) SidesOf(IvpMindist mindist)
    {
        ArgumentNullException.ThrowIfNull(mindist);

        return (SideOf(mindist, 0), SideOf(mindist, 1));
    }

    /// <summary>A contact's two sides in its own order, synapse A first, each on its own ledge.</summary>
    private (IvpLedgeSide First, IvpLedgeSide Second) ContactSides(IvpContactPoint contact) =>
        (SideOf(contact.FirstObject, contact.FirstLedge), SideOf(contact.SecondObject, contact.SecondLedge));

    private IvpLedgeSide SideOf(IvpMindist mindist, int record) =>
        SideOf(
            mindist.HullRecord(record).CollisionObject ?? throw new InvalidOperationException("A synapse record was never linked to an object."),
            mindist.Ledge(record)?.Ledge);

    /// <summary>A side on the ledge its synapse stands on — the one the pair creation named, else the core's own first.</summary>
    /// <remarks>*A mindist made from features alone (a test's hand-built pair) names no ledge, and takes the core's first.*</remarks>
    private IvpLedgeSide SideOf(IvpCollisionObject collisionObject, PhysicsLedge? ledge)
    {
        IvpRigidBody core = collisionObject.Core
            ?? throw new InvalidOperationException("A watched object has no core.");

        PhysicsLedge standing = ledge
            ?? (core.Ledges.Count > 0 ? core.Ledges[0] : throw new InvalidOperationException("A watched object's core has no ledge to stand a synapse on."));

        IvpObjectCache cache = collisionObject.CacheFor(Collisions);

        return IvpLedgeSide.FromLedge(standing, cache.Matrix, cache.CorePosition);
    }

    /// <summary>The minimize the pipeline's two mindist walks take — <c>FUN_180095cb0</c>.</summary>
    private void Minimize(IvpMindist mindist)
    {
        (IvpLedgeSide first, IvpLedgeSide second) = SidesOf(mindist);

        _ = IvpMindistMinimize.Minimize(mindist, first, second, _timeCode);
        LastLength = mindist.Length;
    }

    /// <summary>The distance the last minimize measured for a pair — an instrument, not a field the engine keeps.</summary>
    public float LastLength { get; private set; }

    /// <summary>The scheduler in mode 1 the pipeline's last phase takes — <c>FUN_180099380(mindist, 1, 1)</c>.</summary>
    /// <remarks>
    /// **The pair's time of impact is searched over its own sides** (<see cref="IvpImpactDispatch.Search"/>), and the outcome is
    /// remembered as <see cref="LastOutcome"/> so a caller can see what the scheduler decided. *`removeFar` is null*: a pair past
    /// its far threshold is left exact rather than filed with the objects' hull managers, because that filing is the broad
    /// phase's and is not carried here.
    /// </remarks>
    private void Examine(IvpMindist mindist) => Examine(mindist, IvpRecheck.AfterFeatureChange);

    private void Examine(IvpMindist mindist, IvpRecheck recheck) => Examine(mindist, recheck, removeFar: true);

    private void Examine(IvpMindist mindist, IvpRecheck recheck, bool removeFar)
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
            removeFar ? Filing(mindist) : null,
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

    /// <summary>
    /// What a far pair is filed with — the objects' own hull managers, and what their slot 1 hands the pair back to
    /// (<c>FUN_180097bd0</c> to file, <c>FUN_180097570</c> to tell).
    /// </summary>
    /// <param name="mindist">The pair.</param>
    /// <returns>The filing.</returns>
    /// <remarks>
    /// **This closes the near/far cycle**: a pair past its threshold leaves the exact list and is filed at a hull value, the tail's
    /// hull pass tells it when its object's hull reaches that value, and <see cref="IvpMindistHull.HullPassed"/> makes it exact
    /// again.
    /// </remarks>
    private IvpFarFiling Filing(IvpMindist mindist) =>
        new(_mindists, ObjectOf(mindist.HullRecord(0)), ObjectOf(mindist.HullRecord(1)), HullPassed);

    /// <summary>A filed pair told its object's hull has passed — <c>FUN_180097570</c> into <c>FUN_180097f00</c>.</summary>
    private void HullPassed(IvpMindist mindist, float overshoot)
    {
        IvpCollisionObject first = ObjectOf(mindist.HullRecord(0));
        IvpCollisionObject second = ObjectOf(mindist.HullRecord(1));
        IvpRigidBody firstCore = CoreOf(mindist.HullRecord(0));
        IvpRigidBody secondCore = CoreOf(mindist.HullRecord(1));

        LastHullPass = IvpMindistHull.HullPassed(
            mindist,
            overshoot,
            new IvpHullPass
            {
                Now = Environment.Now,
                Step = Environment.Step,
                First = first,
                Second = second,
                FirstBody = firstCore,
                SecondBody = secondCore,
                FirstBounds = IvpRangeManager.Bounds(firstCore),
                SecondBounds = IvpRangeManager.Bounds(secondCore),
                HandOff = BecomeExact,
                Recheck = pair => Examine(pair, IvpRecheck.AfterMiss),
            });
    }

    /// <summary>What the last hull pass did with a filed pair — an instrument, not a field the engine keeps.</summary>
    public IvpHullPassOutcome? LastHullPass { get; private set; }

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
