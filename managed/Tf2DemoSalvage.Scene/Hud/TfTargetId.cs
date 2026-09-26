using System;
using System.Collections.Generic;
using System.Globalization;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>`CSpectatorTargetID` over `CTargetID` (game/client/tf/tf_hud_target_id.cpp): the spectated player's name, health and data.</summary>
/// <remarks>
/// Hidden by `HIDEHUD_MISCSTATUS | HIDEHUD_TARGET_ID`; drawn only in an observer mode other than freeze cam. The target is the
/// observer target in eye (:1243), else `GetIDTarget()` — the observer target in death cam and chase (`UpdateIDTarget`,
/// c_tf_player.cpp:7061). `ApplySchemeSettings` loads `resource/UI/TargetID.res`, hides `TargetIDBG` and shows the blue
/// spectator background; `PerformLayout` (:1284) sizes to the health panel plus the wider label and centres at the `.res`
/// y, the background red or blue by the target's team. `UpdateID` (:719) fills the labels for a player target.
/// **Not modelled:** `GetIDTarget`'s trace — free cam and the alive player's own ID; buildings, flags, dropped weapons and
/// revive markers as targets; the floating health icon; `tf_spectator_target_location` other than 0; the arena offset;
/// avatars; and the item-name line of a non-stock medigun, which waits for the econ name generator.
/// </remarks>
public sealed class TfSpectatorTargetId : VguiEditablePanel, IHudElement
{
    private const int TeamSpectator = 1;
    private const int TeamRed = 2;
    private const int ClassMedic = 5;
    private const int ClassSpy = 8;
    private const int ConditionTaunting = 7;
    private const int IdChars = 256;

    // `g_aPlayerClassNames` (tf_shareddefs.cpp:38).
    private static readonly string[] ClassNames =
    [
        "#TF_Class_Name_Undefined", "#TF_Class_Name_Scout", "#TF_Class_Name_Sniper", "#TF_Class_Name_Soldier", "#TF_Class_Name_Demoman",
        "#TF_Class_Name_Medic", "#TF_Class_Name_HWGuy", "#TF_Class_Name_Pyro", "#TF_Class_Name_Spy", "#TF_Class_Name_Engineer",
        "#TF_Class_Name_Civilian",
    ];

    private VguiLabel? _nameLabel;
    private VguiLabel? _dataLabel;
    private VguiPanel? _specBlue;
    private VguiPanel? _specRed;
    private VguiPanel? _killStreakIcon;
    private VguiPanel? _avatar;
    private (byte, byte, byte, byte) _labelColorDefault = (255, 255, 255, 255);
    private int _originalY;
    private int _lastEntIndex;
    private float _lastChangeTime;
    private bool _layoutOnUpdate;
    private int _screenWide = 640;
    private int _screenTall = 480;

    /// <summary>`CTargetID( "CSpectatorTargetID" )`: parented to the viewport, its health panel made up front.</summary>
    /// <param name="viewport">The viewport.</param>
    public TfSpectatorTargetId(VguiPanel viewport)
        : base(viewport, "CSpectatorTargetID") =>
        TargetHealth = new TfSpectatorGuiHealth(this, "SpectatorGUIHealth");

    /// <summary>`m_pTargetHealth`.</summary>
    public TfSpectatorGuiHealth TargetHealth { get; }

    /// <summary>`m_iTargetEntIndex`.</summary>
    public int TargetIndex { get; private set; }

    /// <summary>`tf_hud_target_id_alpha`: 100.</summary>
    public int BackgroundAlpha { get; set; } = 100;

    /// <summary>`tf_hud_target_id_disable_floating_health`: 0.</summary>
    public bool DisableFloatingHealth { get; set; }

    /// <summary>The name line as set.</summary>
    public string TargetName { get; private set; } = string.Empty;

    /// <summary>The data line as set.</summary>
    public string TargetData { get; private set; } = string.Empty;

    /// <inheritdoc/>
    public int HiddenBits => HudVisibility.HideMiscStatus | HudVisibility.HideTargetId;

