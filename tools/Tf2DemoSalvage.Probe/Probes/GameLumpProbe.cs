using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// Which game sub-lumps a map carries, and which of them this project reads.
/// </summary>
/// <remarks>
/// **Lump 35 holds several lumps of its own, and only one of them is read here.** `sprp` is the
/// static props; `dprp` is the DETAIL props — the grass, weeds and clutter `vbsp` scatters from a
/// material's `%detailtype`. Nothing in this project has ever opened the second, and a probe that
/// lists the directory is the cheapest way to see what else is in there.
///
/// **The count is the interesting column, not the presence.** A map that declares `dprp` with an
/// empty payload is a map with no detail props, which is a different answer from a map this reader
/// cannot open.
///
/// <code>
///   game-lumps koth_harvest_final
/// </code>
/// </remarks>
public sealed class GameLumpProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "game-lumps";

    /// <inheritdoc/>
    public string Summary => "which game sub-lumps a map carries: game-lumps <map>";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count < 1)
        {
            output.WriteLine("game-lumps <map>");
            return;
        }

        if (new MapLocator(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder)
            .Find(arguments[0]) is not { } path)
        {
            output.WriteLine($"No map named '{arguments[0]}'.");
            return;
        }

        ReadOnlyMemory<byte> file = File.ReadAllBytes(path);

        IReadOnlyList<BspGameLumpEntry> entries = BspGameLumps.Directory(file);

        output.WriteLine($"{Path.GetFileName(path)}: {entries.Count} game sub-lumps");

        foreach (BspGameLumpEntry entry in entries)
        {
            int packed = entry.PackedEnd - entry.Offset;

            string compressed = packed < entry.StoredLength ? "  (COMPRESSED)" : string.Empty;

            output.WriteLine(
                $"  '{entry.Name}' at " +
                entry.Offset.ToString("N0", CultureInfo.InvariantCulture) + ", version " +
                entry.Version.ToString(CultureInfo.InvariantCulture) + ", flags 0x" +
                entry.Flags.ToString("X", CultureInfo.InvariantCulture) + ", " +
                entry.StoredLength.ToString("N0", CultureInfo.InvariantCulture) +
                " bytes stored, " +
                packed.ToString("N0", CultureInfo.InvariantCulture) + " on disk" + compressed);

            // **The first integer of a detail-prop payload is its object count**, which is the one
            // number that says whether implementing it would change any picture.
            if (!string.Equals(entry.Name, "dprp", StringComparison.Ordinal))
            {
                continue;
            }

            ReadOnlyMemory<byte> payload = BspGameLumps.Payload(file, entry);

            output.WriteLine(
                $"      detail props: {BspDetailProps.Count(payload).ToString("N0", CultureInfo.InvariantCulture)}");

            // **The type split decides what drawing them would cost.** A sprite needs a quad
            // builder and the one shared `detail/detailsprites` material; a model goes through the
            // path static props already use. Reporting the split rather than the total is what
            // separates "a day of work" from "an afternoon".
            (IReadOnlyList<string> models, IReadOnlyList<BspDetailSprite> sprites,
                IReadOnlyList<BspDetailProp> objects) = BspDetailProps.Read(file);

            Dictionary<DetailPropType, int> byType = [];

            foreach (DetailPropType type in objects.Select(prop => prop.Type))
            {
                byType[type] = byType.TryGetValue(type, out int seen) ? seen + 1 : 1;
            }

            output.WriteLine(
                $"      dictionaries: {models.Count} models, {sprites.Count} sprite rectangles");

            foreach ((DetailPropType type, int count) in byType)
            {
                output.WriteLine(
                    $"        {type}: {count.ToString("N0", CultureInfo.InvariantCulture)}");
            }

            // **Orientation decides whether a quad can be built once or must face the camera.**
            // `DETAIL_PROP_ORIENT_NORMAL` is a fixed quad and could go in a static buffer;
            // the two screen-aligned kinds have to be turned every frame, which is the difference
            // between reusing the world's geometry path and needing a billboard of its own.
            Dictionary<int, int> byOrientation = [];

            foreach (int orientation in objects.Select(prop => prop.Orientation))
            {
                byOrientation[orientation] =
                    byOrientation.TryGetValue(orientation, out int seen) ? seen + 1 : 1;
            }

            foreach ((int orientation, int count) in byOrientation)
            {
                string named = orientation switch
                {
                    0 => "NORMAL (fixed)",
                    1 => "SCREEN_ALIGNED",
                    2 => "SCREEN_ALIGNED_VERTICAL",
                    _ => "unknown",
                };

                output.WriteLine(
                    $"        orientation {named}: " +
                    count.ToString("N0", CultureInfo.InvariantCulture));
            }

            foreach (string model in models)
            {
                output.WriteLine($"        model '{model}'");
            }
        }
    }
}
