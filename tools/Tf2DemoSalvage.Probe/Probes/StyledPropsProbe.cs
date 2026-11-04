using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>B424's census: which static props a map's styled world lights reach, through the production list builder.</summary>
/// <remarks>
/// Calls <see cref="LevelLighting.StyledLights"/> itself (`engine.dll` `FUN_1801b6bf0`), at the lighting origin and with the world box
/// <see cref="EntityModelSet.StaticPropHandle"/> carries: the very values production builds its handle from. Every prop the set can draw
/// counts, baked or not. Whether a style animates is the demo's (`lightstyles`); the probe reports styles and leaves that to the reader.
/// </remarks>
public sealed class StyledPropsProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "styledprops";

    /// <inheritdoc/>
    public string Summary => "static props a map's styled world lights reach (B424): styledprops <map> [<map>...]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0)
        {
            output.WriteLine("usage: styledprops <map> [<map>...]");
            return;
        }

        MapLocator maps = new(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder);

        if (maps.FindGameFolder() is not { } game)
        {
            output.WriteLine("no game folder");
            return;
        }

        foreach (string name in arguments)
        {
            if (maps.Find(name) is not { } path)
            {
                output.WriteLine($"{name}: not found");
                continue;
            }

            ReadOnlyMemory<byte> file = File.ReadAllBytes(path);
            IReadOnlyList<BspWorldLight> lights = BspWorldLights.Read(file);
            LevelLighting lighting = new(
                BspLeafTree.Read(file), [], lights, null, NullLogger.Instance, visibility: BspVisibility.Read(file));

            MapAssets assets = MapAssets.Load(file.ToArray(), GameArchives.Open(game), 64, mapName: path);
            Dictionary<int, int> propsByStyle = [];
            int reached = 0;
            int props = 0;

            EntityModelSet models = new()
            {
                Geometry = assets.Geometry,
                StaticPropColours = assets.StaticModelColours,
                StaticPlusDynamicLights = lighting.StaticPlusDynamicLights,
                StaticPropFullLighting = lighting.StaticPropFullLightingAt,
                StaticPropHandle = (_, x, y, z, bounds) =>
                {
                    props++;
                    int[] listed = lighting.StyledLights(x, y, z, bounds);

                    if (listed.Length == 0)
                    {
                        return;
                    }

                    reached++;

                    foreach (int style in listed.Select(index => lights[index].Style).Distinct())
                    {
                        propsByStyle[style] = propsByStyle.GetValueOrDefault(style) + 1;
                    }
                },
            };

            models.StaticProps = assets.StaticModels;
            models.Add([]);
            models.Instances([], [], lighting.ModelLightingAt, lighting.ModelSunAt);

            string styled = string.Join(
                " ", lights.Where(light => light.Style != 0).GroupBy(light => light.Style).OrderBy(group => group.Key)
                    .Select(group => $"{group.Key}x{group.Count()}"));

            output.WriteLine(
                $"{Path.GetFileName(path)}: {lights.Count} lights, styled [{styled}]; {props} props, {reached} listed; " +
                $"props by style [{string.Join(" ", propsByStyle.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}:{pair.Value}"))}]");
        }
    }
}
