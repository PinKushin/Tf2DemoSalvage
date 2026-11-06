using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>`CTFHudTeamScore` (game/client/tf/tf_hud_passtime.cpp:168): the two scores, as flag captures, and the round's target.</summary>
public sealed class TfHudPasstimeTeamScore(VguiPanel parent) : VguiEditablePanel(parent, "HudTeamScore")
{
    private const int TeamRed = 2;
    private const int TeamBlue = 3;

    private (int Blue, int Red, int Rounds) _set = (-1, -1, -1);

    /// <inheritdoc/>
    /// <remarks>`ApplySchemeSettings` (:175): the layout, with the playing-to cluster shown.</remarks>
    public override void ApplySchemeSettings(VguiContext context)
    {
        base.ApplySchemeSettings(context);
        LoadControlSettings("resource/UI/HudPasstimeTeamScore.res", context);

        if (FindChildByName("PlayingToCluster") is { } cluster)
        {
            cluster.Visible = true;
        }

        _set = (-1, -1, -1);
    }

    /// <summary>`OnTick` (:191): nothing without the logic; else each team's `GetFlagCaptures()`.</summary>
    /// <param name="state">The game state.</param>
    public void Update(HudState state)
    {
        if (state.Passtime is null)
        {
            return;
        }

        (int Blue, int Red, int Rounds) scores = (
            state.TeamStanding(TeamBlue)?.FlagCaptures ?? 0,
            state.TeamStanding(TeamRed)?.FlagCaptures ?? 0,
            state.ConVars.GetInt("tf_passtime_scores_per_round"));

        // Valve sets these every tick; set here on a change, as each set re-lays the labels.
        if (scores == _set)
        {
            return;
        }

        _set = scores;

        if (FindChildByName("PlayingToCluster") is VguiEditablePanel cluster)
        {
            cluster.SetDialogVariable("rounds", scores.Rounds);
        }

        SetDialogVariable("bluescore", scores.Blue);
        SetDialogVariable("redscore", scores.Red);
    }
}

/// <summary>`CTFHudPasstimeBallStatus` (tf_hud_passtime.cpp:790): the track bar with the ball, the goals, every player and the power meter.</summary>
/// <remarks>
/// **From state each think.** The carrier's name shows while the ball has a carrier, which is what the `pass_get`, `pass_free`,
/// `pass_ball_stolen`, `pass_pass_caught` and `pass_score` handlers (:1476-1539) leave it at. **Not ported:** the event text queue
/// (`CTFHudPasstimeEventText`, :499-783) and its power-up and power-down notices.
/// </remarks>
public sealed class TfHudPasstimeBallStatus(VguiPanel parent) : VguiEditablePanel(parent, "HudPasstimeBallStatus")
{
    private const int TeamRed = 2;
    private const int TeamBlue = 3;
    private const int NumGoalIcons = 3;
    private const int MaxPlayers = 101;
    private const int GoalTypeTower = 2;
    private const int ClassSpy = 8;

    private readonly VguiImagePanel?[] _goalIconsBlue = new VguiImagePanel?[NumGoalIcons];
    private readonly VguiImagePanel?[] _goalIconsRed = new VguiImagePanel?[NumGoalIcons];
    private readonly int[] _goalsBlue = new int[NumGoalIcons];
    private readonly int[] _goalsRed = new int[NumGoalIcons];
    private readonly VguiImagePanel?[] _playerIcons = new VguiImagePanel?[MaxPlayers];
    private VguiImagePanel? _progressBall;
    private VguiLabel? _carrierName;
    private VguiPanel? _levelBar;
    private VguiImagePanel? _selfIcon;
    private VguiPanel? _powerCluster;
    private VguiPanel? _powerFillContainer;
    private VguiImagePanel? _powerFill;
    private bool _initialized;
    private bool _goalsFound;
    private int _fillWidth;
    private (int X, int Y) _blueEnd = (-100, -100);
    private (int X, int Y) _redEnd = (-100, -100);
    private int _carrierShown = -1;

    /// <summary>`GetTeamColor` (:37): "the team colors from g_PR are wrong for probably good reasons".</summary>
    /// <param name="team">The team.</param>
    /// <param name="alpha">The alpha.</param>
    /// <returns>The colour.</returns>
    public static (byte Red, byte Green, byte Blue, byte Alpha) TeamColor(int team, byte alpha) => team switch
    {
        TeamRed => (159, 55, 34, alpha),
        TeamBlue => (76, 109, 128, alpha),
        _ => (245, 231, 222, alpha),
    };

