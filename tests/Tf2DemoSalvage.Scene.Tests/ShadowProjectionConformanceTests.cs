using System;
using System.Numerics;

using Tf2DemoSalvage.Scene.ProjectedShadows;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// <c>CClientShadowMgr::BuildRenderToTextureShadow</c> (`clientshadowmgr.cpp:2464-2565`) and what it calls: the shadow's
/// basis, size, origin, falloff and matrices, and its extra clip planes (finding 107).
/// </summary>
/// <remarks>
/// **The fixture is a standing player's box at the origin of its own frame**, (−10, −10, 0) to (10, 10, 72), cast straight
/// down (0, 0, −1) a distance of 50 — `shadow_control`'s default distance. Every expected value is worked by hand from the
/// SDK lines cited at each test.
/// </remarks>
public sealed class ShadowProjectionConformanceTests
{
    private const float Tolerance = 1e-4f;

    private static readonly Vector3 Down = new(0f, 0f, -1f);

    private static readonly Vector3 Feet = new(100f, 200f, 300f);

    private static ShadowProjection Standing(Vector3 mins, Vector3 maxs, float yaw = 0f) =>
        ShadowProjection.ForRenderToTexture(Feet, (0f, yaw, 0f), mins, maxs, Down, castDistance: 50f);

    /// <remarks>
    /// The y axis is the box's longest side once projected off the shadow direction (`:2494-2506`), x is y × dir (`:2512`),
    /// and each side is the box projected on that axis plus two texels' worth at each edge (`:2517-2525`). A 60 × 20 × 72 box
    /// facing +x: y along +x (60 beats 20), x = (1, 0, 0) × (0, 0, −1) = (0, 1, 0), size (20 + 4, 60 + 4).
    /// </remarks>
    [Test]
    public void ForRenderToTexture_ABoxLongerAlongItsForward_TakesYAlongItAndPadsBothSides()
    {
        ShadowProjection shadow = Standing(new Vector3(-30f, -10f, 0f), new Vector3(30f, 10f, 72f));

        shadow.Size.ShouldBe(new Vector2(24f, 64f));
        shadow.YAxis.ShouldBe(Vector3.UnitX);
        shadow.XAxis.ShouldBe(Vector3.UnitY);
    }

    /// <remarks>
    /// `ComputeLocalShadowOrigin` (`:2022-2071`): the centroid (0, 0, 36) moved along the shadow direction to the box's
    /// face furthest back — minDist = −(0·0 + 0·0 + 36·−1) + (−1 × 72) = −36 — so the origin is (0, 0, 72) in the box's frame,
    /// (100, 200, 372) in the world (`:2532-2535`); the falloff starts at the radius minus minDist, √5984 / 2 + 36.
    /// </remarks>
    [Test]
    public void ForRenderToTexture_AStandingBox_PutsTheOriginAtItsTopAndTheFalloffPastItsRadius()
    {
        ShadowProjection shadow = Standing(new Vector3(-10f, -10f, 0f), new Vector3(10f, 10f, 72f));
        float falloffStart = (MathF.Sqrt(5984f) * 0.5f) + 36f;

        shadow.Origin.ShouldBe(new Vector3(100f, 200f, 372f));
        shadow.FalloffStart.ShouldBe(falloffStart, Tolerance);
        shadow.MaxHeight.ShouldBe(50f + falloffStart, Tolerance, "the cast distance plus the falloff start (:2549)");
    }

    /// <remarks>
    /// `BuildOrthoWorldToShadowMatrix` (`:2126-2151`) then `BuildWorldToTextureMatrix` (`:2112-2122`): shadow space has its
    /// axes x, y, dir and its origin at the shadow origin, and the texture divides x and y by the size and adds a half. The
    /// feet, 72 below the origin straight along the shadow, are at the texture's centre at depth 72; a point 6 along +y
    /// (the shadow's x axis) is at u = 6 / 24 + 0.5.
    /// </remarks>
    [Test]
    public void WorldToTexture_TheFeetAndAPointBeside_AreAtTheCentreAndAQuarterRight()
    {
        ShadowProjection shadow = Standing(new Vector3(-10f, -10f, 0f), new Vector3(10f, 10f, 72f));

        shadow.WorldToTexture(Feet).ShouldBe(new Vector3(0.5f, 0.5f, 72f));
        shadow.WorldToTexture(Feet + new Vector3(0f, 6f, 0f)).X.ShouldBe(0.75f, Tolerance);
        shadow.WorldToShadow(Feet + new Vector3(0f, 6f, 0f)).ShouldBe(new Vector3(6f, 0f, 72f));
    }

    /// <remarks>
    /// The basis is the render angles' (`AngleVectors`, then `vec[1] *= -1`, `:2475-2476`): at yaw 90 forward is +y and
    /// the long side follows it, so the shadow's y axis is +y and its x axis (0, 1, 0) × (0, 0, −1) = (−1, 0, 0).
    /// </remarks>
    [Test]
    public void ForRenderToTexture_TurnedNinetyDegrees_TurnsItsAxes()
    {
        ShadowProjection shadow = Standing(new Vector3(-30f, -10f, 0f), new Vector3(30f, 10f, 72f), yaw: 90f);

        Vector3.Distance(shadow.YAxis, Vector3.UnitY).ShouldBeLessThan(Tolerance);
        Vector3.Distance(shadow.XAxis, -Vector3.UnitX).ShouldBeLessThan(Tolerance);
        shadow.Size.X.ShouldBe(24f, Tolerance);
        shadow.Size.Y.ShouldBe(64f, Tolerance);
    }

    /// <remarks>
    /// `ComputeExtraClipPlanes` (`:2171-2214`): from the render origin, each axis steps to the box's max where the local
    /// shadow direction is negative and its min otherwise, the plane facing that way — here (−10, −10, +72) to the corner
    /// (90, 190, 372), planes (−1, 0, 0)·p = −90, (0, −1, 0)·p = −190 and (0, 0, 1)·p = 372.
    /// </remarks>
    [Test]
    public void ForRenderToTexture_AStandingBox_HasThreeClipPlanesThroughItsFarCorner()
    {
        ShadowProjection shadow = Standing(new Vector3(-10f, -10f, 0f), new Vector3(10f, 10f, 72f));

        shadow.ClipPlanes.ShouldBe(
        [
            (new Vector3(-1f, 0f, 0f), -90f),
            (new Vector3(0f, -1f, 0f), -190f),
            (new Vector3(0f, 0f, 1f), 372f),
        ]);
    }

    /// <remarks>
    /// `AngleVectors` with roll (`mathlib`): pitch 0, yaw 0, roll 90 turns up into −y and right into −z; vec[1] is −right,
    /// so the box's own y runs along +z.
    /// </remarks>
    [Test]
    public void Basis_RolledNinetyDegrees_IsSourcesAngleVectorsWithRightNegated()
    {
        (Vector3 forward, Vector3 left, Vector3 up) = ShadowProjection.Basis((0f, 0f, 90f));

        Vector3.Distance(forward, Vector3.UnitX).ShouldBeLessThan(Tolerance);
        Vector3.Distance(left, Vector3.UnitZ).ShouldBeLessThan(Tolerance);
        Vector3.Distance(up, -Vector3.UnitY).ShouldBeLessThan(Tolerance);
    }
}
