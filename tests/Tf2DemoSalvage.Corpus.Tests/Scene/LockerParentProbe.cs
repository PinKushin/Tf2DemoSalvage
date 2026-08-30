using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Core.Scene;

// Namespaced away from `Tf2DemoSalvage.Corpus.Tests.*`, where `Corpus` binds to the namespace
// rather than to the helper class — the same reason `CorpusPlayerOriginTests` beside it does.
namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// Where a spawn resupply cabinet's move parent comes from — a MEASUREMENT.
/// </summary>
/// <remarks>
/// **The map settles what the answer should be, so this only has to find where ours diverges.**
/// `cp_fulgur`'s entity lump, read by `SpawnRoomEntityProbe`:
///
/// <code>
///   PROP prop_dynamic models/props_gameplay/resupply_locker.mdl
///          name    (prop_locker_blu_5)
///          origin  (3440 -2095.56 240.16)
///          parent  [unparented]
/// </code>
///
/// **Every one of the eight lockers is unparented**, and their `origin` keys are world positions.
/// The viewer's composed-position log said otherwise:
///
/// <code>
///   resupply_locker.mdl composed onto 434: parent (2246 2384 59) + local (3440 -2096 240)
///                                        = (5686 288 299)
/// </code>
///
/// `(3440 -2096 240)` is `prop_locker_blu_5`'s WORLD origin to the rounding, and
/// `(2246 2384 59)` is `door_red_large_1`, the `func_door` brush `*4` at `(2245.5 2384 58.5)`. So a
/// cabinet the map never parented was composed onto a door on the other side of the map, and its
/// own world position was added as though it were a local offset. That is the whole visible defect:
/// the cabinet is drawn thousands of units from the room it belongs to.
///
/// **Serial checking did not remove it** (B231's first half), so the handle is not merely stale —
/// something is putting a live, matching handle into this entity's `moveparent`. The candidates
/// this reports on, in order of how quietly each would fail:
///
/// 1. **The class instance baseline.** `EffectiveProperties` merges it on creation, and it is one
///    blob per server class. If `CDynamicProp`'s baseline was captured from a prop that IS parented
///    — and half the props on this map are, to their gate brushes — then every unparented
///    `CDynamicProp` inherits that parent unless its own update overrides it.
/// 2. **A missed override.** The server would send an explicit no-parent for a locker whose
///    baseline says otherwise; failing to decode that property leaves the baseline standing.
/// 3. **A genuine parent**, which would make the map reading wrong rather than the decode.
///
/// Reports numbers and asserts only the harness precondition (D38).
/// </remarks>
[Explicit("Diagnostic: reports where a resupply locker's move parent comes from.")]
public sealed class LockerParentProbe
{
    /// <summary>The recording the owner was watching.</summary>
    private const string Recording = "tf2-2026-pub-pov-clean";

    /// <summary>The property the whole question turns on.</summary>
    private const string MoveParent = "DT_BaseEntity.moveparent";

    /// <summary>What a track takes its name from.</summary>
    private const string ModelIndexProperty = "DT_BaseEntity.m_nModelIndex";

    /// <summary>How many snapshots to walk before reporting.</summary>
    private const int PacketLimit = 40000;

    [Test]
    public void Decode_ALockersMoveParent_ReportsWhereTheValueCameFrom()
    {
        byte[] file = File.ReadAllBytes(Corpus.Demo(Recording));
        DemoHeader header = DemoHeader.Parse(file);

        List<DemoCommand> commands =
            [.. DemoCommandReader.Read(file.AsMemory(DemoHeader.SizeBytes))];

        DemoCommand tables = commands.First(c => c.Type == DemoCommandType.DataTables);

        DemoSchema schema = SendTableParser.Parse(
            tables.Payload.Span, (ushort)header.NetworkProtocol);

        EntityDecoder decoder = new(
            schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));

