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
            FrozenFlag = timeline.FrozenFlag,
            Players = players,
            Names = Names(timeline),
            AccountIds = AccountIds(timeline),

            // The server's clock, which a timer's end time is on: the last `net_Tick`.
            ServerTime = (timeline.ServerTickAt(tick) ?? tick) * interval,
            RoundState = timeline.RoundStateAt(tick),
            RoundTimers = timeline.RoundTimersAt(tick),
            Teams = timeline.TeamsAt(tick),
            Buildings = timeline.BuildingsAt(tick),
            IdEntities = timeline.IdEntitiesAt(tick),
            ScoreboardPlayers = timeline.ScoreboardPlayersAt(tick),
            IsHltv = !timeline.HasRecordedView,

            // Replicated cvars: what the server sent, or Valve's declared default (FCVAR_REPLICATED, iconvar.h).
            ConVars = new HudConVars(timeline.ServerConVars.Value),
            KeyLookupBinding = bindings is null ? null : command => KeyLookupBinding(bindings, command),
        };
    }

    /// <summary>The state with every player where the frame drew him — his `GetAbsOrigin()` — and all else as newest recorded (B531).</summary>
    /// <param name="state">The state, its players from the newest recorded frame.</param>
    /// <param name="drawn">The frame's own sample of the players, or null when the frame is not of this tick.</param>
    /// <returns>The state with drawn positions.</returns>
    /// <remarks>
    /// **Only the position.** Health, conditions and the rest are not interpolated: a playing TF2 frame prints the health
    /// of the newest tick over a player drawn ten ticks behind it.
    /// </remarks>
    public static HudState WithDrawnPositions(HudState state, IReadOnlyList<ScenePlayer>? drawn)
    {
        if (drawn is null || state.Players is not { Count: > 0 } players)
        {
            return state;
        }

        ScenePlayer[] placed = new ScenePlayer[players.Count];

        for (int index = 0; index < placed.Length; index++)
        {
            placed[index] = players[index];

            foreach (ScenePlayer shown in drawn)
            {
                if (shown.EntityIndex == placed[index].EntityIndex)
                {
                    placed[index] = placed[index] with { X = shown.X, Y = shown.Y, Z = shown.Z };
                    break;
                }
            }
        }

        return state with { Players = placed };
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

    /// <summary>Each slot's `userinfo` `friendsID`, which `C_BasePlayer::GetSteamID` (c_baseplayer.cpp:2878) makes an account ID from.</summary>
    /// <remarks>**Interpolated** as <see cref="Names"/> is: a slot held in turn answers with the later player throughout.</remarks>
    private static Dictionary<int, uint> AccountIds(DemoTimeline timeline)
    {
        Dictionary<int, uint> accounts = [];

        foreach (Core.Net.PlayerInfo player in timeline.Roster.Values)
        {
            accounts[player.EntityIndex] = player.FriendsId;
        }

        return accounts;
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
            Conditions: local.Conditions,
            Flags: local.Flags ?? 0);
    }

    /// <summary>What the HUD reads at a tick: `CHud::IsHidden`'s state, the view's settings, and its camera.</summary>
    /// <param name="view">What the window knows that the HUD reads.</param>
    /// <param name="playersShown">The players where the frame drew them at this tick, or null when it drew another.</param>
    /// <param name="worldToScreen">`engine->WorldToScreenMatrix()`.</param>
    /// <returns>The state, before the target ID's traces.</returns>
    /// <remarks>**Moved out of `MainForm` whole** (D214, thin view slice 5), where it was `HudStateAt`.</remarks>
    public static HudState At(in HudView view, IReadOnlyList<ScenePlayer>? playersShown, float[] worldToScreen)
    {
        int hudTick = view.Tick;

        // The HUD's `GetFOV()` is the local player's. On a POV demo that is the view's own; on SourceTV it is the view's in
        // eye, where `GetFOV` follows the HLTV camera's target.
        // **Interpolated:** SourceTV out of eye gives the view's too, where the engine asks the SourceTV client's own.
        // Players where the frame drew them (B531): what a HUD element standing on one — the floating health icon — asks.
        HudState hudState = HudStates.WithDrawnPositions(
            HudStates.For(view.Timeline, hudTick, view.Scripts, view.Hooks, view.Bindings), playersShown);

        hudState = hudState with
        {
            ConVars = hudState.ConVars with { Client = view.ClientConVars },
            RealTime = view.RealTime,
            Fov = view.Fov,

            // `engine->WorldToScreenMatrix()`, which `GetVectorInHudSpace` projects through.
            WorldToScreen = worldToScreen,
        };

        // **On SourceTV the local player's observer mode and target are the HLTV camera's** (`C_BasePlayer::GetObserverMode`,
        // c_baseplayer.cpp:645): this viewer's camera — first person is in-eye, the shoulder camera chase, free roaming.
        if (view.Timeline is { HasRecordedView: false })
        {
            hudState = hudState with
            {
                ObserverMode = view.Mode switch
                {
                    CameraMode.FirstPerson => Core.Scene.ObserverModes.InEye,
                    CameraMode.ThirdPerson => Core.Scene.ObserverModes.Chase,
                    _ => Core.Scene.ObserverModes.Roaming,
                },
                ObserverTarget = view.Mode is CameraMode.FirstPerson or CameraMode.ThirdPerson ? view.Followed ?? 0 : 0,
            };
        }

        return hudState;
    }
}

/// <summary>What the window knows that the HUD reads at a tick: its clock, its camera, its bindings and convars.</summary>
/// <param name="Timeline">The loaded demo, or null.</param>
/// <param name="Tick">The HUD's tick.</param>
/// <param name="Scripts">The weapon and class scripts, or null where no install is open.</param>
/// <param name="Hooks">The attribute hooks, or null likewise.</param>
/// <param name="Bindings">The viewer's key bindings, or null.</param>
/// <param name="ClientConVars">The client's convars by name, or null.</param>
/// <param name="RealTime">`gpGlobals->realtime`: the window's own clock.</param>
/// <param name="Fov">The local player's `GetFOV()`.</param>
/// <param name="Mode">The camera the view is drawn through.</param>
/// <param name="Followed">The player that camera follows, or null.</param>
public readonly record struct HudView(
    DemoTimeline? Timeline,
    int Tick,
    TfWeaponData? Scripts,
    AttributeHooks? Hooks,
    KeyBindings? Bindings,
    Func<string, string?>? ClientConVars,
    float RealTime,
    float Fov,
    CameraMode Mode,
    int? Followed);
