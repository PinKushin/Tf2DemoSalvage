using System;
using System.Collections.Generic;
using System.Globalization;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>
/// `CTargetID` (game/client/tf/tf_hud_target_id.cpp): shared behaviour for a target-ID panel — a player's or a building's
/// name, health and data line, whichever entity supplies <see cref="TargetIndex"/>. `CMainTargetID` and
/// `CSpectatorTargetID` are siblings under it (not one another), so each is its own sealed class in this file; only the
/// index calculation, the extra `ShouldDraw` gate and the layout differ between them.
/// </summary>
/// <remarks>
/// `ApplySchemeSettings` (:274) loads `resource/UI/TargetID.res`; `ShouldDraw` (:504) reuses the previous target for one
/// tick past losing it unless the target kept a retain field of view, then `IsValidIDTarget` (:340), then `UpdateID`
/// (:719) fills the labels. `PerformLayout` (:599) sizes to the labels, plus the health panel when it is drawn inside.
/// `IsValidIDTarget` makes, shows and deletes the floating health icon (<see cref="TfFloatingHealthIcon"/>).
/// **Not modelled here:** flags, dropped weapons and revive markers as targets (none is decoded); the local medic's no-heal
/// line; `m_bIsCoaching`; Steam avatars — `tf_hud_target_id_show_avatars` 2 asks the Steam friends list, which a
/// recording does not carry; the arena class-layout offset; and `IsHealthBarVisible`'s MvM regeneration case. The moveable
/// sub-panel's pick-up prompt, including <see cref="CanPickupBuilding"/> in full, IS modelled.
/// </remarks>
public abstract class TfTargetId : VguiEditablePanel, IHudElement
{
    private protected const int TeamSpectator = 1;
    private protected const int TeamRed = 2;
    private protected const int TeamBlue = 3;
    private protected const int ClassSpy = 8;
    private protected const int ClassMedic = 5;
    private const int ClassHeavy = 6;
    private const int ConditionTaunting = 7;
    private const int StateDying = 3;
    private const int ObjectSentrygun = 2;
    private const int ObjectTeleporter = 1;
    private const int TeleporterStateIdle = 1;
    private const int TeleporterStateRecharging = 4;
    private const int IdChars = 256;

    /// <summary>`TF_BUILDING_PICKUP_RANGE` (tf_player_shared.cpp:217): 150 units, squared for `CanPickupBuilding`'s compare.</summary>
    private const float BuildingPickupRangeSq = 150f * 150f;

    /// <summary>`TF_BUILDING_RESCUE_MIN_RANGE_SQ` (tf_player_shared.cpp:218): 250 * 250, the `building_teleporting_pickup` deadzone.</summary>
    private const float RescueMinRangeSq = 250f * 250f;

    /// <summary>`TF_COND_GRAPPLINGHOOK` (tf_shareddefs.h:788).</summary>
    private const int ConditionGrapplingHook = 98;

    /// <summary>`TF_COND_RUNE_KNOCKOUT` (tf_shareddefs.h:793): `GetCarryingRuneType() == RUNE_KNOCKOUT`, read as the condition it is.</summary>
    private const int ConditionRuneKnockout = 103;

    /// <summary>`TF_COND_STUNNED` (tf_shareddefs.h:705): "Any type of stun. Check iStunFlags for more info."</summary>
    private const int ConditionStunned = 15;

    /// <summary>`TF_STUN_CONTROLS` (tf_shareddefs.h:1334): `1&lt;&lt;1`.</summary>
    private const int StunControls = 1 << 1;

    /// <summary>`TF_STUN_LOSER_STATE` (:1339): `1&lt;&lt;6`.</summary>
    private const int StunLoserState = 1 << 6;

    /// <summary>`GR_STATE_RND_RUNNING` (teamplayroundbased_gamerules.h:59).</summary>
    private const int RoundStateRndRunning = 4;

    /// <summary>`GR_STATE_TEAM_WIN` (:63).</summary>
    private const int RoundStateTeamWin = 5;

    /// <summary>`GR_STATE_BETWEEN_RNDS` (:78).</summary>
    private const int RoundStateBetweenRounds = 10;

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
    private VguiPanel? _ammoIcon;
    private protected VguiPanel? _avatar;
    private TfImagePanel? _bgPanel;
    private VguiEditablePanel? _moveableSubPanel;
    private VguiIconPanel? _moveableIcon;
    private VguiScalableImagePanel? _moveableSymbolIcon;
    private VguiIconPanel? _moveableIconBg;
    private VguiLabel? _moveableKeyLabel;
    private protected (byte, byte, byte, byte) _labelColorDefault = (255, 255, 255, 255);
    private protected int _originalY;
    private protected int _lastEntIndex;
    private protected float _lastChangeTime;
    private float _targetRetainFov;
    private int _lastScannedEntIndex;
    private TfFloatingHealthIcon? _floatingHealthIcon;
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

    /// <summary>`tf_hud_target_id_offset`: 0 — the `.res` Y offset, in 480-high units.</summary>
    public int YOffset { get; set; }

    /// <summary>The name line as set.</summary>
    public string TargetName { get; private protected set; } = string.Empty;

    /// <summary>The data line as set.</summary>
    public string TargetData { get; private protected set; } = string.Empty;

    /// <inheritdoc/>
    public int HiddenBits => HudVisibility.HideMiscStatus | HudVisibility.HideTargetId;

    /// <summary>"global", then "mid", "commentary" and "arena_target_id" (tf_hud_target_id.cpp:147-153).</summary>
    public IReadOnlyList<string> RenderGroups { get; } = ["global", "mid", "commentary", "arena_target_id"];

    /// <summary>`m_iRenderPriority`: 5 (:150) until the `.res` says `priority` (:304).</summary>
    public int RenderGroupPriority { get; private set; } = 5;

