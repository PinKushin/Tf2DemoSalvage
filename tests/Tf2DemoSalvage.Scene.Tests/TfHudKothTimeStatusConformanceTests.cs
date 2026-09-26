using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`CTFHudKothTimeStatus` (tf_time_panel.cpp:893): KOTH's two timers, the running one marked.</summary>
/// <remarks>BLU's timer is 504, running, 100 seconds left; RED's 505, paused at 180. The local player is RED.</remarks>
public sealed class TfHudKothTimeStatusConformanceTests
{
    [Test]
    public void Think_OnKoth_PointsEachPanelAtItsTeamsTimer()
    {
        TfHudKothTimeStatus koth = Thought(Koth());

        (koth.Visible, koth.BluePanel.TimerIndex, koth.RedPanel.TimerIndex).ShouldBe((true, 504, 505));
        (koth.BluePanel.TimeValue.Text, koth.RedPanel.TimeValue.Text).ShouldBe(("1:40", "3:00"));
    }

    [Test]
    public void ShouldDraw_OutsideKothOrWaitingOrFreezeCamOrSummary_IsHidden()
    {
        TfHudKothTimeStatus koth = Thought(Koth());

        koth.ShouldDraw(Koth() with { Rules = KothRules() with { Koth = false } }).ShouldBeFalse();
        koth.ShouldDraw(Koth() with { Rules = KothRules() with { WaitingForPlayers = true } }).ShouldBeFalse();
        koth.ShouldDraw(Koth() with { ObserverMode = ObserverModes.FreezeCam }).ShouldBeFalse();
        koth.ShouldDraw(Koth() with { Rules = KothRules() with { ShowMatchSummary = true } }).ShouldBeFalse();
        koth.ShouldDraw(Koth()).ShouldBeTrue("the control");
    }

    [Test]
    public void UpdateActiveTeam_OutsideTheMatchHud_MovesTheActiveBackgroundToTheRunningTeam()
    {
        VguiPanel background = Thought(Koth(), useMatchHud: false).FindChildByName("ActiveTimerBG")!;

        (background.Visible, background.X).ShouldBe((true, 11));
    }

    [Test]
    public void UpdateActiveTeam_RedRunning_MovesItToRed()
    {
        HudState redRunning = Koth() with { RoundTimers = [Blue() with { Paused = true }, Red() with { Paused = false }] };

        Thought(redRunning, useMatchHud: false).FindChildByName("ActiveTimerBG")!.X.ShouldBe(77);
    }

    [Test]
    public void UpdateActiveTeam_NeitherRunning_HidesIt() =>
        Thought(Koth() with { RoundTimers = [Blue() with { Paused = true }, Red()] }, useMatchHud: false)
            .FindChildByName("ActiveTimerBG")!.Visible.ShouldBeFalse();

    [Test]
    public void SetTeamBackground_InKoth_IsThePanelsTeamNotTheLocalPlayers()
    {
        TfHudKothTimeStatus koth = Thought(Koth(), useMatchHud: false);

        ((VguiScalableImagePanel)koth.BluePanel.FindChildByName("TimePanelBG")!).ImageName.ShouldBe("../hud/objectives_timepanel_blue_bg");
        ((VguiScalableImagePanel)koth.RedPanel.FindChildByName("TimePanelBG")!).ImageName.ShouldBe("../hud/objectives_timepanel_red_bg");
    }

    private static SceneGameRules KothRules() => new(false, 0, false) { Koth = true, BlueKothTimer = 504, RedKothTimer = 505 };

    private static SceneRoundTimer Blue() =>
        new(504) { EndTime = 200f, MaxLength = 180, State = SceneRoundTimer.StateNormal, ShowInHud = true, ShowTimeRemaining = true };

    private static SceneRoundTimer Red() =>
        new(505) { Paused = true, TimeRemaining = 180f, MaxLength = 180, State = SceneRoundTimer.StateNormal, ShowInHud = true, ShowTimeRemaining = true };

    private static HudState Koth() =>
        new(true, true, 0, 125, true, CurTime: 10f, Team: 2, LocalIndex: 1, ServerTime: 100f, Rules: KothRules(), RoundTimers: [Blue(), Red()]);

    private static TfHudKothTimeStatus Thought(HudState state, bool useMatchHud = true)
    {
        VguiContext context = Context();
        HudViewport viewport = new() { Wide = 640, Tall = 480, Context = context };
        TfHudKothTimeStatus koth = new(viewport) { UseMatchHud = useMatchHud };

        viewport.Think(state);
        koth.PerformApplySchemeSettings(context);
        viewport.Think(state);
        VguiLayout.SolveTraverse(viewport, context);
        VguiLayout.SolveTraverse(viewport, context);
        return koth;
    }

    private static VguiContext Context()
    {
        Dictionary<string, byte[]> files = new()
        {
            ["resource/UI/HudObjectiveKothTimePanel.res"] = Encoding.UTF8.GetBytes("""
                "Resource/UI/HudObjectiveKothTimePanel.res"
                {
                    "HudKothTimeStatus" { "fieldName" "HudKothTimeStatus" "wide" "200" "tall" "80" "visible" "1" "blue_active_xpos" "11" "red_active_xpos" "77" }
                    "BlueTimer" { "ControlName" "CTFHudTimeStatus" "fieldName" "BlueTimer" "wide" "100" "tall" "60" "visible" "1"
                        "TimePanelValue" { "ControlName" "CExLabel" "fieldName" "TimePanelValue" "wide" "80" "tall" "20" "visible" "1" } }
                    "RedTimer" { "ControlName" "CTFHudTimeStatus" "fieldName" "RedTimer" "xpos" "90" "wide" "100" "tall" "60" "visible" "1"
                        "TimePanelValue" { "ControlName" "CExLabel" "fieldName" "TimePanelValue" "wide" "80" "tall" "20" "visible" "1" } }
                    "ActiveTimerBG" { "ControlName" "ImagePanel" "fieldName" "ActiveTimerBG" "ypos" "5" "wide" "50" "tall" "50" "visible" "0" }
                }
                """),
            ["resource/UI/HudObjectiveTimePanel.res"] = Encoding.UTF8.GetBytes("""
                "Resource/UI/HudObjectiveTimePanel.res"
                {
                    "TimePanelBG" { "ControlName" "ScalableImagePanel" "fieldName" "TimePanelBG" "wide" "78" "tall" "33" "visible" "1" "image" "../hud/objectives_timepanel_blue_bg" }
                }
                """),
        };

        KeyValuesTree scheme = KeyValuesTree.Load(Encoding.UTF8.GetBytes("Scheme { Colors { } Borders { } Fonts { } }"), "scheme.res", _ => null);
        VguiScheme colours = VguiScheme.Load(scheme);

        return new VguiContext(colours, VguiBorders.Load(scheme, colours, 480), scheme.Find("Fonts")!, 640, 480, "english")
        {
            Surface = new TextRecorder(),
            Read = files.GetValueOrDefault,
            Localize = _ => null,
        };
    }
}
