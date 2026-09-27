using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`Panel::SetScheme` and `GetScheme` (Panel.cpp): a panel's own scheme, else its parent's, else the root's.</summary>
public sealed class VguiPanelSchemeTests
{
    [Test]
    public void SchemeSettingsTraverse_APanelWithItsOwnScheme_AppliesThatOneToItAndItsChildren()
    {
        VguiContext client = Context("0 0 0 255");
        VguiContext chat = Context("9 9 9 255");
        VguiPanel root = new(null, "root");
        VguiPanel owner = new(root, "owner") { SchemeContext = chat };
        VguiPanel child = new(owner, "child");
        VguiPanel sibling = new(root, "sibling");

        VguiLayout.SolveTraverse(root, client);

        (owner.FgColor.Red, child.FgColor.Red, sibling.FgColor.Red).ShouldBe(((byte)9, (byte)9, (byte)0));
    }

    private static VguiContext Context(string panelColor)
    {
        KeyValuesTree scheme = KeyValuesTree.Load(
            Encoding.UTF8.GetBytes($"Scheme {{ Colors {{ }} BaseSettings {{ \"Panel.FgColor\" \"{panelColor}\" }} Borders {{ }} Fonts {{ }} }}"),
            "scheme.res",
            _ => null);
        VguiScheme colours = VguiScheme.Load(scheme);

        return new VguiContext(colours, VguiBorders.Load(scheme, colours, 480), scheme.Find("Fonts")!, 640, 480, "english") { Surface = new TextRecorder() };
    }
}
