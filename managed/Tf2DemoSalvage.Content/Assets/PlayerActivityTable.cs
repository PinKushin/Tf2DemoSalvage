using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>
/// The player's own activity tables, and `CTFPlayerAnimState::TranslateActivity` whole (B437).
/// </summary>
/// <remarks>
/// **Transcribed from `tf_playeranimstate.cpp:157-221`**, and compared row for row against it by
/// <c>PlayerActivityTableConformanceTests</c>. Kept as rows rather than code for the same reason
/// <see cref="WeaponActivityTable"/> is: the conformance test can then read both directions.
/// </remarks>
public static class PlayerActivityTable
{
    private static readonly Dictionary<PlayerActivityOverride, Dictionary<string, string>> Tables = new()
    {
        [PlayerActivityOverride.KartState] = Rows(
            ("ACT_MP_STAND_IDLE", "ACT_KART_IDLE"),
            ("ACT_MP_RUN", "ACT_KART_IDLE"),
            ("ACT_MP_WALK", "ACT_KART_IDLE"),
            ("ACT_MP_AIRWALK", "ACT_KART_IDLE"),
            ("ACT_MP_ATTACK_STAND_PRIMARYFIRE", "ACT_KART_ACTION_SHOOT"),
            ("ACT_MP_ATTACK_AIRWALK_PRIMARYFIRE", "ACT_KART_ACTION_SHOOT"),
            ("ACT_MP_JUMP_START", "ACT_KART_JUMP_START"),
            ("ACT_MP_JUMP_FLOAT", "ACT_KART_JUMP_FLOAT"),
            ("ACT_MP_JUMP_LAND", "ACT_KART_JUMP_LAND")),

        [PlayerActivityOverride.LoserState] = Rows(
            ("ACT_MP_STAND_IDLE", "ACT_MP_STAND_LOSERSTATE"),
            ("ACT_MP_CROUCH_IDLE", "ACT_MP_CROUCH_LOSERSTATE"),
            ("ACT_MP_RUN", "ACT_MP_RUN_LOSERSTATE"),
            ("ACT_MP_WALK", "ACT_MP_WALK_LOSERSTATE"),
            ("ACT_MP_AIRWALK", "ACT_MP_AIRWALK_LOSERSTATE"),
            ("ACT_MP_CROUCHWALK", "ACT_MP_CROUCHWALK_LOSERSTATE"),
            ("ACT_MP_JUMP", "ACT_MP_JUMP_LOSERSTATE"),
            ("ACT_MP_JUMP_START", "ACT_MP_JUMP_START_LOSERSTATE"),
            ("ACT_MP_JUMP_FLOAT", "ACT_MP_JUMP_FLOAT_LOSERSTATE"),
            ("ACT_MP_JUMP_LAND", "ACT_MP_JUMP_LAND_LOSERSTATE"),
            ("ACT_MP_SWIM", "ACT_MP_SWIM_LOSERSTATE"),
            ("ACT_MP_DOUBLEJUMP_CROUCH", "ACT_MP_DOUBLEJUMP_CROUCH_LOSERSTATE")),

        [PlayerActivityOverride.CompetitiveLoserState] = Rows(
            ("ACT_MP_STAND_IDLE", "ACT_MP_COMPETITIVE_LOSERSTATE")),

        [PlayerActivityOverride.BuildingDeployed] = Rows(
            ("ACT_MP_STAND_IDLE", "ACT_MP_STAND_BUILDING_DEPLOYED"),
            ("ACT_MP_CROUCH_IDLE", "ACT_MP_CROUCH_BUILDING_DEPLOYED"),
            ("ACT_MP_RUN", "ACT_MP_RUN_BUILDING_DEPLOYED"),
            ("ACT_MP_WALK", "ACT_MP_WALK_BUILDING_DEPLOYED"),
            ("ACT_MP_AIRWALK", "ACT_MP_AIRWALK_BUILDING_DEPLOYED"),
            ("ACT_MP_CROUCHWALK", "ACT_MP_CROUCHWALK_BUILDING_DEPLOYED"),
            ("ACT_MP_JUMP", "ACT_MP_JUMP_BUILDING_DEPLOYED"),
            ("ACT_MP_JUMP_START", "ACT_MP_JUMP_START_BUILDING_DEPLOYED"),
            ("ACT_MP_JUMP_FLOAT", "ACT_MP_JUMP_FLOAT_BUILDING_DEPLOYED"),
            ("ACT_MP_JUMP_LAND", "ACT_MP_JUMP_LAND_BUILDING_DEPLOYED"),
            ("ACT_MP_SWIM", "ACT_MP_SWIM_BUILDING_DEPLOYED"),
            ("ACT_MP_ATTACK_STAND_PRIMARYFIRE", "ACT_MP_ATTACK_STAND_BUILDING_DEPLOYED"),
            ("ACT_MP_ATTACK_CROUCH_PRIMARYFIRE", "ACT_MP_ATTACK_CROUCH_BUILDING_DEPLOYED"),
            ("ACT_MP_ATTACK_SWIM_PRIMARYFIRE", "ACT_MP_ATTACK_SWIM_BUILDING_DEPLOYED"),
            ("ACT_MP_ATTACK_AIRWALK_PRIMARYFIRE", "ACT_MP_ATTACK_AIRWALK_BUILDING_DEPLOYED"),
            ("ACT_MP_ATTACK_STAND_GRENADE", "ACT_MP_ATTACK_STAND_GRENADE_BUILDING_DEPLOYED"),
            ("ACT_MP_ATTACK_CROUCH_GRENADE", "ACT_MP_ATTACK_STAND_GRENADE_BUILDING_DEPLOYED"),
            ("ACT_MP_ATTACK_SWIM_GRENADE", "ACT_MP_ATTACK_STAND_GRENADE_BUILDING_DEPLOYED"),
            ("ACT_MP_ATTACK_AIRWALK_GRENADE", "ACT_MP_ATTACK_STAND_GRENADE_BUILDING_DEPLOYED"),
            ("ACT_MP_GESTURE_VC_HANDMOUTH", "ACT_MP_GESTURE_VC_HANDMOUTH_BUILDING"),
            ("ACT_MP_GESTURE_VC_FINGERPOINT", "ACT_MP_GESTURE_VC_FINGERPOINT_BUILDING"),
            ("ACT_MP_GESTURE_VC_FISTPUMP", "ACT_MP_GESTURE_VC_FISTPUMP_BUILDING"),
            ("ACT_MP_GESTURE_VC_THUMBSUP", "ACT_MP_GESTURE_VC_THUMBSUP_BUILDING"),
            ("ACT_MP_GESTURE_VC_NODYES", "ACT_MP_GESTURE_VC_NODYES_BUILDING"),
            ("ACT_MP_GESTURE_VC_NODNO", "ACT_MP_GESTURE_VC_NODNO_BUILDING")),
    };

