using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// A line against a solid static prop — engine.dll `ClipRayToCollideable` (`0x18018f510`): a `SOLID_VPHYSICS` collideable is
/// traced against its vcollide's first solid, placed at the prop's origin and angles, and a studio model's hit takes the
/// MDL's own `$surfaceprop`.
/// </summary>
/// <remarks>
/// The box is one ledge whose IVP half-extents are 1, 0.1 and 0.25 metres. IVP's axes are Source's (x, −z, y), so in Source
/// inches it runs ±39.37 along x, ±9.8425 along y and ±3.937 along z.
/// </remarks>
public sealed class StaticPropCollisionConformanceTests
{
    private static readonly (float X, float Y, float Z) From = (0f, 0f, 0f);
    private static readonly (float X, float Y, float Z) To = (200f, 0f, 0f);

    [Test]
    public void Trace_ALineIntoAnUnturnedProp_StopsOnItsNearFaceWithThePropsSurface()
    {
        StaticPropCollision props = Props(yaw: 0f);

        StaticPropHit hit = props.Trace(From, To).ShouldNotBeNull();

        // Enters at x = 100 − 39.37.
        hit.Fraction.ShouldBe((100f - 39.370079f) / 200f, 1e-5f);
        hit.Normal.X.ShouldBe(-1f, 1e-5f);
        hit.SurfaceProp.ShouldBe(7);
    }

    [Test]
    public void Trace_ALineIntoAPropYawedNinety_MeetsItsNarrowSide()
    {
        // Yawed 90°, the prop's local y lies along world x, so the near face is 9.8425 short of its origin.
        Props(yaw: 90f).Trace(From, To).ShouldNotBeNull().Fraction.ShouldBe((100f - 9.8425197f) / 200f, 1e-4f);
    }

    [Test]
    public void Trace_ALinePassingAbove_MissesIt() =>
        Props(yaw: 0f).Trace((0f, 0f, 10f), (200f, 0f, 10f)).ShouldBeNull();

    [Test]
    public void Trace_ANonSolidProp_IsNotThere() =>
        StaticPropCollision.From([Prop(0f) with { Solid = 0 }], _ => Collide(), _ => 7).Trace(From, To).ShouldBeNull();

    private static StaticPropCollision Props(float yaw) => StaticPropCollision.From([Prop(yaw)], _ => Collide(), _ => 7);

    private static BspStaticProp Prop(float yaw) =>
        new("models/props/box.mdl", 100f, 0f, 0f, 0f, yaw, 0f, 1f, Solid: 6);

    private static IvpStaticPropCollide Collide()
    {
        List<Vector3> points = [];

        foreach (float x in new[] { -1f, 1f })
        {
            foreach (float y in new[] { -0.1f, 0.1f })
            {
                foreach (float z in new[] { -0.25f, 0.25f })
                {
                    points.Add(new Vector3(x, y, z));
                }
            }
        }

        // Point index = 4·x + 2·y + z over the ± bits; two triangles per face, wound either way.
        (int, int, int)[] triangles =
        [
            (0, 1, 3), (0, 3, 2), (4, 5, 7), (4, 7, 6),
            (0, 1, 5), (0, 5, 4), (2, 3, 7), (2, 7, 6),
            (0, 2, 6), (0, 6, 4), (1, 3, 7), (1, 7, 5),
        ];

        PhysicsLedge ledge = new(points, triangles, [], [], [], Vector3.Zero, 1.1f);

        return new IvpStaticPropCollide(PhysicsLedgeTree.ForLedge(ledge), "metal");
    }
}
