using System;
using System.Collections.Generic;
using System.Globalization;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>
/// `CTargetID` (game/client/tf/tf_hud_target_id.cpp): shared behaviour for a target-ID panel — a player's name, health and
/// data line, whichever entity supplies <see cref="TargetIndex"/>. `CMainTargetID` and `CSpectatorTargetID` are siblings
/// under it (not one another), so each is its own sealed class in this file; only the index calculation, the extra
/// `ShouldDraw` gate and the background layout differ between them.
/// </summary>
/// <remarks>
/// `ApplySchemeSettings` (:274) loads `resource/UI/TargetID.res`; `ShouldDraw` (:504) reuses the previous target for one
/// tick past losing it, then `IsValidIDTarget` (:340) for a player target, then `UpdateID` (:719) fills the labels.
/// `PerformLayout` (:599) sizes to the health panel plus the wider label and centres at the `.res` y.
/// **Not modelled here (base):** buildings, flags, dropped weapons and revive markers as targets (no such entity is
/// decoded into <see cref="Core.Scene.ScenePlayer"/> or a sibling type yet — a Scene-layer decode gap, not a HUD one);
/// the floating health icon panel itself (`CFloatingHealthIcon`, a screen-projected overlay — `DrawHealthIcon`'s convar
/// gate IS modelled, via <see cref="DisableFloatingHealth"/>); `tf_spectator_target_location` other than 0; the arena
/// offset; avatars; and the item-name line of a non-stock medigun, which waits for the econ name generator.
/// </remarks>
public abstract class TfTargetId : VguiEditablePanel, IHudElement
{
    private protected const int TeamSpectator = 1;
    private protected const int TeamRed = 2;
    private protected const int ClassMedic = 5;
    private protected const int ClassSpy = 8;
    private const int ConditionTaunting = 7;
    private const int IdChars = 256;

    // `g_aPlayerClassNames` (tf_shareddefs.cpp:38).
    private static readonly string[] ClassNames =
    [
        "#TF_Class_Name_Undefined", "#TF_Class_Name_Scout", "#TF_Class_Name_Sniper", "#TF_Class_Name_Soldier", "#TF_Class_Name_Demoman",
        "#TF_Class_Name_Medic", "#TF_Class_Name_HWGuy", "#TF_Class_Name_Pyro", "#TF_Class_Name_Spy", "#TF_Class_Name_Engineer",
        "#TF_Class_Name_Civilian",
    ];

    private protected VguiLabel? _nameLabel;
    private protected VguiLabel? _dataLabel;
    private protected VguiPanel? _killStreakIcon;
    private protected VguiPanel? _avatar;
    private protected (byte, byte, byte, byte) _labelColorDefault = (255, 255, 255, 255);
    private protected int _originalY;
    private protected int _lastEntIndex;
    private protected float _lastChangeTime;
    private protected bool _layoutOnUpdate;
    private protected int _screenWide = 640;
    private protected int _screenTall = 480;

    /// <summary>`CTargetID( pElementName )`: parented to the viewport, its health panel made up front.</summary>
    /// <param name="viewport">The viewport.</param>
    /// <param name="name">The panel name — `"CMainTargetID"` or `"CSpectatorTargetID"`.</param>
    private protected TfTargetId(VguiPanel viewport, string name)
        : base(viewport, name) =>
        TargetHealth = new TfSpectatorGuiHealth(this, "SpectatorGUIHealth");

    /// <summary>`m_pTargetHealth`.</summary>
    public TfSpectatorGuiHealth TargetHealth { get; }

    /// <summary>`m_iTargetEntIndex`.</summary>
    public int TargetIndex { get; private protected set; }

    /// <summary>`tf_hud_target_id_alpha`: 100.</summary>
    public int BackgroundAlpha { get; set; } = 100;

    /// <summary>`tf_hud_target_id_disable_floating_health`: 0.</summary>
    public bool DisableFloatingHealth { get; set; }

