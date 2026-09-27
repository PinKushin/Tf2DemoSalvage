using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>`CTFHealthPanel` (game/client/tf/tf_hud_playerstatus.cpp:567): the health cross, filled from the bottom.</summary>
/// <remarks>
/// `Paint` (:589): at no health, `hud/health_dead` over the whole panel in white; otherwise `hud/health_color` from
/// <c>tall × (1 − health)</c> down, its texture coordinates from <c>1 − health</c>, in the panel's foreground colour.
/// Over 1 — overheal — the quad starts above the panel and the clip cuts it, as in the game.
/// </remarks>
/// <param name="parent">The parent.</param>
/// <param name="name">The name.</param>
public sealed class TfHealthPanel(VguiPanel? parent, string? name) : VguiPanel(parent, name)
{
    /// <summary>`m_flHealth`: health over maximum, 1 until set.</summary>
    public float Health { get; set; } = 1f;

    /// <inheritdoc/>
    public override void Paint(IVguiSurface surface, VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(surface);

        if (Health <= 0f)
        {
            surface.DrawSetTexture("hud/health_dead");
            surface.DrawSetColor((255, 255, 255, 255));
            surface.DrawTexturedQuad(0, 0, Wide, Tall, 0f, 0f, 1f, 1f);
            return;
        }

        float damageY = Tall * (1f - Health);

        surface.DrawSetTexture("hud/health_color");
        surface.DrawSetColor(FgColor);
        surface.DrawTexturedQuad(0, damageY, Wide, Tall, 0f, 1f - Health, 1f, 1f);
    }
}

/// <summary>`CTFHudPlayerHealth` (tf_hud_playerstatus.cpp:639): the cross, its background, the numbers and the bonus glow.</summary>
/// <remarks>
/// `ApplySchemeSettings` (:714) loads `resource/UI/HudPlayerHealth.res` and keeps the bonus image's bounds. `OnThink`
/// (:942) runs `SetHealth` every 0.05 s of `curtime`. `SetHealth` (:736) sets the fraction, hides the backgrounds at no
/// health, grows the bonus image with overheal or with health under `HealthDeathWarning` of the maximum (then tinted
/// `HealthDeathWarningColor`, as is the cross), and sets `Health` and `MaxHealth` — the maximum only when at least 5 is
/// missing. Entering overheal starts `HudHealthBonusPulse`, entering the warning `HudHealthDyingPulse`, each stopping the
/// other; hiding the glow stops both. `OnThink` (:942) then turns the condition icons back on: `CTFBuffInfo::Update`
/// and the vaccinator/soldier-buff/rune/parachute table (`m_vecBuffInfo`, :663–688) each cycle's first active buff of
/// its class, then the six bleed/debuff panels by `SetPlayerHealthImagePanelVisibility` (:909). **Not modelled:** the
/// Halloween wheel of doom (`UpdateHalloweenStatus`, :1013 — no `TFGameRules()` in this reader) and the player level
/// (no source in the decoded state yet).
/// </remarks>
public class TfHudPlayerHealth : VguiEditablePanel
{
    private const string ResFile = "resource/UI/HudPlayerHealth.res";

    /// <summary>`TF_TEAM_BLUE` (tf_shareddefs.h), against `HudState.Team`.</summary>
    private const int TeamBlue = 3;

