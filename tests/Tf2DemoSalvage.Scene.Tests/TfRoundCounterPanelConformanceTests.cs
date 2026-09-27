using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`CRoundCounterPanel` (game/client/tf/tf_hud_match_status.cpp:57-260): the round-win dots' visibility.</summary>
/// <remarks>
/// `mp_winlimit` 4, RED (team 2) at a score of 1, BLU (team 3) at a score of 3 — chosen so the round indicators (gated on
/// the win limit alone) and the win indicators (gated on <c>Min(winlimit, score)</c>) disagree, which is the only way a
/// test can tell the two counts apart.
/// </remarks>
public sealed class TfRoundCounterPanelConformanceTests
{
    [Test]
    public void PerformLayout_RoundIndicators_AreVisibleUpToTheWinLimit()
    {
        TfRoundCounterPanel counter = Thought(Playing());

        VisibleFlags(counter.BlueRoundIndicators).ShouldBe([true, true, true, true, false]);
        VisibleFlags(counter.RedRoundIndicators).ShouldBe([true, true, true, true, false]);
    }

    [Test]
    public void PerformLayout_WinIndicators_AreVisibleUpToTheLesserOfWinLimitAndScore()
    {
        TfRoundCounterPanel counter = Thought(Playing());

        // BLU scored 3, under the winlimit of 4 — three lit.
        VisibleFlags(counter.BlueWinIndicators).ShouldBe([true, true, true, false, false]);

        // RED scored 1 — one lit, not the winlimit's four.
        VisibleFlags(counter.RedWinIndicators).ShouldBe([true, false, false, false, false]);
    }

    [Test]
    public void PerformLayout_NotUsingTheMatchHud_LeavesEveryIndicatorAtItsDefault()
    {
        TfRoundCounterPanel counter = Thought(Playing(), useMatchHud: false);

        // Freshly created panels default to visible; PerformLayout returning early leaves them so.
        VisibleFlags(counter.BlueRoundIndicators).ShouldBe([true, true, true, true, true]);
    }

    [Test]
    public void PerformLayout_MissingATeam_LeavesEveryIndicatorAtItsDefault() =>
        Thought(Playing() with { Teams = [Team(TeamRed, 1)] }).BlueRoundIndicators.ShouldAllBe(indicator => indicator.Visible);

    [Test]
    public void PerformLayout_NoWinLimit_LeavesEveryIndicatorHidden() =>
        // `LayoutPanels` returns without positioning, but `VisibleCondition(images, 0)` still hides every one.
        Thought(Playing() with { ConVars = default }).BlueRoundIndicators.ShouldAllBe(indicator => !indicator.Visible);

    private const int TeamRed = 2;
    private const int TeamBlue = 3;

    private static List<bool> VisibleFlags(IReadOnlyList<VguiImagePanel> images)
    {
        List<bool> flags = [];

        foreach (VguiImagePanel image in images)
        {
            flags.Add(image.Visible);
        }

        return flags;
    }

    private static SceneTeam Team(int number, int score) => new(number) { Score = score };

    private static HudState Playing() =>
        new(true, true, 0, 125, true, CurTime: 10f, ConVars: TestConVars.Of(("mp_winlimit", "4")), Teams: [Team(TeamRed, 1), Team(TeamBlue, 3)]);

    private static TfRoundCounterPanel Thought(HudState state, bool useMatchHud = true)
    {
        VguiContext context = Context();
        HudViewport viewport = new() { Wide = 640, Tall = 480, Context = context };
        TfHudMatchStatus status = new(viewport) { UseMatchHud = useMatchHud };

        viewport.Think(state with { ObserverMode = ObserverModes.None });
        status.PerformApplySchemeSettings(context);

        viewport.Think(state with { ObserverMode = ObserverModes.None });
        VguiLayout.SolveTraverse(viewport, context);
        VguiLayout.SolveTraverse(viewport, context);

        return status.RoundCounter;
    }

    private static VguiContext Context()
    {
        Dictionary<string, byte[]> files = new()
        {
            ["resource/UI/HudMatchStatus.res"] = Encoding.UTF8.GetBytes("""
                "Resource/UI/HudMatchStatus.res"
                {
                    "HudMatchStatus" { "fieldName" "HudMatchStatus" "wide" "640" "tall" "480" "visible" "1" }
                    "RoundCounter"
                    {
                        "ControlName" "EditablePanel"
                        "fieldName" "RoundCounter"
                        "xpos" "cs-0.5"
                        "ypos" "-2"
                        "wide" "300"
                        "tall" "25"
                        "visible" "1"
                        "indicator_start_offset" "4"
                        "indicator_max_wide" "30"
                        "RoundIndicatorPanel_kv" { "wide" "6" "tall" "6" "image" "../hud/comp_round_counter_dot_bg" }
                        "RoundWinPanelRed_kv" { "wide" "17" "tall" "17" "image" "../hud/comp_round_counter_light_red" }
                        "RoundWinPanelBlue_kv" { "wide" "17" "tall" "17" "image" "../hud/comp_round_counter_light_blue" }
                    }
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
