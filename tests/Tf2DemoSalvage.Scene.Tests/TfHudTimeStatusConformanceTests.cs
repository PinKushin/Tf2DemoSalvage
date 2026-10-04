using System.Collections.Generic;
using System.Linq;
using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// `CTFHudMatchStatus::OnThink` (tf_hud_match_status.cpp:434) choosing the round timer, and `CTFHudTimeStatus`
/// (tf_time_panel.cpp) showing it.
/// </summary>
/// <remarks>
/// Timer 42 runs to 300 on the server's clock, whose time is 100: 200 seconds left of a 600-second maximum. The local
/// player is BLU and alive.
/// </remarks>
public sealed class TfHudTimeStatusConformanceTests
{
    [Test]
    public void OnThink_TheNamedTimerRunning_ShowsItsTimeLeft()
    {
        TfHudMatchStatus status = Thought(Playing());

        (status.TimePanel.Visible, status.TimePanel.TimeValue.Text).ShouldBe((true, "3:20"));
    }

    [Test]
    public void OnThink_APausedTimer_ShowsTheFrozenRemainder() =>
        Thought(Playing() with { RoundTimers = [Timer() with { Paused = true, TimeRemaining = 95.5f }] }).TimePanel.TimeValue.Text.ShouldBe("1:35");

    [Test]
    public void OnThink_ATimerCountingUp_ShowsTheTimePassed() =>
        Thought(Playing() with { RoundTimers = [Timer() with { ShowTimeRemaining = false }] }).TimePanel.TimeValue.Text.ShouldBe("6:40");

    [Test]
    public void OnThink_InFreezeCam_HidesTheTimer() =>
        Thought(Playing() with { ObserverMode = ObserverModes.FreezeCam }).TimePanel.Visible.ShouldBeFalse();

    [Test]
    public void OnThink_NoTimerNamed_HidesTheTimer() =>
        Thought(Playing() with { Rules = Rules() with { TimerToShowInHud = 0 } }).TimePanel.Visible.ShouldBeFalse();

    [Test]
    public void OnThink_OnKoth_HidesTheTimerUnlessWaitingForPlayers()
    {
        Thought(Playing() with { Rules = Rules() with { Koth = true } }).TimePanel.Visible.ShouldBeFalse();
        Thought(Playing() with { Rules = Rules() with { Koth = true, WaitingForPlayers = true } }).TimePanel.Visible.ShouldBeTrue("the control");
    }

    [Test]
    public void OnThink_TournamentModeWaitingForPlayers_HidesTheTimer()
    {
        // `IsInTournamentMode() && IsInWaitingForPlayers()` (tf_hud_match_status.cpp:474).
        Thought(Playing() with { ConVars = TestConVars.Of(("mp_tournament", "1")), Rules = Rules() with { WaitingForPlayers = true } })
            .TimePanel.Visible.ShouldBeFalse();

        Thought(Playing() with { ConVars = TestConVars.Of(("mp_tournament", "1")) }).TimePanel.Visible.ShouldBeTrue("the control — tournament mode alone does not hide it");
        Thought(Playing() with { Rules = Rules() with { WaitingForPlayers = true } }).TimePanel.Visible.ShouldBeTrue("the control — waiting alone does not hide it outside tournament mode");
    }

    [Test]
    public void SetExtraTimePanels_InSetupOnTheTimersUpdate_ShowsTheSetupLabel()
    {
        // The setup label needs the timer, which the panel has only once the match status has pointed it at one; the
        // `teamplay_update_timer` that follows is what shows it.
        TfHudTimeStatus panel = Updated(Playing() with { Rules = Rules() with { Setup = true } });

        (panel.FindChildByName("SetupLabel")!.Visible, panel.FindChildByName("WaitingForPlayersLabel")!.Visible).ShouldBe((true, false));
    }

    /// <remarks>
    /// `SetTimerIndex( int index ){ m_iTimerIndex = ( index &gt;= 0 ) ? index : 0; SetExtraTimePanels(); }`
    /// (tf_time_panel.h:77), and the match status calls it the think it points the panel at the timer
    /// (tf_hud_match_status.cpp:497) — so the setup label shows then, with no `teamplay_update_timer` (B467). This test
    /// asserted the opposite before B467, written from the port rather than the header.
    /// </remarks>
    [Test]
    public void SetTimerIndex_InSetup_ShowsTheSetupLabelWithoutAnUpdate() =>
        Thought(Playing() with { Rules = Rules() with { Setup = true } }).TimePanel.FindChildByName("SetupLabel")!.Visible.ShouldBeTrue();

