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
                // Unsigned, as `SendPropInt( SENDINFO( m_nWaterLevel ), 2, SPROP_UNSIGNED )` sends it (tf_player.cpp:792).
                // Declared signed it read waist deep, 2, back as -2, which no fixture noticed until one sent it (B112).
                UnsignedInt("m_nWaterLevel", bits: 2),
                Int("m_iTeamNum", bits: 3),

                // **The three per-BONE scales** (B312), on `DT_TFPlayer` exactly as
                // `c_tf_player.cpp:539` declares them — not on an exclusive table, so they arrive
                // for every player rather than only the recorder.
                //
                // **Declared here so a demo can carry a value other than 1**, which the corpus
                // cannot: every recording in it is ordinary play and reports all three at 1, so
                // the only way to observe the behaviour end to end is to author the specimen.
                Float("m_flHeadScale", low: 0f, high: 8f, bits: 16),
                Float("m_flTorsoScale", low: 0f, high: 8f, bits: 16),
                Float("m_flHandScale", low: 0f, high: 8f, bits: 16),
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

    /// <summary>
    /// A schema that also declares <c>DT_TFPlayerShared</c>, where the conditions live (B336).
    /// </summary>
    /// <returns>The schema.</returns>
    /// <remarks>
    /// **A variant rather than a change to <see cref="Schema()"/>, deliberately.** Adding a
    /// property to the shared schema shifts every flattened index for every test that uses it —
    /// the shared-helper hazard `docs/memory/instrument-bugs-outnumber-decoder-bugs.md` is about,
    /// where a change to what a helper MEANS passes every targeted check.
    ///
    /// **`DT_TFPlayer` has to be rebuilt rather than appended to**, which is the difference from
    /// <see cref="SchemaWithResource"/>: the conditions are reached through a link from the player
    /// table, and the flattened key a reader looks up is <c>DT_TFPlayerShared.m_nPlayerCond</c> —
    /// the OWNER table supplies the prefix. A table nothing links to is flattened into nothing, and
    /// the player comes back with no conditions and no error.
    ///
    /// **32 bits unsigned, because the conditions are a bitfield and bit 31 is a real condition.**
    /// A signed 32-bit read makes the top bit negative, and `InCond` tests it by mask.
    /// </remarks>
    public static DemoSchema SchemaWithConditions()
    {
        DemoSchema baseline = Schema(OriginTable.NonLocal);

        List<SendTable> tables = [];

        foreach (SendTable table in baseline.Tables)
        {
            tables.Add(
                table.Name == "DT_TFPlayer"
                    ? table with
                    {
                        Properties =
                        [
                            .. table.Properties,
                            Table("playershared", "DT_TFPlayerShared"),
                        ],
                    }
                    : table);
        }

        tables.Add(new SendTable("DT_TFPlayerShared", NeedsDecoder: true,
        [
            UnsignedInt("m_nPlayerCond", bits: 32),
            UnsignedInt("m_nPlayerCondEx", bits: 32),
            UnsignedInt("m_nPlayerCondEx2", bits: 32),
            UnsignedInt("m_nPlayerCondEx3", bits: 32),
            UnsignedInt("m_nPlayerCondEx4", bits: 32),
        ]));

        return new DemoSchema(tables, baseline.ServerClasses);
    }

    /// <summary>One player whose conditions change over several snapshots (B336).</summary>
    /// <param name="intervalPerTick">Seconds per tick, as <c>svc_ServerInfo</c> declares it.</param>
    /// <param name="states">One entry per snapshot: the tick and the <c>m_nPlayerCond</c> word.</param>
    /// <returns>A demo's bytes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="states"/> is null.</exception>
    /// <remarks>
    /// **The corpus cannot answer what this asks.** A burn clock is derived from the TICK a
    /// condition bit turns on, so testing it needs a recording where that tick is known — and the
    /// demos that contain burning at all are on maps this machine does not have (B336). Authoring
    /// the specimen is the only way to predict the answer rather than compare two readings of it.
    /// </remarks>
    public static byte[] DemoOfConditionsOverTicks(
        float intervalPerTick, params (int Tick, int Conditions)[] states)
    {
        ArgumentNullException.ThrowIfNull(states);

        DemoSchema schema = SchemaWithConditions();
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
            (int tick, int conditions) = states[index];

            Dictionary<string, PropertyValue> values = new()
            {
                // A position every snapshot, because a player with nowhere to be is declined
                // before becoming a frame at all.
                ["m_vecOrigin"] = PropertyValue.FromVectorXY(64f, 0f),
                ["m_vecOrigin[2]"] = PropertyValue.FromFloat(0f),
                ["m_nPlayerCond"] = PropertyValue.FromInt(conditions),
            };

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
                    DeltaFromTick: index > 0 ? states[index - 1].Tick : null,
                    BaselineIndex: false,
                    UpdatedEntries: 1,
                    LengthBits: bits,
                    UpdateBaseline: false,
                    Body: body)));
        }

        return SyntheticDemo.From(SyntheticDemo.DefaultProtocol, [.. commands]);
    }

    /// <summary>Class id of the <c>CTFGameRulesProxy</c> in <see cref="DemoOfGestures"/>.</summary>
    private const int GestureRulesClassId = 1;

    /// <summary>Class id of <c>CTEPlayerAnimEvent</c> in <see cref="DemoOfGestures"/>.</summary>
    private const int AnimEventClassId = 2;

    /// <summary>
    /// The game rules entity's slot in <see cref="DemoOfGestures"/> — and an entity that exists, for a grappling
    /// hook to name.
    /// </summary>
    public const int GestureRulesEntityIndex = 40;

    /// <summary>`INVALID_NETWORKED_EHANDLE_VALUE`: eleven index bits and ten serial bits, all set.</summary>
    private const int InvalidNetworkedHandle = (1 << 21) - 1;

    /// <summary>One snapshot of the player <see cref="DemoOfGestures"/> describes (B112).</summary>
    /// <param name="Tick">The snapshot's tick.</param>
    /// <param name="Z">The player's height.</param>
    /// <param name="Flags"><c>m_fFlags</c>: <c>FL_ONGROUND</c> 1, <c>FL_DUCKING</c> 2.</param>
    /// <remarks>
    /// **Every field is sent on every snapshot**, including the ones at their default, so a test that changes one
    /// for a single tick changes it back the next without relying on the delta decoder to do it.
    /// </remarks>
    internal sealed record GestureSnapshot(int Tick, float Z, int Flags)
    {
        /// <summary><c>m_lifeState</c>: 0 alive, 2 dead.</summary>
        public int LifeState { get; init; }

        /// <summary><c>m_fEffects</c>; <c>EF_NODRAW</c> is 0x020.</summary>
        public int Effects { get; init; }

        /// <summary><c>m_nWaterLevel</c>: 2 is waist deep.</summary>
        public int WaterLevel { get; init; }

        /// <summary><c>m_nPlayerCond</c>; <c>TF_COND_AIMING</c> is bit 0.</summary>
        public int PlayerCond { get; init; }

        /// <summary><c>m_iszCustomModel</c>, empty for none.</summary>
        public string CustomModel { get; init; } = string.Empty;

        /// <summary><c>m_bUseClassAnimations</c>.</summary>
        public bool UsesClassAnimations { get; init; }

        /// <summary>The entity <c>m_hGrapplingHookTarget</c> names, or null for the invalid handle.</summary>
        public int? GrapplingHookTarget { get; init; }

        /// <summary>
        /// The serial the handle carries above its index: 1, the one every entity in this fixture enters with, unless
        /// a test names a slot that has since changed hands.
        /// </summary>
        public int GrapplingHookSerial { get; init; } = 1;

        /// <summary>Leaves the PVS at this snapshot: a LEAVE update and nothing else, then an ENTER after it.</summary>
        public bool Dormant { get; init; }

        /// <summary>The events the player raises in this snapshot's packet, after its entities, in order.</summary>
        public IReadOnlyList<PlayerAnimEvent> Events { get; init; } = [];
    }

    /// <summary>
    /// A player whose height, flags and state change over several snapshots and who raises gesture events,
    /// beside a game rules entity (B112).
    /// </summary>
    /// <param name="intervalPerTick">Seconds per tick, as <c>svc_ServerInfo</c> declares it.</param>
    /// <param name="team">The player's <c>m_iTeamNum</c>.</param>
    /// <param name="playerClass">The player's <c>m_iClass</c>.</param>
    /// <param name="rules">
    /// <c>m_iRoundState</c>, <c>m_iWinningTeam</c> and <c>m_nMatchGroupType</c> (-1 for none), or null for a demo with
    /// no game rules.
    /// </param>
    /// <param name="alwaysLoser">Whether the server sends <c>tf_always_loser 1</c> at signon.</param>
    /// <param name="snapshots">The snapshots, in tick order.</param>
    /// <returns>A demo's bytes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="snapshots"/> is null.</exception>
    /// <remarks>
    /// **The corpus cannot give ground truth for this, and a synthetic demo can.** Whether a reload is the
    /// air-walking one depends on a latch the demo never carries — it is the client's, rebuilt from the height
    /// and the flags — so the only way to predict the answer rather than compare two readings is to author the
    /// rise, the landing and the event at known ticks. The events travel as the wire carries them, as
    /// <c>CTEPlayerAnimEvent</c> temp entities after the snapshot in the same packet.
    /// </remarks>
    public static byte[] DemoOfGestures(
        float intervalPerTick,
        int team,
        int playerClass,
        (int RoundState, int WinningTeam, int MatchGroup)? rules,
        bool alwaysLoser,
        params GestureSnapshot[] snapshots)
    {
        ArgumentNullException.ThrowIfNull(snapshots);

        DemoSchema schema = SchemaWithGestures();
        EntityDecoder decoder = new(schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));

        ServerInfoMessage serverInfo = ServerInfo(intervalPerTick);
        List<INetMessage> signon = [serverInfo];

        if (alwaysLoser)
        {
            signon.Add(new SetConVarMessage([new KeyValuePair<string, string>("tf_always_loser", "1")]));
        }

        List<DemoCommand> commands =
        [
            SyntheticDemo.Packet(SyntheticDemo.DefaultProtocol, 0, [.. signon]),
            SyntheticDemo.DataTables(schema),
        ];

        bool present = false;

        for (int index = 0; index < snapshots.Length; index++)
        {
            GestureSnapshot snapshot = snapshots[index];
            List<DecodedEntity> entities = [];

            if (snapshot.Dormant)
            {
                entities.Add(new DecodedEntity(1, PlayerClassId, 1, EntityUpdateType.Leave, []));
                present = false;
            }
            else
            {
                entities.Add(Entity(decoder, PlayerClassId, 1, GesturePlayerValues(snapshot, team, playerClass)) with
                {
                    SerialNumber = 1,
                    UpdateType = present ? EntityUpdateType.Delta : EntityUpdateType.Enter,
                });
                present = true;
            }

            if (index == 0 && rules is { } round)
            {
                entities.Add(Entity(
                    decoder,
                    GestureRulesClassId,
                    GestureRulesEntityIndex,
                    new Dictionary<string, PropertyValue>
                    {
                        ["m_iRoundState"] = PropertyValue.FromInt(round.RoundState),
                        ["m_iWinningTeam"] = PropertyValue.FromInt(round.WinningTeam),
                        ["m_nMatchGroupType"] = PropertyValue.FromInt(round.MatchGroup),
                    }) with { SerialNumber = 1 });
            }

            byte[] body = decoder.EncodeEntities(entities, [], isDelta: index > 0, 0, out int bits);

            List<INetMessage> messages =
            [
                new PacketEntitiesMessage(
                    MaxEntries: 64,
                    IsDelta: index > 0,
                    DeltaFromTick: index > 0 ? snapshots[index - 1].Tick : null,
                    BaselineIndex: false,
                    UpdatedEntries: entities.Count,
                    LengthBits: bits,
                    UpdateBaseline: false,
                    Body: body),
            ];

            if (snapshot.Events.Count > 0)
            {
                byte[] effects = decoder.EncodeTempEntities(
                    [.. snapshot.Events.Select(anEvent => AnimEvent(decoder, 1, anEvent))], reliable: false, lengthBits: 0);

                messages.Add(new TempEntitiesMessage(Count: snapshot.Events.Count, BodyBits: effects.Length * 8, Body: effects));
            }

            // After the signon's server info, as the reader reads it: the temp entities' length field is a VarInt at this
            // protocol and a 17-bit field before ServerInfo, and a packet written without it is read nine bits out.
            commands.Add(SyntheticDemo.PacketAfter(serverInfo, snapshot.Tick, [.. messages]));
        }

        return SyntheticDemo.From(SyntheticDemo.DefaultProtocol, [.. commands]);
    }

    /// <summary>What <see cref="DemoOfGestures"/> sends for its player on one snapshot.</summary>
    private static Dictionary<string, PropertyValue> GesturePlayerValues(GestureSnapshot snapshot, int team, int playerClass) =>
        new()
        {
            ["m_vecOrigin"] = PropertyValue.FromVectorXY(64f, 0f),
            ["m_vecOrigin[2]"] = PropertyValue.FromFloat(snapshot.Z),
            ["m_fFlags"] = PropertyValue.FromInt(snapshot.Flags),
            ["m_lifeState"] = PropertyValue.FromInt(snapshot.LifeState),
            ["m_fEffects"] = PropertyValue.FromInt(snapshot.Effects),
            ["m_nWaterLevel"] = PropertyValue.FromInt(snapshot.WaterLevel),

            // Qualified, because the fixture's DT_TFPlayer declares an m_iTeamNum too and the timeline reads this one.
            ["DT_BaseEntity.m_iTeamNum"] = PropertyValue.FromInt(team),
            ["m_iClass"] = PropertyValue.FromInt(playerClass),
            ["m_iszCustomModel"] = PropertyValue.FromString(snapshot.CustomModel),
            ["m_bUseClassAnimations"] = PropertyValue.FromInt(snapshot.UsesClassAnimations ? 1 : 0),
            ["m_nPlayerCond"] = PropertyValue.FromInt(snapshot.PlayerCond),

            // The serial above the eleven index bits: 1 unless the test says otherwise.
            ["m_hGrapplingHookTarget"] = PropertyValue.FromInt(
                snapshot.GrapplingHookTarget is { } target
                    ? target | (snapshot.GrapplingHookSerial << 11)
                    : InvalidNetworkedHandle),
        };

    /// <summary>One <c>CTEPlayerAnimEvent</c>, carrying every field as a full update.</summary>
    private static DecodedTempEntity AnimEvent(EntityDecoder decoder, int player, PlayerAnimEvent anEvent)
    {
        IReadOnlyList<FlatProperty> flat = decoder.FlattenedFor(AnimEventClassId);

        List<DecodedProperty> properties =
        [
            new(IndexOf(flat, "m_iPlayerIndex"), flat[IndexOf(flat, "m_iPlayerIndex")], PropertyValue.FromInt(player)),
            new(IndexOf(flat, "m_iEvent"), flat[IndexOf(flat, "m_iEvent")], PropertyValue.FromInt((int)anEvent)),
            new(IndexOf(flat, "m_nData"), flat[IndexOf(flat, "m_nData")], PropertyValue.FromInt(0)),
        ];

        properties.Sort((left, right) => left.Index.CompareTo(right.Index));

        return new DecodedTempEntity(ClassId: AnimEventClassId, DelaySeconds: 0f, Properties: properties);
    }

    /// <summary>
    /// The player schema with the tables <see cref="DemoOfGestures"/> needs: the class and its custom model, the
    /// conditions, the grappling hook, the round, and the temp entity a gesture travels in.
    /// </summary>
    /// <remarks>
    /// **A variant rather than a change to <see cref="Schema()"/>**, for the reason
    /// <see cref="SchemaWithConditions"/> gives: a property added to the shared schema shifts every flattened index
    /// for every test that uses it. The round state is reached through
    /// <c>teamplayroundbased_gamerules_data</c> as TF2's proxy reaches it, so the key the timeline reads is
    /// <c>DT_TeamplayRoundBasedRules.m_iRoundState</c>.
    /// </remarks>
    private static DemoSchema SchemaWithGestures()
    {
        DemoSchema baseline = Schema(OriginTable.NonLocal);

        List<SendTable> tables = [];

        foreach (SendTable table in baseline.Tables)
        {
            tables.Add(
                table.Name == "DT_TFPlayer"
                    ? table with
                    {
                        Properties =
                        [
                            .. table.Properties,
                            UnsignedInt("m_hGrapplingHookTarget", bits: 21),
                            Table("playerclass", "DT_TFPlayerClassShared"),
                            Table("playershared", "DT_TFPlayerShared"),
                        ],
                    }
                    : table);
        }

        tables.Add(new SendTable("DT_TFPlayerClassShared", NeedsDecoder: true,
        [
            UnsignedInt("m_iClass", bits: 4),
            String("m_iszCustomModel"),
            UnsignedInt("m_bUseClassAnimations", bits: 1),
        ]));
        tables.Add(new SendTable("DT_TFPlayerShared", NeedsDecoder: true, [UnsignedInt("m_nPlayerCond", bits: 32)]));
        tables.Add(new SendTable("DT_TeamplayRoundBasedRules", NeedsDecoder: true,
        [
            Int("m_iRoundState", bits: 5),
            Int("m_iWinningTeam", bits: 8),
        ]));
        // `SendPropInt( SENDINFO( m_nMatchGroupType ) )` (tf_gamerules.cpp:1536): the default width, 32 bits and
        // signed, so "no match group", -1, travels as itself.
        tables.Add(new SendTable("DT_TFGameRules", NeedsDecoder: true, [Int("m_nMatchGroupType", bits: 32)]));
        tables.Add(new SendTable("DT_TFGameRulesProxy", NeedsDecoder: true,
        [
            Table("teamplayroundbased_gamerules_data", "DT_TeamplayRoundBasedRules"),
            Table("tf_gamerules_data", "DT_TFGameRules"),
        ]));
        tables.Add(new SendTable("DT_TEPlayerAnimEvent", NeedsDecoder: true,
        [
            UnsignedInt("m_iPlayerIndex", bits: 7),
            UnsignedInt("m_iEvent", bits: 6),
            Int("m_nData", bits: 32),
        ]));

        return new DemoSchema(
            tables,
            [
                .. baseline.ServerClasses,
                new ServerClass(GestureRulesClassId, "CTFGameRulesProxy", "DT_TFGameRulesProxy"),
                new ServerClass(AnimEventClassId, "CTEPlayerAnimEvent", "DT_TEPlayerAnimEvent"),
            ]);
    }

    /// <summary>One of Valve's generated array sub-tables: properties named 000, 001, …</summary>
    private static SendTable ArrayTable(string name, int bits = 5) => new(
        name,
        NeedsDecoder: true,
        [
            .. Enumerable.Range(0, ResourceSlots).Select(
                slot => Int(slot.ToString("D3", CultureInfo.InvariantCulture), bits: bits)),
        ]);

    /// <summary>The recorder (entity 1) holding weapons and wearing wearables, each an item with a definition index.</summary>
    /// <param name="weapons">Each weapon: its entity slot and item definition, in `m_hMyWeapons` order.</param>
    /// <param name="wearables">Each wearable, in `m_hMyWearables` order.</param>
    /// <param name="staleWearable">A wearable handle left in the vector past its length, which must not be read.</param>
    /// <param name="activeWeaponSerial">
    /// The serial `m_hActiveWeapon` carries for the first weapon, or null for that weapon's own — so a test can name a
    /// slot whose occupant has changed.
    /// </param>
    /// <returns>A demo's bytes.</returns>
    public static byte[] DemoWithLoadout(
        (int Entity, int Definition)[] weapons,
        (int Entity, int Definition)[] wearables,
        int? staleWearable = null,
        int? activeWeaponSerial = null)
    {
        ArgumentNullException.ThrowIfNull(weapons);
        ArgumentNullException.ThrowIfNull(wearables);

        const int ItemClassId = 1;
        DemoSchema baseline = Schema(OriginTable.NonLocal);
        List<SendTable> tables = [];

        foreach (SendTable table in baseline.Tables)
        {
            tables.Add(
                table.Name == "DT_BasePlayer"
                    ? table with
                    {
                        Properties =
                        [
                            .. table.Properties,
                            Table("m_hMyWeapons", "m_hMyWeapons"),
                            Table("m_hMyWearables", "_ST_m_hMyWearables_8"),
                            Table("bcc", "DT_BaseCombatCharacter"),
                        ],
                    }
                    : table);
        }

        tables.Add(new SendTable("m_hMyWeapons", NeedsDecoder: true,
            [.. Enumerable.Range(0, 48).Select(slot => UnsignedInt(slot.ToString("D3", CultureInfo.InvariantCulture), bits: 21))]));
        // As `SendPropUtlVector` builds it: the length under a `lengthproxy` member, then the elements.
        tables.Add(new SendTable("_ST_m_hMyWearables_8", NeedsDecoder: true,
        [
            Table("lengthproxy", "_LPT_m_hMyWearables_8"),
            .. Enumerable.Range(0, 8).Select(slot => UnsignedInt(slot.ToString("D3", CultureInfo.InvariantCulture), bits: 21)),
        ]));
        tables.Add(new SendTable("_LPT_m_hMyWearables_8", NeedsDecoder: true, [UnsignedInt("lengthprop8", bits: 4)]));
        tables.Add(new SendTable("DT_ScriptCreatedItem", NeedsDecoder: true, [UnsignedInt("m_iItemDefinitionIndex", bits: 16)]));
        tables.Add(new SendTable("DT_BaseCombatCharacter", NeedsDecoder: true, [UnsignedInt("m_hActiveWeapon", bits: 21)]));
        tables.Add(new SendTable("DT_LocalWeaponData", NeedsDecoder: true, [UnsignedInt("m_iClip1", bits: 8)]));
        tables.Add(new SendTable("DT_TestItem", NeedsDecoder: true, [Table("m_Item", "DT_ScriptCreatedItem"), Table("local", "DT_LocalWeaponData")]));

        DemoSchema schema = new(tables, [.. baseline.ServerClasses, new ServerClass(ItemClassId, "CTFScatterGun", "DT_TestItem")]);
        EntityDecoder decoder = new(schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));

        Dictionary<string, PropertyValue> player = new()
        {
            ["m_vecOrigin"] = PropertyValue.FromVectorXY(0f, 0f),
            ["m_vecOrigin[2]"] = PropertyValue.FromFloat(0f),
            ["m_lifeState"] = PropertyValue.FromInt(0),
            ["_LPT_m_hMyWearables_8.lengthprop8"] = PropertyValue.FromInt(wearables.Length),
        };

        if (weapons.Length > 0)
        {
            player["DT_BaseCombatCharacter.m_hActiveWeapon"] = LoadoutHandle(weapons[0].Entity, activeWeaponSerial);
        }

        for (int slot = 0; slot < weapons.Length; slot++)
        {
            player[$"m_hMyWeapons.{slot:D3}"] = LoadoutHandle(weapons[slot].Entity);
        }

        for (int slot = 0; slot < wearables.Length; slot++)
        {
            player[$"_ST_m_hMyWearables_8.{slot:D3}"] = LoadoutHandle(wearables[slot].Entity);
        }

        if (staleWearable is { } stale)
        {
            player[$"_ST_m_hMyWearables_8.{wearables.Length:D3}"] = LoadoutHandle(stale);
        }

        List<DecodedEntity> entities = [Entity(decoder, PlayerClassId, 1, player)];

        foreach ((int entity, int definition) in weapons.Concat(wearables).OrderBy(item => item.Entity))
        {
            // Every weapon's clip is 5 on the wire: `SendProxy_IntAddOne` of a clip of 4.
            entities.Add(Entity(decoder, ItemClassId, entity, new Dictionary<string, PropertyValue>
            {
                ["DT_ScriptCreatedItem.m_iItemDefinitionIndex"] = PropertyValue.FromInt(definition),
                ["DT_LocalWeaponData.m_iClip1"] = PropertyValue.FromInt(5),
            }));
        }

        byte[] body = decoder.EncodeEntities(entities, [], isDelta: false, 0, out int bits);

        return SyntheticDemo.From(
            SyntheticDemo.DefaultProtocol,
            SyntheticDemo.Packet(SyntheticDemo.DefaultProtocol, 0, ServerInfo()),
            SyntheticDemo.DataTables(schema),
            SyntheticDemo.Packet(
                SyntheticDemo.DefaultProtocol,
                100,
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
    /// The recorder (entity 1) with every field `CHudItemEffectMeter` reads (tf_hud_itemeffectmeter.cpp): the rage, hype,
    /// revenge, rune and item charge meters, the kart and the spawn counter, and weapon 30 carrying every per-weapon field.
    /// </summary>
    /// <returns>A demo's bytes.</returns>
    public static byte[] DemoWithItemEffectMeterFields()
    {
        const int ItemClassId = 1;
        DemoSchema baseline = Schema(OriginTable.NonLocal);
        List<SendTable> tables = [];

        foreach (SendTable table in baseline.Tables)
        {
            tables.Add(table.Name switch
            {
                "DT_TFPlayer" => table with
                {
                    Properties =
                    [
                        .. table.Properties, Table("playershared", "DT_TFPlayerShared"),
                        NoScaleFloat("m_flKartNextAvailableBoost"), Int("m_iKartHealth", bits: 10), Int("m_iSpawnCounter", bits: 8),
                    ],
                },
                "DT_BasePlayer" => table with { Properties = [.. table.Properties, Table("m_hMyWeapons", "m_hMyWeapons")] },
                _ => table,
            });
        }

        tables.Add(new SendTable("DT_TFPlayerShared", NeedsDecoder: true,
        [
            NoScaleFloat("m_flHypeMeter"), UnsignedInt("m_iRevengeCrits", bits: 7), NoScaleFloat("m_flRuneCharge"),
            Table("tfsharedlocaldata", "DT_TFPlayerSharedLocal"),
        ]));
        tables.Add(new SendTable("DT_TFPlayerSharedLocal", NeedsDecoder: true,
        [
            NoScaleFloat("m_flRageMeter"), UnsignedInt("m_bRageDraining", bits: 1), Table("m_flItemChargeMeter", "m_flItemChargeMeter"),
        ]));
        tables.Add(new SendTable("m_flItemChargeMeter", NeedsDecoder: true,
            [.. Enumerable.Range(0, 11).Select(slot => NoScaleFloat(slot.ToString("D3", CultureInfo.InvariantCulture)))]));
        tables.Add(new SendTable("m_hMyWeapons", NeedsDecoder: true, [UnsignedInt("000", bits: 21)]));
        tables.Add(new SendTable("DT_LocalTFWeaponData", NeedsDecoder: true, [NoScaleFloat("m_flEffectBarRegenTime")]));
        tables.Add(new SendTable("DT_TFWeaponBase", NeedsDecoder: true,
        [
            Table("LocalActiveTFWeaponData", "DT_LocalTFWeaponData"), NoScaleFloat("m_flEnergy"),
            UnsignedInt("m_nKillComboClass", bits: 4), UnsignedInt("m_nKillComboCount", bits: 2),
        ]));
        tables.Add(new SendTable("DT_TFWeaponKnife", NeedsDecoder: true,
        [
            UnsignedInt("m_bKnifeExists", bits: 1), NoScaleFloat("m_flKnifeRegenerateDuration"), NoScaleFloat("m_flKnifeMeltTimestamp"),
        ]));
        tables.Add(new SendTable("DT_WeaponChargedSMG", NeedsDecoder: true, [NoScaleFloat("m_flMinicritCharge")]));
        tables.Add(new SendTable("DT_TFWeaponRocketPack", NeedsDecoder: true, [UnsignedInt("m_bEnabled", bits: 1)]));
        tables.Add(new SendTable("DT_ParticleCannon", NeedsDecoder: true, [NoScaleFloat("m_flChargeBeginTime")]));
        tables.Add(new SendTable("DT_TFPowerupBottle", NeedsDecoder: true, [UnsignedInt("m_usNumCharges", bits: 8)]));
        tables.Add(new SendTable("DT_TestItem", NeedsDecoder: true,
        [
            Table("base", "DT_TFWeaponBase"), Table("knife", "DT_TFWeaponKnife"), Table("smg", "DT_WeaponChargedSMG"),
            Table("pack", "DT_TFWeaponRocketPack"), Table("cannon", "DT_ParticleCannon"), Table("bottle", "DT_TFPowerupBottle"),
            Table("LocalWeaponData", "DT_LocalWeaponData"),
        ]));
        tables.Add(new SendTable("DT_LocalWeaponData", NeedsDecoder: true, [Int("m_iPrimaryAmmoType", bits: 8)]));

        DemoSchema schema = new(tables, [.. baseline.ServerClasses, new ServerClass(ItemClassId, "CTFKnife", "DT_TestItem")]);
        EntityDecoder decoder = new(schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));

        Dictionary<string, PropertyValue> player = new()
        {
            ["m_vecOrigin"] = PropertyValue.FromVectorXY(0f, 0f),
            ["m_vecOrigin[2]"] = PropertyValue.FromFloat(0f),
            ["m_lifeState"] = PropertyValue.FromInt(0),
            ["m_flHypeMeter"] = PropertyValue.FromFloat(41.5f),
            ["m_iRevengeCrits"] = PropertyValue.FromInt(6),
            ["m_flRuneCharge"] = PropertyValue.FromFloat(62.5f),
            ["m_flRageMeter"] = PropertyValue.FromFloat(88.25f),
            ["m_bRageDraining"] = PropertyValue.FromInt(1),
            ["m_flItemChargeMeter.001"] = PropertyValue.FromFloat(12.75f),
            ["m_flItemChargeMeter.010"] = PropertyValue.FromFloat(99.5f),
            ["m_flKartNextAvailableBoost"] = PropertyValue.FromFloat(203.5f),
            ["m_iKartHealth"] = PropertyValue.FromInt(137),
            ["m_iSpawnCounter"] = PropertyValue.FromInt(1),
            ["m_hMyWeapons.000"] = LoadoutHandle(30),
        };

        List<DecodedEntity> entities =
        [
            Entity(decoder, PlayerClassId, 1, player),
            Entity(decoder, ItemClassId, 30, new Dictionary<string, PropertyValue>
            {
                ["m_flEffectBarRegenTime"] = PropertyValue.FromFloat(311.25f),
                ["m_flEnergy"] = PropertyValue.FromFloat(15f),
                ["m_nKillComboClass"] = PropertyValue.FromInt(7),
                ["m_nKillComboCount"] = PropertyValue.FromInt(2),
                ["m_bKnifeExists"] = PropertyValue.FromInt(1),
                ["m_flKnifeRegenerateDuration"] = PropertyValue.FromFloat(15.5f),
                ["m_flKnifeMeltTimestamp"] = PropertyValue.FromFloat(290.75f),
                ["m_flMinicritCharge"] = PropertyValue.FromFloat(64.5f),
                ["m_bEnabled"] = PropertyValue.FromInt(1),
                ["DT_ParticleCannon.m_flChargeBeginTime"] = PropertyValue.FromFloat(301.5f),
                ["m_usNumCharges"] = PropertyValue.FromInt(3),
                ["m_iPrimaryAmmoType"] = PropertyValue.FromInt(4),
            }),
        ];

        byte[] body = decoder.EncodeEntities(entities, [], isDelta: false, 0, out int bits);

        return SyntheticDemo.From(
            SyntheticDemo.DefaultProtocol,
            SyntheticDemo.Packet(SyntheticDemo.DefaultProtocol, 0, ServerInfo()),
            SyntheticDemo.DataTables(schema),
            SyntheticDemo.Packet(
                SyntheticDemo.DefaultProtocol,
                100,
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

    /// <summary>Player 1 holding medigun 30 that heals player 2, and player 2 with no weapon.</summary>
    /// <param name="charge">The medigun's `DT_TFWeaponMedigunDataNonLocal.m_flChargeLevel`.</param>
    /// <returns>A demo's bytes.</returns>
    public static byte[] DemoWithMedigun(float charge)
    {
        const int MedigunClassId = 1;
        DemoSchema baseline = Schema(OriginTable.NonLocal);
        List<SendTable> tables = [];

        foreach (SendTable table in baseline.Tables)
        {
            tables.Add(
                table.Name == "DT_BasePlayer"
                    ? table with { Properties = [.. table.Properties, Table("bcc", "DT_BaseCombatCharacter")] }
                    : table);
        }

        tables.Add(new SendTable("DT_BaseCombatCharacter", NeedsDecoder: true, [UnsignedInt("m_hActiveWeapon", bits: 21)]));
        tables.Add(new SendTable("DT_TFWeaponMedigunDataNonLocal", NeedsDecoder: true, [NoScaleFloat("m_flChargeLevel")]));
        tables.Add(new SendTable("DT_WeaponMedigun", NeedsDecoder: true,
        [
            UnsignedInt("m_hHealingTarget", bits: 21),
            Table("NonLocalTFWeaponMedigunData", "DT_TFWeaponMedigunDataNonLocal"),
        ]));

        DemoSchema schema = new(tables, [.. baseline.ServerClasses, new ServerClass(MedigunClassId, "CWeaponMedigun", "DT_WeaponMedigun")]);
        EntityDecoder decoder = new(schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));

        List<DecodedEntity> entities =
        [
            Entity(decoder, PlayerClassId, 1, new Dictionary<string, PropertyValue>
            {
                ["m_vecOrigin"] = PropertyValue.FromVectorXY(0f, 0f),
                ["m_vecOrigin[2]"] = PropertyValue.FromFloat(0f),
                ["m_lifeState"] = PropertyValue.FromInt(0),
                ["DT_BaseCombatCharacter.m_hActiveWeapon"] = LoadoutHandle(30),
            }),
            Entity(decoder, PlayerClassId, 2, new Dictionary<string, PropertyValue>
            {
                ["m_vecOrigin"] = PropertyValue.FromVectorXY(64f, 0f),
                ["m_vecOrigin[2]"] = PropertyValue.FromFloat(0f),
                ["m_lifeState"] = PropertyValue.FromInt(0),
            }),
            Entity(decoder, MedigunClassId, 30, new Dictionary<string, PropertyValue>
            {
                ["DT_WeaponMedigun.m_hHealingTarget"] = LoadoutHandle(2),
                ["DT_TFWeaponMedigunDataNonLocal.m_flChargeLevel"] = PropertyValue.FromFloat(charge),
            }),
        ];

        byte[] body = decoder.EncodeEntities(entities, [], isDelta: false, 0, out int bits);

        return SyntheticDemo.From(
            SyntheticDemo.DefaultProtocol,
            SyntheticDemo.Packet(SyntheticDemo.DefaultProtocol, 0, ServerInfo()),
            SyntheticDemo.DataTables(schema),
            SyntheticDemo.Packet(
                SyntheticDemo.DefaultProtocol,
                100,
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

    /// <summary>A handle: the slot with a serial above it — `NUM_ENT_ENTRY_BITS` is 11 — so the decoder has to mask.</summary>
    /// <param name="entity">The slot.</param>
    /// <param name="serial">
    /// The serial the handle carries: by default the one <see cref="Entity"/> gives the occupant — its own index — so the
    /// handle dereferences as `RecvProxy_IntToEHandle` keeps it (client/recvproxy.cpp:80); any other names a slot that has
    /// changed hands, which resolves to nothing (B231).
    /// </param>
    private static PropertyValue LoadoutHandle(int entity, int? serial = null) =>
        PropertyValue.FromInt(entity | ((serial ?? entity) << 11));

    /// <summary>The recorder with `m_Shared.m_nPlayerState` (tf_player_shared.cpp:543) and `m_bIsMiniBoss` (c_tf_player.cpp:3779).</summary>
    /// <param name="playerState">`TF_STATE_*`.</param>
    /// <param name="miniBoss">`m_bIsMiniBoss`.</param>
    /// <returns>A demo's bytes.</returns>
    public static byte[] DemoWithPlayerState(int playerState, bool miniBoss)
    {
        DemoSchema baseline = Schema(OriginTable.NonLocal);
        List<SendTable> tables = [];

        foreach (SendTable table in baseline.Tables)
        {
            tables.Add(
                table.Name == "DT_TFPlayer"
                    ? table with
                    {
                        Properties =
                        [
                            .. table.Properties,
                            Table("playershared", "DT_TFPlayerShared"),
                            UnsignedInt("m_bIsMiniBoss", bits: 1),
                            Table("TFSendHealersDataTable", "DT_TFSendHealersDataTable"),
                        ],
                    }
                    : table);
        }

        tables.Add(new SendTable("DT_TFPlayerShared", NeedsDecoder: true, [UnsignedInt("m_nPlayerState", bits: 3)]));
        tables.Add(new SendTable("DT_TFSendHealersDataTable", NeedsDecoder: true, [UnsignedInt("m_nActiveWpnClip", bits: 8)]));

        DemoSchema schema = new(tables, baseline.ServerClasses);
        EntityDecoder decoder = new(schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));

        List<DecodedEntity> entities =
        [
            Entity(decoder, PlayerClassId, 1, new Dictionary<string, PropertyValue>
            {
                ["m_vecOrigin"] = PropertyValue.FromVectorXY(0f, 0f),
                ["m_vecOrigin[2]"] = PropertyValue.FromFloat(0f),
                ["m_lifeState"] = PropertyValue.FromInt(0),
                ["m_nPlayerState"] = PropertyValue.FromInt(playerState),
                ["m_bIsMiniBoss"] = PropertyValue.FromInt(miniBoss ? 1 : 0),
                ["m_nActiveWpnClip"] = PropertyValue.FromInt(6),
            }),
        ];

        byte[] body = decoder.EncodeEntities(entities, [], isDelta: false, 0, out int bits);

        return SyntheticDemo.From(
            SyntheticDemo.DefaultProtocol,
            SyntheticDemo.Packet(SyntheticDemo.DefaultProtocol, 0, ServerInfo()),
            SyntheticDemo.DataTables(schema),
            SyntheticDemo.Packet(
                SyntheticDemo.DefaultProtocol,
                100,
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

    /// <summary>The recorder (slot 0, entity 1) with what the HUD reads: its own health, its `m_iHideHUD`, the resource's maxima.</summary>
    /// <param name="health">`DT_BasePlayer.m_iHealth`.</param>
    /// <param name="hideHud">`DT_Local.m_iHideHUD`.</param>
    /// <param name="maxHealth">The resource's `m_iMaxHealth` for the recorder.</param>
    /// <param name="maxBuffedHealth">The resource's `m_iMaxBuffedHealth`, which is the buffing base.</param>
    /// <param name="residentHealth">The resource's own `m_iHealth`, which the scoreboard reads and the HUD does not.</param>
    /// <returns>A demo's bytes.</returns>
    public static byte[] DemoWithHudHealth(int health, int hideHud, int maxHealth, int maxBuffedHealth, int residentHealth)
    {
        DemoSchema baseline = Schema(OriginTable.NonLocal);
        List<SendTable> tables = [];

        foreach (SendTable table in baseline.Tables)
        {
            tables.Add(
                table.Name == "DT_BasePlayer"
                    ? table with { Properties = [.. table.Properties, Int("m_iHealth", bits: 10), Table("localdata", "DT_Local")] }
                    : table);
        }

        tables.Add(new SendTable("DT_Local", NeedsDecoder: true, [Int("m_iHideHUD", bits: 14)]));
        tables.Add(ArrayTable("m_iHealth", bits: 10));
        tables.Add(ArrayTable("m_iMaxHealth", bits: 10));
        tables.Add(ArrayTable("m_iMaxBuffedHealth", bits: 10));
        tables.Add(new SendTable("DT_TFPlayerResource", NeedsDecoder: true,
        [
            Table("m_iHealth", "m_iHealth"),
            Table("m_iMaxHealth", "m_iMaxHealth"),
            Table("m_iMaxBuffedHealth", "m_iMaxBuffedHealth"),
            Int("m_iPartyLeaderRedTeamIndex", bits: 8), Int("m_iPartyLeaderBlueTeamIndex", bits: 8), Int("m_iEventTeamStatus", bits: 3),
        ]));

        DemoSchema schema = new(
            tables, [.. baseline.ServerClasses, new ServerClass(ResourceClassId, "CTFPlayerResource", "DT_TFPlayerResource")]);
        EntityDecoder decoder = new(schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));
        const string Slot = "001";

        List<DecodedEntity> entities =
        [
            Entity(decoder, PlayerClassId, 1, new Dictionary<string, PropertyValue>
            {
                ["m_vecOrigin"] = PropertyValue.FromVectorXY(0f, 0f),
                ["m_vecOrigin[2]"] = PropertyValue.FromFloat(0f),
                ["m_lifeState"] = PropertyValue.FromInt(0),
                ["m_iHealth"] = PropertyValue.FromInt(health),
                ["m_iHideHUD"] = PropertyValue.FromInt(hideHud),
            }),
            Entity(decoder, ResourceClassId, ResourceEntityIndex, new Dictionary<string, PropertyValue>
            {
                [$"m_iHealth.{Slot}"] = PropertyValue.FromInt(residentHealth),
                [$"m_iMaxHealth.{Slot}"] = PropertyValue.FromInt(maxHealth),
                [$"m_iMaxBuffedHealth.{Slot}"] = PropertyValue.FromInt(maxBuffedHealth),
                ["m_iPartyLeaderRedTeamIndex"] = PropertyValue.FromInt(3),
                ["m_iPartyLeaderBlueTeamIndex"] = PropertyValue.FromInt(7),
                ["m_iEventTeamStatus"] = PropertyValue.FromInt(2),
            }),
        ];

        byte[] body = decoder.EncodeEntities(entities, [], isDelta: false, 0, out int bits);

        return SyntheticDemo.From(
            SyntheticDemo.DefaultProtocol,
            SyntheticDemo.Packet(SyntheticDemo.DefaultProtocol, 0, ServerInfo()),
            SyntheticDemo.DataTables(schema),
            SyntheticDemo.Packet(
                SyntheticDemo.DefaultProtocol,
                100,
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

    /// <summary>The recorder beside a `CTFGameRulesProxy` holding the two rules the HUD tests, and optionally a player-destruction logic.</summary>
    /// <param name="mannVsMachine">`m_bPlayingMannVsMachine`.</param>
    /// <param name="halloweenScenario">`m_halloweenScenario`.</param>
    /// <param name="playerDestruction">Whether a `CTFPlayerDestructionLogic` exists.</param>
    /// <returns>A demo's bytes.</returns>
    public static byte[] DemoWithGameRules(bool mannVsMachine, int halloweenScenario, bool playerDestruction)
    {
        const int RulesClassId = 1;
        const int LogicClassId = 2;
        DemoSchema baseline = Schema(OriginTable.NonLocal);
        List<SendTable> tables =
        [
            .. baseline.Tables,
            new SendTable("DT_TFGameRules", NeedsDecoder: true, [UnsignedInt("m_bPlayingMannVsMachine", bits: 1), Int("m_halloweenScenario", bits: 4)]),
            new SendTable("DT_TFGameRulesProxy", NeedsDecoder: true, [Table("tf_gamerules_data", "DT_TFGameRules")]),
            new SendTable("DT_TFPlayerDestructionLogic", NeedsDecoder: true, [Int("m_nMaxPoints", bits: 8)]),
        ];

        DemoSchema schema = new(
            tables,
            [
                .. baseline.ServerClasses,
                new ServerClass(RulesClassId, "CTFGameRulesProxy", "DT_TFGameRulesProxy"),
                new ServerClass(LogicClassId, "CTFPlayerDestructionLogic", "DT_TFPlayerDestructionLogic"),
            ]);
        EntityDecoder decoder = new(schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));

        List<DecodedEntity> entities =
        [
            Entity(decoder, PlayerClassId, 1, new Dictionary<string, PropertyValue>
            {
                ["m_vecOrigin"] = PropertyValue.FromVectorXY(0f, 0f),
                ["m_vecOrigin[2]"] = PropertyValue.FromFloat(0f),
                ["m_lifeState"] = PropertyValue.FromInt(0),
            }),
            Entity(decoder, RulesClassId, 40, new Dictionary<string, PropertyValue>
            {
                ["m_bPlayingMannVsMachine"] = PropertyValue.FromInt(mannVsMachine ? 1 : 0),
                ["m_halloweenScenario"] = PropertyValue.FromInt(halloweenScenario),
            }),
        ];

        if (playerDestruction)
        {
            entities.Add(Entity(decoder, LogicClassId, 41, new Dictionary<string, PropertyValue> { ["m_nMaxPoints"] = PropertyValue.FromInt(5) }));
        }

        byte[] body = decoder.EncodeEntities(entities, [], isDelta: false, 0, out int bits);

        return SyntheticDemo.From(
            SyntheticDemo.DefaultProtocol,
            SyntheticDemo.Packet(SyntheticDemo.DefaultProtocol, 0, ServerInfo()),
            SyntheticDemo.DataTables(schema),
            SyntheticDemo.Packet(
                SyntheticDemo.DefaultProtocol,
                100,
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
    /// The recorder beside the rules the time panel reads, all set, a `CTFObjectiveResource` naming timer 42 for the HUD,
    /// and that `CTeamRoundTimer`.
    /// </summary>
    /// <param name="paused">`m_bTimerPaused`.</param>
    /// <returns>A demo's bytes.</returns>
    public static byte[] DemoWithRoundTimer(bool paused)
    {
        const int RulesClassId = 1;
        const int ObjectiveClassId = 2;
        const int TimerClassId = 3;
        DemoSchema baseline = Schema(OriginTable.NonLocal);
        List<SendTable> tables =
        [
            .. baseline.Tables,
            new SendTable(
                "DT_TeamplayRoundBasedRules",
                NeedsDecoder: true,
                [
                    UnsignedInt("m_bInWaitingForPlayers", bits: 1), UnsignedInt("m_bInOvertime", bits: 1),
                    UnsignedInt("m_bInSetup", bits: 1), UnsignedInt("m_bStopWatch", bits: 1),
                ]),
            new SendTable(
                "DT_TFGameRules",
                NeedsDecoder: true,
                [
                    UnsignedInt("m_nGameType", bits: 4), UnsignedInt("m_bPlayingKoth", bits: 1), UnsignedInt("m_bShowMatchSummary", bits: 1),
                    UnsignedInt("m_hRedKothTimer", bits: 21), UnsignedInt("m_hBlueKothTimer", bits: 21),
                    UnsignedInt("m_bMapHasMatchSummaryStage", bits: 1),
                ]),
            new SendTable(
                "DT_TFGameRulesProxy",
                NeedsDecoder: true,
                [Table("teamplayroundbased_gamerules_data", "DT_TeamplayRoundBasedRules"), Table("tf_gamerules_data", "DT_TFGameRules")]),
            new SendTable("DT_BaseTeamObjectiveResource", NeedsDecoder: true, [UnsignedInt("m_iTimerToShowInHUD", bits: 11)]),
            new SendTable("DT_TFObjectiveResource", NeedsDecoder: true, [Table("baseclass", "DT_BaseTeamObjectiveResource")]),
            new SendTable(
                "DT_TeamRoundTimer",
                NeedsDecoder: true,
                [
                    UnsignedInt("m_bTimerPaused", bits: 1), NoScaleFloat("m_flTimeRemaining"), NoScaleFloat("m_flTimerEndTime"),
                    Int("m_nTimerMaxLength", bits: 32), UnsignedInt("m_bIsDisabled", bits: 1), UnsignedInt("m_bShowInHUD", bits: 1),
                    Int("m_nTimerLength", bits: 32), Int("m_nSetupTimeLength", bits: 32), Int("m_nState", bits: 32),
                    UnsignedInt("m_bShowTimeRemaining", bits: 1), UnsignedInt("m_bInCaptureWatchState", bits: 1),
                    UnsignedInt("m_bStopWatchTimer", bits: 1), NoScaleFloat("m_flTotalTime"),
                ]),
        ];

        DemoSchema schema = new(
            tables,
            [
                .. baseline.ServerClasses,
                new ServerClass(RulesClassId, "CTFGameRulesProxy", "DT_TFGameRulesProxy"),
                new ServerClass(ObjectiveClassId, "CTFObjectiveResource", "DT_TFObjectiveResource"),
                new ServerClass(TimerClassId, "CTeamRoundTimer", "DT_TeamRoundTimer"),
            ]);
        EntityDecoder decoder = new(schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));

        List<DecodedEntity> entities =
        [
            Entity(decoder, PlayerClassId, 1, new Dictionary<string, PropertyValue>
            {
                ["m_vecOrigin"] = PropertyValue.FromVectorXY(0f, 0f),
                ["m_vecOrigin[2]"] = PropertyValue.FromFloat(0f),
                ["m_lifeState"] = PropertyValue.FromInt(0),
            }),
            Entity(decoder, RulesClassId, 40, new Dictionary<string, PropertyValue>
            {
                ["m_bInWaitingForPlayers"] = PropertyValue.FromInt(1),
                ["m_bInOvertime"] = PropertyValue.FromInt(1),
                ["m_bInSetup"] = PropertyValue.FromInt(1),
                ["m_bStopWatch"] = PropertyValue.FromInt(1),
                ["m_nGameType"] = PropertyValue.FromInt(4),
                ["m_bPlayingKoth"] = PropertyValue.FromInt(1),
                ["m_bShowMatchSummary"] = PropertyValue.FromInt(1),

                // A handle is the slot in the low 11 bits and a serial above; all 21 bits set is none.
                ["m_hRedKothTimer"] = PropertyValue.FromInt((1 << 21) - 1),
                ["m_hBlueKothTimer"] = PropertyValue.FromInt(42 | (5 << 11)),
                ["m_bMapHasMatchSummaryStage"] = PropertyValue.FromInt(1),
            }),
            Entity(decoder, TimerClassId, 42, new Dictionary<string, PropertyValue>
            {
                ["m_bTimerPaused"] = PropertyValue.FromInt(paused ? 1 : 0),
                ["m_flTimeRemaining"] = PropertyValue.FromFloat(95.5f),
                ["m_flTimerEndTime"] = PropertyValue.FromFloat(300.25f),
                ["m_nTimerMaxLength"] = PropertyValue.FromInt(600),
                ["m_bIsDisabled"] = PropertyValue.FromInt(0),
                ["m_bShowInHUD"] = PropertyValue.FromInt(1),
                ["m_nTimerLength"] = PropertyValue.FromInt(240),
                ["m_nSetupTimeLength"] = PropertyValue.FromInt(60),
                ["m_nState"] = PropertyValue.FromInt(1),
                ["m_bShowTimeRemaining"] = PropertyValue.FromInt(1),
                ["m_bInCaptureWatchState"] = PropertyValue.FromInt(0),
                ["m_bStopWatchTimer"] = PropertyValue.FromInt(0),
                ["m_flTotalTime"] = PropertyValue.FromFloat(12.5f),
            }),
            Entity(decoder, ObjectiveClassId, 43, new Dictionary<string, PropertyValue> { ["m_iTimerToShowInHUD"] = PropertyValue.FromInt(42) }),
        ];

        byte[] body = decoder.EncodeEntities(entities, [], isDelta: false, 0, out int bits);

        return SyntheticDemo.From(
            SyntheticDemo.DefaultProtocol,
            SyntheticDemo.Packet(SyntheticDemo.DefaultProtocol, 0, ServerInfo()),
            SyntheticDemo.DataTables(schema),
            SyntheticDemo.Packet(
                SyntheticDemo.DefaultProtocol,
                100,
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

    /// <summary>A demo carrying one player (the builder) and one `CObjectSentrygun`, every field of the object set to a distinctive value.</summary>
    public static byte[] DemoWithBuilding()
    {
        const int BuilderClassId = PlayerClassId;
        const int SentrygunClassId = 1;
        DemoSchema baseline = Schema(OriginTable.NonLocal);
        List<SendTable> tables =
        [
            .. baseline.Tables,
            new SendTable(
                "DT_BaseObject",
                NeedsDecoder: true,
                [
                    Int("m_iHealth", bits: 16), Int("m_iMaxHealth", bits: 16), UnsignedInt("m_bHasSapper", bits: 1),
                    UnsignedInt("m_iObjectType", bits: 8), UnsignedInt("m_bBuilding", bits: 1), UnsignedInt("m_bPlacing", bits: 1),
                    UnsignedInt("m_bCarried", bits: 1), UnsignedInt("m_bMiniBuilding", bits: 1), UnsignedInt("m_bDisabled", bits: 1),
                    UnsignedInt("m_hBuilder", bits: 21), UnsignedInt("m_iUpgradeLevel", bits: 3), UnsignedInt("m_iUpgradeMetal", bits: 10),
                    UnsignedInt("m_iUpgradeMetalRequired", bits: 10), UnsignedInt("m_iObjectMode", bits: 2),
                    UnsignedInt("m_bDisposableBuilding", bits: 1), NoScaleFloat("m_flPercentageConstructed"),
                    VectorXy("m_vecOrigin", bits: 32), Float("m_vecOrigin[2]", low: -16384f, high: 16384f, bits: 32),
                    Table("baseclass", "DT_BaseEntity"), Table("m_Collision", "DT_CollisionProperty"),
                ]),
            new SendTable("DT_CollisionProperty", NeedsDecoder: true,
            [
                NoScaleVector("m_vecMins"), NoScaleVector("m_vecMaxs"), UnsignedInt("m_nSolidType", bits: 3), UnsignedInt("m_usSolidFlags", bits: 10),
            ]),
            new SendTable(
                "DT_ObjectSentrygun",
                NeedsDecoder: true,
                [
                    Int("m_iAmmoShells", bits: 16), Int("m_iAmmoRockets", bits: 16), Table("baseclass", "DT_BaseObject"),
                ]),
        ];

        DemoSchema schema = new(
            tables,
            [
                .. baseline.ServerClasses,
                new ServerClass(SentrygunClassId, "CObjectSentrygun", "DT_ObjectSentrygun"),
            ]);
        EntityDecoder decoder = new(schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));

        List<DecodedEntity> entities =
        [
            Entity(decoder, BuilderClassId, 1, new Dictionary<string, PropertyValue>
            {
                ["m_vecOrigin"] = PropertyValue.FromVectorXY(0f, 0f),
                ["m_vecOrigin[2]"] = PropertyValue.FromFloat(0f),
                ["m_lifeState"] = PropertyValue.FromInt(0),
            }),
            Entity(decoder, SentrygunClassId, 55, new Dictionary<string, PropertyValue>
            {
                ["m_iHealth"] = PropertyValue.FromInt(111),
                ["m_iMaxHealth"] = PropertyValue.FromInt(150),
                ["m_bHasSapper"] = PropertyValue.FromInt(1),
                ["m_iObjectType"] = PropertyValue.FromInt(2), // OBJ_SENTRYGUN
                ["m_bBuilding"] = PropertyValue.FromInt(1),
                ["m_bPlacing"] = PropertyValue.FromInt(0),
                ["m_bCarried"] = PropertyValue.FromInt(1),
                ["m_bMiniBuilding"] = PropertyValue.FromInt(0),
                ["m_bDisabled"] = PropertyValue.FromInt(1),

                // A handle is the slot in the low 11 bits and a serial above; entity 1's serial is 0.
                ["m_hBuilder"] = PropertyValue.FromInt(1),
                ["m_iUpgradeLevel"] = PropertyValue.FromInt(3),
                ["m_iUpgradeMetal"] = PropertyValue.FromInt(197),
                ["m_iUpgradeMetalRequired"] = PropertyValue.FromInt(200),
                ["m_iObjectMode"] = PropertyValue.FromInt(1),
                ["m_bDisposableBuilding"] = PropertyValue.FromInt(1),
                ["m_flPercentageConstructed"] = PropertyValue.FromFloat(0.75f),
                ["m_vecOrigin"] = PropertyValue.FromVectorXY(128.5f, -64.25f),
                ["m_vecOrigin[2]"] = PropertyValue.FromFloat(32.75f),
                ["m_iAmmoShells"] = PropertyValue.FromInt(140),
                ["m_iAmmoRockets"] = PropertyValue.FromInt(6),
                ["m_vecMins"] = PropertyValue.FromVector(-20f, -20f, 0f),
                ["m_vecMaxs"] = PropertyValue.FromVector(20f, 20f, 66f),
                ["m_nSolidType"] = PropertyValue.FromInt(2), // SOLID_BBOX
                ["m_usSolidFlags"] = PropertyValue.FromInt(4), // FSOLID_NOT_SOLID
            }),
        ];

        byte[] body = decoder.EncodeEntities(entities, [], isDelta: false, 0, out int bits);

        return SyntheticDemo.From(
            SyntheticDemo.DefaultProtocol,
            SyntheticDemo.Packet(SyntheticDemo.DefaultProtocol, 0, ServerInfo()),
            SyntheticDemo.DataTables(schema),
            SyntheticDemo.Packet(
                SyntheticDemo.DefaultProtocol,
                100,
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
    /// A player, a `CTFDroppedWeapon` (entity 60), a `CTFReviveMarker` (61), and game rules carrying the respawn-wave arrays
    /// and a robot destruction logic — every field the target ID's generic branch reads, set to a distinctive value.
    /// </summary>
    /// <returns>A demo's bytes.</returns>
    /// <remarks>The dropped weapon's model, index 2 in the model precache.</remarks>
    public static byte[] DemoWithIdEntities()
    {
        const int WeaponClassId = 1;
        const int MarkerClassId = 2;
        const int RulesClassId = 3;
        const int RobotLogicClassId = 4;
        DemoSchema baseline = Schema(OriginTable.NonLocal);
        List<SendTable> tables =
        [
            .. baseline.Tables,
            new SendTable("DT_CollisionProperty", NeedsDecoder: true,
            [
                NoScaleVector("m_vecMins"), NoScaleVector("m_vecMaxs"), UnsignedInt("m_nSolidType", bits: 3), UnsignedInt("m_usSolidFlags", bits: 10),
            ]),
            new SendTable("DT_ScriptCreatedItem", NeedsDecoder: true,
            [
                UnsignedInt("m_iItemDefinitionIndex", bits: 16), UnsignedInt("m_iEntityQuality", bits: 8), UnsignedInt("m_iAccountID", bits: 32),
                UnsignedInt("m_bInitialized", bits: 1),
            ]),
            new SendTable("DT_TFDroppedWeapon", NeedsDecoder: true,
            [
                Table("m_Item", "DT_ScriptCreatedItem"), NoScaleFloat("m_flChargeLevel"),
                VectorXy("m_vecOrigin", bits: 32), Float("m_vecOrigin[2]", low: -16384f, high: 16384f, bits: 32), NoScaleVector("m_angRotation"),
                Table("baseclass", "DT_BaseEntity"), Table("m_Collision", "DT_CollisionProperty"),
            ]),
            new SendTable("DT_TFReviveMarker", NeedsDecoder: true,
            [
                UnsignedInt("m_hOwner", bits: 21), Int("m_iHealth", bits: 16), Int("m_iMaxHealth", bits: 16),
                VectorXy("m_vecOrigin", bits: 32), Float("m_vecOrigin[2]", low: -16384f, high: 16384f, bits: 32),
                Table("baseclass", "DT_BaseEntity"), Table("m_Collision", "DT_CollisionProperty"),
            ]),
            new SendTable("m_flNextRespawnWave", NeedsDecoder: false, [NoScaleFloat("000"), NoScaleFloat("001"), NoScaleFloat("002"), NoScaleFloat("003")]),
            new SendTable("m_TeamRespawnWaveTimes", NeedsDecoder: false, [NoScaleFloat("000"), NoScaleFloat("001"), NoScaleFloat("002"), NoScaleFloat("003")]),
            new SendTable("DT_TeamplayRoundBasedRules", NeedsDecoder: true,
            [
                Table("m_flNextRespawnWave", "m_flNextRespawnWave"), Table("m_TeamRespawnWaveTimes", "m_TeamRespawnWaveTimes"),
            ]),
            new SendTable("DT_TFGameRulesProxy", NeedsDecoder: true, [Table("teamplayroundbased_gamerules_data", "DT_TeamplayRoundBasedRules")]),
            new SendTable("DT_TFRobotDestructionLogic", NeedsDecoder: true, [NoScaleFloat("m_flBlueTeamRespawnScale"), NoScaleFloat("m_flRedTeamRespawnScale")]),
        ];

        DemoSchema schema = new(
            tables,
            [
                .. baseline.ServerClasses,
                new ServerClass(WeaponClassId, "CTFDroppedWeapon", "DT_TFDroppedWeapon"),
                new ServerClass(MarkerClassId, "CTFReviveMarker", "DT_TFReviveMarker"),
                new ServerClass(RulesClassId, "CTFGameRulesProxy", "DT_TFGameRulesProxy"),
                new ServerClass(RobotLogicClassId, "CTFRobotDestructionLogic", "DT_TFRobotDestructionLogic"),
            ]);
        EntityDecoder decoder = new(schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));

        List<DecodedEntity> entities =
        [
            Entity(decoder, PlayerClassId, 1, new Dictionary<string, PropertyValue>
            {
                ["m_vecOrigin"] = PropertyValue.FromVectorXY(0f, 0f),
                ["m_vecOrigin[2]"] = PropertyValue.FromFloat(0f),
                ["m_lifeState"] = PropertyValue.FromInt(0),
            }),
            Entity(decoder, WeaponClassId, 60, new Dictionary<string, PropertyValue>
            {
                ["m_nModelIndex"] = PropertyValue.FromInt(2),
                ["m_iItemDefinitionIndex"] = PropertyValue.FromInt(211),
                ["m_iEntityQuality"] = PropertyValue.FromInt(11),
                ["m_iAccountID"] = PropertyValue.FromInt(123456789),
                ["m_bInitialized"] = PropertyValue.FromInt(1),
                ["m_flChargeLevel"] = PropertyValue.FromFloat(0.625f),
                ["m_vecOrigin"] = PropertyValue.FromVectorXY(10.5f, -20.25f),
                ["m_vecOrigin[2]"] = PropertyValue.FromFloat(4.75f),
                ["m_angRotation"] = PropertyValue.FromVector(0f, 90f, 0f),
                ["m_vecMins"] = PropertyValue.FromVector(-30f, -4f, -2f),
                ["m_vecMaxs"] = PropertyValue.FromVector(30f, 4f, 6f),
                ["m_nSolidType"] = PropertyValue.FromInt(6), // SOLID_VPHYSICS
                ["m_usSolidFlags"] = PropertyValue.FromInt(0x200), // FSOLID_NOT_STANDABLE
            }),
            Entity(decoder, MarkerClassId, 61, new Dictionary<string, PropertyValue>
            {
                ["m_hOwner"] = PropertyValue.FromInt(1),
                ["m_iHealth"] = PropertyValue.FromInt(37),
                ["m_iMaxHealth"] = PropertyValue.FromInt(85),
                ["m_vecOrigin"] = PropertyValue.FromVectorXY(-100f, 50f),
                ["m_vecOrigin[2]"] = PropertyValue.FromFloat(8f),
                ["m_vecMins"] = PropertyValue.FromVector(-12f, -12f, 0f),
                ["m_vecMaxs"] = PropertyValue.FromVector(12f, 12f, 48f),
                ["m_nSolidType"] = PropertyValue.FromInt(2), // SOLID_BBOX
                ["m_usSolidFlags"] = PropertyValue.FromInt(8), // FSOLID_TRIGGER
            }),
            Entity(decoder, RulesClassId, 40, new Dictionary<string, PropertyValue>
            {
                ["m_flNextRespawnWave.002"] = PropertyValue.FromFloat(120.5f),
                ["m_flNextRespawnWave.003"] = PropertyValue.FromFloat(131.25f),
                ["m_TeamRespawnWaveTimes.002"] = PropertyValue.FromFloat(6f),
                ["m_TeamRespawnWaveTimes.003"] = PropertyValue.FromFloat(-1f),
            }),
            Entity(decoder, RobotLogicClassId, 41, new Dictionary<string, PropertyValue>
            {
                ["m_flRedTeamRespawnScale"] = PropertyValue.FromFloat(0.25f),
                ["m_flBlueTeamRespawnScale"] = PropertyValue.FromFloat(0.5f),
            }),
        ];

        byte[] body = decoder.EncodeEntities(entities, [], isDelta: false, 0, out int bits);

        return SyntheticDemo.From(
            SyntheticDemo.DefaultProtocol,
            SyntheticDemo.Packet(
                SyntheticDemo.DefaultProtocol,
                0,
                ServerInfo(),
                SyntheticDemo.StringTable(ModelPrecache.TableName, [string.Empty, "models/a.mdl", DroppedWeaponModel], maxEntries: 8)),
            SyntheticDemo.DataTables(schema),
            SyntheticDemo.Packet(
                SyntheticDemo.DefaultProtocol,
                100,
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

    /// <summary>The model <see cref="DemoWithIdEntities"/>'s dropped weapon names.</summary>
    public const string DroppedWeaponModel = "models/weapons/w_models/w_shotgun.mdl";

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
        params (int EntityIndex, IReadOnlyDictionary<string, PropertyValue> Values)[] players) =>
        Demo(origin, tick, serverTick: null, players);

    /// <summary>A demo whose one packet at tick 10 carries these messages, after the usual signon.</summary>
    /// <param name="messages">The messages.</param>
    /// <returns>A demo's bytes.</returns>
    public static byte[] DemoWithMessages(params INetMessage[] messages) =>
        SyntheticDemo.From(
            SyntheticDemo.DefaultProtocol,
            SyntheticDemo.Packet(SyntheticDemo.DefaultProtocol, 0, ServerInfo()),
            SyntheticDemo.DataTables(Schema()),
            SyntheticDemo.Packet(SyntheticDemo.DefaultProtocol, 10, messages));

    /// <summary>A demo whose snapshot follows a `net_Tick` naming the server's own tick.</summary>
    /// <param name="tick">The demo's tick for the snapshot.</param>
    /// <param name="serverTick">`gpGlobals->tickcount` on the recording server.</param>
    /// <returns>A demo's bytes.</returns>
    public static byte[] DemoAtServerTick(int tick, int serverTick) =>
        Demo(OriginTable.NonLocal, tick, serverTick, (1, new Dictionary<string, PropertyValue>()));

    private static byte[] Demo(
        OriginTable origin,
        int tick,
        int? serverTick,
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
        PacketEntitiesMessage snapshot = new(
            MaxEntries: 64,
            IsDelta: false,
            DeltaFromTick: null,
            BaselineIndex: false,
            UpdatedEntries: entities.Count,
            LengthBits: bits,
            UpdateBaseline: false,
            Body: body);

        return SyntheticDemo.From(
            SyntheticDemo.DefaultProtocol,
            SyntheticDemo.Packet(SyntheticDemo.DefaultProtocol, 0, ServerInfo()),
            SyntheticDemo.DataTables(schema),
            serverTick is { } server
                ? SyntheticDemo.Packet(SyntheticDemo.DefaultProtocol, tick, new NetTickMessage(server, 0, 0), snapshot)
                : SyntheticDemo.Packet(SyntheticDemo.DefaultProtocol, tick, snapshot));
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
    /// A schema whose <c>CTFPlayerResource</c> also carries the scoreboard's own fields —
    /// connected, valid, alive, score, total score, deaths, ping and active dominations — on top
    /// of the team and class <see cref="SchemaWithResource"/> already declares.
    /// </summary>
    /// <remarks>
    /// A rebuild of <see cref="SchemaWithResource"/>'s own tables rather than an edit to it, the
    /// same reasoning as <see cref="SchemaWithConditions"/>: appending to a table every other
    /// fixture shares would shift flattened indices under tests that never asked for these fields.
    /// </remarks>
    public static DemoSchema SchemaWithScoreboard()
    {
        DemoSchema baseline = Schema(OriginTable.NonLocal);

        return new DemoSchema(
            [
                .. baseline.Tables,

                // Every array's own sub-table declared BEFORE the table that references it,
                // matching SchemaWithResource's own order exactly.
                ArrayTable("m_iTeam"),
                ArrayTable("m_iPlayerClass"),
                // bits: 2, not 1 — ArrayTable's elements are SIGNED (`Int`), and a 1-bit signed
                // field can only hold -1 and 0; writing 1 into it corrupts the whole entity's
                // decode, not just this property.
                ArrayTable("m_bConnected", bits: 2),
                ArrayTable("m_bValid", bits: 2),
                ArrayTable("m_bAlive", bits: 2),
                ArrayTable("m_iScore", bits: 10),
                ArrayTable("m_iTotalScore", bits: 10),
                ArrayTable("m_iDeaths", bits: 10),
                ArrayTable("m_iPing", bits: 10),
                ArrayTable("m_iActiveDominations", bits: 5),
                new SendTable("DT_TFPlayerResource", NeedsDecoder: true,
                [
                    Table("m_iTeam", "m_iTeam"),
                    Table("m_iPlayerClass", "m_iPlayerClass"),
                    Table("m_bConnected", "m_bConnected"),
                    Table("m_bValid", "m_bValid"),
                    Table("m_bAlive", "m_bAlive"),
                    Table("m_iScore", "m_iScore"),
                    Table("m_iTotalScore", "m_iTotalScore"),
                    Table("m_iDeaths", "m_iDeaths"),
                    Table("m_iPing", "m_iPing"),
                    Table("m_iActiveDominations", "m_iActiveDominations"),
                ]),
            ],
            [
                .. baseline.ServerClasses,
                new ServerClass(ResourceClassId, "CTFPlayerResource", "DT_TFPlayerResource"),
            ]);
    }

    /// <summary>
    /// A demo carrying only a <c>CTFPlayerResource</c>, stating every field
    /// <c>CTFClientScoreBoardDialog::UpdatePlayerList</c> reads for each slot.
    /// </summary>
    /// <param name="players">Each connected or valid slot's resource fields.</param>
    /// <returns>A demo's bytes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="players"/> is null.</exception>
    /// <remarks>
    /// **A camera entity carries the only position, at an index no scoreboard slot uses.** The
    /// scoreboard's own loop reads the resource for every slot that is connected or valid, whether
    /// or not that slot has a positioned <c>CTFPlayer</c> this tick — this fixture's resource slots
    /// carry no position at all, the same as a spectator or a mid-connect player. A frame is only
    /// recorded when the packet moved something, so one positioned entity is what makes the frame
    /// exist for <see cref="Core.Scene.DemoTimeline.ScoreboardPlayersAt"/> to find, exactly as
    /// <see cref="DemoWithBuilding"/> pairs its building with a player entity for the same reason.
    /// </remarks>
    public static byte[] DemoWithScoreboard(
        params (int EntityIndex, bool Connected, bool Valid, int Team, bool Alive, int Score, int TotalScore, int Deaths, int Ping, int PlayerClass, int ActiveDominations)[] players)
    {
        ArgumentNullException.ThrowIfNull(players);

        const int CameraEntityIndex = 90;

        DemoSchema schema = SchemaWithScoreboard();
        EntityDecoder decoder = new(
            schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));

        Dictionary<string, PropertyValue> arrays = [];

        foreach ((int entityIndex, bool connected, bool valid, int team, bool alive, int score, int totalScore, int deaths, int ping, int playerClass, int activeDominations) in players)
        {
            string slot = entityIndex.ToString("D3", CultureInfo.InvariantCulture);
            arrays[$"m_bConnected.{slot}"] = PropertyValue.FromInt(connected ? 1 : 0);
            arrays[$"m_bValid.{slot}"] = PropertyValue.FromInt(valid ? 1 : 0);
            arrays[$"m_iTeam.{slot}"] = PropertyValue.FromInt(team);
            arrays[$"m_bAlive.{slot}"] = PropertyValue.FromInt(alive ? 1 : 0);
            arrays[$"m_iScore.{slot}"] = PropertyValue.FromInt(score);
            arrays[$"m_iTotalScore.{slot}"] = PropertyValue.FromInt(totalScore);
            arrays[$"m_iDeaths.{slot}"] = PropertyValue.FromInt(deaths);
            arrays[$"m_iPing.{slot}"] = PropertyValue.FromInt(ping);
            arrays[$"m_iPlayerClass.{slot}"] = PropertyValue.FromInt(playerClass);
            arrays[$"m_iActiveDominations.{slot}"] = PropertyValue.FromInt(activeDominations);
        }

        // Ascending entity index, because entities are delta-coded and the encoder writes
        // ascending gaps (the same reasoning DemoWithResource's own comment gives).
        List<DecodedEntity> entities =
        [
            Entity(decoder, ResourceClassId, ResourceEntityIndex, arrays),
            Entity(decoder, PlayerClassId, CameraEntityIndex, new Dictionary<string, PropertyValue>
            {
                ["m_vecOrigin"] = PropertyValue.FromVectorXY(0f, 0f),
                ["m_vecOrigin[2]"] = PropertyValue.FromFloat(0f),
                ["m_lifeState"] = PropertyValue.FromInt(0),
            }),
        ];
        byte[] body = decoder.EncodeEntities(entities, [], isDelta: false, 0, out int bits);

        return SyntheticDemo.From(
            SyntheticDemo.DefaultProtocol,
            SyntheticDemo.Packet(SyntheticDemo.DefaultProtocol, 0, ServerInfo()),
            SyntheticDemo.DataTables(schema),
            SyntheticDemo.Packet(
                SyntheticDemo.DefaultProtocol,
                100,
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
    /// <c>docs/memory/fixtures-are-the-weak-point.md#real-data-hides-bugs-small-inputs-expose</c> — the condition has to be one
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

    // `SendPropTime` is `SPROP_NOSCALE` (1 << 2): the float's 32 bits as they are.
    private static SendProperty NoScaleFloat(string name) =>
        new(SendPropType.Float, name, 1 << 2, string.Empty, 0f, 0f, 32, 0);

    // `SendPropVector` with `SPROP_NOSCALE`: three 32-bit floats as they are.
    private static SendProperty NoScaleVector(string name) =>
        new(SendPropType.Vector, name, 1 << 2, string.Empty, 0f, 0f, 32, 0);

    private static SendProperty VectorXy(string name, int bits) =>
        new(SendPropType.VectorXY, name, 0, string.Empty, -16384f, 16384f, bits, 0);

    private static SendProperty Table(string name, string referenced) =>
        new(SendPropType.DataTable, name, 0, referenced, 0f, 0f, 0, 0);

    private static SendProperty String(string name) =>
        new(SendPropType.String, name, 0, string.Empty, 0f, 0f, 0, 0);

    /// <summary>A demo whose single snapshot carries RED and BLU team entities, each with a score.</summary>
    /// <param name="redScore">RED's `m_iScore`.</param>
    /// <param name="blueScore">BLU's `m_iScore`.</param>
    /// <param name="redRoundsWon">RED's `m_iRoundsWon`.</param>
    /// <param name="blueRoundsWon">BLU's `m_iRoundsWon`.</param>
    /// <param name="redPlayers">`player_array` entity indices for RED; empty when not asserted.</param>
    /// <param name="bluePlayers">`player_array` entity indices for BLU; empty when not asserted.</param>
    public static byte[] DemoWithTeams(
        int redScore,
        int blueScore,
        int redRoundsWon = 0,
        int blueRoundsWon = 0,
        IReadOnlyList<int>? redPlayers = null,
        IReadOnlyList<int>? bluePlayers = null)
    {
        const int TeamClassId = 2;
        const int PlayerArraySlots = 4;
        redPlayers ??= [];
        bluePlayers ??= [];
        DemoSchema baseline = Schema(OriginTable.NonLocal);
        List<SendTable> tables =
        [
            .. baseline.Tables,
            new SendTable(
                "DT_Team",
                NeedsDecoder: true,
                [
                    UnsignedInt("m_iTeamNum", bits: 5), Int("m_iScore", bits: 32),
                    Int("m_iRoundsWon", bits: 8), String("m_szTeamname"),
                    Table("player_array", "player_array"),
                ]),
            new SendTable("DT_TFTeam", NeedsDecoder: true, [Table("baseclass", "DT_Team")]),
            new SendTable("player_array", NeedsDecoder: true,
                [.. Enumerable.Range(0, PlayerArraySlots).Select(slot => UnsignedInt(slot.ToString("D3", CultureInfo.InvariantCulture), bits: 11))]),
        ];

        DemoSchema schema = new(
            tables,
            [
                .. baseline.ServerClasses,
                new ServerClass(TeamClassId, "CTFTeam", "DT_TFTeam"),
            ]);
        EntityDecoder decoder = new(schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));

        List<DecodedEntity> entities =
        [
            Entity(decoder, PlayerClassId, 1, new Dictionary<string, PropertyValue>
            {
                ["m_vecOrigin"] = PropertyValue.FromVectorXY(0f, 0f),
                ["m_vecOrigin[2]"] = PropertyValue.FromFloat(0f),
                ["m_lifeState"] = PropertyValue.FromInt(0),
            }),
            Entity(decoder, TeamClassId, 2, PlayerArrayEntity(2, redScore, redRoundsWon, "Red", redPlayers)),
            Entity(decoder, TeamClassId, 3, PlayerArrayEntity(3, blueScore, blueRoundsWon, "Blue", bluePlayers)),
        ];

        static Dictionary<string, PropertyValue> PlayerArrayEntity(int teamNum, int score, int roundsWon, string name, IReadOnlyList<int> players)
        {
            Dictionary<string, PropertyValue> data = new()
            {
                ["m_iTeamNum"] = PropertyValue.FromInt(teamNum),
                ["m_iScore"] = PropertyValue.FromInt(score),
                ["m_iRoundsWon"] = PropertyValue.FromInt(roundsWon),
                ["m_szTeamname"] = PropertyValue.FromString(name),
            };

            for (int slot = 0; slot < players.Count; slot++)
            {
                data[$"player_array.{slot:D3}"] = PropertyValue.FromInt(players[slot]);
            }

            return data;
        }

        byte[] body = decoder.EncodeEntities(entities, [], isDelta: false, 0, out int bits);

        return SyntheticDemo.From(
            SyntheticDemo.DefaultProtocol,
            SyntheticDemo.Packet(SyntheticDemo.DefaultProtocol, 0, ServerInfo()),
            SyntheticDemo.DataTables(schema),
            SyntheticDemo.Packet(
                SyntheticDemo.DefaultProtocol,
                100,
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

    /// <summary>The recorder's `m_flDeathTime` in <see cref="DemoWithPlayerClassHud"/>.</summary>
    public const float DeathTime = 77.25f;

    /// <summary>
    /// The recorder (entity 1) with what `CTFHudPlayerClass::OnThink` reads (tf_hud_playerstatus.cpp:184): the spy fields of
    /// `DT_TFPlayerShared` (tf_player_shared.cpp:576-586), its own `m_vecVelocity` (player.cpp:8140), and weapon 30 in hand
    /// carrying `m_iAccountID` and `m_iEntityQuality` (econ_item_view.cpp:187-188).
    /// </summary>
    /// <param name="invisChangeCompleteTime">`m_flInvisChangeCompleteTime`.</param>
    /// <param name="cloakMeter">`m_flCloakMeter`.</param>
    /// <param name="disguiseWeapon">The slot `m_hDisguiseWeapon` names.</param>
    /// <param name="velocity">`m_vecVelocity[0..2]`.</param>
    /// <param name="accountId">The held item's `m_iAccountID`.</param>
    /// <param name="quality">The held item's `m_iEntityQuality`.</param>
    /// <returns>A demo's bytes.</returns>
    /// <remarks>Its `m_flDeathTime` is <see cref="DeathTime"/>.</remarks>
    public static byte[] DemoWithPlayerClassHud(
        float invisChangeCompleteTime, float cloakMeter, int disguiseWeapon, (float X, float Y, float Z) velocity, uint accountId, int quality)
    {
        const int WeaponClassId = 1;
        const int FlagClassId = 2;
        DemoSchema baseline = Schema(OriginTable.NonLocal);
        List<SendTable> tables = [];

        foreach (SendTable table in baseline.Tables)
        {
            tables.Add(table.Name switch
            {
                "DT_TFPlayer" => table with
                {
                    Properties = [.. table.Properties, Table("playershared", "DT_TFPlayerShared"), UnsignedInt("m_hItem", bits: 21)],
                },
                "DT_BasePlayer" => table with
                {
                    Properties = [.. table.Properties, Table("localdata", "DT_LocalPlayerExclusive"), Table("bcc", "DT_BaseCombatCharacter")],
                },
                _ => table,
            });
        }

        tables.Add(new SendTable("DT_TFPlayerShared", NeedsDecoder: true,
        [
            NoScaleFloat("m_flInvisChangeCompleteTime"), NoScaleFloat("m_flCloakMeter"), UnsignedInt("m_hDisguiseWeapon", bits: 21),
        ]));
        tables.Add(new SendTable("DT_LocalPlayerExclusive", NeedsDecoder: true,
        [
            NoScaleFloat("m_vecVelocity[0]"), NoScaleFloat("m_vecVelocity[1]"), NoScaleFloat("m_vecVelocity[2]"), NoScaleFloat("m_flDeathTime"),
        ]));
        tables.Add(new SendTable("DT_BaseCombatCharacter", NeedsDecoder: true, [UnsignedInt("m_hActiveWeapon", bits: 21)]));
        tables.Add(new SendTable("DT_ScriptCreatedItem", NeedsDecoder: true,
        [
            UnsignedInt("m_iItemDefinitionIndex", bits: 20), UnsignedInt("m_iAccountID", bits: 32), Int("m_iEntityQuality", bits: 5),
        ]));
        tables.Add(new SendTable("DT_PipebombLauncherLocalData", NeedsDecoder: true, [NoScaleFloat("m_flChargeBeginTime")]));
        tables.Add(new SendTable("DT_TestWeapon", NeedsDecoder: true,
        [
            Table("m_Item", "DT_ScriptCreatedItem"), Table("PipebombLauncherLocalData", "DT_PipebombLauncherLocalData"),
        ]));
        tables.Add(new SendTable("DT_CaptureFlag", NeedsDecoder: true, [Int("m_nFlagStatus", bits: 3)]));

        DemoSchema schema = new(
            tables,
            [
                .. baseline.ServerClasses,
                new ServerClass(WeaponClassId, "CTFScatterGun", "DT_TestWeapon"),
                new ServerClass(FlagClassId, "CCaptureFlag", "DT_CaptureFlag"),
            ]);
        EntityDecoder decoder = new(schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));

        List<DecodedEntity> entities =
        [
            Entity(decoder, PlayerClassId, 1, new Dictionary<string, PropertyValue>
            {
                ["m_vecOrigin"] = PropertyValue.FromVectorXY(0f, 0f),
                ["m_vecOrigin[2]"] = PropertyValue.FromFloat(0f),
                ["m_lifeState"] = PropertyValue.FromInt(0),
                ["m_flInvisChangeCompleteTime"] = PropertyValue.FromFloat(invisChangeCompleteTime),
                ["m_flCloakMeter"] = PropertyValue.FromFloat(cloakMeter),
                ["m_hDisguiseWeapon"] = LoadoutHandle(disguiseWeapon),
                ["m_vecVelocity[0]"] = PropertyValue.FromFloat(velocity.X),
                ["m_vecVelocity[1]"] = PropertyValue.FromFloat(velocity.Y),
                ["m_vecVelocity[2]"] = PropertyValue.FromFloat(velocity.Z),
                ["m_flDeathTime"] = PropertyValue.FromFloat(DeathTime),
                ["DT_BaseCombatCharacter.m_hActiveWeapon"] = LoadoutHandle(30),
                ["m_hItem"] = LoadoutHandle(31),
            }),
            Entity(decoder, WeaponClassId, 30, new Dictionary<string, PropertyValue>
            {
                ["m_iItemDefinitionIndex"] = PropertyValue.FromInt(13),
                ["m_iAccountID"] = PropertyValue.FromInt(unchecked((int)accountId)),
                ["m_iEntityQuality"] = PropertyValue.FromInt(quality),
                ["m_flChargeBeginTime"] = PropertyValue.FromFloat(3.5f),
            }),
            Entity(decoder, FlagClassId, 31, new Dictionary<string, PropertyValue> { ["m_nFlagStatus"] = PropertyValue.FromInt(1) }),
        ];

        byte[] body = decoder.EncodeEntities(entities, [], isDelta: false, 0, out int bits);

        return SyntheticDemo.From(
            SyntheticDemo.DefaultProtocol,
            SyntheticDemo.Packet(SyntheticDemo.DefaultProtocol, 0, ServerInfo()),
            SyntheticDemo.DataTables(schema),
            SyntheticDemo.Packet(
                SyntheticDemo.DefaultProtocol,
                100,
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
}
