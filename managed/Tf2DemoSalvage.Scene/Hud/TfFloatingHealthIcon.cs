using System;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>`CFloatingHealthIcon` (game/client/tf/tf_hud_target_id.cpp:1386): the health cross drawn over a target's head.</summary>
/// <remarks>
/// A viewport child named "HealthIcon", 128 square until `resource/UI/HealthIconPanel.res` says otherwise, holding its own
/// `CTFSpectatorGUIHealth`. `OnTick`, on a 50 ms tick signal, refreshes the health; `Paint` places it above the target's
/// hull. `CTargetID` makes one, shows it, hides it and deletes it (<see cref="TfTargetId"/>).
/// **Interpolated:** the tick runs from the owning target ID's think rather than vgui's tick list, and a player's hull top
/// ignores `m_flModelScale`, which this project does not decode.
/// </remarks>
public sealed class TfFloatingHealthIcon : VguiEditablePanel
{
    private const float TickSeconds = 0.05f;

    // `VEC_HULL_MAX` z: TF's `g_TFViewVectors` hull (tf_gamerules.cpp), before `m_flModelScale`.
    private const float HullMaxZ = 82f;

    private readonly Func<HudState, ScenePlayer?, bool> _healthBarVisible;
    private int _prevHealth = -1;
    private float _nextTick;

    /// <summary>`CFloatingHealthIcon( parent, name )` then `SetEntity`, as `AddFloatingHealthIcon` (:1434) makes one.</summary>
    /// <param name="viewport">`g_pClientMode->GetViewport()`.</param>
    /// <param name="entity">The target's entity index.</param>
    /// <param name="miniBoss">Whether the target is a mini-boss, which draws as a building (:1426).</param>
    /// <param name="healthBarVisible">`ShouldHealthBarBeVisible( m_hEntity, pLocalTFPlayer )`.</param>
    /// <param name="realTime">Now: `AddTickSignal( GetVPanel(), 50 )` first fires 50 ms on.</param>
    public TfFloatingHealthIcon(VguiPanel viewport, int entity, bool miniBoss, Func<HudState, ScenePlayer?, bool> healthBarVisible, float realTime)
        : base(viewport, "HealthIcon")
    {
        ArgumentNullException.ThrowIfNull(healthBarVisible);

        _healthBarVisible = healthBarVisible;
        _nextTick = realTime + TickSeconds;
        Visible = false;
        (X, Y, Wide, Tall) = (0, 0, 128, 128);
        TargetHealth = new TfSpectatorGuiHealth(this, "SpectatorGUIHealth");

        // `SetEntity` (:1411).
        Entity = entity;
        TargetHealth.AllowAnimations = false;
        TargetHealth.HideHealthBonusImage();
        TargetHealth.Building = miniBoss;
    }

    /// <summary>`m_hEntity`.</summary>
    public int Entity { get; }

    /// <summary>`m_pTargetHealth`.</summary>
    public TfSpectatorGuiHealth TargetHealth { get; }

    /// <inheritdoc/>
    /// <remarks>`ApplySchemeSettings` (:1446): its `.res`, then hidden.</remarks>
    public override void ApplySchemeSettings(VguiContext context)
    {
        base.ApplySchemeSettings(context);
        LoadControlSettings("resource/UI/HealthIconPanel.res", context);
        Visible = false;
    }

    /// <summary>The 50 ms tick signal: `OnTick` when one is due.</summary>
    /// <param name="state">The frame.</param>
    public void Tick(HudState state)
    {
        if (state.RealTime < _nextTick)
        {
            return;
        }

        _nextTick = state.RealTime + TickSeconds;
        OnTick(state);
    }

    /// <summary>`CFloatingHealthIcon::IsVisible` (:1547): hidden to an observer and under the match summary, whatever the flag.</summary>
    /// <param name="state">The frame.</param>
    /// <returns>Whether it counts as visible.</returns>
    public bool IsVisibleNow(HudState state) => state.ObserverMode <= ObserverModes.None && !state.Rules.ShowMatchSummary && Visible;

