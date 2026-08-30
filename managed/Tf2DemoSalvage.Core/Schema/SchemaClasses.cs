using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Core.Schema;

/// <summary>
/// What a class IS, answered from the send tables a demo carries.
/// </summary>
/// <remarks>
/// **This exists because some client behaviour is keyed on the class rather than on a networked
/// value**, and a demo reader is the only participant that never runs the game code which would
/// otherwise supply it. The engine's client constructs an entity, runs its <c>Spawn</c>, and the
/// resulting state is never sent because every client computes it identically.
///
/// The class itself IS on the wire — <c>dem_datatables</c> carries every send table and the
/// inheritance between them — so the answer is recoverable. It just has to be derived rather than
/// read.
/// </remarks>
public static class SchemaClasses
{
    /// <summary><c>CEconWearable</c>'s network table — <c>econ_wearable.cpp:31</c>.</summary>
    /// <remarks>
    /// <c>IMPLEMENT_NETWORKCLASS_ALIASED( EconWearable, DT_WearableItem )</c>. TF2's own wearables
    /// are <c>DT_TFWearable</c> and descend from it, as does <c>DT_TFPowerupBottle</c>.
    /// </remarks>
    public const string WearableTable = "DT_WearableItem";

    /// <summary><c>CBaseCombatWeapon</c>'s network table — <c>basecombatweapon_shared.cpp:2622</c>.</summary>
    /// <remarks>
    /// **A carried weapon follows its owner the same way a wearable does, and from shared code.**
    /// <c>CBaseCombatWeapon::Equip</c> (<c>basecombatweapon_shared.cpp:982</c>) calls
    /// <c>FollowEntity( pOwner )</c> before its <c>#if !defined( CLIENT_DLL )</c> guard, and
    /// <c>FollowEntity</c>'s second parameter defaults to <c>bBoneMerge = true</c>
    /// (<c>baseentity.h:564</c>) — so the client adds <c>EF_BONEMERGE</c> itself here too.
    ///
    /// **Measured, and it is why weapons split down the middle on the wire**: `CTFRocketLauncher`
    /// and `CWeaponMedigun` carry the flag, while `CTFFireAxe`, `CTFShovel`, `CTFBonesaw` and
    /// `CTFWeaponInvis` do not. Reading the wire alone therefore sends *some* weapons and the
    /// viewmodel arms down the transform path — measured as `c_spy_arms.mdl` claimed 319 times and
    /// `c_fireaxe_pyro` 123 times, which is exactly the missing viewmodel (B231).
    /// </remarks>
    public const string CombatWeaponTable = "DT_BaseCombatWeapon";

    /// <summary>Whether a class bone-merges itself the moment the client creates it.</summary>
    /// <param name="schema">The demo's send tables.</param>
    /// <param name="tableName">The class's own table, from its server class entry.</param>
    /// <returns>Whether the engine would have set <c>EF_BONEMERGE</c> without being told.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="schema"/> is null.</exception>
    /// <remarks>
    /// **<c>CEconWearable::Spawn</c>, <c>econ_wearable.cpp:112</c>**, and the placement of the
    /// preprocessor guard is the whole point:
    ///
    /// <code>
    ///   BaseClass::Spawn();
    ///
    ///   AddEffects( EF_BONEMERGE );
    ///   AddEffects( EF_BONEMERGE_FASTCULL );
    ///
    ///   #if !defined( CLIENT_DLL )       // begins AFTER both AddEffects calls
    ///       SetCollisionGroup( COLLISION_GROUP_WEAPON );
    ///       SetBlocksLOS( false );
    ///   #endif
    /// </code>
    ///
    /// Both calls run on the client, for every wearable it creates — the local player's and every
    /// remote player's alike. That is why cosmetics are visible on other players in game, in a POV
    /// recording and in a SourceTV one, while the flag itself never travels: measured on a real
    /// match, **26 of 26 `CTFWearable` entities carry no <c>m_fEffects</c> at all**, against
    /// `CTFRocketLauncher` and `CWeaponMedigun` which do carry `EF_BONEMERGE` on the wire.
    ///
    /// **Derived from the table CHAIN, not from a list of class names.** A name list is a guess
    /// about a hierarchy that the demo already states: `CTFPowerupBottle` is a `CEconWearable`
    /// descendant, is parented, and carries no flag, so a hardcoded check for `CTFWearable` would
    /// have left three of them on the floor.
    ///
    /// **Iterative rather than recursive, because a send table is untrusted input** (D32). A demo
    /// can describe a cycle, and a recursive walk over one never returns — which would hang a map
    /// load rather than report a bad file.
    /// </remarks>
    public static bool BoneMergesItself(DemoSchema schema, string tableName)
    {
        ArgumentNullException.ThrowIfNull(schema);

        // **Two families, and both reach the same call.** A wearable follows its wearer from
        // `CEconWearable::Spawn`; a carried weapon follows its owner from
        // `CBaseCombatWeapon::Equip`. Both are shared code outside a server-only guard, and both
        // land on `FollowEntity(..., bBoneMerge: true)` — so both are set client-side and neither
        // needs to travel.
        return Inherits(schema, tableName, WearableTable)
            || Inherits(schema, tableName, CombatWeaponTable);
    }

    /// <summary>Whether a table is, or descends from, another.</summary>
    /// <param name="schema">The demo's send tables.</param>
    /// <param name="tableName">Where to start.</param>
    /// <param name="ancestor">The table being looked for.</param>
    /// <returns>Whether the chain reaches it.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **Inheritance in the send-table format is an embedded DataTable property**, which is why
    /// this walks properties rather than a base-class field: a table that derives from another
    /// carries it as a <see cref="SendPropType.DataTable"/> whose
    /// <see cref="SendProperty.ReferencedTable"/> names the parent. `SchemaFlattener` walks the
    /// same edges for the same reason.
    /// </remarks>
    public static bool Inherits(DemoSchema schema, string tableName, string ancestor)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(tableName);
        ArgumentNullException.ThrowIfNull(ancestor);

        // Seen-set rather than a depth limit: a cycle is the failure being guarded against, and a
        // limit would also refuse a legitimately deep hierarchy.
        HashSet<string> seen = new(StringComparer.Ordinal);
        Stack<string> pending = new();

        pending.Push(tableName);

        while (pending.Count > 0)
        {
            string current = pending.Pop();

            if (string.Equals(current, ancestor, StringComparison.Ordinal))
            {
                return true;
            }

            if (!seen.Add(current) || schema.FindTable(current) is not { } table)
            {
                // A table the schema does not define is not an error: a demo names what it names,
                // and an unknown ancestor simply cannot be reached through it.
                continue;
            }

            foreach (SendProperty property in table.Properties)
            {
                if (property.Type == SendPropType.DataTable &&
                    property.ReferencedTable.Length > 0)
                {
                    pending.Push(property.ReferencedTable);
                }
            }
        }

        return false;
    }
}
