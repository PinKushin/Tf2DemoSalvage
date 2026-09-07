using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// A body colliding with the static world — the thing that stops a corpse (B58).
/// </summary>
/// <remarks>
/// **The observation this suite exists for is one the owner made by looking**: *"the ground didnt
/// collide witht he corpse so the corpse just fell through the ground"*. Every part of the physics
/// path had its own green suite at the time, and none of them could fail for that reason, because
/// none of them involved a world.
///
/// **Synthetic geometry rather than a map (D38).** The floor is written here, so the test knows the
/// height a body must stop at; a map would only let it compare two readings of the same `.bsp`.
/// </remarks>
public sealed class IvpWorldContactConformanceTests
{
    private const float Step = 1f / 66f;

    /// <remarks>
    /// **The control, and it has to come first.** Without it, "the body ends up at z = 0" is
    /// satisfied by a body that never moved at all — which is exactly what a broken contact search
    /// that froze everything would produce.
    /// </remarks>
    [Test]
    public void Simulate_WithNoWorld_FallsUnderGravity()
    {
        IvpEnvironment environment = new(Step);

        IvpRigidBody body = Body(100f);

        environment.Add(body);

        for (int step = 0; step < 66; step++)
        {
            environment.Simulate();
        }

        // One second of Source gravity is 800 units per second, so it is far below where it began.
        body.Position.Z.ShouldBeLessThan(-250d, "half of 800 times one second squared, less the 100 it started at");
    }

    /// <remarks>
    /// **The measurement.** Same body, same second, with a floor under it — it must be resting on
    /// the floor rather than through it. The tolerance is the solver's own contact slop, since a
    /// resting body is deliberately left a fraction inside rather than driven to exactly zero.
    /// </remarks>
    [Test]
    public void Simulate_WithAFloor_StopsOnIt()
    {
        IvpEnvironment environment = new(Step) { World = Floor() };

        IvpRigidBody body = Body(100f);

        environment.Add(body);

        for (int step = 0; step < 66; step++)
        {
            environment.Simulate();
        }

        // The hull is a 2-unit cube about the body's centre, so a resting centre sits a unit up.
        body.Position.Z.ShouldBeGreaterThan(0d);
        body.Position.Z.ShouldBeLessThan(4d);
    }

    /// <remarks>
    /// **A body already at rest must not be pushed UP**, which is the failure a penetration bias
    /// produces when it converts depth into velocity with no slop: the corpse climbs, slowly and
    /// convincingly, and looks like buoyancy.
    /// </remarks>
    [Test]
    public void Simulate_WithABodyAtRest_DoesNotLiftIt()
    {
        IvpEnvironment environment = new(Step) { World = Floor() };

        IvpRigidBody body = Body(1f);

        environment.Add(body);

        for (int step = 0; step < 132; step++)
        {
            environment.Simulate();
        }

        body.Position.Z.ShouldBeLessThan(4d);
    }

    /// <remarks>
    /// **An immovable body is not collided**, matching the island driver, which does not integrate
    /// one either. Without this a piece of static geometry given a hull would push itself out of
    /// the world it IS.
    /// </remarks>
    [Test]
    public void Find_ForAnImmovableBody_ReportsNoContacts()
    {
        IvpRigidBody body = Body(-100f);

        body.Immovable = true;

        List<IvpContact> contacts = [];

        IvpContact.Find(body, Floor(), contacts);

        contacts.ShouldBeEmpty();

        // The control: the same body, movable, is deep inside the floor and must report contacts.
        body.Immovable = false;

        IvpContact.Find(body, Floor(), contacts);

        contacts.ShouldNotBeEmpty();
    }

    /// <remarks>
    /// **The shallowest face wins, and this is the input that tells the two readings apart.** A
    /// point just inside the top of a floor block is barely below its top face and a long way above
    /// its bottom one; pushing it out by the DEEPEST face would drive it down through the block.
    /// </remarks>
    [Test]
    public void Penetration_JustInsideTheTopFace_PushesUp()
    {
        (Vector3 Normal, float Depth)? hit = Floor().Penetration(new Vector3(0f, 0f, -1f));

        hit.ShouldNotBeNull();
        hit.Value.Normal.Z.ShouldBe(1f, 1e-4f);
        hit.Value.Depth.ShouldBe(1f, 1e-3f);
    }

    /// <summary>A body with a two-unit cube for a hull, at a height.</summary>
    private static IvpRigidBody Body(float height) =>
        new()
        {
            Position = (0d, 0d, height),
            InverseMass = 1f,
            Inertia = (10f, 10f, 10f),
            InverseInertia = (0.1f, 0.1f, 0.1f),
            Hull =
            [
                (-1f, -1f, -1f), (1f, -1f, -1f), (1f, 1f, -1f), (-1f, 1f, -1f),
                (-1f, -1f, 1f), (1f, -1f, 1f), (1f, 1f, 1f), (-1f, 1f, 1f),
            ],
        };

    /// <summary>A slab whose top face is z = 0, given in IVP metres as a real hull would be.</summary>
    /// <remarks>
    /// **Written in metres and converted by the world, not handed over in Source units**, because
    /// the conversion at that seam is one of the things this suite is here to keep honest: a hull
    /// that arrived already converted would pass every test while the real one was 39 times too
    /// small.
    /// </remarks>
    private static IvpWorldCollision Floor()
    {
        const float Metre = 0.0254f;

        float wide = 1000f * Metre;
        float deep = 100f * Metre;

        List<Vector3> points =
        [
            new(-wide, -wide, -deep), new(wide, -wide, -deep),
            new(wide, wide, -deep), new(-wide, wide, -deep),
            new(-wide, -wide, 0f), new(wide, -wide, 0f),
            new(wide, wide, 0f), new(-wide, wide, 0f),
        ];

        // Wound so every normal points OUT of the slab, which is what makes "behind every plane"
        // mean "inside".
        List<(int A, int B, int C)> triangles =
        [
            (4, 5, 6), (4, 6, 7),       // top,    +Z
            (0, 2, 1), (0, 3, 2),       // bottom, -Z
            (0, 1, 5), (0, 5, 4),       // -Y
            (2, 3, 7), (2, 7, 6),       // +Y
            (1, 2, 6), (1, 6, 5),       // +X
            (3, 0, 4), (3, 4, 7),       // -X
        ];

        IvpWorldCollision world = new();

        world.Add(points, triangles, new Vector3(0f, 0f, -deep / 2f), wide * 2f);

        return world;
    }
}