    /// <inheritdoc/>
    public override void ApplySettings(KeyValuesTree block, VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(block);

        base.ApplySettings(block, context);

        // `inResourceData->GetInt( "priority" )` — 0 when absent, as `GetInt` answers.
        RenderGroupPriority = PanelLayout.Atoi(block.Find("priority")?.Value ?? "0");

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
        _bgPanel = FindChildByName("TargetIDBG") as TfImagePanel;
        _killStreakIcon = FindChildByName("KillStreakIcon");
        _ammoIcon = FindChildByName("AmmoIcon");
        _avatar = FindChildByName("AvatarImage");

        _moveableSubPanel = FindChildByName("MoveableSubPanel") as VguiEditablePanel;

        if (_moveableSubPanel is not null)
        {
            _moveableIcon = _moveableSubPanel.FindChildByName("MoveableIcon") as VguiIconPanel;
            _moveableSymbolIcon = _moveableSubPanel.FindChildByName("MoveableSymbolIcon") as VguiScalableImagePanel;
            _moveableIconBg = _moveableSubPanel.FindChildByName("MoveableIconBG") as VguiIconPanel;
            _moveableKeyLabel = _moveableSubPanel.FindChildByName("MoveableKeyLabel") as VguiLabel;
        }

        // `Reset`'s default label colour.
        _labelColorDefault = context.Scheme.GetColor("Label.TextColor", (255, 255, 255, 255));
    }

    /// <summary>`CTargetID::ShouldDraw` (:504), gated by each sibling's own extra condition (:1201, :1213).</summary>
    /// <param name="state">The local player.</param>
    /// <returns>Whether it draws.</returns>
    public virtual bool ShouldDraw(HudState state)
    {
        if (!state.HasLocalPlayer || !ExtraShouldDrawGate(state))
        {
            return false;
        }

        _floatingHealthIcon?.Tick(state);

        if (!HudVisibility.ShouldDraw(state, this) || state.Rules.ShowMatchSummary || state.Player(state.LocalIndex) is null
            || state.Conditions.Has(ConditionTaunting))
        {
            UpdateFloatingHealthIconVisibility(state, visible: false);
            return false;
        }

        TargetIndex = CalculateTargetIndex(state);

        if (TargetIndex == 0)
        {
            if (_targetRetainFov == 0f)
            {
                // "Check to see if we should clear our ID", else "Keep re-using the old one".
                if (_lastChangeTime != 0f && state.CurTime > _lastChangeTime)
                {
                    (_lastChangeTime, _lastEntIndex) = (0f, 0);
                }
                else
                {
                    TargetIndex = _lastEntIndex;
                }
            }

            // "If we're showing a floating health icon, and no longer have a target, hide it".
            UpdateFloatingHealthIconVisibility(state, visible: false);
        }
        else
        {
            _lastChangeTime = state.CurTime;

            if (TargetIndex != _lastScannedEntIndex)
            {
                // "If we switched to another, valid target for a floating health icon, recreate it on the next pass".
                DeleteFloatingHealthIcon();
                _lastScannedEntIndex = TargetIndex;
            }
        }

        bool valid = IsValidIdTarget(state, TargetIndex, out float retainFov);

        if (!valid)
        {
            _lastEntIndex = 0;
            return false;
        }

        if (!Visible || TargetIndex != _lastEntIndex)
        {
            _lastEntIndex = TargetIndex;
            _layoutOnUpdate = true;
            _targetRetainFov = retainFov;

            if (_avatar is not null)
            {
                _avatar.Visible = false;
            }
        }

        UpdateId(state);
        return true;
    }

    /// <summary>`m_pFloatingHealthIcon`, or null.</summary>
    public TfFloatingHealthIcon? FloatingHealthIcon => _floatingHealthIcon;

    /// <summary>`UpdateFloatingHealthIconVisibility` (:321), comparing against the icon's own `IsVisible` override.</summary>
    private void UpdateFloatingHealthIconVisibility(HudState state, bool visible)
    {
        if (_floatingHealthIcon is { } icon && icon.IsVisibleNow(state) != visible)
        {
            icon.SetVisible(state, visible);
        }
    }

    /// <summary>`m_pFloatingHealthIcon->MarkForDeletion(); m_pFloatingHealthIcon = NULL;`.</summary>
    private void DeleteFloatingHealthIcon()
    {
        _floatingHealthIcon?.SetParent(null);
        _floatingHealthIcon = null;
    }

    /// <summary>`LevelShutdown` (:159): the icon goes with the level.</summary>
    public void LevelShutdown() => DeleteFloatingHealthIcon();

    /// <summary>The extra gate each sibling's own `ShouldDraw` checks before `CTargetID::ShouldDraw`'s common body.</summary>
    /// <param name="state">The local player.</param>
    private protected abstract bool ExtraShouldDrawGate(HudState state);

    /// <summary>`CalculateTargetIndex( C_TFPlayer* )`, which each sibling overrides its own way.</summary>
    /// <param name="state">The local player.</param>
    private protected abstract int CalculateTargetIndex(HudState state);

    /// <summary>
    /// `CTargetID::CalculateTargetIndex`'s tail (:706): "If our target entity is already in our secondary ID, don't show it
    /// in primary."
    /// </summary>
    /// <param name="index">`GetIDTarget()`.</param>
    private protected int WithoutSecondary(int index) =>
        Parent?.FindChildByName("CSecondaryTargetID") is TfSecondaryTargetId secondary && !ReferenceEquals(secondary, this) && secondary.TargetIndex == index
            ? 0
            : index;

    /// <summary>`CTargetID::DrawHealthIcon` (:205): the health panel draws inside for a building, or with floating health off.</summary>
    private protected bool DrawHealthIcon(HudState state) => state.Building(TargetIndex) is not null || DisableFloatingHealth;

