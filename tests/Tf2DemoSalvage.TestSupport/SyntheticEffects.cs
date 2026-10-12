using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Tests;

/// <summary>
/// A demo of players, weapons and the one-shot effects a presenter draws: explosions, blood, effect dispatches, TF
/// particle effects and fired bullets, each a temp entity with the values the test chose (D38, D214).
/// </summary>
/// <remarks>
/// **Every float is `SPROP_NOSCALE`**, so a value read back is the value written and a test can assert it exactly. The
/// real tables quantise; what a feed does with the number afterwards is what these demos are for, and the wire widths
/// have their own conformance tests.
///
/// **An effect FIRES an interpolation window after it arrives** (`DemoTimeline.FireTick`), six ticks at the default
/// 0.1 s and this demo's 0.015 s a tick. A test reads the tick off the feed rather than adding six.
/// </remarks>
internal static class SyntheticEffects
{
    /// <summary>The demo's seconds per tick.</summary>
    public const float Interval = 0.015f;

    /// <summary>The tick the entities' one full snapshot arrives on.</summary>
    public const int PlayersTick = 10;

    /// <summary>The players' class.</summary>
    public const string PlayerClass = "CTFPlayer";

    /// <summary>The class a weapon entity is created with: one that reaches `DT_BaseCombatWeapon`.</summary>
    public const string WeaponClass = "CTFScattergun";

    private const int NoScale = 1 << 2;

    private static readonly (string Class, SendTable Table)[] EffectTables =
    [
        ("CTETFExplosion", new SendTable("DT_TETFExplosion", NeedsDecoder: true,
        [
            Float("m_vecOrigin[0]"), Float("m_vecOrigin[1]"), Float("m_vecOrigin[2]"), Vector("m_vecNormal"),
            Int("m_iWeaponID"), Int("entindex"), Int("m_iCustomParticleIndex"),
        ])),
        ("CTETFBlood", new SendTable("DT_TETFBlood", NeedsDecoder: true,
        [
            Float("m_vecOrigin[0]"), Float("m_vecOrigin[1]"), Float("m_vecOrigin[2]"), Vector("m_vecNormal"),
            Int("entindex"),
        ])),
        ("CTEEffectDispatch", new SendTable("DT_TEEffectDispatch", NeedsDecoder: true,
        [
            Float("m_vOrigin[0]"), Float("m_vOrigin[1]"), Float("m_vOrigin[2]"),
            Float("m_vStart[0]"), Float("m_vStart[1]"), Float("m_vStart[2]"),
            Vector("m_vAngles"), Vector("m_vNormal"), Int("m_iEffectName"), Int("m_fFlags"),
            Int("m_nAttachmentIndex"), Int("m_nSurfaceProp"), Int("m_nDamageType"), Int("m_nHitBox"), Int("entindex"),
            Int("m_bControlPoint1"),
            Float("m_ControlPoint1.m_vecOffset[0]"), Float("m_ControlPoint1.m_vecOffset[1]"),
            Float("m_ControlPoint1.m_vecOffset[2]"),
        ])),
        ("CTETFParticleEffect", new SendTable("DT_TETFParticleEffect", NeedsDecoder: true,
        [
            Float("m_vecOrigin[0]"), Float("m_vecOrigin[1]"), Float("m_vecOrigin[2]"),
            Float("m_vecStart[0]"), Float("m_vecStart[1]"), Float("m_vecStart[2]"),
            Vector("m_vecAngles"), Int("m_iParticleSystemIndex"), Int("entindex"), Int("m_iAttachType"),
            Int("m_iAttachmentPointIndex"),
        ])),
        ("CTEFireBullets", new SendTable("DT_TEFireBullets", NeedsDecoder: true,
        [
            Vector("m_vecOrigin"), Float("m_vecAngles[0]"), Float("m_vecAngles[1]"), Int("m_iWeaponID"),
            Int("m_iMode"), Int("m_iSeed"), Int("m_iPlayer"), Float("m_flSpread"), Int("m_bCritical"),
        ])),
    ];

