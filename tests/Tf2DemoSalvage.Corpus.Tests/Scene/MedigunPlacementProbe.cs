using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Core.Scene;

// Namespaced away from `Tf2DemoSalvage.Corpus.Tests.*`, where `Corpus` binds to the namespace
// rather than to the helper class — the same reason `CorpusPlayerOriginTests` beside it does.
namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// Where a medigun ends up against a weapon that works — a MEASUREMENT.
/// </summary>
/// <remarks>
/// **The owner's report is a CONTRAST, and that is what makes it tractable:** *"mediguns still are
/// not drawing on other players too, but the flamethrower, and it looks like everything else,
/// draws"*. One weapon of a pair fails, so whatever both share is not the cause and the difference
/// between them is.
///
/// **Two false starts are kept here, because both were plausible and both were wrong.**
///
/// The render log says `c_medigun.mdl` IS drawn — `drawing 2 of 2 batches ... at
/// (1054, -2281.4, 294.7)` — which reads as "drawn in the wrong place". Every track carrying that
/// model then reported `parent none, merged False`, which reads as "the bone-merge rule is not
/// firing for weapons". Neither survived: those tracks are the **ten `CTFDroppedWeapon` entities**
/// lying on the floor, which are correctly unmerged and correctly at a world position, and
/// `BoneMergesItself` is `True` for `CWeaponMedigun` exactly as it should be.
///
/// **What is actually wrong is that the held weapon has no model index at all.** The three live
/// `CWeaponMedigun` entities are recognised perfectly — bone-merged, owned by players 8 and 21 —
/// and network neither `m_nModelIndex` nor `m_iWorldModelIndex`:
///
/// <code>
///   ENTITY  968 CTFRocketLauncher model  996 worldmodel  426 merged True attachment 18
///   ENTITY 1100 CTFFlameThrower   model none worldmodel  225 merged True attachment  5
///   ENTITY  940 CTFMinigun        model none worldmodel  393 merged True attachment 24
///   ENTITY 1017 CWeaponMedigun    model none worldmodel none merged True attachment  8
///   ENTITY 1109 CWeaponMedigun    model none worldmodel none merged True attachment 21
///   ENTITY 1192 CTFMinigun        model none worldmodel none merged True attachment 23
/// </code>
///
/// **So it is not medigun-specific** — a minigun does it too. `DemoTimeline.ModelFor` reads
/// `WorldModelIndex() ?? ModelIndex()`, gets nothing, and the track is born with an empty model
/// path; an empty path routes it to `playerTracks` rather than `props`, and nothing draws it.
///
/// **The engine does not read that field either.** `DemoTimeline.cs:1600` already records why, for
/// the viewmodel: *"`pItem-&gt;GetPlayerDisplayModel( iClass, team )` (`econ_entity.cpp:1167`), which
/// is `model_player` from `items_game.txt`. Taking the weapon entity's own `m_nModelIndex` was tried
/// on 2026-08-28 and drew no weapon at all: `m_hWeapon` says WHICH weapon and the schema says what
/// it looks like."* `WeaponModels.For` implements that and is wired to the viewmodel and to the
/// followed player — and not to the weapon entities other players carry. Half a mechanism again.
///
/// Reports numbers, asserts only that the walk ran (D38).
/// </remarks>
[Explicit("Diagnostic: reports medigun placement against a weapon that draws.")]
public sealed class MedigunPlacementProbe
{
    /// <summary>The recording the owner was watching.</summary>
    private const string Recording = "tf2-2026-pub-pov-clean";

    /// <summary>Ticks to sample across the demo.</summary>
    private const int Samples = 300;

    /// <summary>The weapon that fails and the one that works, as model-path fragments.</summary>
    private static readonly string[] Watched = ["medigun", "flamethrower"];

