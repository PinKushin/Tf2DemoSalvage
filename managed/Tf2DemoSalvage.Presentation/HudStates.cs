using System;

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
    /// `GetMaxBuffedHealth` (tf_player_shared.cpp:2235) the resource's `m_iMaxBuffedHealth` × `tf_max_health_boost`
    /// (1.5, `FCVAR_DEVELOPMENTONLY`, so fixed on retail) floored to a 5. `curtime` is the tick times the interval.
    /// </remarks>
    /// <param name="scripts">The weapon and class scripts, or null where no install is open.</param>
    /// <param name="hooks">The attribute hooks, or null likewise.</param>
    public static HudState For(DemoTimeline? timeline, int tick, TfWeaponData? scripts = null, AttributeHooks? hooks = null)
    {
        if (timeline is null)
        {
            return default;
        }

        if (timeline.RecorderEntityIndex is not { } recorder)
        {
            return new HudState(InGame: true, HasLocalPlayer: false, HideHud: 0, Health: 0, Alive: false);
        }

        foreach (ScenePlayer player in timeline.PlayersAt(tick))
        {
            if (player.EntityIndex == recorder)
            {
                return From(player, tick, scripts, hooks);
            }
        }

        return new HudState(InGame: true, HasLocalPlayer: false, HideHud: 0, Health: 0, Alive: false);
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
        int buffing = local.MaxHealthForBuffing ?? 1;

        return new HudState(
            InGame: true,
            HasLocalPlayer: true,
            HideHud: local.HideHud ?? 0,
            Health: local.EntityHealth ?? 0,
            Alive: (local.LifeState ?? 0) == 0,
            MaxHealth: local.MaxHealth ?? 1,
            MaxBuffedHealth: (int)MathF.Floor(buffing * 1.5f / 5f) * 5,
            CurTime: (float)(tick * ScenePropTrack.Tf2TickInterval),
            Team: local.Team ?? 0,
            Ammo: scripts is not null && hooks is not null ? TfAmmo.For(local, scripts, hooks) : default,
            ActiveWeapon: local.ActiveWeapon ?? 0);
    }
}
