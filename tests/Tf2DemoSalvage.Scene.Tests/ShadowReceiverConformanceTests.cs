using System;
using System.Numerics;

using Tf2DemoSalvage.Scene.ProjectedShadows;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// Whether a world surface takes a render-to-texture shadow — `engine.dll` `FUN_1801233f0`, with the bounding sphere
/// `CShadowMgr::ProjectShadow` (`FUN_180127de0`) stores (finding 107).
/// </summary>
/// <remarks>
/// **The fixture** is the standing player of <see cref="ShadowProjectionConformanceTests"/>: shadow origin (100, 200, 372),
/// cast straight down, max height 50 + √5984 / 2 + 36, size 24 × 24. Its sphere is centred half the max height below the
/// origin with radius half the max height plus half the size's diagonal.
/// </remarks>
public sealed class ShadowReceiverConformanceTests
{
    private static readonly ShadowProjection Player = ShadowProjection.ForRenderToTexture(
        new Vector3(100f, 200f, 300f), (0f, 0f, 0f), new Vector3(-10f, -10f, 0f), new Vector3(10f, 10f, 72f), new Vector3(0f, 0f, -1f), 50f);

    private static readonly float MaxHeight = 50f + (MathF.Sqrt(5984f) * 0.5f) + 36f;

    [Test]
    public void BoundingSphere_ThePlayersShadow_IsHalfwayDownWithHalfTheDiagonalAdded()
    {
        Player.SphereCentre.ShouldBe(new Vector3(100f, 200f, 372f - (MaxHeight * 0.5f)));
        Player.SphereRadius.ShouldBe((MaxHeight * 0.5f) + (MathF.Sqrt((24f * 24f) + (24f * 24f)) * 0.5f), 1e-4f);
    }

    /// <remarks>A floor facing the shadow (dot ≤ −0.01), below its origin, the sphere crossing it: taken.</remarks>
    [Test]
    public void Takes_AFloorUnderThePlayer_IsTrue() =>
        ShadowReceiver.Takes(Player, Vector3.UnitZ, 300f, SurfaceShadowBits.DynamicShadows).ShouldBeTrue();

    /// <remarks>
    /// A floor whose plane faces down, the same way as the shadow (dot = +1): never, unless the surface is no-cull — then the
    /// shadow's origin must be BEHIND the plane (n·origin ≤ dist: −372 ≤ −300), and the sphere still crosses it.
    /// </remarks>
    [Test]
    public void Takes_AFloorWhosePlaneFacesDown_IsFalseUnlessNoCull()
    {
        ShadowReceiver.Takes(Player, -Vector3.UnitZ, -300f, SurfaceShadowBits.DynamicShadows).ShouldBeFalse();
        ShadowReceiver.Takes(Player, -Vector3.UnitZ, -300f, SurfaceShadowBits.DynamicShadows | SurfaceShadowBits.NoCull).ShouldBeTrue();
    }

    /// <remarks>The same no-cull plane with the origin in front of it (a ceiling at z = 390 facing down): not taken.</remarks>
    [Test]
    public void Takes_ANoCullCeilingAboveTheOrigin_IsFalse() =>
        ShadowReceiver.Takes(Player, -Vector3.UnitZ, -390f, SurfaceShadowBits.DynamicShadows | SurfaceShadowBits.NoCull).ShouldBeFalse();

    /// <remarks>The shadow's origin must not be behind the plane: a floor ABOVE the origin, at z = 400, is behind.</remarks>
    [Test]
    public void Takes_AFloorAboveTheOrigin_IsFalse() =>
        ShadowReceiver.Takes(Player, Vector3.UnitZ, 400f, SurfaceShadowBits.DynamicShadows).ShouldBeFalse();

    /// <remarks>Beyond the sphere (the floor further below than the centre plus the radius): not taken.</remarks>
    [Test]
    public void Takes_AFloorBeyondTheSphere_IsFalse() =>
        ShadowReceiver.Takes(Player, Vector3.UnitZ, 100f, SurfaceShadowBits.DynamicShadows).ShouldBeFalse();

    /// <remarks>A wall parallel to the shadow (dot 0): not facing it by 0.01, so never — even no-cull needs |dot| ≥ 0.01.</remarks>
    [Test]
    public void Takes_AWallAlongTheShadow_IsFalse()
    {
        ShadowReceiver.Takes(Player, Vector3.UnitX, 100f, SurfaceShadowBits.DynamicShadows).ShouldBeFalse();
        ShadowReceiver.Takes(Player, Vector3.UnitX, 100f, SurfaceShadowBits.DynamicShadows | SurfaceShadowBits.NoCull).ShouldBeFalse();
    }

    /// <remarks>Without the face's dynamic-shadows bit, or with translucent, alpha-tested or no-shadows set, never.</remarks>
    [TestCase(SurfaceShadowBits.None)]
    [TestCase(SurfaceShadowBits.DynamicShadows | SurfaceShadowBits.Translucent)]
    [TestCase(SurfaceShadowBits.DynamicShadows | SurfaceShadowBits.AlphaTest)]
    [TestCase(SurfaceShadowBits.DynamicShadows | SurfaceShadowBits.NoShadows)]
    public void Takes_AFloorTheEngineExcludes_IsFalse(SurfaceShadowBits flags) =>
        ShadowReceiver.Takes(Player, Vector3.UnitZ, 300f, flags).ShouldBeFalse();
}
