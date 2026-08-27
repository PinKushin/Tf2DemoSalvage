using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// What numeric range a compiled map's two light lumps actually occupy.
/// </summary>
/// <remarks>
/// **Written to catch a unit mismatch, and it proved there is none** — which is the reason it is
/// kept rather than deleted. The prediction and how it died are both recorded below, because a
/// conclusion without the reasoning that failed is the kind that gets confidently repeated.
///
/// ## The prediction, and what killed it
///
/// vrad's `LightForString` (`utils/vrad/lightmap.cpp:1088`) stores a `light` key like this:
///
/// <code>
/// intensity[0] = pow( r / 255.0, 2.2 ) * 255;    // convert to linear
/// </code>
///
/// The `* 255` says `LUMP_WORLDLIGHTS` is on a nought-to-255 scale, while
/// `LUMP_LEAF_AMBIENT_LIGHTING` is `ColorRGBExp32` read with `TexLightToLinear`, which Valve
/// describes as producing a nought-to-**one** value. `LevelLighting` sums the two — the leaf cube
/// with world lights folded in, plus a sky light the shader adds on top — so they looked like two
/// scales meeting in one expression, with the sun unprotected because `emit_skylight` is
/// directional and gets no distance falloff to absorb the factor.
///
/// **Measured on `cp_process_final`: sky light 2.313, brightest leaf ambient sample 2.938.** The sky
/// light is not 200 and the ambient is not below one. They are on the SAME scale, the mismatch does
/// not exist, and the arithmetic above was reasoning about `light` keys rather than about what a
/// compiled map contains.
///
/// ## What is true instead, and why it is worth asserting
///
/// **Both lumps carry light above white**, which is Valve's overbright range — a lightmap "holds
/// light brighter than white" is exactly what `BspLightmaps` already documents, where the halving
/// into a byte and the shader's doubling implement it. So the range is deliberate and shared.
///
/// The two paths do treat it differently, and that IS real: `BspLightmaps.Overbright` halves on the
/// way into an 8-bit texture, and `BspAmbientLight.Colour` stores a raw float with no equivalent
/// step. That asymmetry is noted in B170 rather than asserted here, because this test's subject is
/// the SCALE the format uses, which stays true however B170 is resolved.
///
/// **The bounds are the assertion.** Requiring both to sit above one and below sixteen would fail
/// against a nought-to-255 scale (a sky light near 200), against a clamped nought-to-one scale
/// (nothing above white anywhere), and against a decode that lost or gained the exponent — which is
/// the failure `BspAmbientLight.Colour` records having shipped once, at 255 times too dark.
/// </remarks>
public sealed class LightScaleConformanceTests
{
    /// <summary>Valve's overbright range: above white, but nowhere near a nought-to-255 scale.</summary>
    private const float AboveWhite = 1f;

    /// <summary>Four doublings past white, which no LDR lump on a shipped map approaches.</summary>
    private const float FarAboveWhite = 16f;

    [Test]
    public void LightLumps_OnACompiledMap_ShareValvesOverbrightRange()
    {
        ReadOnlyMemory<byte> map = MapCache.Bytes();

        IReadOnlyList<BspWorldLight> lights = BspWorldLights.Read(map);

        if (BspWorldLights.Sun(lights) is not { } sun)
        {
            Assert.Ignore($"{MapCache.DefaultMap} has no sky light to measure.");
            return;
        }

        float sky = Largest(sun.Intensity);

        // The control, and it is what makes this an experiment rather than an observation: "the sky
        // light is 2.3" says nothing until there is a second scale to read it against.
        float ambient = 0f;

        foreach (AmbientSamples leaf in BspAmbientLight.Read(map))
        {
            foreach (AmbientSample sample in leaf.Samples)
            {
                ambient = Math.Max(ambient, Largest(sample.Cube));
            }
        }

        TestContext.Out.WriteLine(
            $"LIGHT SCALES on {MapCache.DefaultMap}: sky light {sky:0.###}, " +
            $"brightest leaf ambient sample {ambient:0.###}");

        // **The control first.** With no ambient samples at all the comparison below would pass on
        // an absence rather than on a measurement.
        ambient.ShouldBeGreaterThan(
            0f, "the map must carry leaf ambient light before its scale can be compared to anything");

        sky.ShouldBeInRange(
            AboveWhite,
            FarAboveWhite,
            "a sky light sits in Valve's overbright range; below one would mean the exponent was " +
            "lost, and near 255 would mean LUMP_WORLDLIGHTS was being read on vrad's light-key scale");

        ambient.ShouldBeInRange(
            AboveWhite,
            FarAboveWhite,
            "leaf ambient is TexLightToLinear and shares that range; 255 times too dark is the " +
            "decode failure BspAmbientLight.Colour records having shipped once");
    }

    /// <summary>The largest of three channels, which is what "how big is this value" means here.</summary>
    private static float Largest((float Red, float Green, float Blue) colour) =>
        Math.Max(colour.Red, Math.Max(colour.Green, colour.Blue));

    /// <summary>The largest channel on any face of a cube.</summary>
    private static float Largest(AmbientCube cube) =>
        Math.Max(
            Math.Max(Largest(cube.PositiveX), Largest(cube.NegativeX)),
            Math.Max(
                Math.Max(Largest(cube.PositiveY), Largest(cube.NegativeY)),
                Math.Max(Largest(cube.PositiveZ), Largest(cube.NegativeZ))));
}