    /// <summary>The name line as set.</summary>
    public string TargetName { get; private protected set; } = string.Empty;

    /// <summary>The data line as set.</summary>
    public string TargetData { get; private protected set; } = string.Empty;

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
    /// <remarks>`CTargetID::ApplySchemeSettings` (:274).</remarks>
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

        // `Reset`'s default label colour.
        _labelColorDefault = context.Scheme.GetColor("Label.TextColor", (255, 255, 255, 255));
    }

    /// <summary>`CTargetID::ShouldDraw` (:504), gated by each sibling's own extra condition (:1201, :1213).</summary>
    /// <param name="state">The local player.</param>
    /// <returns>Whether it draws.</returns>
    public bool ShouldDraw(HudState state)
    {
        if (!state.HasLocalPlayer || !ExtraShouldDrawGate(state))
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

    /// <summary>The extra gate each sibling's own `ShouldDraw` checks before `CTargetID::ShouldDraw`'s common body.</summary>
    /// <param name="state">The local player.</param>
    private protected abstract bool ExtraShouldDrawGate(HudState state);

    /// <summary>`CalculateTargetIndex( C_TFPlayer* )`, which each sibling overrides its own way.</summary>
    /// <param name="state">The local player.</param>
    private protected abstract int CalculateTargetIndex(HudState state);

    /// <inheritdoc/>
    /// <remarks>`CTargetID::PerformLayout` (:599).</remarks>
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
    }

    /// <summary>
    /// `IsValidIDTarget` (:340) for a player target: the health-branch condition at :455 (spectator, same team, or a spy
    /// seeing through a disguise/stealth) is the only case ported for the health string; but :483-490 is unconditional
    /// once that first branch doesn't already show health — `pEnt->IsVisibleToTargetID()` — so an ordinary enemy who
    /// isn't stealthed is still a valid target, just without a health line (<see cref="UpdateId"/> gates that
    /// separately via its own `showHealth`).
    /// </summary>
    private protected virtual bool IsValidIdTarget(HudState state)
    {
        if (TargetIndex == 0 || state.Player(TargetIndex) is not { } target)
        {
            return false;
        }

        bool spectator = state.Team == TeamSpectator;
        bool stealthed = target.Conditions.IsStealthed;

        return spectator || target.Team == state.Team || !stealthed;
    }

    /// <summary>`CTargetID::UpdateID` (:719) for a player.</summary>
    private protected void UpdateId(HudState state)
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
    private protected (string Data, bool KillStreak) DataString(ScenePlayer target, bool disguised, bool enemy)
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
    private protected static bool IsEnemyPlayer(int localTeam, int targetTeam) => localTeam switch
    {
        TeamRed => targetTeam == 3,
        3 => targetTeam == TeamRed,
        _ => false,
    };

    /// <summary>The labels' text and colours, and a layout when either changed width (:1066).</summary>
    private protected void SetLabels(string id, string data)
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

    private protected string? Find(string token) => HudViewport.Of(this)?.Context?.Localize?.Invoke(token[1..]);

    private protected int XRes(int x) => (int)(x * (_screenWide / 640.0));
}

/// <summary>`CSpectatorTargetID` over `CTargetID`: the spectated player's name, health and data.</summary>
/// <remarks>
/// Drawn only in an observer mode other than freeze cam (:1213). The target is the observer target in eye (:1243), else
/// `GetIDTarget()` — the observer target in death cam and chase (`UpdateIDTarget`, c_tf_player.cpp:7061). Its own
/// `ApplySchemeSettings` (:1263) additionally hides `TargetIDBG` and shows the blue spectator background; its own
/// `PerformLayout` (:1284) recolours that background red or blue by the target's team, on top of the shared layout.
/// **Not modelled here:** `GetIDTarget`'s trace (see <see cref="IdTargetTrace"/>, which is not needed by this sibling —
/// a spectator's target always comes from the observer target, never the crosshair).
/// </remarks>
public sealed class TfSpectatorTargetId : TfTargetId
{
    private VguiPanel? _specBlue;
    private VguiPanel? _specRed;

