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
/// Every model a map places, from either source, filtered by name.
/// </summary>
/// <remarks>
/// **A map places models two ways and a probe that reads one of them reports a false absence.**
/// Static props live in the GAME lump; anything that moves, opens or is parented to something else
/// is a brush or point entity in the entity lump with a <c>model</c> key. "We do not draw X" is a
/// claim about both.
///
/// Written for the owner's report that a setup gate is missing its frame:
///
/// > *"the actual locked before round starts spawn doors are the chickenwire texture/prop and a
/// > yellow pipe like frame … our issue is we are dropping or not drawing the yellow pipe frame"*
///
/// **It reports what the MAP contains, and nothing about what we draw.** Those are two questions
/// and answering them together is how a missing feature gets mistaken for a rendering fault — see
/// `docs/memory/read-the-map-before-the-renderer.md`. If the frame is not in this list under any
/// name, it is not a drawing bug at all.
/// </remarks>
public sealed class MapModelProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "map-models";

    /// <inheritdoc/>
    public string Summary => "models a map places, static and entity: map-models <map> [substring]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0)
        {
            output.WriteLine("map-models <map> [substring] — for example: map-models cp_fulgur pipe");
            return;
        }

        string map = arguments[0];
        string filter = arguments.Count > 1 ? arguments[1] : string.Empty;

        MapLocator locator = new(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder);
        // `Find` takes the name a demo header carries and appends the extension itself, and it
        // refuses anything with a path separator in it. Passing "cp_fulgur.bsp" searches for
        // "cp_fulgur.bsp.bsp" and reports the map as missing.
        string? path = locator.Find(
            map.EndsWith(".bsp", StringComparison.OrdinalIgnoreCase) ? map[..^4] : map);

        if (path is null)
        {
            output.WriteLine($"No map named '{map}' in the game's maps folder or ours.");
            return;
        }

        byte[] bytes = File.ReadAllBytes(path);
        output.WriteLine($"{path} filter '{filter}'");

        Report(output, bytes, filter);
    }

    private static void Report(TextWriter output, byte[] bytes, string filter)
    {
        // Static props, grouped: a map places the same fence forty times and forty lines say less
        // than one line and a count.
        Dictionary<string, int> statics = new(StringComparer.OrdinalIgnoreCase);

        foreach (string model in BspStaticProps.Read(bytes)
            .Where(prop => Matches(prop.Model, filter))
            .Select(prop => prop.Model))
        {
            statics[model] = statics.TryGetValue(model, out int seen) ? seen + 1 : 1;
        }

        foreach ((string model, int count) in statics.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            output.WriteLine(
                $"STATIC {count.ToString(CultureInfo.InvariantCulture),4}  {model}");
        }

        // Entities, one line each and NOT grouped: a gate's frame is one entity and its origin,
        // parent and name are the whole question.
        foreach (BspEntity entity in BspEntities.ReadFrom(bytes))
        {
            if (!entity.TryGetValue("model", out string model) || !Matches(model, filter))
            {
                continue;
            }

            output.WriteLine(
                $"ENTITY {entity.ClassName} '{model}' "
                + $"name '{Value(entity, "targetname")}' "
                + $"parent '{Value(entity, "parentname")}' "
                + $"origin '{Value(entity, "origin")}' "
                + $"rendermode '{Value(entity, "rendermode")}' "
                + $"disableshadows '{Value(entity, "disableshadows")}' "
                + $"skin '{Value(entity, "skin")}'");
        }
    }

    /// <summary>An empty filter matches everything; otherwise a case-insensitive substring.</summary>
    private static bool Matches(string model, string filter) =>
        filter.Length == 0
        || model.Contains(filter, StringComparison.OrdinalIgnoreCase);

    private static string Value(BspEntity entity, string key) =>
        entity.TryGetValue(key, out string value) ? value : "";
}
