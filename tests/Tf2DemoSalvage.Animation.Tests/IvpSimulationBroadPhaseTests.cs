using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Probe.Oracle;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>The broad phase finding its own pairs — <c>FUN_180098880</c> into the pair creator (B369, D172).</summary>
/// <remarks>
/// **This is what `Watch` stood in for**: an object filed in the OV tree, and a second one near it, make a pair watcher which makes
/// the pair's mindists. Synthetic conformance (D38).
/// </remarks>
public sealed class IvpSimulationBroadPhaseTests
{
    private const float Half = 4f;

    private static readonly IIvpMaterial Material = new IvpReplayMaterial(0d, 0d, HasSecondFriction: false);

    [Test]
    public void Collide_ABodyWithALedge_IsFiledInTheTree()
    {
        IvpSimulation simulation = Simulation();
        IvpRigidBody core = Body((0d, 0d, 0d));
        simulation.Add(core);

        IvpCollisionObject collisionObject = simulation.Collide(core, Material);

        collisionObject.Node.ShouldNotBeNull();
        collisionObject.Node.Cell.ShouldNotBeNull("the node is in a cell of the tree");
        simulation.Collisions.BroadPhaseRuns.ShouldBe(1);
        core.Objects.ShouldBe([collisionObject]);
    }

    [Test]
    public void Collide_ABodyWithNoLedge_Refuses()
    {
        IvpSimulation simulation = Simulation();
        IvpRigidBody core = new() { Orientation = (0d, 0d, 0d, 1d), WorkingOrientation = (0d, 0d, 0d, 1d) };
        simulation.Add(core);

        Should.Throw<System.InvalidOperationException>(() => simulation.Collide(core, Material));
    }

    /// <remarks>**Two bodies within each other's range make a watcher**, which is the creator's own slot 5.</remarks>
    [Test]
    public void Collide_TwoBodiesWithinRange_MakeAPairWatcher()
    {
        IvpSimulation simulation = Simulation();
        IvpRigidBody first = Body((0d, 0d, 0d));
        IvpRigidBody second = Body((0d, 0d, 6d));
        simulation.Add(first);
        simulation.Add(second);

        IvpCollisionObject firstObject = simulation.Collide(first, Material);
        simulation.Collide(second, Material);

        firstObject.Node!.Watchers.Count.ShouldBe(1, "the creator made one watcher for the pair");
    }

    [Test]
    public void Collide_TwoBodiesFarApart_MakeNoWatcher()
    {
        IvpSimulation simulation = Simulation();
        IvpRigidBody first = Body((0d, 0d, 0d));
        IvpRigidBody second = Body((0d, 0d, 4000d));
        simulation.Add(first);
        simulation.Add(second);

        IvpCollisionObject firstObject = simulation.Collide(first, Material);
        simulation.Collide(second, Material);

        firstObject.Node!.Watchers.ShouldBeEmpty();
    }

    /// <remarks>
    /// **The watcher's refresh makes the pair's mindists** (<c>FUN_180096680</c>), which is what the pipeline then minimizes — so a
    /// pair nobody named reaches the collision path.
    /// </remarks>
    [Test]
    public void Advance_TwoBodiesTheBroadPhaseFound_AreMinimizedByThePipeline()
    {
        IvpSimulation simulation = Simulation();
        IvpRigidBody first = Body((0d, 0d, 0d));
        IvpRigidBody second = Body((0d, 0d, 8.4d));
        simulation.Add(first);
        simulation.Add(second);
        simulation.Collide(first, Material);
        simulation.Collide(second, Material);
        simulation.Start();

        simulation.Advance(0.05d);

        simulation.Mindists.ShouldBeGreaterThan(0, "the watcher's own refresh made the pair's mindists");
    }

    /// <remarks>
    /// **A new pair of two ordinary objects is made exact and examined at once** (<c>FUN_1800977f0</c>): linked, minimized, and
    /// handed to the scheduler asking for a far pair's removal. Two still bodies cannot close within a PSI, so it is filed with
    /// the hull managers before any PSI has run — the engine's own answer, not a failure to promote it.
    /// </remarks>
    [Test]
    public void Collide_APairTheBroadPhaseMade_IsExaminedAndFiledAtOnce()
    {
        IvpSimulation simulation = Simulation();
        IvpRigidBody first = Body((0d, 0d, 0d));
        IvpRigidBody second = Body((0d, 0d, 8.4d));
        simulation.Add(first);
        simulation.Add(second);
        simulation.Collide(first, Material);
        simulation.Collide(second, Material);

        simulation.Mindists.ShouldBeGreaterThan(0, "the control: the watcher made the pair's mindists");
        simulation.LastLength.ShouldBe(0.4f, 1e-4f, "minimized as it became exact");
        simulation.LastOutcome.ShouldBe(IvpScheduleOutcome.Filed);
        simulation.ExactPairs.ShouldBe(0);
    }

