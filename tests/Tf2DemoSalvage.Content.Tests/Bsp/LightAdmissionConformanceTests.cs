using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>What `Mod_LoadWorldlights` rewrites, and `FUN_1801b99a0`'s tests against a box (B669's review).</summary>
/// <remarks>
/// `engine.dll` `FUN_1801042d0` and `FUN_1800cbdd0` (0x1801054d0 and 0x1800ccfd0 on build 11191711); `FUN_1801b99a0`, `FUN_1801736b0`,
/// `FUN_180276b00`; `FUN_1801b60a0`'s cell. Read in decompile and disassembly, and at #215 the 83 lights this admits are the 83 the
/// running engine traced (`measurements/2026-11-03-b669-pipe-traces-live.md`).
/// </remarks>
public sealed class LightAdmissionConformanceTests
{
    private const float HdrLumen = 1960.7844f;

    private static BspWorldLight Lamp(float bright) =>
        new((0f, 0f, 0f), (bright, bright, bright), default, WorldLightKind.Point, QuadraticAttenuation: 1f);

    /// <summary>The factor is the lump's: `sqrt( 66.67 · |I| )` on the HDR lump and `sqrt( 33.33 · |I| )` on the other, process setting or no.</summary>
    [TestCase(true, 475.828f)]
    [TestCase(false, 336.461f)]
    public void AsLoaded_AQuadraticLampOfRadiusZero_ComputesFromTheLumpItCameFrom(bool hdrLump, float expected)
    {
        BspWorldLights.AsLoaded(Lamp(HdrLumen), hdrLump).Radius.ShouldBe(expected, 0.005f);
    }

    [Test]
    public void AsLoaded_ALinearLamp_DividesByTheLinearTerm()
    {
        // (66.67 * |I| - constant) / linear: |I| = sqrt(3) * 100, so (11547.0 - 2) / 4.
        BspWorldLight light = new((0f, 0f, 0f), (100f, 100f, 100f), default, WorldLightKind.Point, ConstantAttenuation: 2f, LinearAttenuation: 4f);

        BspWorldLights.AsLoaded(light, hdrLump: true).Radius.ShouldBe(2886.25f, 0.1f);
    }

    [Test]
    public void AsLoaded_ARadiusAlreadyGiven_IsKept()
    {
        BspWorldLights.AsLoaded(Lamp(HdrLumen) with { Radius = 0.5f }, hdrLump: true).Radius.ShouldBe(0.5f);
    }

    [TestCase(WorldLightKind.Point)]
    [TestCase(WorldLightKind.Spotlight)]
    public void AsLoaded_APointOrSpotOfNoAttenuation_BecomesQuadratic(WorldLightKind kind)
    {
        BspWorldLight loaded = BspWorldLights.AsLoaded(new BspWorldLight((0f, 0f, 0f), (100f, 0f, 0f), default, kind), hdrLump: true);

        loaded.QuadraticAttenuation.ShouldBe(1f);
        loaded.ConstantAttenuation.ShouldBe(0f);
    }

    /// <summary>The loop has no case for type 0, so its terms stay zero, and its radius is the no-distance answer.</summary>
    [Test]
    public void AsLoaded_ASurfaceLight_KeepsItsTermsAndHas2000()
    {
        BspWorldLight loaded = BspWorldLights.AsLoaded(new BspWorldLight((0f, 0f, 0f), (100f, 100f, 100f), default, WorldLightKind.Surface), hdrLump: true);

        loaded.QuadraticAttenuation.ShouldBe(0f);
        loaded.ConstantAttenuation.ShouldBe(0f, "a sabotage that wrote the constant passed until this line");
        loaded.Radius.ShouldBe(2000f);
    }

    [Test]
    public void AsLoaded_ASpotOfExponentZero_BecomesOne()
    {
        BspWorldLights.AsLoaded(new BspWorldLight((0f, 0f, 0f), (1f, 1f, 1f), default, WorldLightKind.Spotlight, Exponent: 0f), hdrLump: true)
            .Exponent.ShouldBe(1f);
    }

    /// <summary>`(int)fabs( v ) >> shift`, complemented when negative, then that cell's far edge: an exact negative multiple stays on its far edge.</summary>
    [TestCase(-64f, 5, -64f)]
    [TestCase(-33f, 5, -32f)]
    [TestCase(-32f, 5, -32f)]
    [TestCase(-31.5f, 5, 0f)]
    [TestCase(-0.5f, 5, 0f)]
    [TestCase(0f, 5, 32f)]
    [TestCase(31.9f, 5, 32f)]
    [TestCase(32f, 5, 64f)]
    [TestCase(-128f, 7, -128f)]
    [TestCase(127.9f, 7, 128f)]
    public void CellFarCorner_AtEachBoundary_IsTheEnginesCell(float value, int shift, float expected)
    {
        LocalLights.CellFarCorner(value, shift).ShouldBe(expected);
    }

