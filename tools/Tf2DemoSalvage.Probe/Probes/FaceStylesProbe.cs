using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>Which light styles the installed maps' faces carry beyond style 0.</summary>
/// <remarks>
/// **A face lit by a flickering or switchable light stores a lightmap per style**, and the engine adds each one times
/// its style's current value (`d_lightstylevalue`). Styles 1 to 12 are the animated set the game defines; 32 and up are
/// switchable, a named light's. This counts how many faces on how many maps carry any of them, which decides whether
/// drawing style 0 alone is visible.
/// </remarks>
public sealed class FaceStylesProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "face-styles";

    /// <inheritdoc/>
    public string Summary => "light styles the installed maps' faces carry beyond 0: face-styles [map]";

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

        SortedDictionary<int, (int Faces, HashSet<string> Maps)> byStyle = [];
        int faces = 0;

        foreach (string map in maps)
        {
            BspLightSamples samples = BspLightmaps.ReadSamples(File.ReadAllBytes(map));
            string name = Path.GetFileNameWithoutExtension(map);

            for (int face = 0; samples.Layout(face) is { } layout; face++)
            {
                faces++;
                (byte a, byte b, byte c, byte d) = layout.Styles;

                foreach (byte style in new[] { a, b, c, d })
                {
                    if (style is 0 or 255)
                    {
                        continue;
                    }

                    (int count, HashSet<string> on) = byStyle.GetValueOrDefault(style, (0, []));

                    on.Add(name);
                    byStyle[style] = (count + 1, on);
                }
            }
        }

        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{maps.Length} maps, {faces} faces"));

        foreach ((int style, (int count, HashSet<string> on)) in byStyle)
        {
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  style {style,3}  {count,7} faces on {on.Count,3} maps  e.g. {string.Join(", ", on.Order(StringComparer.Ordinal).Take(3))}"));
        }
    }
}