    /// <inheritdoc/>
    /// <remarks>`CTargetID::PerformLayout` (:599), `UseVR` off and no arena panel.</remarks>
    protected override void PerformLayout()
    {
        base.PerformLayout();

        HudState state = HudViewport.Of(this)?.State ?? default;
        bool drawHealthIcon = DrawHealthIcon(state);
        bool avatarVisible = _avatar is { Visible: true };
        int width = XRes(5) + XRes(10);

        if (drawHealthIcon)
        {
            width += TargetHealth.Wide;
        }

        if (avatarVisible)
        {
            width += _avatar!.Wide + XRes(2);
        }

        if (_nameLabel is not null && _dataLabel is not null)
        {
            (int nameWide, _) = _nameLabel.GetContentSize();
            (int dataWide, _) = _dataLabel.GetContentSize();

            width += Math.Max(nameWide, dataWide);

            if (_bgPanel is not null)
            {
                (_bgPanel.Wide, _bgPanel.Tall) = (width, Tall);
            }

            int wideExtra = (drawHealthIcon ? TargetHealth.Wide : 0) + (avatarVisible ? _avatar!.Wide + XRes(4) : 0);
            int buffer = avatarVisible ? 6 : 8;

            _nameLabel.X = XRes(buffer) + wideExtra;
            _dataLabel.X = XRes(buffer) + wideExtra;

            if (_killStreakIcon is not null)
            {
                // `cl_hud_minmode` 0.
                _killStreakIcon.X = XRes(9) + wideExtra;
            }
        }

        // "Put the moveable icon to the right hand of our panel" (:658).
        if (_moveableSubPanel is { Visible: true } subPanel)
        {
            if (_moveableKeyLabel is { } keyLabel && _moveableIcon is { } icon && _moveableSymbolIcon is { } symbolIcon
                && _moveableIconBg is { } iconBg)
            {
                keyLabel.SizeToContents();

                int indent = XRes(4);
                int moveWide = Math.Max(XRes(16) + keyLabel.Wide + indent, icon.Wide + indent + XRes(8));
                keyLabel.Wide = moveWide;
                (subPanel.Wide, subPanel.Tall) = (moveWide, Tall);
                (subPanel.X, subPanel.Y) = (width - indent, 0);

                int y = keyLabel.Y;
                (symbolIcon.X, symbolIcon.Y) = ((moveWide - symbolIcon.Wide) / 2, y - symbolIcon.Tall);
                (icon.X, icon.Y) = ((moveWide - icon.Wide) / 2, y - icon.Tall);
                (iconBg.Wide, iconBg.Tall) = (subPanel.Wide, subPanel.Tall);
            }

            // "Now add our extra width to the total size" (:680).
            width += subPanel.Wide;
        }

        Wide = width;
        (X, Y) = ((int)((_screenWide - width) * 0.5), _originalY + YRes(YOffset));
    }

    /// <summary>`CTargetID::IsValidIDTarget` (:340), with no retain field of view to test against — as `ShouldDraw` calls it.</summary>
    /// <param name="state">The local player.</param>
    /// <param name="index">The candidate.</param>
    /// <param name="retainFov">`flNewTargetRetainFOV`: non-zero for a target kept while it stays in view.</param>
    /// <returns>Whether it is a target to show.</returns>
    private protected virtual bool IsValidIdTarget(HudState state, int index, out float retainFov)
    {
        retainFov = 0f;

        if (index == 0 || state.Player(state.LocalIndex) is not { } local)
        {
            return false;
        }

        ScenePlayer? player = state.Player(index);
        SceneBuilding? building = state.Building(index);

        if (player is null && building is null)
        {
            return false;
        }

        int targetTeam = player?.Team ?? building?.Team ?? 0;
        int hideEnemyHealth = (int)LocalAttribute(local, "hide_enemy_health");
        bool inSameTeam = InSameDisguisedTeam(local, targetTeam, player);
        bool spy = state.PlayerClass == ClassSpy && hideEnemyHealth == 0;

        if (state.Rules.MannVsMachine)
        {
            // "We don't want to show health bars to the spy in MVM because it's distracting".
            spy = false;

            if (local.Conditions.Has(PlayerConditions.Disguised) && local.DisguiseTeam != local.Team)
            {
                int theirApparentTeam = player is { } disguised && disguised.Conditions.Has(PlayerConditions.Disguised)
                    ? disguised.DisguiseTeam ?? 0
                    : targetTeam;

                if (local.DisguiseTeam == theirApparentTeam)
                {
                    inSameTeam = false;
                }
            }
        }

        bool spectator = state.Team == TeamSpectator;
        bool valid = false;
        bool healthBarVisible = ShouldHealthBarBeVisible(state, local, player);
        bool show = healthBarVisible;

        if (player is { } target)
        {
            bool stealthed = false;
            int seeEnemyHealth = 0;

            if (target.Conditions.IsStealthed)
            {
                stealthed = true;
                healthBarVisible = false;
                show = false;
            }

            if (!stealthed)
            {
                seeEnemyHealth = (int)LocalAttribute(local, "see_enemy_health");
            }

            bool maintainInFov = local.Team != targetTeam;

            if (healthBarVisible)
            {
                bool enemyMiniBoss = target.IsMiniBoss && targetTeam != local.Team;

                show = enemyMiniBoss;

                if (show)
                {
                    // "Minibosses keep the health indicator up within a small FOV until a different valid target is selected".
                    maintainInFov = true;
                }
            }

            if (maintainInFov)
            {
                // The retain FOV (:449-452) — non-zero, whatever the distance, which is all `ShouldDraw` reads of it.
                float distance = EyeDistance(local, target);
                float interp = (800f - Math.Min(distance, 800f)) / 800f;

                retainFov = (interp * interp * 13f) + 0.75f;
            }

            valid = spectator || local.Team == targetTeam || ((inSameTeam || spy || seeEnemyHealth != 0) && !stealthed);
        }

        if (show || healthBarVisible)
        {
            // "See if we're re-targeting our previous".
            if (_floatingHealthIcon is { } icon)
            {
                if (icon.Entity == index)
                {
                    UpdateFloatingHealthIconVisibility(state, visible: true);
                }
                else
                {
                    DeleteFloatingHealthIcon();
                }
            }

            // "Recreate the floating health icon if there isn't one, we're not a spectator, and we're not a spy or this was a
            // robot from Robot Destruction-Mode".
            if (_floatingHealthIcon is null && !spectator && (!spy || healthBarVisible) && !DrawHealthIcon(state) && Parent is { } viewport)
            {
                _floatingHealthIcon = new TfFloatingHealthIcon(
                    viewport, index, player is { IsMiniBoss: true }, (now, target) => now.Player(now.LocalIndex) is { } me && ShouldHealthBarBeVisible(now, me, target), state.RealTime);
            }
        }
        else if (building is not null && (inSameTeam || spy))
        {
            valid = true;
        }
        else
        {
            UpdateFloatingHealthIconVisibility(state, visible: false);
        }

        return valid;
    }

