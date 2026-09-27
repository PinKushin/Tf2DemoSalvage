using System;
using System.Collections.Generic;
using System.Globalization;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>`CTFHudMatchStatus` (game/client/tf/tf_hud_match_status.cpp): the match HUD, which carries the round timer.</summary>
/// <remarks>
/// Hidden by `HIDEHUD_MISCSTATUS | HIDEHUD_MATCH_STATUS`, drawn regardless during the match summary. `OnThink` (:434) keeps
/// the time panel on the timer the objective resource names — or the one it already shows while that is still enabled
/// and in the HUD — and shows it outside freeze cam, outside KOTH (whose timers are their own element) unless waiting for
/// players, and not during the match summary. `tf_use_match_hud` (1) and not Mann vs. Machine loads its `.res` with
/// `if_match`.
/// **Not modelled:** team status, player lists and avatars, match doors, round sign and rank-up labels; `if_large`, which
/// needs the match group's size; the freeze-cam screenshot test; and an open viewport panel.
/// </remarks>
public sealed class TfHudMatchStatus : VguiEditablePanel, IHudElement
{
    private bool _usedMatchHud;

    /// <summary>`CTFHudMatchStatus( "HudMatchStatus" )`: parented to the viewport, its time panel made up front.</summary>
    /// <param name="viewport">The viewport.</param>
    public TfHudMatchStatus(VguiPanel viewport)
        : base(viewport, "HudMatchStatus") =>
        TimePanel = new TfHudTimeStatus(this, "ObjectiveStatusTimePanel");

    /// <summary>`m_pTimePanel`.</summary>
    public TfHudTimeStatus TimePanel { get; }

    /// <inheritdoc/>
    public override string ClassName => "CTFHudMatchStatus";

    /// <summary>`tf_use_match_hud`: 1.</summary>
    public bool UseMatchHud { get; set; } = true;

    /// <inheritdoc/>
    public int HiddenBits => HudVisibility.HideMiscStatus | HudVisibility.HideMatchStatus;

    /// <summary>`ShouldUseMatchHUD()` (:43): `tf_use_match_hud`, never in Mann vs. Machine.</summary>
    /// <param name="state">The game state.</param>
    public bool ShouldUseMatchHud(HudState state) => !state.Rules.MannVsMachine && UseMatchHud;

    /// <summary>`CTFHudMatchStatus::ShouldDraw` (:417): always during the match summary, else `CHudElement`'s.</summary>
    /// <param name="state">The game state.</param>
    /// <returns>Whether it draws.</returns>
    public bool ShouldDraw(HudState state) => state.Rules.ShowMatchSummary || !HudVisibility.IsHidden(state, HiddenBits);

    /// <inheritdoc/>
    public override void ApplySchemeSettings(VguiContext context)
    {
        base.ApplySchemeSettings(context);

        HudState state = HudViewport.Of(this)?.State ?? default;

        _usedMatchHud = ShouldUseMatchHud(state);
        TimePanel.UseMatchHud = _usedMatchHud;
        LoadControlSettings("resource/UI/HudMatchStatus.res", context, _usedMatchHud ? ["if_match"] : null);
    }

    /// <inheritdoc/>
    protected override void OnThink()
    {
        if (HudViewport.Of(this) is not { } viewport)
        {
            return;
        }

        HudState state = viewport.State;

        // A different choice reloads the scheme, which reapplies the conditions.
        if (ShouldUseMatchHud(state) != _usedMatchHud)
        {
            InvalidateLayout(reloadScheme: true);
        }

        bool display = state.ObserverMode != ObserverModes.FreezeCam;

        // `IsInTournamentMode() && IsInWaitingForPlayers()` (:474).
        if (state.TournamentMode && state.Rules.WaitingForPlayers)
        {
            display = false;
        }

        if (display)
        {
            // "is the time panel still pointing at an active timer?"
            if (state.RoundTimer(TimePanel.TimerIndex) is { Disabled: false, ShowInHud: true })
            {
                display = true;
            }
            else
            {
                int active = state.Rules.TimerToShowInHud;

                display = active != 0 && state.RoundTimer(active) is not null;
                TimePanel.TimerIndex = active;
            }
        }

        if (display && !state.Rules.ShowMatchSummary)
        {
            TimePanel.Visible = !state.Rules.Koth || state.Rules.WaitingForPlayers;
        }
        else
        {
            TimePanel.Visible = false;
        }
    }
}