    /// <inheritdoc/>
    public override void ApplySettings(KeyValuesTree block, VguiContext context)
    {
        base.ApplySettings(block, context);

        // `GetPos( x, m_nOriginalY )` after the layout file places it.
        _originalY = Y;
    }

    /// <inheritdoc/>
    /// <remarks>`CTargetID::ApplySchemeSettings` (:274), then `CSpectatorTargetID`'s (:1263).</remarks>
    public override void ApplySchemeSettings(VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        LoadControlSettings("resource/UI/TargetID.res", context);
        base.ApplySchemeSettings(context);
        (_screenWide, _screenTall) = (context.ScreenWide, context.ScreenTall);

        _nameLabel = FindChildByName("TargetNameLabel") as VguiLabel;
        _dataLabel = FindChildByName("TargetDataLabel") as VguiLabel;
        _killStreakIcon = FindChildByName("KillStreakIcon");
        _avatar = FindChildByName("AvatarImage");
        _specBlue = FindChildByName("TargetIDBG_Spec_Blue");
        _specRed = FindChildByName("TargetIDBG_Spec_Red");

        // `Reset`'s default label colour.
        _labelColorDefault = context.Scheme.GetColor("Label.TextColor", (255, 255, 255, 255));

        if (FindChildByName("TargetIDBG") is { } background)
        {
            background.Visible = false;
        }

        if (_specBlue is not null)
        {
            _specBlue.Visible = true;
        }
    }

    /// <summary>`CSpectatorTargetID::ShouldDraw` (:1213), then `CTargetID::ShouldDraw` (:504).</summary>
    /// <param name="state">The local player.</param>
    /// <returns>Whether it draws.</returns>
    public bool ShouldDraw(HudState state)
    {
        if (!state.HasLocalPlayer || state.ObserverMode <= ObserverModes.None || state.ObserverMode == ObserverModes.FreezeCam)
        {
            return false;
        }

        if (HudVisibility.IsHidden(state, HiddenBits) || state.Conditions.Has(ConditionTaunting))
        {
            return false;
        }

        TargetIndex = CalculateTargetIndex(state);

        if (TargetIndex == 0)
        {
            // "Check to see if we should clear our ID", else keep the old one.
            if (_lastChangeTime != 0f && state.CurTime > _lastChangeTime)
            {
                (_lastChangeTime, _lastEntIndex) = (0f, 0);
            }
            else
            {
                TargetIndex = _lastEntIndex;
            }
        }
        else
        {
            _lastChangeTime = state.CurTime;
        }

        if (!IsValidIdTarget(state))
        {
            _lastEntIndex = 0;
            return false;
        }

        if (!Visible || TargetIndex != _lastEntIndex)
        {
            _lastEntIndex = TargetIndex;
            _layoutOnUpdate = true;

            if (_avatar is not null)
            {
                _avatar.Visible = false;
            }
        }

        UpdateId(state);
        return true;
    }

    /// <inheritdoc/>
    /// <remarks>`CSpectatorTargetID::PerformLayout` (:1284), `tf_spectator_target_location` 0.</remarks>
    protected override void PerformLayout()
    {
        base.PerformLayout();

        if (_nameLabel is null || _dataLabel is null)
        {
            return;
        }

        int width = TargetHealth.Wide + XRes(5) + XRes(10);
        (int nameWide, _) = _nameLabel.GetContentSize();
        (int dataWide, _) = _dataLabel.GetContentSize();

        width += Math.Max(nameWide, dataWide);
        Wide = width;

        int buffer = _avatar is { Visible: true } ? 6 : 8;

        _nameLabel.X = XRes(buffer) + TargetHealth.Wide;
        _dataLabel.X = XRes(buffer) + TargetHealth.Wide;

        if (_killStreakIcon is not null)
        {
            _killStreakIcon.X = XRes(10) + TargetHealth.Wide;
        }

        (X, Y) = ((int)((_screenWide - width) * 0.5), _originalY);

        foreach (VguiPanel? background in (ReadOnlySpan<VguiPanel?>)[_specBlue, _specRed])
        {
            if (background is not null)
            {
                (background.Wide, background.Tall) = (width, Tall);
            }
        }

        if (_specBlue is not null && _specRed is not null && TargetIndex != 0
            && HudViewport.Of(this)?.State.Player(TargetIndex) is { } target)
        {
            bool red = target.Team == TeamRed;

            _specBlue.Visible = !red;
            _specRed.Visible = red;
            _specBlue.SetAnimationValue("alpha", (float)BackgroundAlpha);
            _specRed.SetAnimationValue("alpha", (float)BackgroundAlpha);
        }
    }