    /// <summary>`ShouldHealthBarBeVisible` (:78): whether the floating health bar would follow this target.</summary>
    private bool ShouldHealthBarBeVisible(HudState state, ScenePlayer local, ScenePlayer? target)
    {
        if (DisableFloatingHealth)
        {
            return false;
        }

        // `IsHealthBarVisible`: a building's is the base's false; a player's is `IsMiniBoss()` (c_tf_player.cpp:10731).
        if (target is { IsMiniBoss: true })
        {
            return true;
        }

        if (target is not { } player)
        {
            return false;
        }

        int targetTeam = player.Team ?? 0;

        if ((int)LocalAttribute(local, "hide_enemy_health") > 0 && local.Team != targetTeam)
        {
            return false;
        }

        return state.PlayerClass == ClassSpy || local.Team == targetTeam || InSameDisguisedTeam(local, targetTeam, target)
            || (int)LocalAttribute(local, "see_enemy_health") != 0;
    }

    /// <summary>`CTargetID::UpdateID` (:719).</summary>
    private protected void UpdateId(HudState state)
    {
        if (state.Player(state.LocalIndex) is not { } local)
        {
            return;
        }

        ScenePlayer? player = state.Player(TargetIndex);
        SceneBuilding? building = state.Building(TargetIndex);

        if (player is null && building is null)
        {
            return;
        }

        string id = string.Empty;
        string data = string.Empty;
        float health = 0f;
        float maxHealth = 1f;
        int maxBuffedHealth = 0;
        int targetTeam = player?.Team ?? building?.Team ?? 0;
        string? actionIcon = null;
        string? actionCommand = null;

        TargetHealth.Building = false;
        TargetHealth.SetLevel(-1);

        if (player is { } target)
        {
            (id, data, health, maxHealth, maxBuffedHealth, targetTeam) = PlayerId(state, local, target, targetTeam);
        }
        else if (building is { } obj)
        {
            id = ObjectIdString(state, local, obj);
            data = ObjectDataString(state, obj);
            (health, maxHealth) = (obj.Health, obj.MaxHealth);
            TargetHealth.Building = true;

            if (_killStreakIcon is not null)
            {
                _killStreakIcon.Visible = false;
            }

            // "Switch the icon to the right object" (:885) — only for a building the local Engineer built himself.
            if (obj.BuilderEntityIndex == local.EntityIndex)
            {
                if (CanPickupBuilding(state, local, obj))
                {
                    actionCommand = "+attack2";
                }

                actionIcon = obj.ObjectType switch
                {
                    SceneBuilding.Teleporter => obj.ObjectMode == SceneBuilding.TeleporterEntrance
                        ? "obj_status_tele_entrance"
                        : "obj_status_tele_exit",
                    SceneBuilding.Sentrygun => obj.UpgradeLevel switch
                    {
                        3 => "obj_status_sentrygun_3",
                        2 => "obj_status_sentrygun_2",
                        _ => "obj_status_sentrygun_1",
                    },
                    _ => "obj_status_dispenser",
                };
            }
        }

        // "fixup for health being 1 when dead".
        if (player is { IsAlive: false })
        {
            health = 0f;
        }

        TargetHealth.SetHealth((int)health, (int)maxHealth, maxBuffedHealth);
        TargetHealth.Visible = DrawHealthIcon(state);

        // The moveable sub-panel — the builder's own pick-up prompt (:1010-1047).
        if (_moveableSubPanel is { } subPanel)
        {
            bool showActionKey = actionCommand is not null;

            if (subPanel.Visible != showActionKey)
            {
                subPanel.Visible = showActionKey;
                _layoutOnUpdate = true;
            }

            if (subPanel.Visible)
            {
                subPanel.SetDialogVariable("movekey", state.BuildingPickupKey ?? string.Empty);
            }

            if (_moveableIcon is not null)
            {
                if (actionIcon is not null)
                {
                    _moveableIcon.SetIcon(actionIcon);
                }

                _moveableIcon.Visible = actionIcon is not null;
            }
        }

        SetLabels(id, data);

        if (_bgPanel is not null)
        {
            _bgPanel.LocalTeam = targetTeam;
            _bgPanel.SetAnimationValue("alpha", (float)BackgroundAlpha);
        }
    }

    /// <summary>`CTFPlayer::CanPickupBuilding` (tf_player_shared.cpp:12421), ported in full.</summary>
    private bool CanPickupBuilding(HudState state, ScenePlayer local, SceneBuilding obj)
    {
        if (obj.Building) // :12426
        {
            return false;
        }

        // `IsUpgrading()`: hardcoded false except for a sentry's `SENTRY_STATE_UPGRADING` (:12429).
        if (obj.ObjectType == SceneBuilding.Sentrygun && obj.SentryState == SceneBuilding.SentryStateUpgrading)
        {
            return false;
        }

        if (obj.Sapped) // `HasSapper()`, :12432
        {
            return false;
        }

        if (obj.PlasmaDisabled) // :12435
        {
            return false;
        }

        if (obj.UpgradeLevel != obj.HighestUpgradeLevel) // :12439
        {
            return false;
        }

        if (!local.IsAlive) // :12442
        {
            return false;
        }

        if (local.CarryingObject) // `IsCarryingObject()`, :12445
        {
            return false;
        }

        if (IsLoserStateStunned(local) || IsControlStunned(local)) // :12447
        {
            return false;
        }

        if (IsLoser(state, local)) // :12450
        {
            return false;
        }

        // `State_Get() != GR_STATE_RND_RUNNING && != GR_STATE_STALEMATE && != GR_STATE_BETWEEN_RNDS` (:12452).
        if (state.RoundState is not (RoundStateRndRunning or HudState.RoundStateStalemate or RoundStateBetweenRounds))
        {
            return false;
        }

        if (local.Conditions.Has(ConditionGrapplingHook)) // :12457
        {
            return false;
        }

        // "There's ammo in the clip... no switching away!" (:12461-12462).
        if (ActiveWeaponItem(local) is { } activeWeapon
            && WeaponAttribute(local, activeWeapon, "auto_fires_full_clip") != 0f
            && (local.WeaponClip1 ?? -1) > 0)
        {
            return false;
        }

        if (local.Conditions.Has(ConditionRuneKnockout)) // `GetCarryingRuneType() == RUNE_KNOCKOUT`, :12464
        {
            return false;
        }

        // `TF_BUILDING_PICKUP_RANGE` (:12475), eye-to-origin — the same view-offset simplification `EyeDistance` already
        // makes (view offset excluded; only the outcome is read).
        if (obj.Position is not { } origin)
        {
            return false;
        }

        float dx = origin.X - local.X;
        float dy = origin.Y - local.Y;
        float dz = origin.Z - local.Z;
        float distanceSq = (dx * dx) + (dy * dy) + (dz * dz);

        // `CALL_ATTRIB_HOOK_INT_ON_OTHER( pWeapon, iIncreasedRangeCost, building_teleporting_pickup )` (:12481) — an
        // attribute hooked on the WEAPON, applying its OWNER's providers (`AttributeHooks.OnWeapon`), same as :12462.
        float increasedRangeCost = ActiveWeaponItem(local) is { } rangeWeapon
            ? WeaponAttribute(local, rangeWeapon, "building_teleporting_pickup")
            : 0f;

        if (increasedRangeCost != 0f)
        {
            // "False on deadzone" (:12486).
            if (distanceSq > BuildingPickupRangeSq && distanceSq < RescueMinRangeSq)
            {
                return false;
            }

            int metal = local.Ammo is { } ammo && Tf2DemoSalvage.Scene.TfWeaponData.AmmoMetal < ammo.Count
                ? ammo[Tf2DemoSalvage.Scene.TfWeaponData.AmmoMetal]
                : 0;

            if (distanceSq >= RescueMinRangeSq && metal < increasedRangeCost)
            {
                return false;
            }

            return true;
        }

        if (distanceSq > BuildingPickupRangeSq)
        {
            return false;
        }

        if (state.Rules.InTraining) // :12492
        {
            return obj.ObjectType switch
            {
                SceneBuilding.Dispenser => state.TrainingCanPickupDispenser,
                SceneBuilding.Teleporter => obj.ObjectMode == SceneBuilding.TeleporterEntrance
                    ? state.TrainingCanPickupTeleEntrance
                    : state.TrainingCanPickupTeleExit,
                SceneBuilding.Sentrygun => state.TrainingCanPickupSentry,
                _ => true,
            };
        }

        return true;
    }

