using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests;

/// <summary>
/// A demo carrying one carried weapon, with or without a networked model index.
/// </summary>
/// <remarks>
/// **The situation is chosen rather than found**, which is the whole argument for a synthetic
/// fixture (D38): a real recording has to be searched for a medic, and the assertion then becomes
/// whatever that search turned up. Here the weapon is built with exactly the combination under
/// test — an item and no model, an item and a model, or neither.
///
/// The schema is deliberately minimal but not a caricature: the weapon's table chains to
/// <c>DT_BaseCombatWeapon</c>, because that is what <c>SchemaClasses.BoneMergesItself</c> walks to
/// decide that a weapon merges onto its owner without the flag ever travelling
/// (<c>CBaseCombatWeapon::Equip</c> calls <c>FollowEntity</c> with bone merge defaulted on).
/// A chain that skipped it would describe a weapon the game does not have.
/// </remarks>
internal static class SyntheticWeapon
{
    /// <summary>Class id of the weapon this fixture networks.</summary>
    public const int WeaponClassId = 0;

    /// <summary>Entity slot the weapon occupies.</summary>
    public const int WeaponEntityIndex = 9;

    /// <summary>Precache index of the weapon's world model, when it sends one.</summary>
    public const int WorldModel = 2;

    /// <summary>What that index names.</summary>
    public const string WorldModelPath = "models/weapons/w_models/w_rocketlauncher.mdl";

    /// <summary>A demo of one carried weapon.</summary>
    /// <param name="item">Its item definition index, or null to send none.</param>
    /// <param name="worldModelIndex">Its world model index, or null to send none.</param>
    /// <param name="ownerEntity">The entity slot that owns it.</param>
    /// <returns>The demo's bytes.</returns>
    public static byte[] Demo(int? item, int? worldModelIndex, int ownerEntity)
    {
        DemoSchema schema = Schema();

        EntityDecoder decoder = new(
            schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));

        IReadOnlyList<FlatProperty> flat = decoder.FlattenedFor(WeaponClassId);

        List<DecodedProperty> properties =
        [
            // **The owner, which is what a bone-merged weapon hangs off.** `FollowEntity` zeroes
            // the follower's local origin, so an owner handle is the only thing that says where a
            // carried weapon is.
            Property(flat, "m_hOwnerEntity", PropertyValue.FromInt(Handle(ownerEntity))),

            // **`EF_BONEMERGE`, sent because a real one sends it.** Measured on `cp_fulgur`: every
            // live `CWeaponMedigun` reports the flag on the wire. Wearables are the opposite case —
            // 26 of 26 send no `m_fEffects` at all, because `CEconWearable::Spawn` sets it on the
            // client — and a fixture that copied the wearable's shape here would describe a weapon
            // this project has never seen and would fail the attachment branch for a reason the
            // real entity does not have.
            Property(flat, "m_fEffects", PropertyValue.FromInt(BoneMerge)),
        ];

        if (item is { } definition)
        {
            properties.Add(Property(
                flat, "m_iItemDefinitionIndex", PropertyValue.FromInt(definition)));
        }

        if (worldModelIndex is { } world)
        {
            properties.Add(Property(
                flat, "m_iWorldModelIndex", PropertyValue.FromInt(world)));
        }

        properties.Sort((left, right) => left.Index.CompareTo(right.Index));

        DecodedEntity weapon = new(
            WeaponEntityIndex, WeaponClassId, 3, EntityUpdateType.Enter, properties);

        // **The owner has to EXIST, because the handle's serial is checked** (B231).
        // `RecvProxy_IntToEHandle` keeps index and serial, and dereferencing compares the serial
        // against the slot's current occupant — so a handle naming an empty slot resolves to
        // nothing, exactly as a dangling one should. A first version of this fixture sent only the
        // weapon and its owner therefore resolved to null, which dropped the entity through the
        // no-attachment-and-no-origin branch and made all three tests fail for a reason that had
        // nothing to do with what they assert.
        //
        // Serial 1 to match `Handle`. Entities are encoded in ascending slot order because the wire
        // stores each index as a delta from the previous one.
        DecodedEntity owner = new(
            ownerEntity, WeaponClassId, OwnerSerial, EntityUpdateType.Enter, []);