    [Test]
    public void SetTimerIndex_Negative_IsZero()
    {
        TfHudTimeStatus panel = Thought(Playing()).TimePanel;

        panel.SetTimerIndex(-3);

        panel.TimerIndex.ShouldBe(0);
    }

    [Test]
    public void SetExtraTimePanels_WaitingForPlayersInSetup_ShowsWaitingNotSetup()
    {
        TfHudTimeStatus panel = Updated(Playing() with { Rules = Rules() with { Setup = true, WaitingForPlayers = true } });

        (panel.FindChildByName("SetupLabel")!.Visible, panel.FindChildByName("WaitingForPlayersLabel")!.Visible).ShouldBe((false, true));
    }

    private static TfHudTimeStatus Updated(HudState state)
    {
        TfHudTimeStatus panel = Thought(state).TimePanel;
        SceneGameEvent update = new(0, "teamplay_update_timer", new Dictionary<string, object?>(), new Dictionary<int, PlayerInfo>());

        panel.HandleGameEvent(new HudGameEvent(update, 10f, [], 1, state.Rules, "cp_test", 0));
        return panel;
    }

    [Test]
    public void SetExtraTimePanels_InAStalemate_ShowsSuddenDeathOutsideArena()
    {
        Thought(Playing() with { RoundState = HudState.RoundStateStalemate }).TimePanel.FindChildByName("SuddenDeathLabel")!.Visible.ShouldBeTrue();
        Thought(Playing() with { RoundState = HudState.RoundStateStalemate, Rules = Rules() with { GameType = SceneGameRules.GameTypeArena } })
            .TimePanel.FindChildByName("SuddenDeathLabel")!.Visible.ShouldBeFalse();
    }

    [Test]
    public void SetExtraTimePanels_InOvertime_ShowsOvertime() =>
        Thought(Playing() with { Rules = Rules() with { Overtime = true } }).TimePanel.FindChildByName("OvertimeLabel")!.Visible.ShouldBeTrue();

    [Test]
    public void OnThink_OutsideTheMatchHud_SetsTheProgressBarToTheTimePassed()
    {
        TfProgressBar bar = (TfProgressBar)Thought(Playing(), useMatchHud: false).TimePanel.FindChildByName("TimePanelProgressBar")!;

        (bar.Visible, bar.Percentage).ShouldBe((true, 400f / 600f));
    }

    [Test]
    public void OnThink_InTheMatchHud_HidesTheProgressBar() =>
        Thought(Playing()).TimePanel.FindChildByName("TimePanelProgressBar")!.Visible.ShouldBeFalse();

    [Test]
    public void SetTeamBackground_OnRed_IsTheRedImage()
    {
        TfHudTimeStatus panel = Thought(Playing() with { Team = 2 }, useMatchHud: false).TimePanel;

        ((VguiScalableImagePanel)panel.FindChildByName("TimePanelBG")!).ImageName.ShouldBe("../hud/objectives_timepanel_red_bg");
        ((VguiScalableImagePanel)Thought(Playing(), useMatchHud: false).TimePanel.FindChildByName("TimePanelBG")!).ImageName
            .ShouldBe("../hud/objectives_timepanel_blue_bg", "the control");
    }

    [Test]
    public void Paint_AfterTimeAdded_DrawsTheDelta() =>
        PaintedAfterTimeAdded(90).ShouldBe("+1:30");

    /// <remarks>`NUM_TIMER_DELTA_ITEMS 10` (tf_time_panel.h:57): a ring of ten, each painted while it lives (:843) — B466.</remarks>
    [Test]
    public void Paint_TenDeltasWithinTheirLifetime_DrawsAllTen() =>
        PaintedAfterTimeAdded(61, 62, 63, 64, 65, 66, 67, 68, 69, 70).ShouldBe("+1:01+1:02+1:03+1:04+1:05+1:06+1:07+1:08+1:09+1:10");

    /// <remarks>`m_iTimerDeltaHead %= NUM_TIMER_DELTA_ITEMS` (tf_time_panel.cpp:402): the eleventh takes slot 0.</remarks>
    [Test]
    public void Paint_AnEleventhDelta_ReplacesTheOldest() =>
        PaintedAfterTimeAdded(61, 62, 63, 64, 65, 66, 67, 68, 69, 70, 71).ShouldBe("+1:11+1:02+1:03+1:04+1:05+1:06+1:07+1:08+1:09+1:10");