    /// <summary>`GetProgressBallImageForTeam` (:48).</summary>
    /// <param name="team">The carrier's team, else the ball's.</param>
    /// <returns>The image.</returns>
    public static string BallImage(int team) => team switch
    {
        TeamRed => "../passtime/hud/passtime_ballcontrol_red",
        TeamBlue => "../passtime/hud/passtime_ballcontrol_blue",
        _ => "../passtime/hud/passtime_ballcontrol_none",
    };

    /// <summary>`GetProgressGoalImage` (:103): a goal on BLU scores for BLU and sits on RED's side, so it is drawn red.</summary>
    /// <param name="goal">The goal.</param>
    /// <returns>The image.</returns>
    public static string GoalImage(ScenePasstimeGoal goal)
    {
        string colour = goal.Team == TeamBlue ? "red" : "blue";

        return goal.GoalType switch
        {
            GoalTypeTower => $"../passtime/hud/passtime_goal_{colour}_{(goal.TriggerDisabled ? "locked" : "unlocked")}",
            1 => $"../passtime/hud/passtime_endzone_{colour}_icon",
            _ => $"../passtime/hud/passtime_goal_{colour}_icon",
        };
    }

    /// <summary>`GetPlayerProgressPortrait` (:67): the class and team's portrait, a scout for any other class.</summary>
    /// <param name="playerClass">`GetClassIndex()`.</param>
    /// <param name="team">The team.</param>
    /// <returns>The image.</returns>
    public static string Portrait(int playerClass, int team)
    {
        string name = playerClass switch
        {
            3 => "soldier",
            2 => "sniper",
            4 => "demo",
            5 => "medic",
            6 => "heavy",
            7 => "pyro",
            ClassSpy => "spy",
            9 => "eng",
            _ => "scout",
        };

        return $"../passtime/hud/portrait_{name}_{(team == TeamRed ? "red" : "blu")}";
    }

    /// <summary>`Lerp( flFrac, int, int )`: the float result truncated into the int it returns.</summary>
    private static int Lerp(float frac, int from, int to) => (int)(from + ((to - from) * frac));

    /// <inheritdoc/>
    /// <remarks>`ApplySchemeSettings` (:821): the layout, the controls it names, the final fifth of the meter, the track's ends.</remarks>
    public override void ApplySchemeSettings(VguiContext context)
    {
        base.ApplySchemeSettings(context);
        _initialized = false;
        LoadControlSettings("resource/UI/HudPasstimeBallStatus.res", context);

        _powerCluster = FindChildByName("BallPowerCluster");
        _progressBall = FindChildByName("ProgressBallIcon") as VguiImagePanel;
        _carrierName = FindChildByName("ProgressBallCarrierName") as VguiLabel;
        _levelBar = FindChildByName("ProgressLevelBar");
        _selfIcon = FindChildByName("ProgressSelfPlayerIcon") as VguiImagePanel;

        VguiPanel powerRoot = _powerCluster ?? this;
        _powerFillContainer = powerRoot.FindChildByName("BallPowerMeterFillContainer", recurseDown: true);
        _powerFill = powerRoot.FindChildByName("BallPowerMeterFill", recurseDown: true) as VguiImagePanel;
        VguiPanel? frame = powerRoot.FindChildByName("BallPowerMeterFrame", recurseDown: true);
        VguiPanel? finalSection = powerRoot.FindChildByName("BallPowerMeterFinalSectionContainer", recurseDown: true);

        _initialized = _progressBall is not null && _carrierName is not null && _levelBar is not null && _selfIcon is not null
            && _powerFillContainer is not null && _powerFill is not null && finalSection is not null && frame is not null;

        // "just bail if the res file is missing required controls".
        if (!_initialized)
        {
            return;
        }

        _fillWidth = _powerFillContainer!.Wide;
        int finalWidth = (int)(_fillWidth * 0.2f);
        finalSection!.Wide = finalWidth;
        finalSection.X += _fillWidth - finalWidth;

        if (FindChildByName("RedProgressEnd") is { } redEnd && FindChildByName("BlueProgressEnd") is { } blueEnd)
        {
            _redEnd = (redEnd.X, redEnd.Y);
            _blueEnd = (blueEnd.X, blueEnd.Y);
        }
        else
        {
            _redEnd = (-100, -100);
            _blueEnd = (-100, -100);
        }

        for (int i = 0; i < NumGoalIcons; i++)
        {
            _goalIconsRed[i] = FindChildByName($"GoalRed{i}") as VguiImagePanel;
            _goalIconsBlue[i] = FindChildByName($"GoalBlue{i}") as VguiImagePanel;
        }

        for (int i = 0; i < MaxPlayers; i++)
        {
            _playerIcons[i] = FindChildByName($"playericon{i}") as VguiImagePanel;
            _playerIcons[i]?.SetShouldScaleImage(true);
        }

        _goalsFound = false;
        _carrierShown = -1;
    }

