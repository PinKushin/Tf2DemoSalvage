using System;
using System.Numerics;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>A corpse dropped on a floor, stepped tick by tick, its every pose hashed to the bit (finding 83, job 2).</summary>
/// <remarks>
/// **The differential for the allocation work under `CorpsePhysics.Advance`**: reusing a buffer or a built side must leave
/// every pose bit-identical, and this pins the bits as they were before any of it (captured at `1cf92804`). It is not a
/// claim about what the engine computes — the conformance suites hold that — only that a change meant to move no number
/// moved none. *The control*: the corpse lands, so the contact, friction and time-of-impact paths the allocations live in
/// all ran.
/// </remarks>
public sealed class IvpCorpseStepDifferentialTests
{
    private const int Ticks = 200;

    [Test]
    public void Simulate_ACorpseDroppedOnAFloor_EveryPoseHashesAsBefore()
    {
        (IvpRagdollWorld world, IvpRagdoll ragdoll) = Dropped();

        ulong hash = 14695981039346656037UL;
        long allocatedFrom = 0;

        for (int tick = 0; tick < Ticks; tick++)
        {
            if (tick == 20)
            {
                allocatedFrom = GC.GetAllocatedBytesForCurrentThread();
            }

            world.Simulate(1f / 66f);

            foreach ((Vector3 position, Quaternion orientation) in ragdoll.State())
            {
                hash = Mix(hash, position.X, position.Y, position.Z, orientation.X, orientation.Y, orientation.Z, orientation.W);
            }
        }

        long physics = GC.GetAllocatedBytesForCurrentThread() - allocatedFrom - NonPhysics(ragdoll, Ticks - 20);

        // **A ceiling on the physics' OWN allocation**, the loop's bytes less a control loop of the same 180 ticks' hashing and
        // State() reads without the step (the runtime's and the hash's, which the physics does not own). Measured:
        // 819,888-821,832 bytes after a side was kept per refresh, varying with test order; about 950,000 before (973,928 for the
        // whole loop). The ceiling sits ~3.5% above, below the old figure, so putting the per-look side back reddens it and a few
        // KB of runtime drift does not.
        physics.ShouldBeLessThanOrEqualTo(850_000L, "the step's own allocation, ticks 20-199");

        // The hook a corpse's entity is handed every step is one delegate, not one per step (52 MB per 20 s on f12).
        ragdoll.PoseHook.ShouldBeSameAs(ragdoll.PoseHook);
        world.Simulation.Environment.Impacts.ShouldBeGreaterThan(0, $"the control: the corpse struck the floor; root at {ragdoll.State()[0].Position}");
        hash.ShouldBe(1606571577950495611UL,"the poses' bits as they were before the allocation work");
    }

    /// <summary>The bytes the loop's non-physics work allocates over <paramref name="ticks"/> ticks: State() and the hash.</summary>
    private static long NonPhysics(IvpRagdoll ragdoll, int ticks)
    {
        ulong hash = 0;
        long from = GC.GetAllocatedBytesForCurrentThread();

        for (int tick = 0; tick < ticks; tick++)
        {
            foreach ((Vector3 position, Quaternion orientation) in ragdoll.State())
            {
                hash = Mix(hash, position.X, position.Y, position.Z, orientation.X, orientation.Y, orientation.Z, orientation.W);
            }
        }

        long spent = GC.GetAllocatedBytesForCurrentThread() - from;
        GC.KeepAlive(hash);
        return spent;
    }

    private static (IvpRagdollWorld World, IvpRagdoll Ragdoll) Dropped()
    {
        IvpRagdollWorld world = new(
            1f / 66f,
            new Vector3(0f, 0f, -800f),
            new VphysicsSurfaceProps(
            [
                new VphysicsSurface("default", new SurfacePhysicsParams(0.8f, 0.25f, 0f, 0f, 0f), hasSecondFriction: false),
            ]));

        world.AddStatic(
            IvpTestSurface.Boxes((Vector3.Zero, new Vector3(20f, 0.5f, 20f))),
            // The box is in IVP's frame, whose second axis is Source's up: a 40-metre slab a metre thick.
            new Vector3(0f, 0f, -80f),
            world.Surfaces.ObjectMaterial("default")!,
            contents: 0x1);

        IvpRagdoll ragdoll = IvpRagdoll.Create(
            world,
            Body(),
            [(Vector3.Zero, Quaternion.Identity), (new Vector3(0f, 6f, 10f), Quaternion.Identity)]);
        ragdoll.Kill(new Vector3(300f, 0f, -200f), forceBone: 1);

        return (world, ragdoll);
    }