    // Condition numbers `tf_shareddefs.h` gives that `Core.Scene.PlayerConditions` does not yet name — cited by line so
    // a later addition there can replace these rather than duplicate them.
    private const int CondStunned = 15; // :705 — any stun; `m_pSlowedImage` regardless of `iStunFlags`.
    private const int CondOffenseBuff = 16; // :706
    private const int CondBleeding = 25; // :715
    private const int CondDefenseBuff = 26; // :716
    private const int CondMadMilk = 27; // :717
    private const int CondRegenOnDamageBuff = 29; // :719
    private const int CondMarkedForDeath = 30; // :720
    private const int CondMarkedForDeathSilent = 48; // :738
    private const int CondMedigunUberBulletResist = 58; // :748
    private const int CondMedigunUberBlastResist = 59; // :749
    private const int CondMedigunUberFireResist = 60; // :750
    private const int CondMedigunSmallBulletResist = 61; // :751
    private const int CondMedigunSmallBlastResist = 62; // :752
    private const int CondMedigunSmallFireResist = 63; // :753
    private const int CondParachuteActive = 80; // :770
    private const int CondRuneStrength = 90; // :780
    private const int CondRuneHaste = 91; // :781
    private const int CondRuneRegen = 92; // :782
    private const int CondRuneResist = 93; // :783
    private const int CondRuneVampire = 94; // :784
    private const int CondRuneReflect = 95; // :785
    private const int CondRunePrecision = 96; // :786
    private const int CondRuneAgility = 97; // :787
    private const int CondGrapplingHookBleeding = 101; // :791
    private const int CondRuneKnockout = 103; // :793
    private const int CondRuneKing = 109; // :799
    private const int CondRunePlague = 110; // :800
    private const int CondRuneSupernova = 111; // :801
    private const int CondPasstimePenaltyDebuff = 119; // :809
    private const int CondGas = 123; // :813

    /// <summary>`m_vecBuffInfo` (:663–688), in construction order — each row a `CTFBuffInfo`.</summary>
    /// <remarks>
    /// Index into <see cref="ConditionImages"/> starting at 8 (<c>PlayerStatus_MedicUberBulletResistImage</c>).
    /// <see cref="BuffClass"/> is `m_eClass`: two rows sharing one class (the vaccinator's uber/small pair) mean only
    /// the first found active draws — `OnThink`'s `m_vecActiveClasses` skip (:972).
    /// </remarks>
    private static readonly BuffInfo[] BuffInfos =
    [
        new(CondMedigunUberBulletResist, BuffClass.BulletResist, "../HUD/defense_buff_bullet_blue", "../HUD/defense_buff_bullet_red"),
        new(CondMedigunUberBlastResist, BuffClass.BlastResist, "../HUD/defense_buff_explosion_blue", "../HUD/defense_buff_explosion_red"),
        new(CondMedigunUberFireResist, BuffClass.FireResist, "../HUD/defense_buff_fire_blue", "../HUD/defense_buff_fire_red"),
        new(CondMedigunSmallBulletResist, BuffClass.BulletResist, "../HUD/defense_buff_bullet_blue", "../HUD/defense_buff_bullet_red"),
        new(CondMedigunSmallBlastResist, BuffClass.BlastResist, "../HUD/defense_buff_explosion_blue", "../HUD/defense_buff_explosion_red"),
        new(CondMedigunSmallFireResist, BuffClass.FireResist, "../HUD/defense_buff_fire_blue", "../HUD/defense_buff_fire_red"),
        new(CondOffenseBuff, BuffClass.SoldierOffense, "../Effects/soldier_buff_offense_blue", "../Effects/soldier_buff_offense_red"),
        new(CondDefenseBuff, BuffClass.SoldierDefense, "../Effects/soldier_buff_defense_blue", "../Effects/soldier_buff_defense_red"),
        new(CondRegenOnDamageBuff, BuffClass.SoldierHealOnHit, "../Effects/soldier_buff_healonhit_blue", "../Effects/soldier_buff_healonhit_red"),
        new(CondRuneStrength, BuffClass.RuneStrength, "../Effects/powerup_strength_hud", "../Effects/powerup_strength_hud"),
        new(CondRuneHaste, BuffClass.RuneHaste, "../Effects/powerup_haste_hud", "../Effects/powerup_haste_hud"),
        new(CondRuneRegen, BuffClass.RuneRegen, "../Effects/powerup_regen_hud", "../Effects/powerup_regen_hud"),
        new(CondRuneResist, BuffClass.RuneResist, "../Effects/powerup_resist_hud", "../Effects/powerup_resist_hud"),
        new(CondRuneVampire, BuffClass.RuneVampire, "../Effects/powerup_vampire_hud", "../Effects/powerup_vampire_hud"),
        new(CondRuneReflect, BuffClass.RuneReflect, "../Effects/powerup_reflect_hud", "../Effects/powerup_reflect_hud"),
        new(CondRunePrecision, BuffClass.RunePrecision, "../Effects/powerup_precision_hud", "../Effects/powerup_precision_hud"),
        new(CondRuneAgility, BuffClass.RuneAgility, "../Effects/powerup_agility_hud", "../Effects/powerup_agility_hud"),
        new(CondRuneKnockout, BuffClass.RuneKnockout, "../Effects/powerup_knockout_hud", "../Effects/powerup_knockout_hud"),
        new(CondRuneKing, BuffClass.RuneKing, "../Effects/powerup_king_hud", "../Effects/powerup_king_hud"),
        new(CondRunePlague, BuffClass.RunePlague, "../Effects/powerup_plague_hud", "../Effects/powerup_plague_hud"),
        new(CondRuneSupernova, BuffClass.RuneSupernova, "../Effects/powerup_supernova_hud", "../Effects/powerup_supernova_hud"),
        new(CondParachuteActive, BuffClass.Parachute, "../HUD/hud_parachute_active", "../HUD/hud_parachute_active"),
    ];

