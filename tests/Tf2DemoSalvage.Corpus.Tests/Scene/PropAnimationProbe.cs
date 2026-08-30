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
/// Everything the wire says about an animated prop, update by update — a MEASUREMENT.
/// </summary>
/// <remarks>
/// **Written because a fix built on an assumption did not work.** The spawn cabinets were made to
/// restart their animation clock on a `m_nNewSequenceParity` change — the rule
/// `C_BaseAnimating::OnDataChanged` uses at <c>c_baseanimating.cpp:4737</c> — and the owner reports
/// they still stay open. The parity CHANGING was never measured; it was inferred from the sequence
/// changing. Those are different fields and the inference is exactly the kind this session has been
/// punished for.
///
/// **Three fields decide how a prop animates and this project reads one of them:**
///
/// - <c>m_bClientSideAnimation</c> — whether the client advances the cycle at all, or takes the
///   server's. `C_BaseAnimating::UpdateClientSideAnimation` is called only when it is set
///   (<c>c_baseanimating.cpp:4731</c>). Never read here.
/// - <c>DT_BaseAnimating.m_flPlaybackRate</c> — the third factor in
///   <c>addcycle = flInterval * cyclerate * m_flPlaybackRate</c> (<c>:5493</c>). Read for the
///   viewmodel only.
/// - <c>m_nNewSequenceParity</c> — that an animation began again. Read on the viewmodel until now.
///
/// Prints them per update rather than as a final state, because "the value at the end" cannot show
/// a transition and every question here is about one.
///
/// Reports numbers, asserts only the harness precondition (D38).
/// </remarks>
[Explicit("Diagnostic: reports every animation field a prop sends, update by update.")]
public sealed class PropAnimationProbe
{
    /// <summary>The recording the owner was watching.</summary>
    private const string Recording = "tf2-2026-pub-pov-clean";

    /// <summary>How many updates to print before stopping.</summary>
    private const int Lines = 30;

    [Test]
    public void Decode_AnAnimatedProp_ReportsEveryFieldItSends()
    {
        byte[] file = File.ReadAllBytes(Corpus.Demo(Recording));
        DemoHeader header = DemoHeader.Parse(file);

        List<DemoCommand> commands =
            [.. DemoCommandReader.Read(file.AsMemory(DemoHeader.SizeBytes))];

        DemoSchema schema = SendTableParser.Parse(
            commands.First(command => command.Type == DemoCommandType.DataTables).Payload.Span,
            (ushort)header.NetworkProtocol);

        EntityDecoder decoder = new(
            schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));

        EntityStateTable table = new(decoder);

        foreach (ServerClass serverClass in schema.ServerClasses)
        {
            table.SetClassName(serverClass.Id, serverClass.ClassName);
        }

        NetDecodeState state = new() { NetworkProtocol = (ushort)header.NetworkProtocol };

        // The cabinets the owner is looking at, from `SpawnRoomEntityProbe`'s reading of the map.
        HashSet<int> watched = [52, 54, 105, 312, 314];

        List<string> lines = [];
        HashSet<string> items = new(StringComparer.Ordinal);
        Dictionary<string, int> sent = new(StringComparer.Ordinal);

        foreach (DemoCommand command in commands
            .Where(c => c.Type is DemoCommandType.Signon or DemoCommandType.Packet))
        {
            foreach (INetMessage message in
                NetMessageReader.Read(command.Payload.Span, state).Messages)
            {
                switch (message)
                {
                    case CreateStringTableMessage { Name: BaselineBuilder.TableName } create:
                        BaselineBuilder.Apply(create.Entries, decoder);
                        continue;

                    case UpdateStringTableMessage update
                        when state.StringTableName(update.TableId) == BaselineBuilder.TableName:
                        BaselineBuilder.Apply(update.Entries, decoder);
                        continue;

                    case PacketEntitiesMessage { LengthBits: > 0 } snapshot:
                        foreach (DecodedEntity entity in
                            decoder.Decode(snapshot.Body.Span, snapshot, snapshot.LengthBits))
                        {
                            table.Apply(entity);

                            // **Every distinct (class, item) a WEAPON entity ever states**, over
                            // the whole recording rather than at the end. The owner sees one medic
                            // holding a medigun and another holding nothing, and a Kritzkrieg,
                            // Quick-Fix or Vaccinator is a different item index from the stock 211
                            // — so guessing the number is the wrong move when the demo states it.
                            if (table.TryGet(entity.EntityIndex, out EntityState? weapon) &&
                                (weapon.ClassName ?? string.Empty).Contains(
                                    "Weapon", StringComparison.Ordinal))
                            {
                                items.Add(
                                    $"{weapon.ClassName} item "
                                    + $"{weapon.ItemDefinitionIndex()?.ToString(CultureInfo.InvariantCulture) ?? "none"}");
                            }

                            if (!watched.Contains(entity.EntityIndex) ||
                                !table.TryGet(entity.EntityIndex, out EntityState? now) ||
                                !(now.ClassName ?? string.Empty).Contains(
                                    "DynamicProp", StringComparison.Ordinal))
                            {
                                continue;
                            }

                            // **Which fields the UPDATE itself carried**, separately from what the
                            // accumulated state holds. A value present in the state and never on
                            // the wire came from the class baseline, which is a different fact.
                            foreach (DecodedProperty property in entity.Properties)
                            {
                                string name =
                                    $"{property.Definition.OwnerTable}.{property.Definition.Property.Name}";

                                sent[name] = sent.GetValueOrDefault(name) + 1;
                            }

                            if (lines.Count < Lines)
                            {
                                lines.Add(
                                    $"UPDATE tick {command.Tick.ToString(CultureInfo.InvariantCulture)} "
                                    + $"entity {entity.EntityIndex.ToString(CultureInfo.InvariantCulture)} "
                                    + $"{entity.UpdateType} "
                                    + $"seq {Value(now, "DT_BaseAnimating.m_nSequence")} "
                                    + $"parity {Value(now, "DT_BaseAnimating.m_nNewSequenceParity")} "
                                    + $"cycle {Value(now, "DT_ServerAnimationData.m_flCycle")} "
                                    + $"clientside {Value(now, "DT_BaseAnimating.m_bClientSideAnimation")} "
                                    + $"rate {Value(now, "DT_BaseAnimating.m_flPlaybackRate")} "
                                    + $"animtime {Value(now, "DT_AnimTimeMustBeFirst.m_flAnimTime")}");
                            }
                        }

                        continue;

                    default:
                        continue;
                }
            }
        }

        foreach (string line in lines)
        {
            TestContext.Out.WriteLine(line);
        }

        // **What these entities ever put on the wire at all.** A field that never appears here is
        // one the server does not send for this class, which settles "we do not read it" against
        // "there is nothing to read".
        foreach ((string name, int count) in sent.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            TestContext.Out.WriteLine(
                $"SENT {name} x{count.ToString(CultureInfo.InvariantCulture)}");
        }

        foreach (string item in items.Order(StringComparer.Ordinal))
        {
            TestContext.Out.WriteLine($"ITEM {item}");
        }

        lines.Count.ShouldBeGreaterThan(0, "the walk saw no updates for the watched entities");
    }

    /// <summary>A property's value, or a word saying it is absent.</summary>
    private static string Value(EntityState entity, string key) =>
        entity.Integer(key)?.ToString(CultureInfo.InvariantCulture) ?? "-";
}
