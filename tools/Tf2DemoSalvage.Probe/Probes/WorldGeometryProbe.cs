using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// The static-prop geometry actually PRODUCED near a point, by material.
/// </summary>
/// <remarks>
/// **The difference between "the map places it" and "we built it".** `map-near` reads the BSP and
/// says what the map declares; this loads the map exactly as the viewer does and reports the baked
/// vertices that came out the other end. A prop that is placed, loads, reports no missing model and
/// still contributes nothing is a different bug from one that is misplaced, and only this can tell
/// them apart.
///
/// Written for the owner's report on the blue-spawn setup gates (B238): *"i can tell you the frame
/// is not drawing where it belongs because its not there"*. Every instrument until now said the
/// frame loads — `pairing … 120v vtx 264c` — and none of them looked at the output.
///
/// <code>
///   world-near cp_fulgur 5416 -2168 432
///   world-near cp_fulgur 5416 -2168 432 512
/// </code>
/// </remarks>
public sealed class WorldGeometryProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "world-near";

    /// <inheritdoc/>
    public string Summary =>
        "baked prop geometry near a point: world-near <map> <x> <y> <z> [radius]";

    private const float DefaultRadius = 128f;

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count < 4)
        {
            output.WriteLine("world-near <map> <x> <y> <z> [radius]");
            return;
        }

        string map = arguments[0];
        float x = Number(arguments[1]);
        float y = Number(arguments[2]);
        float z = Number(arguments[3]);
        float radius = arguments.Count > 4 ? Number(arguments[4]) : DefaultRadius;

        MapLocator locator = new(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder);

        string? path = locator.Find(
            map.EndsWith(".bsp", StringComparison.OrdinalIgnoreCase) ? map[..^4] : map);

        if (path is null || locator.FindGameFolder() is not { } folder)
        {
            output.WriteLine($"No map named '{map}', or the game is not installed.");
            return;
        }

        byte[] bytes = File.ReadAllBytes(path);

        GameContent game = GameContent.Open(folder, NullLoggerFactory.Instance);
        LoadedMap loaded = LoadedMap.Read(bytes, game, timeline: null, 0, NullLoggerFactory.Instance);

        if (loaded.Assets is not { } assets)
        {
            output.WriteLine("The map loaded with no assets, so no prop geometry was built.");
            return;
        }

        // What the map SAYS is there, so the two halves of the question sit side by side.
        foreach (BspStaticProp prop in BspStaticProps.Read(bytes)
            .Where(prop => Near(prop.X, prop.Y, prop.Z, x, y, z, radius)))
        {
            output.WriteLine(
                $"PLACED ({prop.X:0} {prop.Y:0} {prop.Z:0}) skin "
                + $"{prop.Skin.ToString(CultureInfo.InvariantCulture)}  {prop.Model}");
        }

        Dictionary<int, int> corners = [];

        // **And their baked COLOUR, because "built" and "visible" are different claims.** A static
        // prop's light is baked into its vertices here, so geometry that reaches the buffer at
        // (0,0,0) draws black — present, correct, and indistinguishable from missing in a dark
        // doorway. Reporting the count alone is the mistake that made a wall out of a one-face sign.
        Dictionary<int, (float Red, float Green, float Blue)> light = [];

        foreach (PropVertex corner in assets.Props
            .Where(corner => Near(corner.X, corner.Y, corner.Z, x, y, z, radius)))
        {
            int material = corner.MaterialIndex;

            corners[material] = corners.TryGetValue(material, out int seen) ? seen + 1 : 1;

            (float red, float green, float blue) =
                light.TryGetValue(material, out (float Red, float Green, float Blue) sum)
                    ? sum
                    : (0f, 0f, 0f);

            light[material] = (red + corner.Red, green + corner.Green, blue + corner.Blue);
        }

        output.WriteLine(
            $"BUILT {corners.Values.Sum().ToString(CultureInfo.InvariantCulture)} prop corners "
            + $"within {radius.ToString("0", CultureInfo.InvariantCulture)} of "
            + $"({x:0} {y:0} {z:0}), across "
            + $"{corners.Count.ToString(CultureInfo.InvariantCulture)} materials");

        foreach ((int material, int count) in corners.OrderByDescending(pair => pair.Value))
        {
            (float red, float green, float blue) = light[material];

            output.WriteLine(
                $"  {count,6} corners  material "
                + $"{material.ToString(CultureInfo.InvariantCulture),4}  "
                + $"mean colour ({red / count:0.000} {green / count:0.000} {blue / count:0.000})");
        }
    }

    private static bool Near(
        float x, float y, float z, float atX, float atY, float atZ, float radius) =>
        Math.Abs(x - atX) <= radius && Math.Abs(y - atY) <= radius && Math.Abs(z - atZ) <= radius;

    private static float Number(string text) => float.Parse(text, CultureInfo.InvariantCulture);
}