    /// <summary>What the panel paints after a `teamplay_timer_time_added` for timer 42 per entry, all at curtime 10.</summary>
    private static string PaintedAfterTimeAdded(params int[] seconds)
    {
        TfHudMatchStatus status = Built(Playing(), useMatchHud: true);
        TextRecorder surface = new();

        Think(status, Playing());

        foreach (int one in seconds)
        {
            SceneGameEvent added = new(0, "teamplay_timer_time_added", new Dictionary<string, object?> { ["timer"] = 42, ["seconds_added"] = one }, new Dictionary<int, PlayerInfo>());

            status.TimePanel.HandleGameEvent(new HudGameEvent(added, 10f, [], 1, Rules(), "cp_test", 0));
        }

        status.TimePanel.Paint(surface, Context());

        return string.Concat(surface.Glyphs.Select(glyph => glyph[0]));
    }

    [TestCase(3725, "1:02:05")]
    [TestCase(125, "02:05")]
    [TestCase(-40, "00:00")]
    public void OnThink_ShowingTheServerTimeLimit_WritesTheMapTimeLeft(int secondsLeft, string expected)
    {
        // `GetTimeLeft` (teamplayroundbased_gamerules.cpp:1226): mp_timelimit * 60 + m_flMapResetTime - curtime, floored at
        // 0; formatted by tf_time_panel.cpp:782-806 through TF_HUD_ServerTimeLeft / ...NoHours / ...ChangeOnRoundEnd.
        TfHudMatchStatus status = Thought(ServerLimited(secondsLeft));

        VguiLabel label = (VguiLabel)status.TimePanel.FindChildByName("ServerTimeLimitLabel")!;

        (label.Visible, label.Text).ShouldBe((true, expected));
    }

    [Test]
    public void OnThink_ServerTimeLimitCvarOff_HidesTheLabel()
    {
        TfHudMatchStatus status = Thought(ServerLimited(125) with { ConVars = TestConVars.Of(("mp_timelimit", "30")) });

        status.TimePanel.FindChildByName("ServerTimeLimitLabel")!.Visible.ShouldBeFalse("tf_hud_show_servertimelimit defaults to 0");
    }

    [Test]
    public void OnThink_ServerTimeLimitInSetup_HidesTheLabel()
    {
        HudState state = ServerLimited(125);
        TfHudMatchStatus status = Thought(state with { Rules = state.Rules with { Setup = true } });

        status.TimePanel.FindChildByName("ServerTimeLimitLabel")!.Visible.ShouldBeFalse("!TFGameRules()->InSetup() (:748)");
    }

    /// <summary>A 30-minute map limit with <paramref name="secondsLeft"/> left at curtime 10, and the cvar on.</summary>
    private static HudState ServerLimited(int secondsLeft) =>
        Playing() with
        {
            ConVars = TestConVars.Of(("tf_hud_show_servertimelimit", "1"), ("mp_timelimit", "30")),
            Rules = Rules() with { MapResetTime = 10f + secondsLeft - 1800f },
        };

    private static HudState Playing() =>
        new(true, true, 0, 125, true, CurTime: 10f, Team: 3, LocalIndex: 1, ServerTime: 100f, Rules: Rules(), RoundTimers: [Timer()]);

    private static SceneGameRules Rules() => new(false, 0, false) { TimerToShowInHud = 42 };

    private static SceneRoundTimer Timer() =>
        new(42) { EndTime = 300f, MaxLength = 600, Length = 600, State = SceneRoundTimer.StateNormal, ShowInHud = true, ShowTimeRemaining = true };

    private static TfHudMatchStatus Thought(HudState state, bool useMatchHud = true)
    {
        TfHudMatchStatus status = Built(state, useMatchHud);

        Think(status, state);
        return status;
    }

    private static void Think(TfHudMatchStatus status, HudState state)
    {
        HudViewport viewport = (HudViewport)status.Parent!;

        viewport.Think(state);
        VguiLayout.SolveTraverse(viewport, viewport.Context!);
        VguiLayout.SolveTraverse(viewport, viewport.Context!);
    }

    private static TfHudMatchStatus Built(HudState state, bool useMatchHud)
    {
        VguiContext context = Context();
        HudViewport viewport = new() { Wide = 640, Tall = 480, Context = context };
        TfHudMatchStatus status = new(viewport, new EntityModelSet());

        viewport.Think((state with { ObserverMode = ObserverModes.None }).WithMatchHud(useMatchHud));
        status.PerformApplySchemeSettings(context);
        return status;
    }