    /// <summary>Where <see cref="BuffInfos"/> starts in <see cref="ConditionImages"/> / <see cref="_conditionImages"/>.</summary>
    private const int BuffImagesOffset = 8;

    // The constructor's (:641–689) condition and buff images, in its order; every one is hidden at each think.
    private static readonly string[] ConditionImages =
    [
        "PlayerStatusBleedImage", "PlayerStatusHookBleedImage", "PlayerStatusMarkedForDeathImage",
        "PlayerStatusMarkedForDeathSilentImage", "PlayerStatusMilkImage", "PlayerStatusGasImage", "PlayerStatusSlowed",
        "PlayerStatus_WheelOfDoom",
        "PlayerStatus_MedicUberBulletResistImage", "PlayerStatus_MedicUberBlastResistImage", "PlayerStatus_MedicUberFireResistImage",
        "PlayerStatus_MedicSmallBulletResistImage", "PlayerStatus_MedicSmallBlastResistImage", "PlayerStatus_MedicSmallFireResistImage",
        "PlayerStatus_SoldierOffenseBuff", "PlayerStatus_SoldierDefenseBuff", "PlayerStatus_SoldierHealOnHitBuff",
        "PlayerStatus_RuneStrength", "PlayerStatus_RuneHaste", "PlayerStatus_RuneRegen", "PlayerStatus_RuneResist",
        "PlayerStatus_RuneVampire", "PlayerStatus_RuneReflect", "PlayerStatus_RunePrecision", "PlayerStatus_RuneAgility",
        "PlayerStatus_RuneKnockout", "PlayerStatus_RuneKing", "PlayerStatus_RunePlague", "PlayerStatus_RuneSupernova",
        "PlayerStatus_Parachute",
    ];

    private readonly VguiImagePanel _healthImageBg;
    private readonly VguiImagePanel _healthBonusImage;
    private readonly VguiImagePanel _buildingHealthImageBg;
    private readonly VguiImagePanel[] _conditionImages;
    private (int X, int Y, int Wide, int Tall) _bonusOrigin = (-1, -1, -1, -1);
    private float _nextThink;
    private float _lastCurTime;
    private int _health = -1;
    private AnimState _animState;
    private int _maxHealth;
    private TfExLabel? _playerLevel;