    /// <remarks>
    /// **A frozen object keeps no side** (review of finding 83's reuse): `Freeze` drops the object's cache, and the side built on
    /// that cache must go with it, or every settled corpse pins a dead cache and a side for its life. *The control*: before
    /// the freeze the landed root did hold a side.
    /// </remarks>
    [Test]
    public void Freeze_ALandedCorpsesUnit_LeavesNoSideOnItsObjects()
    {
        (IvpRagdollWorld world, IvpRagdoll ragdoll) = Dropped();
        for (int tick = 0; tick < Ticks; tick++)
        {
            world.Simulate(1f / 66f);
        }

        IvpRigidBody root = ragdoll.Bodies[0];
        root.Objects.ShouldContain(made => made.LastSide != null, "the control: the landed corpse's root built a side");

        root.Unit!.Freeze(world.Simulation.Environment, world.Simulation.Now);

        root.Objects.ShouldAllBe(made => made.Cache == null && made.LastSide == null);
    }

    /// <remarks>
    /// **A side on a ledge already compacted allocates the side and nothing else** — the closure for the miss was made on every
    /// call (281 MB per 20 s on f12). *The control* is a bare `new IvpLedgeSide` of the same pieces, measured the same way.
    /// </remarks>
    [Test]
    public void FromLedge_ALedgeAlreadyCompacted_AllocatesOnlyTheSide()
    {
        PhysicsLedge ledge = Tf2DemoSalvage.Probe.Oracle.IvpTestCube.Ledges(4f)[0];
        IvpMatrix current = IvpMatrix.FromRotation((0f, 0f, 0f, 1f), (0d, 0d, 0d));
        IvpLedgeSide first = IvpLedgeSide.FromLedge(ledge, current, (0d, 0d, 0d));
        _ = new IvpLedgeSide(first.Points, first.Topology, current, (0d, 0d, 0d));

        long before = GC.GetAllocatedBytesForCurrentThread();
        _ = new IvpLedgeSide(first.Points, first.Topology, current, (0d, 0d, 0d));
        long bare = GC.GetAllocatedBytesForCurrentThread() - before;

        before = GC.GetAllocatedBytesForCurrentThread();
        _ = IvpLedgeSide.FromLedge(ledge, current, (0d, 0d, 0d));
        long made = GC.GetAllocatedBytesForCurrentThread() - before;

        made.ShouldBe(bare);
    }

    private static ulong Mix(ulong hash, params float[] values)
    {
        foreach (float value in values)
        {
            hash = (hash ^ (uint)BitConverter.SingleToInt32Bits(value)) * 1099511628211UL;
        }

        return hash;
    }

    /// <summary>Two elements with surfaces, joined, the shape `IvpRagdollWorldTests` builds its corpses from.</summary>
    private static RagdollBody Body()
    {
        byte[] surface = IvpTestSurface.Bytes((Vector3.Zero, new Vector3(0.1f)));
        byte[] file =
        [
            .. BitConverter.GetBytes(16),
            .. BitConverter.GetBytes(0x59485056),
            .. BitConverter.GetBytes(2),
            .. BitConverter.GetBytes(0),
            .. BitConverter.GetBytes(surface.Length), .. surface,
            .. BitConverter.GetBytes(surface.Length), .. surface,
            .. System.Text.Encoding.ASCII.GetBytes("""
                solid {
                  "index" "0"
                  "name" "bip_root"
                  "mass" "10.0"
                  "surfaceprop" "default"
                }
                solid {
                  "index" "1"
                  "name" "bip_child"
                  "parent" "bip_root"
                  "mass" "2.0"
                  "surfaceprop" "default"
                }
                ragdollconstraint {
                  "parent" "0"
                  "child" "1"
                  "xmin" "-30" "xmax" "30" "ymin" "-30" "ymax" "30" "zmin" "-30" "zmax" "30"
                }
                """),
        ];

        return RagdollBody.Build(PhysicsModel.Read(file), RagdollSkeletons.Straight()).ShouldNotBeNull();
    }
}