    /// <summary>One entity, entering on <see cref="PlayersTick"/>.</summary>
    /// <param name="EntityIndex">Its slot.</param>
    /// <param name="ClassName">Its class: <see cref="PlayerClass"/> or <see cref="WeaponClass"/>.</param>
    /// <param name="Values">Its properties.</param>
    internal sealed record Placed(int EntityIndex, string ClassName, IReadOnlyDictionary<string, PropertyValue> Values);

    /// <summary>A later change to one entity's properties, alone in its snapshot.</summary>
    /// <param name="Tick">The tick the snapshot arrives on.</param>
    /// <param name="EntityIndex">The entity.</param>
    /// <param name="Values">The properties that changed.</param>
    internal sealed record Change(int Tick, int EntityIndex, IReadOnlyDictionary<string, PropertyValue> Values);

    /// <summary>One temp entity: the tick its message arrives on, its class, and its fields by name.</summary>
    /// <param name="Tick">The tick the message arrives on.</param>
    /// <param name="ClassName">The effect's class, `CTETFExplosion` and the like.</param>
    /// <param name="Values">Its fields.</param>
    internal sealed record Effect(int Tick, string ClassName, IReadOnlyDictionary<string, PropertyValue> Values);

    /// <summary>The players' schema, a weapon class, and a table and class for each effect.</summary>
    /// <remarks>
    /// The players' `DT_BaseEntity` gains `m_hOwnerEntity` and their `DT_BaseAnimating` `m_nMuzzleFlashParity`, appended,
    /// because the timeline reads both by their declaring table's name and a second table of that name cannot be
    /// declared. Only this fixture's demos carry them.
    /// </remarks>
    public static DemoSchema Schema()
    {
        DemoSchema players = SyntheticPlayer.Schema();

        SendTable[] weapon =
        [
            new("DT_ScriptCreatedItem", NeedsDecoder: true, [Int("m_iItemDefinitionIndex")]),
            new("DT_BaseCombatWeapon", NeedsDecoder: true,
            [
                Table("baseanimating", "DT_BaseAnimating"),
                Table("item", "DT_ScriptCreatedItem"),
            ]),
            new("DT_TFScattergun", NeedsDecoder: true, [Table("combatweapon", "DT_BaseCombatWeapon")]),
        ];

        return new DemoSchema(
            [
                .. players.Tables.Select(table => table.Name switch
                {
                    "DT_BaseEntity" =>
                        new SendTable(table.Name, table.NeedsDecoder, [.. table.Properties, Int("m_hOwnerEntity")]),
                    "DT_BaseAnimating" =>
                        new SendTable(table.Name, table.NeedsDecoder, [.. table.Properties, Int("m_nMuzzleFlashParity")]),
                    _ => table,
                }),
                .. weapon,
                .. EffectTables.Select(effect => effect.Table),
            ],
            [
                .. players.ServerClasses,
                new ServerClass(players.ServerClasses.Count, WeaponClass, "DT_TFScattergun"),
                .. EffectTables.Select((effect, index) =>
                    new ServerClass(players.ServerClasses.Count + 1 + index, effect.Class, effect.Table.Name)),
            ]);
    }

