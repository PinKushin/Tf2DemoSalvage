using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>`CHudTFCrosshair` over `CHudCrosshair` (game/client/tf/tf_hud_crosshair.cpp, game/client/hud_crosshair.cpp).</summary>
/// <remarks>
/// Hidden by `HIDEHUD_PLAYERDEAD | HIDEHUD_CROSSHAIR`, sized to the screen, and drawn at its centre — the observer-mode-none
/// offset arms are VR and Sixense, which a demo has neither of. The icon is whatever <see cref="TfHudWeapon"/> set last;
/// `cl_crosshair_file` replaces it with `vgui/crosshairs/NAME` drawn 32 × 32 about the centre, twice over as written
/// (`DrawTexturedRect( iX-iWidth, … iX+iWidth … )`, :215). Colour and scale are `cl_crosshair_red/green/blue` and
/// `cl_crosshair_scale` / 32, times the weapon's <see cref="WeaponCrosshairScale"/>.
/// **Not modelled, and why:** `IsDrawingLoadingImage`, `IsPaused`, `IsInVGuiInputMode` and the view-entity test are the
/// watching client's own state, not the recording's; `IsCurrentViewAccessAllowed` is a split-screen guard.
/// </remarks>
public sealed class TfHudCrosshair : VguiPanel, IHudElement
{
    private const int ConditionTaunting = 7;
    private const int ConditionGhostMode = PlayerConditions.HalloweenGhostMode;
    private const int ConditionZoomed = 1;
    private const int ClassSniper = 2;
    private const int CustomCrosshairSize = 32;

    private int _screenWide = 640;
    private int _screenTall = 480;

    /// <summary>`CHudCrosshair( pElementName )`: "HudCrosshair", parented to the viewport.</summary>
    /// <param name="viewport">The viewport.</param>
    public TfHudCrosshair(VguiPanel viewport)
        : base(viewport, "HudCrosshair")
    {
    }

    /// <summary>The crosshair cvars.</summary>
    public CrosshairSettings Settings { get; set; } = new();

    /// <summary>`m_pCrosshair`: the icon to draw, or null before a weapon has set one.</summary>
    public HudTexture? Crosshair { get; set; }

    /// <summary>`m_pDefaultCrosshair`: `crosshair_default`.</summary>
    public HudTexture? DefaultCrosshair { get; private set; }

    /// <inheritdoc/>
    public int HiddenBits => HudVisibility.HidePlayerDead | HudVisibility.HideCrosshair;

    /// <summary>`ClientModeTFNormal::ShouldDrawCrosshair` (clientmode_tf.cpp:601): not while a sniper is scoped with `tf_hud_no_crosshair_on_scope_zoom`.</summary>
    /// <param name="state">The local player.</param>
    /// <param name="settings">The cvars.</param>
    /// <returns>Whether the client mode lets a crosshair draw.</returns>
    public static bool ClientModeAllows(HudState state, CrosshairSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return state.HasLocalPlayer
            && !(state.PlayerClass == ClassSniper && state.Conditions.Has(ConditionZoomed) && settings.NoCrosshairOnScopeZoom != 0);
    }

    /// <summary>`CHudTFCrosshair::ShouldDraw` (tf_hud_crosshair.cpp:64), then `CHudCrosshair::ShouldDraw` (hud_crosshair.cpp:81).</summary>
    /// <param name="state">The local player.</param>
    /// <returns>Whether it draws.</returns>
    public bool ShouldDraw(HudState state)
    {
        // "turn off for the minigames" (:67), then `ShowMatchSummary()` (:70).
        if (state.Rules.ActiveMinigame || state.Rules.ShowMatchSummary)
        {
            return false;
        }

        if (!state.HasLocalPlayer || state.Conditions.Has(ConditionGhostMode) || state.Conditions.Has(ConditionTaunting))
        {
            return false;
        }

        // `m_flTimeToHideUntil > gpGlobals->curtime` (:84).
        if (TimeToHideUntil > state.CurTime)
        {
            return false;
        }

        if (state.WeaponClass is { } weapon && !TfHudWeapon.WeaponDrawsCrosshair(weapon, state, Settings, HudViewport.Of(this)?.Scripts))
        {
            return false;
        }

        // "draw a crosshair only if alive or spectating in eye"
        bool needsDraw = Crosshair is not null
            && Settings.Enabled != 0
            && ClientModeAllows(state, Settings)
            && (state.Flags & state.FrozenFlag) == 0
            && (state.Alive
                || state.ObserverMode == Core.Scene.ObserverModes.InEye
                || (Settings.Observer != 0 && state.ObserverMode == Core.Scene.ObserverModes.Roaming));

        return needsDraw && !HudVisibility.IsHidden(state, HiddenBits);
    }

