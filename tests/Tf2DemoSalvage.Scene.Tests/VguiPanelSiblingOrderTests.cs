using System.Linq;

using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// `VPanel::SetParent` (vgui2.dll 0x18001c3e0) appends then calls `SetZPos` with the child's current z, and
/// `VPanel::SetZPos` (0x18001c610) is a strict bubble. docs/RISKS.md B552.
/// </summary>
public sealed class VguiPanelSiblingOrderTests
{
    private static string[] Order(VguiPanel parent) => [.. parent.Children.Select(child => child.Name)];

    [Test]
    public void SetParent_AmmoPairBothGivenZ5AfterParenting_ShadowEndsBeforeTheNumber()
    {
        VguiPanel root = new(null, "root");
        VguiPanel number = new(root, "AmmoInClip");
        number.ZPos = 5;
        VguiPanel shadow = new(root, "AmmoInClipShadow");
        shadow.ZPos = 5;

        Order(root).ShouldBe(["AmmoInClipShadow", "AmmoInClip"]);
    }

    [Test]
    public void SetParent_PairAtZ0_KeepsResOrder()
    {
        VguiPanel root = new(null, "root");
        _ = new VguiPanel(root, "main");
        _ = new VguiPanel(root, "shadow");

        Order(root).ShouldBe(["main", "shadow"]);
    }

    [Test]
    public void ZPos_NewcomerGivenTheSameZAsASibling_DoesNotPassIt()
    {
        VguiPanel root = new(null, "root");
        VguiPanel first = new(root, "first");
        first.ZPos = 5;
        VguiPanel second = new(root, "second");
        second.ZPos = 5;
        VguiPanel third = new(root, "third");
        third.ZPos = 5;

        Order(root).ShouldBe(["third", "second", "first"]);
    }

    [Test]
    public void ZPos_HigherValue_SinksLaterPastLowerSiblings()
    {
        VguiPanel root = new(null, "root");
        VguiPanel a = new(root, "a");
        _ = new VguiPanel(root, "b");
        _ = new VguiPanel(root, "c");

        a.ZPos = 1;

        Order(root).ShouldBe(["b", "c", "a"]);
    }

    [Test]
    public void ZPos_LowerValue_MovesEarlierPastHigherSiblings()
    {
        VguiPanel root = new(null, "root");
        _ = new VguiPanel(root, "a");
        VguiPanel b = new(root, "b");
        VguiPanel c = new(root, "c");
        c.ZPos = 5;
        b.ZPos = 5;
        c.ZPos = 2;

        Order(root).ShouldBe(["a", "c", "b"]);
    }

    [Test]
    public void ZPos_PanelWithNoParent_StoresTheValue()
    {
        VguiPanel orphan = new(null, "orphan");

        orphan.ZPos = 7;

        orphan.ZPos.ShouldBe(7);
    }

    [Test]
    public void SetParent_PanelsCreatedInZOrder_AreUnchanged()
    {
        VguiPanel root = new(null, "root");
        foreach ((string name, int z) in new[] { ("a", 0), ("b", 1), ("c", 2) })
        {
            VguiPanel panel = new(root, name);
            panel.ZPos = z;
        }

        Order(root).ShouldBe(["a", "b", "c"]);
    }
}