        // **Which class ids can even be a locker.** The model is not on the wire as a string, so the
        // entity is found by class and then by model index against the model string table — which
        // this walk resolves as it goes rather than assuming an index.
        Dictionary<int, string> classNames = schema.ServerClasses.ToDictionary(
            serverClass => serverClass.Id,
            serverClass => serverClass.ClassName,
            EqualityComparer<int>.Default);

        EntityStateTable table = new(decoder);

        foreach (ServerClass serverClass in schema.ServerClasses)
        {
            table.SetClassName(serverClass.Id, serverClass.ClassName);
        }

        NetDecodeState state = new() { NetworkProtocol = (ushort)header.NetworkProtocol };

        // **Every `CDynamicProp` update, with the parent value AND whether the wire carried it.**
        // The distinction is the point: a value present in the accumulated state but absent from
        // every update is a value the baseline supplied, which is candidate 1 above.
        List<string> lines = [];
        int updates = 0;
        int carriedParent = 0;
        int snapshots = 0;

        foreach (DemoCommand command in commands
            .Where(c => c.Type is DemoCommandType.Signon or DemoCommandType.Packet))
        {
            foreach (INetMessage message in
                NetMessageReader.Read(command.Payload.Span, state).Messages)
            {
                // **The instance baselines, without which the walk below measures nothing.** A
                // first pass omitted these and then reported `BASELINE CDynamicProp moveparent
                // ABSENT` — which was a fact about an empty decoder, not about the demo, and it
                // very nearly closed off the right answer. `DemoTimeline` applies them at
                // `DemoTimeline.cs:1086`, so a probe that does not is decoding a different demo.
                switch (message)
                {
                    case CreateStringTableMessage { Name: BaselineBuilder.TableName } create:
                        BaselineBuilder.Apply(create.Entries, decoder);
                        continue;

                    case UpdateStringTableMessage update
                        when state.StringTableName(update.TableId) == BaselineBuilder.TableName:
                        BaselineBuilder.Apply(update.Entries, decoder);
                        continue;

                    default:
                        break;
                }

                if (message is not PacketEntitiesMessage snapshot || snapshot.LengthBits <= 0)
                {
                    continue;
                }

                foreach (DecodedEntity entity in
                    decoder.Decode(snapshot.Body.Span, snapshot, snapshot.LengthBits))
                {
                    table.Apply(entity);

                    if (!IsProp(classNames, entity.ClassId))
                    {
                        continue;
                    }

                    updates++;

                    // **`Cast` first, because `DecodedProperty` is a STRUCT.** A bare
                    // `FirstOrDefault` returns `default(DecodedProperty)` when nothing matches, and
                    // a `is not null` test on that is a pattern that compiles and never fires.
                    DecodedProperty? onWire = entity.Properties
                        .Cast<DecodedProperty?>()
                        .FirstOrDefault(property => Name(property!.Value) == MoveParent);

                    if (onWire is not null)
                    {
                        carriedParent++;
                    }

                    // **Slot 432 and its parent 434 only, and with the MODEL INDEX.** The tracks
                    // report slot 432 as a windowed door at tick 0 and a resupply locker at tick
                    // 11, while it keeps its move parent throughout — so either the entity changed
                    // model, or the index is stable and the precache table underneath it moved.
                    // Printing the index separates those two outright.
                    if (entity.EntityIndex == 432 &&
                        lines.Count < 40 &&
                        table.TryGet(entity.EntityIndex, out EntityState? now))
                    {
                        lines.Add(
                            $"UPDATE {entity.UpdateType} 432 wire-serial {entity.SerialNumber} "
                            + $"state-serial {now.SerialNumber} "
                            + $"props {entity.Properties.Count} "
                            + $"modelindex {now.ModelIndex()?.ToString(CultureInfo.InvariantCulture)
                                ?? "none"} "
                            + $"origin {Where(now)} "
                            + $"wire-moveparent {Raw(onWire)} "
                            + $"state-moveparent {now.Integer(MoveParent)
                                ?.ToString(CultureInfo.InvariantCulture) ?? "none"}");
                    }
                }

                if (++snapshots >= PacketLimit)
                {
                    break;
                }
            }
        }

        TestContext.Out.WriteLine(
            $"{updates} prop updates, {carriedParent} carried moveparent on the wire");

        foreach (string line in lines)
        {
            TestContext.Out.WriteLine(line);
        }

        // **What the accumulated state ended up believing**, which is what the scene reads.
        foreach (EntityState prop in table.All
            .Where(entity => IsProp(classNames, entity.ClassId))
            .OrderBy(entity => entity.EntityIndex))
        {
            if (prop.Integer(MoveParent) is not { } raw)
            {
                continue;
            }

            TestContext.Out.WriteLine(
                $"STATE {prop.EntityIndex} class {prop.ClassName ?? "?"} "
                + $"raw {raw.ToString(CultureInfo.InvariantCulture)} "
                + $"slot {(raw & 2047).ToString(CultureInfo.InvariantCulture)} "
                + $"serial {(raw >> 11).ToString(CultureInfo.InvariantCulture)} "
                + $"attachment {prop.Attachment()?.ToString(CultureInfo.InvariantCulture)
                    ?? "none"} "
                + $"resolved {table.Resolve(raw)?.ToString(CultureInfo.InvariantCulture)
                    ?? "NOTHING"} "
                + $"origin {Where(prop)} "
                + $"model {prop.ModelIndex()?.ToString(CultureInfo.InvariantCulture) ?? "none"}");
        }

        // **The baseline itself, which is candidate 1 stated directly.** An `Enter` carrying no
        // `moveparent` on the wire but a parented entity in the table can only have got it here.
        foreach (ServerClass serverClass in schema.ServerClasses
            .Where(serverClass => serverClass.ClassName.Contains(
                "Prop", StringComparison.Ordinal)))
        {
            DecodedEntity probe = new(
                1, serverClass.Id, 0, EntityUpdateType.Enter, []);

            IReadOnlyList<DecodedProperty> baseline = decoder.EffectiveProperties(probe);

            DecodedProperty? parent = baseline
                .Cast<DecodedProperty?>()
                .FirstOrDefault(property => Name(property!.Value) == MoveParent);

            // **The model index too, which is what a track is NAMED from.** A track fixes its model
            // path at construction, so whatever the baseline supplies on the creating update is the
            // name that entity carries for the rest of the demo.
            DecodedProperty? model = baseline
                .Cast<DecodedProperty?>()
                .FirstOrDefault(property => Name(property!.Value) == ModelIndexProperty);

            TestContext.Out.WriteLine(
                $"BASELINE {serverClass.ClassName} of {baseline.Count} properties: "
                + $"moveparent {Raw(parent)} modelindex {Raw(model)}");
        }

        // **The TRACKS, which is the layer between the state table and the scene.** Everything above
        // says the wire is right: no locker carries a move parent, and no prop class's baseline
        // supplies one. `ParentedPropDiagnostic` nonetheless reports
        // `432 resupply_locker.mdl ... parent [434] at [(3440 -2096 240) | (4 -1 -58) | (2 0 -59)]`
        // — a cabinet's model path carrying a windowed door's parent AND its local offsets.
        //
        // A track fixes its model path at construction and never revises it, so a track that
        // outlives the entity it was named for keeps the wrong name. This prints every track for the
        // slots in question, with the serial that is supposed to end one.
        DemoTimeline timeline = TimelineCache.For(Corpus.Demo(Recording));

        foreach (ScenePropTrack track in timeline.Props
            .Where(track => track.EntityIndex is 432 or 434
                || track.ModelPath.Contains("resupply_locker", StringComparison.OrdinalIgnoreCase))
            .OrderBy(track => track.EntityIndex)
            .ThenBy(track => track.FirstTick))
        {
            TestContext.Out.WriteLine(
                $"TRACK {track.EntityIndex} serial {track.SerialNumber} "
                + $"from tick {track.FirstTick} over {track.Keyframes.Count} keyframes "
                + $"parent {track.AttachedTo?.ToString(CultureInfo.InvariantCulture) ?? "none"} "
                + $"merged {track.BoneMerged} "
                + $"{track.ModelPath}");
        }

        // **Every write to the model precache, and every one that CHANGES an index's meaning.**
        // Entity 432 keeps model index 1177 from tick 0 to the end of the demo, and the timeline
        // labels it `windowed_door.mdl` at tick 0 and `resupply_locker.mdl` from tick 11. Same
        // index, same entity, two answers — so the table underneath moved.
        //
        // `ModelPrecache.Apply` writes `into[entry.Index] = entry.Text`, keyed by the entry's own
        // declared index rather than by arrival order, so a shift can only come from entries
        // arriving with wrong indices or wrong text. This counts the contradictions directly.
        Dictionary<int, string> precache = [];
        List<string> rewrites = [];
        int writes = 0;

        NetDecodeState tableState = new() { NetworkProtocol = (ushort)header.NetworkProtocol };

        foreach (DemoCommand command in commands
            .Where(c => c.Type is DemoCommandType.Signon or DemoCommandType.Packet))
        {
            foreach (INetMessage message in
                NetMessageReader.Read(command.Payload.Span, tableState).Messages)
            {
                IReadOnlyList<StringTableEntry>? entries = message switch
                {
                    CreateStringTableMessage { Name: ModelPrecache.TableName } create
                        => create.Entries,
                    UpdateStringTableMessage update
                        when tableState.StringTableName(update.TableId) == ModelPrecache.TableName
                        => update.Entries,
                    _ => null,
                };

                if (entries is null)
                {
                    continue;
                }

                string kind = message is CreateStringTableMessage ? "CREATE" : "UPDATE";

                foreach (StringTableEntry entry in entries)
                {
                    if (entry.Index < 0 || string.IsNullOrEmpty(entry.Text))
                    {
                        continue;
                    }

                    writes++;

                    if (precache.TryGetValue(entry.Index, out string? had) &&
                        !string.Equals(had, entry.Text, StringComparison.Ordinal) &&
                        rewrites.Count < 20)
                    {
                        rewrites.Add(
                            $"REWRITE tick {command.Tick} {kind} index {entry.Index}: "
                            + $"{had} -> {entry.Text}");
                    }

                    precache[entry.Index] = entry.Text;
                }
            }
        }

        TestContext.Out.WriteLine(
            $"PRECACHE {writes} writes, {precache.Count} distinct indices, "
            + $"index 1177 ends as {precache.GetValueOrDefault(1177, "NOTHING")}");

        foreach (string rewrite in rewrites)
        {
            TestContext.Out.WriteLine(rewrite);
        }

        updates.ShouldBeGreaterThan(0, "the demo produced no prop updates at all");
    }

    /// <summary>Whether a class id is one of the dynamic-prop classes.</summary>
    private static bool IsProp(Dictionary<int, string> names, int classId) =>
        names.TryGetValue(classId, out string? name)
        && name.Contains("DynamicProp", StringComparison.Ordinal);

    /// <summary>A property's fully qualified name, as the state table keys it.</summary>
    private static string Name(DecodedProperty property) =>
        $"{property.Definition.OwnerTable}.{property.Definition.Property.Name}";

    /// <summary>A property's integer value, or a word saying it was absent.</summary>
    private static string Raw(DecodedProperty? property) =>
        property is not { } present
            ? "ABSENT"
            : present.Value.AsInt.ToString(CultureInfo.InvariantCulture);

    /// <summary>An entity's origin, printed.</summary>
    private static string Where(EntityState entity) =>
        entity.Origin() is { } at ? $"({at.X:0} {at.Y:0} {at.Z:0})" : "none";
}