    /// <summary>`OnTick` (:1339).</summary>
    /// <param name="state">The game state.</param>
    public void Update(HudState state)
    {
        if (state.Passtime is not { } passtime || !_initialized)
        {
            return;
        }

        // `BShouldDraw` (:931): the local player alive and out of the freeze cam, in a running round or stalemate, past setup.
        bool draw = state.Alive
            && state.ObserverMode != ObserverModes.FreezeCam
            && state.RoundState is RoundStateRunning or HudState.RoundStateStalemate
            && !state.Rules.Setup;

        // `OnTickHidden` hides the name every tick; only a ball event shows it again, so a hidden bar never shows it.
        if (!draw || passtime.Ball == 0)
        {
            OnTickHidden();
        }
        else
        {
            OnTickVisible(state, passtime);
            UpdateCarrierName(state, passtime);
        }
    }

    private const int RoundStateRunning = 4;

    private void UpdateCarrierName(HudState state, ScenePasstime passtime)
    {
        if (passtime.Carrier == _carrierShown)
        {
            return;
        }

        _carrierShown = passtime.Carrier;

        if (passtime.Carrier == 0)
        {
            _carrierName!.Visible = false;
            return;
        }

        // `OnBallGetOther`/`OnBallGetSelf` (:1574-1617): "#TF_Passtime_CarrierName", else "%s1", with the carrier's name.
        string name = state.Names?.GetValueOrDefault(passtime.Carrier) ?? string.Empty;
        Func<string, string?>? localize = HudViewport.Of(this)?.Context?.Localize;
        string format = localize?.Invoke("#TF_Passtime_CarrierName") ?? "%s1";

        _carrierName!.SetText(VguiLocalize.ConstructString(format, 128, name), localize);
        _carrierName.Visible = true;
    }

    /// <summary>`OnTickHidden` (:1032).</summary>
    private void OnTickHidden()
    {
        _goalsFound = false;
        _progressBall!.Visible = false;
        _carrierName!.Visible = false;
        _carrierShown = -1;
        _selfIcon!.Visible = false;
        _levelBar!.Visible = false;

        if (_powerCluster is not null)
        {
            _powerCluster.Visible = false;
        }

        foreach (VguiImagePanel? icon in _playerIcons)
        {
            if (icon is not null)
            {
                icon.Visible = false;
            }
        }

        HideGoalIcons();
    }

    private void HideGoalIcons()
    {
        for (int i = 0; i < NumGoalIcons; i++)
        {
            if (_goalIconsBlue[i] is { } blue)
            {
                blue.Visible = false;
            }

            if (_goalIconsRed[i] is { } red)
            {
                red.Visible = false;
            }
        }
    }

    /// <summary>`OnTickVisible` (:1075).</summary>
    private void OnTickVisible(HudState state, ScenePasstime passtime)
    {
        _levelBar!.Visible = true;

        // "Update ball icon": by the carrier's position where there is one, else the ball's (`CalcBallProgressFrac`, :987).
        ScenePlayer? carrier = passtime.Carrier == 0 ? null : state.Player(passtime.Carrier);
        float ballFrac = passtime.ProgressFrac(carrier is { } c ? (c.X, c.Y, c.Z) : passtime.BallOrigin);
        _progressBall!.Visible = true;
        _progressBall.X = Lerp(ballFrac, _blueEnd.X, _redEnd.X) - (_progressBall.Wide / 2);
        _progressBall.Y = Lerp(ballFrac, _blueEnd.Y, _redEnd.Y) - (_progressBall.Tall / 2);

        int team = carrier?.Team ?? passtime.BallTeam;
        SetImageIfChanged(_progressBall, BallImage(team));

        // "Update ball power" (:1107).
        if (_powerCluster is not null)
        {
            _powerCluster.Visible = true;
        }

        int power = passtime.BallPower;
        int threshold = state.ConVars.GetInt("tf_passtime_powerball_threshold");
        int alpha = power > threshold ? (int)(150f + ((255f - 150f) * ((1f + MathF.Sin(state.CurTime * 5f)) / 2f))) : 222;
        _powerFill!.DrawColor = TeamColor(team, (byte)alpha);
        _powerFillContainer!.Wide = (int)(_fillWidth * (power / 100f));

        UpdatePlayerIcons(state, passtime);
        UpdateGoals(passtime);
    }

