using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`vgui_controls` panels built from `.res` blocks, as `Panel.cpp`, `EditablePanel.cpp` and `BuildGroup.cpp` do.</summary>
/// <remarks>
/// `Panel::ApplySettings` (Panel.cpp:4340) sizes then positions against the SCREEN unless `proportionalToParent` is 1;
/// `BuildGroup::ApplySettings` (BuildGroup.cpp:1234) hands each block to the first registered panel of that name, case-
/// insensitive, else makes one from its `ControlName`; an `EditablePanel` registers itself first (EditablePanel.cpp:63).
/// </remarks>
public sealed class VguiPanelConformanceTests
{
    private const int ScreenWide = 1920;
    private const int ScreenTall = 1080;

    [Test]
    public void ApplySettings_APlainBlock_SizesAndPlacesAgainstTheScreen()
    {
        VguiEditablePanel root = Root();
        VguiPanel child = new(root, "Child");

        root.ApplySettings(Block("""
            "Child" { "xpos" "r10" "ypos" "c0" "wide" "20" "tall" "f0" "zpos" "3" "visible" "0" "enabled" "0" }
            """), Context());

        (child.X, child.Y, child.Wide, child.Tall).ShouldBe((ScreenWide - 22, ScreenTall / 2, 45, ScreenTall));
        child.ZPos.ShouldBe(3);
        child.Visible.ShouldBeFalse();
        child.Enabled.ShouldBeFalse();
    }

    [Test]
    public void ApplySettings_ProportionalToParent_MeasuresAgainstTheParent()
    {
        VguiEditablePanel root = Root();
        VguiPanel child = new(root, "Child");

        root.Wide = 400;
        root.Tall = 300;
        root.ApplySettings(Block("""
            "Child" { "xpos" "r0" "wide" "f0" "proportionalToParent" "1" }
            """), Context());

        (child.X, child.Wide).ShouldBe((400, 400));
    }

    [Test]
    public void ApplySettings_ABlockNamedLikeTheEditablePanel_AppliesToItself()
    {
        VguiEditablePanel root = Root();

        root.ApplySettings(Block("""
            "ROOT" { "wide" "100" }
            """), Context());

        root.Wide.ShouldBe(225);
    }

    [Test]
    public void ApplySettings_AnUnknownNameWithAControlName_IsMadeByTheFactory()
    {
        VguiEditablePanel root = Root();

        root.ApplySettings(Block("""
            "Made" { "ControlName" "Panel" "fieldName" "Renamed" "wide" "10" }
            "Unmade" { "ControlName" "NoSuchControl" }
            "atomic" "1"
            """), Context());

        root.Children.Count.ShouldBe(1);
        root.Children[0].Name.ShouldBe("Renamed");
        root.Children[0].Wide.ShouldBe(22);
    }

    [Test]
    public void ApplySettings_TwoPanelsOfOneName_OnlyTheFirstRegisteredIsSet()
    {
        VguiEditablePanel root = Root();
        VguiPanel first = new(root, "Twin");
        VguiPanel second = new(root, "twin");

        root.ApplySettings(Block("""
            "TWIN" { "wide" "10" }
            """), Context());

        (first.Wide, second.Wide).ShouldBe((22, 64));
    }

    [Test]
    public void ApplySettings_ANestedEditablePanel_AppliesItsOwnBlockToItsChildren()
    {
        VguiEditablePanel root = Root();
        VguiEditablePanel inner = new(root, "Inner");
        VguiPanel leaf = new(inner, "Leaf");

        root.ApplySettings(Block("""
            "Inner" { "wide" "100" "Leaf" { "wide" "10" } }
            "Leaf" { "wide" "50" }
            """), Context());

        (inner.Wide, leaf.Wide).ShouldBe((225, 22));
    }

    [Test]
    public void ApplySettings_ABorderName_IsTheSchemesBorder()
    {
        VguiEditablePanel root = Root();
        VguiPanel child = new(root, "Child");

        root.ApplySettings(Block("""
            "Child" { "border" "Frame" "paintbackground" "0" "paintborder" "0" }
            """), Context());

        child.Border.ShouldNotBeNull();
        child.Border.Name.ShouldBe("Frame");
        child.PaintBackgroundEnabled.ShouldBeFalse();
        child.PaintBorderEnabled.ShouldBeFalse();
    }