        DecodedEntity[] entities = [owner, weapon];

        byte[] body = decoder.EncodeEntities(entities, [], isDelta: false, 0, out int bits);

        return SyntheticDemo.From(
            SyntheticDemo.DefaultProtocol,
            SyntheticDemo.Packet(
                SyntheticDemo.DefaultProtocol,
                0,

                // **`svc_ServerInfo` first, because the timeline needs a tick interval.** Every
                // other synthetic fixture sends one; leaving it out here produced no tracks at all
                // and looked exactly like the entity being filtered.
                ServerInfo(),
                SyntheticDemo.StringTable(
                    ModelPrecache.TableName,
                    [string.Empty, "models/unused.mdl", WorldModelPath],
                    maxEntries: 8)),
            SyntheticDemo.DataTables(schema),
            SyntheticDemo.Packet(
                SyntheticDemo.DefaultProtocol,
                1,
                new PacketEntitiesMessage(
                    MaxEntries: 64,
                    IsDelta: false,
                    DeltaFromTick: null,
                    BaselineIndex: false,

                    // **From the array, never a literal.** The decoder reads exactly this many
                    // entities and stops; a hardcoded 1 against two encoded entities read the owner,
                    // never reached the weapon, and reported an empty timeline — which looks
                    // identical to the entity being filtered out by the code under test.
                    UpdatedEntries: entities.Length,
                    LengthBits: bits,
                    UpdateBaseline: false,
                    Body: body)));
    }

    /// <summary>A demo of one carried weapon that is holstered and drawn again.</summary>
    /// <param name="ownerEntity">The entity slot that owns it.</param>
    /// <param name="states">
    /// The tick and <c>m_iState</c> of each snapshot. <c>WEAPON_NOT_CARRIED</c> is 0,
    /// <c>WEAPON_IS_CARRIED_BY_PLAYER</c> 1 and <c>WEAPON_IS_ACTIVE</c> 2
    /// (<c>shareddefs.h:296-298</c>).
    /// </param>
    /// <returns>The demo's bytes.</returns>
    /// <remarks>
    /// **The situation a real demo makes expensive to find.** A recording has to be searched for a
    /// player who switched weapons, and the assertion then becomes whatever the search turned up —
    /// where here the two states are put in deliberately and the test predicts each by value.
    ///
    /// The weapon sends an item and no model index, which is the medigun's shape: measured on
    /// `cp_fulgur`, every `CWeaponMedigun` networks neither `m_nModelIndex` nor
    /// `m_iWorldModelIndex` while stating item 211.
    /// </remarks>
    public static byte[] DemoOfStates(int ownerEntity, params (int Tick, int State)[] states)
    {
        ArgumentNullException.ThrowIfNull(states);

        DemoSchema schema = Schema();

        EntityDecoder decoder = new(
            schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));

        IReadOnlyList<FlatProperty> flat = decoder.FlattenedFor(WeaponClassId);

        List<DemoCommand> commands =
        [
            SyntheticDemo.Packet(
                SyntheticDemo.DefaultProtocol,
                0,
                ServerInfo(),
                SyntheticDemo.StringTable(
                    ModelPrecache.TableName,
                    [string.Empty, "models/unused.mdl", WorldModelPath],
                    maxEntries: 8)),
            SyntheticDemo.DataTables(schema),
        ];

        for (int index = 0; index < states.Length; index++)
        {
            (int tick, int state) = states[index];

            List<DecodedProperty> properties =
            [
                Property(flat, "m_hOwnerEntity", PropertyValue.FromInt(Handle(ownerEntity))),
                Property(flat, "m_fEffects", PropertyValue.FromInt(BoneMerge)),
                Property(flat, "m_iItemDefinitionIndex", PropertyValue.FromInt(Medigun)),
                Property(flat, "m_iState", PropertyValue.FromInt(state)),
            ];

            properties.Sort((left, right) => left.Index.CompareTo(right.Index));

            DecodedEntity weapon = new(
                WeaponEntityIndex,
                WeaponClassId,
                3,
                index == 0 ? EntityUpdateType.Enter : EntityUpdateType.Delta,
                properties);

            // The owner exists only on the first snapshot, because it is created there and the
            // handle's serial is checked against the slot's occupant from then on (B231).
            DecodedEntity[] entities = index == 0
                ? [new DecodedEntity(
                    ownerEntity, WeaponClassId, OwnerSerial, EntityUpdateType.Enter, []), weapon]
                : [weapon];

            byte[] body = decoder.EncodeEntities(entities, [], isDelta: index > 0, 0, out int bits);

            commands.Add(SyntheticDemo.Packet(
                SyntheticDemo.DefaultProtocol,
                tick,
                new PacketEntitiesMessage(
                    MaxEntries: 64,
                    IsDelta: index > 0,
                    DeltaFromTick: index > 0 ? states[index - 1].Tick : null,
                    BaselineIndex: false,
                    UpdatedEntries: entities.Length,
                    LengthBits: bits,
                    UpdateBaseline: false,
                    Body: body)));
        }

        return SyntheticDemo.From(SyntheticDemo.DefaultProtocol, [.. commands]);
    }

    /// <summary>The stock Medi Gun's item definition index.</summary>
    public const int Medigun = 211;

    /// <summary>An entity handle: serial above the edict bits, slot below.</summary>
    private static int Handle(int slot) => (OwnerSerial << EdictBits) | slot;

    /// <summary>The owner's serial, which its handle must agree with.</summary>
    private const int OwnerSerial = 1;

    /// <summary>The <c>svc_ServerInfo</c> a timeline needs to know its tick rate.</summary>
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

    /// <summary><c>MAX_EDICT_BITS</c>.</summary>
    private const int EdictBits = 11;

    /// <summary><c>EF_BONEMERGE</c>, <c>const.h</c>.</summary>
    private const int BoneMerge = 1;

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

        throw new InvalidOperationException($"the fixture schema declares no {name}");
    }

    /// <summary>One weapon class, chained to <c>DT_BaseCombatWeapon</c> as a real one is.</summary>
    private static DemoSchema Schema() => new(
        [
            new SendTable("DT_BaseEntity", NeedsDecoder: true,
            [
                new SendProperty(SendPropType.Int, "m_nModelIndex", 1, string.Empty, 0f, 0f, 13, 0),
                new SendProperty(
                    SendPropType.Int, "m_hOwnerEntity", 1, string.Empty, 0f, 0f, 21, 0),
                new SendProperty(SendPropType.Int, "m_fEffects", 1, string.Empty, 0f, 0f, 11, 0),
            ]),
            new SendTable("DT_ScriptCreatedItem", NeedsDecoder: true,
            [
                new SendProperty(
                    SendPropType.Int, "m_iItemDefinitionIndex", 1, string.Empty, 0f, 0f, 16, 0),
            ]),
            new SendTable("DT_BaseCombatWeapon", NeedsDecoder: true,
            [
                new SendProperty(
                    SendPropType.Int, "m_iWorldModelIndex", 1, string.Empty, 0f, 0f, 13, 0),

                // **Eight bits unsigned, which is Valve's own declaration**:
                // `SendPropInt( SENDINFO(m_iState), 8, SPROP_UNSIGNED )`,
                // `basecombatweapon_shared.cpp:2871`. In the MAIN table rather than
                // `DT_LocalWeaponData`, so it travels to every client — which is what makes
                // `C_BaseCombatWeapon::ShouldDraw`'s `return bIsActive` answerable for another
                // player's weapon at all.
                new SendProperty(SendPropType.Int, "m_iState", 1, string.Empty, 0f, 0f, 8, 0),
                new SendProperty(
                    SendPropType.DataTable, "baseentity", 1, "DT_BaseEntity", 0f, 0f, 0, 0),
                new SendProperty(
                    SendPropType.DataTable, "item", 1, "DT_ScriptCreatedItem", 0f, 0f, 0, 0),
            ]),
            new SendTable("DT_WeaponMedigun", NeedsDecoder: true,
            [
                new SendProperty(
                    SendPropType.DataTable, "combatweapon", 1, "DT_BaseCombatWeapon", 0f, 0f, 0, 0),
            ]),
        ],
        [new ServerClass(WeaponClassId, "CWeaponMedigun", "DT_WeaponMedigun")]);
}
