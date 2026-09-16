using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Probe.Oracle;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>A watched pair minimized by the PSI's own walk — the pipeline's phases 5 and 6 (B369, D172).</summary>
/// <remarks>
/// **The broad phase is not carried here**, so the pair is named by the caller; everything after that is the engine's path — the
/// minimize each PSI (<c>FUN_1800983e0</c>) over sides built from each core's live transform. Synthetic conformance (D38).
/// </remarks>
public sealed class IvpSimulationWatchTests
{
    private const float Half = 4f;

    [Test]
    public void SidesOf_AWatchedPair_StandsEachSynapseOnItsOwnCoresTransform()
    {
        (IvpSimulation simulation, IvpMindist mindist, IvpRigidBody first, _) = Watched();
        simulation.Start();
        simulation.Advance(0.01d);

        (IvpLedgeSide side, _) = simulation.SidesOf(mindist);

        side.CorePosition.ShouldBe(first.Position);
        side.Points.Count.ShouldBe(8, "the cube's own points, in the object's frame");
    }

    /// <remarks>
    /// **A pair event between PSIs measures the bodies at the event**: the side stands on the object's cache, refreshed at the
    /// clock's time, so a body moving since its last step is placed along its velocity.
    /// </remarks>
    [Test]
    public void SidesOf_AMovingBodyBetweenPsis_StandsWhereTheBodyIsAtTheClock()
    {
        (IvpSimulation simulation, IvpMindist mindist, IvpRigidBody first, _) = Watched();
        first.PreviousVelocity = (0f, 0f, 10f);
        first.LastStepped = 0d;
        simulation.Environment.Now = 0.25d;
        simulation.Collisions.Now = 0.25d;
        simulation.Collisions.Psi = 99;

        (IvpLedgeSide side, _) = simulation.SidesOf(mindist);

        side.CorePosition.Z.ShouldBe(first.Position.Z + 2.5d, 1e-9d);
        side.Current.Translation.Z.ShouldBe(first.Position.Z + 2.5d, 1e-9d, "the matrix stands there too");
    }

    [Test]
    public void Advance_AWatchedPair_IsMinimizedEveryPsi()
    {
        (IvpSimulation simulation, IvpMindist mindist, _, _) = Watched();
        simulation.Start();

        simulation.Advance(0.05d);

        mindist.MinimizedAt.ShouldNotBeNull("the pipeline's own walk minimized it");
    }

    /// <remarks>**The measure is between the two bodies**, so moving one of them changes what the minimize finds.</remarks>
    [Test]
    public void Advance_TwoBodiesApart_MeasuresAGapBetweenThem()
    {
        (IvpSimulation simulation, IvpMindist mindist, _, IvpRigidBody second) = Watched(apart: 40d);
        simulation.Start();

        simulation.Advance(0.05d);
        float far = mindist.Length;

        (IvpSimulation closer, IvpMindist close, _, _) = Watched(apart: 12d);
        closer.Start();
        closer.Advance(0.05d);

        close.Length.ShouldBeLessThan(far, "the nearer pair measures the shorter gap");
        second.Position.Z.ShouldBe(40d, 1e-9d, "the control: the far body really is where the fixture put it");
    }

    /// <remarks>
    /// **Phase 6 is the re-examine** (<c>FUN_180099380(mindist, 1, 1)</c>): two bodies past the far threshold are FILED with their
    /// objects' hull managers and leave the exact list, which is what closes the near/far cycle.
    /// </remarks>
    [Test]
    public void Advance_TwoBodiesFarApart_AreFiledWithTheirHullManagers()
    {
        (IvpSimulation simulation, IvpMindist mindist, IvpRigidBody first, IvpRigidBody second) = Watched(apart: 400d);
        simulation.Start();

        simulation.Advance(0.05d);

        simulation.LastOutcome.ShouldBe(IvpScheduleOutcome.Filed);
        mindist.HullRecord(0).HullSlot.ShouldNotBeNull("record 0 is filed at a hull value");
        mindist.HullRecord(1).HullSlot.ShouldNotBeNull();
        first.Unit.ShouldNotBeNull("the control: the fixture's bodies are really in the simulation");
        second.Unit.ShouldNotBeNull();
    }

