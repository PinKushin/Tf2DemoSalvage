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
/// Which brush entities a map places, by class — the ones no position probe can find.
/// </summary>
/// <remarks>
/// **A brush entity has no origin, which is why it is invisible to `map-near`.** Its geometry is a
/// brush MODEL (<c>"model" "*17"</c>) whose vertices are already in world space, so the entity's
/// own <c>origin</c> key is absent or zero — and a probe that ranks by distance from a point lists
/// every static prop in the room and none of the doors, windows or areaportals standing in it.
/// That cost a measurement: a search around a spawn returned twenty-four props and nothing that
/// could account for a black window.
///
/// **The bounds come from the brush model**, so a class with no origin still gets a position that
/// means something.
///
/// <code>
///   brush-ents koth_harvest_final
///   brush-ents koth_harvest_final areaportal
/// </code>
/// </remarks>
public sealed class BrushEntityProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "brush-ents";

    /// <inheritdoc/>
    public string Summary =>
        "brush entities a map places, by class: brush-ents <map> [classname substring]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count < 1)
        {
            output.WriteLine("brush-ents <map> [classname substring]");
            return;
        }

        if (new MapLocator(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder)
            .Find(arguments[0]) is not { } path)
        {
            output.WriteLine($"No map named '{arguments[0]}'.");
            return;
        }

        string? wanted = arguments.Count > 1 ? arguments[1] : null;

        ReadOnlyMemory<byte> file = File.ReadAllBytes(path);

        IReadOnlyList<BspEntity> entities = BspEntities.ReadFrom(file);
        IReadOnlyList<BspModel> models = BspModels.Read(file);

        output.WriteLine(
            $"{Path.GetFileName(path)}: {entities.Count} entities, {models.Count} brush models");

        Dictionary<string, int> byClass = new(StringComparer.OrdinalIgnoreCase);

        foreach (BspEntity entity in entities)
        {
            if (!entity.TryGetValue("model", out string model) || !model.StartsWith('*'))
            {
                continue;
            }

            byClass[entity.ClassName] =
                byClass.TryGetValue(entity.ClassName, out int seen) ? seen + 1 : 1;
        }

        output.WriteLine($"  {byClass.Count} classes carry a brush model:");

        foreach ((string name, int count) in byClass.OrderByDescending(each => each.Value))
        {
            output.WriteLine(
                $"    {count.ToString(CultureInfo.InvariantCulture),4}  {name}");
        }

        // **Classes with NO brush model are listed too when asked for by name**, because that is
        // itself the answer for `func_areaportal`: vbsp consumes the brush and leaves a point
        // entity carrying only a `portalnumber`, so "it has no model" is the finding rather than a
        // gap in this probe.
        if (wanted is null)
        {
            return;
        }

        output.WriteLine($"  every '{wanted}' entity, model or not:");

        foreach (BspEntity entity in entities
            .Where(each => each.ClassName.Contains(wanted, StringComparison.OrdinalIgnoreCase)))
        {
            Describe(output, entity, models);
        }
    }

    /// <summary>One entity, with its brush model's bounds when it has one.</summary>
    private static void Describe(
        TextWriter output, BspEntity entity, IReadOnlyList<BspModel> models)
    {
        string model = entity.TryGetValue("model", out string named) ? named : "(no model)";

        string bounds = model.StartsWith('*')
            && int.TryParse(
                model[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int index)
            && index >= 0 && index < models.Count
                ? $"({models[index].Minimum.X:0} {models[index].Minimum.Y:0} "
                    + $"{models[index].Minimum.Z:0}) .. ({models[index].Maximum.X:0} "
                    + $"{models[index].Maximum.Y:0} {models[index].Maximum.Z:0})"
                : string.Empty;

        string keys = string.Join(
            " ",
            entity.Values
                .Where(pair => !string.Equals(pair.Key, "classname", StringComparison.Ordinal))
                .Select(pair => $"{pair.Key}='{pair.Value}'"));

        output.WriteLine($"    {entity.ClassName} {model} {bounds}");
        output.WriteLine($"      {keys}");
    }
}
