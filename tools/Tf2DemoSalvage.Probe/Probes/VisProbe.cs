using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// What the PVS keeps from one point, and what it costs — the numbers behind a black doorway.
/// </summary>
/// <remarks>
/// **Written because a screenshot differential proved nothing.** Turning the PVS off and
/// re-capturing produced an identical frame, which was read as "the PVS is not the cause" — and
/// that reading is only sound if the PVS was filtering to begin with. A test whose manipulation
/// changes nothing cannot distinguish "this is not the cause" from "I changed nothing", and the
/// control that separates them is the one below: the leaf count with the PVS applied, against the
/// leaf count without it, from the same point.
///
/// **It reports the values the production walk used**, by calling <see cref="WorldVisibility"/>
/// itself rather than reimplementing the descent — the rule B243 exists for. A second walk would be
/// free to disagree with the renderer about what it kept.
///
/// <code>
///   vis koth_harvest_final 288 2250 69
/// </code>
///
/// **The frustum is deliberately unbuilt**, so the number is the PVS's alone: `Leaves` applies no
/// frustum test for a default <c>ViewFrustum</c>, which isolates the question from where the camera
/// happens to be pointing.
/// </remarks>
public sealed class VisProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "vis";

    /// <inheritdoc/>
    public string Summary =>
        "what the PVS keeps from a point, with and without it: vis <map> <x> <y> <z>";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count < 4)
        {
            output.WriteLine("vis <map> <x> <y> <z>");
            return;
        }

        if (new MapLocator(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder)
            .Find(arguments[0]) is not { } path)
        {
            output.WriteLine($"No map named '{arguments[0]}'.");
            return;
        }

        float x = float.Parse(arguments[1], CultureInfo.InvariantCulture);
        float y = float.Parse(arguments[2], CultureInfo.InvariantCulture);
        float z = float.Parse(arguments[3], CultureInfo.InvariantCulture);

        ReadOnlyMemory<byte> file = File.ReadAllBytes(path);

        BspLeafTree tree = BspLeafTree.Read(file);
        BspVisibility pvs = BspVisibility.Read(file);

        output.WriteLine(
            $"{Path.GetFileName(path)} at ({x:0} {y:0} {z:0})");

        // **The three facts that decide whether the PVS can filter at all.** A map with no vis
        // lump, or a point with no cluster, means the walk applies no PVS test — and a differential
        // run in that state measures nothing.
        int leaf = tree.LeafAt(x, y, z);

        output.WriteLine(
            $"  leaf {leaf}, cluster {tree.Cluster(leaf)}, area {tree.Area(leaf)}");

        output.WriteLine(
            $"  vis lump: {(pvs.HasData ? $"{pvs.ClusterCount} clusters" : "ABSENT — no PVS test can run")}"
            + $", tree has {tree.LeafCount} leaves");

        WorldVisibility real = new(tree, pvs);
        WorldVisibility none = new(tree, BspVisibility.None);

        int withPvs = real.Leaves(x, y, z, default).Count;
        int withoutPvs = none.Leaves(x, y, z, default).Count;

        output.WriteLine(
            $"  leaves kept WITH the PVS:    {withPvs}");

        output.WriteLine(
            $"  leaves kept WITHOUT it:      {withoutPvs}");

        // **The control, stated rather than left to the reader.** Equal counts mean the PVS is not
        // filtering from here, and any experiment that "turned it off" changed nothing.
        output.WriteLine(
            withPvs == withoutPvs
                ? "  THE PVS IS NOT FILTERING FROM HERE — a differential against it measures nothing"
                : $"  the PVS removes {withoutPvs - withPvs} leaves "
                    + $"({100d * (withoutPvs - withPvs) / Math.Max(withoutPvs, 1):0.0}%)");

        Areas(output, tree, real.Leaves(x, y, z, default));
    }

    /// <summary>Which areas the kept leaves belong to, since an areaportal splits by area.</summary>
    /// <remarks>
    /// **The spread across areas is what an areaportal question needs.** A view that keeps leaves
    /// from one area only is a view that stops at the first portal, whatever the reason.
    /// </remarks>
    private static void Areas(TextWriter output, BspLeafTree tree, IReadOnlyList<int> leaves)
    {
        Dictionary<int, int> perArea = [];

        foreach (int leaf in leaves)
        {
            int area = tree.Area(leaf);

            perArea[area] = perArea.TryGetValue(area, out int seen) ? seen + 1 : 1;
        }

        output.WriteLine($"  kept leaves span {perArea.Count} areas:");

        foreach ((int area, int count) in perArea)
        {
            output.WriteLine(
                $"    area {area.ToString(CultureInfo.InvariantCulture),3}: "
                + $"{count.ToString(CultureInfo.InvariantCulture),5} leaves");
        }
    }
}