    /// <remarks>
    /// **The cycle's other half**: a filed pair whose body then moves far enough for its hull to pass is told — the tail's hull
    /// pass (<c>FUN_18009a690</c>) into <c>FUN_180097570</c> — and handed to <c>FUN_1800977f0</c>, which measures it again. A pair
    /// still far is filed again at once, so what shows the cycle is the told pair's new, shorter length.
    /// </remarks>
    [Test]
    public void Advance_AFiledPairWhoseBodyMoves_IsToldAndMeasuredAgain()
    {
        (IvpSimulation simulation, IvpMindist mindist, IvpRigidBody first, _) = Watched(apart: 400d);
        simulation.Start();
        simulation.Advance(0.05d);
        simulation.LastOutcome.ShouldBe(IvpScheduleOutcome.Filed, "the control: it really was filed first");
        float filedAt = mindist.Length;

        // Fast enough that the hull it was filed at is passed within a few PSIs.
        first.Velocity = (0f, 0f, 3000f);
        first.PreviousVelocity = (0f, 0f, 3000f);

        for (double until = 0.1d; until <= 0.3d; until += 0.05d)
        {
            simulation.Advance(until);
        }

        simulation.LastHullPass.ShouldNotBeNull("its hull passed and it was told");
        mindist.Length.ShouldBeLessThan(filedAt - 100f, "measured again where the body has moved to");
    }

    /// <remarks>
    /// **A near pair is the case that reads the velocity**: the far branch answers on distance alone, so only a pair close enough
    /// to close within a PSI shows that the scheduler was handed the bodies' own motion.
    /// </remarks>
    [Test]
    public void Advance_TwoBodiesCloseAndClosing_AreNotScheduledAsFar()
    {
        (IvpSimulation simulation, _, IvpRigidBody first, _) = Watched(apart: 9d);
        first.Velocity = (0f, 0f, 400f);
        first.PreviousVelocity = (0f, 0f, 400f);
        simulation.Start();

        simulation.Advance(0.05d);

        simulation.LastOutcome.ShouldNotBe(IvpScheduleOutcome.Far, "a body a unit away and closing fast is not far");
    }

    /// <remarks>**The bounds the scheduler reads are written by the step**, so a moving body's own speed reaches it.</remarks>
    [Test]
    public void Advance_AMovingBody_HasItsSpeedBoundsWrittenEveryStep()
    {
        (IvpSimulation simulation, _, IvpRigidBody first, _) = Watched();
        first.Velocity = (0f, 0f, -120f);
        // Off the x axis, so the axis written is not the same vector the no-turn fallback uses.
        first.AngularVelocity = (0f, 2f, 1f);
        simulation.Start();

        simulation.Advance(0.05d);

        first.LinearSpeed.ShouldBe(120f, 1e-3f);
        first.AngularSpeedBound.ShouldBeGreaterThan(0f, "a turning body bounds its own spin");
        first.RotationAxis.ShouldNotBe((1f, 0f, 0f), "the fallback axis is replaced by the step's own");
    }

    /// <remarks>
    /// **A queued pair event fires and collides**: the scheduler queues it in phase 6, the drain hands it to
    /// <c>FUN_1800992e0</c>, and a collision links a contact into a friction system — which is the whole chain, end to end, in the
    /// ported driver.
    /// </remarks>
    [Test]
    public void Advance_TwoBodiesTouching_FireTheirPairEvent()
    {
        // Cubes of half four, their facing sides 0.6 apart. The far threshold is the step times the closing bound plus the margin,
        // which at this speed is about a unit.
        (IvpSimulation simulation, IvpMindist mindist, IvpRigidBody first, _) = Watched(apart: 8.6d);
        first.Velocity = (0f, 0f, 60f);
        first.PreviousVelocity = (0f, 0f, 60f);
        simulation.Start();

        // Two slices: the scheduler queues the pair's event a fraction past the end of the first, and the second reaches it.
        simulation.Advance(0.1d);
        simulation.Advance(0.2d);

        simulation.PairEvents.ShouldBeGreaterThan(
            0, $"the pair's own event fired; last outcome {simulation.LastOutcome}, length {simulation.LastLength}, z {first.Position.Z}");
        mindist.MinimizedAt.ShouldNotBeNull("the control: the pipeline reached the pair at all");
    }

