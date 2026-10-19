using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Viewer3D.UiTests;

// Not in the `UiTests` namespace: its `ViewerSession` fixture launches a viewer for every test beneath it.
namespace Tf2DemoSalvage.Viewer3D.GoldenTests;

/// <summary>The failure reporter's tree walk never replaces the failure it reports (B558). No viewer is launched.</summary>
/// <remarks>The tree is a string-keyed dictionary of children; `describe` may be made to throw for one node.</remarks>
public sealed class WalkTreeTests
{
    private static readonly Dictionary<string, string[]> Tree = new()
    {
        ["window"] = ["menu", "status"],
        ["menu"] = ["file"],
        ["file"] = [],
        ["status"] = [],
    };

    private static string Walk(Func<string, string> describe, Func<string>? root = null) =>
        ViewerApplication.WalkTree(root ?? (() => "window"), describe, node => Tree[node]);

    [Test]
    public void WalkTree_NothingThrows_ReturnsTheWholeTreeIndented() =>
        Walk(node => node).ShouldBe(
            $"window{Environment.NewLine}  menu{Environment.NewLine}    file{Environment.NewLine}  status{Environment.NewLine}");

    [Test]
    public void WalkTree_AReadThrowsMidWalk_KeepsThePartialTreeAndNamesTheException()
    {
        string tree = Walk(node => node == "file"
            ? throw new TimeoutException("Operation timed out.") // stands in for CI's UIA COMException 0x80131505
            : node);

        tree.ShouldStartWith($"window{Environment.NewLine}  menu{Environment.NewLine}");
        tree.ShouldContain("tree unreadable after 2 lines: TimeoutException: Operation timed out.");
    }

    [Test]
    public void WalkTree_TheRootReadThrows_ReportsItAfterZeroLines() =>
        Walk(node => node, () => throw new InvalidOperationException("no window"))
            .ShouldBe("tree unreadable after 0 lines: InvalidOperationException: no window");

    [Test]
    public void WalkTree_AReadThrows_TheCallersMessageStillContainsThePrimaryText()
    {
        // The shape of Find's failure: the primary text, then the tree in the same string.
        string message = $"No element with automation id 'X' appeared. The live tree was:{Environment.NewLine}" +
            Walk(node => throw new NotSupportedException("AutomationId"));

        message.ShouldStartWith("No element with automation id 'X' appeared.");
        message.ShouldContain("NotSupportedException: AutomationId");
    }
}
