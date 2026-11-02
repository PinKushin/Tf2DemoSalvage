using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// Census of installed maps for water on a brush ENTITY: a <c>func_water_analog</c>, or any brush model past the world's
/// whose faces carry a <c>Water</c> material (B657 review).
/// </summary>
/// <remarks>
/// **Why it matters:** a brush entity's faces are drawn by the model path (<c>WorldRenderer</c>'s <c>CullFor</c>), never
/// through <c>WaterBatch</c>, so such water gets neither the <c>Water</c> shader's passes nor its cull.
///
/// **The control is in every line:** each map prints its WORLD water faces beside its entity water faces, so a map
/// known to have water (ctf_2fort) must show world faces, or the material lookup is broken and its zeroes mean nothing.
/// A material is <c>Water</c> when its VMT, read from the map's pakfile and then the game's archives, names that
/// shader; a cubemap patch is followed through its <c>include</c>. Names containing "water" that resolve to no VMT are
/// listed, so what the census could not see is stated.
///
/// <code>
///   water-brush-ents             every installed map
///   water-brush-ents ctf_2fort   one
/// </code>
/// </remarks>
public sealed class WaterBrushEntityProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "water-brush-ents";

    /// <inheritdoc/>
    public string Summary =>
        "installed maps' water on brush entities (func_water_analog, a door with a Water material): water-brush-ents [map]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        MapLocator locator = new(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder);

        if (locator.FindGameFolder() is not { } game || locator.Find("ctf_2fort") is not { } anyMap)
        {
            output.WriteLine("No TF2 install found.");
            return;
        }

        GameArchives archives = GameArchives.Open(game);
        Dictionary<string, bool> isWater = new(StringComparer.OrdinalIgnoreCase);

        IEnumerable<string> maps = arguments.Count > 0
            ? [locator.Find(arguments[0]) ?? throw new ArgumentException($"no map named '{arguments[0]}'")]
            : Directory.EnumerateFiles(Path.GetDirectoryName(anyMap)!, "*.bsp").Order(StringComparer.Ordinal);

        int mapsRead = 0;
        int withWorldWater = 0;
        int withEntityWater = 0;
        int skipped = 0;
        HashSet<string> unresolved = new(StringComparer.OrdinalIgnoreCase);

        foreach (string map in maps)
        {
            // Per map: a pakfile can ship its own material under a name the game also has.
            isWater.Clear();

            // **One bad map is a line, not the end of the census**: the read, the pakfile and every VMT it resolves.
            try
            {
                byte[] bytes = File.ReadAllBytes(map);

                Census(map, MapLevel.Read(bytes, NullLogger.Instance), PakFile.ReadFrom(bytes));
                mapsRead++;
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or InvalidDataException
                or ArgumentException or IndexOutOfRangeException or OverflowException or InvalidOperationException)
            {
                skipped++;
                output.WriteLine($"{Path.GetFileName(map)}: skipped, {failure.GetType().Name}: {failure.Message}");
            }
        }

        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{mapsRead} maps read, {skipped} skipped unreadable; {withWorldWater} with world water; {withEntityWater} with water on a brush entity"));

        if (unresolved.Count > 0)
        {
            output.WriteLine($"names with 'water' not resolved to a VMT ({unresolved.Count}): {string.Join(", ", unresolved.Order(StringComparer.Ordinal))}");
        }

        void Census(string map, MapLevel level, PakFile pak)
        {
            IReadOnlyList<BspModel> models = level.BrushModels ?? [];
            int worldFaces = models.Count > 0 ? models[0].FaceCount : int.MaxValue;

            bool Water(int material)
            {
                if (material < 0 || material >= level.Materials.Count)
                {
                    return false;
                }

                string name = level.Materials[material].Name;

                if (!isWater.TryGetValue(name, out bool known))
                {
                    known = Resolve(pak, archives, name, unresolved);
                    isWater[name] = known;
                }

                return known;
            }

            int worldWater = level.Surfaces.Count(surface => surface.FaceIndex < worldFaces && Water(surface.MaterialIndex));

            // Which brush model owns each face past the world's.
            List<string> found = [];

            for (int model = 1; model < models.Count; model++)
            {
                int first = models[model].FirstFace;
                int count = models[model].FaceCount;
                string[] waters = [.. level.Surfaces
                    .Where(surface => surface.FaceIndex >= first && surface.FaceIndex < first + count && Water(surface.MaterialIndex))
                    .Select(surface => level.Materials[surface.MaterialIndex].Name)
                    .Distinct(StringComparer.OrdinalIgnoreCase)];

                if (waters.Length > 0)
                {
                    string owner = level.Entities
                        .FirstOrDefault(entity => entity.TryGetValue("model", out string named) &&
                            string.Equals(named, "*" + model.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))?
                        .ClassName ?? "(no entity)";

                    found.Add($"*{model} {owner}: {string.Join(", ", waters)}");
                }
            }

            int analogs = level.Entities.Count(entity =>
                entity.ClassName.Equals("func_water_analog", StringComparison.OrdinalIgnoreCase));

            withWorldWater += worldWater > 0 ? 1 : 0;
            withEntityWater += found.Count > 0 || analogs > 0 ? 1 : 0;

            if (worldWater > 0 || found.Count > 0 || analogs > 0)
            {
                output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"{Path.GetFileName(map)}: world water faces {worldWater}, func_water_analog {analogs}, brush models with water {found.Count}"));

                foreach (string line in found)
                {
                    output.WriteLine("    " + line);
                }
            }
        }
    }

    /// <summary>Whether a material's VMT names the Water shader: the map's pakfile first, then the game's, through a patch's include.</summary>
    private static bool Resolve(PakFile pak, GameArchives archives, string name, HashSet<string> unresolved)
    {
        string path = name.Replace('\\', '/');

        if (path.EndsWith(".vmt", StringComparison.OrdinalIgnoreCase))
        {
            path = path[..^4];
        }

        string file = path.StartsWith("materials/", StringComparison.OrdinalIgnoreCase) ? path + ".vmt" : "materials/" + path + ".vmt";

        // A cubemap patch names the material it patches in "include"; one hop covers what vbsp writes.
        for (int hop = 0; hop < 3; hop++)
        {
            if ((pak.ReadFile(file) ?? archives.Read(file)) is not { } bytes)
            {
                break;
            }

            VmtMaterial material = VmtMaterial.Parse(bytes);

            if (material.Shader.Equals("patch", StringComparison.OrdinalIgnoreCase) && material.Include is { } include)
            {
                file = include.Replace('\\', '/');
                continue;
            }

            return material.Shader.Equals("Water", StringComparison.OrdinalIgnoreCase);
        }

        if (path.Contains("water", StringComparison.OrdinalIgnoreCase))
        {
            unresolved.Add(path);
        }

        return false;
    }
}
