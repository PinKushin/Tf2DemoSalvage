using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// When a parented prop LOSES its parent, snapshot by snapshot — a MEASUREMENT.
/// </summary>
/// <remarks>
/// **The owner's observation is what this is aimed at**: *"the things are showing up at tick 0, but
/// immedietly dissapearing when you hit play"*. That is the signature of a value that is present
/// when an entity enters and gone shortly after — not of a value that never arrives, which is what
/// two earlier readings concluded.
///
/// The entity table holds a good handle for these props — `moveparent 491597 -> slot 77` on entity
/// 78 — while the timeline reports `AttachedTo = none`. `DemoTimeline.RecordProp` reassigns
/// `track.AttachedTo` from `state.Attachment()` on EVERY update, so anything that empties the
/// accumulated state, or any update where the property is absent AND an origin is present,
/// overwrites a parent already learned.
///
/// **Two candidates, and they need different fixes.** A serial-number change makes
/// `EntityStateTable.Apply` build a fresh `EntityState` — the slot is a different entity, and every
/// property it does not resend is genuinely gone. A delta that simply omits `moveparent` is the
/// opposite: the engine keeps `m_pMoveParent` until told otherwise, so dropping it there would be
/// this project inventing an unparenting the server never sent.
///
/// This reports the snapshot index, the update type and the serial alongside the handle, so the two
/// are distinguishable rather than guessed between.
///
/// Explicit, and it asserts nothing about the demo beyond the precondition that the walk ran (D38).
/// </remarks>
[Explicit("Diagnostic: reports when a parented prop's moveparent appears and disappears.")]
public sealed class ParentLifetimeDiagnostic
{
    /// <summary>The recording the owner was watching.</summary>
    private const string Recording = "tf2-2026-pub-pov-clean";

    /// <summary>The spawn-door props, whose parent the timeline loses.</summary>
    private static readonly int[] Watched = [78, 81, 411];

    /// <summary>How many transitions to print before falling silent.</summary>
    /// <remarks>
    /// A cap rather than the whole history: the question is WHEN the value first changes, and a
    /// prop updated every tick for 28,000 ticks would bury that under its own noise.
    /// </remarks>
    private const int Transitions = 12;

    [Test]
    public void Decode_TheSpawnDoorProps_ReportsWhenTheirParentChanges()
    {
        string path = Corpus.Demo(Recording);

        byte[] file = File.ReadAllBytes(path);
        DemoHeader header = DemoHeader.Parse(file);
        NetDecodeState state = new() { NetworkProtocol = (ushort)header.NetworkProtocol };

        List<DemoCommand> commands =
            [.. DemoCommandReader.Read(file.AsMemory(DemoHeader.SizeBytes))];

        DemoCommand? tables = commands.FirstOrDefault(
            command => command.Type == DemoCommandType.DataTables);

        if (tables is not { } dataTables)
        {
            Assert.Ignore("the recording carries no send tables");
            return;
        }

        DemoSchema schema = SendTableParser.Parse(
            dataTables.Payload.Span, (ushort)header.NetworkProtocol);

        EntityDecoder decoder = new(
            schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));

        // **Every update for the watched slots, in order, with what it carried.** The point is the
        // TRANSITION, so both the presence of the property and the serial travel together — a
        // change in either explains a lost parent, and they mean different things.
        Dictionary<int, (long? Handle, int Serial)> last = [];
        Dictionary<int, int> printed = [];

        int snapshot = 0;

        foreach (DemoCommand command in commands
            .Where(c => c.Type is DemoCommandType.Signon or DemoCommandType.Packet))
        {
            foreach (INetMessage message in NetMessageReader.Read(command.Payload.Span, state)
                .Messages)
            {
                if (message is not PacketEntitiesMessage packet || packet.LengthBits <= 0)
                {
                    continue;
                }

                snapshot++;

                foreach (DecodedEntity entity in
                    decoder.Decode(packet.Body.Span, packet, packet.LengthBits))
                {
                    if (!Watched.Contains(entity.EntityIndex))
                    {
                        continue;
                    }

                    long? handle = null;

                    foreach (DecodedProperty property in entity.Properties)
                    {
                        if (property.Definition.Property.Name.Contains(
                            "moveparent", StringComparison.OrdinalIgnoreCase))
                        {
                            handle = property.Value.AsInt;
                        }
                    }

                    (long? Handle, int Serial) now = (handle, entity.SerialNumber);

                    bool changed = !last.TryGetValue(entity.EntityIndex, out (long? Handle, int Serial) was)
                        || was.Serial != now.Serial
                        || (was.Handle is null) != (now.Handle is null);

                    last[entity.EntityIndex] = now;

                    if (!changed || printed.GetValueOrDefault(entity.EntityIndex) >= Transitions)
                    {
                        continue;
                    }

                    printed[entity.EntityIndex] =
                        printed.GetValueOrDefault(entity.EntityIndex) + 1;

                    TestContext.Out.WriteLine(
                        $"LIFE {entity.EntityIndex} snapshot {snapshot} tick {command.Tick}: "
                        + $"{entity.UpdateType}, serial {entity.SerialNumber}, "
                        + $"{entity.Properties.Count} properties, moveparent "
                        + (handle is { } value
                            ? value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                            : "not in this update"));
                }
            }
        }

        // A precondition on the HARNESS, not a claim about the demo.
        snapshot.ShouldBeGreaterThan(0, "the recording yielded no entity snapshots at all");
    }
}
