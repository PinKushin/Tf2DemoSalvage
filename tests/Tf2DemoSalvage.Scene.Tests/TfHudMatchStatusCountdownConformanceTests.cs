using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`CTFHudMatchStatus::HandleCountdown` (tf_hud_match_status.cpp:614), minus the 3D round sign and match doors.</summary>
public sealed class TfHudMatchStatusCountdownConformanceTests
{
    [Test]
    public void HandleGameEvent_RestartTimerTime_SetsTheCountdownLabel()
    {
        TfHudMatchStatus status = Built();

        status.HandleGameEvent(RestartTimer(7));

        ((VguiLabel)status.FindChildByName("CountdownLabel")!).Text.ShouldBe("7");
    }

    [Test]
    public void HandleGameEvent_RestartTimerTimeUpdated_ReplacesThePreviousValue()
    {
        TfHudMatchStatus status = Built();

        status.HandleGameEvent(RestartTimer(9));
        status.HandleGameEvent(RestartTimer(8));

        ((VguiLabel)status.FindChildByName("CountdownLabel")!).Text.ShouldBe("8");
    }

    [Test]
    public void HandleGameEvent_NotUsingTheMatchHud_DoesNothing()
    {
        TfHudMatchStatus status = Built(useMatchHud: false);

        status.HandleGameEvent(RestartTimer(7));

        ((VguiLabel)status.FindChildByName("CountdownLabel")!).Text.ShouldBe("%countdown%", "the format is unset, never substituted");
    }

    [TestCase(0, 0)]
    [TestCase(1, 1)]
    public void HandleGameEvent_TenSecondsLeft_ShowsTheCountdownOnlyAfterTheFirstRound(int roundsPlayed, int expectedAnimations)
    {
        // `case 10:` (tf_hud_match_status.cpp:629): `GetRoundsPlayed() == 0` takes the match-start doors, else the 2D countdown.
        TfHudMatchStatus status = Built();
        HudViewport viewport = (HudViewport)status.Parent!;
        VguiContext context = viewport.Context!;

        viewport.Animations.SetScriptFile(viewport, "scripts/countdown.txt", wipeAll: true, context).ShouldBeTrue();
        status.HandleGameEvent(RestartTimer(10, new SceneGameRules(false, 0, false) { RoundsPlayed = roundsPlayed }));

        viewport.Animations.ActiveAnimationCount.ShouldBe(expectedAnimations);
    }

    private static HudGameEvent RestartTimer(int time, SceneGameRules rules = default)
    {
        SceneGameEvent restart = new(0, "restart_timer_time", new Dictionary<string, object?> { ["time"] = time }, new Dictionary<int, Core.Net.PlayerInfo>());

        return new HudGameEvent(restart, 10f, [], 1, rules, "cp_test", 0);
    }

    private static TfHudMatchStatus Built(bool useMatchHud = true)
    {
        VguiContext context = Context();
        HudViewport viewport = new() { Wide = 640, Tall = 480, Context = context };
        TfHudMatchStatus status = new(viewport, new EntityModelSet());

        viewport.Think((default(HudState) with { ObserverMode = ObserverModes.None }).WithMatchHud(useMatchHud));
        status.PerformApplySchemeSettings(context);
        return status;
    }

    private static VguiContext Context()
    {
        Dictionary<string, byte[]> files = new()
        {
            ["scripts/countdown.txt"] = Encoding.UTF8.GetBytes("event HudMatchStatus_ShowCountdown\n{\n\tAnimate CountdownLabel Alpha 255 Linear 0.0 0.5\n}\n"),
            ["resource/UI/HudMatchStatus.res"] = Encoding.UTF8.GetBytes("""
                "Resource/UI/HudMatchStatus.res"
                {
                    "HudMatchStatus" { "fieldName" "HudMatchStatus" "wide" "640" "tall" "480" "visible" "1" }
                    "CountdownLabel" { "ControlName" "CExLabel" "fieldName" "CountdownLabel" "wide" "40" "tall" "40" "visible" "0" "labelText" "%countdown%" }
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
                    "ObjectiveStatusTimePanel" { "fieldName" "ObjectiveStatusTimePanel" "wide" "110" "tall" "150" "visible" "0" }
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
