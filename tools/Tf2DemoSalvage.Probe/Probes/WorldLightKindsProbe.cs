using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>A map's world lights by kind, and how many of each vrad put in the leaf cubes (B669).</summary>
/// <remarks>
/// The control is the point and spot rows: `leaf_ambient_lighting.cpp:107` asserts only a surface light takes the flag,
/// so a nonzero count on either means the flags were misread.
/// </remarks>
public sealed class WorldLightKindsProbe : IProbe
{
    /// <summary>`IsLeafAmbientSurfaceLight` (`leaf_ambient_lighting.cpp:183-198`): flagged when `brightest / 512² &lt; 0.005`, so style-0 surface lights under this.</summary>
    private const float FlagLimit = 0.005f * 512f * 512f;

    private static float Brightest(BspWorldLight light) =>
        Math.Max(light.Intensity.Red, Math.Max(light.Intensity.Green, light.Intensity.Blue));

    /// <inheritdoc/>
    public string Name => "world-light-kinds";

    /// <inheritdoc/>
    public string Summary => "a map's world lights by kind, with how many are flagged as in the leaf ambient cubes: world-light-kinds <map>";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        MapLocator locator = new(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder);

        if (arguments.Count < 1 || locator.Find(arguments[0]) is not { } path)
        {
            output.WriteLine(Summary);
            return;
        }

        foreach (IGrouping<WorldLightKind, BspWorldLight> kind in BspWorldLights.Read(File.ReadAllBytes(path)).GroupBy(static light => light.Kind).OrderBy(static group => group.Key))
        {
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{kind.Key,-12} {kind.Count(),5}  in the leaf cubes {kind.Count(static light => light.InAmbientCube),5}, " +
                $"outside every cluster {kind.Count(static light => light.Cluster < 0),5}, " +
                $"brightest channel {kind.Min(Brightest):0.0}..{kind.Max(Brightest):0.0}, vrad's rule would flag {kind.Count(static light => light is { Kind: WorldLightKind.Surface, Style: 0 } && Brightest(light) < FlagLimit),5}"));
        }
    }
}