/// <summary>`CTFHudTimeStatus` (game/client/tf/tf_time_panel.cpp:290): the round timer — its clock, dial and state labels.</summary>
/// <remarks>
/// `ApplySchemeSettings` (:622) loads `resource/UI/HudObjectiveTimePanel.res` (`if_match` under the match HUD) and the team
/// background. `OnThink` (:674), every tenth of a second: the time left — or passed, for a timer that counts up — as
/// `m:ss`, and the dial's share of the time gone. `SetExtraTimePanels` (:440) shows setup, waiting for players, overtime
/// and sudden death, and hides everything for a stopwatch. `teamplay_timer_time_added` for this timer rises and fades as
/// `+m:ss` in `Paint` (:839).
/// **Not modelled:** the server time limit label, off with `tf_hud_show_servertimelimit`'s 0; the arena player count's
/// offset; and the `TimerFlash` a timer's unpausing starts from its receive proxy.
/// </remarks>
public sealed class TfHudTimeStatus : VguiEditablePanel
{
    private const int NumTimerDeltaItems = 2;
    private const int TeamRed = 2;

    private readonly (float DieTime, int Amount)[] _deltas = new (float, int)[NumTimerDeltaItems];
    private int _deltaHead;
    private float _nextThink;
    private float _lastCurTime;
    private bool _kothMode;
    private bool _cachedOvertime;
    private int? _team;
    private TfProgressBar? _progressBar;
    private VguiLabel? _overtimeLabel;
    private VguiPanel? _overtimeBg;
    private VguiLabel? _suddenDeathLabel;
    private VguiPanel? _suddenDeathBg;
    private VguiLabel? _waitingLabel;
    private VguiPanel? _waitingBg;
    private VguiLabel? _setupLabel;
    private VguiPanel? _setupBg;
    private VguiScalableImagePanel? _timerBg;
    private VguiPanel? _serverTimeLabel;
    private VguiPanel? _serverTimeBg;

    /// <summary>`CTFHudTimeStatus( parent, name )`: its value label, and its panel variables (tf_time_panel.h:125).</summary>
    /// <param name="parent">The parent.</param>
    /// <param name="name">The panel's name.</param>
    public TfHudTimeStatus(VguiPanel? parent, string? name)
        : base(parent, name)
    {
        TimeValue = new TfExLabel(this, "TimePanelValue");
        DeclareAnimationVar("delta_item_start_y", VguiPanelVarType.ProportionalFloat, "100");
        DeclareAnimationVar("delta_item_end_y", VguiPanelVarType.ProportionalFloat, "0");
        DeclareAnimationVar("delta_item_x", VguiPanelVarType.ProportionalFloat, "0");
        DeclareAnimationVar("PositiveColor", VguiPanelVarType.Color, "0 255 0 255");
        DeclareAnimationVar("NegativeColor", VguiPanelVarType.Color, "255 0 0 255");
        DeclareAnimationVar("delta_lifetime", VguiPanelVarType.Real, "2.0");
        DeclareAnimationVar("delta_item_font", VguiPanelVarType.Font, "Default");
    }

    /// <summary>`m_pTimeValue`.</summary>
    public TfExLabel TimeValue { get; }

    /// <summary>The events the constructor listens for (:320); `localplayer_changeteam` is the team change `OnThink` sees.</summary>
    public static IReadOnlySet<string> ListensFor { get; } =
        new HashSet<string>(["teamplay_update_timer", "teamplay_timer_time_added", "localplayer_changeteam"], StringComparer.Ordinal);

