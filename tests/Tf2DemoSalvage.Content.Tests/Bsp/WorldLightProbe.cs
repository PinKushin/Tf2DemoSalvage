using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// What lights a real map was compiled with.
/// </summary>
/// <remarks>
/// The sun is the one that matters here: models take only the ambient cube today, so anything
/// outdoors renders as though it were in shade (B53). This says whether the map carries a sky
/// light at all, and which way it points.
/// </remarks>
public sealed class WorldLightProbe
{
    [Test]
    [Explicit("Diagnostic. Prints a map's world lights and its sun.")]
    public void WhatLightsDoesTheMapHave()
    {
        string map = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Tf2DemoSalvage",
            "maps",
            "cp_process_f12.bsp");

        if (!File.Exists(map))
        {
            Assert.Ignore($"No map at {map}; open a demo in the viewer first.");
            return;
        }

        IReadOnlyList<BspWorldLight> lights = BspWorldLights.Read(File.ReadAllBytes(map));

        lights.Count.ShouldBeGreaterThan(0);

        foreach (IGrouping<WorldLightKind, BspWorldLight> kind in lights.GroupBy(light => light.Kind))
        {
            TestContext.Out.WriteLine($"LIGHTS {kind.Count(),5}  {kind.Key}");
        }

        if (BspWorldLights.Sun(lights) is { } sun)
        {
            TestContext.Out.WriteLine(
                $"SUN intensity ({sun.Intensity.Red:F1}, {sun.Intensity.Green:F1}, " +
                $"{sun.Intensity.Blue:F1}) direction ({sun.Normal.X:F3}, {sun.Normal.Y:F3}, " +
                $"{sun.Normal.Z:F3})");
        }
        else
        {
            TestContext.Out.WriteLine("SUN none");
        }
    }
}
