using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
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

    /// <summary>The flattened index of a property, or a failure naming what was available.</summary>
    /// <remarks>
    /// Throws rather than returning -1 on purpose. A missing property silently encodes nothing,
    /// and the test then fails on an assertion about a value that was never sent — which reads as
    /// a decoder bug. Naming the table is the useful half of the message, because the usual cause
    /// is a property put in the wrong one.
    /// </remarks>
    private static int IndexOf(IReadOnlyList<FlatProperty> flat, string name)
    {
        for (int i = 0; i < flat.Count; i++)
        {
            if (string.Equals(flat[i].Property.Name, name, StringComparison.Ordinal))
            {
                return i;
            }
        }

        throw new ArgumentException(
            $"'{name}' is not in the flattened list for CTFPlayer. It holds: " +
            string.Join(", ", flat.Select(entry => $"{entry.OwnerTable}.{entry.Property.Name}")),
            nameof(name));
    }

    private static ServerInfoMessage ServerInfo() => new(
        NetworkProtocol: SyntheticDemo.DefaultProtocol,
        ServerCount: 1,
        IsSourceTv: true,
        IsDedicated: true,
        MapCrc: 0,
        MaxClasses: 1,
        MapHash: new byte[16],
        PlayerSlot: 0,
        MaxPlayers: 24,
        IntervalPerTick: 1f / 66.67f,
        Platform: 'w',
        GameDirectory: "tf",
        Map: "cp_process_final",
        Skybox: "sky_tf2_04",
        ServerName: "synthetic",
        IsReplay: false);

    private static SendProperty Int(string name, int bits) =>
        new(SendPropType.Int, name, 0, string.Empty, 0f, 0f, bits, 0);

    private static SendProperty Float(string name, float low, float high, int bits) =>
        new(SendPropType.Float, name, 0, string.Empty, low, high, bits, 0);

    private static SendProperty VectorXy(string name, int bits) =>
        new(SendPropType.VectorXY, name, 0, string.Empty, -16384f, 16384f, bits, 0);

    private static SendProperty Table(string name, string referenced) =>
        new(SendPropType.DataTable, name, 0, referenced, 0f, 0f, 0, 0);
}