    /// <summary>"Update player icons" (:1147): a pip per living RED or BLU player above or below the bar, and the local portrait.</summary>
    private void UpdatePlayerIcons(HudState state, ScenePasstime passtime)
    {
        int specTarget = state.ObserverTarget;
        bool observer = state.ObserverMode != ObserverModes.None;
        _selfIcon!.Visible = false;
        int halfHeight = _levelBar!.Tall / 7;

        for (int index = 1; index <= MaxPlayers; index++)
        {
            if (_playerIcons[index - 1] is not { } icon)
            {
                break;
            }

            SceneScoreboardPlayer? resource = Resource(state, index);
            int entTeam = resource?.Team ?? 0;

            // Not connected, dead, a spectator, or dormant ("not available on this client right now").
            if (resource is not { Connected: true, Alive: true } || entTeam is not (TeamRed or TeamBlue) || state.Player(index) is not { } player)
            {
                icon.Visible = false;
                continue;
            }

            if (resource.Value.PlayerClass == ClassSpy)
            {
                // `IsFullyInvisible`: `GetPercentInvisible() == 1`. **Interpolated:** the motion cloak's dry state is not read.
                if (PlayerInvisibility.Percent(player, state.CurTime, motionCloak: false) >= 1f)
                {
                    icon.Visible = false;
                    continue;
                }

                if (player.DisguiseTeam is TeamRed or TeamBlue)
                {
                    entTeam = player.DisguiseTeam.Value;
                }
            }

            float frac = passtime.ProgressFrac((player.X, player.Y, player.Z));
            int x = Lerp(frac, _blueEnd.X, _redEnd.X) - (icon.Wide / 2);
            int y = Lerp(frac, _blueEnd.Y, _redEnd.Y);

            icon.Visible = true;

            if (entTeam == TeamRed)
            {
                (icon.X, icon.Y) = (x, y - icon.Tall - halfHeight);
                SetImageIfChanged(icon, "../passtime/hud/passtime_ballcontrol_team_red");
            }
            else
            {
                (icon.X, icon.Y) = (x, y + halfHeight);
                SetImageIfChanged(icon, "../passtime/hud/passtime_ballcontrol_team_blue");
            }

            // "Local player image" (:1223).
            if (index == state.LocalIndex || (observer && index == specTarget))
            {
                ScenePlayer portrait = observer && specTarget != 0 && state.Player(specTarget) is { } target ? target : player;
                SetImageIfChanged(_selfIcon, Portrait(portrait.PlayerClass ?? 0, portrait.Team ?? 0));
                _selfIcon.X = Lerp(frac, _blueEnd.X, _redEnd.X) - (_selfIcon.Wide / 2);
                _selfIcon.Y = y - _selfIcon.Tall - halfHeight;
                _selfIcon.Visible = true;
            }
        }
    }

    private static SceneScoreboardPlayer? Resource(HudState state, int index)
    {
        IReadOnlyList<SceneScoreboardPlayer> players = state.ScoreboardPlayers ?? [];

        for (int at = 0; at < players.Count; at++)
        {
            if (players[at].EntityIndex == index)
            {
                return players[at];
            }
        }

        return null;
    }