    /// <summary>`CTFHudPlayerHealth( parent, name )`: its children made up front, so the `.res` finds them by name.</summary>
    /// <param name="parent">The parent.</param>
    /// <param name="name">The name.</param>
    public TfHudPlayerHealth(VguiPanel? parent, string? name)
        : base(parent, name)
    {
        HealthImage = new TfHealthPanel(this, "PlayerStatusHealthImage");
        _healthImageBg = new VguiImagePanel(this, "PlayerStatusHealthImageBG");
        _healthBonusImage = new VguiImagePanel(this, "PlayerStatusHealthBonusImage");
        _buildingHealthImageBg = new VguiImagePanel(this, "BuildingStatusHealthImageBG");
        _conditionImages = Array.ConvertAll(ConditionImages, image => new VguiImagePanel(this, image));

        DeclareAnimationVar("HealthBonusPosAdj", VguiPanelVarType.Whole, "25");
        DeclareAnimationVar("HealthDeathWarning", VguiPanelVarType.Real, "0.49");
        DeclareAnimationVar("HealthDeathWarningColor", VguiPanelVarType.Color, "HUDDeathWarning");
    }

    /// <summary>`m_pHealthImage`.</summary>
    public TfHealthPanel HealthImage { get; }

    /// <summary>`m_pHealthBonusImage`.</summary>
    public VguiImagePanel HealthBonusImage => _healthBonusImage;

    /// <summary>`m_pHealthImageBG`.</summary>
    public VguiImagePanel HealthImageBackground => _healthImageBg;

    /// <summary>`m_bBuilding`: `SetBuilding` — the building background shown beside the cross, for a target ID of a building.</summary>
    public bool Building { get; set; }

    /// <summary>`GetResFilename`.</summary>
    protected virtual string ResFilename => ResFile;

    /// <inheritdoc/>
    public override void ApplySchemeSettings(VguiContext context)
    {
        LoadControlSettings(ResFilename, context);
        _bonusOrigin = (_healthBonusImage.X, _healthBonusImage.Y, _healthBonusImage.Wide, _healthBonusImage.Tall);
        _nextThink = 0f;
        base.ApplySchemeSettings(context);
        _buildingHealthImageBg.Visible = Building;
        _playerLevel = FindChildByName("PlayerStatusPlayerLevel") as TfExLabel;
    }

    /// <summary>`SetLevel` (:870): the level label shown with the number, or hidden below 0.</summary>
    /// <param name="level">The level, or -1.</param>
    public void SetLevel(int level)
    {
        if (_playerLevel is not { } label)
        {
            return;
        }

        if (level >= 0)
        {
            label.SetText(level.ToString(System.Globalization.CultureInfo.InvariantCulture), null);
        }

        label.Visible = level >= 0;
    }

    /// <summary>`OnThink`: every 0.05 s, the local player's health, and the condition icons off.</summary>
    protected override void OnThink()
    {
        if (HudViewport.Of(this) is { } viewport)
        {
            Think(viewport.State);
        }
    }

    /// <summary>`OnThink`'s body, given the state it reads.</summary>
    /// <param name="state">The game state.</param>
    public void Think(HudState state)
    {
        // A seek backwards restarts playback, which `ResetHUD` answers with `Reset` (:702) — TF2 cannot seek back at all.
        if (state.CurTime < _lastCurTime)
        {
            _nextThink = state.CurTime + 0.05f;
        }

        _lastCurTime = state.CurTime;

        if (_nextThink >= state.CurTime)
        {
            return;
        }

        if (state.HasLocalPlayer)
        {
            SetHealth(state.Health, state.MaxHealth, state.MaxBuffedHealth);

            foreach (VguiImagePanel image in _conditionImages)
            {
                image.Visible = false;
            }

            UpdateConditionIcons(state);
        }

        _nextThink = state.CurTime + 0.05f;
    }