    /// <summary>`CSpectatorTargetID::CalculateTargetIndex` over `CTargetID`'s `GetIDTarget()`.</summary>
    private static int CalculateTargetIndex(HudState state)
    {
        if (state.ObserverMode == ObserverModes.InEye && state.ObserverTarget != 0)
        {
            return state.ObserverTarget;
        }

        // `UpdateIDTarget`: "If we're in deathcam, ID our killer" — and in chase.
        return state.ObserverMode is ObserverModes.DeathCam or ObserverModes.Chase
            && state.ObserverTarget != 0 && state.ObserverTarget != state.LocalIndex
            ? state.ObserverTarget
            : 0;
    }

    /// <summary>`IsValidIDTarget` (:340) for a player target: a spectator sees anyone not stealthed.</summary>
    private bool IsValidIdTarget(HudState state)
    {
        if (TargetIndex == 0 || state.Player(TargetIndex) is not { } target)
        {
            return false;
        }

        HudState local = state;
        bool spectator = local.Team == TeamSpectator;
        bool stealthed = target.Conditions.IsStealthed;

        // `bReturn = ( bSpectator || InSameTeam || ( ( bInSameTeam || bSpy || iSeeEnemyHealth ) && !bStealthed ) )`.
        return spectator || target.Team == local.Team || (local.PlayerClass == ClassSpy && !stealthed);
    }

    /// <summary>`CTargetID::UpdateID` (:719) for a player.</summary>
    private void UpdateId(HudState state)
    {
        if (state.Player(TargetIndex) is not { } target)
        {
            return;
        }

        string name = state.Names?.GetValueOrDefault(TargetIndex) ?? string.Empty;
        bool disguised = target.Conditions.Has(PlayerConditions.Disguised) && !target.Conditions.IsStealthed;
        bool spectator = state.Team == TeamSpectator;
        bool sameTeam = target.Team == state.Team;
        bool enemy = IsEnemyPlayer(state.Team, target.Team ?? 0);

        // "is the target a disguised enemy spy?" — with a disguise target, the name becomes theirs.
        bool disguisedEnemy = disguised && enemy && target.DisguiseTarget is not null;

        if (disguisedEnemy)
        {
            name = state.Names?.GetValueOrDefault(target.DisguiseTarget!.Value) ?? string.Empty;
        }

        (string data, bool killStreakData) = DataString(target, disguised, enemy);
        bool showHealth = spectator || sameTeam || state.PlayerClass is ClassSpy or ClassMedic or 6 || disguisedEnemy;
        string id = string.Empty;

        if (showHealth)
        {
            id = VguiLocalize.ConstructString(Find("#TF_playerid_sameteam"), IdChars, string.Empty, name);
        }

        int health = disguisedEnemy ? target.DisguiseHealth ?? 0 : target.EntityHealth ?? 0;
        int maxHealth = target.MaxHealth ?? 1;
        int maxBuffed = (int)MathF.Floor((target.MaxHealthForBuffing ?? 1) * 1.5f / 5f) * 5;

        if ((target.LifeState ?? 0) != 0)
        {
            // "fixup for health being 1 when dead"
            health = 0;
        }

        TargetHealth.Building = false;
        TargetHealth.SetLevel(-1);

        if (showHealth)
        {
            TargetHealth.SetHealth(health, maxHealth, maxBuffed);
        }
        else
        {
            TargetHealth.SetHealth(0, 1, 0);
        }

        TargetHealth.Visible = DisableFloatingHealth;

        if (_killStreakIcon is not null)
        {
            _killStreakIcon.Visible = killStreakData && data.Length > 0;
        }

        SetLabels(id, data);
    }

