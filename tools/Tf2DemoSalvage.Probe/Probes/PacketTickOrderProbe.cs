using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Container;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// Entity snapshots in signon commands, and packets stamped below the running maximum of every packet before them (B881).
/// </summary>
/// <remarks>
/// **Why**: `DemoTimeline.RecordProp` hands a track each command's own tick, for a signon as well as a packet, and a
/// signon carries the server's tick (70,718 on z1800). If a signon holds a snapshot that updates an entity, or a packet's
/// tick falls below an earlier packet's, a track can receive a tick that is not its arrival. **Controls**: the signon
/// commands counted on the same stream (an absence of signon snapshots means nothing without them), and the packets
/// repeating the running maximum, which a POV demo has (finding 106). The snapshot walk is production's
/// (<see cref="DemoCorpus.EntitySnapshots"/>).
///
/// <code>
///   packet-tick-order                                   (every corpus demo)
///   packet-tick-order z1800 tf2-2007-build3258-spec-pov-cp_granary
/// </code>
/// </remarks>
public sealed class PacketTickOrderProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "packet-tick-order";

    /// <inheritdoc/>
    public string Summary =>
        "entity snapshots in signons, and packets stamped below an earlier packet's tick (B881): packet-tick-order [demo...]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        // No arguments is the census: every demo the corpus walk holds (gcor and lcor), counted so a run can be checked.
        if (arguments.Count == 0)
        {
            IReadOnlyList<string> files = DemoCorpus.Files(output);
            output.WriteLine($"packet-tick-order over {files.Count} corpus demos");

            foreach (string file in files)
            {
                Report(output, Path.GetFileNameWithoutExtension(file), File.ReadAllBytes(file));
            }

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
        // **Every packet, snapshot or not** (review 9, Fable): `DemoPlayer.FirstPacketPast` reads up to the running maximum of
        // ALL packet ticks, so a packet below it is counted whatever it carries — a superset of the snapshot packets that
        // reach `RecordProp`. **The control is the repeats**: a POV demo's packets repeat a tick (finding 106), so packets
        // EQUAL to the running maximum must appear on a POV demo — the comparison's other side, seen working.
        int packets = 0;
        int below = 0;
        int repeats = 0;
        int worstDrop = 0;
        int signons = 0;
        int? firstSignon = null;
        int runningMax = int.MinValue;

        foreach (DemoCommand command in DemoCommandReader.Read(bytes.AsMemory(DemoHeader.SizeBytes)))
        {
            if (command.Type == DemoCommandType.Signon)
            {
                signons++;
                firstSignon ??= command.Tick;
                continue;
            }

            if (command.Type != DemoCommandType.Packet)
            {
                continue;
            }

            packets++;

            if (command.Tick < runningMax)
            {
                below++;
                worstDrop = Math.Max(worstDrop, runningMax - command.Tick);
            }
            else if (command.Tick == runningMax)
            {
                repeats++;
            }

            runningMax = Math.Max(runningMax, command.Tick);
        }

        // Signon snapshots through production's walk; its control is the signon commands counted above, on the same stream.
        int inSignon = 0;
        int signonUpdates = 0;
        int snapshots = 0;

        foreach (DemoEntitySnapshot entry in DemoCorpus.EntitySnapshots(bytes))
        {
            snapshots++;

            if (entry.Command.Type == DemoCommandType.Signon)
            {
                inSignon++;
                signonUpdates += entry.Snapshot.UpdatedEntries;
            }
        }

        string first = firstSignon?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-";
        output.WriteLine(
            $"{name}: {signons} signon commands (first at tick {first}), {inSignon} signon snapshots ({signonUpdates} " +
            $"entity updates) of {snapshots}; {packets} packets, {below} below the running max (worst drop {worstDrop}), " +
            $"{repeats} repeating it");
    }
}