    /// <summary>`CTFPlayerShared::IsLoserStateStunned` (tf_player_shared.cpp:9966): stunned, and the stun says so.</summary>
    /// <remarks>
    /// `GetActiveStunInfo()` on the CLIENT is non-null exactly when `m_iStunIndex &gt;= 0` (:7474-7475) and its
    /// `iStunFlags` is `m_iStunFlags` verbatim (:7462-7463) — the client keeps no separate per-attacker stun list, so
    /// this reads the two networked fields directly rather than reconstructing one.
    /// </remarks>
    private static bool IsLoserStateStunned(ScenePlayer local) =>
        local.StunIndex is >= 0 && local.Conditions.Has(ConditionStunned) && ((local.StunFlags ?? 0) & StunLoserState) != 0;

    /// <summary>`CTFPlayerShared::IsControlStunned` (:9952) — as <see cref="IsLoserStateStunned"/>, `TF_STUN_CONTROLS`.</summary>
    private static bool IsControlStunned(ScenePlayer local) =>
        local.StunIndex is >= 0 && local.Conditions.Has(ConditionStunned) && ((local.StunFlags ?? 0) & StunControls) != 0;

    /// <summary>`CTFPlayerShared::IsLoser` (:13654).</summary>
    private static bool IsLoser(HudState state, ScenePlayer local)
    {
        if (state.AlwaysLoser) // `tf_always_loser.GetBool()`, :13656
        {
            return true;
        }

        // "No loser mode in competitive" (:13663) — `IsMatchTypeCompetitive()`, not `IsCompetitiveMode()` (D89 audit).
        if (state.Rules.IsMatchTypeCompetitive)
        {
            return false;
        }

        if (state.RoundState != RoundStateTeamWin) // :13666
        {
            return IsLoserStateStunned(local);
        }

        bool loser = state.Rules.WinningTeam != local.Team; // :13671

        // "don't reveal disguised spies" (:13675-13680).
        if (loser && local.PlayerClass == ClassSpy && local.Conditions.Has(PlayerConditions.Disguised)
            && local.DisguiseTeam == state.Rules.WinningTeam)
        {
            loser = false;
        }

        return loser;
    }

    /// <summary>The active weapon among a player's items, or null — the same lookup `TfAmmo.For` makes.</summary>
    private static SceneItem? ActiveWeaponItem(ScenePlayer local)
    {
        foreach (SceneItem item in local.Items ?? [])
        {
            if (item.EntityIndex == local.ActiveWeapon)
            {
                return item;
            }
        }

        return null;
    }

    /// <summary>`CALL_ATTRIB_HOOK_INT( value, name )` on a weapon (`AttributeHooks.OnWeapon`), from 0.</summary>
    private float WeaponAttribute(ScenePlayer local, SceneItem weapon, string attributeClass) =>
        HudViewport.Of(this)?.WeaponAttribute?.Invoke(local, weapon, attributeClass, 0f) ?? 0f;

