using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>
/// What the anim state reads of the installed game at decode time: the class scripts, and the model's crouch walk
/// through the whole of `TranslateActivity` — player table, weapon role, item (B437).
/// </summary>
/// <param name="classes">The class scripts and models.</param>
/// <param name="items">The item schema, for `anim_slot` and `animation_replacement`; null without one.</param>
/// <param name="readFile">Opens a game file, for the weapon scripts a role is read from.</param>
public sealed class ClassAnimation(PlayerClassModels classes, ItemSchema? items, Func<string, byte[]?> readFile)
    : IClassAnimationScripts
{
    /// <summary>Each weapon's role per holder, read from its script the first time it is asked.</summary>
    private readonly ConcurrentDictionary<(string Weapon, int? Class), WeaponRoles> _roles = new();

    /// <inheritdoc/>
    public ClassAnimationScript ScriptOf(int? playerClass) => classes.ScriptOf(playerClass);

    /// <inheritdoc/>
    /// <remarks>
    /// <code>
    /// translateActivity = ActivityOverride( translateActivity );                               // :128, player table
    /// if ( pWeapon ) {
    ///     translateActivity = pWeapon-&gt;ActivityOverride( translateActivity );                // :133, role's acttable
    ///     translateActivity = GetStaticData()-&gt;GetActivityOverride( GetTeamNumber(), … );     // :138, item
    /// }
    /// </code>
    /// No weapon, no role and no item. The winner's step (`:143-150`) rewrites only stands, never a crouch walk.
    /// </remarks>
    public bool HasCrouchWalk(
        int? playerClass, PlayerActivityOverride table, string? weaponClass, int? weaponItem, int team)
    {
        IReadOnlyDictionary<string, string>? itemOverride = weaponClass is not null && weaponItem is { } item
            ? items?.ActivityReplacements(item, team)
            : null;

        string crouchWalk = PlayerActivityTable.Translate(
            PlayerClassModels.CrouchWalk,
            weaponClass is null ? NoRole : RoleOf(weaponClass, playerClass, weaponItem),
            table,
            competitiveWinnerClass: null,
            itemOverride);

        return classes.DeclaresActivity(playerClass, crouchWalk);
    }

    /// <summary>A role no weapon table names, so its step changes nothing — `if ( pWeapon )` false.</summary>
    private const string NoRole = "";

    /// <summary>`GetActivityWeaponRole`: the script's type, then the item's `anim_slot` (<see cref="WeaponRoles"/>).</summary>
    private string RoleOf(string weaponClass, int? playerClass, int? weaponItem) =>
        _roles.GetOrAdd((weaponClass, playerClass), key => WeaponRoles.Read(readFile, [key]))
            .Suffix(weaponClass, playerClass, weaponItem is { } item && items is { } schema ? schema.AnimSlot(item) : -1);
}
