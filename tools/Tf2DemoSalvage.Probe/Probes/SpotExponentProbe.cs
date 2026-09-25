using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>How many spotlights the installed maps carry, by their penumbra exponent.</summary>
/// <remarks>
/// **An exponent of zero is where Valve's two spotlight paths disagree.** `studiorender.dll`'s CPU light (`0x180021b20`)
/// skips the power when the exponent is 0 or 1, but the vertex shader (`common_vs_fxc.h:785`) raises to it regardless,
/// and `pow( x, 0 )` is one — lighting the whole outside of the cone. Whether any shipped map carries such a light
/// decides whether that difference is visible.
/// </remarks>
public sealed class SpotExponentProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "spot-exponents";

    /// <inheritdoc/>
    public string Summary => "spotlights on the installed maps, by penumbra exponent: spot-exponents [map]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        MapLocator locator = new(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder);

        if (locator.Find(arguments.Count > 0 ? arguments[0] : "koth_harvest_final") is not { } anyMap)
        {
            output.WriteLine("No installed maps found.");
            return;
        }

        string[] maps = arguments.Count > 0
            ? [anyMap]
            : [.. Directory.EnumerateFiles(Path.GetDirectoryName(anyMap) ?? ".", "*.bsp").Order(StringComparer.Ordinal)];

        SortedDictionary<float, int> byExponent = [];
        int spots = 0;
        int points = 0;

        foreach (string map in maps)
        {
            foreach (BspWorldLight light in BspWorldLights.Read(File.ReadAllBytes(map)))
            {
                if (light.Kind == WorldLightKind.Point)
                {
                    points++;
                }

                if (light.Kind != WorldLightKind.Spotlight)
                {
                    continue;
                }

                spots++;
                byExponent[light.Exponent] = byExponent.GetValueOrDefault(light.Exponent) + 1;
            }
        }

        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{maps.Length} maps, {points} point lights, {spots} spotlights"));

        foreach ((float exponent, int count) in byExponent)
        {
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  exponent {exponent,8:0.###}  {count}"));
        }
    }
}
