using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>How often a first-person eye changes PVS cluster, and what one re-mark costs — the PVS walk's miss path (B259).</summary>
/// <remarks>
/// **The eye is the RECORDED one, a tick at a time**: the player's origin plus its view offset (68 when the demo sends
/// none), from <see cref="DemoTimeline.PlayersAt(int)"/>. The viewer's first-person camera is interpolated between ticks, so
/// a boundary is crossed in the same number of places; what this misses is a crossing and recrossing inside one tick.
///
/// **The cost is the production instrument**, <see cref="WorldVisibility.MarkTicks"/>, read after a <see cref="WorldVisibility.Leaves"/>
/// call that moved the eye into the other of two clusters the path visits — every call a miss. The control is the
/// same call repeated from one eye, which must read 0 (the hit path).
/// </remarks>
public sealed class ClusterPathProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "cluster-path";

    /// <inheritdoc/>
    public string Summary =>
        "a player's eye cluster changes per second over ticks, and one PVS re-mark's cost: cluster-path <demo> <map> <entity> <fromTick> <toTick> [re-marks]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count < 5 || DemoCorpus.Find(arguments[0], output) is not { } demo ||
            new MapLocator(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder).Find(arguments[1]) is not { } map)
        {
            output.WriteLine("Usage: cluster-path <demo> <map> <entity> <fromTick> <toTick> [re-marks]");
            return;
        }

        int entity = int.Parse(arguments[2], CultureInfo.InvariantCulture);
        int from = int.Parse(arguments[3], CultureInfo.InvariantCulture);
        int to = int.Parse(arguments[4], CultureInfo.InvariantCulture);
        int remarks = arguments.Count > 5 ? int.Parse(arguments[5], CultureInfo.InvariantCulture) : 2000;

        ReadOnlyMemory<byte> file = File.ReadAllBytes(map);
        BspLeafTree tree = BspLeafTree.Read(file);
        BspVisibility pvs = BspVisibility.Read(file);
        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(demo));

        List<(int Tick, int Cluster, (float X, float Y, float Z) Eye)> path = [];

        for (int tick = from; tick <= to; tick++)
        {
            foreach (ScenePlayer player in timeline.PlayersAt(tick))
            {
                if (player.EntityIndex == entity)
                {
                    (float X, float Y, float Z) eye = (player.X, player.Y, player.Z + (player.ViewOffsetZ ?? 68f));
                    path.Add((tick, tree.ClusterAt(eye.X, eye.Y, eye.Z), eye));
                }
            }
        }

        if (path.Count < 2)
        {
            output.WriteLine($"entity {entity} is on {path.Count} of the ticks {from}-{to}; nothing to count");
            return;
        }

        int changes = 0;
        List<(int From, int To, int Tick)> moves = [];

        for (int at = 1; at < path.Count; at++)
        {
            if (path[at].Cluster != path[at - 1].Cluster)
            {
                changes++;
                moves.Add((path[at - 1].Cluster, path[at].Cluster, path[at].Tick));
            }
        }

        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{Path.GetFileName(demo)} on {Path.GetFileName(map)}: entity {entity} on {path.Count} of ticks {from}-{to}; " +
            $"{pvs.ClusterCount} clusters, {tree.LeafCount} leaves"));
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  eye cluster changes: {changes} over {to - from} ticks; distinct clusters {path.Select(step => step.Cluster).Distinct().Count()}; " +
            $"in no cluster on {path.Count(step => step.Cluster < 0)} ticks"));

        foreach ((int fromCluster, int toCluster, int tick) in moves.Take(40))
        {
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"    tick {tick}: {fromCluster} -> {toCluster}"));
        }

        // Two eyes in different clusters the path really visits, so every call re-marks.
        (int Tick, int Cluster, (float X, float Y, float Z) Eye) first = path[0];
        (int Tick, int Cluster, (float X, float Y, float Z) Eye)? other = path.FirstOrDefault(step => step.Cluster != first.Cluster);

        if (other is not { } second || first.Cluster < 0 || second.Cluster < 0)
        {
            output.WriteLine("  the path has no second clustered eye; no re-mark to time");
            return;
        }

        WorldVisibility visibility = new(tree, pvs);
        long[] ticks = new long[remarks];

        for (int at = 0; at < remarks + 200; at++)
        {
            (float X, float Y, float Z) eye = at % 2 == 0 ? first.Eye : second.Eye;

            visibility.Leaves(eye.X, eye.Y, eye.Z, default);

            if (at >= 200)
            {
                ticks[at - 200] = visibility.MarkTicks;
            }
        }

        visibility.Leaves(first.Eye.X, first.Eye.Y, first.Eye.Z, default);
        visibility.Leaves(first.Eye.X, first.Eye.Y, first.Eye.Z, default);
        long hit = visibility.MarkTicks;

        Array.Sort(ticks);
        double Micro(long value) => value * 1_000_000d / Stopwatch.Frequency;

        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  one re-mark, clusters {first.Cluster} <-> {second.Cluster}, {remarks} timed after 200 warm: " +
            $"median {Micro(ticks[remarks / 2]):0.0} us, p10 {Micro(ticks[remarks / 10]):0.0}, p90 {Micro(ticks[remarks * 9 / 10]):0.0}, " +
            $"mean {Micro((long)ticks.Average()):0.0}; the control, a repeat from one eye: {Micro(hit):0.0} us"));
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  bound: {changes} changes in {to - from} ticks x median = {changes * Micro(ticks[remarks / 2]) / 1000:0.000} ms over the window"));
    }
}
