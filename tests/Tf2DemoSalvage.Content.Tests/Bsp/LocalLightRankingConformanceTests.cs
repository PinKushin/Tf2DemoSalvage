using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// A light is ranked by luminance times its distance falloff alone, and a spot is admitted by a sphere around the point
/// touching its cone (B669, correcting B665).
/// </summary>
/// <remarks>
/// `engine.dll` `FUN_1801b60a0`: `MULSS XMM6,XMM7` is the luminance times `FUN_1801b9570`'s return, with the angle kept apart
/// in XMM8 for the fold. That return carries no cone: **measured**, the running engine returned 0.002225 for the spot at
/// (-3400 -1128 486), 449 units from #215's point, which is 1/449 and not 1/449 times the 0.0512 fringe
/// (`measurements/2026-11-03-b669-pipe-traces-live.md`). B665 had this as the ratio's cone, *interpolated*.
///
/// **What B665 saw is real but is a different test.** `FUN_1801b99a0`, before any trace, refuses a spot whose cone misses a
/// sphere (`FUN_1801736b0`) around the point, radius the distance to the far corner of its 32x32x128 cell (`FUN_1801b60a0`).
/// A spot aimed away from a point takes no slot, unless the sphere reaches it.
/// </remarks>
public sealed class LocalLightRankingConformanceTests
{
    /// <summary>A point a little short of its cell's far corner: (32, 32, 128), so the sphere's radius is about 1.73.</summary>
    private const float NearCorner = 1f;

    [Test]
    public void Strongest_FourNearSpotsAimedAwayAndAFarPointLamp_ChoosesOnlyThePointLamp()
    {
        (float x, float y, float z) = (32f - NearCorner, 32f - NearCorner, 128f - NearCorner);

        // Four bright spotlights ten units off, each aimed straight away from the point, and one dim point lamp a hundred
        // units off. The cone test refuses the four, which would otherwise rank far above the lamp.
        List<BspWorldLight> lights =
        [
            Spot((x + 10f, y, z), (1f, 0f, 0f)),
            Spot((x - 10f, y, z), (-1f, 0f, 0f)),
            Spot((x, y + 10f, z), (0f, 1f, 0f)),
            Spot((x, y - 10f, z), (0f, -1f, 0f)),
            Lamp((x, y, z + 100f), 100f),
        ];

        Span<LocalLight> into = stackalloc LocalLight[LocalLights.MaximumLocalLights];

        LocalLights.Strongest(lights, x, y, z, into).ShouldBe(1);

        into[0].Z.ShouldBe(z + 100f);
        into[0].Spot.ShouldBeFalse();
    }

    [Test]
    public void Strongest_ASpotAimedAwayWithinTheCellsSphere_TakesASlot()
    {
        // At the cell's near corner, (0, 0, 0): the far corner is (32, 32, 128), so the sphere's radius is 135.8 and holds
        // a spot ten units off. `fVar2 <= param_2` admits it, aimed away or not, and it outranks the lamp.
        List<BspWorldLight> lights =
        [
            Spot((10f, 0f, 0f), (1f, 0f, 0f)),
        ];

        Span<LocalLight> into = stackalloc LocalLight[LocalLights.MaximumLocalLights];

        LocalLights.Strongest(lights, 0f, 0f, 0f, into).ShouldBe(1);
        into[0].Spot.ShouldBeTrue();
    }

    [Test]
    public void Split_ASpotOffItsAxisAgainstFourLamps_RanksByDistanceAloneAndEvictsALamp()
    {
        // The spot sits eight units over a point six to its side: distance 10, so falloff 0.01. White 100 gives a strength
        // of 1.0, with no cone in it. Four white lamps at ten units, 70 each, are 0.7. So the spot takes a slot and one lamp
        // folds, by its own 0.7 on the face it looks at. The coned reading (B665) gave the spot 0.6, under every lamp.
        List<BspWorldLight> lights =
        [
            new(
                (0f, 0f, 8f), (100f, 100f, 100f), (0f, 0f, -1f), WorldLightKind.Spotlight,
                ConstantAttenuation: 0f, LinearAttenuation: 0f, QuadraticAttenuation: 1f,
                StopDot: 0.9f, StopDot2: 0.5f, Exponent: 1f),
            Lamp((16f, 0f, 0f), 70f),
            Lamp((-4f, 0f, 0f), 70f),
            Lamp((6f, 10f, 0f), 70f),
            Lamp((6f, -10f, 0f), 70f),
        ];

        Span<LocalLight> into = stackalloc LocalLight[LocalLights.MaximumLocalLights];
        AmbientCube cube = default;

        LocalLights.Split(lights, 6f, 0f, 0f, into, ref cube).ShouldBe(4);

        into[0].Spot.ShouldBeTrue("its strength, 1.0, is over every lamp's 0.7");

        // The one lamp folded, the last of the equal four: (6, -10, 0), 70 at ten units, square on to -y.
        cube.NegativeY.Red.ShouldBe(0.7f, 0.001f);
        cube.PositiveZ.Red.ShouldBe(0f);
    }

    /// <summary>
    /// `FUN_1800cbdd0`, `Mod_LoadWorldlights`' radius for a lump radius of 0: with HDR on, `(sqrt(b² - 4a(c - 66.67·|I|)) - b) / 2a`.
    /// `cp_process_f12`'s point lamp of 1960.78 on each channel, a = 1: |I| 3396.1, radius 475.8. It is out at 761, where the
    /// running engine never traces it, though its falloff there is 0.0034, far over `r_worldlightmin`.
    /// </summary>
    [TestCase(450f, 1)]
    [TestCase(500f, 0)]
    public void Strongest_APointLampAroundItsComputedRadius_IsCutOffThere(float distance, int expected)
    {
        // At a cell corner, so the cell adds 1.73 at most. HDR is on by default in `BspHdr`.
        (float x, float y, float z) = (32f - NearCorner, 32f - NearCorner, 128f - NearCorner);
        List<BspWorldLight> lights = [Lamp((x, y, z + distance), 1960.7844f)];
        Span<LocalLight> into = stackalloc LocalLight[LocalLights.MaximumLocalLights];

        LocalLights.Strongest(lights, x, y, z, into).ShouldBe(expected);
    }

    private static BspWorldLight Spot((float X, float Y, float Z) origin, (float X, float Y, float Z) aim) =>
        new(
            origin, (10000f, 10000f, 10000f), aim, WorldLightKind.Spotlight,
            ConstantAttenuation: 0f, LinearAttenuation: 0f, QuadraticAttenuation: 1f,
            StopDot: 0.9f, StopDot2: 0.7f, Exponent: 1f);

    private static BspWorldLight Lamp((float X, float Y, float Z) origin, float bright) =>
        new(
            origin, (bright, bright, bright), (0f, 0f, 0f), WorldLightKind.Point,
            ConstantAttenuation: 0f, LinearAttenuation: 0f, QuadraticAttenuation: 1f);
}
