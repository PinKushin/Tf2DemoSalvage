using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;
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

    /// <summary>The 40 × 40 × 1 slab's bounding radius, √(40² + 40² + 1²). *Given the cube's 4 it was a sphere smaller than its face*,
    /// and a cube tipping toward its edge left the range and had its pair deleted while resting on it.</summary>
    private static readonly float SlabRadius = System.MathF.Sqrt(3201f);

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
        (IvpSimulation simulation, IvpRigidBody moving, IvpRigidBody still, IvpCollisionObject movingObject) = DrivenTogether(until: 0.2d);

        simulation.Environment.Impacts.ShouldBeGreaterThan(
            0,
            $"{simulation.PairEvents} pair events, {simulation.Mindists} mindists, {simulation.ExactPairs} exact, last outcome " +
            $"{simulation.LastOutcome}, z {moving.Position.Z}, hull value {movingObject.Hull.Value}, next " +
            $"{movingObject.Hull.NextPsiValue}, minimum {movingObject.Hull.Synapses.Minimum}, last length {simulation.LastLength}, " +
            $"last hull pass {simulation.LastHullPass}");
        moving.FrictionInfo!.System.ShouldBeSameAs(still.FrictionInfo!.System, "one system holds the contact between two movers");
        moving.Unit.ShouldBeSameAs(still.Unit, "and they are simulated as one unit");
        still.Velocity.Z.ShouldBeGreaterThan(0f, "the push reached the body that was still");
        (moving.Velocity.Z + still.Velocity.Z).ShouldBe(6f, 1e-3f, "and what one gained the other lost");
    }

    /// <remarks>
    /// **After the impact the contact holds them apart.** A cube hit on a corner turns, so the centers may come a little inside 8,
    /// but not through: the contact's record has to measure the same penetration the pair's minimize does.
    /// </remarks>
    [Test]
    public void Advance_TwoBodiesDrivenTogether_DoNotPassThroughEachOther()
    {
        (_, IvpRigidBody moving, IvpRigidBody still, _) = DrivenTogether(until: 1d);

        IvpContactPoint? contact = moving.FrictionInfo?.System.FirstContact;
        (still.Position.Z - moving.Position.Z).ShouldBeGreaterThan(
            7d,
            $"moving at {moving.Position.Z}, still at {still.Position.Z}; contact {contact?.First.Kind} {contact?.First.Feature} of " +
            $"{(ReferenceEquals(contact?.FirstObject.Core, moving) ? "moving" : "still")} against {contact?.Second.Kind} " +
            $"{contact?.Second.Feature}, gap {contact?.Gap}, spin {moving.AngularVelocity}/{still.AngularVelocity}");
    }

    /// <remarks>
    /// **The world is a static object** — an immovable core in no unit, its surface filed in the broad phase like any other — and a
    /// body dropped on it comes to rest on its face.
    /// </remarks>
    [Test]
    public void Advance_ABodyDroppedOnAStaticSlab_ComesToRestOnIt()
    {
        IvpSimulation simulation = new(Environment(), (0f, 0f, -10f), () => 0f);
        IvpRigidBody body = Body((1d, 2d, 10d));
        IvpRigidBody slab = Body((0d, 0d, 0d));
        slab.Immovable = true;
        slab.InverseMass = 0f;
        slab.InverseInertia = (0f, 0f, 0f);
        slab.Ledges = IvpTestCube.Box(40f, 40f, 1f);
        slab.Radius = SlabRadius;
        simulation.Add(body);
        simulation.Collide(body, Material);
        simulation.Collide(slab, Material);
        simulation.Start();

        for (double at = 0.02d; at <= 3d; at += 0.01d)
        {
            simulation.Advance(at);
        }

        body.Position.Z.ShouldBeGreaterThan(4.5d, $"the slab's top is at 1 and the cube's half is 4; impacts {simulation.Environment.Impacts}");
        body.Position.Z.ShouldBeLessThan(5.5d, "and it came down to it");
    }

    /// <remarks>
    /// **A static surface of many ledges measures each pair on its own ledge.** A map's collide is one surface over many convexes;
    /// the pair creation names which ledge each synapse stands on (<c>FUN_1800975d0</c>), and a side stood on the object's first
    /// ledge would measure a cube over the higher step against the lower one.
    /// </remarks>
    [Test]
    public void Advance_ABodyDroppedOnTheHigherOfTwoLedges_RestsOnThatLedge()
    {
        IvpSimulation simulation = new(Environment(), (0f, 0f, -10f), () => 0f);
        IvpRigidBody body = Body((20d, 1d, 14d));
        IvpRigidBody world = Body((0d, 0d, 0d));
        world.Immovable = true;
        world.InverseMass = 0f;
        world.InverseInertia = (0f, 0f, 0f);
        PhysicsLedgeTree surface = IvpTestSurface.Boxes(
            (new Vector3(-20f, 0f, 0f), new Vector3(20f, 40f, 1f)),
            (new Vector3(20f, 0f, 2f), new Vector3(20f, 40f, 1f)));

        // The core's own ledge list names the LOWER ledge, so a side that ignored the synapse's ledge would measure the wrong step.
        PhysicsLedge lower = surface.Root.Left?.Ledge ?? throw new System.InvalidOperationException("The surface has no lower ledge.");
        world.Ledges = [lower];
        simulation.Add(body);
        simulation.Collide(body, Material);
        simulation.Collide(world, surface, Material);
        simulation.Start();

        for (double at = 0.02d; at <= 3d; at += 0.01d)
        {
            simulation.Advance(at);
        }

        body.Position.Z.ShouldBe(7d, 0.5d, "the higher ledge's top is at 3 and the cube's half is 4");
    }

    /// <remarks>
    /// **The pair events and the queue a deleted pair leaves are ONE queue**, the time manager's: a pair the scheduler queues is on
    /// <see cref="IvpCollisionEnvironment.EventQueue"/>, which <c>FUN_180098dd0</c> takes it out of. *With two queues, a queued pair
    /// deleted by a refile named a slot in the other one, and a ragdoll's first contact on `cp_process_f12` threw.*
    /// </remarks>
    [Test]
    public void Advance_APairTheSchedulerQueues_IsOnTheCollisionEnvironmentsEventQueue()
    {
        IvpSimulation simulation = Simulation();
        IvpRigidBody moving = Body((2d, 1d, 0d));
        IvpRigidBody still = Body((0d, 0d, 9d));
        moving.Velocity = (0f, 0f, 6f);
        moving.PreviousVelocity = (0f, 0f, 6f);
        simulation.Add(moving);
        simulation.Add(still);
        simulation.Collide(moving, Material);
        simulation.Collide(still, Material);
        simulation.Start();

        bool queued = false;

        for (double at = 0.02d; at <= 1d && !queued; at += 0.01d)
        {
            simulation.Advance(at);
            queued = simulation.LastOutcome == IvpScheduleOutcome.Queued && simulation.Collisions.EventQueue.Count > 0;
        }

        queued.ShouldBeTrue("a queued pair is on the environment's own queue");
    }

    /// <remarks>
    /// **A displacement is a static object over the virtual-mesh manager** (<c>PhysCreateVirtualTerrain</c>): its hull ledge is the
    /// root, every face virtual, opened into the triangles near the body. A 2000-inch power-2 basin — the border ring raised 100
    /// inches, the middle flat at Source z 0 — puts IVP's y 0 plane under x and z 12.7..38.1; a cube of half 4 dropped along IVP +Y,
    /// Source −Z, comes to rest in the middle with its centre at y −4.
    /// </remarks>
    [Test]
    public void Advance_ABodyDroppedOnVirtualTerrain_ComesToRestOnIt()
    {
        IvpSimulation simulation = new(Environment(friction: 0.8d, elasticity: 0.25d), (0f, 10f, 0f), () => 0f);
        IvpRigidBody body = Body((25.4d, -10d, 25.4d));
        IvpRigidBody ground = Body((0d, 0d, 0d));
        ground.Immovable = true;
        ground.InverseMass = 0f;
        ground.InverseInertia = (0f, 0f, 0f);

        (Vector3, float)[] field = new (Vector3, float)[25];

        for (int index = 0; index < 25; index++)
        {
            if (index / 5 is 0 or 4 || index % 5 is 0 or 4)
            {
                field[index] = (Vector3.UnitZ, 100f);
            }
        }

        DisplacementCollisionTree tree = DisplacementCollisionTree.Build(
            [Vector3.Zero, new Vector3(0f, 2000f, 0f), new Vector3(2000f, 2000f, 0f), new Vector3(2000f, 0f, 0f)], 2, field);

        // The convex hull: the raised corners 0, 4, 24, 20 over the flat middle's corners 6, 8, 18, 16, each face wound outward.
        byte[] hull = HullBlob(
            tree.Vertices,
            [
                (0, 4, 24), (0, 24, 20),
                (6, 16, 18), (6, 18, 8),
                (0, 6, 8), (0, 8, 4),
                (4, 8, 18), (4, 18, 24),
                (24, 18, 16), (24, 16, 20),
                (20, 16, 6), (20, 6, 0),
            ]);

        simulation.Add(body);
        simulation.Collide(body, Material);
        IvpVirtualMeshSurfaceManager manager = new(PhysicsVirtualMesh.Build(tree.Vertices, tree.Triangles, hull), tree);
        simulation.Collide(ground, manager, Material);
        simulation.Start();

        for (double at = 0.02d; at <= 3d; at += 0.01d)
        {
            simulation.Advance(at);
        }

        body.Position.Y.ShouldBe(
            -4d,
            0.5d,
            $"the middle is at y 0 and the cube's half is 4; impacts {simulation.Environment.Impacts}, last hull pass {simulation.LastHullPass}");
    }

    /// <remarks>
    /// **The work the normal pushes did is paid back as damping** (<c>FUN_180088ae0</c> banks it per pair, <c>FUN_180086b40</c>
    /// takes it out of the pair's relative motion). A cube resting on two contacts with the bank unported crept up at 0.012 a
    /// second on a constant push.
    /// </remarks>
    [Test]
    public void Advance_ABodyRestingOnAStaticSlab_DoesNotCreep()
    {
        IvpSimulation simulation = new(Environment(), (0f, 0f, -10f), () => 0f);
        IvpRigidBody body = Body((1d, 2d, 10d));
        IvpRigidBody slab = Body((0d, 0d, 0d));
        slab.Immovable = true;
        slab.InverseMass = 0f;
        slab.InverseInertia = (0f, 0f, 0f);
        slab.Ledges = IvpTestCube.Box(40f, 40f, 1f);
        slab.Radius = SlabRadius;
        simulation.Add(body);
        simulation.Collide(body, Material);
        simulation.Collide(slab, Material);
        simulation.Start();

        // *Eight seconds, not three*: with the slab's inverse diameter read (the edge target's factor), the cube is left about a
        // centimetre inside the slab and pushed out at 0.0064 a second, still climbing at three and at rest by eight. The claim is
        // that it stops, not how soon.
        for (double at = 0.02d; at <= 8d; at += 0.01d)
        {
            simulation.Advance(at);
        }

        System.Math.Abs(body.Velocity.Z).ShouldBeLessThan(1e-3f, $"at {body.Position.Z}");
    }

    /// <summary>
    /// A <c>LUMP_PHYSDISP</c> blob of one hull over a displacement's vertices, every face and edge virtual: edges numbered as first
    /// met, each triangle's pierce the face turned most against it.
    /// </summary>
    private static byte[] HullBlob(IReadOnlyList<Vector3> vertices, (int A, int B, int C)[] triangles)
    {
        List<(int From, int To)> edges = [];
        List<byte> body = [];

        foreach ((int a, int b, int c) in triangles)
        {
            foreach ((int from, int to) in ((int, int)[])[(a, b), (b, c), (c, a)])
            {
                int id = edges.FindIndex(edge => edge == (to, from));

                if (id < 0)
                {
                    id = edges.Count;
                    edges.Add((from, to));
                }

                body.Add((byte)id);
            }

            Vector3 normal = Normal(vertices, (a, b, c));
            int pierce = Enumerable.Range(0, triangles.Length).MinBy(other => Vector3.Dot(Normal(vertices, triangles[other]), normal));
            body.Add((byte)pierce);
        }

        foreach ((int from, int to) in edges)
        {
            body.Add((byte)from);
            body.Add((byte)to);
        }

        return [1, 0, 0, 0, (byte)triangles.Length, (byte)triangles.Length, (byte)edges.Count, (byte)edges.Count, 0, .. body];
    }

    private static Vector3 Normal(IReadOnlyList<Vector3> vertices, (int A, int B, int C) triangle) =>
        Vector3.Normalize(Vector3.Cross(vertices[triangle.B] - vertices[triangle.A], vertices[triangle.C] - vertices[triangle.A]));

    /// <summary>Two cubes of half four, faces one apart, the first driven at the second at 6 — advanced in 0.01 slices.</summary>
    private static (IvpSimulation, IvpRigidBody Moving, IvpRigidBody Still, IvpCollisionObject MovingObject) DrivenTogether(double until)
    {
        IvpSimulation simulation = Simulation();

        // **Offset, so a corner of one lands inside the other's face.** Exactly aligned cubes meet corner to corner — a point–point
        // contact at zero distance, whose normal has no direction — and the impact spends itself on spin.
        IvpRigidBody moving = Body((2d, 1d, 0d));
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

        for (double at = 0.02d; at <= until; at += 0.01d)
        {
            simulation.Advance(at);
        }

        return (simulation, moving, still, movingObject);
    }

    private static IvpSimulation Simulation() => new(Environment(), (0f, 0f, 0f), () => 0f);

    private static IvpRigidBody Body((double X, double Y, double Z) at) =>
        new()
        {
            Position = at,
            Orientation = (0d, 0d, 0d, 1d),
            WorkingOrientation = (0d, 0d, 0d, 1d),
            CoreMatrix = IvpMatrix.FromRotation((0f, 0f, 0f, 1f), at),
            // The cube's bounding radius, as `FUN_180078b90` takes one from a surface — its corner.
            Radius = Half * System.MathF.Sqrt(3f),
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

    private static IvpImpactEnvironment Environment() => Environment(friction: 0d, elasticity: 0d);

    /// <remarks>
    /// Valve's own "default" surface (scripts/surfaceproperties.txt, confirmed live against the shipped vphysics.dll
    /// by `vphysics-materials parse`) is friction 0.8, elasticity 0.25 - not every collision's frictionless, perfectly
    /// inelastic pair here. Kept as an explicit override rather than <see cref="Environment()"/>'s own default: the
    /// other tests in this file were authored and tuned against 0/0, and forcing the real values onto all of them at
    /// once surfaces a second, separate divergence (a friction/damping instability, B369) that needs its own fix
    /// rather than riding along with this one.
    /// </remarks>
    private static IvpImpactEnvironment Environment(double friction, double elasticity) =>
        new()
        {
            InverseStep = 66d,
            Step = 1d / 66d,
            Limits = new IvpAnomalyLimits(2000f, 6, 3600f, 250, 1f, 1e30f),
            Anomalies = new VphysicsAnomalyManager(new IvpImpactReplay.FixedAnswer(answer: false)),
            Materials = new IvpReplayMaterials(new IvpReplayMaterial(0d, 0d, HasSecondFriction: false), friction, elasticity),
            RestDelay = 5f,
            RestCheckCountdown = 15,
        };
}
