using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Tests;

/// <summary>
/// A demo carrying one <c>CTFProjectile_Rocket</c>-shaped entity, moving over several ticks.
/// </summary>
/// <remarks>
/// **The schema mirrors what a rocket actually declares, not a player's.** `CTFBaseRocket` sends
/// its own position and rotation on its OWN table rather than inheriting `DT_BaseEntity`'s —
/// `RecvPropVector( RECVINFO_NAME( m_vecNetworkOrigin, m_vecOrigin ) )`,
/// `tf_weaponbase_rocket.cpp:43` — as a single three-component <see cref="SendPropType.Vector"/>,
/// not the split XY-plus-Z shape a modern player's exclusive table uses. `EntityStateTableTests`
/// already established this is where <c>Origin()</c> has to look (B372); this is the same shape, run
/// through the full <c>DemoTimeline.Build</c> pipeline instead of the table in isolation, so a
/// position bug anywhere between the wire and the drawn pose has somewhere to show up.
///
/// **An owner, so `OwnedBy` resolves to a real entity** — B397 asked whether a rocket's OWNER is
/// placed correctly, and that question needs a second entity in the demo to own it.
///
/// This is a MINIMAL rocket: only what the timeline reads for position and ownership. Existing
/// coverage for the model/angle resolution stays in <c>EntityStateTableTests</c>.
/// </remarks>
internal static class SyntheticRocket
{
    /// <summary>Class id the rocket entity is created with.</summary>
    public const int RocketClassId = 0;

    /// <summary>Class id the owning player entity is created with.</summary>
    public const int OwnerClassId = 1;

    /// <summary>Entity slot the rocket occupies.</summary>
    public const int RocketEntityIndex = 40;

    /// <summary>Entity slot the owning player occupies.</summary>
    public const int OwnerEntityIndex = 7;

    /// <summary>Precache index of the rocket's model.</summary>
    public const int Model = 1;

    /// <summary>What that index names.</summary>
    public const string ModelPath = "models/weapons/w_models/w_rocket.mdl";

    /// <summary>A schema with one rocket class and one bystander player class.</summary>
    public static DemoSchema Schema() => new(
        [
            new SendTable("DT_BaseEntity", NeedsDecoder: true,
            [
                Int("m_nModelIndex", bits: 13),
                UnsignedInt("m_hOwnerEntity", bits: 11),
            ]),
            new SendTable("DT_TFBaseRocket", NeedsDecoder: true,
            [
                Vector("m_vecOrigin", bits: 32),
                Table("baseentity", "DT_BaseEntity"),
            ]),
            new SendTable("DT_BasePlayer", NeedsDecoder: true,
            [
                Table("baseentity", "DT_BaseEntity"),
            ]),
        ],
        [
            new ServerClass(RocketClassId, "CTFProjectile_Rocket", "DT_TFBaseRocket"),
            new ServerClass(OwnerClassId, "CTFPlayer", "DT_BasePlayer"),
        ]);

    /// <summary>A demo of one rocket moving in a straight line, owned by a stationary bystander.</summary>
    /// <param name="intervalPerTick">The demo's seconds per tick.</param>
    /// <param name="positions">Tick and position for each snapshot; the first creates the rocket.</param>
    /// <returns>The demo's bytes.</returns>
    public static byte[] DemoOverTicks(
        float intervalPerTick, params (int Tick, float X, float Y, float Z)[] positions)
    {
        ArgumentNullException.ThrowIfNull(positions);

        DemoSchema schema = Schema();
        EntityDecoder decoder = new(
            schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));

        List<DemoCommand> commands =
        [
            SyntheticDemo.Packet(
                SyntheticDemo.DefaultProtocol,
                0,
                ServerInfo(intervalPerTick),
                SyntheticDemo.StringTable(
                    ModelPrecache.TableName, [string.Empty, ModelPath], maxEntries: 8)),
            SyntheticDemo.DataTables(schema),
        ];

