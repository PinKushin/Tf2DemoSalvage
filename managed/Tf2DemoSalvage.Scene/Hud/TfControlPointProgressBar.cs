using System;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>`CControlPointProgressBar` (game/client/hud_controlpointicons.cpp:1371): the teardrop above the point the recorder stands on.</summary>
/// <remarks>
/// **From state each frame, where the engine sets it up at `controlpoint_starttouch`, `endtouch` and `updatecapping`**
/// (:847-891, :1183-1216), so text that changes with no such event, such as a point unlocking, updates here at once.
/// **Not ported:** `IsControlStunned` and the disguise team of `PlayerMayCapturePoint`, which need state not decoded; a
/// disguised spy is treated as disguised as the enemy. The "mid" render group lock of `IsVisible` (:1473). The sideways
/// teardrop of `CP_DIR_NE` and `CP_DIR_NW` (:1434-1450), which a custom icon layout sets. `GetActiveRoundTimer` is read as
/// any round timer.
/// </remarks>
/// <param name="parent">The objective status.</param>
public sealed class TfControlPointProgressBar(VguiPanel parent) : VguiEditablePanel(parent, "ControlPointProgressBar")
{
    private const int TeamRed = 2;
    private const int TeamBlue = 3;
    private const int RoundRunning = 4;
    private const int RoundTeamWin = 5;
    private const int GameTypeArena = 4;
    private const int GameTypeMvm = 5;

    // ETFCond (tf_shareddefs.h).
    private const int ConditionPhase = 14;
    private const int ConditionMegaHeal = 28;

    private VguiCircularProgressBar? _bar;
    private VguiLabel? _barText;
    private VguiIconPanel? _teardrop;
    private VguiIconPanel? _teardropSide;
    private VguiPanel? _blocked;
    private string? _text;

    /// <summary>`m_pAttachedToIcon`.</summary>
    public TfControlPointIcon? AttachedTo { get; private set; }

    /// <summary>The text key last shown.</summary>
    public string? Text => _text;

    /// <inheritdoc/>
    public override string ClassName => "CControlPointProgressBar";

    /// <inheritdoc/>
    /// <remarks>`ApplySchemeSettings` (:1386): the layout, its children, and the bar swept clockwise in reverse.</remarks>
    public override void ApplySchemeSettings(VguiContext context)
    {
        base.ApplySchemeSettings(context);
        LoadControlSettings("resource/UI/ControlPointProgressBar.res", context);

        _bar = FindChildByName("ProgressBar") as VguiCircularProgressBar;
        _barText = FindChildByName("ProgressText") as VguiLabel;
        _teardrop = FindChildByName("Teardrop") as VguiIconPanel;
        _teardropSide = FindChildByName("TeardropSide") as VguiIconPanel;
        _blocked = FindChildByName("Blocked");

        if (_bar is not null)
        {
            (_bar.Clockwise, _bar.ReverseProgress) = (true, true);
        }
    }

    /// <summary>`SetupForPoint` (:1499), `PerformLayout`'s north placement (:1426-1432) and `Paint`'s progress (:1482).</summary>
    /// <param name="state">The game state.</param>
    /// <param name="icon">The icon of the point the recorder stands on, or null.</param>
    /// <param name="index">Its control point.</param>
    /// <param name="context">The scheme, for the text.</param>
    public void Update(HudState state, TfControlPointIcon? icon, int index, VguiContext? context)
    {
        AttachedTo = icon;

        if (icon is null || state.Objectives is not { } objectives || state.RoundState == RoundTeamWin
            || state.ObserverMode == ObserverModes.FreezeCam || !state.HasLocalPlayer)
        {
            Visible = false;
            return;
        }

        Visible = true;

        SceneControlPoint point = objectives.Points[index];
        bool blocked = CapIsBlocked(point);

        if (!blocked && point.CappingTeam != 0 && point.CappingTeam != point.Owner && point.CappingTeam == state.Team)
        {
            if (_bar is { } bar)
            {
                bar.SetBgImage(point.CappingTeam == TeamRed ? "progress_bar_red" : "progress_bar_blu");
                bar.SetFgImage(point.Owner switch { TeamRed => "progress_bar_red", TeamBlue => "progress_bar_blu", _ => "progress_bar" });
                bar.Visible = true;

                // `GetCPCapPercentage`: the share of the capture done.
                bar.SetProgress(Math.Clamp(1f - point.LazyCapPercent, 0f, 1f));
            }

            SetChildVisible(_blocked, false);
            SetChildVisible(_barText, false);
        }
        else
        {
            SetChildVisible(_bar, false);
            SetChildVisible(_blocked, true);
            SetChildVisible(_barText, true);

            string text = BarText(state, objectives, index);

            if (!string.Equals(text, _text, StringComparison.Ordinal))
            {
                _text = text;
                _barText?.SetText(text, context?.Localize);
            }
        }

        // `CP_DIR_N`: centred over the icon, its bottom on the icon's top.
        SetChildVisible(_teardrop, true);
        SetChildVisible(_teardropSide, false);
        // `SetPos` with the icon's absolute position, as Valve does: the objective status sits at the screen's origin.
        X = icon.AbsX - ((Wide - icon.Wide) / 2);
        Y = icon.AbsY - Tall;
    }

