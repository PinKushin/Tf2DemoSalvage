using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`CTFHudTraining` (game/client/tf/tf_hud_training.cpp).</summary>
public sealed class TfHudTrainingConformanceTests
{
    /// <remarks>
    /// `AddTickSignal( GetVPanel(), 10 )` (:128) and `OnTick` (:291): the prompt follows `IsWaitingForTrainingContinue()` on
    /// a 10 ms tick of real time.
    /// </remarks>
    [Test]
    public void OnTick_WithinTenMillisecondsOfBeingMade_HasNotShownThePrompt()
    {
        (HudViewport viewport, TfHudTraining panel) = Built();
        HudState waiting = new(true, true, 0, 125, true) { Rules = new SceneGameRules(false, 0, false) { Present = true, WaitingForTrainingContinue = true } };

        Think(viewport, waiting with { RealTime = 0.009f });
        bool early = Prompt(panel).Visible;
        // 0.01f is 0.0099999998, which `GetTimeMillis` truncates to 9.
        Think(viewport, waiting with { RealTime = 0.0101f });

        (early, Prompt(panel).Visible).ShouldBe((false, true));
    }

    private static VguiPanel Prompt(TfHudTraining panel) => panel.FindChildByName("PressSpacebarToContinue")!;

    private static void Think(HudViewport viewport, HudState state)
    {
        viewport.Think(state);
        VguiLayout.SolveTraverse(viewport, viewport.Context!);
    }

    private static (HudViewport Viewport, TfHudTraining Panel) Built()
    {
        Dictionary<string, byte[]> files = new()
        {
            ["resource/UI/HudTraining.res"] = Encoding.UTF8.GetBytes(
                "\"Resource/UI/HudTraining.res\" { \"PressSpacebarToContinue\" { \"ControlName\" \"Label\" \"fieldName\" \"PressSpacebarToContinue\" \"visible\" \"0\" } }"),
        };
        KeyValuesTree scheme = KeyValuesTree.Load(Encoding.UTF8.GetBytes("Scheme { Borders { } Fonts { } }"), "scheme.res", _ => null);
        VguiScheme colours = VguiScheme.Load(scheme);
        VguiContext context = new(colours, VguiBorders.Load(scheme, colours, 480), scheme.Find("Fonts")!, 640, 480, "english")
        {
            Read = path => files.GetValueOrDefault(path),
        };
        HudViewport viewport = new() { Wide = 640, Tall = 480, Context = context };

        return (viewport, new TfHudTraining(viewport));
    }
}
