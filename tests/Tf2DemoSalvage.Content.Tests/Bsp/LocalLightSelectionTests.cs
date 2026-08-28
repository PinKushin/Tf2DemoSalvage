using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// Which lights <see cref="LocalLights.Strongest"/> hands a shader, and in what form.
/// </summary>
/// <remarks>
/// **The selection is shared with <see cref="LocalLights.AddTo"/> on purpose**, so these also pin
/// that the extraction did not change it: a model taking one set of lights in its diffuse and
/// another in its highlight would be a defect nothing else here could see.
///
/// **Written after the method rather than before it, which is the wrong order** — the conformance
/// suite came first, but this one did not. Two of the three mechanisms were then checked by
/// manipulation to recover part of what the ordering would have given:
///
/// - ranking by falloff alone, dropping the intensity factor, reddened
///   `RanksByStrength` and the pre-existing `TheStrongestAreChosen_NotTheFirstFour` and nothing
///   else;
/// - returning the raw attenuation terms, skipping vrad's all-zero rule, reddened
///   `NormalisesConstantToOne` alone.
///
/// The third — `IsLocal`'s exclusion of the sun and surface lights — was not sabotaged here,
/// because it is pre-existing and already carried by the eighteen tests beside this file. Said
/// plainly rather than claimed as a full pass.
/// </remarks>
public sealed class LocalLightSelectionTests
{
    [Test]
    public void Strongest_WithNoLights_WritesNone()
    {
        Span<LocalLight> into = stackalloc LocalLight[LocalLights.MaximumLocalLights];

        LocalLights.Strongest([], 0f, 0f, 0f, into).ShouldBe(0);
    }

    /// <summary>That only the four strongest survive, and that they are the four NEAREST here.</summary>
    /// <remarks>
    /// Six identical lamps in a line, so distance is the only thing separating them and the
    /// prediction is exact rather than "some subset". The engine keeps four
    /// (`PixelShaderDoLightingLinear` nests to `nNumLights > 3`), so a fifth arriving would mean the
    /// shader silently ignores it.
    /// </remarks>
    [Test]
    public void Strongest_WithSixCandidates_KeepsTheFourNearest()
    {
        List<BspWorldLight> lights = [];

        foreach (int distance in new[] { 600, 100, 500, 200, 400, 300 })
        {
            lights.Add(Lamp(distance, 0f, 0f));
        }

        Span<LocalLight> into = stackalloc LocalLight[LocalLights.MaximumLocalLights];

        LocalLights.Strongest(lights, 0f, 0f, 0f, into).ShouldBe(4);

        // Strongest first, so the distances come back ascending.
        into[0].X.ShouldBe(100f);
        into[1].X.ShouldBe(200f);
        into[2].X.ShouldBe(300f);
        into[3].X.ShouldBe(400f);
    }

    /// <summary>That a bright far light beats a dim near one, which is why strength is ranked.</summary>
    /// <remarks>
    /// **The control for the test above.** With every lamp identical, "nearest four" and "strongest
    /// four" predict the same set — so that test alone cannot tell a distance sort from a strength
    /// sort. This one separates them: a 100× brighter light at four times the range wins.
    /// </remarks>
    [Test]
    public void Strongest_WithADimNearLampAndABrightFarOne_RanksByStrength()
    {
        List<BspWorldLight> lights =
        [
            Lamp(100f, 0f, 0f, intensity: 1f),
            Lamp(400f, 0f, 0f, intensity: 100f),
        ];

        Span<LocalLight> into = stackalloc LocalLight[LocalLights.MaximumLocalLights];

        LocalLights.Strongest(lights, 0f, 0f, 0f, into).ShouldBe(2);

        into[0].X.ShouldBe(400f, "the brighter light contributes more even from further away");
    }

