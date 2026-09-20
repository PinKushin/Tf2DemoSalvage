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
    public void Remove_ACoreWhoseContactNeighbourIsInStateEight_QueuesThatNeighboursRevive()
    {
        IvpSimulation simulation = Simulation(out IvpRigidBody going, out IvpRigidBody staying);
        IvpCollisionObject neighbourObject = ContactBetween(going, staying);
        neighbourObject.MovementState = 8;

        simulation.Remove(going);

        staying.ReviveQueued.ShouldBeTrue("a body resting on the removed one must be woken, not left on a dead contact");
    }

    /// <remarks>
    /// The other half of <c>FUN_180073a30</c>, and the control for the test above: an object NOT in state <c>8</c> takes
    /// <c>FUN_180078820</c> instead, which only resets the core's two anchor times to now — so the rest test cannot call it
    /// settled on the strength of an anchor older than the contact that has just gone. It is not queued.
    /// </remarks>
    [Test]
    public void Remove_ACoreWhoseContactNeighbourIsNotInStateEight_ResetsThatNeighboursAnchorsWithoutQueueingIt()
    {
        IvpSimulation simulation = Simulation(out IvpRigidBody going, out IvpRigidBody staying);
        IvpCollisionObject neighbourObject = ContactBetween(going, staying);
        neighbourObject.MovementState = 1;
        staying.RestAnchorTime = -5d;
        staying.SettleAnchorTime = -5d;

        simulation.Remove(going);

        staying.RestAnchorTime.ShouldBe(simulation.Now);
        staying.SettleAnchorTime.ShouldBe(simulation.Now);
        staying.ReviveQueued.ShouldBeFalse("only a state-8 object joins the revive list");
    }

    /// <remarks><c>FUN_1800788b0</c>'s asleep branch: <c>FUN_180083210</c> then <c>operator delete</c> — the contact is gone.</remarks>
    [Test]
    public void Remove_ACoreWhoseContactNeighbourIsAsleep_DropsTheContact()
    {
        IvpSimulation simulation = Simulation(out IvpRigidBody going, out IvpRigidBody staying);
        IvpCollisionObject neighbourObject = ContactBetween(going, staying);
        IvpCollisionObject goingObject = going.Objects[0];
        staying.Unit!.Asleep();

        simulation.Remove(going);

        goingObject.ContactPoints.ShouldBeEmpty("the neighbour is asleep, so the contact is deleted rather than rebuilt");
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
        IvpCollisionObject goingObject = going.Objects[0];
        going.UnitState = 3;
        goingObject.MovementState = 4;

        simulation.Remove(going);

        going.UnitState.ShouldBe(3, "the 0x21 freeze is for the refile's duration only");
        goingObject.MovementState.ShouldBe(4);
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

    /// <remarks>
    /// **<c>DestroyConstraint</c>'s notify is <c>Wake</c>**, which the destroy dump had guessed was bookkeeping. Slot
    /// <c>+0xc0</c> of <c>CPhysicsObject</c>'s vtable (<c>0x1800ecbf0</c>) is the pointer at <c>0x1800eccb0</c>,
    /// <c>0x18001e3d0</c>, which disassembles to <c>mov rcx, [rcx+0x10]</c> then a tail jump to <c>0x180073a30</c> —
    /// <c>IPhysicsObject::Wake</c>'s body, the same routine the contact walk calls.
    ///
    /// So a ragdoll losing its joints has every limb woken: each must resume simulating on its own rather than staying asleep
    /// in a pose the joints were holding.
    /// </remarks>
    [Test]
    public void RemoveConstraints_AGroupJoiningTwoBodies_WakesBothOfThem()
    {
        IvpSimulation simulation = Simulation(out IvpRigidBody first, out IvpRigidBody second);
        first.Objects[0].MovementState = 8;
        second.Objects[0].MovementState = 8;
        IvpConstraintGroup group = Group(first, second);
        simulation.Add(group);

        simulation.RemoveConstraints(group);

        first.ReviveQueued.ShouldBeTrue("the reference object is told through its vtable +0xc0, which is Wake");
        second.ReviveQueued.ShouldBeTrue("and so is the attached object");
    }

    /// <remarks>
    /// The constraint's notify reaches endpoint objects it still holds live pointers to, so it cannot run after their own
    /// <c>DestroyObject</c>. <c>CLAUDE.md</c>'s note that <c>RagdollDestroy</c> destroys constraints first is confirmed by
    /// that: waking a body about to be removed is harmless, waking one already torn down is not.
    /// </remarks>
    [Test]
    public void RemoveConstraints_AGroup_TakesItsControllerOffBothBodies()
    {
        IvpSimulation simulation = Simulation(out IvpRigidBody first, out IvpRigidBody second);
        IvpConstraintGroup group = Group(first, second);
        simulation.Add(group);

        simulation.RemoveConstraints(group);

        first.Controllers.ShouldNotContain(controller => controller is IvpConstraintController);
        second.Controllers.ShouldNotContain(controller => controller is IvpConstraintController);
    }

    private static IvpConstraintGroup Group(IvpRigidBody first, IvpRigidBody second)
    {
        IvpConstraintGroup group = new();

        group.Joints.Add(new IvpRagdollJoint
        {
            BodyA = first,
            BodyB = second,
            Constraint = IvpRagdollConstraint.FromDegrees(
                primary: (-30f, 15f),
                narrower: (-25f, 25f),
                wider: (-79f, 57f),
                reference: IvpConstraintFrame.Identity,
                attached: IvpConstraintFrame.Identity),
        });

        return group;
    }

    /// <summary>A contact between the two bodies' objects, filed on both as the engine's buckets hold it.</summary>
    /// <remarks>
    /// **The mindist is built, used and then taken back off both objects' synapse lists**, so these tests exercise the CONTACT
    /// walk (<c>FUN_1800788b0</c>) alone. <see cref="IvpContactPoint"/> needs a linked mindist to take its two records from, but
    /// leaving it linked would also put the mindist walk (<c>FUN_180086500</c>) in the way, and that one re-minimizes — which
    /// these synthetic bodies cannot do, having no ledges to stand a synapse on. The mindist walk gets its own coverage from
    /// <c>RebuildRestingContacts</c>'s existing tests, which is the same routine.
    /// </remarks>
    private static IvpCollisionObject ContactBetween(IvpRigidBody going, IvpRigidBody staying)
    {
        IvpCollisionObject goingObject = going.Objects[0];
        IvpCollisionObject stayingObject = staying.Objects[0];

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

        goingObject.Synapses.Clear();
        stayingObject.Synapses.Clear();

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

        // One collision object each, which is what the broad phase files and what the two walks iterate.
        foreach (IvpRigidBody body in new[] { going, staying })
        {
            IvpCollisionObject collisionObject = new() { Core = body };

            body.Objects.Add(collisionObject);
            simulation.Objects.Add(collisionObject);
        }

        // **Both woken and taken off the revive list, or two of these tests could not fail.** `Add` puts a body in a SLEEPING
        // unit and queues its revive for the first PSI (B369) — so a removal that woke nothing would still find
        // `ReviveQueued` true from creation, and a removal that skipped its walks entirely would be indistinguishable, since
        // `FUN_180073700` runs them only for an awake core.
        foreach (IvpRigidBody body in new[] { going, staying })
        {
            body.Unit!.Woken();
            simulation.Units.Sleeping.Remove(body.Unit);
            simulation.Units.Active.Add(body.Unit);
            simulation.Environment.ReviveQueue.Remove(body);
            body.ReviveQueued = false;
        }

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