    /// <inheritdoc/>
    public override void ApplySchemeSettings(VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        base.ApplySchemeSettings(context);
        DefaultCrosshair = HudViewport.Of(this)?.Icons?.GetIcon("crosshair_default");
        PaintBackgroundEnabled = false;
        (_screenWide, _screenTall) = (context.ScreenWide, context.ScreenTall);
        (Wide, Tall) = (_screenWide, _screenTall);
    }

    /// <summary>
    /// `pWeapon-&gt;GetWeaponCrosshairScale( flWeaponScale )` (hud_crosshair.cpp:252-259, tf_hud_crosshair.cpp:189-196): 1 from
    /// the base (basecombatweapon_shared.h:513) for every weapon but `CTFRevolver` and its `CTFRevolver_Secondary`, which with
    /// `CanHeadshot()` — `set_weapon_mode` 1 (tf_weapon_revolver.h:49) — remap the time since `m_flLastAccuracyCheck` from
    /// [1.0, 0.5] onto [0.75, 2.5] (tf_weapon_revolver.cpp:207-223).
    /// </summary>
    /// <remarks>
    /// **`m_flLastAccuracyCheck` is a prediction-only field** (:30), set to `curtime` in `PrimaryAttack` (:157) on the same
    /// call, at the same curtime, as `FireProjectile` sets the networked `m_flLastFireTime` (tf_weaponbase_gun.cpp:354);
    /// nothing else writes either, so the networked value stands for it. *Interpolated:* a shot prediction re-runs ahead of
    /// the last packet is not seen until the packet carries it, and the clock is the server's (`ServerTime`) where the
    /// engine uses `GetFinalPredictedTime() + interpolation_amount * TICK_INTERVAL`.
    /// </remarks>
    /// <param name="state">The local player.</param>
    /// <param name="weaponAttribute">`CALL_ATTRIB_HOOK` on a weapon, or null where no schema is open — then no headshot mode.</param>
    /// <returns>The weapon's scale.</returns>
    public static float WeaponCrosshairScale(HudState state, Func<ScenePlayer, SceneItem, string, float, float>? weaponAttribute)
    {
        if (state.Player(state.LocalIndex) is not { } owner)
        {
            return 1f;
        }

        SceneItem? weapon = null;

        foreach (SceneItem item in owner.Items ?? [])
        {
            if (item.EntityIndex == owner.ActiveWeapon)
            {
                weapon = item;
            }
        }

        if (weapon is not { ClassName: "CTFRevolver" or "CTFRevolver_Secondary" } revolver || weaponAttribute is null
            || AttributeHooks.RoundFloatToInt(weaponAttribute(owner, revolver, "set_weapon_mode", 0f)) != 1)
        {
            return 1f;
        }

        // RemapValClamped( flTimeSinceCheck, 1.0f, 0.5f, 0.75f, 2.5f ) (mathlib.h).
        float fraction = Math.Clamp((state.ServerTime - revolver.LastFireTime - 1f) / (0.5f - 1f), 0f, 1f);

        return 0.75f + ((2.5f - 0.75f) * fraction);
    }

    /// <summary>The events the constructor listens for (tf_hud_crosshair.cpp:46).</summary>
    public static IReadOnlySet<string> ListensFor { get; } = new HashSet<string>(["restart_timer_time"], StringComparer.Ordinal);

