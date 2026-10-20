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

        ulong hash = 14695981039346656037UL;

        for (int tick = 0; tick < Ticks; tick++)
        {
            world.Simulate(1f / 66f);

            foreach ((Vector3 position, Quaternion orientation) in ragdoll.State())
            {
                hash = Mix(hash, position.X, position.Y, position.Z, orientation.X, orientation.Y, orientation.Z, orientation.W);
            }
        }

        world.Simulation.Environment.Impacts.ShouldBeGreaterThan(0, $"the control: the corpse struck the floor; root at {ragdoll.State()[0].Position}");
        hash.ShouldBe(1606571577950495611UL,"the poses' bits as they were before the allocation work");
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
