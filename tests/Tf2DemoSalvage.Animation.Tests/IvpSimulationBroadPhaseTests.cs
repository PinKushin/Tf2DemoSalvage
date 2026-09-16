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

    [Test]
    public void Collide_ABodyWithALedge_IsFiledInTheTree()
    {
        IvpSimulation simulation = Simulation();
        IvpRigidBody core = Body((0d, 0d, 0d));
        simulation.Add(core);

        IvpCollisionObject collisionObject = simulation.Collide(core);

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

        Should.Throw<System.InvalidOperationException>(() => simulation.Collide(core));
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

        IvpCollisionObject firstObject = simulation.Collide(first);
        simulation.Collide(second);

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

        IvpCollisionObject firstObject = simulation.Collide(first);
        simulation.Collide(second);

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
        simulation.Collide(first);
        simulation.Collide(second);
        simulation.Start();

        simulation.Advance(0.05d);

        simulation.Mindists.ShouldBeGreaterThan(0, "the watcher's own refresh made the pair's mindists");
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
            InverseInertia = (1f, 1f, 1f),
            Damping = 0f,
            RotationDamping = 0f,
            RestAnchorOrientation = (0f, 0f, 0f, 1f),
            SettleAnchorOrientation = (0f, 0f, 0f, 1f),
            RestAnchorPosition = ((float)at.X, (float)at.Y, (float)at.Z),
            SettleAnchorPosition = ((float)at.X, (float)at.Y, (float)at.Z),
            Ledges = Cube(),
        };

    private static List<PhysicsLedge> Cube()
    {
        List<Vector3> points =
        [
            new(-Half, -Half, -Half), new(Half, -Half, -Half),
            new(Half, Half, -Half), new(-Half, Half, -Half),
            new(-Half, -Half, Half), new(Half, -Half, Half),
            new(Half, Half, Half), new(-Half, Half, Half),
        ];

        List<(int A, int B, int C)> triangles =
        [
            (4, 5, 6), (4, 6, 7), (0, 2, 1), (0, 3, 2),
            (0, 1, 5), (0, 5, 4), (2, 3, 7), (2, 7, 6),
            (1, 2, 6), (1, 6, 5), (3, 0, 4), (3, 4, 7),
        ];

        return
        [
            new PhysicsLedge(
                points,
                triangles,
                new (int, int, int)[triangles.Count],
                new int[triangles.Count],
                new int[triangles.Count],
                Vector3.Zero,
                Half * 2f),
        ];
    }

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