    [Test]
    public void ApplySchemeSettings_AColourOverride_SurvivesTheSchemeColours()
    {
        VguiEditablePanel root = Root();
        VguiPanel child = new(root, "Child");
        VguiContext context = Context();

        root.ApplySettings(Block("""
            "Child" { "fgcolor_override" "10.9 20 30 40" "bgcolor_override" "Orange" }
            """), context);
        child.ApplySchemeSettings(context);

        child.FgColor.ShouldBe(((byte)10, (byte)20, (byte)30, (byte)40), "sscanf %f, cast to unsigned char");
        child.BgColor.ShouldBe(((byte)255, (byte)128, (byte)0, (byte)255));
    }

    [Test]
    public void ApplySchemeSettings_NoOverride_TakesPanelFgAndBgColor()
    {
        VguiPanel panel = new(null, "Plain");

        panel.ApplySchemeSettings(Context());

        panel.FgColor.ShouldBe(((byte)1, (byte)2, (byte)3, (byte)4));
    }

    [Test]
    public void ProcessConditionalKeys_AMatchingCondition_PromotesItsKeys()
    {
        KeyValuesTree block = Block("""
            "Child" { "wide" "10" "if_mvm" { "wide" "20" "tall" "30" } }
            """);

        VguiBuildGroup.ProcessConditionalKeys(block, ["if_mvm"]);

        KeyValuesTree child = block.Find("Child")!;
        child.Find("wide")!.Value.ShouldBe("20");
        child.Find("tall")!.Value.ShouldBe("30");
    }

    [Test]
    public void AnimationVars_EachConverter_ReadsAsPanelCppConvertsIt()
    {
        VguiEditablePanel root = Root();
        VarPanel panel = new(root, "Vars");

        root.ApplySettings(Block("""
            "Vars" { "pint" "10" "pfloat" "10.9" "color" "Orange" "flag" "2" "xp" "r5" }
            """), Context());

        panel.GetInt("pint").ShouldBe(22, "proportional_int scales even though the panel is not proportional");
        panel.GetFloat("pfloat").ShouldBe(22f, "the float is scaled through an int, so the fraction is lost");
        panel.GetColor("color").ShouldBe(((byte)255, (byte)128, (byte)0, (byte)255));
        panel.GetBool("flag").ShouldBeTrue();
        panel.GetInt("xp").ShouldBe(ScreenWide - 5, "proportional_xpos goes through ComputePos, which scales only a proportional panel");
        panel.GetInt("untouched").ShouldBe(7, "the default");
    }

    private sealed class VarPanel : VguiPanel
    {
        public VarPanel(VguiPanel? parent, string name)
            : base(parent, name)
        {
            Proportional = false;
            DeclareAnimationVar("pint", VguiPanelVarType.ProportionalInt, "0");
            DeclareAnimationVar("pfloat", VguiPanelVarType.ProportionalFloat, "0");
            DeclareAnimationVar("color", VguiPanelVarType.Color, "0 0 0 0");
            DeclareAnimationVar("flag", VguiPanelVarType.Bool, "false");
            DeclareAnimationVar("xp", VguiPanelVarType.ProportionalXPos, "0");
            DeclareAnimationVar("untouched", VguiPanelVarType.Whole, "7");
        }
    }

    private static VguiEditablePanel Root() => new(null, "Root") { Proportional = true };

    private static KeyValuesTree Block(string body) =>
        KeyValuesTree.Load(Encoding.UTF8.GetBytes("Resource { " + body + " }"), "test.res", _ => null);

    private static VguiContext Context()
    {
        KeyValuesTree scheme = KeyValuesTree.Load(
            Encoding.UTF8.GetBytes("""
                Scheme
                {
                    Colors { "Orange" "255 128 0 255" }
                    BaseSettings { "Panel.FgColor" "1 2 3 4" }
                    Borders { "Frame" { } }
                    Fonts { }
                }
                """),
            "scheme.res",
            _ => null);
        VguiScheme colours = VguiScheme.Load(scheme);

        return new VguiContext(colours, VguiBorders.Load(scheme, colours, ScreenTall), scheme.Find("Fonts")!, ScreenWide, ScreenTall, "english");
    }
}