    /// <summary>The winner's stand, `ACT_MP_COMPETITIVE_WINNERSTATE`.</summary>
    private const string WinnerState = "ACT_MP_COMPETITIVE_WINNERSTATE";

    /// <summary>`TF_CLASS_DEMOMAN`.</summary>
    private const int Demoman = 4;

    /// <summary>`TF_CLASS_SPY`.</summary>
    private const int Spy = 8;

    /// <summary>One table's rows, for the conformance test.</summary>
    /// <param name="table">Which table.</param>
    /// <returns>Its rows, or none.</returns>
    public static IReadOnlyDictionary<string, string> For(PlayerActivityOverride table) =>
        Tables.TryGetValue(table, out Dictionary<string, string>? rows)
            ? rows
            : new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>`CTFPlayerAnimState::TranslateActivity` (`tf_playeranimstate.cpp:124-153`), past the swim.</summary>
    /// <param name="activity">The generic activity the anim state asked for.</param>
    /// <param name="role">The held weapon's role, as <see cref="WeaponActivityTable"/> keys it.</param>
    /// <param name="table">The player's own table.</param>
    /// <param name="competitiveWinnerClass">The class of a player under `TF_COND_COMPETITIVE_WINNER`, else null.</param>
    /// <param name="item">
    /// The held item's `animation_replacement` rows for the team (<see cref="ItemSchema.ActivityReplacements"/>), or null.
    /// </param>
    /// <returns>The name the model is asked for.</returns>
    /// <remarks>
    /// **The player's table, then the weapon's, then the item's `animation_replacement`, then the winner's stand**,
    /// the engine's order (`:126-151`).
    /// </remarks>
    public static string Translate(
        string activity,
        string role,
        PlayerActivityOverride table,
        int? competitiveWinnerClass,
        IReadOnlyDictionary<string, string>? item = null)
    {
        ArgumentNullException.ThrowIfNull(activity);

        string translated = For(table).TryGetValue(activity, out string? player) ? player : activity;

        translated = WeaponActivityTable.Override(role, translated);

        if (item is not null && item.TryGetValue(translated, out string? replaced))
        {
            translated = replaced;
        }

        return competitiveWinnerClass is { } winner && IsWinnerStand(winner, translated) ? WinnerState : translated;
    }

    /// <summary>The stands `TranslateActivity` swaps for the winner's: any class's primary, a spy's melee, a demoman's secondary.</summary>
    private static bool IsWinnerStand(int winner, string translated) =>
        translated == "ACT_MP_STAND_PRIMARY" ||
        (winner == Spy && translated == "ACT_MP_STAND_MELEE") ||
        (winner == Demoman && translated == "ACT_MP_STAND_SECONDARY");

    private static Dictionary<string, string> Rows(params (string From, string To)[] rows)
    {
        Dictionary<string, string> table = new(StringComparer.Ordinal);

        foreach ((string from, string to) in rows)
        {
            _ = table.TryAdd(from, to);
        }

        return table;
    }
}