    /// <remarks>
    /// **A collision wakes a sleeping unit** (<c>FUN_180074360</c> into <c>FUN_1800758e0</c>): the unit sleeps at the rest check,
    /// and the pair's own event puts it back on the active list.
    /// </remarks>
    [Test]
    public void Advance_AStillUnit_SleepsAtItsOwnRestCheck()
    {
        // Two things decide when a unit can sleep, and both are the engine's: the check has to land on a PSI where time has passed
        // (at the first event, now IS the anchor time, so a body reads Still), and the countdown at `env+0x1a8` is decremented once
        // per UNIT, so with two units one check reaches one of them.
        (IvpSimulation simulation, _, IvpRigidBody first, _) = Watched(apart: 0.6d, restCheckCountdown: 4);
        simulation.Start();

        simulation.Advance(0.05d);

        simulation.AwakeUnits.ShouldBe(1, $"one of the two units slept; core state {first.UnitState}");
    }

    /// <remarks>**The list move a collision asks for** — <c>FUN_1800758e0</c>'s tail, which <c>FUN_180074360</c> reaches for an
    /// object whose own state is <c>8</c>.</remarks>
    [Test]
    public void Wake_ASleepingUnit_GoesBackOnTheActiveList()
    {
        (IvpSimulation simulation, _, _, _) = Watched(apart: 0.6d, restCheckCountdown: 4);
        simulation.Start();
        simulation.Advance(0.05d);
        simulation.Units.Sleeping.Count.ShouldBe(1, "the control: exactly one unit is asleep to wake");
        IvpSimulationUnit asleep = simulation.Units.Sleeping[0];

        simulation.Wake(asleep).ShouldBeTrue();

        asleep.State.ShouldBe(1);
        simulation.AwakeUnits.ShouldBe(2);
        simulation.Units.Sleeping.ShouldBeEmpty();
    }

    /// <remarks>
    /// **A woken core that had been stepped is revived at rest** (<c>FUN_1800892b0</c>): its state is 1, its clock and anchors are
    /// now, and its velocity and the step's bookkeeping are zeroed.
    /// </remarks>
    [Test]
    public void Wake_ASleepingSteppedCore_IsRevivedAtRestAtNow()
    {
        (IvpSimulation simulation, _, _, _) = Watched(apart: 0.6d, restCheckCountdown: 4);
        simulation.Start();
        simulation.Advance(0.05d);
        IvpRigidBody core = simulation.Units.Sleeping[0].Cores[0];
        core.Velocity = (3f, 0f, 0f);
        core.LinearSpeed = 3f;

        simulation.Wake(simulation.Units.Sleeping[0]);

        core.UnitState.ShouldBe(1);
        core.LastStepped.ShouldBe(simulation.Now);
        core.RestAnchorTime.ShouldBe(simulation.Now);
        core.Velocity.ShouldBe((0f, 0f, 0f), "a core stepped before loses its velocity");
        core.LinearSpeed.ShouldBe(0f);
    }

    /// <remarks>
    /// **A revived core rebuilds its resting contacts** (<c>FUN_180086500</c>): each of its pairs with a movable core that has no
    /// friction system of its own is minimized, and one closer than <see cref="IvpCollisionTolerance.RestingContactGap"/> becomes a
    /// contact point in a friction system, with no impact. Two still cubes whose faces are 0.005 apart: inside the margin, so the
    /// pair stays exact rather than filed far (a filed pair is on no exact list for the walk to find), and under the 0.0286 gap.
    /// </remarks>
    [Test]
    public void Wake_ASleepingCoreBesideAnotherWithinTheRestingGap_BuildsAContactWithoutAnImpact()
    {
        (IvpSimulation simulation, IvpMindist pair, IvpRigidBody first, IvpRigidBody second) = Watched(apart: 8.005d, restCheckCountdown: 4);
        simulation.Start();
        simulation.Advance(0.05d);
        simulation.Units.Sleeping.Count.ShouldBe(1, "the control: exactly one unit is asleep to wake");
        IvpRigidBody asleep = simulation.Units.Sleeping[0].Cores[0];
        IvpRigidBody awake = ReferenceEquals(asleep, first) ? second : first;
        awake.FrictionInfo.ShouldBeNull("the control: the other core has no system before the wake");
        System.Linq.Enumerable.Sum(asleep.Objects, o => o.Synapses.Count).ShouldBe(1, "the control: the pair is still exact");

        simulation.Wake(simulation.Units.Sleeping[0]);

        awake.FrictionInfo.ShouldNotBeNull(
            $"objects {asleep.Objects.Count}, synapses {System.Linq.Enumerable.Sum(asleep.Objects, o => o.Synapses.Count)}, flags 0x{pair.Flags:x}, length {pair.Length}");
        asleep.FrictionInfo.ShouldNotBeNull();
        simulation.Environment.Impacts.ShouldBe(0);
    }

