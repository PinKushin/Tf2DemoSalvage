using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>Every key a map's entity lump gives the entities of one class, optionally only those near a point.</summary>
/// <remarks>
/// The lump as <see cref="BspEntities.ReadFrom"/> reads it, nothing interpreted: an absent key is absent, which is how
/// a default is told from a stated value. The control is the count — a class the map certainly places must list.
/// </remarks>
public sealed class EntityKeysProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "entity-keys";

    /// <inheritdoc/>
    public string Summary => "every key of a map's entities of one class: entity-keys <map> <classname substring> [x y z radius]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count < 2)
        {
            output.WriteLine(Summary);
            return;
        }

        MapLocator locator = new(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder);

        if (locator.Find(arguments[0]) is not { } path)
        {
            output.WriteLine($"No map named '{arguments[0]}'.");
            return;
        }

        (float X, float Y, float Z, float Radius)? near = arguments.Count >= 6
            ? (Number(arguments[2]), Number(arguments[3]), Number(arguments[4]), Number(arguments[5]))
            : null;

        int listed = 0;
        int ofClass = 0;

        foreach (BspEntity entity in BspEntities.ReadFrom(File.ReadAllBytes(path)))
        {
            if (!entity.ClassName.Contains(arguments[1], StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            ofClass++;

            if (near is { } point && !Within(entity, point))
            {
                continue;
            }

            listed++;
            output.WriteLine(string.Join("  ", entity.Values.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase).Select(pair => $"{pair.Key} '{pair.Value}'")));
        }

        output.WriteLine($"{listed} listed of {ofClass} whose class contains '{arguments[1]}'");
    }

    private static float Number(string text) => float.Parse(text, CultureInfo.InvariantCulture);

    private static bool Within(BspEntity entity, (float X, float Y, float Z, float Radius) point)
    {
        if (!entity.TryGetValue("origin", out string origin))
        {
            return false;
        }

        string[] parts = origin.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length != 3)
        {
            return false;
        }

        float dx = Number(parts[0]) - point.X;
        float dy = Number(parts[1]) - point.Y;
        float dz = Number(parts[2]) - point.Z;

        return (dx * dx) + (dy * dy) + (dz * dz) <= point.Radius * point.Radius;
    }
}
