using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>What the direct lights actually add, and why it is so little.</summary>
/// <remarks>
/// **Measured on the viewer first, which is what this exists to explain.** Reporting the two light
/// terms apart across a whole map gave, at every sampled place:
///
/// <code>
/// bounce 0.0723, with direct 0.0732      bounce 0.2561, with direct 0.2562
/// bounce 0.1875, with direct 0.1928      bounce 0.1677, with direct 0.1679
/// </code>
///
/// A direct term of under three per cent, usually under one, from 136 world lights. `LocalLights` is
/// wired in and has a dozen tests, so the question is not whether it runs — it is whether so little
/// is CORRECT. Two readings fit that number and they need opposite fixes:
///
/// - most of a map's lights are surface lights, which carry no falloff and are rightly excluded as
///   non-runtime, so a near-zero direct term is honest and the darkness lies in the ambient cube;
/// - or eligible lights are present and near, and something in selection or falloff is throwing
///   their contribution away.
///
/// A diagnostic rather than a check, because what it reports is a property of one map rather than of
/// the code. It runs on `cp_process_f12` because that is the map on this machine.
/// </remarks>
public sealed class LocalLightContributionProbe
{
    /// <summary>The map the symptom was seen on, then the one kept beside the corpus.</summary>
    /// <remarks>
    /// **`koth_harvest_final` first, because that is where the measurement was made.** Answering a
    /// question about one map by examining a different one is how a correct measurement ends up
    /// describing the wrong thing.
    /// </remarks>
    private static IEnumerable<string> Candidates =>
    [
        @"F:\SteamLibrary\steamapps\common\Team Fortress 2\tf\maps\koth_harvest_final.bsp",
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Tf2DemoSalvage", "maps", "cp_process_f12.bsp"),
    ];

    [Test]
    [Explicit("diagnostic")]
    public void LocalLights_WhatTheyAddOnARealMap_IsReported()
    {
        if (Candidates.FirstOrDefault(File.Exists) is not { } mapPath)
        {
            Assert.Ignore("no map to read on this machine.");
            return;
        }

        TestContext.Out.WriteLine(Path.GetFileName(mapPath));

        IReadOnlyList<BspWorldLight> lights = BspWorldLights.Read(File.ReadAllBytes(mapPath));

        TestContext.Out.WriteLine($"{lights.Count} world lights");

        foreach (IGrouping<WorldLightKind, BspWorldLight> kind in lights.GroupBy(light => light.Kind))
        {
            // **How many are eligible at all is the first fork.** A surface light has no falloff
            // terms, so it cannot be evaluated at runtime and the engine bakes it instead.
            int withFalloff = kind.Count(light =>
                light.ConstantAttenuation != 0f ||
                light.LinearAttenuation != 0f ||
                light.QuadraticAttenuation != 0f);

            TestContext.Out.WriteLine(
                $"  {kind.Key}: {kind.Count()}, {withFalloff} with a falloff");
        }

        // What the brightest few look like, since intensity is the term whose UNITS are least
        // obvious: a light stored in a scale we mis-assume produces exactly this symptom, a
        // contribution that is present, ordered correctly, and far too small.
        foreach (BspWorldLight light in lights
            .Where(light => light.Kind != WorldLightKind.SkyAmbient)
            .OrderByDescending(light =>
                light.Intensity.Red + light.Intensity.Green + light.Intensity.Blue)
            .Take(6))
        {
            TestContext.Out.WriteLine(
                $"  {light.Kind} at ({light.Origin.X:0},{light.Origin.Y:0},{light.Origin.Z:0}) " +
                $"rgb ({light.Intensity.Red:0.###},{light.Intensity.Green:0.###}," +
                $"{light.Intensity.Blue:0.###}) radius {light.Radius:0} " +
                $"attn ({light.ConstantAttenuation:0.###},{light.LinearAttenuation:0.###}," +
                $"{light.QuadraticAttenuation:0.###})");
        }

        // **What the nearest lights do at a place a player actually stood.** This is the spy of
        // z1800 at tick 47601, whose gloves prompted all of this. Reporting distance, cone dot and
        // the two stop cosines together is what separates "nothing is near" from "everything near is
        // being rejected by the cone".
        //
        // Written out per light rather than summarised, because the question is which TERM kills the
        // contribution and a total cannot say.
        (float X, float Y, float Z) where = (-232f, -1896f, 72f);

        foreach (BspWorldLight light in lights
            .Where(light => light.Kind is WorldLightKind.Spotlight or WorldLightKind.Point)
            .OrderBy(light => Distance(light.Origin, where))
            .Take(6))
        {
            float distance = Distance(light.Origin, where);

            // From the light TOWARDS the point, which is the direction the cone test compares
            // against: `rdir.Dot( m_Direction ) >= m_PhiDot` (lightdesc.h:102).
            (float X, float Y, float Z) toward = (
                (where.X - light.Origin.X) / distance,
                (where.Y - light.Origin.Y) / distance,
                (where.Z - light.Origin.Z) / distance);

            float cone = (toward.X * light.Normal.X) +
                (toward.Y * light.Normal.Y) +
                (toward.Z * light.Normal.Z);

            float falloff = light.ConstantAttenuation +
                (light.LinearAttenuation * distance) +
                (light.QuadraticAttenuation * distance * distance);

            TestContext.Out.WriteLine(
                $"  {light.Kind} {distance:0} units away: " +
                $"cone dot {cone:0.###} against stop {light.StopDot:0.###}/{light.StopDot2:0.###}, " +
                $"falloff {falloff:0} gives {light.Intensity.Red / falloff:0.####} " +
                $"{(cone >= light.StopDot2 ? "INSIDE the cone" : "outside the cone")}");
        }

        lights.ShouldNotBeEmpty("no lights were read, so nothing above was measured");
    }