    /// <summary>`UpdateID`'s player branch (:754-865).</summary>
    private (string Id, string Data, float Health, float MaxHealth, int MaxBuffedHealth, int TargetTeam) PlayerId(
        HudState state, ScenePlayer local, ScenePlayer target, int targetTeam)
    {
        string name = state.Names?.GetValueOrDefault(TargetIndex) ?? string.Empty;
        bool disguisedTarget = false;
        bool disguisedEnemy = false;

        // "determine if the target is a disguised spy (either friendly or enemy)".
        if (target.Conditions.Has(PlayerConditions.Disguised) && !target.Conditions.IsStealthed)
        {
            disguisedTarget = true;

            if (local.Team != targetTeam)
            {
                targetTeam = target.DisguiseTeam ?? 0;
            }

            if (IsEnemyPlayer(local.Team ?? 0, target.Team ?? 0) && target.DisguiseTarget is { } disguise)
            {
                disguisedEnemy = true;
                name = state.Names?.GetValueOrDefault(disguise) ?? string.Empty;
            }
        }

        bool inSameTeam = InSameDisguisedTeam(local, target.Team ?? 0, target);
        (string data, DataKind kind) = DataString(local, target, disguisedTarget);
        string? format = null;
        bool showHealth = false;

        if (state.Team == TeamSpectator || inSameTeam || state.PlayerClass is ClassSpy or ClassMedic or ClassHeavy || disguisedEnemy)
        {
            format = "#TF_playerid_sameteam";
            showHealth = true;
        }
        else if (local.PlayerState == StateDying)
        {
            // "We're looking at an enemy who killed us."
            format = "#TF_playerid_diffteam";
            showHealth = true;
        }

        float health = 0f;
        float maxHealth = 1f;
        int maxBuffedHealth = 0;

        if (showHealth)
        {
            if (disguisedEnemy)
            {
                // `GetDisguiseMaxHealth` (tf_player_shared.cpp:8358): the disguise class's own, else the player's.
                int disguiseMax = HudViewport.Of(this)?.Scripts?.ClassMaxHealth(target.DisguiseClass ?? 0) ?? target.MaxHealth ?? 1;
                int disguiseHealth = target.DisguiseHealth ?? 0;

                (health, maxHealth, maxBuffedHealth) = (disguiseHealth, disguiseMax, HudState.GetMaxBuffedHealth(disguiseMax, disguiseMax, disguiseHealth));
            }
            else
            {
                int current = target.EntityHealth ?? 0;
                int max = target.MaxHealth ?? 1;

                (health, maxHealth, maxBuffedHealth) = (current, max, HudState.GetMaxBuffedHealth(target.MaxHealthForBuffing ?? 1, max, current));
            }
        }

        string id = format is null ? string.Empty : VguiLocalize.ConstructString(Find(format), IdChars, Prepend, name);

        // "Show target's clip state to attached medics" (:852).
        if (_ammoIcon is not null)
        {
            _ammoIcon.Visible = kind == DataKind.Ammo && data.Length > 0 && IsHealTargetOf(local, target);
        }

        if (_killStreakIcon is not null)
        {
            _killStreakIcon.Visible = kind == DataKind.KillStreak && data.Length > 0;
        }

        return (id, data, health, maxHealth, maxBuffedHealth, targetTeam);
    }

    /// <summary>`GetPrepend()`: empty but for `CSecondaryTargetID`'s "healing"/"healer" line.</summary>
    private protected virtual string Prepend => string.Empty;

    /// <summary>`C_TFPlayer::GetTargetIDDataString` (c_tf_player.cpp:9748).</summary>
    private (string Data, DataKind Kind) DataString(ScenePlayer local, ScenePlayer target, bool disguised)
    {
        string data = string.Empty;
        bool enemy = IsEnemyPlayer(local.Team ?? 0, target.Team ?? 0);

        if (disguised)
        {
            if (!enemy)
            {
                // "The target is a disguised friendly spy": the disguise's team and class.
                bool asEnemy = target.DisguiseTeam != target.Team;

                data = VguiLocalize.ConstructString(
                    Find("#TF_playerid_friendlyspy_disguise"), IdChars, Find(asEnemy ? "#TF_enemy" : "#TF_friendly") ?? string.Empty, LocalizedClassName(target.DisguiseClass));
            }
            else if (target.DisguiseClass == ClassSpy)
            {
                // "an enemy spy disguised as a friendly spy. Show a fake team & class ID element."
                data = VguiLocalize.ConstructString(
                    Find("#TF_playerid_friendlyspy_disguise"), IdChars, Find("#TF_enemy") ?? string.Empty, LocalizedClassName(target.DisguiseMaskClass));
            }
        }

        if (target.PlayerClass == ClassMedic)
        {
            string charge = MathF.Round((target.Medigun?.Charge ?? 0f) * 100f).ToString("0", CultureInfo.InvariantCulture);

            return (target.Medigun is { Quality: not 0 } medigun
                ? VguiLocalize.ConstructString(Find("#TF_playerid_mediccharge_wpn"), IdChars, charge, HudViewport.Of(this)?.ItemName?.Invoke(medigun.Definition, medigun.Quality) ?? string.Empty)
                : VguiLocalize.ConstructString(Find("#TF_playerid_mediccharge"), IdChars, charge), DataKind.None);
        }

        if (disguised && target.DisguiseClass == ClassMedic && enemy)
        {
            // "Show a fake charge level for a disguised enemy medic."
            return (VguiLocalize.ConstructString(Find("#TF_playerid_mediccharge"), IdChars, "0"), DataKind.None);
        }

        // A local medic sees his heal target's clip (:9809-9840). **Not modelled:** `weapon_blocks_healing`'s no-heal line
        // (:9815), which needs the target's weapon's attributes — a recording carries only the recorder's own items.
        if (local.PlayerClass == ClassMedic && target.ActiveWeapon is not null && data.Length == 0
            && target.ActiveWeaponClip is { } clip and >= 0 && IsHealTargetOf(local, target))
        {
            return (VguiLocalize.ConstructString(Find("#TF_playerid_ammo"), IdChars, clip.ToString(CultureInfo.InvariantCulture)), DataKind.Ammo);
        }

        // "Check for kill streak data".
        if (target.KillStreak is > 0 and var streak)
        {
            return (VguiLocalize.ConstructString(Find("#TF_playerid_ammo"), IdChars, streak.ToString(CultureInfo.InvariantCulture)), DataKind.KillStreak);
        }

        return (data, DataKind.None);
    }

    /// <summary>`bIsAmmoData` / `bIsKillStreakData`: which icon the data line wants.</summary>
    private enum DataKind
    {
        None,
        Ammo,
        KillStreak,
    }

    /// <summary>`ToTFPlayer( pLocalTFPlayer->MedicGetHealTarget() ) == pPlayer` (tf_player_shared.cpp:13021).</summary>
    private static bool IsHealTargetOf(ScenePlayer local, ScenePlayer target) =>
        local.PlayerClass == ClassMedic && local.ActiveMedigun?.HealTarget == target.EntityIndex;