    private static BspWorldLight SpotDown(float x, float y, float z, float outer) =>
        new((x, y, z), (100000f, 100000f, 100000f), (0f, 0f, -1f), WorldLightKind.Spotlight,
            QuadraticAttenuation: 1f, Radius: 100000f, StopDot: 1f, StopDot2: outer, Exponent: 1f);

    /// <summary>
    /// A sphere of 1.73 at a cell's corner, a 45-degree cone 100 above it. 102 off the axis the sphere pokes into the cone by the pulled-back
    /// apex, and 104 off it does not. The point itself is outside the cone in both.
    /// </summary>
    [TestCase(102f, true)]
    [TestCase(104f, false)]
    public void Reaches_ASpotWhoseConeMissesThePoint_IsAdmittedOnlyWhereTheCellsSphereTouches(float offAxis, bool expected)
    {
        const float At = 31f;
        const float Height = 127f;

        LocalLights.Reaches(SpotDown(At - offAxis, At, Height + 100f, 0.70710677f), At, At, Height).ShouldBe(expected);
    }

    /// <summary>
    /// `FUN_1801736b0`'s second branch: the point past the apex and behind the light, inside the pulled-back cone. A box whose far corner is 10
    /// off makes the sphere 10: 8 behind the light the sphere holds the light, and 12 behind it does not.
    /// </summary>
    [TestCase(8f, true)]
    [TestCase(12f, false)]
    public void Reaches_APointBehindTheSpot_NeedsTheSphereToHoldTheLight(float behind, bool expected)
    {
        LightBounds box = new((-1000f, -1000f, -1000f), (10f, 0f, behind));

        LocalLights.Reaches(SpotDown(0f, 0f, 0f, 0.70710677f), 0f, 0f, behind, box).ShouldBe(expected);
    }

    /// <summary>
    /// A `stopdot2` of 1 has a sine of 0, so the apex goes back by infinity, and a zero in the normal makes that NaN. `JC` after `COMISS` is taken on
    /// unordered: refused, on the axis or not.
    /// </summary>
    [Test]
    public void Reaches_ASpotOfSineZero_IsRefused()
    {
        LocalLights.Reaches(SpotDown(31f, 31f, 227f, 1f), 31f, 31f, 127f).ShouldBeFalse();
    }

    /// <summary>
    /// `FUN_1801b6bf0` and `FUN_1801b6550` pass the prop's box, not the cell. A lamp of radius 100, 150 from a point: out against the cell, in
    /// against a box reaching 100 toward it. And past its radius at the point it still folds: flags 2 skips the falloff's cutoff.
    /// </summary>
    [Test]
    public void Reaches_ALampPastItsRadiusFromThePoint_IsAdmittedByAPropBoxNearIt()
    {
        BspWorldLight lamp = Lamp(100f) with { Origin = (31f, 31f, 277f), Radius = 100f };
        LightBounds tall = new((0f, 0f, 0f), (32f, 32f, 227f));

        LocalLights.Reaches(lamp, 31f, 31f, 127f).ShouldBeFalse("the point's cell ends at z 128, 149 from the lamp");
        LocalLights.Reaches(lamp, 31f, 31f, 127f, tall).ShouldBeTrue("the box ends at z 227, 50 from it");
        LocalLights.Fold(default, lamp, 31f, 31f, 127f).PositiveZ.Red.ShouldBe(100f / (150f * 150f), 0.0001f);
    }

    /// <summary>A static prop's dlight is tested against the prop's box, `FUN_1801b6550` from `FUN_1801ba590`; without one, against the point's cell.</summary>
    [Test]
    public void Add_ADlightGivenAPropsBox_IsTestedAgainstIt()
    {
        BspWorldLight dlight = Lamp(100f) with { Origin = (31f, 31f, 277f), Radius = 100f };
        LightBounds tall = new((0f, 0f, 0f), (32f, 32f, 227f));

        LightingState cell = new(default);
        LightingState box = new(default);

        LocalLights.Add(cell, dlight, 31f, 31f, 127f, dynamicLight: true);
        LocalLights.Add(box, dlight, 31f, 31f, 127f, dynamicLight: true, bounds: tall);

        cell.Count.ShouldBe(0);
        box.Count.ShouldBe(1);
    }

