using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>Dialog variables: `EditablePanel::SetDialogVariable`, `Label::OnDialogVariablesChanged`, `ConstructString`.</summary>
/// <remarks>
/// `ForceSubPanelsToUpdateWithNewDialogVariables` (EditablePanel.cpp:1036) sends the variables to the panel and its
/// direct children only. A label whose text came from a localised token (a `%name%` label text is one) rebuilds it with
/// `ConstructString` (vgui2.dll 0x180025320): `%name%` becomes the variable, `[unknown]` when unset; `%%` is `%`; a `%s`
/// followed by a digit, or a `%` with no closing one, stays as written.
/// </remarks>
public sealed class VguiDialogVariablesConformanceTests
{
    [TestCase("%Health%", "150")]
    [TestCase("HP %Health% of %Max%", "HP 150 of [unknown]")]
    [TestCase("100%% sure", "100% sure")]
    [TestCase("%s1 is %Health%", "%s1 is 150")]
    [TestCase("50% off", "50% off")]
    [TestCase("", "")]
    public void ConstructString_Variables_AreSubstituted(string format, string expected) =>
        VguiLocalize.ConstructString(format, new Dictionary<string, string> { ["Health"] = "150" }).ShouldBe(expected);

    [Test]
    public void ConstructString_NoVariables_LeavesTheNamesAsWritten() =>
        VguiLocalize.ConstructString("%Health%", null).ShouldBe("%Health%");

    [Test]
    public void SetDialogVariable_ADirectChildLabel_ShowsTheValue()
    {
        (VguiEditablePanel panel, VguiLabel child, VguiLabel grandchild) = Tree();

        panel.SetDialogVariable("Health", 150);

        child.Text.ShouldBe("150");
        grandchild.Text.ShouldBe("%Health%", "only direct children are told");
    }

    [Test]
    public void SetDialogVariable_ALocalisedToken_IsRebuiltFromItsFormat()
    {
        VguiContext context = Context();
        VguiEditablePanel panel = new(null, "Panel");
        VguiLabel label = new(panel, "Label");

        panel.LoadControlSettings(Resource("\"Label\" { \"labelText\" \"#TF_Health\" }"), context);
        panel.SetDialogVariable("Health", 99);
        label.Text.ShouldBe("Health: 99");

        panel.SetDialogVariable("Health", 98);
        label.Text.ShouldBe("Health: 98", "the format is kept, not the last text");
    }

    [Test]
    public void SetDialogVariable_PlainText_IsNotTouched()
    {
        VguiContext context = Context();
        VguiEditablePanel panel = new(null, "Panel");
        VguiLabel label = new(panel, "Label");

        panel.LoadControlSettings(Resource("\"Label\" { \"labelText\" \"Plain %Health%\" }"), context);
        panel.SetDialogVariable("Health", 1);

        label.Text.ShouldBe("Plain %Health%", "only a localised token remembers a format");
    }

    [Test]
    public void LoadControlSettings_VariablesSetBefore_AreApplied()
    {
        VguiContext context = Context();
        VguiEditablePanel panel = new(null, "Panel");
        VguiLabel label = new(panel, "Label");

        panel.SetDialogVariable("Health", 7);
        panel.LoadControlSettings(Resource("\"Label\" { \"labelText\" \"%Health%\" }"), context);

        label.Text.ShouldBe("7");
    }

    [Test]
    public void SetDialogVariable_AFloat_IsSixDecimalPlaces()
    {
        (VguiEditablePanel panel, VguiLabel child, _) = Tree();

        panel.SetDialogVariable("Health", 1.5f);

        child.Text.ShouldBe("1.500000");
    }

    private static (VguiEditablePanel Panel, VguiLabel Child, VguiLabel Grandchild) Tree()
    {
        VguiContext context = Context();
        VguiEditablePanel panel = new(null, "Panel");
        VguiLabel child = new(panel, "Child");
        VguiEditablePanel middle = new(panel, "Middle");
        VguiLabel grandchild = new(middle, "Grandchild");

        panel.LoadControlSettings(Resource("\"Child\" { \"labelText\" \"%Health%\" }"), context);
        middle.LoadControlSettings(Resource("\"Grandchild\" { \"labelText\" \"%Health%\" }"), context);

        return (panel, child, grandchild);
    }

    private static KeyValuesTree Resource(string body) =>
        KeyValuesTree.Load(Encoding.UTF8.GetBytes("Resource { " + body + " }"), "test.res", _ => null);

    private static VguiContext Context()
    {
        KeyValuesTree scheme = KeyValuesTree.Load(Encoding.UTF8.GetBytes("Scheme { Borders { } Fonts { } }"), "scheme.res", _ => null);
        VguiScheme colours = VguiScheme.Load(scheme);
        Dictionary<string, string> strings = new() { ["TF_Health"] = "Health: %Health%" };

        return new VguiContext(colours, VguiBorders.Load(scheme, colours, 480), scheme.Find("Fonts")!, 640, 480, "english")
        {
            Surface = new TextRecorder(),
            Localize = strings.GetValueOrDefault,
        };
    }
}
