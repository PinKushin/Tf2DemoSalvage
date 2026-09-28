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
/// The static-prop model draws actually PRODUCED near a point.
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
        "static prop model draws near a point, with their baked colours: world-near <map> <x> <y> <z> [radius]";

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

        // **What was BUILT: each nearby placement's model draw** (B426) — the geometry the model draw
        // finds for it, and its baked COLOUR, because "built" and "visible" are different claims: a
        // colour mesh of (0,0,0) draws black — present, correct, and indistinguishable from missing in
        // a dark doorway. A placement with no colour mesh is lit per draw instead.
        List<Core.Scene.SceneProp> near =
            [.. assets.StaticModels.Where(prop => Near(prop.Pose.X, prop.Pose.Y, prop.Pose.Z, x, y, z, radius))];

        output.WriteLine(
            $"BUILT {near.Count.ToString(CultureInfo.InvariantCulture)} model draws "
            + $"within {radius.ToString("0", CultureInfo.InvariantCulture)} of ({x:0} {y:0} {z:0})");

        foreach (Core.Scene.SceneProp prop in near)
        {
            int corners = assets.Geometry(prop.ModelPath)?.Geometry[0].Count ?? 0;

            string light = assets.StaticModelColours.TryGetValue(prop.EntityIndex, out float[]? colours)
                ? $"mean baked colour ({Mean(colours, 0):0.000} {Mean(colours, 1):0.000} {Mean(colours, 2):0.000})"
                : "no colour mesh, lit per draw";

            output.WriteLine(
                $"  {corners,6} corners  skin {prop.Pose.Skin.ToString(CultureInfo.InvariantCulture)}  "
                + $"{light}  {prop.ModelPath}");
        }
    }

    /// <summary>One channel's mean over a red-green-blue colour mesh.</summary>
    private static float Mean(float[] colours, int channel)
    {
        float sum = 0f;

        for (int at = channel; at < colours.Length; at += 3)
        {
            sum += colours[at];
        }

        return colours.Length == 0 ? 0f : sum / (colours.Length / 3);
    }

    private static bool Near(
        float x, float y, float z, float atX, float atY, float atZ, float radius) =>
        Math.Abs(x - atX) <= radius && Math.Abs(y - atY) <= radius && Math.Abs(z - atZ) <= radius;

    private static float Number(string text) => float.Parse(text, CultureInfo.InvariantCulture);
}
