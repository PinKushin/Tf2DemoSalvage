using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>An `emit_surface` light at runtime: an inverse-square area light the light cache ranks and folds like any other (B669).</summary>
/// <remarks>
/// *Read in disassembly*, `engine.dll` (TF2 x64), and from published source where vrad keeps the same two functions:
///
/// - **Distance**, `FUN_1800efe40` case 0, `Engine_WorldLightDistanceFalloff` (`leaf_ambient_lighting.cpp:77`):
///   `1 / max( 1, d² + 1e-10 )`, zero past a nonzero radius. The attenuation terms are not read, so their being all zero
///   is not "no falloff".
/// - **Angle**, `FUN_1800efbf0` case 0, `Engine_WorldLightAngle` (`:58`): `dot2 = -delta · normal`, zero at or under
///   `ON_EPSILON / 10` = 0.01 ("behind light surface"), else `dot2` once, with no fringe.
/// - **Cutoffs**, `FUN_1801b9570` and `FUN_1801b60a0`: both `r_worldlightmin` tests are guarded by `type != 0`.
/// - **Slot shape**, `FUN_1800f0e10` case 0: a spot with attenuation 0/0/1, theta and phi pi, both dots 0, falloff 1.
/// </remarks>
public sealed class LocalLightSurfaceConformanceTests
{
    [Test]
    public void Strongest_ASurfaceLightFacingThePoint_TakesASlotAsTheEnginesSpot()
    {
        List<BspWorldLight> lights = [Surface((0f, 0f, 100f), (0f, 0f, -1f), 5000f)];
        Span<LocalLight> into = stackalloc LocalLight[LocalLights.MaximumLocalLights];

        LocalLights.Strongest(lights, 0f, 0f, 0f, into).ShouldBe(1);

        LocalLight taken = into[0];
        (taken.Constant, taken.Linear, taken.Quadratic).ShouldBe((0f, 0f, 1f));
        (taken.Spot, taken.SpotInner, taken.SpotOuter, taken.SpotExponent).ShouldBe((true, 0f, 0f, 1f));
        taken.Direction.ShouldBe((0f, 0f, -1f));
        taken.Red.ShouldBe(5000f);
    }

    /// <summary>
    /// 200 units off and 60 degrees from its normal, a surface light of 40000 ranks at `1/40000 · 0.5 · 40000` = 0.5, between
    /// lamps at 0.75 and 0.3. With no cosine it would rank first (1), and with two, last (0.25).
    /// </summary>
    [Test]
    public void Strongest_ASurfaceLightOffItsNormal_RanksByDistanceTimesOneCosine()
    {
        List<BspWorldLight> lights =
        [
            Surface((173.2050808f, 0f, 100f), (0f, 0f, -1f), 40000f),
            Lamp((0f, 0f, -100f), 7500f),
            Lamp((0f, 0f, -200f), 12000f),
        ];
        Span<LocalLight> into = stackalloc LocalLight[LocalLights.MaximumLocalLights];

        LocalLights.Strongest(lights, 0f, 0f, 0f, into).ShouldBe(3);

        into[0].Red.ShouldBe(7500f);
        into[1].Red.ShouldBe(40000f);
        into[2].Red.ShouldBe(12000f);
    }

    /// <summary>`0.01 &lt; dot2`, strictly: a point behind the surface or at 0.01 is unlit, and just inside it is lit.</summary>
    [TestCase(-1f, 0)]
    [TestCase(0.009f, 0)]
    [TestCase(0.011f, 1)]
    public void Strongest_ASurfaceLightsNormalAgainstThePoint_NeedsMoreThanTheEpsilon(float dot2, int expected)
    {
        // The point is 100 units down -Z from the light, so delta (point to light) is +Z and dot2 = -normal.Z.
        List<BspWorldLight> lights = [Surface((0f, 0f, 100f), (MathF.Sqrt(1f - (dot2 * dot2)), 0f, -dot2), 5000f)];
        Span<LocalLight> into = stackalloc LocalLight[LocalLights.MaximumLocalLights];

        LocalLights.Strongest(lights, 0f, 0f, 0f, into).ShouldBe(expected);
    }

    /// <summary>Neither `r_worldlightmin` test applies to type 0; the point lamp beside it is the control that the test still runs.</summary>
    [Test]
    public void Strongest_ASurfaceLightBelowWorldLightMin_StillTakesASlot()
    {
        // Ratio 1/1,000,000 at 1000 units, intensity 1: 1e-6, under the 0.0002 minimum.
        List<BspWorldLight> lights =
        [
            Surface((0f, 0f, 1000f), (0f, 0f, -1f), 1f),
            Lamp((0f, 0f, -1000f), 1f),
        ];
        Span<LocalLight> into = stackalloc LocalLight[LocalLights.MaximumLocalLights];

        LocalLights.Strongest(lights, 0f, 0f, 0f, into).ShouldBe(1);

        into[0].Spot.ShouldBeTrue();
    }

