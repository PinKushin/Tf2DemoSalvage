using System.Linq;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Probe.Oracle;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>Taking an object out of the world — <c>FUN_180073700</c> and its two neighbour walks (B369, D172).</summary>
/// <remarks>
/// **Removal is not a list-unlink, and that is the whole finding** (`docs/findings/51`, *Removing an object*). The engine treats a
/// departing object as one that just went STATIC: it freezes it, re-files it in the broad phase so the neighbours see a settled
/// picture, and then re-derives each neighbour's pair and contact against that picture. A mindist dies because the re-derivation
/// finds no live partner — searching the destroy path for <c>IvpMindistManager::Unlink</c> or <c>IvpOvTree::Remove</c> finds
/// nothing, because they are reached one level down inside the ordinary minimize.
///
/// **The order is the part a from-scratch removal gets wrong**, so these tests pin the order rather than the end state:
/// the refile happens under a temporary freeze, the walks run AFTER it, and waking a neighbour is something removal DOES.
/// A body resting on the corpse that is about to vanish has to be woken, or it hangs in the air on a contact whose other half no
/// longer exists.
///
/// Synthetic throughout (D38): every value asserted is one the test put there.
///
/// *Interpolated, and flagged because it decides two of these assertions*: the engine's "is that core asleep" test is
/// <c>core+0x0 &amp; 2</c>, a core-level bit this port does not carry — the port models sleep on the UNIT
/// (<see cref="IvpSimulationUnit.State"/>, <c>8</c> asleep). These tests use the unit's state as the stand-in. If a core-level
/// asleep bit is ever ported, they should move onto it.
/// </remarks>
public sealed class IvpObjectRemovalConformanceTests
{
    /// <remarks>
    /// <c>FUN_1800788b0</c>'s awake branch calls <c>FUN_180073a30</c> for every object in the neighbour core's own buckets, and
    /// that function IS <c>IPhysicsObject::Wake</c>'s body: an object whose movement state is <c>8</c> takes
    /// <c>FUN_180087e00</c>, which lists the core for the next PSI's revive rather than waking it on the spot.
    /// </remarks>
    [Test]
    public void Remove_ACoreWhoseContactNeighbourIsAwake_QueuesThatNeighboursRevive()
    {
        IvpSimulation simulation = Simulation(out IvpRigidBody going, out IvpRigidBody staying);
        IvpCollisionObject neighbourObject = ContactBetween(going, staying);

        simulation.Remove(going);

        neighbourObject.Core.ShouldBe(staying);
        staying.ReviveQueued.ShouldBeTrue("a body resting on the removed one must be woken, not left on a dead contact");
    }

    /// <remarks><c>FUN_1800788b0</c>'s asleep branch: <c>FUN_180083210</c> then <c>operator delete</c> — the contact is gone.</remarks>
    [Test]
    public void Remove_ACoreWhoseContactNeighbourIsAsleep_DropsTheContact()
    {
        IvpSimulation simulation = Simulation(out IvpRigidBody going, out IvpRigidBody staying);
        IvpCollisionObject neighbourObject = ContactBetween(going, staying);
        staying.Unit!.Asleep();

        simulation.Remove(going);

        going.Objects[0].ContactPoints.ShouldBeEmpty("the neighbour is asleep, so the contact is deleted rather than rebuilt");
        neighbourObject.ContactPoints.ShouldBeEmpty();
        staying.ReviveQueued.ShouldBeFalse("an asleep neighbour is left asleep");
    }

