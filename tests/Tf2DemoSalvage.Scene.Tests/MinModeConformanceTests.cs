using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// `BuildGroup::LoadControlSettings` (BuildGroup.cpp:953-960): under `cl_hud_minmode` every `.res` read from disk has its
/// `_minmode` keys promoted, whichever panel loads it.
/// </summary>
public sealed class MinModeConformanceTests
{
    [TestCase("1", 40)]
    [TestCase("0", 10)]
    public void LoadControlSettings_UnderClHudMinmode_PromotesMinmodeKeys(string minMode, int expectedX)
    {
        Dictionary<string, byte[]> files = new()
        {
            ["resource/UI/Test.res"] = Encoding.UTF8.GetBytes("""
                "Resource/UI/Test.res"
                {
                    "Child" { "ControlName" "EditablePanel" "fieldName" "Child" "xpos" "10" "xpos_minmode" "40" "wide" "5" "tall" "5" }
                }
                """),
        };
        KeyValuesTree scheme = KeyValuesTree.Load(Encoding.UTF8.GetBytes("Scheme { Colors { } Borders { } Fonts { } }"), "scheme.res", _ => null);
        VguiScheme colours = VguiScheme.Load(scheme);
        VguiContext context = new(colours, VguiBorders.Load(scheme, colours, 480), scheme.Find("Fonts")!, 640, 480, "english")
        {
            Surface = new TextRecorder(),
            Read = files.GetValueOrDefault,
            Localize = _ => null,
        };
        HudViewport viewport = new() { Wide = 640, Tall = 480, Context = context };
        VguiEditablePanel panel = new(viewport, "Test");

        viewport.SetConVars(new HudConVars(null, name => name == "cl_hud_minmode" ? minMode : null));
        panel.LoadControlSettings("resource/UI/Test.res", context);

        panel.FindChildByName("Child")!.X.ShouldBe(expectedX);
    }
}