    /// <summary>`FireGameEvent` (tf_hud_crosshair.cpp:122).</summary>
    /// <param name="fired">The event.</param>
    public void HandleGameEvent(HudGameEvent fired)
    {
        ArgumentNullException.ThrowIfNull(fired);

        // "restart_timer_time" in a competitive-mode match, 1 to 10 seconds: hidden until it runs out (:124-133).
        if (fired.Event.Name == "restart_timer_time" && TfMatchGroupDescription.IsCompetitiveMode(fired.Rules.MatchGroup)
            && fired.Event.GetInt("time") is <= 10 and > 0 and var time)
        {
            TimeToHideUntil = fired.CurTime + time;
            return;
        }

        TimeToHideUntil = -1f;
    }

    /// <summary>`LevelShutdown` (:93-104): the hide cleared — which a seek, restarting playback, is.</summary>
    public void LevelShutdown() => TimeToHideUntil = -1f;

    /// <summary>`m_flTimeToHideUntil`: -1 from the constructor (:44).</summary>
    public float TimeToHideUntil { get; private set; } = -1f;

    /// <summary>`ResetCrosshair`: the default icon.</summary>
    public void ResetCrosshair() => Crosshair = DefaultCrosshair;

    /// <inheritdoc/>
    /// <remarks>`CHudTFCrosshair::Paint` (tf_hud_crosshair.cpp:143), then `CHudCrosshair::Paint` (hud_crosshair.cpp:233).</remarks>
    public override void Paint(IVguiSurface surface, VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(surface);

        HudViewport? viewport = HudViewport.Of(this);

        if (viewport?.State is not { HasLocalPlayer: true } state)
        {
            return;
        }

        // `GetDrawPosition`: the middle of the full-screen viewport.
        float x = _screenWide / 2f;
        float y = _screenTall / 2f;
        float playerScale = WeaponCrosshairScale(state, viewport.WeaponAttribute) * Settings.Scale / 32f;
        (byte, byte, byte, byte) color = ((byte)Settings.Red, (byte)Settings.Green, (byte)Settings.Blue, 255);
        int centreX = (int)(x + 0.5f);
        int centreY = (int)(y + 0.5f);

        if (Settings.File.Length > 0)
        {
            int customWide = (int)((playerScale * CustomCrosshairSize) + 0.5f);
            int customTall = (int)((playerScale * CustomCrosshairSize) + 0.5f);

            surface.DrawSetColor(color);
            surface.DrawSetTexture("vgui/crosshairs/" + Settings.File);
            surface.DrawTexturedRect(centreX - customWide, centreY - customTall, centreX + customWide, centreY + customTall);
            surface.DrawSetTexture(null);
            return;
        }

        if (Crosshair is not { } icon)
        {
            return;
        }

        int textureWide = icon.Width;
        int textureTall = icon.Height;
        int wide = (int)((playerScale * textureWide) + 0.5f);
        int tall = (int)((playerScale * textureTall) + 0.5f);

        icon.DrawSelfCropped(surface, centreX - (wide / 2), centreY - (tall / 2), 0, 0, textureWide, textureTall, wide, tall, color);
    }
}

/// <summary>`CHudWeapon` (game/client/hud_weapon.cpp): each paint, the active weapon picks the crosshair's icon.</summary>
/// <remarks>
/// Hidden by `HIDEHUD_WEAPONSELECTION`. `C_TFWeaponBase::Redraw` (tf_weaponbase.cpp:3650) calls `DrawCrosshair` when the weapon
/// and the client mode allow one; `C_BaseCombatWeapon::DrawCrosshair` (c_basecombatweapon.cpp:229) takes the script's
/// `crosshair` icon at a field of view of 90 or more and its `zoom` icon — the `crosshair` one when there is none — below, and
/// `crosshair_default` when the script names neither. No weapon resets it too.
/// </remarks>
public sealed class TfHudWeapon : VguiPanel, IHudElement
{
    // `ShouldDrawCrosshair` returns false outright for these (tf_weapon_invis.h:53, tf_weapon_pda.h:59, tf_weapon_decoy.h:43).
    private static readonly HashSet<string> NeverDrawCrosshair = new(StringComparer.Ordinal)
    {
        "CTFWeaponInvis", "CTFWeaponPDA", "CTFWeaponPDA_Engineer_Build", "CTFWeaponPDA_Engineer_Destroy", "CTFWeaponPDA_Spy", "CTFDecoy",
    };