    /// <summary>
    /// The surface exemption skips both comparisons that used to refuse a NaN, and one in a slot breaks the sort's order and poisons a
    /// cached cube. A NaN strength is refused for every kind, and the control beside it survives.
    /// </summary>
    [Test]
    public void Split_ASurfaceLightOfNaNIntensity_IsNeitherSlottedNorFolded()
    {
        List<BspWorldLight> lights =
        [
            Surface((0f, 0f, 100f), (0f, 0f, -1f), float.NaN),
            Lamp((0f, 0f, 100f), 10000f),
        ];
        Span<LocalLight> into = stackalloc LocalLight[LocalLights.MaximumLocalLights];
        AmbientCube cube = default;

        LocalLights.Split(lights, 0f, 0f, 0f, into, ref cube).ShouldBe(1);

        into[0].Red.ShouldBe(10000f);
        cube.PositiveZ.Red.ShouldBe(0f);
    }

    /// <summary>The radius is the only cull: `d² &gt; r²` is out, and exactly on it is in.</summary>
    [TestCase(100f, 1)]
    [TestCase(99.9f, 0)]
    public void Strongest_ASurfaceLightWithARadius_CullsPastIt(float radius, int expected)
    {
        List<BspWorldLight> lights = [Surface((0f, 0f, 100f), (0f, 0f, -1f), 5000f) with { Radius = radius }];
        Span<LocalLight> into = stackalloc LocalLight[LocalLights.MaximumLocalLights];

        LocalLights.Strongest(lights, 0f, 0f, 0f, into).ShouldBe(expected);
    }

    /// <summary>
    /// The distance clamps at one unit: 0.5 units off the light is `1 / 1`, not `1 / 0.25`. Four brighter lamps take the
    /// slots, so the light is folded: the +Z face adds `n·d (1) · dot2 (1) · falloff (1) · 3` = 3 exactly.
    /// </summary>
    [Test]
    public void Split_ASurfaceLightUnderAUnitAway_ClampsTheDistanceToOne()
    {
        List<BspWorldLight> lights =
        [
            Lamp((64f, 0f, 0f), 900000f),
            Lamp((-64f, 0f, 0f), 800000f),
            Lamp((0f, 64f, 0f), 700000f),
            Lamp((0f, -64f, 0f), 600000f),
            Surface((0f, 0f, 0.5f), (0f, 0f, -1f), 3f),
        ];
        Span<LocalLight> into = stackalloc LocalLight[LocalLights.MaximumLocalLights];
        AmbientCube cube = default;

        LocalLights.Split(lights, 0f, 0f, 0f, into, ref cube).ShouldBe(4);

        cube.PositiveZ.Red.ShouldBe(3f, 0.0001f);
        cube.NegativeZ.Red.ShouldBe(0f);
    }

    /// <summary>
    /// A folded surface light at 100 units, straight overhead and facing down: `1 · 1 · 1/10000 · 5000` on the +Z face. One
    /// cosine of the light's own, not two: 200 units off at 60 degrees, the +Z face takes `0.5 · 0.5 · 1/40000 · 5000`, the
    /// first 0.5 being the face's n·d and the second the light's.
    /// </summary>
    [TestCase(0f, 100f, 0.5f)]
    [TestCase(173.2050808f, 100f, 0.03125f)]
    public void Split_AFoldedSurfaceLight_AddsOneCosineOfItsOwn(float across, float up, float expected)
    {
        List<BspWorldLight> lights =
        [
            Lamp((64f, 0f, 0f), 900000f),
            Lamp((-64f, 0f, 0f), 800000f),
            Lamp((0f, 64f, 0f), 700000f),
            Lamp((0f, -64f, 0f), 600000f),
            Surface((across, 0f, up), (0f, 0f, -1f), 5000f),
        ];
        Span<LocalLight> into = stackalloc LocalLight[LocalLights.MaximumLocalLights];
        AmbientCube cube = default;

        LocalLights.Split(lights, 0f, 0f, 0f, into, ref cube);

        cube.PositiveZ.Green.ShouldBe(expected, 0.0001f);
    }

    /// <summary>The visibility trace takes type 0 down the same path as the rest (`FUN_1801b9570`).</summary>
    [Test]
    public void Strongest_ASurfaceLightBehindAWall_TakesNoSlot()
    {
        List<BspWorldLight> lights = [Surface((0f, 0f, 100f), (0f, 0f, -1f), 5000f)];
        Span<LocalLight> into = stackalloc LocalLight[LocalLights.MaximumLocalLights];

        LocalLights.Strongest(lights, 0f, 0f, 0f, into, traceKeeps: static _ => false).ShouldBe(0);
    }

    /// <summary>A light's in-ambient-cube bit is `DWL_FLAGS_INAMBIENTCUBE`, 1 (`bspfile.h:966`), of `dworldlight_t.flags`.</summary>
    [TestCase(0, false)]
    [TestCase(1, true)]
    [TestCase(2, false)]
    [TestCase(3, true)]
    public void InAmbientCube_TheFlagsLowBit_IsTheAnswer(int flags, bool expected) =>
        (Surface(default, (0f, 0f, -1f), 1f) with { Flags = flags }).InAmbientCube.ShouldBe(expected);

    private static BspWorldLight Surface((float X, float Y, float Z) origin, (float X, float Y, float Z) normal, float bright) =>
        new(origin, (bright, bright, bright), normal, WorldLightKind.Surface);

    private static BspWorldLight Lamp((float X, float Y, float Z) origin, float bright) =>
        new(origin, (bright, bright, bright), default, WorldLightKind.Point, QuadraticAttenuation: 1f);
}