    /// <summary>`C_TFPlayer::GetTargetIDDataString` (c_tf_player.cpp:9748) as a spectator — or anyone not a medic — sees it.</summary>
    private (string Data, bool KillStreak) DataString(ScenePlayer target, bool disguised, bool enemy)
    {
        string data = string.Empty;

        if (disguised && !enemy)
        {
            // "a disguised friendly spy": the disguise's team and class.
            bool asEnemy = target.DisguiseTeam != target.Team;
            int disguiseClass = target.DisguiseClass ?? 0;

            data = VguiLocalize.ConstructString(
                Find("#TF_playerid_friendlyspy_disguise"),
                IdChars,
                Find(asEnemy ? "#TF_enemy" : "#TF_friendly") ?? string.Empty,
                Find(ClassNames[Math.Clamp(disguiseClass, 0, ClassNames.Length - 1)]) ?? string.Empty);
        }

        if (target.PlayerClass == ClassMedic)
        {
            string charge = MathF.Round((target.Medigun?.Charge ?? 0f) * 100f).ToString("0", CultureInfo.InvariantCulture);

            return (target.Medigun is { Quality: not 0 } medigun
                ? VguiLocalize.ConstructString(Find("#TF_playerid_mediccharge_wpn"), IdChars, charge, HudViewport.Of(this)?.ItemName?.Invoke(medigun.Definition, medigun.Quality) ?? string.Empty)
                : VguiLocalize.ConstructString(Find("#TF_playerid_mediccharge"), IdChars, charge), false);
        }

        if (disguised && target.DisguiseClass == ClassMedic && enemy)
        {
            return (VguiLocalize.ConstructString(Find("#TF_playerid_mediccharge"), IdChars, "0"), false);
        }

        // A local medic's no-heal and clip lines need the local medigun's target, which a spectator has not; then the streak.
        if (target.KillStreak is > 0 and var streak)
        {
            return (VguiLocalize.ConstructString(Find("#TF_playerid_ammo"), IdChars, streak.ToString(CultureInfo.InvariantCulture)), true);
        }

        return (data, false);
    }

    /// <summary>`C_TFPlayer::IsEnemyPlayer` (c_tf_player.cpp:5384): only RED against BLU and back.</summary>
    private static bool IsEnemyPlayer(int localTeam, int targetTeam) => localTeam switch
    {
        TeamRed => targetTeam == 3,
        3 => targetTeam == TeamRed,
        _ => false,
    };

    /// <summary>The labels' text and colours, and a layout when either changed width (:1066).</summary>
    private void SetLabels(string id, string data)
    {
        if (_nameLabel is null || _dataLabel is null)
        {
            return;
        }

        (int nameWide, _) = _nameLabel.GetContentSize();
        (int dataWide, _) = _dataLabel.GetContentSize();

        _nameLabel.Visible = id.Length > 0;
        _nameLabel.FgColor = _labelColorDefault;
        _dataLabel.Visible = data.Length > 0;
        _dataLabel.FgColor = _labelColorDefault;

        if (id.Length > 0)
        {
            SetDialogVariable("targetname", id);
        }
        else
        {
            _nameLabel.SetText(string.Empty, null);
        }

        if (data.Length > 0)
        {
            SetDialogVariable("targetdata", data);
        }
        else
        {
            _dataLabel.SetText(string.Empty, null);
        }

        (TargetName, TargetData) = (id, data);
        (int newNameWide, _) = _nameLabel.GetContentSize();
        (int newDataWide, _) = _dataLabel.GetContentSize();

        if (_layoutOnUpdate || newNameWide != nameWide || newDataWide != dataWide)
        {
            InvalidateLayout();
            _layoutOnUpdate = false;
        }
    }

    private string? Find(string token) => HudViewport.Of(this)?.Context?.Localize?.Invoke(token[1..]);

    private int XRes(int x) => (int)(x * (_screenWide / 640.0));
}