    private static VguiContext Context()
    {
        Dictionary<string, byte[]> files = new()
        {
            ["resource/UI/HudMatchStatus.res"] = Encoding.UTF8.GetBytes("""
                "Resource/UI/HudMatchStatus.res"
                {
                    "HudMatchStatus" { "fieldName" "HudMatchStatus" "wide" "640" "tall" "480" "visible" "1" }
                    "ObjectiveStatusTimePanel"
                    {
                        "ControlName" "EditablePanel" "fieldName" "ObjectiveStatusTimePanel" "wide" "110" "tall" "150" "visible" "0"
                        "delta_item_x" "22" "delta_item_start_y" "50" "delta_item_end_y" "70" "delta_lifetime" "1.5" "delta_item_font" "Default"
                        "TimePanelValue" { "ControlName" "CExLabel" "fieldName" "TimePanelValue" "wide" "80" "tall" "20" "visible" "1" }
                    }
                }
                """),
            ["resource/UI/HudObjectiveTimePanel.res"] = Encoding.UTF8.GetBytes("""
                "Resource/UI/HudObjectiveTimePanel.res"
                {
                    "TimePanelBG" { "ControlName" "ScalableImagePanel" "fieldName" "TimePanelBG" "wide" "78" "tall" "33" "visible" "1" "image" "../hud/objectives_timepanel_blue_bg" "if_match" { "visible" "0" } }
                    "TimePanelProgressBar" { "ControlName" "CTFProgressBar" "fieldName" "TimePanelProgressBar" "wide" "20" "tall" "20" "visible" "1" "if_match" { "visible" "0" } }
                    "WaitingForPlayersLabel" { "ControlName" "CExLabel" "fieldName" "WaitingForPlayersLabel" "visible" "0" "labelText" "#game_WaitingForPlayers" }
                    "WaitingForPlayersBG" { "ControlName" "CTFImagePanel" "fieldName" "WaitingForPlayersBG" "visible" "0" }
                    "OvertimeLabel" { "ControlName" "CExLabel" "fieldName" "OvertimeLabel" "visible" "0" "labelText" "#game_Overtime" }
                    "OvertimeBG" { "ControlName" "CTFImagePanel" "fieldName" "OvertimeBG" "visible" "0" }
                    "SuddenDeathLabel" { "ControlName" "CExLabel" "fieldName" "SuddenDeathLabel" "visible" "0" "labelText" "#game_SuddenDeath" }
                    "SuddenDeathBG" { "ControlName" "CTFImagePanel" "fieldName" "SuddenDeathBG" "visible" "0" }
                    "SetupLabel" { "ControlName" "CExLabel" "fieldName" "SetupLabel" "visible" "0" "labelText" "#game_Setup" }
                    "SetupBG" { "ControlName" "CTFImagePanel" "fieldName" "SetupBG" "visible" "0" }
                    "ServerTimeLimitLabel" { "ControlName" "CExLabel" "fieldName" "ServerTimeLimitLabel" "visible" "0" "labelText" "%servertimeleft%" }
                    "ServerTimeLimitLabelBG" { "ControlName" "CTFImagePanel" "fieldName" "ServerTimeLimitLabelBG" "visible" "0" }
                }
                """),
        };

        // As tf_english.txt ships them.
        Dictionary<string, string> strings = new()
        {
            ["TF_HUD_ServerTimeLeft"] = "%s1:%s2:%s3",
            ["TF_HUD_ServerTimeLeftNoHours"] = "%s1:%s2",
            ["TF_HUD_ServerNoTimeLimit"] = string.Empty,
            ["TF_HUD_ServerChangeOnRoundEnd"] = "00:00",
        };

        KeyValuesTree scheme = KeyValuesTree.Load(Encoding.UTF8.GetBytes("Scheme { Colors { } Borders { } Fonts { } }"), "scheme.res", _ => null);
        VguiScheme colours = VguiScheme.Load(scheme);

        return new VguiContext(colours, VguiBorders.Load(scheme, colours, 480), scheme.Find("Fonts")!, 640, 480, "english")
        {
            Surface = new TextRecorder(),
            Read = files.GetValueOrDefault,
            Localize = strings.GetValueOrDefault,
        };
    }
}