    /// <summary>`FUN_180276b00` uses plain compares: an inverted box, as a negative scale can make, measures from its min and throws nothing.</summary>
    [TestCase(25f, false)]
    [TestCase(50f, true)]
    public void Reaches_AnInvertedBox_MeasuresFromItsMinFirst(float radius, bool expected)
    {
        // Min 40, max 0 on x: the light at 5 is under the min, so 35 away.
        BspWorldLight lamp = Lamp(100f) with { Origin = (5f, 0f, 0f), Radius = radius };

        LocalLights.Reaches(lamp, 0f, 0f, 0f, new LightBounds((40f, 0f, 0f), (0f, 0f, 0f))).ShouldBe(expected);
    }

    /// <summary>`FUN_1800cbdd0` answers 0 where the falloff never reaches its threshold, and `FUN_1801b99a0` then has `r² &lt; d²` refuse it from outside the box; inside is `0 &lt; 0`.</summary>
    [TestCase(129f, false)]
    [TestCase(100f, true)]
    public void Reaches_ARadiusOfZeroLeftByTheLoad_IsRefusedOutsideTheBox(float z, bool expected)
    {
        // The cell of (0 0 0) ends at 128. 66.67 * |I| = 1154.7, under the constant: the root, (sqrt( 25 - 21.2 ) - 5) / 2, is negative, so 0. Its falloff
        // at 129 is 5.4e-4 of the 10, over `r_worldlightmin`, so the radius is what refuses it.
        BspWorldLight lamp = BspWorldLights.AsLoaded(
            new BspWorldLight((0f, 0f, z), (10f, 10f, 10f), default, WorldLightKind.Point, ConstantAttenuation: 1160f, LinearAttenuation: 5f, QuadraticAttenuation: 1f), hdrLump: true);

        lamp.Radius.ShouldBe(0f, "the control: the load left it 0");
        LocalLights.Reaches(lamp, 0f, 0f, 0f).ShouldBe(expected);
    }

    /// <summary>`FUN_1800efe40`'s branches: a point or spot has no clamp under 1, a surface light has, a quake light is its linear term less the distance, and a sky light is 1.</summary>
    [TestCase(WorldLightKind.Point, 0.5f, 3f, 12f)]
    [TestCase(WorldLightKind.Spotlight, 0.5f, 3f, 12f)]
    [TestCase(WorldLightKind.Surface, 0.5f, 3f, 3f)]
    public void Fold_ALightUnderOneUnitAway_ClampsOnlyASurfaceLight(WorldLightKind kind, float distance, float bright, float expected)
    {
        BspWorldLight light = new((0f, 0f, distance), (bright, bright, bright), (0f, 0f, -1f), kind, QuadraticAttenuation: 1f, StopDot: 0.5f, StopDot2: 0.5f, Exponent: 1f);

        LocalLights.Fold(default, light, 0f, 0f, 0f).PositiveZ.Red.ShouldBe(expected, 0.001f);
    }

    /// <summary>`emit_quakelight`, `linear_attn − d`, 0 past it, and all-zero terms are not rewritten: the load's loop has no case for it.</summary>
    [TestCase(30f, 60f, 30f)]
    [TestCase(70f, 60f, 0f)]
    [TestCase(30f, 0f, 0f)]
    public void Fold_AQuakeLight_IsItsLinearTermLessTheDistance(float distance, float linear, float expected)
    {
        BspWorldLight light = BspWorldLights.AsLoaded(
            new BspWorldLight((0f, 0f, distance), (1f, 1f, 1f), default, WorldLightKind.QuakeLight, LinearAttenuation: linear), hdrLump: true);

        light.QuadraticAttenuation.ShouldBe(0f);
        LocalLights.Fold(default, light, 0f, 0f, 0f).PositiveZ.Red.ShouldBe(expected, 0.001f);
    }

    /// <summary>`FUN_1800efe40` case 0 reads the radius whichever way it is reached; it is the point and spot case that takes the flag.</summary>
    [Test]
    public void Fold_ASurfaceLightPastItsRadius_AddsNothing()
    {
        BspWorldLight surface = new((0f, 0f, 200f), (1000f, 1000f, 1000f), (0f, 0f, -1f), WorldLightKind.Surface, Radius: 100f);

        LocalLights.Fold(default, surface, 0f, 0f, 0f).PositiveZ.Red.ShouldBe(0f);

        // The control: a radius that reaches adds the same light's 1000 / 200^2.
        LocalLights.Fold(default, surface with { Radius = 300f }, 0f, 0f, 0f).PositiveZ.Red.ShouldBe(0.025f, 0.0001f);
    }
}
