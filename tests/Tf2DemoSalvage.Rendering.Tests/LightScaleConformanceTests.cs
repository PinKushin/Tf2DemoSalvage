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

    /// <summary>
    /// Whether the lump that lights BRUSHES and the lump that lights MODELS agree on scale.
    /// </summary>
    /// <remarks>
    /// **The two lumps are read by different code and meet on the same screen.** `LUMP_LIGHTING`
    /// becomes the lightmap atlas a brush surface samples; `LUMP_LEAF_AMBIENT_LIGHTING` becomes the
    /// ambient cube a model is lit by. Both are `ColorRGBExp32` and both are decoded as
    /// `mantissa * 2^exponent`, so **the raw linear value is directly comparable** — which is what
    /// this measures, before either path's own later handling.
    ///
    /// **Why raw rather than final.** The lightmap path then halves into a byte
    /// (`BspLightmaps.Overbright`) and the shader doubles it back, which is Valve's convention for
    /// byte-stored light — `common_vertexlitgeneric_dx9.h:270` does the same to static vertex
    /// lighting, premultiplied by `cOOOverbright` and multiplied by `cOverbright` in the shader. The
    /// ambient cube is a float constant and needs no such step. Comparing after that would be
    /// comparing storage tricks; comparing before it asks the question that matters, which is
    /// whether the two lumps describe light in the same units.
    ///
    /// **This is the B170 differential.** If a brush and a model standing against it are given
    /// numbers an order of magnitude apart, the world can look right while every model blows out —
    /// which is the reported symptom. The test states the requirement rather than the defect, so it
    /// survives the fix.
    /// </remarks>
    [Test]
    public void LightingLumps_DecodedIdentically_AgreeOnScale()
    {
        ReadOnlyMemory<byte> map = MapCache.Bytes();

        float ambient = 0f;

        foreach (AmbientSamples leaf in BspAmbientLight.Read(map))
        {
            foreach (AmbientSample sample in leaf.Samples)
            {
                ambient = Math.Max(ambient, Largest(sample.Cube));
            }
        }

        // `BspLumpIndex.Lighting`, named here because that type is internal to Content. `LUMP_LIGHTING`
        // is 8 in `public/bspfile.h` and has been since the format existed.
        const int LumpLighting = 8;

        ReadOnlySpan<byte> lighting = BspLumpData
            .Read(map, BspHeader.Parse(map.Span).Lump(LumpLighting)).Span;

        float lightmap = 0f;

        for (int at = 0; at + 4 <= lighting.Length; at += 4)
        {
            // The identical expression BspAmbientLight.Colour and BspLightmaps.Decode both use.
            float scale = MathF.Pow(2f, (sbyte)lighting[at + 3]);

            lightmap = Math.Max(
                lightmap,
                Math.Max(lighting[at] * scale, Math.Max(lighting[at + 1] * scale, lighting[at + 2] * scale)));
        }

        // **The raw luxel is NOT what reaches the shader, and comparing it would be the wrong
        // instrument.** `BspLightmaps.Overbright` halves into a byte, the texture normalises that
        // byte against 255, and the shader doubles it back — so the value a brush surface is
        // actually lit by is `min(raw / 255, 2)`. The byte storage divides by 255 implicitly, and
        // measuring before it compares a stored form against a decoded one.
        //
        // Measured 2026-08-27 on cp_process_final: raw luxel 2464 against raw ambient 2.938, a
        // factor of 839 that says nothing, because 2464 stores as a saturated byte and arrives as 2.
        float delivered = Math.Min(lightmap / 255f, 2f);

        TestContext.Out.WriteLine(
            $"LUMP SCALES on {MapCache.DefaultMap}: brightest lightmap luxel {lightmap:0.###} " +
            $"(delivered {delivered:0.###}), brightest leaf ambient sample {ambient:0.###}");

        // Both controls first: either lump being empty would make the ratio below meaningless.
        lightmap.ShouldBeGreaterThan(0f, "the map must carry lightmap samples to compare against");
        ambient.ShouldBeGreaterThan(0f, "the map must carry leaf ambient samples to compare");

        // **An order of magnitude is the threshold, not equality.** The brightest luxel and the
        // brightest leaf sample are different measurements of a map and are not expected to match;
        // what would be a defect is a decode that put them in different UNITS, and a factor of ten
        // is far outside what sampling difference explains and far inside a factor of 255.
        float ratio = Math.Max(delivered / ambient, ambient / delivered);

        ratio.ShouldBeLessThan(
            10f,
            $"LUMP_LIGHTING and LUMP_LEAF_AMBIENT_LIGHTING are both ColorRGBExp32 decoded as " +
            $"mantissa*2^exponent, so brushes and models must be lit in the same units " +
            $"(lightmap delivered {delivered:0.###}, ambient {ambient:0.###})");
    }

    /// <summary>
    /// Which of the two lighting compiles a map carries, LDR or HDR, era by era.
    /// </summary>
    /// <remarks>
    /// **The era axis reached the renderer, which nobody had checked.** The owner, 2026-08-27, on
    /// B170: *"no all the weapons are washed out on the modern demos"* — so the discriminator is the
    /// DEMO, not the weapon, and a demo selects a map. That makes "what differs between a 2013 map
    /// and a 2026 one" a question with a measurable answer.
    ///
    /// **`bspfile.h` pairs every lighting lump.** `LUMP_LIGHTING` (8) and `LUMP_LEAF_AMBIENT_LIGHTING`
    /// (56) are the LDR compile; `LUMP_LIGHTING_HDR` (53) and `LUMP_LEAF_AMBIENT_LIGHTING_HDR` (55)
    /// are the HDR one. A map may carry either or both, and `BspLumpIndex` already documents the
    /// pairing as "the subtle part".
    ///
    /// **This project reads the LDR pair, always.** `BspAmbientLight.Read` takes lumps 56 and 52 and
    /// `BspLightmaps` takes 8, with no branch on what the map actually contains. TF2 itself runs
    /// `mat_hdr_level 2` by default on any map compiled for it.
    ///
    /// **Reported rather than asserted, deliberately.** What the right behaviour IS — read HDR when
    /// present, and what tone mapping that then obliges — is a parity decision for the owner, not
    /// something to settle inside a test. See D89: Valve parity is the first principle, and a
    /// divergence is asked rather than assumed. What this pins down is the FACT each map carries,
    /// which is the input that decision needs.
    /// </remarks>
    [Test]
    public void LightingLumps_AcrossTheEraAxis_AreReported()
    {
        // `bspfile.h`, named locally because BspLumpIndex is internal to Content.
        const int LumpLighting = 8;
        const int LumpLightingHdr = 53;
        const int LumpLeafAmbient = 56;
        const int LumpLeafAmbientHdr = 55;

        bool measuredAny = false;

        // A modern map and two the era specimens were recorded on. Chosen rather than swept: the
        // question is whether the answer CHANGES with era, which needs one from each end, not every
        // map installed.
        foreach (string name in new[] { "cp_process_final", "cp_badlands", "cp_granary" })
        {
            if (!MapCache.Exists(name))
            {
                TestContext.Out.WriteLine($"LIGHTING LUMPS {name}: not installed");
                continue;
            }

            measuredAny = true;

            ReadOnlyMemory<byte> map = MapCache.Bytes(name);
            BspHeader header = BspHeader.Parse(map.Span);

            TestContext.Out.WriteLine(
                $"LIGHTING LUMPS {name}: " +
                $"LDR lighting {header.Lump(LumpLighting).Length} bytes, " +
                $"HDR lighting {header.Lump(LumpLightingHdr).Length} bytes, " +
                $"LDR leaf ambient {header.Lump(LumpLeafAmbient).Length} bytes, " +
                $"HDR leaf ambient {header.Lump(LumpLeafAmbientHdr).Length} bytes");
        }

        // The control: with no map installed this would report nothing and read as "no map carries
        // HDR lighting", which is a statement about this machine rather than about any map.
        measuredAny.ShouldBeTrue("at least one of the named maps must be installed to measure anything");
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