    /// <remarks>
    /// **The whole chain, with nothing named by hand**: the broad phase finds the pair, its watcher makes the mindists, the pipeline
    /// minimizes them, the scheduler queues the event, the drain fires it, and the collision links a contact into a friction system
    /// and pushes the bodies apart.
    /// </remarks>
    [Test]
    public void Advance_TwoBodiesDrivenTogether_CollideAndShareTheirMomentum()
    {
        IvpSimulation simulation = Simulation();
        IvpRigidBody moving = Body((0d, 0d, 0d));
        IvpRigidBody still = Body((0d, 0d, 9d));

        // Slow enough that the pair is filed, told, minimized and scheduled before the surfaces meet — a body crossing the whole gap
        // inside one PSI would pass through, which is what the hull filing exists to prevent.
        moving.Velocity = (0f, 0f, 6f);
        moving.PreviousVelocity = (0f, 0f, 6f);
        simulation.Add(moving);
        simulation.Add(still);
        IvpCollisionObject movingObject = simulation.Collide(moving, Material);
        simulation.Collide(still, Material);
        simulation.Start();

        for (double until = 0.02d; until <= 0.2d; until += 0.01d)
        {
            simulation.Advance(until);
        }

        simulation.Environment.Impacts.ShouldBe(
            1,
            $"{simulation.PairEvents} pair events, {simulation.Mindists} mindists, {simulation.ExactPairs} exact, last outcome " +
            $"{simulation.LastOutcome}, z {moving.Position.Z}, hull value {movingObject.Hull.Value}, next " +
            $"{movingObject.Hull.NextPsiValue}, minimum {movingObject.Hull.Synapses.Minimum}, last length {simulation.LastLength}, " +
            $"last hull pass {simulation.LastHullPass}");
        moving.FrictionInfo!.System.ShouldBeSameAs(still.FrictionInfo!.System, "one system holds the contact between two movers");
        moving.Unit.ShouldBeSameAs(still.Unit, "and they are simulated as one unit");
        still.Velocity.Z.ShouldBeGreaterThan(0f, "the push reached the body that was still");
        (moving.Velocity.Z + still.Velocity.Z).ShouldBe(6f, 1e-3f, "and what one gained the other lost");
    }

    private static IvpSimulation Simulation() => new(Environment(), (0f, 0f, 0f), () => 0f);

    private static IvpRigidBody Body((double X, double Y, double Z) at) =>
        new()
        {
            Position = at,
            Orientation = (0d, 0d, 0d, 1d),
            WorkingOrientation = (0d, 0d, 0d, 1d),
            CoreMatrix = IvpMatrix.FromRotation((0f, 0f, 0f, 1f), at),
            Radius = Half,
            InverseMass = 1f,
            // A solid cube's: I = m·s²/6 with side 8 and unit mass. A looser inertia lets a corner hit spend its push on spin.
            InverseInertia = (6f / 64f, 6f / 64f, 6f / 64f),
            Damping = 0f,
            RotationDamping = 0f,
            RestAnchorOrientation = (0f, 0f, 0f, 1f),
            SettleAnchorOrientation = (0f, 0f, 0f, 1f),
            RestAnchorPosition = ((float)at.X, (float)at.Y, (float)at.Z),
            SettleAnchorPosition = ((float)at.X, (float)at.Y, (float)at.Z),
            Ledges = IvpTestCube.Ledges(Half),
        };

    private static IvpImpactEnvironment Environment() =>
        new()
        {
            InverseStep = 66d,
            Step = 1d / 66d,
            Limits = new IvpAnomalyLimits(2000f, 6, 3600f, 250, 1f, 1e30f),
            Anomalies = new VphysicsAnomalyManager(new IvpImpactReplay.FixedAnswer(answer: false)),
            Materials = new IvpReplayMaterials(new IvpReplayMaterial(0d, 0d, HasSecondFriction: false), 0d, 0d),
            RestDelay = 5f,
            RestCheckCountdown = 15,
        };
}
