using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// Whether each player's `m_hGroundEntity` arrives on the wire or only comes from the class baseline (B768).
/// </summary>
/// <remarks>
/// **The question B768 turns on.** `m_hGroundEntity` is in `DT_LocalPlayerExclusive` (player.cpp:8117-8138), sent to the
/// player himself, and a SourceTV recording receives everyone's. `C_TFPlayer::BuildTransformations` dips a crouching
/// player whose `GetGroundEntity()` is NULL (c_tf_player.cpp:8764). The client holds what it received: the instance
/// baseline the entity was created from, then any delta that carried the field. So per player this counts the packets
/// that CARRIED the field and the distinct values they carried, beside the value the client holds at the end.
///
/// <code>
///   ground-entity tf2-live-pov-cp_process_final-bots-2026-11-05
/// </code>
/// </remarks>
public sealed class GroundEntityProbe : IProbe
{
    private const string Field = "m_hGroundEntity";

    /// <inheritdoc/>
    public string Name => "ground-entity";

    /// <inheritdoc/>
    public string Summary =>
        "per player, whether m_hGroundEntity arrives in deltas or only from the baseline, recorder marked: ground-entity <demo>";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0)
        {
            output.WriteLine("ground-entity <demo>");
            return;
        }

        if (DemoCorpus.Find(arguments[0], output) is not { } path)
        {
            output.WriteLine($"No demo named '{arguments[0]}'.");
            return;
        }

        byte[] bytes = File.ReadAllBytes(path);
        ushort protocol = (ushort)DemoHeader.Parse(bytes).NetworkProtocol;
        List<DemoCommand> commands = [.. DemoCommandReader.Read(bytes.AsMemory(DemoHeader.SizeBytes))];
        DemoCommand tables = commands.FirstOrDefault(command => command.Type == DemoCommandType.DataTables);

        if (tables.Type != DemoCommandType.DataTables)
        {
            output.WriteLine("The demo carries no dem_datatables.");
            return;
        }

        DemoSchema schema = SendTableParser.Parse(tables.Payload.Span, protocol);
        EntityDecoder decoder = new(schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));
        EntityStateTable entities = new(decoder);

        foreach (ServerClass serverClass in schema.ServerClasses)
        {
            entities.SetClassName(serverClass.Id, serverClass.ClassName);
        }

        NetDecodeState state = new() { NetworkProtocol = protocol };
        Dictionary<(int Index, int Serial), (int Carried, HashSet<long> Values)> players = [];
        int? slot = null;
        bool sourceTv = false;

        foreach (DemoCommand command in commands.Where(
            command => command.Type is DemoCommandType.Packet or DemoCommandType.Signon))
        {
            foreach (INetMessage message in NetMessageReader.Read(command.Payload.Span, state).Messages)
            {
                switch (message)
                {
                    case ServerInfoMessage server:
                        slot = server.PlayerSlot;
                        sourceTv = server.IsSourceTv;
                        break;

                    case CreateStringTableMessage { Name: BaselineBuilder.TableName } create:
                        BaselineBuilder.Replace(create.Entries, decoder);
                        break;

                    case UpdateStringTableMessage update
                        when state.StringTableName(update.TableId) == BaselineBuilder.TableName:
                        BaselineBuilder.Apply(update.Entries, decoder);
                        break;

                    case PacketEntitiesMessage snapshot when snapshot.LengthBits > 0:
                        entities.PacketTick = command.Tick;

                        foreach (DecodedEntity entity in decoder.Decode(snapshot.Body.Span, snapshot, snapshot.LengthBits))
                        {
                            entities.Apply(entity);

                            if (!entities.TryGet(entity.EntityIndex, out EntityState? live) || live.ClassName != "CTFPlayer")
                            {
                                continue;
                            }

                            (int Index, int Serial) key = (entity.EntityIndex, live.SerialNumber);

                            if (!players.TryGetValue(key, out (int Carried, HashSet<long> Values) seen))
                            {
                                seen = (0, []);
                            }

                            foreach (DecodedProperty carried in entity.Properties)
                            {
                                if (carried.Definition.Property.Name == Field)
                                {
                                    seen.Carried++;
                                    seen.Values.Add(carried.Value.AsInt);
                                }
                            }

                            players[key] = seen;
                        }

                        break;

                    default:
                        break;
                }
            }
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{Path.GetFileName(path)}: SourceTV {sourceTv}, recorder slot {slot?.ToString(CultureInfo.InvariantCulture) ?? "?"}"));

        foreach (((int index, int serial), (int carried, HashSet<long> values)) in players.OrderBy(pair => pair.Key))
        {
            string held = entities.TryGet(index, out EntityState? last) &&
                          last.Properties.Keys.FirstOrDefault(name => name.EndsWith(Field, StringComparison.Ordinal)) is { } name
                ? last.Properties[name].ToString()
                : "-";

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  player {index,3}/{serial,-4}{(slot + 1 == index ? " RECORDER" : "         ")}  carried in {carried,6} packets, " +
                $"{values.Count,3} distinct values; held at end {held}"));
        }
    }
}
