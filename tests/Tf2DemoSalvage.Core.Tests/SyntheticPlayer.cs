using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Tests;

/// <summary>
/// A demo carrying a schema and one player entity, without needing a recording.
/// </summary>
/// <remarks>
/// **The whole entity path, assembled from pieces that already existed.** The schema comes from
/// <see cref="SyntheticSchema"/>, the entity body from <c>EntityDecoder.EncodeEntities</c>, and
/// the container from <see cref="SyntheticDemo"/>. Nothing here is new decoding logic; what was
/// missing was only the ability to WRITE a schema, which is why every entity test needed a real
/// file.
///
/// **The tables are the ones EntityState actually looks in, and that is the fragile part.** A
/// property is found by its declaring table as well as its name, so <c>m_fFlags</c> in
/// <c>DT_TFPlayer</c> rather than <c>DT_BasePlayer</c> is silently not found — a fixture that gets
/// this wrong produces a player with no position and a test that fails for a reason unrelated to
/// what it measures. See <c>docs/memory/a-property-name-needs-its-declaring-table.md</c>.
///
/// This is a MINIMAL player, not a faithful one. It carries what the timeline reads and nothing
/// else, which is the right trade for a fixture: a test that fails should point at the property it
/// names rather than at three hundred it does not.
/// </remarks>
internal static class SyntheticPlayer
{
    /// <summary>Class id the player entity is created with.</summary>
    public const int PlayerClassId = 0;

    /// <summary>Which exclusive table carries a player's position.</summary>
    /// <remarks>
    /// **Not a property of the recording mode, and the corpus settled that.** The obvious rule —
    /// a point-of-view demo resolves through the local table and a SourceTV recording through the
    /// non-local one — is FALSE: the 2013 SourceTV demo is 21 non-local against 2 local, and a
    /// modern demos.tf SourceTV recording came back 12 local and 0 non-local. Any reader branching
    /// on POV-versus-SourceTV is wrong on some era.
    ///
    /// What matters is that the resolver reaches both, which is why this is a fixture axis: a
    /// synthetic demo can be written with either table rather than hoping the corpus contains one
    /// of each.
    /// </remarks>
    public enum OriginTable
    {
        /// <summary><c>DT_TFNonLocalPlayerExclusive</c>.</summary>
        NonLocal,

        /// <summary><c>DT_TFLocalPlayerExclusive</c>.</summary>
        Local,
    }

    /// <summary>A schema with the tables a player's position and pose are read from.</summary>
    /// <remarks>
    /// Nested through <c>DT_TFPlayer</c> by DataTable properties, because that is how inheritance
    /// is expressed on the wire and how <c>SchemaFlattener</c> reaches the parent tables. Listing
    /// them side by side without the nesting produces a flattened list with none of them in it.
    /// </remarks>
    public static DemoSchema Schema() => Schema(OriginTable.NonLocal);

    /// <summary>A schema carrying the player's position in the chosen exclusive table.</summary>
    /// <param name="origin">Which of the two mutually exclusive tables to declare.</param>
    /// <returns>The schema.</returns>
    /// <remarks>
    /// One or the other, never both — a demo carries whichever the server sent for that player, and
    /// a fixture declaring both would describe a combination no recording contains.
    /// </remarks>
    public static DemoSchema Schema(OriginTable origin) => new(
        [
            new SendTable("DT_BaseEntity", NeedsDecoder: true,
            [
                Int("m_nModelIndex", bits: 13),
                Int("m_fEffects", bits: 11),

                // **Team lives here and not on DT_TFPlayer**, which is where the first draft of
                // this fixture put it. The timeline looks for "DT_BaseEntity.m_iTeamNum" by its
                // fully qualified name, so the wrong table is not a near miss — it is silently no
                // match, and the player comes back with a null team.
                // See docs/memory/a-property-name-needs-its-declaring-table.md.
                Int("m_iTeamNum", bits: 3),
            ]),
            new SendTable("DT_BaseAnimating", NeedsDecoder: true,
            [
                Int("m_nSequence", bits: 12),
                Float("m_flCycle", low: 0f, high: 1f, bits: 10),
                Int("m_nSkin", bits: 10),
                Table("baseentity", "DT_BaseEntity"),
            ]),
            new SendTable("DT_BasePlayer", NeedsDecoder: true,
            [
                Int("m_fFlags", bits: 11),
                Int("m_lifeState", bits: 3),
                Table("baseanimating", "DT_BaseAnimating"),
            ]),

            // One exclusive table, named by the caller. They are complements — a player's position
            // arrives in one or the other, never both — so declaring both would describe a
            // combination no recording contains.
            new SendTable(
                origin == OriginTable.Local
                    ? "DT_TFLocalPlayerExclusive"
                    : "DT_TFNonLocalPlayerExclusive",
                NeedsDecoder: true,
            [
                // **VectorXY, not Vector, and the two are different ERAS rather than a style
                // choice.** A three-component vector is the launch shape and carries height
                // inside itself; the modern shape sends the horizontal pair here and height in
                // m_vecOrigin[2]. EntityState branches on which arrived, so a fixture declaring a
                // full Vector *and* a separate Z describes a demo that has never existed — the
                // vector wins, the separate height is never read, and the test fails on a
                // coordinate the fixture never sent.
                VectorXy("m_vecOrigin", bits: 32),
                Float("m_vecOrigin[2]", low: -16384f, high: 16384f, bits: 32),
                Float("m_angEyeAngles[0]", low: -90f, high: 90f, bits: 12),
                Float("m_angEyeAngles[1]", low: -180f, high: 180f, bits: 12),
            ]),
            new SendTable("DT_TFPlayer", NeedsDecoder: true,
            [
                Int("m_nWaterLevel", bits: 2),
                Int("m_iTeamNum", bits: 3),
                Table("baseplayer", "DT_BasePlayer"),
                Table(
                    "exclusivedata",
                    origin == OriginTable.Local
                        ? "DT_TFLocalPlayerExclusive"
                        : "DT_TFNonLocalPlayerExclusive"),
            ]),
        ],
        [new ServerClass(PlayerClassId, "CTFPlayer", "DT_TFPlayer")]);