    /// <remarks>**A core never stepped keeps the velocity it was made with** — the save and restore around <c>FUN_180077670</c>.</remarks>
    [Test]
    public void Add_ABodyMadeMoving_IsRevivedWithItsVelocity()
    {
        IvpSimulation simulation = new(Environment(), (0f, 0f, 0f), () => 0f);
        IvpRigidBody core = Body((0d, 0d, 0d));
        core.Velocity = (0f, 0f, 5f);
        core.PreviousVelocity = (0f, 0f, 5f);

        simulation.Add(core);

        core.UnitState.ShouldBe(1, "woken as it was added");
        core.Velocity.ShouldBe((0f, 0f, 5f));
        core.PreviousVelocity.ShouldBe((0f, 0f, 0f), "the step's own record of it is zeroed");
        simulation.AwakeUnits.ShouldBe(1);
    }

    [Test]
    public void Wake_AnAwakeUnit_IsLeftAlone()
    {
        (IvpSimulation simulation, _, IvpRigidBody first, _) = Watched();

        simulation.Wake(first.Unit!).ShouldBeFalse();

        simulation.AwakeUnits.ShouldBe(2, "it was already on the active list, and is not there twice");
    }

    private static (IvpSimulation, IvpMindist, IvpRigidBody, IvpRigidBody) Watched(double apart = 40d, short restCheckCountdown = 15)
    {
        IvpSimulation simulation = new(Environment(restCheckCountdown), (0f, 0f, 0f), () => 0f);

        // Both bodies sit OFF the origin, so a side built at the origin instead of at its own core is a different side.
        IvpRigidBody first = Body((7d, -3d, 0d));
        IvpRigidBody second = Body((7d, -3d, apart));
        simulation.Add(first);
        simulation.Add(second);

        IvpReplayMaterial material = new(0d, 0d, HasSecondFriction: false);
        IvpCollisionObject firstObject = new() { Core = first, Material = material };
        IvpCollisionObject secondObject = new() { Core = second, Material = material };
        IvpMindist mindist = new(
            new IvpSynapse(new IvpLedgeEdge(0, 0), IvpFeatureKind.Point),
            new IvpSynapse(new IvpLedgeEdge(0, 0), IvpFeatureKind.Point),
            extraRadius: 0f);

        simulation.Watch(mindist, firstObject, secondObject);

        return (simulation, mindist, first, second);
    }

    private static IvpRigidBody Body((double X, double Y, double Z) at) =>
        new()
        {
            Position = at,
            Orientation = (0d, 0d, 0d, 1d),
            WorkingOrientation = (0d, 0d, 0d, 1d),
            CoreMatrix = IvpMatrix.FromRotation((0f, 0f, 0f, 1f), at),
            Radius = Half,
            InverseMass = 1f,
            InverseInertia = (1f, 1f, 1f),
            Damping = 0f,
            RotationDamping = 0f,
            // The anchors a body that has been sitting where it is would hold — position as well as orientation, or the rest test
            // reads it as having moved from the origin.
            RestAnchorOrientation = (0f, 0f, 0f, 1f),
            SettleAnchorOrientation = (0f, 0f, 0f, 1f),
            RestAnchorPosition = ((float)at.X, (float)at.Y, (float)at.Z),
            SettleAnchorPosition = ((float)at.X, (float)at.Y, (float)at.Z),
            Ledges = IvpTestCube.Ledges(Half),
        };

    private static IvpImpactEnvironment Environment(short restCheckCountdown = 15) =>
        new()
        {
            InverseStep = 66d,
            Step = 1d / 66d,
            Limits = new IvpAnomalyLimits(2000f, 6, 3600f, 250, 1f, 1e30f),
            Anomalies = new VphysicsAnomalyManager(new IvpImpactReplay.FixedAnswer(answer: false)),
            Materials = new IvpReplayMaterials(new IvpReplayMaterial(0d, 0d, HasSecondFriction: false), 0d, 0d),

            // A rest delay of zero lets a still body be called resting on its first check rather than after five seconds.
            RestDelay = 0f,
            RestCheckCountdown = restCheckCountdown,
        };
}
