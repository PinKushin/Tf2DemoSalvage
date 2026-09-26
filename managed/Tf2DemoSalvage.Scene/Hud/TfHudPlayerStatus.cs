using System;

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
/// missing. **Not modelled yet:** the pulse animations it starts (the animation controller), the condition icons (every
/// one is hidden each think until conditions are read), the Halloween wheel and the player level.
/// </remarks>
public sealed class TfHudPlayerHealth : VguiEditablePanel
{
    private const string ResFile = "resource/UI/HudPlayerHealth.res";

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
    private int _maxHealth;

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

    /// <inheritdoc/>
    public override void ApplySchemeSettings(VguiContext context)
    {
        LoadControlSettings(ResFile, context);
        _bonusOrigin = (_healthBonusImage.X, _healthBonusImage.Y, _healthBonusImage.Wide, _healthBonusImage.Tall);
        _nextThink = 0f;
        base.ApplySchemeSettings(context);
        _buildingHealthImageBg.Visible = false;
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
        }

        _nextThink = state.CurTime + 0.05f;
    }

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
            _buildingHealthImageBg.Visible = false;
            float warning = GetFloat("HealthDeathWarning");

            if (_health > _maxHealth)
            {
                float boostMax = maxBuffedHealth - _maxHealth;

                ShowBonus((255, 255, 255, 255), Math.Min((_health - _maxHealth) / boostMax, 1f));
            }
            else if (_health < _maxHealth * warning)
            {
                float boostMax = _maxHealth * warning;
                (byte, byte, byte, byte) colour = GetColor("HealthDeathWarningColor");

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

    /// <summary>`HideHealthBonusImage` (:890): back to its own bounds, and hidden.</summary>
    private void HideHealthBonusImage()
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
    }
}

/// <summary>`CTFHudPlayerStatus` (tf_hud_playerstatus.cpp:1057): the element `HudPlayerStatus`, holding the health panel.</summary>
/// <remarks>
/// Hidden by `HIDEHUD_HEALTH | HIDEHUD_PLAYERDEAD`. **Not modelled yet:** `HudPlayerClass` (the class portrait), and
/// `ShouldDraw`'s extra refusals — ghost mode, a minigame, the match summary.
/// </remarks>
public sealed class TfHudPlayerStatus : VguiEditablePanel, IHudElement
{
    /// <summary>Parented to the viewport, the health panel under it.</summary>
    /// <param name="viewport">The viewport.</param>
    public TfHudPlayerStatus(VguiPanel viewport)
        : base(viewport, "HudPlayerStatus") =>
        Health = new TfHudPlayerHealth(this, "HudPlayerHealth");

    /// <summary>`m_pHudPlayerHealth`.</summary>
    public TfHudPlayerHealth Health { get; }

    /// <inheritdoc/>
    public int HiddenBits => HudVisibility.HideHealth | HudVisibility.HidePlayerDead;
}