    /// <summary>Class id of the <c>CTFPlayerResource</c> entity, when the schema declares one.</summary>
    public const int ResourceClassId = 1;

    /// <summary>Entity slot the resource occupies. Real demos put it low and it never moves.</summary>
    public const int ResourceEntityIndex = 30;

    /// <summary>How many player slots the resource's arrays cover.</summary>
    private const int ResourceSlots = 34;

    /// <summary>
    /// A schema that also declares <c>CTFPlayerResource</c>, where team and class really live.
    /// </summary>
    /// <param name="origin">Which exclusive table carries player positions.</param>
    /// <returns>The schema.</returns>
    /// <remarks>
    /// **The array naming here is the whole point of the fixture, and it is not obvious.** Source's
    /// <c>SendPropArray</c> does not emit one property with an element count — it generates a
    /// **sub-table named after the array**, whose properties are named <c>000</c>, <c>001</c> and
    /// so on. Flattened, that makes the owner table <c>m_iTeam</c> and the property <c>001</c>, so
    /// the key a reader looks up is <c>m_iTeam.001</c> with no <c>DT_</c> prefix anywhere in it.
    ///
    /// Reading the code without knowing that leads straight to the wrong conclusion: the flattener
    /// emits one <c>FlatProperty</c> per array, <c>EntityStateTable</c> keys everything as
    /// <c>OwnerTable.Name</c>, and nothing in the repository expands an array into indexed keys —
    /// from which it follows that <c>m_iTeam.001</c> can never match and the resource path is dead.
    /// It is not dead; every corpus demo reports 100% of sightings with a class through it. The
    /// missing piece is that the sub-table supplies the prefix.
    ///
    /// **Team and health have a fallback to the player entity and class does not**, which is why
    /// class is the one worth asserting: if this lookup broke, team would quietly keep working and
    /// only the class would go null.
    /// </remarks>
    public static DemoSchema SchemaWithResource(OriginTable origin = OriginTable.NonLocal)
    {
        DemoSchema baseline = Schema(origin);

        return new DemoSchema(
            [
                .. baseline.Tables,
                ArrayTable("m_iTeam"),
                ArrayTable("m_iPlayerClass"),
                new SendTable("DT_TFPlayerResource", NeedsDecoder: true,
                [
                    Table("m_iTeam", "m_iTeam"),
                    Table("m_iPlayerClass", "m_iPlayerClass"),
                ]),
            ],
            [
                .. baseline.ServerClasses,
                new ServerClass(ResourceClassId, "CTFPlayerResource", "DT_TFPlayerResource"),
            ]);
    }

    /// <summary>One of Valve's generated array sub-tables: properties named 000, 001, …</summary>
    private static SendTable ArrayTable(string name) => new(
        name,
        NeedsDecoder: true,
        [
            .. Enumerable.Range(0, ResourceSlots).Select(
                slot => Int(slot.ToString("D3", CultureInfo.InvariantCulture), bits: 5)),
        ]);