    private TfHudCrosshair? _crosshair;

    /// <summary>`CHudWeapon( pElementName )`: "HudWeapon", parented to the viewport.</summary>
    /// <param name="viewport">The viewport.</param>
    public TfHudWeapon(VguiPanel viewport)
        : base(viewport, "HudWeapon")
    {
    }

    /// <inheritdoc/>
    public int HiddenBits => HudVisibility.HideWeaponSelection;

    /// <summary>`CTFWeaponBase::ShouldDrawCrosshair` (tf_weaponbase.cpp:3638): the script's `DrawCrosshair` unless `cl_crosshair_file` is set.</summary>
    /// <param name="weapon">The weapon's server class.</param>
    /// <param name="state">The local player, for the script's class.</param>
    /// <param name="settings">The cvars.</param>
    /// <param name="scripts">The weapon scripts, or null — then the script's default, true.</param>
    /// <returns>Whether the weapon draws a crosshair.</returns>
    public static bool WeaponDrawsCrosshair(string weapon, HudState state, CrosshairSettings settings, TfWeaponData? scripts)
    {
        ArgumentNullException.ThrowIfNull(weapon);
        ArgumentNullException.ThrowIfNull(settings);

        if (NeverDrawCrosshair.Contains(weapon))
        {
            return false;
        }

        return settings.File.Length > 0 || scripts is null || scripts.WeaponHud(weapon, state.PlayerClass).DrawCrosshair;
    }

    /// <inheritdoc/>
    public override void ApplySchemeSettings(VguiContext context)
    {
        base.ApplySchemeSettings(context);
        PaintBackgroundEnabled = false;

        // `m_pCrosshair = GET_HUDELEMENT( CHudCrosshair )`.
        _crosshair = Parent?.FindChildByName("HudCrosshair") as TfHudCrosshair;
    }

    /// <inheritdoc/>
    protected override void PerformLayout()
    {
        base.PerformLayout();

        if (Parent is { } parent)
        {
            (X, Y, Wide, Tall) = (0, 0, parent.Wide, parent.Tall);
        }
    }

    /// <inheritdoc/>
    public override void Paint(IVguiSurface surface, VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        HudViewport? viewport = HudViewport.Of(this);

        if (viewport?.State is not { HasLocalPlayer: true } state || _crosshair is null)
        {
            return;
        }

        if (state.WeaponClass is not { } weapon)
        {
            _crosshair.ResetCrosshair();
            return;
        }

        if (!WeaponDrawsCrosshair(weapon, state, _crosshair.Settings, viewport.Scripts) || !TfHudCrosshair.ClientModeAllows(state, _crosshair.Settings))
        {
            return;
        }

        HudTexture? crosshair = null;
        HudTexture? zoom = null;

        if (viewport.Scripts is { } scripts && viewport.Icons is { } icons)
        {
            foreach (HudTexture icon in scripts.WeaponHud(weapon, state.PlayerClass).Icons)
            {
                // `FindHudTextureInDict`: the first of each name.
                if (crosshair is null && string.Equals(icon.ShortName, "crosshair", StringComparison.OrdinalIgnoreCase))
                {
                    crosshair = icons.AddUnsearchable(icon, context);
                }
                else if (zoom is null && string.Equals(icon.ShortName, "zoom", StringComparison.OrdinalIgnoreCase))
                {
                    zoom = icons.AddUnsearchable(icon, context);
                }
            }
        }

        // "normal crosshairs" at 90 or more, "zoomed crosshairs" below; a missing zoom icon is the crosshair's.
        HudTexture? chosen = state.Fov >= 90f ? crosshair : zoom ?? crosshair;

        if (chosen is null)
        {
            _crosshair.ResetCrosshair();
        }
        else
        {
            _crosshair.Crosshair = chosen;
        }
    }
}
