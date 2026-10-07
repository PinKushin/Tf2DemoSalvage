using System.Numerics;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// <c>CCollisionProperty::WorldSpaceCenter</c> (`collisionproperty.h:391-414`): the OBB's centre carried to the world —
/// what a rope end on an ordinary entity hangs from (`CalculateEndPointAttachment`, `c_rope.cpp:1961`, B478).
/// </summary>
public sealed class WorldSpaceCenterConformanceTests
{
    private static readonly ScenePose Turned = new() { X = 100f, Y = 200f, Z = 300f, Yaw = 90f };

    /// <remarks>
    /// **An OBB in entity space turns with the entity** — <c>SOLID_VPHYSICS</c> (6) here: the centre ( 10, 0, 5 ) under a
    /// 90° yaw is ( 0, 10, 5 ) from the origin (`VectorTransform( in, CollisionToWorldTransform() )`).
    /// </remarks>
    [Test]
    public void Of_AnEntitySpaceBoxTurned90Degrees_TurnsItsCentre()
    {
        SceneCollision box = new(6, 0, 0) { Mins = (0f, -4f, 0f), Maxs = (20f, 4f, 10f) };

        Vector3 centre = WorldSpaceCenter.Of(Turned, box);

        Vector3.Distance(centre, new Vector3(100f, 210f, 305f)).ShouldBeLessThan(0.001f);
    }

    /// <remarks>
    /// **A world-aligned box only adds** — <c>SOLID_BBOX</c>, <c>SOLID_NONE</c> or <c>FSOLID_FORCE_WORLD_ALIGNED</c>
    /// (`IsBoundsDefinedInEntitySpace`, `collisionproperty.h:340-344`) — and so does no collision at all, whose centre is
    /// the origin.
    /// </remarks>
    [TestCase(2, 0)]
    [TestCase(0, 0)]
    [TestCase(6, 64)]
    public void Of_AWorldAlignedBox_AddsItsCentre(int solid, int flags)
    {
        SceneCollision box = new(solid, flags, 0) { Mins = (0f, -4f, 0f), Maxs = (20f, 4f, 10f) };

        WorldSpaceCenter.Of(Turned, box).ShouldBe(new Vector3(110f, 200f, 305f));
        WorldSpaceCenter.Of(Turned, null).ShouldBe(new Vector3(100f, 200f, 300f));
    }
}
