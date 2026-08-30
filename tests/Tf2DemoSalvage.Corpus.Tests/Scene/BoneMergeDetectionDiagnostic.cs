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
/// Whether <c>EF_BONEMERGE</c> actually reaches this project — a MEASUREMENT.
/// </summary>
/// <remarks>
/// **Written because a claim was repeated instead of checked.** `EntityState.Attachment`'s own
/// remarks say *"a `CTFWearable` sends `moveparent` and no `m_fEffects` at all"*, and that sentence
/// was used to argue this project cannot tell a bone-merged follower from an ordinary parented one
/// — an argument that then justified a change which broke every wearable in the viewer. The owner
/// asked the right question: *"why cant we?"*
///
/// **The SDK says we should be able to.** <c>CBaseEntity::FollowEntity</c>
/// (<c>baseentity_shared.cpp:2360</c>), called with <c>bBoneMerge = true</c> from
/// <c>econ_wearable.cpp:192</c>:
///
/// <code>
///   SetParent( pBaseEntity );
///   SetMoveType( MOVETYPE_NONE );
///   if ( bBoneMerge )
///       AddEffects( EF_BONEMERGE );
///   AddSolidFlags( FSOLID_NOT_SOLID );
///   SetLocalOrigin( vec3_origin );
///   SetLocalAngles( vec3_angle );
/// </code>
///
/// <c>m_fEffects</c> is networked at <c>EF_MAX_BITS</c> unsigned (<c>baseentity.cpp:278</c>), so the
/// flag is on the wire. Whether it ARRIVES here — through the class baseline, through a delta, or
/// not at all — is a fact about this decoder, and only a measurement can say.
///
/// **The second half of that snippet matters as much as the first.** The engine zeroes a
/// follower's local origin and angles itself, so a bone-merged wearable's <c>m_vecOrigin</c> is
/// genuinely (0,0,0) on the wire rather than something this project has to blank out.
///
/// Explicit, and it asserts nothing about the demo beyond the precondition that the walk ran (D38).
/// </remarks>
[Explicit("Diagnostic: reports whether EF_BONEMERGE reaches the entity table.")]
public sealed class BoneMergeDetectionDiagnostic
{
    /// <summary>The recording the owner was watching.</summary>
    private const string Recording = "tf2-2026-pub-pov-clean";