    /// <summary>`C_BaseObject::GetTargetIDString` (c_baseobject.cpp:892), not as a spectator.</summary>
    private string ObjectIdString(HudState state, ScenePlayer local, SceneBuilding obj)
    {
        if (!InSameDisguisedTeam(local, obj.Team ?? 0, null) && state.PlayerClass != ClassSpy)
        {
            return string.Empty;
        }

        (string? statusName, string? modeName, int altModes) = ObjectInfo(obj);
        string objectName = Find(StatusName(obj, statusName)) ?? string.Empty;
        string builder = obj.BuilderEntityIndex is { } owner ? state.Names?.GetValueOrDefault(owner) ?? string.Empty : string.Empty;
        string format = obj.MiniBuilding && !obj.DisposableBuilding ? "#TF_playerid_object_mini" : "#TF_playerid_object";

        if (altModes > 0)
        {
            // Valve's own string drops the '#' here; `Find` skips one only when present.
            return Find("TF_playerid_object_mode") is { } withMode
                ? VguiLocalize.ConstructString(withMode, IdChars, objectName, builder, Find(modeName ?? string.Empty) ?? string.Empty)
                : string.Empty;
        }

        return Find(format) is { } localized ? VguiLocalize.ConstructString(localized, IdChars, objectName, builder) : string.Empty;
    }

    /// <summary>`C_BaseObject::GetTargetIDDataString` (c_baseobject.cpp:965), and a teleporter's override (c_obj_teleporter.cpp:334).</summary>
    private string ObjectDataString(HudState state, SceneBuilding obj)
    {
        // "Sentryguns have models for each level, so we don't show it in their target ID."
        bool showLevel = obj.ObjectType != ObjectSentrygun;
        string level = obj.UpgradeLevel.ToString(CultureInfo.InvariantCulture);
        string baseString = string.Empty;

        if (obj.UpgradeLevel >= 3)
        {
            if (showLevel)
            {
                baseString = VguiLocalize.ConstructString(Find("#TF_playerid_object_level"), IdChars, level);
            }
        }
        else if (!obj.MiniBuilding && !obj.DisposableBuilding)
        {
            // "level 1 and 2 show upgrade progress".
            string progress = string.Create(CultureInfo.InvariantCulture, $"{obj.UpgradeMetal} / {obj.UpgradeMetalRequired}");

            baseString = showLevel
                ? VguiLocalize.ConstructString(Find("#TF_playerid_object_upgrading_level"), IdChars, level, progress)
                : VguiLocalize.ConstructString(Find("#TF_playerid_object_upgrading"), IdChars, progress);
        }

        if (obj.ObjectType != ObjectTeleporter)
        {
            return baseString;
        }

        string data = string.Empty;

        if (obj.TeleporterState == TeleporterStateRecharging && obj.TeleporterRechargeTime is { } recharge && state.ServerTime < recharge)
        {
            float duration = obj.TeleporterRechargeDuration ?? 0f;
            float percent = Math.Clamp((recharge - state.ServerTime) / duration, 0f, 1f);
            string recharging = MathF.Round(100f - (percent * 100f)).ToString("0", CultureInfo.InvariantCulture);

            data = VguiLocalize.ConstructString(Find("#TF_playerid_object_recharging"), IdChars, recharging);
        }
        else if (obj.TeleporterState == TeleporterStateIdle)
        {
            data = VguiLocalize.ConstructString(Find("#TF_playerid_teleporter_nomatch"), IdChars);
        }

        // "Concatenate the base level string".
        return data + "   " + baseString;
    }

    /// <summary>`GetObjectInfo( GetType() )`'s alt mode for this building's mode.</summary>
    private (string? StatusName, string? ModeName, int AltModes) ObjectInfo(SceneBuilding obj) =>
        HudViewport.Of(this)?.Scripts?.ObjectInfo(obj.ObjectType, obj.ObjectMode) ?? (null, null, 0);

    /// <summary>`GetStatusName()`: a sentry's override (c_obj_sentrygun.cpp:748), else the object info's (c_baseobject.cpp:365).</summary>
    private static string StatusName(SceneBuilding obj, string? statusName)
    {
        if (obj.ObjectType != ObjectSentrygun)
        {
            return statusName ?? string.Empty;
        }

        return obj.DisposableBuilding ? "#TF_Object_Sentry_Disp" : "#TF_Object_Sentry";
    }

    /// <summary>`C_TFPlayer::InSameDisguisedTeam` (c_tf_player.cpp:10129), `m_bIsCoaching` not modelled.</summary>
    private protected static bool InSameDisguisedTeam(ScenePlayer local, int targetTeam, ScenePlayer? target)
    {
        int localTeam = local.Team ?? 0;
        int myApparentTeam = local.Conditions.Has(PlayerConditions.Disguised) ? local.DisguiseTeam ?? 0 : localTeam;
        int theirApparentTeam = target is { } player && player.Conditions.Has(PlayerConditions.Disguised) ? player.DisguiseTeam ?? 0 : targetTeam;

        return myApparentTeam == theirApparentTeam || localTeam == targetTeam || theirApparentTeam == localTeam;
    }

    /// <summary>`C_TFPlayer::IsEnemyPlayer` (c_tf_player.cpp:5384): only RED against BLU and back.</summary>
    private protected static bool IsEnemyPlayer(int localTeam, int targetTeam) => localTeam switch
    {
        TeamRed => targetTeam == TeamBlue,
        TeamBlue => targetTeam == TeamRed,
        _ => false,
    };

    /// <summary>`CALL_ATTRIB_HOOK_FLOAT_ON_OTHER( pLocalTFPlayer, …, name )` from 0.</summary>
    private float LocalAttribute(ScenePlayer local, string attributeClass) =>
        HudViewport.Of(this)?.PlayerAttribute?.Invoke(local, attributeClass, 0f) ?? 0f;

    /// <summary>`VectorNormalize( pEnt->EyePosition() - pLocalTFPlayer->EyePosition() )`, both at their origins' height.</summary>
    /// <remarks>**Interpolated:** eye heights (`m_vecViewOffset`) are left out; only whether the result is non-zero is read.</remarks>
    private static float EyeDistance(ScenePlayer local, ScenePlayer target)
    {
        float dx = target.X - local.X;
        float dy = target.Y - local.Y;
        float dz = target.Z - local.Z;

        return MathF.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }

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

    /// <summary>A class's localized name, `g_aPlayerClassNames[ index ]`.</summary>
    private string LocalizedClassName(int? playerClass) => Find(ClassNames[Math.Clamp(playerClass ?? 0, 0, ClassNames.Length - 1)]) ?? string.Empty;

    /// <summary>`g_pVGuiLocalize->Find`: a leading '#' is skipped when there is one.</summary>
    private protected string? Find(string token) =>
        HudViewport.Of(this)?.Context?.Localize?.Invoke(token.StartsWith('#') ? token[1..] : token);

