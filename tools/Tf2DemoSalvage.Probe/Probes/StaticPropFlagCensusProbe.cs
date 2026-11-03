using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>Which maps place a static prop whose model is not compiled <c>$staticprop</c> as the game ships it (B674).</summary>
/// <remarks>
/// vbsp deletes such a prop at compile (<c>staticprop.cpp:170-180</c>), so one on disk means the model changed since the map was built, the one way to reach flags 7 with no bump flag.
/// **The control:** a total of placements read, and a player model, which must read as not flagged. A model no archive holds is counted apart.
/// </remarks>
public sealed class StaticPropFlagCensusProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "static-prop-flag-census";

    /// <inheritdoc/>
    public string Summary => "every installed map's static props whose model is not flagged $staticprop, with a control total: static-prop-flag-census [map substring]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        MapLocator locator = new(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder);

        if (locator.FindGameFolder() is not { } folder)
        {
            output.WriteLine("No game folder.");
            return;
        }

        string filter = arguments.Count > 0 ? arguments[0] : string.Empty;
        GameArchives archives = GameArchives.Open(folder);
        Dictionary<string, int?> shipped = new(StringComparer.OrdinalIgnoreCase);
        (int Maps, int Placements, int Flagged, int NotFlagged, int Missing) total = default;

        foreach (string map in Directory.EnumerateFiles(Path.Combine(folder, "maps"), "*.bsp").Order(StringComparer.Ordinal))
        {
            if (!Path.GetFileName(map).Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            byte[] bytes = File.ReadAllBytes(map);
            PakFile pak = PakFile.ReadFrom(bytes);
            List<string> strays = [];

            total.Maps++;

            foreach (string model in BspStaticProps.Read(bytes).Select(static prop => prop.Model))
            {
                total.Placements++;

                // A map's own copy overrides the game's, and is per map, so it is not kept across maps.
                int? flags = pak.ReadFile(model) is { Length: > 0 } own ? FlagsOf(own) : Shipped(model);

                switch (flags)
                {
                    case null:
                        total.Missing++;
                        break;
                    case { } value when StaticPropVertexLighting.Lights(value):
                        total.Flagged++;
                        break;
                    default:
                        total.NotFlagged++;
                        strays.Add(model);
                        break;
                }
            }

            if (strays.Count > 0)
            {
                output.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{Path.GetFileName(map)}: {strays.Count} placements, {string.Join(", ", strays.Distinct(StringComparer.OrdinalIgnoreCase).Take(5))}"));
            }
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{total.Maps} maps, {total.Placements} placements: {total.Flagged} flagged, {total.NotFlagged} not, {total.Missing} whose model no archive holds"));

        // A model that must NOT be flagged, or "not 0" cannot be read as an absence.
        const string NotStatic = "models/player/scout.mdl";
        output.WriteLine($"control: {NotStatic} flagged $staticprop: {(Shipped(NotStatic) is { } scout ? StaticPropVertexLighting.Lights(scout).ToString() : "not found")}");

        int? Shipped(string path)
        {
            if (!shipped.TryGetValue(path, out int? flags))
            {
                flags = archives.Read(path) is { Length: > 0 } file ? FlagsOf(file) : null;
                shipped[path] = flags;
            }

            return flags;
        }
    }

    private static int FlagsOf(byte[] file) => StudioModel.Read(file).Flags;
}