    /// <summary>The second half of `OnThink` (:962–1004): the buff table, then the six bleed/debuff panels.</summary>
    /// <param name="state">The state, for <c>Conditions</c>, <c>Team</c> and <c>RealTime</c>.</param>
    private void UpdateConditionIcons(HudState state)
    {
        PlayerConditions cond = state.Conditions;
        bool blue = state.Team == TeamBlue;

        // `color_offset`/`color_fade` (:952): a 5-step cycle over wall-clock realtime, 0.1 s per step.
        int colorOffset = (int)(state.RealTime * 10f) % 5;
        byte colorFade = (byte)(160 + (colorOffset * 10));

        // "just above the health '+'", nudged over 25 (:955–959).
        int xOffset = HealthImage.X + 25;

        List<BuffClass> activeClasses = [];

        for (int i = 0; i < BuffInfos.Length; i++)
        {
            BuffInfo info = BuffInfos[i];

            if (activeClasses.Contains(info.Class))
            {
                continue;
            }

            VguiImagePanel panel = _conditionImages[BuffImagesOffset + i];

            if (cond.Has(info.Condition))
            {
                panel.SetImage(blue ? info.BlueImage : info.RedImage);
            }

            SetVisibility(cond, info.Condition, panel, ref xOffset, (255, 255, 255, colorFade));

            if (panel.Visible)
            {
                activeClasses.Add(info.Class);
            }
        }

        // The old, non-buff-table panels (:986–1003) — bleeding and its hook variant share a starting X on purpose,
        // "draw this on top of bleeding" — the hook icon never advances the shared offset.
        int bloodX = xOffset;
        SetVisibility(cond, CondBleeding, _conditionImages[0], ref xOffset, (colorFade, 0, 0, 255));
        SetVisibility(cond, CondGrapplingHookBleeding, _conditionImages[1], ref bloodX, (255, 255, 255, 255));
        SetVisibility(cond, CondMadMilk, _conditionImages[4], ref xOffset, (colorFade, colorFade, colorFade, 255));
        SetVisibility(cond, CondMarkedForDeath, _conditionImages[2], ref xOffset, ((byte)(255 - colorFade), (byte)(245 - colorFade), (byte)(245 - colorFade), 255));
        SetVisibility(cond, CondMarkedForDeathSilent, _conditionImages[3], ref xOffset, ((byte)(125 - colorFade), (byte)(255 - colorFade), (byte)(255 - colorFade), 255));

        // Same target panel as marked-for-death-silent — Valve's own row (:1001), not a typo carried over by accident.
        SetVisibility(cond, CondPasstimePenaltyDebuff, _conditionImages[3], ref xOffset, ((byte)(125 - colorFade), (byte)(255 - colorFade), (byte)(255 - colorFade), 255));
        SetVisibility(cond, CondStunned, _conditionImages[6], ref xOffset, (colorFade, colorFade, 0, 255));
        SetVisibility(cond, CondGas, _conditionImages[5], ref xOffset, (colorFade, colorFade, colorFade, 255));
    }

    /// <summary>`SetPlayerHealthImagePanelVisibility` (:909): shown, tinted and moved to <paramref name="xOffset"/> only
    /// the frame it turns on — every panel starts this think already hidden, so this is exactly once per active condition.</summary>
    private static void SetVisibility(PlayerConditions cond, int condition, VguiImagePanel panel, ref int xOffset, (byte, byte, byte, byte) color)
    {
        if (!cond.Has(condition) || panel.Visible)
        {
            return;
        }

        panel.Visible = true;
        panel.DrawColor = color;
        panel.X = xOffset;
        xOffset += 100;
    }

    /// <summary>`BuffClass_t` (tf_hud_playerstatus.h) — which buffs share a slot, so only the first active one draws.</summary>
    private enum BuffClass
    {
        BulletResist, BlastResist, FireResist,
        SoldierOffense, SoldierDefense, SoldierHealOnHit,
        RuneStrength, RuneHaste, RuneRegen, RuneResist, RuneVampire, RuneReflect, RunePrecision, RuneAgility,
        RuneKnockout, RuneKing, RunePlague, RuneSupernova,
        Parachute,
    }