    /// <inheritdoc/>
    public override string ClassName => "CTFHudTimeStatus";

    /// <summary>`m_iTimerIndex`: the timer shown, 0 for none.</summary>
    public int TimerIndex { get; set; }

    /// <summary>`ShouldUseMatchHUD()`, as the parent decided it.</summary>
    public bool UseMatchHud { get; set; } = true;

    /// <summary>`m_nTeam` (`SetTeam`): whose timer this is in KOTH — RED's panel is RED, every other `TEAM_UNASSIGNED`.</summary>
    public int Team { get; set; }

    /// <inheritdoc/>
    public override void ApplySchemeSettings(VguiContext context)
    {
        LoadControlSettings("resource/UI/HudObjectiveTimePanel.res", context, UseMatchHud ? ["if_match"] : null);

        _progressBar = FindChildByName("TimePanelProgressBar") as TfProgressBar;
        _overtimeLabel = FindChildByName("OvertimeLabel") as VguiLabel;
        _overtimeBg = FindChildByName("OvertimeBG");
        _suddenDeathLabel = FindChildByName("SuddenDeathLabel") as VguiLabel;
        _suddenDeathBg = FindChildByName("SuddenDeathBG");
        _waitingLabel = FindChildByName("WaitingForPlayersLabel") as VguiLabel;
        _waitingBg = FindChildByName("WaitingForPlayersBG");
        _setupLabel = FindChildByName("SetupLabel") as VguiLabel;
        _setupBg = FindChildByName("SetupBG");
        _timerBg = FindChildByName("TimePanelBG") as VguiScalableImagePanel;

        HudState state = HudViewport.Of(this)?.State ?? default;

        SetTeamBackground(state);
        _serverTimeLabel = FindChildByName("ServerTimeLimitLabel");
        _serverTimeBg = FindChildByName("ServerTimeLimitLabelBG");
        _nextThink = 0f;
        TimerIndex = 0;
        SetExtraTimePanels(state);
        base.ApplySchemeSettings(context);
    }

    /// <summary>`FireGameEvent` (:361).</summary>
    /// <param name="fired">The event.</param>
    public void HandleGameEvent(HudGameEvent fired)
    {
        ArgumentNullException.ThrowIfNull(fired);

        HudState state = HudViewport.Of(this)?.State ?? default;

        switch (fired.Event.Name)
        {
            case "teamplay_update_timer":
                if (state.Rules.Koth)
                {
                    InvalidateLayout(reloadScheme: true);
                }

                SetExtraTimePanels(state);
                break;
            case "teamplay_timer_time_added":
                SetTimeAdded(fired.Event.GetInt("timer", -1), fired.Event.GetInt("seconds_added"), fired.CurTime, state.HasLocalPlayer);
                break;
            default:
                break;
        }
    }

    /// <inheritdoc/>
    protected override void OnThink()
    {
        HudState state = HudViewport.Of(this)?.State ?? default;

        // `localplayer_changeteam`.
        if (state.Team != _team)
        {
            _team = state.Team;
            SetTeamBackground(state);
        }

        // A seek moves the clock back; the throttle starts again from there.
        if (state.CurTime < _lastCurTime)
        {
            _nextThink = 0f;
        }

        _lastCurTime = state.CurTime;

        if (_nextThink >= state.CurTime)
        {
            return;
        }

        if (_kothMode != state.Rules.Koth)
        {
            _kothMode = state.Rules.Koth;
            InvalidateLayout(reloadScheme: true);
        }

        if (state.RoundTimer(TimerIndex) is { } timer)
        {
            int totalTime = timer.TimerMaxLength;
            int timeRemaining = (int)timer.TimeRemainingAt(state.ServerTime);
            int timeToDisplay = timer.ShowTimeRemaining ? timeRemaining : totalTime - timeRemaining;

            if (TimeValue.Visible)
            {
                if (timeToDisplay <= 0 && state.Rules.Koth)
                {
                    SetExtraTimePanels(state);
                }

                int shown = Math.Max(timeToDisplay, 0);

                TimeValue.SetText(string.Create(CultureInfo.InvariantCulture, $"{shown / 60}:{shown % 60:00}"), null);
            }

            if (_progressBar is { Visible: true } bar)
            {
                bar.Percentage = totalTime == 0 ? 0f : ((float)totalTime - timeRemaining) / totalTime;
            }

            // `tf_hud_show_servertimelimit` is 0, so the server time limit stays hidden.
            if (_serverTimeLabel is not null && _serverTimeBg is not null)
            {
                _serverTimeLabel.Visible = false;
                _serverTimeBg.Visible = false;
            }
        }

        _nextThink = state.CurTime + 0.1f;
    }