    /// <summary>`CTargetID( "CSpectatorTargetID" )`.</summary>
    /// <param name="viewport">The viewport.</param>
    public TfSpectatorTargetId(VguiPanel viewport)
        : base(viewport, "CSpectatorTargetID")
    {
    }

    /// <inheritdoc/>
    /// <remarks>`CTargetID::ApplySchemeSettings` (:274), then `CSpectatorTargetID`'s (:1263).</remarks>
    public override void ApplySchemeSettings(VguiContext context)
    {
        base.ApplySchemeSettings(context);

        _specBlue = FindChildByName("TargetIDBG_Spec_Blue");
        _specRed = FindChildByName("TargetIDBG_Spec_Red");

        if (FindChildByName("TargetIDBG") is { } background)
        {
            background.Visible = false;
        }

        if (_specBlue is not null)
        {
            _specBlue.Visible = true;
        }
    }

    /// <summary>`CSpectatorTargetID::ShouldDraw` (:1213)'s extra gate, before `CTargetID::ShouldDraw`'s common body.</summary>
    private protected override bool ExtraShouldDrawGate(HudState state) =>
        state.ObserverMode > ObserverModes.None && state.ObserverMode != ObserverModes.FreezeCam;

    /// <summary>`CSpectatorTargetID::CalculateTargetIndex` (:1243) over `CTargetID`'s `GetIDTarget()`.</summary>
    private protected override int CalculateTargetIndex(HudState state)
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

    /// <inheritdoc/>
    /// <remarks>`CSpectatorTargetID::PerformLayout` (:1284), `tf_spectator_target_location` 0.</remarks>
    protected override void PerformLayout()
    {
        base.PerformLayout();

        foreach (VguiPanel? background in (ReadOnlySpan<VguiPanel?>)[_specBlue, _specRed])
        {
            if (background is not null)
            {
                (background.Wide, background.Tall) = (Wide, Tall);
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
}

/// <summary>`CMainTargetID` over `CTargetID`: the crosshair target's name, health and data while not spectating.</summary>
/// <remarks>
/// Drawn only while the local player is not in any observer mode (:1201) — `ShouldDraw` returns `BaseClass::ShouldDraw()`
/// with no other override, and `CalculateTargetIndex` is not overridden either, so both are `CTargetID`'s own: the target
/// is `GetIDTarget()` (<see cref="IdTargetTrace"/>) minus whatever `CSecondaryTargetID` is already showing (:702), and
/// layout is the shared `CTargetID::PerformLayout` with no team-coloured background swap.
/// **Not modelled here:** the "minus `CSecondaryTargetID`'s current target" subtraction (:707) — `CSecondaryTargetID`
/// (the medic heal-target/healer line) is not ported in this pass, so there is nothing yet to subtract against; wiring
/// `IdTargetTrace`'s result into <see cref="HudState"/> from a real world/entity trace (`MainForm`'s job, left for a
/// follow-up so as not to touch that file here).
/// </remarks>
public sealed class TfMainTargetId : TfTargetId
{
    /// <summary>`CTargetID( "CMainTargetID" )`.</summary>
    /// <param name="viewport">The viewport.</param>
    public TfMainTargetId(VguiPanel viewport)
        : base(viewport, "CMainTargetID")
    {
    }

    /// <summary>`CMainTargetID::ShouldDraw` (:1201)'s extra gate: not in any observer mode.</summary>
    private protected override bool ExtraShouldDrawGate(HudState state) => state.ObserverMode <= ObserverModes.None;

    /// <summary>`CTargetID::CalculateTargetIndex` (:702): `GetIDTarget()` — <see cref="HudState.IdTarget"/>, the crosshair
    /// trace precomputed by whoever drives the world/entity trace (<see cref="IdTargetTrace"/>).</summary>
    private protected override int CalculateTargetIndex(HudState state) => state.IdTarget ?? 0;
}