    /// <summary>A demo of these entities, entering on <see cref="PlayersTick"/>, their later changes, and these effects.</summary>
    /// <param name="entities">Each entity's slot, class and properties.</param>
    /// <param name="effectNames">The `EffectDispatch` string table, by index.</param>
    /// <param name="particleNames">The `ParticleEffectNames` string table, by index.</param>
    /// <param name="changes">Later changes, in tick order, each after <see cref="PlayersTick"/>.</param>
    /// <param name="effects">The temp entities, in tick order; one message each.</param>
    /// <returns>The demo's bytes.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static byte[] Demo(
        IReadOnlyList<Placed> entities,
        IReadOnlyList<string> effectNames,
        IReadOnlyList<string> particleNames,
        IReadOnlyList<Change> changes,
        params Effect[] effects)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(effectNames);
        ArgumentNullException.ThrowIfNull(particleNames);
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(effects);

        DemoSchema schema = Schema();
        EntityDecoder decoder = new(schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));

        // **Every later packet is written AFTER this one**, as a reader reads it: `SyntheticDemo.Packet` encodes at
        // protocol 0, where `svc_TempEntities` states its length in 17 bits and not a VarInt, and the body then lands
        // nine bits out and decodes to "no class" (B112).
        ServerInfoMessage serverInfo = ServerInfo(schema.ServerClasses.Count);

        List<DemoCommand> commands =
        [
            SyntheticDemo.Packet(
                SyntheticDemo.DefaultProtocol,
                0,
                serverInfo,
                SyntheticDemo.StringTable(EffectDispatchFeed.TableName, [.. effectNames], maxEntries: 64),
                SyntheticDemo.StringTable(EffectDispatchFeed.ParticleTableName, [.. particleNames], maxEntries: 64)),
            SyntheticDemo.DataTables(schema),
        ];

        if (entities.Count > 0)
        {
            // Ascending by slot: a snapshot's entity indices are delta-coded.
            List<DecodedEntity> entering =
            [
                .. entities.OrderBy(entity => entity.EntityIndex).Select(entity => new DecodedEntity(
                    entity.EntityIndex,
                    ClassId(schema, entity.ClassName),
                    SerialNumber: entity.EntityIndex,
                    EntityUpdateType.Enter,
                    Properties(decoder, ClassId(schema, entity.ClassName), entity.Values))),
            ];

            byte[] snapshot = decoder.EncodeEntities(entering, [], isDelta: false, 0, out int bits);

            commands.Add(SyntheticDemo.PacketAfter(
                serverInfo,
                PlayersTick,
                new PacketEntitiesMessage(
                    MaxEntries: 64,
                    IsDelta: false,
                    DeltaFromTick: null,
                    BaselineIndex: false,
                    UpdatedEntries: entering.Count,
                    LengthBits: bits,
                    UpdateBaseline: false,
                    Body: snapshot)));
        }

        // One packet each, in tick order; a change before an effect of its own tick, as a snapshot precedes its events.
        List<(int Tick, int Order, DemoCommand Command)> later = [];
        int from = PlayersTick;

        foreach (Change change in changes)
        {
            int classId = ClassId(schema, entities.First(entity => entity.EntityIndex == change.EntityIndex).ClassName);
            DecodedEntity changed = new(
                change.EntityIndex,
                classId,
                SerialNumber: change.EntityIndex,
                EntityUpdateType.Delta,
                Properties(decoder, classId, change.Values));

            byte[] body = decoder.EncodeEntities([changed], [], isDelta: true, 0, out int bits);

            later.Add((change.Tick, 0, SyntheticDemo.PacketAfter(
                serverInfo,
                change.Tick,
                new PacketEntitiesMessage(
                    MaxEntries: 64,
                    IsDelta: true,
                    DeltaFromTick: from,
                    BaselineIndex: false,
                    UpdatedEntries: 1,
                    LengthBits: bits,
                    UpdateBaseline: false,
                    Body: body))));

            from = change.Tick;
        }

        foreach (Effect effect in effects)
        {
            int classId = ClassId(schema, effect.ClassName);

            byte[] body = decoder.EncodeTempEntities(
                [new DecodedTempEntity(classId, DelaySeconds: 0f, Properties(decoder, classId, effect.Values))],
                reliable: false,
                lengthBits: 0);

            later.Add((effect.Tick, 1, SyntheticDemo.PacketAfter(
                serverInfo,
                effect.Tick,
                new TempEntitiesMessage(Count: 1, BodyBits: body.Length * 8, Body: body))));
        }

        commands.AddRange(
            later.OrderBy(packet => packet.Tick).ThenBy(packet => packet.Order).Select(packet => packet.Command));

        return SyntheticDemo.From(SyntheticDemo.DefaultProtocol, [.. commands]);
    }

    /// <summary>A player's properties: where he stands, his team, alive.</summary>
    /// <param name="x">His origin's x.</param>
    /// <param name="y">His origin's y.</param>
    /// <param name="z">His origin's z.</param>
    /// <param name="team">His team.</param>
    /// <returns>The properties.</returns>
    public static IReadOnlyDictionary<string, PropertyValue> Standing(float x, float y, float z, int team) =>
        new Dictionary<string, PropertyValue>(StringComparer.Ordinal)
        {
            ["m_vecOrigin"] = PropertyValue.FromVectorXY(x, y),
            ["m_vecOrigin[2]"] = PropertyValue.FromFloat(z),
            ["DT_BaseEntity.m_iTeamNum"] = PropertyValue.FromInt(team),
            ["m_lifeState"] = PropertyValue.FromInt(0),
        };

    private static int ClassId(DemoSchema schema, string className) =>
        schema.ServerClasses.First(entry => string.Equals(entry.ClassName, className, StringComparison.Ordinal)).Id;

    private static List<DecodedProperty> Properties(
        EntityDecoder decoder, int classId, IReadOnlyDictionary<string, PropertyValue> values)
    {
        IReadOnlyList<FlatProperty> flat = decoder.FlattenedFor(classId);
        List<DecodedProperty> properties = [];

        foreach ((string name, PropertyValue value) in values)
        {
            int index = IndexOf(flat, name);
            properties.Add(new DecodedProperty(index, flat[index], value));
        }

        // Sorted by property index: properties are delta-coded against the one before.
        properties.Sort((left, right) => left.Index.CompareTo(right.Index));

        return properties;
    }

    /// <summary>The flattened index of a property by its bare or table-qualified name; throws naming what there is.</summary>
    private static int IndexOf(IReadOnlyList<FlatProperty> flat, string name)
    {
        for (int index = 0; index < flat.Count; index++)
        {
            if (string.Equals(flat[index].Property.Name, name, StringComparison.Ordinal) ||
                string.Equals($"{flat[index].OwnerTable}.{flat[index].Property.Name}", name, StringComparison.Ordinal))
            {
                return index;
            }
        }

        throw new ArgumentException(
            $"'{name}' is not in the flattened list. It holds: " +
            string.Join(", ", flat.Select(entry => $"{entry.OwnerTable}.{entry.Property.Name}")),
            nameof(name));
    }

    private static ServerInfoMessage ServerInfo(int classes) => new(
        NetworkProtocol: SyntheticDemo.DefaultProtocol,
        ServerCount: 1,
        IsSourceTv: true,
        IsDedicated: true,
        MapCrc: 0,
        MaxClasses: (ushort)classes,
        MapHash: new byte[16],
        PlayerSlot: 0,
        MaxPlayers: 24,
        IntervalPerTick: Interval,
        Platform: 'w',
        GameDirectory: "tf",
        Map: "cp_process_final",
        Skybox: "sky_tf2_04",
        ServerName: "synthetic",
        IsReplay: false);

    private static SendProperty Int(string name) =>
        new(SendPropType.Int, name, 0, string.Empty, 0f, 0f, 32, 0);

    private static SendProperty Float(string name) =>
        new(SendPropType.Float, name, NoScale, string.Empty, 0f, 0f, 32, 0);

    private static SendProperty Vector(string name) =>
        new(SendPropType.Vector, name, NoScale, string.Empty, 0f, 0f, 32, 0);

    private static SendProperty Table(string name, string referenced) =>
        new(SendPropType.DataTable, name, 0, referenced, 0f, 0f, 0, 0);
}