    [Test]
    public void Decode_TheMedigun_ReportsHowItReachesTheScene()
    {
        DemoTimeline timeline = TimelineCache.For(Corpus.Demo(Recording));

        int first = timeline.FirstTick;
        int last = timeline.LastTick;
        int step = Math.Max(1, (last - first) / Samples);

        // **Every track, not only what PropsAt yields.** A weapon that never reaches the scene is
        // invisible to a walk of the scene, so the track list is what separates "never decoded"
        // from "decoded and then dropped".
        // **By ITEM as well as by model path**, because a weapon whose model the wire never carried
        // reaches Core with an empty path — the item is the only thing naming it until the Scene
        // layer resolves `model_player`. Filtering on the path alone showed the ten dropped
        // weapons and hid the three held ones, which is the whole question.
        foreach (ScenePropTrack track in timeline.Props
            .Where(track => track.ItemDefinitionIndex is not null
                || Watched.Any(name =>
                    track.ModelPath.Contains(name, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(track => track.ModelPath, StringComparer.Ordinal)
            .ThenBy(track => track.EntityIndex))
        {
            TestContext.Out.WriteLine(
                $"TRACK {track.EntityIndex} serial {track.SerialNumber} "
                + $"parent {track.AttachedTo?.ToString(CultureInfo.InvariantCulture) ?? "none"} "
                + $"merged {track.BoneMerged} "
                + $"keyframes {track.Keyframes.Count} "
                + $"item {track.ItemDefinitionIndex?.ToString(CultureInfo.InvariantCulture) ?? "none"} "
                + $"state {track.WeaponState?.ToString(CultureInfo.InvariantCulture) ?? "none"} "
                + $"owner {track.OwnedBy?.ToString(CultureInfo.InvariantCulture) ?? "none"} "
                + $"{track.ClassName} '{track.ModelPath}'");
        }

        // **How many tracks the routing change added to Props**, which is the FPS question. A
        // track with an item and no model used to produce nothing at all; now it is a prop, and
        // every per-frame walk over Props pays for it.
        int unresolved = timeline.Props.Count(
            track => track.ModelPath.Length == 0 && track.ItemDefinitionIndex is not null);

        TestContext.Out.WriteLine(
            $"CENSUS {timeline.Props.Count} prop tracks, {unresolved} of them awaiting an item "
            + $"lookup, {timeline.PlayerTracks.Count} player tracks");

        // **What sequence the spawn cabinets are actually told to play, and when.** The owner
        // reports they now stay OPEN, where before they looped for ever. `ClampCycle` holds a
        // finished one-shot on its last frame, which is right — `open` ends open. So the question is
        // whether the demo ever says `close`, and if it does, whether the track follows.
        foreach (ScenePropTrack cabinet in timeline.Props
            .Where(track =>
                track.ModelPath.Contains("resupply_locker", StringComparison.OrdinalIgnoreCase))
            .OrderBy(track => track.EntityIndex)
            .Take(4))
        {
            List<string> sequences =
            [
                .. cabinet.Keyframes
                    .Select(frame => $"t{frame.Tick.ToString(CultureInfo.InvariantCulture)}"
                        + $":seq{frame.Pose.Sequence.ToString(CultureInfo.InvariantCulture)}"
                        + $"@{frame.Pose.Cycle:0.00}")
                    .Take(10),
            ];

            TestContext.Out.WriteLine(
                $"CABINET {cabinet.EntityIndex} {cabinet.Keyframes.Count} keyframes, "
                + $"distinct sequences "
                + $"[{string.Join(",", cabinet.Keyframes.Select(f => f.Pose.Sequence).Distinct().Order())}]"
                + $" first: {string.Join(" ", sequences)}");
        }

        // What actually reaches the scene, and where.
        Dictionary<string, HashSet<string>> placed = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, int> sightings = new(StringComparer.OrdinalIgnoreCase);

        List<SceneProp> props = [];

        for (int tick = first; tick <= last; tick += step)
        {
            props.Clear();
            timeline.PropsAt(tick, props);

            foreach (SceneProp prop in props)
            {
                if (!Watched.Any(name =>
                    prop.ModelPath.Contains(name, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                string key = $"{prop.EntityIndex} {prop.ModelPath} merged {prop.BoneMerged} "
                    + $"parent {prop.AttachedTo?.ToString(CultureInfo.InvariantCulture) ?? "none"}";

                sightings[key] = sightings.GetValueOrDefault(key) + 1;

                if (!placed.TryGetValue(key, out HashSet<string>? where))
                {
                    where = new HashSet<string>(StringComparer.Ordinal);
                    placed[key] = where;
                }

                where.Add($"({prop.Pose.X:0} {prop.Pose.Y:0} {prop.Pose.Z:0})");
            }
        }

        foreach ((string key, int seen) in sightings.OrderBy(
            entry => entry.Key, StringComparer.Ordinal))
        {
            TestContext.Out.WriteLine(
                $"SCENE {key}: {seen} sightings at {string.Join(" | ", placed[key].Take(3))}");
        }

        // **What the class-derived rule actually says, which is the thing under suspicion.** Every
        // medigun above reports `merged False`, and a class answer cannot vary between two
        // instances — so if `BoneMergesItself` were true for `CWeaponMedigun`, none of them could
        // read false. One flamethrower reads true while three read false, which says the same thing
        // from the other side: that one sent `EF_BONEMERGE` on the wire and the class rule is
        // supplying nothing.
        DemoSchema schema = SchemaOf(Corpus.Demo(Recording));

        foreach (ServerClass serverClass in schema.ServerClasses
            .Where(serverClass =>
                serverClass.ClassName.Contains("Weapon", StringComparison.Ordinal)
                || serverClass.ClassName.Contains("Wearable", StringComparison.Ordinal)
                || serverClass.ClassName.Contains("Medigun", StringComparison.Ordinal)
                || serverClass.ClassName.Contains("FlameThrower", StringComparison.Ordinal))
            .OrderBy(serverClass => serverClass.ClassName, StringComparer.Ordinal))
        {
            TestContext.Out.WriteLine(
                $"RULE {serverClass.ClassName} table {serverClass.TableName}: "
                + $"mergesItself {SchemaClasses.BoneMergesItself(schema, serverClass.TableName)}, "
                + $"inheritsWearable "
                + $"{SchemaClasses.Inherits(schema, serverClass.TableName, SchemaClasses.WearableTable)}, "
                + $"inheritsCombatWeapon "
                + $"{SchemaClasses.Inherits(schema, serverClass.TableName, SchemaClasses.CombatWeaponTable)}");
        }

        // **What `CDynamicProp` can send about its animation at all.** `animtime ABSENT` has two
        // readings — the property is not in the schema under the name we ask for, or it is and the
        // server never sends it — and only the flattened list separates them.
        // See docs/memory/a-property-name-needs-its-declaring-table.md: a wrong owner table is not
        // a near miss, it is silently no match.
        foreach (ServerClass prop in schema.ServerClasses
            .Where(candidate => candidate.ClassName == "CDynamicProp"))
        {
            EntityDecoder reader = new(
                schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));

            foreach (FlatProperty flat in reader.FlattenedFor(prop.Id)
                .Where(flat =>
                    flat.Property.Name.Contains("Anim", StringComparison.OrdinalIgnoreCase)
                    || flat.Property.Name.Contains("Cycle", StringComparison.OrdinalIgnoreCase)
                    || flat.Property.Name.Contains("Playback", StringComparison.OrdinalIgnoreCase)
                    || flat.Property.Name.Contains("Sequence", StringComparison.OrdinalIgnoreCase)
                    || flat.Property.Name.Contains("Simulation", StringComparison.OrdinalIgnoreCase)))
            {
                TestContext.Out.WriteLine(
                    $"SCHEMA {flat.OwnerTable}.{flat.Property.Name} "
                    + $"type {flat.Property.Type}");
            }
        }

        // **The chain itself, because "the walk returned false" has two readings**: the ancestor is
        // genuinely absent, or the walk cannot see it. Only the tables the schema actually links
        // separate those.
        foreach (string table in new[] { "DT_TFWeaponMedigun", "DT_WeaponMedigun", "DT_TFWeaponBase" })
        {
            SendTable? found = schema.FindTable(table);

            TestContext.Out.WriteLine(
                found is null
                    ? $"CHAIN {table}: NOT IN SCHEMA"
                    : $"CHAIN {table}: links "
                        + string.Join(", ", found.Properties
                            .Where(property => property.Type == SendPropType.DataTable)
                            .Select(property => property.ReferencedTable)
                            .Where(name => name.Length > 0)));
        }

        // **Which CLASS each medigun-shaped entity actually is, which the two reports above put in
        // direct contradiction.** `BoneMergesItself` is true for `CWeaponMedigun`, and a class
        // answer cannot vary between instances — yet every track carrying `c_medigun.mdl` reports
        // `merged False`. Both cannot describe the same entity, so the tracks are something else.
        //
        // `CTFDroppedWeapon` is the obvious candidate and is `mergesItself False` above, correctly:
        // a weapon lying on the floor is not merged to anybody. If that is what these are, then the
        // real question is not why they are unmerged but where the HELD medigun went.
        EntityStateTable entities = Accumulate(Corpus.Demo(Recording));

        Dictionary<string, int> byClass = new(StringComparer.Ordinal);

        foreach (EntityState entity in entities.All)
        {
            string name = entity.ClassName ?? "?";

            if (!name.Contains("Weapon", StringComparison.Ordinal) &&
                !name.Contains("Medigun", StringComparison.Ordinal) &&
                !name.Contains("Launcher", StringComparison.Ordinal) &&
                !name.Contains("Thrower", StringComparison.Ordinal) &&
                !name.Contains("Minigun", StringComparison.Ordinal))
            {
                continue;
            }

            byClass[name] = byClass.GetValueOrDefault(name) + 1;

            // **The weapons that WORK are reported beside the one that does not**, because "the
            // medigun has no model index" is only a finding if a rocket launcher has one. Without
            // the comparison it is a fact about weapons in general and the contrast the owner
            // reported would be unexplained.
            if (!name.Contains("Medigun", StringComparison.Ordinal) &&
                !name.Contains("RocketLauncher", StringComparison.Ordinal) &&
                !name.Contains("FlameThrower", StringComparison.Ordinal) &&
                !name.Contains("Minigun", StringComparison.Ordinal))
            {
                continue;
            }

            TestContext.Out.WriteLine(
                $"ENTITY {entity.EntityIndex} {name} "
                + $"model {entity.ModelIndex()?.ToString(CultureInfo.InvariantCulture) ?? "none"} "
                + $"worldmodel "
                + $"{entity.WorldModelIndex()?.ToString(CultureInfo.InvariantCulture) ?? "none"} "
                + $"item "
                + $"{entity.ItemDefinitionIndex()?.ToString(CultureInfo.InvariantCulture) ?? "none"} "
                + $"merged {entity.IsBoneMerged} "
                + $"attachment "
                + $"{entity.Attachment()?.ToString(CultureInfo.InvariantCulture) ?? "none"} "
                + $"origin {(entity.Origin() is { } at ? $"({at.X:0} {at.Y:0} {at.Z:0})" : "none")}");
        }

        // **Does the demo carry `m_flAnimTime`?** `C_BaseAnimating::FrameAdvance` measures its
        // interval as `curtime - m_flAnimTime` and re-stamps it every advance
        // (`c_baseanimating.cpp:5480`), and the field is NETWORKED —
        // `RecvPropInt( RECVINFO(m_flAnimTime), 0, RecvProxy_AnimTime )`, `c_baseentity.cpp:424`.
        // This project cites it in seven comments and decodes it nowhere, substituting demo time
        // zero, which is why a one-shot prop animation is finished before the first frame is drawn.
        foreach (EntityState cabinet in entities.All
            .Where(entity => (entity.ClassName ?? string.Empty)
                .Contains("DynamicProp", StringComparison.Ordinal))
            .OrderBy(entity => entity.EntityIndex)
            .Take(6))
        {
            TestContext.Out.WriteLine(
                $"ANIMTIME {cabinet.EntityIndex} {cabinet.ClassName} "
                + $"sequence "
                + $"{cabinet.Integer("DT_BaseAnimating.m_nSequence")
                    ?.ToString(CultureInfo.InvariantCulture) ?? "none"} "
                + $"animtime "
                + $"{cabinet.Integer("DT_BaseEntity.m_flAnimTime")
                    ?.ToString(CultureInfo.InvariantCulture) ?? "ABSENT"} "
                + $"simtime "
                + $"{cabinet.Integer("DT_BaseEntity.m_flSimulationTime")
                    ?.ToString(CultureInfo.InvariantCulture) ?? "ABSENT"} "
                + $"rate "
                + $"{cabinet.Integer("DT_BaseAnimating.m_flPlaybackRate")
                    ?.ToString(CultureInfo.InvariantCulture) ?? "ABSENT"}");
        }

        foreach ((string name, int count) in byClass.OrderBy(
            entry => entry.Key, StringComparer.Ordinal))
        {
            TestContext.Out.WriteLine($"CLASS {name}: {count} live entities");
        }

        timeline.Props.Count.ShouldBeGreaterThan(0, "the demo produced no prop tracks at all");
    }

    /// <summary>Every entity the demo's snapshots produce, baselines included.</summary>
    /// <remarks>Baselines applied deliberately: without them an entity's state is a fact about an
    /// empty decoder rather than about the demo, which is a mistake `LockerParentProbe` records.
    /// </remarks>
    private static EntityStateTable Accumulate(string path)
    {
        byte[] file = System.IO.File.ReadAllBytes(path);
        DemoHeader header = DemoHeader.Parse(file);
        NetDecodeState state = new() { NetworkProtocol = (ushort)header.NetworkProtocol };

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
                        }

                        continue;

                    default:
                        continue;
                }
            }
        }

        return table;
    }

    /// <summary>The demo's send tables.</summary>
    private static DemoSchema SchemaOf(string path)
    {
        byte[] file = System.IO.File.ReadAllBytes(path);
        DemoHeader header = DemoHeader.Parse(file);

        DemoCommand tables = DemoCommandReader.Read(file.AsMemory(DemoHeader.SizeBytes))
            .First(command => command.Type == DemoCommandType.DataTables);

        return SendTableParser.Parse(tables.Payload.Span, (ushort)header.NetworkProtocol);
    }
}
