using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// The strength the lightcache ranks a spotlight by includes its cone, so a spotlight aimed away from a point takes no slot
/// there (B665).
/// </summary>
/// <remarks>
/// `engine.dll` (TF2 x64) `FUN_1801b60a0`, `AddWorldLightToLightingState`, ranks a light by Rec.601 luminance times the
/// ratio it computes at the point (`LocalLightFoldTests`); that ratio comes from `FUN_1801b99a0` → `FUN_1801b9570`, which
/// is the distance falloff times a spotlight's cone (`dot2` times the fringe), and a light whose ratio is not positive is
/// never added. *Read in disassembly* for each link (RISKS B453, B424's `FUN_1801b9570`, B514's sky trace); that the
/// ranking multiplies THIS ratio is the chain of those reads, not one read of the multiply (*interpolated*, B665).
///
/// The port ranked by the distance falloff alone and applied the cone only when the chosen lights were shaded, so on
/// `koth_toxic` three spotlights that do not reach the vat's lighting origin — one aimed up from beneath the point, two
/// whose cones miss it — took three of its four slots and evicted the green lamp above it.
/// </remarks>
public sealed class LocalLightRankingConformanceTests
{
    [Test]
    public void Strongest_FourNearSpotsAimedAwayAndAFarPointLamp_ChoosesOnlyThePointLamp()
    {
        // Four bright spotlights ten units off, each aimed straight away from the point (dot2 = -1, outside any cone),
        // and one dim point lamp a hundred units off. Distance-only ranking keeps the four spots and evicts the lamp.
        List<BspWorldLight> lights =
        [
            Spot((10f, 0f, 0f), (1f, 0f, 0f)),
            Spot((-10f, 0f, 0f), (-1f, 0f, 0f)),
            Spot((0f, 10f, 0f), (0f, 1f, 0f)),
            Spot((0f, -10f, 0f), (0f, -1f, 0f)),
            Lamp((0f, 0f, 100f), 100f),
        ];

        Span<LocalLight> into = stackalloc LocalLight[LocalLights.MaximumLocalLights];

        LocalLights.Strongest(lights, 0f, 0f, 0f, into).ShouldBe(1);

        into[0].Z.ShouldBe(100f);
        into[0].Spot.ShouldBeFalse();
    }

    [Test]
    public void Split_ASpotOffItsAxisAgainstFourLamps_RanksByTheConedStrengthAndFoldsTheSpot()
    {
        // The spot sits eight units over a point six to its side (SpotlightFalloffConformanceTests' geometry):
        // distance 10, so falloff 0.01; dot2 0.8, fringe (0.8 - 0.5) / (0.9 - 0.5) = 0.75; ratio 0.01 · 0.8 · 0.75 = 0.006.
        // White 100 gives a luminance strength of 0.6. Four white lamps at ten units, 70 each, are 0.7.
        // Ranked by the coned ratio the spot is the weakest and is folded; ranked by distance alone it is 1.0 and evicts
        // a lamp.
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

        foreach (LocalLight chosen in into)
        {
            chosen.Spot.ShouldBeFalse("the spot's coned strength, 0.6, is under every lamp's 0.7");
        }

        // Folded onto +Z by its n·d of 0.8: 0.8 · 0.006 · 100 = 0.48, the value SpotlightFalloffConformanceTests pins.
        cube.PositiveZ.Red.ShouldBe(0.48f, 0.001f);
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