    [Test]
    [Explicit("diagnostic")]
    public void ModelLightingAgainstTheLightmap_OnARealMap_IsReported()
    {
        if (Candidates.FirstOrDefault(File.Exists) is not { } mapPath)
        {
            Assert.Ignore("no map to read on this machine.");
            return;
        }

        byte[] file = File.ReadAllBytes(mapPath);

        // **The arbiter, and it needs nothing from the engine.** The brushes in the room where the
        // symptom was seen are lit by the same lamps, decoded by this project, and look correct on
        // screen. So the lightmap is a known-good reference for how bright that place should be, and
        // the ambient cube is what a MODEL gets there. Both lumps are `ColorRGBExp32` and both are
        // decoded as `mantissa * 2^exponent`, so the two numbers are in one space and comparable.
        //
        // Whole-map distributions rather than one point, deliberately: a single face and a single
        // leaf could differ for a dozen honest reasons, whereas a systematic factor between the two
        // populations is the thing being tested for.
        List<float> lightmap = [];

        foreach (BspLightmap map in BspLightmaps.Read(file))
        {
            ReadOnlySpan<byte> pixels = map.Pixels.Span;

            for (int at = 0; at + 3 < pixels.Length; at += 4)
            {
                // The stored byte is `linear / 2` clamped, so doubling recovers the linear value —
                // Valve's overbright, which the shader undoes the same way.
                lightmap.Add(((pixels[at] + pixels[at + 1] + pixels[at + 2]) / 3f) * 2f);
            }
        }

        List<float> cubes = [];

        foreach (AmbientSamples leaf in BspAmbientLight.Read(file))
        {
            foreach (AmbientSample sample in leaf.Samples)
            {
                cubes.Add(AmbientCube.Luminance(sample.Cube));
            }
        }

        Report("lightmap luxels", lightmap);
        Report("ambient cube samples", cubes);

        if (Median(lightmap) is > 0f and { } lit && Median(cubes) is > 0f and { } cube)
        {
            TestContext.Out.WriteLine(
                $"  median lightmap is {lit / cube:0.#}x the median ambient cube");
        }

        // Controls: an empty lump reads as "no difference" and would otherwise pass silently.
        lightmap.ShouldNotBeEmpty("no lightmap samples were read");
        cubes.ShouldNotBeEmpty("no ambient samples were read");
    }