    /// <summary>One `CTFBuffInfo` (:663–688) — a condition, its class, and its team-coloured image pair.</summary>
    private readonly record struct BuffInfo(int Condition, BuffClass Class, string BlueImage, string RedImage);

    /// <summary>`SetHealth` (:736).</summary>
    /// <param name="health">The health.</param>
    /// <param name="maxHealth">The class maximum.</param>
    /// <param name="maxBuffedHealth">The overheal maximum.</param>
    public void SetHealth(int health, int maxHealth, int maxBuffedHealth)
    {
        _health = health;
        _maxHealth = maxHealth;
        HealthImage.Health = (float)_health / _maxHealth;
        HealthImage.FgColor = (255, 255, 255, 255);

        if (_health <= 0)
        {
            _healthImageBg.Visible = false;
            _buildingHealthImageBg.Visible = false;
            HideHealthBonusImage();
        }
        else
        {
            _healthImageBg.Visible = true;
            _buildingHealthImageBg.Visible = Building;
            float warning = GetFloat("HealthDeathWarning");

            if (_health > _maxHealth)
            {
                float boostMax = maxBuffedHealth - _maxHealth;

                if (_bonusOrigin.Wide != -1 && AllowAnimations && _animState != AnimState.Bonus)
                {
                    Animate("HudHealthDyingPulseStop");
                    Animate("HudHealthBonusPulse");
                    _animState = AnimState.Bonus;
                }

                ShowBonus((255, 255, 255, 255), Math.Min((_health - _maxHealth) / boostMax, 1f));
            }
            else if (_health < _maxHealth * warning)
            {
                float boostMax = _maxHealth * warning;
                (byte, byte, byte, byte) colour = GetColor("HealthDeathWarningColor");

                if (_bonusOrigin.Wide != -1 && AllowAnimations && _animState != AnimState.Dying)
                {
                    Animate("HudHealthBonusPulseStop");
                    Animate("HudHealthDyingPulse");
                    _animState = AnimState.Dying;
                }

                ShowBonus(colour, (boostMax - _health) / boostMax);
                HealthImage.FgColor = colour;
            }
            else
            {
                HideHealthBonusImage();
            }
        }

        if (_health > 0)
        {
            SetDialogVariable("Health", _health);
            SetDialogVariable("MaxHealth", _maxHealth - _health >= 5 ? _maxHealth.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty);
        }
        else
        {
            SetDialogVariable("Health", string.Empty);
            SetDialogVariable("MaxHealth", string.Empty);
        }
    }

    /// <summary>The bonus image shown, tinted, and grown by <paramref name="percent"/> of `HealthBonusPosAdj` each way.</summary>
    private void ShowBonus((byte, byte, byte, byte) colour, float percent)
    {
        if (_bonusOrigin.Wide == -1)
        {
            return;
        }

        _healthBonusImage.Visible = true;
        _healthBonusImage.DrawColor = colour;

        // `RoundFloatToInt` is cvtss2si: to nearest, ties to even.
        int adjust = (int)MathF.Round(percent * GetInt("HealthBonusPosAdj"), MidpointRounding.ToEven);

        (_healthBonusImage.X, _healthBonusImage.Y) = (_bonusOrigin.X - adjust, _bonusOrigin.Y - adjust);
        (_healthBonusImage.Wide, _healthBonusImage.Tall) = (_bonusOrigin.Wide + (2 * adjust), _bonusOrigin.Tall + (2 * adjust));
    }

    /// <summary>`SetAllowAnimations` (tf_hud_playerstatus.h:176): `m_bAnimate`, true from the constructor (:691).</summary>
    public bool AllowAnimations { get; set; } = true;