    private protected int XRes(int x) => (int)(x * (_screenWide / 640.0));

    private protected int YRes(int y) => (int)(y * (_screenTall / 480.0));
}

/// <summary>`CSpectatorTargetID` over `CTargetID`: the spectated player's name, health and data.</summary>
/// <remarks>
/// Drawn only in an observer mode other than freeze cam (:1213). The target is the observer target in eye (:1243), else
/// `GetIDTarget()` — the observer target in death cam and chase (`UpdateIDTarget`, c_tf_player.cpp:7061). Its own
/// `ApplySchemeSettings` (:1263) additionally hides `TargetIDBG` and shows the blue spectator background; its own
/// `PerformLayout` (:1284) always counts the health panel and recolours that background red or blue by the target's team.
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
        return WithoutSecondary(state.ObserverMode is ObserverModes.DeathCam or ObserverModes.Chase
            && state.ObserverTarget != 0 && state.ObserverTarget != state.LocalIndex
            ? state.ObserverTarget
            : 0);
    }

    /// <inheritdoc/>
    /// <remarks>`CSpectatorTargetID::PerformLayout` (:1284), `tf_spectator_target_location` 0.</remarks>
    protected override void PerformLayout()
    {
        if (_nameLabel is not null && _dataLabel is not null)
        {
            (int nameWide, _) = _nameLabel.GetContentSize();
            (int dataWide, _) = _dataLabel.GetContentSize();
            int width = TargetHealth.Wide + XRes(5) + XRes(10) + Math.Max(nameWide, dataWide);
            int buffer = _avatar is { Visible: true } ? 6 : 8;

            Wide = width;
            _nameLabel.X = XRes(buffer) + TargetHealth.Wide;
            _dataLabel.X = XRes(buffer) + TargetHealth.Wide;

            if (_killStreakIcon is not null)
            {
                _killStreakIcon.X = XRes(10) + TargetHealth.Wide;
            }

            (X, Y) = ((int)((_screenWide - width) * 0.5), _originalY);
        }

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
/// layout is the shared `CTargetID::PerformLayout`.
/// **Not modelled here:** the "minus `CSecondaryTargetID`'s current target" subtraction (:707) — `CSecondaryTargetID` is
/// not ported yet.
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
    private protected override int CalculateTargetIndex(HudState state) => WithoutSecondary(state.IdTarget ?? 0);
}

/// <summary>`CSecondaryTargetID` (:1126): the local medic's heal target, or the medic healing the local player.</summary>
/// <remarks>
/// `ShouldDraw` (:1138) is `CTargetID`'s with no observer gate; its hiding of lower-priority "mid" elements waits for the
/// render groups. `CalculateTargetIndex` (:1165): `MedicGetHealTarget()` first, then `GetHealer()` — which each medigun's
/// `ClientThink` (tf_weapon_medigun.cpp:2273) sets on its heal target through `SetHealer` (c_tf_player.cpp:8852), keeping
/// the highest charge.
/// **Interpolated:** mediguns think in entity order here, so of two equal charges the later entity wins; the game's
/// order is its client-think list's.
/// </remarks>
public sealed class TfSecondaryTargetId : TfTargetId
{
    private const int ClassMedicId = 5;
    private string _prepend = string.Empty;
    private bool _wasHidingLowerElements;

    /// <summary>`CTargetID( "CSecondaryTargetID" )`.</summary>
    /// <param name="viewport">The viewport.</param>
    public TfSecondaryTargetId(VguiPanel viewport)
        : base(viewport, "CSecondaryTargetID")
    {
    }

    /// <inheritdoc/>
    private protected override string Prepend => _prepend;

    /// <summary>`CSecondaryTargetID::ShouldDraw` (:1138): drawing locks the "mid" group against lower priorities; not, unlocks it.</summary>
    /// <param name="state">The local player.</param>
    /// <returns>Whether it draws.</returns>
    public override bool ShouldDraw(HudState state)
    {
        bool draw = base.ShouldDraw(state);

        if (HudViewport.Of(this) is { } viewport && draw != _wasHidingLowerElements)
        {
            if (draw)
            {
                viewport.LockRenderGroup("mid", this);
            }
            else
            {
                viewport.UnlockRenderGroup("mid", this);
            }

            _wasHidingLowerElements = draw;
        }

        return draw;
    }

    /// <summary>No gate of its own: `CSecondaryTargetID::ShouldDraw` calls `BaseClass::ShouldDraw()` directly.</summary>
    private protected override bool ExtraShouldDrawGate(HudState state) => true;

    /// <summary>`CSecondaryTargetID::CalculateTargetIndex` (:1165).</summary>
    private protected override int CalculateTargetIndex(HudState state)
    {
        // "If we're a medic & we're healing someone, target him." `MedicGetHealTarget` (tf_player_shared.cpp:13021).
        if (state.Player(state.LocalIndex) is { PlayerClass: ClassMedicId, ActiveMedigun: { HealTarget: { } healTarget } } && healTarget != 0)
        {
            if (healTarget != TargetIndex)
            {
                _prepend = Find("#TF_playerid_healtarget") ?? string.Empty;
            }

            return healTarget;
        }

        // "If we have a healer, target him."
        if (Healer(state) is { } healer)
        {
            if (healer != TargetIndex)
            {
                _prepend = Find("#TF_playerid_healer") ?? string.Empty;
            }

            return healer;
        }

        if (TargetIndex != 0)
        {
            _prepend = string.Empty;
        }

        return 0;
    }

    /// <summary>`GetHealer()`: of the living medics whose medigun in hand heals the local player, the highest charge.</summary>
    private static int? Healer(HudState state)
    {
        int? healer = null;
        float healerCharge = 0f;

        foreach (ScenePlayer medic in state.Players ?? [])
        {
            if (medic.ActiveMedigun is not { HealTarget: { } target, Charge: var charge } || target != state.LocalIndex || !medic.IsAlive)
            {
                continue;
            }

            // `SetHealer`: "if ( m_flHealerChargeLevel > flChargeLevel ) return;".
            if (healer is not null && healerCharge > charge)
            {
                continue;
            }

            (healer, healerCharge) = (medic.EntityIndex, charge);
        }

        return healer;
    }
}