    /// <summary>`UpdateBarText` (:1546): why the recorder is not capturing.</summary>
    /// <param name="state">The game state.</param>
    /// <param name="objectives">The objectives.</param>
    /// <param name="index">The point.</param>
    /// <returns>The localization key.</returns>
    public static string BarText(HudState state, SceneObjectives objectives, int index)
    {
        ArgumentNullException.ThrowIfNull(objectives);

        SceneControlPoint point = objectives.Points[index];
        int team = state.Team;

        if (!PointsMayBeCaptured(state) || point.Locked)
        {
            return "#Team_Capture_NotNow";
        }

        // `mp_blockstyle` is 1 (teamplayroundbased_gamerules.cpp:199).
        if (point.CappingTeam != 0 && point.CappingTeam != team)
        {
            if (point.Blocked)
            {
                return "#Team_Blocking_Capture";
            }

            if (point.Owner == 0)
            {
                return "#Team_Reverting_Capture";
            }
        }

        if (point.Owner == team)
        {
            int enemy = team == TeamRed ? TeamBlue : TeamRed;

            return team != 0 && !point.CanCap[enemy] ? "#Team_Capture_Owned" : "#Team_Capture_OwnPoint";
        }

        if (!TfControlPointIcon.TeamMayCapture(state, objectives, team, index))
        {
            return state.Rules.GameType == GameTypeArena ? "#Team_Capture_NotNow" : "#Team_Capture_Linear";
        }

        if (PlayerMayNotCapture(state) is { } reason)
        {
            return reason;
        }

        // `mp_capstyle` is 1 (:198), so the required cappers are never short.
        if (point.CappingTeam == team)
        {
            return "#Team_Capture_Blocked";
        }

        return point.CanCap[team] ? "#Team_Waiting_for_teammate" : "#Team_Cannot_Capture";
    }

    /// <summary>`CTFGameRules::PointsMayBeCaptured` (tf_gamerules.cpp:22047) over the round-based rules' (teamplayroundbased_gamerules.h:192).</summary>
    private static bool PointsMayBeCaptured(HudState state)
    {
        if (state.Rules.GameType == GameTypeMvm)
        {
            return true;
        }

        if (state.Rules.Setup && state.RoundTimers is { Count: > 0 })
        {
            return false;
        }

        return state.RoundState is RoundRunning or HudState.RoundStateStalemate && !state.Rules.WaitingForPlayers;
    }

    /// <summary>`CTFGameRules::PlayerMayCapturePoint` (tf_gamerules.cpp:16835) for what the demo carries.</summary>
    private static string? PlayerMayNotCapture(HudState state)
    {
        PlayerConditions conditions = state.Conditions;

        if (conditions.IsStealthed)
        {
            return "#Cant_cap_stealthed";
        }

        if ((conditions.IsInvulnerable || conditions.Has(ConditionMegaHeal)) && state.Rules.GameType != GameTypeMvm)
        {
            return "#Cant_cap_invuln";
        }

        if (conditions.Has(ConditionPhase))
        {
            return "#Cant_cap_invuln";
        }

        return conditions.Has(PlayerConditions.Disguised) ? "#Cant_cap_disguised" : null;
    }

    /// <summary>`CapIsBlocked` (c_team_objectiveresource.cpp:356): a capture under way with none of its team on the point.</summary>
    /// <remarks>
    /// `m_flCapTimeLeft` is `m_flLazyCapPerc * m_flTeamCapTime` (:216), the share left, and `SetCappingTeam` zeroes it for the
    /// owner or no team (:326-333).
    /// </remarks>
    private static bool CapIsBlocked(SceneControlPoint point) =>
        point.CappingTeam > 1 && point.CappingTeam != point.Owner
        && point.LazyCapPercent * point.CapTime[point.CappingTeam] > 0f
        && point.TeamMembers[point.CappingTeam] == 0;

    private static void SetChildVisible(VguiPanel? child, bool visible)
    {
        if (child is not null)
        {
            child.Visible = visible;
        }
    }
}