    [Test]
    [Explicit("diagnostic")]
    public void WorldLightIntensity_AgainstTheAuthoredValue_IsReported()
    {
        if (Candidates.FirstOrDefault(File.Exists) is not { } mapPath)
        {
            Assert.Ignore("no map to read on this machine.");
            return;
        }

        byte[] file = File.ReadAllBytes(mapPath);

        // **The entity lump is plain text and holds what the mapper actually wrote**, which makes it
        // the one reference that can say whether our decoded intensity is the right magnitude. vrad
        // turns `_light "R G B brightness"` into
        //
        //     intensity = pow( channel / 255, 2.2 ) * 255,  then  * ( brightness / 255 )
        //
        // and then scales the whole thing by the falloff denominator at a hundred units
        // (`lightmap.cpp:1253`). Dividing our stored value by that ratio should give the authored
        // number back; if it does not, the gap is the factor the runtime term is short by.
        int lights = 0;

        foreach (BspEntity entity in BspEntities.ReadFrom(file))
        {
            if (!entity.TryGetValue("classname", out string? classname) ||
                classname is not ("light" or "light_spot") ||
                !entity.TryGetValue("_light", out string? authored))
            {
                continue;
            }

            if (lights++ < 6)
            {
                // **`_lightHDR` wins when the map was compiled with HDR, which TF2's are**
                // (`lightmap.cpp:1133`), and a negative value there means "no HDR override, use
                // `_light`". Printing both is what tells the two cases apart.
                entity.TryGetValue("_lightHDR", out string? hdr);
                entity.TryGetValue("_lightscaleHDR", out string? hdrScale);

                TestContext.Out.WriteLine(
                    $"  authored _light \"{authored}\" _lightHDR \"{hdr ?? "-"}\" " +
                    $"_lightscaleHDR \"{hdrScale ?? "-"}\"");
            }
        }

        TestContext.Out.WriteLine($"  {lights} light entities in the lump");

        // **Joined on ORIGIN, because guessing the pairing produced nonsense.** Matching the
        // brightest decoded light against the first authored one gave per-channel factors of 102,
        // 90 and 78 — not a constant, which is the signature of comparing two different lamps
        // rather than of a scale error. A light entity and its `dworldlight_t` share a position, so
        // that is the join.
        Dictionary<(int X, int Y, int Z), string> authoredAt = [];

        foreach (BspEntity entity in BspEntities.ReadFrom(file))
        {
            if (entity.TryGetValue("classname", out string? name) &&
                name is "light" or "light_spot" &&
                entity.TryGetValue("_light", out string? value) &&
                entity.TryGetValue("origin", out string? origin))
            {
                float[] parts =
                [
                    .. origin.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                        .Select(part => float.Parse(part, CultureInfo.InvariantCulture)),
                ];

                if (parts.Length == 3)
                {
                    authoredAt[((int)parts[0], (int)parts[1], (int)parts[2])] = value;
                }
            }
        }

        int compared = 0;

        foreach (BspWorldLight decoded in BspWorldLights.Read(file)
            .Where(entry => entry.Kind is WorldLightKind.Spotlight or WorldLightKind.Point))
        {
            if (compared >= 5 ||
                !authoredAt.TryGetValue(
                    ((int)decoded.Origin.X, (int)decoded.Origin.Y, (int)decoded.Origin.Z),
                    out string? authored))
            {
                continue;
            }

            compared++;

            float ratio = decoded.ConstantAttenuation +
                (100f * decoded.LinearAttenuation) +
                (100f * 100f * decoded.QuadraticAttenuation);

            // vrad's own arithmetic, so the expected value is computed rather than eyeballed:
            // pow( channel / 255, 2.2 ) * 255, then * ( brightness / 255 ).
            float[] rgb =
            [
                .. authored.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Select(part => float.Parse(part, CultureInfo.InvariantCulture)),
            ];

            float brightness = rgb.Length > 3 ? rgb[3] / 255f : 1f;
            float expected = MathF.Pow(rgb[0] / 255f, 2.2f) * 255f * brightness;
            float ours = decoded.Intensity.Red / ratio;

            TestContext.Out.WriteLine(
                $"  at ({decoded.Origin.X:0},{decoded.Origin.Y:0},{decoded.Origin.Z:0}) " +
                $"authored \"{authored}\" expects {expected:0.##}, we read {ours:0.###} " +
                $"— factor {(ours > 0 ? expected / ours : 0):0.#}");
        }

        compared.ShouldBeGreaterThan(0, "no decoded light matched an entity by origin");
    }

    /// <summary>Prints a distribution, since a mean alone hides a clamp or a long tail.</summary>
    private static void Report(string what, List<float> values)
    {
        if (values.Count == 0)
        {
            TestContext.Out.WriteLine($"{what}: none");
            return;
        }

        float[] sorted = [.. values.Order()];

        TestContext.Out.WriteLine(
            $"{what}: {sorted.Length:N0} values, " +
            $"min {sorted[0]:0.####}, " +
            $"median {sorted[sorted.Length / 2]:0.####}, " +
            $"90th {sorted[(int)(sorted.Length * 0.9)]:0.####}, " +
            $"max {sorted[^1]:0.####}, " +
            $"mean {values.Average():0.####}");
    }

    /// <summary>The median, or zero for an empty set.</summary>
    private static float Median(List<float> values)
    {
        if (values.Count == 0)
        {
            return 0f;
        }

        float[] sorted = [.. values.Order()];

        return sorted[sorted.Length / 2];
    }

    /// <summary>Distance between a light and a point.</summary>
    private static float Distance((float X, float Y, float Z) from, (float X, float Y, float Z) to)
    {
        float dx = from.X - to.X;
        float dy = from.Y - to.Y;
        float dz = from.Z - to.Z;

        return MathF.Max(1f, MathF.Sqrt((dx * dx) + (dy * dy) + (dz * dz)));
    }
}
