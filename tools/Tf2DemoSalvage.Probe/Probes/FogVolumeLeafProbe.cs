using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// What <c>R_GetVisibleFogVolume</c> reads from the eye's leaf, down a column of heights (B664).
/// </summary>
/// <remarks>
/// TF2's <c>r_debugcheapwater</c> prints the distance the engine read from the eye leaf, which is the
/// <c>LUMP_LEAFMINDISTTOWATER</c> entry (engine.dll <c>0x1800e031f</c>). Printing ours from the same tree and lump,
/// at the eye and above and below it, shows which leaf matches. The production reads are <see cref="BspLeafTree"/> and
/// <see cref="BspWater"/>, with no second route. Control: a point over the world water reports a water leaf or a
/// small distance.
/// </remarks>
public sealed class FogVolumeLeafProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "fog-leaf";

    /// <inheritdoc/>
    public string Summary =>
        "the eye leaf's contents, water ID and distance to water, in a column: fog-leaf <map> <x> <y> <z> [step] [count]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count < 4)
        {
            output.WriteLine("fog-leaf <map> <x> <y> <z> [step] [count]");
            return;
        }

        if (new MapLocator(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder).Find(arguments[0]) is not { } path)
        {
            output.WriteLine($"No map named '{arguments[0]}'.");
            return;
        }

        float x = float.Parse(arguments[1], CultureInfo.InvariantCulture);
        float y = float.Parse(arguments[2], CultureInfo.InvariantCulture);
        float z = float.Parse(arguments[3], CultureInfo.InvariantCulture);
        float step = arguments.Count > 4 ? float.Parse(arguments[4], CultureInfo.InvariantCulture) : 16f;
        int count = arguments.Count > 5 ? int.Parse(arguments[5], CultureInfo.InvariantCulture) : 8;

        ReadOnlyMemory<byte> file = File.ReadAllBytes(path);
        BspLeafTree tree = BspLeafTree.Read(file);
        BspWater water = BspWater.Read(file);

        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{Path.GetFileName(path)}: {tree.LeafCount} leaves, {water.LeafDistances.Count} distances, {water.Volumes.Count} volumes"));

        for (int at = -count; at <= count; at++)
        {
            float height = z + (at * step);
            int leaf = tree.LeafAt(x, y, height);

            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  z {height,7:0.#}: leaf {leaf,6} contents 0x{tree.Contents(leaf):X} water {tree.WaterDataId(leaf),3} " +
                $"dist {water.DistanceToWater(leaf)} testfog {(tree.Contents(leaf) & VisibleFogVolume.ContentsTestFogVolume) != 0}"));
        }
    }
}