    [Test]
    public void Decode_TheEntities_ReportsHowManyParentedOnesCarryBoneMerge()
    {
        EntityStateTable table = Accumulate(Corpus.Demo(Recording), packetLimit: 4000);

        int entities = 0;
        int parented = 0;
        int merged = 0;
        int parentedAndMerged = 0;
        int effectsAbsent = 0;
        int parentedEffectsAbsent = 0;

        Dictionary<string, (int Parented, int Merged, int NoEffects)> byClass = [];

        foreach (EntityState entity in table.All)
        {
            entities++;

            bool hasParent = entity.Attachment() is not null;
            long? effects = entity.Properties.TryGetValue("DT_BaseEntity.m_fEffects", out PropertyValue value)
                ? value.AsInt
                : null;

            bool boneMerged = ((effects ?? 0) & 0x001) != 0;
            bool noEffects = effects is null;

            if (noEffects)
            {
                effectsAbsent++;
            }

            if (boneMerged)
            {
                merged++;
            }

            if (!hasParent)
            {
                continue;
            }

            parented++;

            if (boneMerged)
            {
                parentedAndMerged++;
            }

            if (noEffects)
            {
                parentedEffectsAbsent++;
            }

            string name = entity.ClassName ?? "?";

            (int p, int m, int n) = byClass.GetValueOrDefault(name);

            byClass[name] = (p + 1, m + (boneMerged ? 1 : 0), n + (noEffects ? 1 : 0));
        }

        TestContext.Out.WriteLine(
            $"{entities} entities: {parented} parented, {merged} bone-merged, "
            + $"{parentedAndMerged} both, {effectsAbsent} with no m_fEffects at all "
            + $"({parentedEffectsAbsent} of those parented)");

        foreach ((string name, (int p, int m, int n)) in byClass
            .OrderByDescending(entry => entry.Value.Parented))
        {
            TestContext.Out.WriteLine(
                $"  {name}: {p} parented, {m} bone-merged, {n} with no m_fEffects");
        }

        // **Every key a wearable actually carries that mentions effects or a parent**, because
        // "the property is absent" and "we looked under the wrong table" produce the identical
        // answer from `Effects()` — which tries `DT_BaseEntity` and `DT_BaseViewModel` and nothing
        // else. `docs/memory/a-property-name-needs-its-declaring-table.md` is this exact shape, and
        // the engine cannot have the problem because it resolves a flattened index rather than a
        // name.
        EntityState? wearable = table.All.FirstOrDefault(
            entity => string.Equals(entity.ClassName, "CTFWearable", StringComparison.Ordinal));

        if (wearable is null)
        {
            TestContext.Out.WriteLine("no CTFWearable in this recording");
        }
        else
        {
            TestContext.Out.WriteLine(
                $"CTFWearable {wearable.EntityIndex} carries {wearable.Properties.Count} properties");

            foreach (string key in wearable.Properties.Keys
                .Where(name =>
                    name.Contains("ffect", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("arent", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("oveType", StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.Ordinal))
            {
                TestContext.Out.WriteLine($"  KEY {key} = {wearable.Properties[key]}");
            }
        }

        // **Which tables a wearable's own properties are declared by**, because that is the only
        // thing in a demo that can stand in for `CEconWearable::Spawn` running on the client.
        // `AddEffects( EF_BONEMERGE )` there is outside the server-only guard and fires for every
        // wearable the client creates, remote players' included — which is why cosmetics are
        // visible in POV and STV demos alike, and why the flag is never on the wire. The demo
        // carries the send-table inheritance in `dem_datatables`, so the class IS recoverable.
        if (wearable is not null)
        {
            TestContext.Out.WriteLine(
                "tables a wearable declares: " + string.Join(
                    ", ",
                    wearable.Properties.Keys
                        .Select(key => key.Split('.')[0])
                        .Distinct(StringComparer.Ordinal)
                        .Order(StringComparer.Ordinal)));
        }

        // **The proposed rule, scored against the real demo before any production code uses it.**
        // `SchemaClasses.BoneMergesItself` derives what `CEconWearable::Spawn` sets on the client;
        // this reports how it classifies every parented class, so the two columns can be compared
        // by eye against what each class actually IS. A predicate that is right in a synthetic test
        // and wrong on the corpus is the shape that has already cost two reverts tonight.
        DemoSchema? schema = SchemaOf(Corpus.Demo(Recording));

        if (schema is null)
        {
            TestContext.Out.WriteLine("no schema in this recording");
        }
        else
        {
            foreach (ServerClass serverClass in schema.ServerClasses
                .Where(candidate => byClass.ContainsKey(candidate.ClassName))
                .OrderBy(candidate => candidate.ClassName, StringComparer.Ordinal))
            {
                TestContext.Out.WriteLine(
                    $"  RULE {serverClass.ClassName} ({serverClass.TableName}): "
                    + $"bone-merges itself = "
                    + SchemaClasses.BoneMergesItself(schema, serverClass.TableName));
            }
        }

        // A precondition on the HARNESS, not a claim about the demo.
        entities.ShouldBeGreaterThan(0, "the demo decoded into no entities at all");
    }

    /// <summary>The demo's send tables, or null when it carries none.</summary>
    private static DemoSchema? SchemaOf(string path)
    {
        byte[] file = File.ReadAllBytes(path);
        DemoHeader header = DemoHeader.Parse(file);

        DemoCommand? tables = DemoCommandReader.Read(file.AsMemory(DemoHeader.SizeBytes))
            .FirstOrDefault(command => command.Type == DemoCommandType.DataTables);

        return tables is { } dataTables
            ? SendTableParser.Parse(dataTables.Payload.Span, (ushort)header.NetworkProtocol)
            : null;
    }

    /// <summary>Every entity a demo's snapshots produce.</summary>
    /// <remarks>Copied from `CorpusRenderModeDiagnostic.Accumulate`, which records why.</remarks>
    private static EntityStateTable Accumulate(string path, int packetLimit)
    {
        byte[] file = File.ReadAllBytes(path);
        DemoHeader header = DemoHeader.Parse(file);
        NetDecodeState state = new() { NetworkProtocol = (ushort)header.NetworkProtocol };

        List<DemoCommand> commands =
            [.. DemoCommandReader.Read(file.AsMemory(DemoHeader.SizeBytes))];

        EntityDecoder? decoder = null;
        IReadOnlyList<ServerClass> classes = [];
        DemoCommand? tables = commands.FirstOrDefault(c => c.Type == DemoCommandType.DataTables);

        if (tables is { } dataTables)
        {
            DemoSchema schema = SendTableParser.Parse(
                dataTables.Payload.Span, (ushort)header.NetworkProtocol);

            decoder = new EntityDecoder(
                schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));

            classes = schema.ServerClasses;
        }

        EntityStateTable table = new((IEntityBaselines?)decoder ?? EntityBaselines.None);

        foreach (ServerClass serverClass in classes)
        {
            table.SetClassName(serverClass.Id, serverClass.ClassName);
        }

        if (decoder is null)
        {
            return table;
        }

        int snapshots = 0;

        foreach (DemoCommand command in commands
            .Where(c => c.Type is DemoCommandType.Signon or DemoCommandType.Packet))
        {
            foreach (INetMessage message in NetMessageReader.Read(command.Payload.Span, state)
                .Messages)
            {
                if (message is not PacketEntitiesMessage snapshot || snapshot.LengthBits <= 0)
                {
                    continue;
                }

                foreach (DecodedEntity entity in
                    decoder.Decode(snapshot.Body.Span, snapshot, snapshot.LengthBits))
                {
                    table.Apply(entity);
                }

                if (++snapshots >= packetLimit)
                {
                    return table;
                }
            }
        }

        return table;
    }
}