    /// <summary>`HideHealthBonusImage` (:890): back to its own bounds, and hidden.</summary>
    public void HideHealthBonusImage()
    {
        if (!_healthBonusImage.Visible)
        {
            return;
        }

        if (_bonusOrigin.Wide != -1)
        {
            (_healthBonusImage.X, _healthBonusImage.Y, _healthBonusImage.Wide, _healthBonusImage.Tall) = _bonusOrigin;
        }

        _healthBonusImage.Visible = false;
        Animate("HudHealthBonusPulseStop");
        Animate("HudHealthDyingPulseStop");
        _animState = AnimState.None;
    }

    /// <summary>`g_pClientMode->GetViewportAnimationController()->StartAnimationSequence( this, name )`.</summary>
    private void Animate(string sequence) => HudViewport.Of(this)?.Animations.StartAnimationSequence(this, sequence);

    /// <summary>`m_iAnimState`.</summary>
    private enum AnimState
    {
        None,
        Bonus,
        Dying,
    }
}

/// <summary>`CTFSpectatorGUIHealth` (vgui/tf_spectatorgui.h:28): the health panel as the target ID holds it — its own `.res`, and no think.</summary>
/// <param name="parent">The parent.</param>
/// <param name="name">The name.</param>
public sealed class TfSpectatorGuiHealth(VguiPanel? parent, string? name) : TfHudPlayerHealth(parent, name)
{
    /// <inheritdoc/>
    protected override string ResFilename => "resource/UI/SpectatorGUIHealth.res";

    /// <inheritdoc/>
    /// <remarks>"Do nothing. We're just preventing the base health panel from updating."</remarks>
    protected override void OnThink()
    {
        // The target ID sets this panel's health itself; the local player's must not overwrite it.
    }
}

/// <summary>`CTFHudPlayerStatus` (tf_hud_playerstatus.cpp:1057): the element `HudPlayerStatus`, holding the class and health panels.</summary>
/// <remarks>Hidden by `HIDEHUD_HEALTH | HIDEHUD_PLAYERDEAD`.</remarks>
public sealed class TfHudPlayerStatus : VguiEditablePanel, IHudElement
{
    /// <summary>Parented to the viewport, the class panel then the health panel under it (:1062-1063).</summary>
    /// <param name="viewport">The viewport.</param>
    /// <param name="mdlCache">`vgui::MDLCache()`, for the class model panel.</param>
    public TfHudPlayerStatus(VguiPanel viewport, IMdlCache mdlCache)
        : base(viewport, "HudPlayerStatus")
    {
        PlayerClass = new TfHudPlayerClass(this, "HudPlayerClass", mdlCache);
        Health = new TfHudPlayerHealth(this, "HudPlayerHealth");
    }

    /// <summary>`m_pHudPlayerClass`.</summary>
    public TfHudPlayerClass PlayerClass { get; }

    /// <summary>`m_pHudPlayerHealth`.</summary>
    public TfHudPlayerHealth Health { get; }

    /// <inheritdoc/>
    public int HiddenBits => HudVisibility.HideHealth | HudVisibility.HidePlayerDead;

    /// <summary>
    /// `CTFHudPlayerStatus::ShouldDraw` (:1087): not as a Halloween ghost, nor under the match summary, then
    /// `CHudElement::ShouldDraw`. **Not modelled:** an active minigame (`CTFMinigameLogic`), which is not decoded.
    /// </summary>
    /// <param name="state">The game state.</param>
    /// <returns>Whether it draws.</returns>
    public bool ShouldDraw(HudState state) =>
        !state.Conditions.Has(ConditionHalloweenGhostMode) && !state.Rules.ShowMatchSummary && HudVisibility.ShouldDraw(state, this);

    /// <summary>`TF_COND_HALLOWEEN_GHOST_MODE` (tf_shareddefs.h:767).</summary>
    private const int ConditionHalloweenGhostMode = 77;
}
