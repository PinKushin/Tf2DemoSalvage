using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>Paint order and absolute placement, as `vgui2.dll`'s `VPanel` keeps them.</summary>
/// <remarks>
/// Closed code, renamed in `tf2vgui2`: `VPanel_SetZPos` (0x18001c610) stores a `short` and bubbles the panel through its
/// parent's children — past a neighbour only when strictly out of order, so equal z keeps insertion order.
/// `VPanel_Solve` (0x18001c8e0): a pinned panel's position is measured from its sibling's corner (the table at
/// 0x18007c530), the offset negated on a sibling's left or top edge; then parent position plus inset, and a clip rectangle
/// cut to the parent's, its far edge pulled in by the parent's right and bottom inset.
/// </remarks>
public sealed class VguiSolveConformanceTests
{
    [Test]
    public void ZPos_Raised_MovesPastEveryLowerSibling()
    {
        VguiPanel parent = new(null, "Parent");
        VguiPanel a = new(parent, "A");
        VguiPanel b = new(parent, "B");
        VguiPanel c = new(parent, "C");

        a.ZPos = 5;
        c.ZPos = -1;

        parent.Children.ShouldBe([c, b, a]);
    }

    [Test]
    public void ZPos_Equal_KeepsTheOrderTheyWereAdded()
    {
        VguiPanel parent = new(null, "Parent");
        VguiPanel a = new(parent, "A");
        VguiPanel b = new(parent, "B");

        b.ZPos = 0;
        a.ZPos = 0;

        parent.Children.ShouldBe([a, b]);
    }

    [Test]
    public void ZPos_BeyondAShort_Wraps() =>
        new VguiPanel(null, "Wide") { ZPos = 70000 }.ZPos.ShouldBe(70000 - 65536);

    [Test]
    public void Solve_APlainChild_IsParentPositionPlusInsetPlusItsOwn()
    {
        VguiPanel parent = new(null, "Parent") { X = 10, Y = 20, Wide = 100, Tall = 100, Inset = (1, 2, 3, 4) };
        VguiPanel child = new(parent, "Child") { X = 5, Y = 6, Wide = 20, Tall = 20 };

        VguiLayout.InternalSolveTraverse(parent);

        (child.AbsX, child.AbsY).ShouldBe((16, 28));
    }

    [Test]
    public void Solve_AChildPastItsParent_IsClippedToTheParentLessItsFarInset()
    {
        VguiPanel parent = new(null, "Parent") { Wide = 100, Tall = 100, Inset = (0, 0, 3, 4) };
        VguiPanel child = new(parent, "Child") { X = 90, Y = 90, Wide = 20, Tall = 20 };

        VguiLayout.InternalSolveTraverse(parent);

        child.ClipRect.ShouldBe((90, 90, 97, 96));
    }

    [Test]
    public void Solve_PinnedToASiblingsRightEdge_AddsTheOffset()
    {
        (VguiPanel sibling, VguiPanel pinned) = Pair("PIN_TOPLEFT", "PIN_TOPRIGHT", x: 5, y: 3);

        VguiLayout.InternalSolveTraverse(sibling.Parent!);

        (pinned.AbsX, pinned.AbsY).ShouldBe((145, 47), "x from the right edge onward; y above the sibling's top");
    }

    [Test]
    public void Solve_PinnedToASiblingsLeftEdge_SubtractsTheOffsetAndItsOwnWidth()
    {
        (VguiPanel sibling, VguiPanel pinned) = Pair("PIN_TOPRIGHT", "PIN_TOPLEFT", x: 5, y: 0);

        VguiLayout.InternalSolveTraverse(sibling.Parent!);

        pinned.AbsX.ShouldBe(100 - 30 - 5);
    }

    [Test]
    public void Solve_PinnedToACentre_TakesHalfTheSibling()
    {
        (VguiPanel sibling, VguiPanel pinned) = Pair("PIN_CENTER_TOP", "PIN_CENTER_BOTTOM", x: 0, y: 2);

        VguiLayout.InternalSolveTraverse(sibling.Parent!);

        (pinned.AbsX, pinned.AbsY).ShouldBe((100 + 20 - 15, 50 + 20 + 2));
    }

    private static (VguiPanel Sibling, VguiPanel Pinned) Pair(string ownCorner, string siblingCorner, int x, int y)
    {
        VguiEditablePanel parent = new(null, "Parent") { Wide = 640, Tall = 480 };
        VguiPanel sibling = new(parent, "Anchor") { X = 100, Y = 50, Wide = 40, Tall = 20 };
        VguiPanel pinned = new(parent, "Pinned") { X = x, Y = y, Wide = 30, Tall = 10 };

        pinned.PinTo("anchor", ownCorner, siblingCorner);

        return (sibling, pinned);
    }
}
