using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Presentation;

/// <summary>What the HUD reads of a demo at one tick: `CHud::IsHidden`'s inputs and the local player's health.</summary>
public static class HudStates
{
    /// <summary>The state at a tick.</summary>
    /// <param name="timeline">The demo, or null when none is open.</param>
    /// <param name="tick">The tick.</param>
    /// <returns>The state.</returns>
    /// <remarks>
    /// `C_BasePlayer::GetLocalPlayer()` is the recorder on a POV demo and SourceTV's own client — a spectator with no
    /// health — otherwise. `GetHealth` is the player's own `m_iHealth`; `GetMaxHealth` the resource's `m_iMaxHealth`;
    /// `GetMaxBuffedHealth` is <see cref="HudState.GetMaxBuffedHealth"/> over the resource's `m_iMaxBuffedHealth`. `curtime`
    /// is the tick times the interval.
    /// </remarks>
    /// <param name="scripts">The weapon and class scripts, or null where no install is open.</param>
    /// <param name="hooks">The attribute hooks, or null likewise.</param>
    /// <param name="bindings">
    /// The viewer's key bindings, or null where none is open. `Key_LookupBinding( "+attack2" )` (tf_hud_target_id.cpp:1034)
    /// for the target ID's moveable sub-panel — see <see cref="HudState.KeyLookupBinding"/>.
    /// </param>
    public static HudState For(
        DemoTimeline? timeline, int tick, TfWeaponData? scripts = null, AttributeHooks? hooks = null, KeyBindings? bindings = null)
    {
        if (timeline is null)
        {
            return default;
        }

        HudState state = new(InGame: true, HasLocalPlayer: false, HideHud: 0, Health: 0, Alive: false);
        IReadOnlyList<ScenePlayer> players = timeline.PlayersAt(tick);

        if (timeline.RecorderEntityIndex is { } recorder)
        {
            foreach (ScenePlayer player in players)
            {
                if (player.EntityIndex == recorder)
                {
                    state = From(player, tick, scripts, hooks);
                    break;
                }
            }
        }

        // The recording server's own interval — the clock the event feed stamps notices with.
        float interval = timeline.IntervalPerTick > 0f ? timeline.IntervalPerTick : (float)ScenePropTrack.Tf2TickInterval;

        return state with
        {
            CurTime = tick * interval,
            Rules = timeline.RulesAt(tick),
            LocalIndex = timeline.RecorderEntityIndex ?? 0,
            Players = players,
            Names = Names(timeline),

            // The server's clock, which a timer's end time is on: the last `net_Tick`.
            ServerTime = (timeline.ServerTickAt(tick) ?? tick) * interval,
            RoundState = timeline.RoundStateAt(tick),
            RoundTimers = timeline.RoundTimersAt(tick),
            Teams = timeline.TeamsAt(tick),
            Buildings = timeline.BuildingsAt(tick),
            ScoreboardPlayers = timeline.ScoreboardPlayersAt(tick),

            // Replicated cvars: what the server sent, or Valve's declared default (FCVAR_REPLICATED, iconvar.h).
            ConVars = new HudConVars(timeline.ServerConVars.Value),
            KeyLookupBinding = bindings is null ? null : command => KeyLookupBinding(bindings, command),
        };
    }

    /// <summary>
    /// `engine->Key_LookupBinding( command )` over the viewer's own table (D101): the key bound to the command, or null
    /// when the command is not one the viewer names or nothing is bound to it.
    /// </summary>
    private static string? KeyLookupBinding(KeyBindings bindings, string command) =>
        KeyBindings.ActionOf(command) is { } action && bindings.KeyFor(action) is { Length: > 0 } key ? key : null;

    /// <summary>`GetPlayerName` by entity index: the `userinfo` name of whoever last held the slot.</summary>
    /// <remarks>
    /// **Interpolated:** the roster keeps every player by user id, and a slot two players held in turn answers with the
    /// later one throughout; the game names whoever holds it at the moment asked.
    /// </remarks>
    private static Dictionary<int, string> Names(DemoTimeline timeline)
    {
        Dictionary<int, string> names = [];

        foreach (Core.Net.PlayerInfo player in timeline.Roster.Values)
        {
            names[player.EntityIndex] = player.Name;
        }

        return names;
    }

    /// <summary>The state for a local player at a tick.</summary>
    /// <param name="local">The local player.</param>
    /// <param name="tick">The tick.</param>
    /// <returns>The state.</returns>
    /// <param name="scripts">The weapon and class scripts, or null — then no ammo is known.</param>
    /// <param name="hooks">The attribute hooks, or null likewise.</param>
    /// <remarks>An unsent maximum is `TF_HEALTH_UNDEFINED`, 1, as `GetArrayValue` answers.</remarks>
    public static HudState From(ScenePlayer local, int tick, TfWeaponData? scripts = null, AttributeHooks? hooks = null)
    {
        return new HudState(
            InGame: true,
            HasLocalPlayer: true,
            HideHud: local.HideHud ?? 0,
            Health: local.EntityHealth ?? 0,
            Alive: (local.LifeState ?? 0) == 0,
            MaxHealth: local.MaxHealth ?? 1,
            MaxBuffedHealth: HudState.GetMaxBuffedHealth(local.MaxHealthForBuffing ?? 1, local.MaxHealth ?? 1, local.EntityHealth ?? 0),
            CurTime: (float)(tick * ScenePropTrack.Tf2TickInterval),
            Team: local.Team ?? 0,
            Ammo: scripts is not null && hooks is not null ? TfAmmo.For(local, scripts, hooks) : default,
            ActiveWeapon: local.ActiveWeapon ?? 0,
            ObserverMode: local.ObserverMode ?? 0,
            ObserverTarget: local.ObserverTarget ?? 0,
            WeaponClass: local.WeaponClass,
            PlayerClass: local.PlayerClass ?? 0,
            Conditions: local.Conditions);
    }
}
