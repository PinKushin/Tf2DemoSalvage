using System;
using System.Collections.Generic;
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

    /// <summary>Distance between a light and a point.</summary>
    private static float Distance((float X, float Y, float Z) from, (float X, float Y, float Z) to)
    {
        float dx = from.X - to.X;
        float dy = from.Y - to.Y;
        float dz = from.Z - to.Z;

        return MathF.Max(1f, MathF.Sqrt((dx * dx) + (dy * dy) + (dz * dz)));
    }
}