        // The owner exists from the first tick onward, standing still, so `OwnedBy` has a real
        // entity to resolve to. Its own entity index is what the rocket's `m_hOwnerEntity` names.
        // Carries a harmless model index rather than no properties at all: an ENTER with nothing
        // in it is not a shape any real update takes.
        DecodedEntity owner = Entity(
            decoder,
            OwnerClassId,
            OwnerEntityIndex,
            new Dictionary<string, PropertyValue> { ["m_nModelIndex"] = PropertyValue.FromInt(0) })
            with
        {
            UpdateType = EntityUpdateType.Enter,
        };

        for (int index = 0; index < positions.Length; index++)
        {
            (int tick, float x, float y, float z) = positions[index];

            Dictionary<string, PropertyValue> rocketValues = new()
            {
                ["m_vecOrigin"] = PropertyValue.FromVector(x, y, z),
                ["m_hOwnerEntity"] = PropertyValue.FromInt(OwnerEntityIndex),
            };

            if (index == 0)
            {
                rocketValues["m_nModelIndex"] = PropertyValue.FromInt(Model);
            }

            DecodedEntity rocket = Entity(decoder, RocketClassId, RocketEntityIndex, rocketValues)
                with
            {
                UpdateType = index == 0 ? EntityUpdateType.Enter : EntityUpdateType.Delta,
            };

            // Ascending by entity index, because entity indices are delta-coded and the encoder
            // writes the gap to the next slot rather than the slot itself.
            List<DecodedEntity> entities = index == 0 ? [owner, rocket] : [rocket];

            byte[] body = decoder.EncodeEntities(
                entities, [], isDelta: index > 0, 0, out int bits);

            commands.Add(SyntheticDemo.Packet(
                SyntheticDemo.DefaultProtocol,
                tick,
                new PacketEntitiesMessage(
                    MaxEntries: 96,
                    IsDelta: index > 0,
                    DeltaFromTick: index > 0 ? positions[index - 1].Tick : null,
                    BaselineIndex: false,
                    UpdatedEntries: entities.Count,
                    LengthBits: bits,
                    UpdateBaseline: false,
                    Body: body)));
        }

        return SyntheticDemo.From(SyntheticDemo.DefaultProtocol, [.. commands]);
    }

    private static DecodedEntity Entity(
        EntityDecoder decoder,
        int classId,
        int entityIndex,
        Dictionary<string, PropertyValue> values)
    {
        IReadOnlyList<FlatProperty> flat = decoder.FlattenedFor(classId);
        List<DecodedProperty> properties = [];

        foreach (KeyValuePair<string, PropertyValue> value in values)
        {
            properties.Add(Property(flat, value.Key, value.Value));
        }

        properties.Sort((left, right) => left.Index.CompareTo(right.Index));

        return new DecodedEntity(
            entityIndex, classId, SerialNumber: entityIndex, EntityUpdateType.Enter, properties);
    }

    private static DecodedProperty Property(
        IReadOnlyList<FlatProperty> flat, string name, PropertyValue value)
    {
        for (int index = 0; index < flat.Count; index++)
        {
            if (string.Equals(flat[index].Property.Name, name, StringComparison.Ordinal))
            {
                return new DecodedProperty(index, flat[index], value);
            }
        }

        throw new InvalidOperationException($"no flattened property named '{name}'");
    }

    private static ServerInfoMessage ServerInfo(float intervalPerTick = 1f / 66.67f) => new(
        NetworkProtocol: SyntheticDemo.DefaultProtocol,
        ServerCount: 1,
        IsSourceTv: false,
        IsDedicated: false,
        MapCrc: 0,
        MaxClasses: 2,
        MapHash: new byte[16],
        PlayerSlot: 0,
        MaxPlayers: 2,
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
        new(SendPropType.Int, name, 1 << 3, string.Empty, 0f, 0f, bits, 0);

    private static SendProperty Vector(string name, int bits) =>
        new(SendPropType.Vector, name, 0, string.Empty, -16384f, 16384f, bits, 0);

    private static SendProperty Table(string name, string referenced) =>
        new(SendPropType.DataTable, name, 0, referenced, 0f, 0f, 0, 0);
}