    /// <remarks>
    /// <c>FUN_180073700</c> writes <c>0x21</c> to the core's byte at <c>+1</c> and the object's at <c>+0xf</c>, calls
    /// <c>IvpBroadPhase::Refile</c>, and then writes BOTH back. The freeze lasts for the refile only — it is there so the
    /// neighbour walks below it see a settled picture, not to leave the object frozen.
    ///
    /// *Not independently observed*: that the value is <c>0x21</c> DURING the refile. The port's refile is a static call with no
    /// seam to watch it through, so this asserts the restore half only. Reading the state back unchanged is what fails if an
    /// implementation forgets to restore it.
    /// </remarks>
    [Test]
    public void Remove_ACoreThatWasAwake_RestoresTheStateBytesTheRefileForced()
    {
        IvpSimulation simulation = Simulation(out IvpRigidBody going, out _);
        going.UnitState = 3;
        going.Objects[0].MovementState = 4;

        simulation.Remove(going);

        going.UnitState.ShouldBe(3, "the 0x21 freeze is for the refile's duration only");
        going.Objects[0].MovementState.ShouldBe(4);
    }

    /// <remarks>
    /// <c>CPhysicsEnvironment::DestroyObject</c> (<c>0x180013250</c>) takes the object out of the environment's active list by
    /// swap-with-last before anything else; <c>FUN_180073700</c> then drops the core from the active-core bucket
    /// (<c>FUN_180075610</c> or <c>FUN_1800758e0</c>). Nothing may still step it afterwards.
    /// </remarks>
    [Test]
    public void Remove_ACore_LeavesNoUnitHoldingIt()
    {
        IvpSimulation simulation = Simulation(out IvpRigidBody going, out IvpRigidBody staying);

        simulation.Remove(going);

        simulation.Units.Active.Concat(simulation.Units.Sleeping)
            .SelectMany(unit => unit.Cores)
            .ShouldNotContain(going);
        simulation.Units.Active.Concat(simulation.Units.Sleeping)
            .SelectMany(unit => unit.Cores)
            .ShouldContain(staying, "removing one body does not disturb the others");
    }

    /// <summary>A contact between the two bodies, filed on both objects as the engine's buckets hold it.</summary>
    private static IvpCollisionObject ContactBetween(IvpRigidBody going, IvpRigidBody staying)
    {
        IvpCollisionObject goingObject = new() { Core = going };
        IvpCollisionObject stayingObject = new() { Core = staying };

        going.Objects.Add(goingObject);
        staying.Objects.Add(stayingObject);

        IvpMindist mindist = new(
            new IvpSynapse(new IvpLedgeEdge(0, 0), IvpFeatureKind.Point),
            new IvpSynapse(new IvpLedgeEdge(0, 0), IvpFeatureKind.Triangle),
            extraRadius: 0f)
        {
            Flags = 0xC0000,
        };

        new IvpMindistManager().LinkExact(mindist, goingObject, stayingObject);

        IvpLedgeSide side = IvpContactGeometryConformanceTests.Anywhere();
        IvpContactPoint contact = new(mindist, goingObject, side, stayingObject, side, now: 0d);

        goingObject.ContactPoints.AddLast(contact);
        stayingObject.ContactPoints.AddLast(contact);

        return stayingObject;
    }

    private static IvpSimulation Simulation(out IvpRigidBody going, out IvpRigidBody staying)
    {
        going = Body();
        staying = Body();

        IvpSimulation simulation = new(
            new IvpImpactEnvironment
            {
                InverseStep = 2d,
                Step = 0.5d,
                Limits = new IvpAnomalyLimits(1000f, 0, 1000f, 250, 0f, 0f),
                Anomalies = new VphysicsAnomalyManager(new IvpImpactReplay.FixedAnswer(answer: false)),
                Materials = new IvpReplayMaterials(new IvpReplayMaterial(0d, 0d, HasSecondFriction: false), 0d, 0d),
                RestDelay = 1f,
                RestCheckCountdown = 5,
            },
            (0f, 0f, -10f),
            () => 0f);

        simulation.Add(going);
        simulation.Add(staying);

        return simulation;
    }

    private static IvpRigidBody Body() =>
        new()
        {
            Orientation = (0d, 0d, 0d, 1d),
            WorkingOrientation = (0d, 0d, 0d, 1d),
            Radius = 1f,
            InverseMass = 1f,
            Damping = 0f,
            RotationDamping = 0f,
        };
}
