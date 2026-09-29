using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>B424's census: which static props a map's styled world lights reach, through the production list builder.</summary>
/// <remarks>
/// Calls <see cref="LevelLighting.StyledLights"/> itself (`engine.dll` `FUN_1801b6bf0`). A prop is asked at its lump
/// lighting origin when flagged and otherwise at its lump origin — the illumination point needs the model, so an
/// unflagged prop is measured a few units off where the viewer asks. Every prop counts, baked or not.
/// Whether a style animates is the demo's (`lightstyles`); the probe reports styles and leaves that to the reader.
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

            IReadOnlyList<BspStaticProp> props = BspStaticProps.Read(file);
            Dictionary<int, int> propsByStyle = [];
            int reached = 0;

            foreach (BspStaticProp prop in props)
            {
                (float x, float y, float z) = prop.UsesLightingOrigin ? prop.LightingOrigin : (prop.X, prop.Y, prop.Z);
                int[] listed = lighting.StyledLights(x, y, z);

                if (listed.Length == 0)
                {
                    continue;
                }

                reached++;

                foreach (int style in listed.Select(index => lights[index].Style).Distinct())
                {
                    propsByStyle[style] = propsByStyle.GetValueOrDefault(style) + 1;
                }
            }

            string styled = string.Join(
                " ", lights.Where(light => light.Style != 0).GroupBy(light => light.Style).OrderBy(group => group.Key)
                    .Select(group => $"{group.Key}x{group.Count()}"));

            output.WriteLine(
                $"{Path.GetFileName(path)}: {lights.Count} lights, styled [{styled}]; {props.Count} props, {reached} listed; " +
                $"props by style [{string.Join(" ", propsByStyle.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}:{pair.Value}"))}]");
        }
    }
}