    /// <inheritdoc/>
    /// <remarks>Each delta rises from `delta_item_start_y` to `delta_item_end_y` over its life, fading in the second half.</remarks>
    public override void Paint(IVguiSurface surface, VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(surface);

        base.Paint(surface, context);

        float curTime = HudViewport.Of(this)?.State.CurTime ?? 0f;
        float lifetime = GetFloat("delta_lifetime");

        foreach ((float dieTime, int amount) in _deltas)
        {
            if (dieTime <= curTime)
            {
                continue;
            }

            (byte red, byte green, byte blue, byte alpha) = amount > 0 ? GetColor("PositiveColor") : GetColor("NegativeColor");
            float lifetimePercent = (dieTime - curTime) / lifetime;

            if (lifetimePercent < 0.5)
            {
                alpha = (byte)(int)(255.0f * (lifetimePercent / 0.5));
            }

            float height = GetFloat("delta_item_start_y") - GetFloat("delta_item_end_y");
            float y = GetFloat("delta_item_end_y") + (lifetimePercent * height);

            if (GetFont("delta_item_font") is { } font)
            {
                surface.DrawSetTextFont(font);
            }

            surface.DrawSetTextColor((red, green, blue, alpha));
            surface.DrawSetTextPos((int)GetFloat("delta_item_x"), (int)y);

            int clock = Math.Abs(amount);

            surface.DrawPrintText(string.Create(CultureInfo.InvariantCulture, $"{(amount > 0 ? '+' : '-')}{clock / 60}:{clock % 60:00}"));
        }
    }

    /// <summary>`SetExtraTimePanels` against this frame's state — what a `teamplay_update_timer` would run.</summary>
    public void RefreshExtraTimePanels() => SetExtraTimePanels(HudViewport.Of(this)?.State ?? default);

    /// <summary>`SetTimeAdded` (:390): a delta for the timer shown, while there is a local player.</summary>
    private void SetTimeAdded(int timer, int seconds, float curTime, bool hasLocalPlayer)
    {
        if (TimerIndex != timer || seconds == 0 || !hasLocalPlayer)
        {
            return;
        }

        _deltas[_deltaHead] = (curTime + GetFloat("delta_lifetime"), seconds);
        _deltaHead = (_deltaHead + 1) % NumTimerDeltaItems;
    }

    /// <summary>`SetTeamBackground` (:327): the local team's background — in KOTH outside waiting for players, `m_nTeam`'s.</summary>
    private void SetTeamBackground(HudState state)
    {
        if (_timerBg is null)
        {
            return;
        }

        int team = !state.Rules.Koth || state.Rules.WaitingForPlayers ? state.Team : Team;

        _timerBg.SetImage(team == TeamRed ? "../hud/objectives_timepanel_red_bg" : "../hud/objectives_timepanel_blue_bg");
    }