    /// <summary>"Refresh goal icons if necessary" (:1246) and "Update goal icons" (:1307).</summary>
    private void UpdateGoals(ScenePasstime passtime)
    {
        if (!_goalsFound && passtime.NumSections > 0)
        {
            Array.Clear(_goalsBlue);
            Array.Clear(_goalsRed);
            int count = passtime.Goals.Count;

            // `iMaxSortedGoals` 8, "arbitrary"; sorted by track position, blue end first. `std::sort` is not stable; this is.
            if (count is > 1 and < 8)
            {
                List<ScenePasstimeGoal> sorted = [.. passtime.Goals];
                sorted.Sort((a, b) => passtime.ProgressFrac(a.Origin).CompareTo(passtime.ProgressFrac(b.Origin)));
                int redIcon = 0;
                int blueIcon = 0;

                for (int goal = 0; goal < count; goal++)
                {
                    // "goal's teams are backwards": the goals on BLU's side are RED's.
                    if (blueIcon < NumGoalIcons && sorted[goal].Team == TeamRed)
                    {
                        _goalsBlue[blueIcon++] = sorted[goal].EntityIndex;
                    }

                    if (redIcon < NumGoalIcons && sorted[count - goal - 1].Team == TeamBlue)
                    {
                        _goalsRed[redIcon++] = sorted[count - goal - 1].EntityIndex;
                    }
                }

                _goalsFound = true;
            }
        }

        if (!_goalsFound)
        {
            HideGoalIcons();
            return;
        }

        for (int i = 0; i < NumGoalIcons; i++)
        {
            UpdateGoalIcon(_goalIconsBlue[i], passtime, _goalsBlue[i]);
            UpdateGoalIcon(_goalIconsRed[i], passtime, _goalsRed[i]);
        }
    }

    /// <summary>`UpdateGoalIcon` (:998): hidden without a goal; dimmed when a hoop or endzone is disabled; placed on the bar.</summary>
    private void UpdateGoalIcon(VguiImagePanel? icon, ScenePasstime passtime, int entity)
    {
        if (icon is null)
        {
            return;
        }

        ScenePasstimeGoal? found = null;

        for (int at = 0; at < passtime.Goals.Count; at++)
        {
            if (passtime.Goals[at].EntityIndex == entity)
            {
                found = passtime.Goals[at];
            }
        }

        if (entity == 0 || found is not { } goal)
        {
            icon.Visible = false;
            return;
        }

        icon.Visible = true;
        icon.SetAlpha(goal.TriggerDisabled && goal.GoalType != GoalTypeTower ? 50f : 255f);
        SetImageIfChanged(icon, GoalImage(goal));

        float frac = passtime.ProgressFrac(goal.Origin);
        int halfHeight = _levelBar!.Tall / 6;
        icon.X = Lerp(frac, _blueEnd.X, _redEnd.X) - (icon.Wide / 2);
        icon.Y = Lerp(frac, _blueEnd.Y, _redEnd.Y) - halfHeight;
    }

    /// <summary>`SetImage` returns early on the same name; checked here so no name is built for it.</summary>
    private static void SetImageIfChanged(VguiImagePanel icon, string image)
    {
        if (!string.Equals(icon.ImageName, image, StringComparison.Ordinal))
        {
            icon.SetImage(image);
        }
    }
}

/// <summary>`CTFHudPasstime` (tf_hud_passtime.cpp:1634): the Pass Time HUD's host, on the objective status.</summary>
/// <remarks>**Not ported:** the pass notice (`CTFHudPasstimePassNotify`), the ball and player off-screen arrows (B709).</remarks>
public sealed class TfHudPasstime : VguiEditablePanel
{
    /// <summary>`CTFHudPasstime( pParent )`.</summary>
    /// <param name="parent">The objective status.</param>
    public TfHudPasstime(VguiPanel parent)
        : base(parent, "HudPasstime")
    {
        BallStatus = new TfHudPasstimeBallStatus(this);
        TeamScore = new TfHudPasstimeTeamScore(this);
    }

    /// <summary>`m_pBallStatus`.</summary>
    public TfHudPasstimeBallStatus BallStatus { get; }

    /// <summary>`m_pTeamScore`.</summary>
    public TfHudPasstimeTeamScore TeamScore { get; }

    /// <inheritdoc/>
    public override void ApplySchemeSettings(VguiContext context)
    {
        base.ApplySchemeSettings(context);
        LoadControlSettings("resource/UI/HudPasstime.res", context);
    }

    /// <summary>`OnTick` (:1672) and its children's ticks.</summary>
    /// <param name="state">The game state.</param>
    public void Update(HudState state)
    {
        TeamScore.Visible = Visible;
        TeamScore.Update(state);
        BallStatus.Update(state);
    }
}
