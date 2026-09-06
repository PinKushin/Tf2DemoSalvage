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
/// Which maps override the detail prop distances, and to what.
/// </summary>
/// <remarks>
/// **A map can cut `cl_detaildist` and `cl_detailfade`, and only cut them.** An
/// `env_detail_controller` in the entity lump makes
/// `CDetailObjectSystem::LevelInitPostEntity` (<c>detailobjectsystem.cpp:1524</c>) write both
/// cvars as <c>MIN(the config's value, the map's)</c>.
///
/// **This probe exists because the census decides whether the feature is visible.** The entity is
/// not in any FGD TF2 ships, so a mapper cannot pick it out of Hammer's list — which makes "how
/// many maps carry one anyway" a question about real files rather than about the engine. With no
/// argument it reads every installed map and reports only those that carry one; with a map name it
/// reports that map alone, including the absence.
///
/// **The reported numbers are the map's own keys, not the resulting cvars**, because the cvars
/// depend on a config this probe is not reading. The `MIN` is stated beside them so a wrong
/// reading of which value wins is recognisable.
///
/// <code>
///   detail-controller
///   detail-controller koth_harvest_final
/// </code>
/// </remarks>
public sealed class DetailControllerProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "detail-controller";

    /// <inheritdoc/>
    public string Summary => "maps that override cl_detaildist: detail-controller [map]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        MapLocator locator = new(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder);

        if (arguments.Count > 0)
        {
            if (locator.Find(arguments[0]) is not { } named)
            {
                output.WriteLine($"No map named '{arguments[0]}'.");
                return;
            }

            Report(output, named, alwaysReport: true);
            return;
        }

        // The folder is taken from a map the locator found rather than rebuilt, so the census and
        // a single lookup can never disagree about which install they read.
        if (locator.Find("koth_harvest_final") is not { } anyMap)
        {
            output.WriteLine("No installed maps found.");
            return;
        }

        string folder = Path.GetDirectoryName(anyMap)!;
        string[] maps = [.. Directory.EnumerateFiles(folder, "*.bsp").Order(StringComparer.Ordinal)];

        int carried = 0;
        int read = 0;

        foreach (string map in maps)
        {
            (bool controller, bool worldspawn) = Report(output, map, alwaysReport: false);
            carried += controller ? 1 : 0;
            read += worldspawn ? 1 : 0;
        }

        output.WriteLine(
            $"{carried} of {maps.Length} installed maps carry an {BspEntities.DetailControllerClass}.");

        // **The control, because zero is the expected answer here and a broken reader gives the
        // same one.** Every compiled map has exactly one `worldspawn`, so this number must equal
        // the map count; anything less means the entity lump was not read, not that the entity is
        // absent.
        output.WriteLine($"{read} of {maps.Length} yielded a worldspawn (the control; must match).");
    }

    private static (bool Controller, bool Worldspawn) Report(
        TextWriter output, string path, bool alwaysReport)
    {
        string name = Path.GetFileNameWithoutExtension(path);

        IReadOnlyList<BspEntity> entities;

        try
        {
            entities = BspEntities.ReadFrom(File.ReadAllBytes(path));
        }
        catch (InvalidDataException failure)
        {
            // A map this reader cannot open is reported rather than skipped: an unreadable file
            // counted as "carries none" would understate the census silently.
            output.WriteLine($"{name}: unreadable — {failure.Message}");
            return (false, false);
        }

        bool worldspawn = entities.Any(entity =>
            string.Equals(entity.ClassName, "worldspawn", StringComparison.OrdinalIgnoreCase));

        // **`worldspawn`'s other say over the detail props**, read here because it arrives at the
        // same moment and from the same block. `CDetailObjectSystem::LevelInitPostEntity` starts
        // with it: `DETAIL_SPRITE_MATERIAL` unless `pWorld->GetDetailSpriteMaterial()` is a
        // non-empty string (`detailobjectsystem.cpp:1516`), which `worldspawn`'s `detailmaterial`
        // key sets (`world.cpp:392`).
        string material = entities
            .Where(entity =>
                string.Equals(entity.ClassName, "worldspawn", StringComparison.OrdinalIgnoreCase))
            .Select(entity => entity.TryGetValue("detailmaterial", out string? stated) ? stated : string.Empty)
            .FirstOrDefault(string.Empty);

        if (material.Length > 0)
        {
            output.WriteLine($"{name}: detailmaterial '{material}'");
        }

        if (BspEntities.DetailController(entities) is not { } controller)
        {
            if (alwaysReport)
            {
                output.WriteLine(
                    $"{name}: no {BspEntities.DetailControllerClass} — cl_detaildist and " +
                    "cl_detailfade keep whatever the config set." +
                    $" ({entities.Count} entities read; worldspawn {(worldspawn ? "present" : "MISSING")})");
            }

            return (false, worldspawn);
        }

        int count = entities.Count(entity =>
            string.Equals(entity.ClassName, BspEntities.DetailControllerClass, StringComparison.OrdinalIgnoreCase));

        output.WriteLine(
            $"{name}: fademindist {controller.FadeStart.ToString(CultureInfo.InvariantCulture)} " +
            $"-> cl_detailfade, fademaxdist {controller.FadeEnd.ToString(CultureInfo.InvariantCulture)} " +
            $"-> cl_detaildist, each MIN'd against the config's value" +
            $"{(count > 1 ? $" ({count} controllers; the last one wins)" : string.Empty)}");

        return (true, worldspawn);
    }
}