    /// <summary>A decoder over the default schema, which the encoder also needs.</summary>
    public static EntityDecoder Decoder()
    {
        DemoSchema schema = Schema();
        return new EntityDecoder(schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));
    }

    /// <summary>
    /// A demo whose single snapshot creates one player at a position, with the given properties.
    /// </summary>
    /// <param name="values">Property names and values, e.g. <c>["m_iHealth"] = 125</c>.</param>
    /// <param name="entityIndex">Which entity slot the player occupies.</param>
    /// <param name="tick">The tick the snapshot is stamped with.</param>
    /// <returns>A demo's bytes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="values"/> is null.</exception>
    /// <exception cref="ArgumentException">A named property is not in the flattened list.</exception>
    public static byte[] Demo(
        IReadOnlyDictionary<string, PropertyValue> values, int entityIndex = 1, int tick = 66) =>
        Demo(OriginTable.NonLocal, tick, (entityIndex, values));

    /// <summary>A demo whose single snapshot creates several players at once.</summary>
    /// <param name="origin">Which exclusive table carries their positions.</param>
    /// <param name="tick">The tick the snapshot is stamped with.</param>
    /// <param name="players">One entry per player: its slot and its properties.</param>
    /// <returns>A demo's bytes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="players"/> is null.</exception>
    /// <remarks>
    /// **Several entities in one snapshot is a different code path from one repeated.** Entity
    /// indices are delta-coded, so the encoder writes the GAP to the next slot rather than the slot
    /// itself — a demo with players in slots 1, 2 and 5 exercises that, and three separate
    /// single-player demos do not.
    /// </remarks>
    public static byte[] Demo(
        OriginTable origin,
        int tick,
        params (int EntityIndex, IReadOnlyDictionary<string, PropertyValue> Values)[] players)
    {
        ArgumentNullException.ThrowIfNull(players);

        DemoSchema schema = Schema(origin);
        EntityDecoder decoder = new(
            schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));

        IReadOnlyList<FlatProperty> flat = decoder.FlattenedFor(PlayerClassId);
        List<DecodedEntity> entities = [];

        // Ascending, because a snapshot's entity indices are delta-coded and the encoder writes the
        // gap to the next slot. Out of order it encodes negative gaps, which is not a stream any
        // server produces.
        foreach ((int entityIndex, IReadOnlyDictionary<string, PropertyValue> values) in
            players.OrderBy(player => player.EntityIndex))
        {
            ArgumentNullException.ThrowIfNull(values);

            List<DecodedProperty> properties = [];
            foreach ((string name, PropertyValue value) in values)
            {
                int index = IndexOf(flat, name);
                properties.Add(new DecodedProperty(index, flat[index], value));
            }

            // Sorted by property index, for the same reason: properties are delta-coded against
            // the previous index. Out of order they encode to a stream that decodes to different
            // properties entirely.
            properties.Sort((left, right) => left.Index.CompareTo(right.Index));

            entities.Add(new DecodedEntity(
                entityIndex,
                PlayerClassId,

                // A distinct serial per slot, so two players are two tracks rather than one slot
                // being reused. TrackIdentity keys on the pair.
                SerialNumber: entityIndex,
                EntityUpdateType.Enter,
                properties));
        }

        byte[] body = decoder.EncodeEntities(entities, [], isDelta: false, 0, out int bits);

        return SyntheticDemo.From(
            SyntheticDemo.DefaultProtocol,
            SyntheticDemo.Packet(SyntheticDemo.DefaultProtocol, 0, ServerInfo()),
            SyntheticDemo.DataTables(schema),
            SyntheticDemo.Packet(
                SyntheticDemo.DefaultProtocol,
                tick,
                new PacketEntitiesMessage(
                    MaxEntries: 64,
                    IsDelta: false,
                    DeltaFromTick: null,
                    BaselineIndex: false,
                    UpdatedEntries: entities.Count,
                    LengthBits: bits,
                    UpdateBaseline: false,
                    Body: body)));
    }

    /// <summary>
    /// A demo carrying players and a <c>CTFPlayerResource</c> stating each one's team and class.
    /// </summary>
    /// <param name="tick">The tick the snapshot is stamped with.</param>
    /// <param name="players">Player slot, team and class for each.</param>
    /// <returns>A demo's bytes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="players"/> is null.</exception>
    /// <remarks>
    /// The players themselves carry a position and nothing else about their identity, which is the
    /// modern shape: a reader taking team off the player entity gets it from the fallback and a
    /// reader taking class off it gets nothing at all.
    /// </remarks>
    public static byte[] DemoWithResource(
        int tick, params (int EntityIndex, int Team, int PlayerClass)[] players)
    {
        ArgumentNullException.ThrowIfNull(players);

        DemoSchema schema = SchemaWithResource();
        EntityDecoder decoder = new(
            schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));

        List<DecodedEntity> entities = [];

        foreach ((int entityIndex, _, _) in players.OrderBy(player => player.EntityIndex))
        {
            entities.Add(Entity(
                decoder,
                PlayerClassId,
                entityIndex,
                new Dictionary<string, PropertyValue>
                {
                    ["m_vecOrigin"] = PropertyValue.FromVectorXY(entityIndex * 64f, 0f),
                    ["m_vecOrigin[2]"] = PropertyValue.FromFloat(0f),
                    ["m_lifeState"] = PropertyValue.FromInt(0),
                }));
        }

        // The resource sits after the players, because entity indices are delta-coded and the
        // encoder writes ascending gaps.
        Dictionary<string, PropertyValue> arrays = [];
        foreach ((int entityIndex, int team, int playerClass) in players)
        {
            string slot = entityIndex.ToString("D3", CultureInfo.InvariantCulture);
            arrays[$"m_iTeam.{slot}"] = PropertyValue.FromInt(team);
            arrays[$"m_iPlayerClass.{slot}"] = PropertyValue.FromInt(playerClass);
        }

        entities.Add(Entity(decoder, ResourceClassId, ResourceEntityIndex, arrays));

        byte[] body = decoder.EncodeEntities(entities, [], isDelta: false, 0, out int bits);

        return SyntheticDemo.From(
            SyntheticDemo.DefaultProtocol,
            SyntheticDemo.Packet(SyntheticDemo.DefaultProtocol, 0, ServerInfo()),
            SyntheticDemo.DataTables(schema),
            SyntheticDemo.Packet(
                SyntheticDemo.DefaultProtocol,
                tick,
                new PacketEntitiesMessage(
                    MaxEntries: 64,
                    IsDelta: false,
                    DeltaFromTick: null,
                    BaselineIndex: false,
                    UpdatedEntries: entities.Count,
                    LengthBits: bits,
                    UpdateBaseline: false,
                    Body: body)));
    }

    /// <summary>
    /// A demo whose one player is at a different place on each of several ticks.
    /// </summary>
    /// <param name="intervalPerTick">
    /// The server's tick interval, recorded in <c>svc_ServerInfo</c>. Never a constant in real
    /// demos — early servers ran 33 tick — so it is a parameter here rather than a fixture default.
    /// </param>
    /// <param name="positions">One entry per snapshot: the tick, and where the player is on it.</param>
    /// <returns>A demo's bytes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="positions"/> is null.</exception>
    /// <remarks>
    /// **Each snapshot after the first is a DELTA, which is what a real demo sends.** A stream of
    /// full snapshots would decode correctly and exercise none of the delta path — and the delta
    /// path is where an entity keeps the properties an update does not mention.
    /// </remarks>
    public static byte[] DemoOverTicks(
        float intervalPerTick, params (int Tick, float X, float Y)[] positions)
    {
        ArgumentNullException.ThrowIfNull(positions);

        DemoSchema schema = Schema();
        EntityDecoder decoder = new(
            schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));

        List<DemoCommand> commands =
        [
            SyntheticDemo.Packet(
                SyntheticDemo.DefaultProtocol, 0, ServerInfo(intervalPerTick)),
            SyntheticDemo.DataTables(schema),
        ];

        for (int index = 0; index < positions.Length; index++)
        {
            (int tick, float x, float y) = positions[index];

            Dictionary<string, PropertyValue> values = new()
            {
                ["m_vecOrigin"] = PropertyValue.FromVectorXY(x, y),
                ["m_vecOrigin[2]"] = PropertyValue.FromFloat(0f),
            };

            // Only the first snapshot introduces the entity; the rest move it. Team and life state
            // ride on the entering update and are retained, which is the delta behaviour a viewer
            // depends on.
            if (index == 0)
            {
                values["m_iTeamNum"] = PropertyValue.FromInt(SceneTeams.Red);
                values["m_lifeState"] = PropertyValue.FromInt(0);
            }

            DecodedEntity player = Entity(decoder, PlayerClassId, 1, values) with
            {
                UpdateType = index == 0 ? EntityUpdateType.Enter : EntityUpdateType.Delta,
            };

            byte[] body = decoder.EncodeEntities(
                [player], [], isDelta: index > 0, 0, out int bits);

            commands.Add(SyntheticDemo.Packet(
                SyntheticDemo.DefaultProtocol,
                tick,
                new PacketEntitiesMessage(
                    MaxEntries: 64,
                    IsDelta: index > 0,
                    DeltaFromTick: index > 0 ? positions[index - 1].Tick : null,
                    BaselineIndex: false,
                    UpdatedEntries: 1,
                    LengthBits: bits,
                    UpdateBaseline: false,
                    Body: body)));
        }

        return SyntheticDemo.From(SyntheticDemo.DefaultProtocol, [.. commands]);
    }

    /// <summary>Several players moving across several snapshots, each with a life state.</summary>
    /// <param name="intervalPerTick">Seconds per tick, as <c>svc_ServerInfo</c> declares it.</param>
    /// <param name="states">
    /// One entry per snapshot: the tick, and each player's slot, X position and <c>m_lifeState</c>.
    /// </param>
    /// <returns>A demo's bytes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="states"/> is null.</exception>
    /// <remarks>
    /// **<see cref="DemoOverTicks"/> moves one player and fixes their life state at alive**, which
    /// is the right shape for testing movement and the wrong one for testing anything that
    /// branches on being dead: with a single subject, "held the position" and "interpolated
    /// nothing at all" are the same observation.
    ///
    /// This carries a bystander, so a behaviour that applies to one player can be distinguished
    /// from one that applies to the frame.
    /// </remarks>
    public static byte[] DemoOfPlayersOverTicks(
        float intervalPerTick,
        params (int Tick, (int EntityIndex, float X, int LifeState)[] Players)[] states)
    {
        ArgumentNullException.ThrowIfNull(states);

        DemoSchema schema = Schema();
        EntityDecoder decoder = new(
            schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));

        List<DemoCommand> commands =
        [
            SyntheticDemo.Packet(
                SyntheticDemo.DefaultProtocol, 0, ServerInfo(intervalPerTick)),
            SyntheticDemo.DataTables(schema),
        ];

        for (int index = 0; index < states.Length; index++)
        {
            (int tick, (int EntityIndex, float X, int LifeState)[] players) = states[index];

            List<DecodedEntity> entities = [];

            // Ascending, because entity indices are delta-coded and the encoder writes the gap to
            // the next slot rather than the slot itself.
            foreach ((int entityIndex, float x, int lifeState) in
                players.OrderBy(player => player.EntityIndex))
            {
                Dictionary<string, PropertyValue> values = new()
                {
                    ["m_vecOrigin"] = PropertyValue.FromVectorXY(x, 0f),
                    ["m_vecOrigin[2]"] = PropertyValue.FromFloat(0f),

                    // Sent on every snapshot rather than only the first, because a life state that
                    // changes mid-demo is the case this fixture exists for and retaining an
                    // entering value would make that unexpressible.
                    ["m_lifeState"] = PropertyValue.FromInt(lifeState),
                };

                if (index == 0)
                {
                    values["m_iTeamNum"] = PropertyValue.FromInt(SceneTeams.Red);
                }

                entities.Add(Entity(decoder, PlayerClassId, entityIndex, values) with
                {
                    SerialNumber = entityIndex,
                    UpdateType = index == 0 ? EntityUpdateType.Enter : EntityUpdateType.Delta,
                });
            }

            byte[] body = decoder.EncodeEntities(
                entities, [], isDelta: index > 0, 0, out int bits);

            commands.Add(SyntheticDemo.Packet(
                SyntheticDemo.DefaultProtocol,
                tick,
                new PacketEntitiesMessage(
                    MaxEntries: 64,
                    IsDelta: index > 0,
                    DeltaFromTick: index > 0 ? states[index - 1].Tick : null,
                    BaselineIndex: false,
                    UpdatedEntries: entities.Count,
                    LengthBits: bits,
                    UpdateBaseline: false,
                    Body: body)));
        }

        return SyntheticDemo.From(SyntheticDemo.DefaultProtocol, [.. commands]);
    }

    /// <summary>Class id of the ordinary prop entity, when the schema declares one.</summary>
    public const int PropClassId = 2;

    /// <summary>
    /// A demo whose one non-player entity changes its effects flags from tick to tick.
    /// </summary>
    /// <param name="intervalPerTick">The server's tick interval.</param>
    /// <param name="states">One entry per snapshot: the tick, and the effects value on it.</param>
    /// <returns>A demo's bytes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="states"/> is null.</exception>
    /// <remarks>
    /// **A prop rather than a player, because the two land in different lists.** A player goes to
    /// <c>PlayerTracks</c> and everything else to <c>Props</c>, so a fixture that used a
    /// <c>CTFPlayer</c> here would leave <c>PropsAt</c> empty and the test would assert nothing.
    ///
    /// The entity carries a model index, which is what earns it a track: a prop with no model is
    /// nothing a viewer can draw.
    /// </remarks>
    public static byte[] DemoOfEffects(
        float intervalPerTick, params (int Tick, int Effects)[] states)
    {
        ArgumentNullException.ThrowIfNull(states);

        DemoSchema schema = SchemaWithProp();
        EntityDecoder decoder = new(
            schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));

        // **A prop earns a track by resolving its model index through the precache**, so without
        // this table the entity decodes correctly, gets no model, and never becomes a prop the
        // timeline can report. Index 7 is what the entity below names.
        List<string> models = [.. Enumerable.Repeat(string.Empty, 7), "models/props_gameplay/resupply_locker.mdl"];

        List<DemoCommand> commands =
        [
            SyntheticDemo.Packet(
                SyntheticDemo.DefaultProtocol,
                0,
                ServerInfo(intervalPerTick),
                SyntheticDemo.StringTable("modelprecache", models, maxEntries: 1024)),
            SyntheticDemo.DataTables(schema),
        ];

        for (int index = 0; index < states.Length; index++)
        {
            (int tick, int effects) = states[index];

            Dictionary<string, PropertyValue> values = new()
            {
                ["m_fEffects"] = PropertyValue.FromInt(effects),
                ["m_vecOrigin"] = PropertyValue.FromVectorXY(64f, 64f),
                ["m_vecOrigin[2]"] = PropertyValue.FromFloat(0f),
            };

            if (index == 0)
            {
                values["m_nModelIndex"] = PropertyValue.FromInt(7);
            }

            DecodedEntity prop = Entity(decoder, PropClassId, 3, values) with
            {
                UpdateType = index == 0 ? EntityUpdateType.Enter : EntityUpdateType.Delta,
            };

            byte[] body = decoder.EncodeEntities(
                [prop], [], isDelta: index > 0, 0, out int bits);

            commands.Add(SyntheticDemo.Packet(
                SyntheticDemo.DefaultProtocol,
                tick,
                new PacketEntitiesMessage(
                    MaxEntries: 64,
                    IsDelta: index > 0,
                    DeltaFromTick: index > 0 ? states[index - 1].Tick : null,
                    BaselineIndex: false,
                    UpdatedEntries: 1,
                    LengthBits: bits,
                    UpdateBaseline: false,
                    Body: body)));
        }

        return SyntheticDemo.From(SyntheticDemo.DefaultProtocol, [.. commands]);
    }

    /// <summary>A demo carrying a schema and <c>svc_TempEntities</c> effects that share a class.</summary>
    /// <param name="count">How many effects the message carries.</param>
    /// <returns>A demo's bytes.</returns>
    /// <remarks>
    /// **A temp entity is a one-shot effect and never enters the entity table**, so it exercises a
    /// decode path a snapshot does not reach: no entity index, no serial number, and a class id
    /// that an effect may omit to repeat the previous one. Two effects rather than one for exactly
    /// that reason — the repeat is only expressible from the second onwards, and a decoder that
    /// treats each effect independently desynchronises there rather than at the first.
    ///
    /// The effects carry one property each, because "an effect with fields" and "an effect with
    /// none" render differently and both are worth having available.
    /// </remarks>
    public static byte[] DemoWithTempEntities(int count = 2)
    {
        DemoSchema schema = SchemaWithProp();
        EntityDecoder decoder = new(
            schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));

        IReadOnlyList<FlatProperty> flat = decoder.FlattenedFor(PropClassId);
        int effects = IndexOf(flat, "m_fEffects");

        DecodedTempEntity effect = new(
            ClassId: PropClassId,
            DelaySeconds: 0f,
            Properties: [new DecodedProperty(effects, flat[effects], PropertyValue.FromInt(3))]);

        byte[] body = decoder.EncodeTempEntities(
            [.. Enumerable.Repeat(effect, count)], reliable: false, lengthBits: 0);

        return SyntheticDemo.From(
            SyntheticDemo.DefaultProtocol,
            SyntheticDemo.DataTables(schema),
            SyntheticDemo.Packet(
                SyntheticDemo.DefaultProtocol,
                66,
                new TempEntitiesMessage(
                    Count: count, BodyBits: body.Length * 8, Body: body)));
    }

    /// <summary>A demo whose packets carry chosen recorded views in their prologues.</summary>
    /// <param name="views">One entry per packet: the tick, and the view origin to record.</param>
    /// <returns>A demo's bytes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="views"/> is null.</exception>
    /// <remarks>
    /// **The prologue is normally zeroed by <see cref="SyntheticDemo.Packet"/>**, which is right
    /// for every other fixture — the bytes are opaque to this project and writing them back as
    /// read is what makes a demo reproduce. Here they are the subject, so they are filled in.
    ///
    /// Angles are derived from the origin rather than passed separately, because no test so far
    /// needs to choose them independently and a parameter nothing varies is a parameter that gets
    /// passed wrongly. <c>democmdinfo_t</c>'s layout is int flags, then viewOrigin, viewAngles and
    /// localViewAngles, then a resampled copy of all three.
    /// </remarks>
    public static byte[] DemoWithRecordedViews(
        params (int Tick, (float X, float Y, float Z) Origin)[] views)
    {
        ArgumentNullException.ThrowIfNull(views);

        DemoSchema schema = Schema();
        List<DemoCommand> commands =
        [
            SyntheticDemo.Packet(SyntheticDemo.DefaultProtocol, 0, ServerInfo()),
            SyntheticDemo.DataTables(schema),
        ];

        foreach ((int tick, (float x, float y, float z)) in views)
        {
            byte[] prologue = new byte[PrologueBytes];

            // Flags stay zero, so the ORIGINAL copy is the live one rather than the resampled.
            BitConverter.GetBytes(x).CopyTo(prologue, 4);
            BitConverter.GetBytes(y).CopyTo(prologue, 8);
            BitConverter.GetBytes(z).CopyTo(prologue, 12);

            // Pitch and yaw scaled off the origin so two packets differ in both, which is what
            // catches a lookup that finds the right tick and reads the wrong field.
            BitConverter.GetBytes(x / 10f).CopyTo(prologue, 16);
            BitConverter.GetBytes(y / 10f).CopyTo(prologue, 20);

            commands.Add(
                SyntheticDemo.Packet(SyntheticDemo.DefaultProtocol, tick) with
                {
                    Prologue = prologue,
                });
        }

        return SyntheticDemo.From(SyntheticDemo.DefaultProtocol, [.. commands]);
    }

    /// <summary>Bytes of <c>democmdinfo_t</c> and the sequence numbers before a packet's body.</summary>
    private const int PrologueBytes = 76 + 8;

    /// <summary>Class id of the viewmodel entity, when the schema declares one.</summary>
    public const int ViewmodelClassId = 3;

    /// <summary>Entity slot the viewmodel occupies.</summary>
    private const int ViewmodelEntityIndex = 8;

    /// <summary>Entity slot the off-hand viewmodel occupies, when the fixture carries one.</summary>
    /// <remarks>
    /// **After the main hand, deliberately.** The defect this fixture exists to catch is a lookup
    /// that keeps whichever viewmodel it saw last, so an off hand recorded FIRST would let the
    /// broken reader answer correctly by accident. See
    /// <c>docs/memory/real-data-hides-bugs-small-inputs-expose.md</c> — the condition has to be one
    /// where correct and broken disagree.
    /// </remarks>
    private const int OffHandEntityIndex = 9;

    /// <summary>Source's <c>SPROP_UNSIGNED</c>.</summary>
    private const int UnsignedFlag = 1 << 0;

    /// <summary>The later tick at which <c>offHandHiddenLater</c> flags the off hand.</summary>
    /// <remarks>
    /// **A second tick is what separates two designs that both pass on one.** Recording a hidden
    /// viewmodel as hidden and skipping it at record time are indistinguishable when the demo has
    /// only ever described it once. They differ the moment a watch is put away: skipping leaves the
    /// last recorded sample saying "visible", and the lookup keeps answering with it for ever.
    /// </remarks>
    internal const int HiddenTick = 132;

    /// <summary>A demo carrying a player and the weapon they see in their own hands.</summary>
    /// <param name="owner">
    /// The entity the viewmodel names as its owner, or <c>null</c> for the point-of-view shape
    /// where the demo names nobody.
    /// </param>
    /// <param name="offHandModelIndex">
    /// A second viewmodel in slot 1, or <c>null</c> for the one-viewmodel shape.
    /// </param>
    /// <param name="offHandOwner">Who owns that second one, defaulting to the first's owner.</param>
    /// <param name="secondUnowned">
    /// Make the second viewmodel a main hand naming NO owner, which is the SourceTV shape that
    /// broke the lookup: a demo carrying owned viewmodels and one whose owner did not decode.
    /// </param>
    /// <returns>A demo's bytes.</returns>
    /// <remarks>
    /// **Both shapes are real and the corpus has both.** A point-of-view recording carries exactly
    /// one viewmodel and never names an owner, because a client only ever receives its own; a
    /// modern SourceTV recording carries one per player and names each. A fixture offering only the
    /// second would let a lookup that requires an owner pass, and that lookup finds nothing on
    /// eight of the nine corpus demos.
    /// </remarks>
    /// <param name="offHandHidden">Flag the off hand <c>EF_NODRAW</c> from the first tick.</param>
    /// <param name="offHandHiddenLater">
    /// Describe the scene a second time at <see cref="HiddenTick"/> with the off hand flagged, which
    /// is a watch being put away.
    /// </param>
    /// <param name="offHandStowedLater">
    /// The same second tick, but with the off hand's MODEL cleared to index 0 rather than flagged.
    /// The other way a viewmodel leaves the screen, and a separate chance to get it wrong.
    /// </param>
    public static byte[] DemoWithViewmodel(
        int? owner,
        int? offHandModelIndex = null,
        int? offHandOwner = null,
        bool secondUnowned = false,
        bool offHandHidden = false,
        bool offHandHiddenLater = false,
        bool offHandStowedLater = false)
    {
        DemoSchema schema = SchemaWithViewmodel();
        EntityDecoder decoder = new(
            schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));

        DecodedEntity player = Entity(
            decoder,
            PlayerClassId,
            1,
            new Dictionary<string, PropertyValue>
            {
                ["m_vecOrigin"] = PropertyValue.FromVectorXY(0f, 0f),
                ["m_vecOrigin[2]"] = PropertyValue.FromFloat(0f),
                ["m_lifeState"] = PropertyValue.FromInt(0),
            });

        List<DecodedEntity> entities =
        [
            player,
            Entity(
                decoder,
                ViewmodelClassId,
                ViewmodelEntityIndex,
                Viewmodel(modelIndex: 4, slot: 0, owner)),
        ];

        // The off hand, when the fixture is the two-viewmodel shape. TF2 gives it to the spy's
        // watch and to grenades — `CTFWeaponInvis::Spawn` calls `SetViewModelIndex( 1 )`.
        if (offHandModelIndex is { } offHand)
        {
            entities.Add(Entity(
                decoder,
                ViewmodelClassId,
                OffHandEntityIndex,
                Viewmodel(
                    offHand,
                    slot: secondUnowned ? 0 : 1,
                    secondUnowned ? null : offHandOwner ?? owner,
                    hidden: offHandHidden)));
        }

        byte[] body = decoder.EncodeEntities(
            [.. entities], [], isDelta: false, 0, out int bits);

        // **The same scene again with the watch put away**, sent whole rather than as a delta so
        // the fixture exercises the timeline rather than the delta decoder.
        List<DemoCommand> later = [];

        if ((offHandHiddenLater || offHandStowedLater) && offHandModelIndex is { } present)
        {
            // Stowing clears the MODEL; hiding sets EF_NODRAW. Two different ways the engine takes
            // a viewmodel off screen, and a reader can get one right and the other wrong.
            int stowed = offHandStowedLater ? 0 : present;

            later.Add(SecondTick(decoder, owner, offHandOwner, stowed, hidden: offHandHiddenLater));
        }

        List<DemoCommand> commands =
        [
            SyntheticDemo.Packet(
                SyntheticDemo.DefaultProtocol,
                0,
                ServerInfo(),

                // The precache is what turns the model index into a path, and without it the
                // viewmodel decodes perfectly and resolves to nothing.
                SyntheticDemo.StringTable(
                    "modelprecache",
                    ["", "a.mdl", "b.mdl", "models/weapons/v_watch.mdl",
                     "models/weapons/v_scattergun.mdl"],
                    maxEntries: 1024)),
            SyntheticDemo.DataTables(schema),
            SyntheticDemo.Packet(
                SyntheticDemo.DefaultProtocol,
                66,
                new PacketEntitiesMessage(
                    MaxEntries: 64,
                    IsDelta: false,
                    DeltaFromTick: null,
                    BaselineIndex: false,
                    UpdatedEntries: entities.Count,
                    LengthBits: bits,
                    UpdateBaseline: false,
                    Body: body)),
            .. later,
        ];

        return SyntheticDemo.From(SyntheticDemo.DefaultProtocol, [.. commands]);
    }

    /// <summary>The same scene at <see cref="HiddenTick"/>, with the off hand flagged EF_NODRAW.</summary>
    /// <remarks>
    /// Whole rather than a delta, because what is under test is what the TIMELINE does with a
    /// viewmodel that stops being drawn — routing it through the delta decoder would put a second
    /// subject in an experiment that already has one.
    /// </remarks>
    private static DemoCommand SecondTick(
        EntityDecoder decoder, int? owner, int? offHandOwner, int offHandModelIndex, bool hidden)
    {
        List<DecodedEntity> entities =
        [
            Entity(
                decoder,
                PlayerClassId,
                1,
                new Dictionary<string, PropertyValue>
                {
                    ["m_vecOrigin"] = PropertyValue.FromVectorXY(0f, 0f),
                    ["m_vecOrigin[2]"] = PropertyValue.FromFloat(0f),
                    ["m_lifeState"] = PropertyValue.FromInt(0),
                }),
            Entity(
                decoder,
                ViewmodelClassId,
                ViewmodelEntityIndex,
                Viewmodel(modelIndex: 4, slot: 0, owner)),
            Entity(
                decoder,
                ViewmodelClassId,
                OffHandEntityIndex,
                Viewmodel(offHandModelIndex, slot: 1, offHandOwner ?? owner, hidden)),
        ];

        byte[] body = decoder.EncodeEntities(
            [.. entities], [], isDelta: false, 0, out int bits);

        return SyntheticDemo.Packet(
            SyntheticDemo.DefaultProtocol,
            HiddenTick,
            new PacketEntitiesMessage(
                MaxEntries: 64,
                IsDelta: false,
                DeltaFromTick: null,
                BaselineIndex: false,
                UpdatedEntries: entities.Count,
                LengthBits: bits,
                UpdateBaseline: false,
                Body: body));
    }

    /// <summary>One viewmodel's properties, as <c>DT_BaseViewModel</c> carries them.</summary>
    /// <param name="modelIndex">Index into <c>modelprecache</c>.</param>
    /// <param name="slot">0 for the weapon in hand, 1 for the off hand.</param>
    /// <param name="owner">
    /// The owning player, or <c>null</c> for the point-of-view shape where the demo names nobody.
    /// </param>
    /// <param name="hidden">
    /// Whether to set <c>EF_NODRAW</c>, as <c>CTFWeaponInvis::SetWeaponVisible</c> does on the
    /// watch's viewmodel when it is not deployed.
    /// </param>
    private static Dictionary<string, PropertyValue> Viewmodel(
        int modelIndex, int slot, int? owner, bool hidden = false)
    {
        Dictionary<string, PropertyValue> properties = new()
        {
            ["m_nModelIndex"] = PropertyValue.FromInt(modelIndex),
            ["m_nSequence"] = PropertyValue.FromInt(7),
            ["m_flPlaybackRate"] = PropertyValue.FromFloat(1f),
            ["m_nViewModelIndex"] = PropertyValue.FromInt(slot),

            // Always sent, hidden or not, because the engine sends the whole field and a fixture
            // that omitted it when clear would not distinguish "no flags" from "never said".
            ["m_fEffects"] = PropertyValue.FromInt(hidden ? 0x020 : 0),
        };

        // Absent rather than zero: an unset handle is how a POV demo says "mine", and zero would
        // be entity slot zero, which is the world.
        if (owner is { } entity)
        {
            properties["m_hOwner"] = PropertyValue.FromInt(entity);
        }

        return properties;
    }

    /// <summary>A schema that also declares a viewmodel class, with no base table.</summary>
    /// <remarks>
    /// **<c>DT_BaseViewModel</c> and nothing else, which is the point.** The real table is declared
    /// <c>BEGIN_NETWORK_TABLE_NOBASE</c>, so a viewmodel inherits no <c>DT_BaseEntity</c> — no
    /// origin, no angles, and an owner under <c>m_hOwner</c> rather than <c>m_hOwnerEntity</c>. A
    /// fixture that gave it a base table would let a reader looking in the wrong place pass.
    /// </remarks>
    private static DemoSchema SchemaWithViewmodel()
    {
        DemoSchema baseline = Schema();

        return new DemoSchema(
            [
                .. baseline.Tables,
                new SendTable("DT_BaseViewModel", NeedsDecoder: true,
                [
                    Int("m_nModelIndex", bits: 13),
                    Int("m_nSequence", bits: 8),
                    Float("m_flPlaybackRate", low: -4f, high: 12f, bits: 8),
                    Int("m_hOwner", bits: 21),

                    // One bit unsigned, as `VIEWMODEL_INDEX_BITS` declares it — signed would make
                    // slot 1 arrive as -1 and the fixture would agree with a reader that never
                    // matched it.
                    UnsignedInt("m_nViewModelIndex", bits: 1),

                    // **Ten bits unsigned, and the reason this table needed correcting.** NOBASE
                    // means a viewmodel inherits no DT_BaseEntity, and this project read that as
                    // "so it has no m_fEffects" — but the table declares its own
                    // (`baseviewmodel_shared.cpp:565`), and EF_NODRAW on it is how the engine hides
                    // the spy's watch. A fixture without it would agree with the wrong reader.
                    UnsignedInt("m_fEffects", bits: 10),
                ]),
            ],
            [
                .. baseline.ServerClasses,
                new ServerClass(ViewmodelClassId, "CTFViewModel", "DT_BaseViewModel"),
            ]);
    }

    /// <summary>A schema that also declares an ordinary drawable prop class.</summary>
    internal static DemoSchema SchemaWithProp()
    {
        DemoSchema baseline = Schema();

        return new DemoSchema(
            [
                // **A prop's origin lives on DT_BaseEntity, and the first draft of this fixture put
                // it on the prop's own table.** EntityState.Origin() searches exactly three tables
                // — the two player exclusives and DT_BaseEntity — so an origin anywhere else is
                // not a near miss, it is no match at all.
                //
                // The consequence is silent and total: RecordProp returns early for an entity with
                // neither an origin nor an attachment, so the prop decoded perfectly, carried its
                // model index, and produced no track. Props came back empty and it read as a
                // broken precache. See docs/memory/a-property-name-needs-its-declaring-table.md.
                new SendTable("DT_BaseEntity", NeedsDecoder: true,
                [
                    Int("m_nModelIndex", bits: 13),
                    Int("m_fEffects", bits: 11),
                    Int("m_iTeamNum", bits: 3),
                    VectorXy("m_vecOrigin", bits: 32),
                    Float("m_vecOrigin[2]", low: -16384f, high: 16384f, bits: 32),
                ]),
                .. baseline.Tables.Where(
                    table => !string.Equals(table.Name, "DT_BaseEntity", StringComparison.Ordinal)),
                new SendTable("DT_BaseAnimatingProp", NeedsDecoder: true,
                [
                    Table("baseanimating", "DT_BaseAnimating"),
                ]),
            ],
            [
                .. baseline.ServerClasses,
                new ServerClass(PropClassId, "CBaseAnimating", "DT_BaseAnimatingProp"),
            ]);
    }

    /// <summary>Builds one entity's update from named property values.</summary>
    private static DecodedEntity Entity(
        EntityDecoder decoder,
        int classId,
        int entityIndex,
        IReadOnlyDictionary<string, PropertyValue> values)
    {
        IReadOnlyList<FlatProperty> flat = decoder.FlattenedFor(classId);
        List<DecodedProperty> properties = [];

        foreach ((string name, PropertyValue value) in values)
        {
            int index = IndexOf(flat, name);
            properties.Add(new DecodedProperty(index, flat[index], value));
        }

        properties.Sort((left, right) => left.Index.CompareTo(right.Index));

        return new DecodedEntity(
            entityIndex, classId, entityIndex, EntityUpdateType.Enter, properties);
    }

    /// <summary>The flattened index of a property, or a failure naming what was available.</summary>
    /// <remarks>
    /// Throws rather than returning -1 on purpose. A missing property silently encodes nothing,
    /// and the test then fails on an assertion about a value that was never sent — which reads as
    /// a decoder bug. Listing what the class does hold is the useful half of the message, because
    /// the usual cause is a property declared in the wrong table.
    ///
    /// **Matches the qualified name as well as the bare one, and an array is why.** Valve's
    /// <c>SendPropArray</c> generates a sub-table named after the array, so an element's own name
    /// is only <c>001</c> while the key everything else uses is <c>m_iTeam.001</c>. Matching the
    /// bare name alone finds nothing for those, and matching it alone would also make <c>001</c>
    /// ambiguous across two arrays.
    /// </remarks>
    private static int IndexOf(IReadOnlyList<FlatProperty> flat, string name)
    {
        for (int i = 0; i < flat.Count; i++)
        {
            string qualified = $"{flat[i].OwnerTable}.{flat[i].Property.Name}";

            if (string.Equals(flat[i].Property.Name, name, StringComparison.Ordinal) ||
                string.Equals(qualified, name, StringComparison.Ordinal))
            {
                return i;
            }
        }

        throw new ArgumentException(
            $"'{name}' is not in the flattened list. It holds: " +
            string.Join(", ", flat.Select(entry => $"{entry.OwnerTable}.{entry.Property.Name}")),
            nameof(name));
    }

    private static ServerInfoMessage ServerInfo(float intervalPerTick = 1f / 66.67f) => new(
        NetworkProtocol: SyntheticDemo.DefaultProtocol,
        ServerCount: 1,
        IsSourceTv: true,
        IsDedicated: true,
        MapCrc: 0,
        MaxClasses: 1,
        MapHash: new byte[16],
        PlayerSlot: 0,
        MaxPlayers: 24,
        IntervalPerTick: intervalPerTick,
        Platform: 'w',
        GameDirectory: "tf",
        Map: "cp_process_final",
        Skybox: "sky_tf2_04",
        ServerName: "synthetic",
        IsReplay: false);

    private static SendProperty Int(string name, int bits) =>
        new(SendPropType.Int, name, 0, string.Empty, 0f, 0f, bits, 0);

    private static SendProperty UnsignedInt(string name, int bits) =>
        new(SendPropType.Int, name, UnsignedFlag, string.Empty, 0f, 0f, bits, 0);

    private static SendProperty Float(string name, float low, float high, int bits) =>
        new(SendPropType.Float, name, 0, string.Empty, low, high, bits, 0);

    private static SendProperty VectorXy(string name, int bits) =>
        new(SendPropType.VectorXY, name, 0, string.Empty, -16384f, 16384f, bits, 0);

    private static SendProperty Table(string name, string referenced) =>
        new(SendPropType.DataTable, name, 0, referenced, 0f, 0f, 0, 0);
}