    /// <summary>`SetExtraTimePanels` (:440).</summary>
    private void SetExtraTimePanels(HudState state)
    {
        SceneRoundTimer? timer = state.RoundTimer(TimerIndex);

        if (timer is { StopWatch: true })
        {
            foreach (VguiPanel? panel in (VguiPanel?[])[_timerBg, _progressBar, _waitingLabel, _waitingBg, _overtimeLabel, _overtimeBg, _setupLabel, _setupBg, _suddenDeathLabel, _suddenDeathBg, _serverTimeLabel, _serverTimeBg])
            {
                panel?.Visible = false;
            }

            return;
        }

        bool inSetup = state.Rules.Setup;
        bool waiting = state.Rules.WaitingForPlayers;
        VguiAnimationController? animations = HudViewport.Of(this)?.Animations;

        if (_setupBg is not null && _setupLabel is not null && timer is not null)
        {
            _setupBg.Visible = inSetup;
            _setupLabel.Visible = inSetup;
        }

        if (_suddenDeathBg is not null && _suddenDeathLabel is not null)
        {
            bool suddenDeath = state.RoundState == HudState.RoundStateStalemate && state.Rules.GameType != SceneGameRules.GameTypeArena;

            if (suddenDeath != _suddenDeathLabel.Visible)
            {
                if (suddenDeath)
                {
                    animations?.StartAnimationSequence("SuddenDeathLabelPulseRed");
                }
                else
                {
                    animations?.StopAnimationSequence(this, "SuddenDeathLabelPulseRed");
                }
            }

            _suddenDeathBg.Visible = suddenDeath;
            _suddenDeathLabel.Visible = suddenDeath;
        }

        if (_overtimeBg is not null && _overtimeLabel is not null)
        {
            bool overtime = state.Rules.Overtime;

            if (state.Rules.Koth && timer is { } kothTimer)
            {
                bool expired = kothTimer.TimeRemainingAt(state.ServerTime) <= 0;

                if (overtime)
                {
                    overtime = expired;
                    _cachedOvertime |= expired;
                }
                else if (_cachedOvertime)
                {
                    overtime = expired;
                    _cachedOvertime = expired;
                }
            }

            if (overtime)
            {
                if (_suddenDeathBg is not null && _suddenDeathLabel is not null)
                {
                    _suddenDeathBg.Visible = false;
                    _suddenDeathLabel.Visible = false;
                }

                if (!_overtimeLabel.Visible)
                {
                    _overtimeBg.Visible = true;
                    _overtimeLabel.Visible = true;
                    animations?.StartAnimationSequence(this, "OvertimeLabelPulseRed");
                    CheckClockLabelLength(_overtimeLabel, _overtimeBg);
                }
            }
            else
            {
                _overtimeBg.Visible = false;
                _overtimeLabel.Visible = false;
                animations?.StopAnimationSequence(this, "OvertimeLabelPulseRed");
            }
        }

        if (_waitingBg is not null && _waitingLabel is not null)
        {
            _waitingBg.Visible = waiting;
            _waitingLabel.Visible = waiting;

            if (waiting)
            {
                // "can't be waiting for players *AND* in setup at the same time"
                if (_setupBg is not null && _setupLabel is not null)
                {
                    _setupBg.Visible = false;
                    _setupLabel.Visible = false;
                }

                CheckClockLabelLength(_waitingLabel, _waitingBg);
            }
        }

        if (_serverTimeLabel is not null && _serverTimeBg is not null)
        {
            // "This appears in the same space after SetUp and WaitingForPlayers is gone" — off with the cvar's 0.
            _serverTimeLabel.Visible = false;
            _serverTimeBg.Visible = false;
        }
    }

    /// <summary>`CheckClockLabelLength` (:412): a label wider than its text box grows about its centre; wider than its background, the background hides.</summary>
    private static void CheckClockLabelLength(VguiLabel label, VguiPanel background)
    {
        (int textWide, _) = label.GetContentSize();

        if (textWide > label.Wide)
        {
            int x = label.X + (int)((label.Wide / 2.0f) - (textWide / 2.0f));

            (label.X, label.Wide) = (x, textWide);
        }

        if (label.Wide > background.Wide)
        {
            background.Visible = false;
        }
    }
}