    /// <summary>That the sun and surface lights never arrive as local lights.</summary>
    /// <remarks>
    /// **A control, not a formality.** `emit_skylight` has no falloff and would be applied at
    /// whatever origin the compiler recorded; `emit_surface` is already resolved into the lightmaps
    /// and the leaf cube, so passing one here double-counts it. Both were measured doing exactly
    /// that on cp_process — see the remarks on `LocalLights.IsLocal`.
    /// </remarks>
    [Test]
    public void Strongest_GivenASkylightAndASurfaceLight_KeepsNeither()
    {
        List<BspWorldLight> lights =
        [
            Lamp(100f, 0f, 0f) with { Kind = WorldLightKind.SkyLight },
            Lamp(120f, 0f, 0f) with { Kind = WorldLightKind.Surface },
            Lamp(140f, 0f, 0f),
        ];

        Span<LocalLight> into = stackalloc LocalLight[LocalLights.MaximumLocalLights];

        LocalLights.Strongest(lights, 0f, 0f, 0f, into).ShouldBe(1);
        into[0].X.ShouldBe(140f);
    }

    /// <summary>That a light with no attenuation at all leaves with vrad's constant of one.</summary>
    /// <remarks>
    /// **The number that was once float.Epsilon and made a reciprocal infinite.** vrad's
    /// `lightmap.cpp` normalises the all-zero case to `constant_attn = 1`; doing it here means no
    /// consumer has to, and a shader dividing by the raw zero would produce infinity rather than a
    /// wrong colour — which is the failure mode that reads as a driver fault.
    /// </remarks>
    [Test]
    public void Strongest_ForALightWithNoAttenuationTerms_NormalisesConstantToOne()
    {
        List<BspWorldLight> lights = [Lamp(100f, 0f, 0f) with
        {
            ConstantAttenuation = 0f, LinearAttenuation = 0f, QuadraticAttenuation = 0f,
        }];

        Span<LocalLight> into = stackalloc LocalLight[LocalLights.MaximumLocalLights];

        LocalLights.Strongest(lights, 0f, 0f, 0f, into).ShouldBe(1);

        into[0].Constant.ShouldBe(1f);
        into[0].Linear.ShouldBe(0f);
        into[0].Quadratic.ShouldBe(0f);
    }

    /// <summary>That real attenuation terms are passed through untouched.</summary>
    /// <remarks>
    /// The control for the normalisation above: with only the all-zero case tested, "normalised the
    /// zeros" and "overwrote every light with 1, 0, 0" are the same observation.
    /// </remarks>
    [Test]
    public void Strongest_ForALightWithAttenuationTerms_PassesThemThrough()
    {
        List<BspWorldLight> lights = [Lamp(100f, 0f, 0f) with
        {
            ConstantAttenuation = 0.5f, LinearAttenuation = 0.25f, QuadraticAttenuation = 0.125f,
        }];

        Span<LocalLight> into = stackalloc LocalLight[LocalLights.MaximumLocalLights];

        LocalLights.Strongest(lights, 0f, 0f, 0f, into).ShouldBe(1);

        into[0].Constant.ShouldBe(0.5f);
        into[0].Linear.ShouldBe(0.25f);
        into[0].Quadratic.ShouldBe(0.125f);
    }

    [Test]
    public void Strongest_ForEveryChosenLight_CarriesItsColourAndPosition()
    {
        List<BspWorldLight> lights = [Lamp(30f, 40f, 50f, intensity: 2f)];

        Span<LocalLight> into = stackalloc LocalLight[LocalLights.MaximumLocalLights];

        LocalLights.Strongest(lights, 0f, 0f, 0f, into).ShouldBe(1);

        (into[0].X, into[0].Y, into[0].Z).ShouldBe((30f, 40f, 50f));
        (into[0].Red, into[0].Green, into[0].Blue).ShouldBe((2f, 2f, 2f));
    }

    [Test]
    public void Strongest_GivenTooLittleRoom_Throws()
    {
        Should.Throw<ArgumentException>(() =>
        {
            LocalLight[] into = new LocalLight[LocalLights.MaximumLocalLights - 1];

            LocalLights.Strongest([], 0f, 0f, 0f, into);
        });
    }

    private static BspWorldLight Lamp(float x, float y, float z, float intensity = 1f) =>
        new()
        {
            Origin = (x, y, z),
            Intensity = (intensity, intensity, intensity),
            Kind = WorldLightKind.Point,
            ConstantAttenuation = 0f,
            LinearAttenuation = 0f,
            QuadraticAttenuation = 1f,
            Radius = 0f,
        };
}
