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
/// Everything a map places within reach of a point, static and entity alike.
/// </summary>
/// <remarks>
/// **Built for the coordinates <c>cl_showpos</c> gives.** The owner stands where something looks
/// wrong, reads three numbers off the screen, and this says what the map put there — which is the
/// difference between "we are not drawing it" and "it was never there", and those have opposite
/// fixes.
///
/// <code>
///   map-near cp_fulgur 5416 -2168 472
///   map-near cp_fulgur 5416 -2168 472 512
/// </code>
///
/// **A static prop and a brush entity are found the same way here and reported differently**,
/// because what you can do about each differs: a static prop is a placement in the game lump with a
/// transform, and an entity has a name, a parent and a rendermode that decide whether it draws at
/// all.
/// </remarks>
public sealed class MapNearProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "map-near";

    /// <inheritdoc/>
    public string Summary => "what a map places near a point: map-near <map> <x> <y> <z> [radius]";

    private const float DefaultRadius = 256f;

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count < 4)
        {
            output.WriteLine("map-near <map> <x> <y> <z> [radius] — the numbers cl_showpos prints");
            return;
        }

        string map = arguments[0];
        float x = Coordinate(arguments[1]);
        float y = Coordinate(arguments[2]);
        float z = Coordinate(arguments[3]);
        float radius = arguments.Count > 4 ? Coordinate(arguments[4]) : DefaultRadius;

        MapLocator locator = new(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder);
        string? path = locator.Find(
            map.EndsWith(".bsp", StringComparison.OrdinalIgnoreCase) ? map[..^4] : map);

        if (path is null)
        {
            output.WriteLine($"No map named '{map}' in the game's maps folder or ours.");
            return;
        }

        byte[] bytes = File.ReadAllBytes(path);

        output.WriteLine(
            $"{Path.GetFileName(path)} within {radius.ToString("0", CultureInfo.InvariantCulture)} "
            + $"of ({x:0} {y:0} {z:0})");

        foreach (BspStaticProp prop in BspStaticProps.Read(bytes)
            .Where(prop => Near(prop.X, prop.Y, prop.Z, x, y, z, radius))
            .OrderBy(prop => Distance(prop.X, prop.Y, prop.Z, x, y, z)))
        {
            output.WriteLine(
                $"STATIC {Distance(prop.X, prop.Y, prop.Z, x, y, z),6:0} away  "
                + $"({prop.X:0} {prop.Y:0} {prop.Z:0}) "
                + $"pitch {prop.Pitch:0} yaw {prop.Yaw:0} roll {prop.Roll:0} "
                + $"scale {prop.Scale:0.##} skin {prop.Skin.ToString(CultureInfo.InvariantCulture)}  "
                + prop.Model);
        }

        foreach (BspEntity entity in BspEntities.ReadFrom(bytes))
        {
            if (Origin(entity) is not { } at || !Near(at.X, at.Y, at.Z, x, y, z, radius))
            {
                continue;
            }

            output.WriteLine(
                $"ENTITY {Distance(at.X, at.Y, at.Z, x, y, z),6:0} away  "
                + $"({at.X:0} {at.Y:0} {at.Z:0}) "
                + $"{entity.ClassName} '{Value(entity, "model")}' "
                + $"name '{Value(entity, "targetname")}' "
                + $"parent '{Value(entity, "parentname")}' "
                + $"rendermode '{Value(entity, "rendermode")}'");
        }
    }

    /// <summary>A box rather than a sphere, so a long prop is not missed by its origin.</summary>
    private static bool Near(
        float x, float y, float z, float atX, float atY, float atZ, float radius) =>
        Math.Abs(x - atX) <= radius && Math.Abs(y - atY) <= radius && Math.Abs(z - atZ) <= radius;

    private static float Distance(
        float x, float y, float z, float atX, float atY, float atZ) =>
        MathF.Sqrt(((x - atX) * (x - atX)) + ((y - atY) * (y - atY)) + ((z - atZ) * (z - atZ)));

    private static float Coordinate(string text) =>
        float.Parse(text, CultureInfo.InvariantCulture);

    /// <summary>An entity's origin, or null when it declares none — a worldspawn brush.</summary>
    private static (float X, float Y, float Z)? Origin(BspEntity entity)
    {
        if (!entity.TryGetValue("origin", out string origin))
        {
            return null;
        }

        string[] parts = origin.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return parts.Length == 3
            && float.TryParse(parts[0], CultureInfo.InvariantCulture, out float x)
            && float.TryParse(parts[1], CultureInfo.InvariantCulture, out float y)
            && float.TryParse(parts[2], CultureInfo.InvariantCulture, out float z)
                ? (x, y, z)
                : null;
    }

    private static string Value(BspEntity entity, string key) =>
        entity.TryGetValue(key, out string value) ? value : "";
}