    /// <summary>`SetVisible` (:1535): showing it places it first.</summary>
    /// <param name="state">The frame.</param>
    /// <param name="visible">Whether to show it.</param>
    public void SetVisible(HudState state, bool visible)
    {
        if (visible)
        {
            CalculatePosition(state);
        }

        Visible = visible;
    }

    /// <summary>`CalculatePosition` (:1510): centred above the target's hull, `tf_healthicon_height_offset` (10) higher.</summary>
    /// <param name="state">The frame.</param>
    /// <returns>Whether the target could be placed.</returns>
    public bool CalculatePosition(HudState state)
    {
        if (!state.HasLocalPlayer || state.Player(Entity) is not { } target || state.WorldToScreen is not { } matrix)
        {
            return false;
        }

        // `GetHealthBarHeightOffset()` is the base's 0 (c_baseentity.h:521).
        (int x, int y) = GetVectorInHudSpace(matrix, target.X, target.Y, target.Z + HullMaxZ + 10f, ScreenWide(), ScreenTall());

        (X, Y) = (x - (Wide / 2), y - Tall);
        return true;
    }

    /// <inheritdoc/>
    /// <remarks>`Paint` (:1501): drawn only where it could be placed.</remarks>
    public override void Paint(IVguiSurface surface, VguiContext context)
    {
        if (HudViewport.Of(this) is not { } viewport || !CalculatePosition(viewport.State))
        {
            return;
        }

        base.Paint(surface, context);
    }

    /// <summary>`GetVectorInHudSpace` (cdll_util.cpp:552) through `FrustumTransform` (view_scene.cpp:55).</summary>
    private static (int X, int Y) GetVectorInHudSpace(float[] m, float x, float y, float z, int wide, int tall)
    {
        float screenX = (x * m[0]) + (y * m[4]) + (z * m[8]) + m[12];
        float screenY = (x * m[1]) + (y * m[5]) + (z * m[9]) + m[13];
        float w = (x * m[3]) + (y * m[7]) + (z * m[11]) + m[15];

        if (w < 0.001f)
        {
            // "behind": pushed far off screen.
            screenX *= 100000f;
            screenY *= 100000f;
        }
        else
        {
            float invW = 1f / w;

            screenX *= invW;
            screenY *= invW;
        }

        return ((int)(0.5f * (1f + screenX) * wide), (int)(0.5f * (1f - screenY) * tall));
    }

    /// <summary>`OnTick` (:1457).</summary>
    private void OnTick(HudState state)
    {
        ScenePlayer? target = state.Player(Entity);

        if (!_healthBarVisible(state, target) || target is not { } player)
        {
            SetVisible(state, false);
            return;
        }

        if (player.Conditions.IsStealthed)
        {
            SetVisible(state, false);
            return;
        }

        // "Defaults for all entities": `GetMaxHealth()` for both the max and the buffed max. Valve holds them in floats; every
        // one is a whole number of health, so ints compare them exactly.
        int health = player.EntityHealth ?? 0;
        int maxHealth = player.MaxHealth ?? 1;
        int maxBuffedHealth = player.MaxHealth ?? 1;

        if (player.Conditions.Has(PlayerConditions.Disguised) && player.IsEnemy)
        {
            int disguiseMax = HudViewport.Of(this)?.Scripts?.ClassMaxHealth(player.DisguiseClass ?? 0) ?? player.MaxHealth ?? 1;

            health = player.DisguiseHealth ?? 0;
            maxHealth = disguiseMax;
            maxBuffedHealth = HudState.GetMaxBuffedHealth(disguiseMax, disguiseMax, health);
        }

        if (health != _prevHealth)
        {
            TargetHealth.SetHealth(health, maxHealth, maxBuffedHealth);
            _prevHealth = health;
        }
    }

    private int ScreenWide() => HudViewport.Of(this)?.Context?.ScreenWide ?? 640;

    private int ScreenTall() => HudViewport.Of(this)?.Context?.ScreenTall ?? 480;
}
