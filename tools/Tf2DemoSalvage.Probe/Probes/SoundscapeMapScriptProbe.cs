using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>Which installed maps ship their own <c>scripts/soundscapes_&lt;map&gt;.txt</c>, and what reading it changes.</summary>
/// <remarks>
/// **B465's census, through the production reader.** `C_SoundscapeSystem::Init` appends the map's own script after the
/// manifest's files (`c_soundscape.cpp:306-336`), read with the map's pakfile mounted first; `SoundscapeCatalog.ForLevel`
/// is that reader. For each map this says where the script was found — the pakfile, the install, or both — and how many
/// of the map's soundscape placements name no catalog entry read through the install alone and through the level. The
/// control is the stock count: a map that ships nothing must come out the same both ways.
/// </remarks>
public sealed class SoundscapeMapScriptProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "soundscape-map-scripts";

    /// <inheritdoc/>
    public string Summary =>
        "installed maps shipping their own soundscape script, and the placements it resolves: soundscape-map-scripts [map]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        MapLocator locator = new(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder);

        if (locator.Find(arguments.Count > 0 ? arguments[0] : "koth_harvest_final") is not { } anyMap ||
            locator.FindGameFolder() is not { } folder)
        {
            output.WriteLine("No installed maps or no game found.");
            return;
        }

        string[] maps = arguments.Count > 0
            ? [anyMap]
            : [.. Directory.EnumerateFiles(Path.GetDirectoryName(anyMap) ?? ".", "*.bsp").Order(StringComparer.Ordinal)];

        Func<string, byte[]?> install = GameContent.Open(folder, NullLoggerFactory.Instance).Archives.Read;
        int stock = SoundscapeCatalog.Load(install).Count;

        int inPak = 0;
        int inInstall = 0;
        int changed = 0;
        int resolvedNow = 0;

        foreach (string map in maps)
        {
            byte[] bytes = File.ReadAllBytes(map);
            string name = Path.GetFileNameWithoutExtension(map);
            string script = $"scripts/soundscapes_{name}.txt";
            PakFile pak = PakFile.ReadFrom(bytes);

            bool packed = pak.Contains(script);
            bool installed = install(script) is not null;

            inPak += packed ? 1 : 0;
            inInstall += installed ? 1 : 0;

            IReadOnlyList<BspEntity> entities = BspEntities.ReadFrom(bytes);
            SoundscapeCatalog before = SoundscapeCatalog.Load(install, name);
            SoundscapeCatalog after = SoundscapeCatalog.ForLevel(pak, install, name);

            int unresolvedBefore = SoundscapePlacements.From(entities, before).Placements.Count(placement => placement.Index < 0);
            int unresolvedAfter = SoundscapePlacements.From(entities, after).Placements.Count(placement => placement.Index < 0);

            if (packed || installed || unresolvedBefore != unresolvedAfter)
            {
                changed += unresolvedBefore != unresolvedAfter ? 1 : 0;
                resolvedNow += unresolvedBefore - unresolvedAfter;

                output.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"  {name}: script in {(packed ? "pakfile" : string.Empty)}{(packed && installed ? "+" : string.Empty)}" +
                    $"{(installed ? "install" : string.Empty)}; catalog {before.Count} -> {after.Count}; " +
                    $"unresolved placements {unresolvedBefore} -> {unresolvedAfter}"));
            }
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{maps.Length} maps, stock catalog {stock}: own script in the pakfile {inPak}, in the install {inInstall}; " +
            $"maps whose placements resolve differently {changed}, placements newly resolved {resolvedNow}"));
    }
}
