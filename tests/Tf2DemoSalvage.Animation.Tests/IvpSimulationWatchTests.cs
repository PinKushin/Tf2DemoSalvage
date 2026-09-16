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
        (IvpSimulation _, IvpMindist mindist, IvpRigidBody first, _) = Watched();

        (IvpLedgeSide side, _) = IvpSimulation.SidesOf(mindist);

        side.CorePosition.ShouldBe(first.Position);
        side.Points.Count.ShouldBe(8, "the cube's own points, in the object's frame");
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

    private static (IvpSimulation, IvpMindist, IvpRigidBody, IvpRigidBody) Watched(double apart = 40d)
    {
        IvpSimulation simulation = new(Environment(), (0f, 0f, 0f), () => 0f);

        // Both bodies sit OFF the origin, so a side built at the origin instead of at its own core is a different side.
        IvpRigidBody first = Body((7d, -3d, 0d));
        IvpRigidBody second = Body((7d, -3d, apart));
        simulation.Add(first);
        simulation.Add(second);

        IvpCollisionObject firstObject = new() { Core = first };
        IvpCollisionObject secondObject = new() { Core = second };
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
            RestAnchorOrientation = (0f, 0f, 0f, 1f),
            SettleAnchorOrientation = (0f, 0f, 0f, 1f),
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
