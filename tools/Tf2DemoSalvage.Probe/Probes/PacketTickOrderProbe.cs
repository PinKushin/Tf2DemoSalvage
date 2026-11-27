using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Container;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// Entity snapshots a scene track would take below its newest tick: those in signon commands, and packets stamped below
/// the running maximum of earlier snapshot ticks (B881).
/// </summary>
/// <remarks>
/// **Why**: `DemoTimeline.RecordProp` hands a track each command's own tick, for a signon as well as a packet, and a
/// signon carries the server's tick (70,718 on z1800). If a signon holds a snapshot that updates an entity, or a packet's
/// tick falls below an earlier one's, that track receives a tick below its newest. **The control** is the count of all
/// snapshots, which must be above zero, and each demo's first signon and packet ticks, which show the axis was read.
/// The walk is production's (<see cref="DemoCorpus.EntitySnapshots"/>).
///
/// <code>
///   packet-tick-order z1800 tf2-2007-build3258-spec-pov-cp_granary
/// </code>
/// </remarks>
public sealed class PacketTickOrderProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "packet-tick-order";

    /// <inheritdoc/>
    public string Summary =>
        "entity snapshots in signons, and snapshots stamped below an earlier one's tick (B881): packet-tick-order <demo>...";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0)
        {
            output.WriteLine("packet-tick-order <demo>...");
            return;
        }

        foreach (string name in arguments)
        {
            if (DemoCorpus.Find(name, output) is not { } path)
            {
                output.WriteLine($"No demo named '{name}'.");
                continue;
            }

            Report(output, Path.GetFileNameWithoutExtension(path), File.ReadAllBytes(path));
        }
    }

    /// <summary>One demo's counts.</summary>
    private static void Report(TextWriter output, string name, byte[] bytes)
    {
        int snapshots = 0;
        int inSignon = 0;
        int signonUpdates = 0;
        int below = 0;
        int belowUpdates = 0;
        int worstDrop = 0;
        int? firstSignon = null;
        int? firstPacket = null;
        int runningMax = int.MinValue;

        // The control for "no snapshot in a signon": the signon commands themselves, and their first tick, read from the
        // same command stream — an absence of snapshots means nothing if no signon was seen at all.
        DemoCommand[] signons = [.. DemoCommandReader.Read(bytes.AsMemory(DemoHeader.SizeBytes))
            .Where(command => command.Type == DemoCommandType.Signon)];

        output.WriteLine(
            $"{name}: {signons.Length} signon commands, first at tick " +
            $"{(signons.Length > 0 ? signons[0].Tick.ToString(System.Globalization.CultureInfo.InvariantCulture) : "-")}");

        foreach (DemoEntitySnapshot entry in DemoCorpus.EntitySnapshots(bytes))
        {
            snapshots++;
            int tick = entry.Command.Tick;

            if (entry.Command.Type == DemoCommandType.Signon)
            {
                firstSignon ??= tick;
                inSignon++;
                signonUpdates += entry.Snapshot.UpdatedEntries;
                continue;
            }

            firstPacket ??= tick;

            if (tick < runningMax)
            {
                below++;
                belowUpdates += entry.Snapshot.UpdatedEntries;
                worstDrop = Math.Max(worstDrop, runningMax - tick);
            }

            runningMax = Math.Max(runningMax, tick);
        }

        output.WriteLine(
            $"{name}: {snapshots} snapshots; in signons {inSignon} ({signonUpdates} entity updates), first signon tick " +
            $"{firstSignon?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"}; first packet tick " +
            $"{firstPacket?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"}; below the running max " +
            $"{below} ({belowUpdates} entity updates, worst drop {worstDrop})");
    }
}
