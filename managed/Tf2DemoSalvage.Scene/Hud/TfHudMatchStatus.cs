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
/// needs the match group's size; the freeze-cam screenshot test; and an open viewport panel. The round counter is its own
/// panel, <see cref="TfRoundCounterPanel"/> — see its remarks for what it leaves out.
/// </remarks>
public sealed class TfHudMatchStatus : VguiEditablePanel, IHudElement
{
    private bool _usedMatchHud;

    /// <summary>`CTFHudMatchStatus( "HudMatchStatus" )`: parented to the viewport, its time panel made up front.</summary>
    /// <param name="viewport">The viewport.</param>
    public TfHudMatchStatus(VguiPanel viewport)
        : base(viewport, "HudMatchStatus")
    {
        RoundCounter = new TfRoundCounterPanel(this);
        TimePanel = new TfHudTimeStatus(this, "ObjectiveStatusTimePanel");
    }

    /// <summary>`m_pRoundCounter`.</summary>
    public TfRoundCounterPanel RoundCounter { get; }

    /// <summary>`m_pTimePanel`.</summary>
    public TfHudTimeStatus TimePanel { get; }

    /// <inheritdoc/>
    public override string ClassName => "CTFHudMatchStatus";

    /// <inheritdoc/>
    public int HiddenBits => HudVisibility.HideMiscStatus | HudVisibility.HideMatchStatus;

    /// <summary>`ShouldUseMatchHUD()` (:43): `tf_use_match_hud`, never in Mann vs. Machine.</summary>
    /// <param name="state">The game state.</param>
    public static bool ShouldUseMatchHud(HudState state) =>
        !state.Rules.MannVsMachine && state.ConVars.GetBool("tf_use_match_hud");

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
        RoundCounter.UseMatchHud = _usedMatchHud;

        // `SetPanelsVisible` (:339): `m_pRoundCounter->SetVisible( ShouldUseMatchHUD() )`.
        RoundCounter.Visible = _usedMatchHud;
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

        // `IsInTournamentMode() && IsInWaitingForPlayers()` (:474); the former is `mp_tournament.GetBool()`
        // (teamplayroundbased_gamerules.cpp:3488).
        if (state.ConVars.GetBool("mp_tournament") && state.Rules.WaitingForPlayers)
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

    /// <summary>The events `CTFHudMatchStatus`'s constructor listens for that this port models (:302-303).</summary>
    /// <remarks>
    /// `teamplay_round_start` and `show_match_summary` are also listened for in the engine, but their only handling here
    /// is `ShowRoundSign`/the match-doors animation — 3D model panels, out of this port's scope (see the class remarks).
    /// `teamplay_round_start` is included anyway so a later pass has somewhere to dispatch it; it is presently a no-op.
    /// </remarks>
    public static IReadOnlySet<string> ListensFor { get; } =
        new HashSet<string>(["restart_timer_time", "teamplay_round_start"], StringComparer.Ordinal);

    /// <summary>`FireGameEvent` (:547), restricted to the 2D countdown label.</summary>
    /// <param name="fired">The event.</param>
    public void HandleGameEvent(HudGameEvent fired)
    {
        ArgumentNullException.ThrowIfNull(fired);

        if (!ShouldUseMatchHud(HudViewport.Of(this)?.State ?? default))
        {
            return;
        }

        if (fired.Event.Name == "restart_timer_time")
        {
            HandleCountdown(fired.Event.GetInt("time"), fired.Rules.RoundsPlayed);
        }

        // `teamplay_round_start`: `ShowRoundSign` when rounds have already been played — out of scope, so nothing 2D
        // happens here (see `ListensFor`'s remarks).
    }

    /// <summary>`HandleCountdown` (:614), minus `ShowRoundSign` and `ShowMatchStartDoors` — both 3D model panels.</summary>
    /// <param name="time">`event->GetInt( "time" )`: seconds left on the restart countdown.</param>
    /// <param name="roundsPlayed">`TFGameRules()->GetRoundsPlayed()`.</param>
    private void HandleCountdown(int time, int roundsPlayed)
    {
        SetDialogVariable("countdown", time);

        // `case 10:` — on the first round `ShowMatchStartDoors`, a 3D model panel not modelled here; after it the 2D countdown.
        if (time == 10 && roundsPlayed != 0)
        {
            HudViewport.Of(this)?.Animations?.StartAnimationSequence(this, "HudMatchStatus_ShowCountdown");
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

            // "Optional display of mp_timelimit on HUD" (tf_time_panel.cpp:741).
            if (_serverTimeLabel is not null && _serverTimeBg is not null)
            {
                int serverTimeLimit = state.ConVars.GetInt("mp_timelimit") * 60;
                bool display = state.ConVars.GetInt("tf_hud_show_servertimelimit") != 0
                    && !state.Rules.Setup
                    && !state.Rules.WaitingForPlayers
                    && timer.MaxLength > 0 // `IsRoundMaxTimerSet()` (teamplay_round_timer.h:82)
                    && serverTimeLimit != 0;

                _serverTimeLabel.Visible = display;
                _serverTimeBg.Visible = display;

                if (display)
                {
                    SetDialogVariable("servertimeleft", ServerTimeLeft(state));
                }
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
            // "This appears in the same space after SetUp and WaitingForPlayers is gone" (tf_time_panel.cpp:593).
            bool display = state.ConVars.GetInt("tf_hud_show_servertimelimit") != 0 && !inSetup && !waiting;

            _serverTimeLabel.Visible = display;
            _serverTimeBg.Visible = display;
        }
    }

    /// <summary>The `servertimeleft` text (tf_time_panel.cpp:770-806) from `GetTimeLeft()` (teamplayroundbased_gamerules.cpp:1226).</summary>
    private string ServerTimeLeft(HudState state)
    {
        const int Chars = 128;

        if (state.ConVars.GetInt("mp_timelimit") * 60 == 0)
        {
            return VguiLocalize.ConstructString(Find("TF_HUD_ServerNoTimeLimit"), Chars);
        }

        float timeLimit = state.ConVars.GetInt("mp_timelimit") * 60;
        int timeLeft = Math.Max((int)(state.Rules.MapResetTime + timeLimit - state.CurTime), 0);

        if (timeLeft == 0)
        {
            return VguiLocalize.ConstructString(Find("TF_HUD_ServerChangeOnRoundEnd"), Chars);
        }

        int hours = timeLeft / 3600;
        string minutes = ((timeLeft % 3600) / 60).ToString("00", CultureInfo.InvariantCulture);
        string seconds = (timeLeft % 60).ToString("00", CultureInfo.InvariantCulture);

        return hours == 0
            ? VguiLocalize.ConstructString(Find("TF_HUD_ServerTimeLeftNoHours"), Chars, minutes, seconds)
            : VguiLocalize.ConstructString(
                Find("TF_HUD_ServerTimeLeft"), Chars, hours.ToString(CultureInfo.InvariantCulture), minutes, seconds);
    }

    private string? Find(string token) => HudViewport.Of(this)?.Context?.Localize?.Invoke(token);

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
