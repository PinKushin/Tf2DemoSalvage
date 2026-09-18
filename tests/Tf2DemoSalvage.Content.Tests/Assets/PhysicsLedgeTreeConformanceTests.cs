using System.Numerics;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// <see cref="PhysicsLedgeTree.SingleLedge"/> — the flat, one-node shortcut for a body whose collision is exactly
/// one convex ledge, the ordinary shape of a ragdoll bone's own `.phy` data (B369).
/// </summary>
public sealed class PhysicsLedgeTreeConformanceTests
{
    /// <remarks>A terminal node naming the ledge, with the ledge's own sphere carried through unchanged.</remarks>
    [Test]
    public void SingleLedge_AConvexLedge_BuildsATerminalNodeNamingIt()
    {
        PhysicsLedge ledge = new(
            Points: [new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f)],
            Triangles: [(0, 1, 2)],
            EdgeOffsets: [(0, 0, 0)],
            PierceTriangles: [0],
            MaterialIndices: [0],
            Center: new Vector3(1f, 2f, 3f),
            Radius: 4f);

        PhysicsLedgeTreeNode node = PhysicsLedgeTree.SingleLedge(ledge);

        node.HasLedge.ShouldBeTrue();
        node.Ledge.ShouldBe(ledge);
        node.Center.ShouldBe(ledge.Center);
        node.Radius.ShouldBe(ledge.Radius);
        node.LedgeChildren.ShouldBe(0);
        node.IsTerminal.ShouldBeTrue();
        node.Left.ShouldBeNull();
        node.Right.ShouldBeNull();
    }

    /// <remarks>
    /// A maxed box makes <c>IvpLedgeTree.Walk</c>'s box test a no-op — the widest a byte box can state, so it can
    /// only ever pass and never wrongly excludes the one ledge this node names.
    /// </remarks>
    [Test]
    public void SingleLedge_AConvexLedge_MaxesOutTheBoxSoItNeverExcludesTheLedge()
    {
        PhysicsLedge ledge = new(
            Points: [new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f)],
            Triangles: [(0, 1, 2)],
            EdgeOffsets: [(0, 0, 0)],
            PierceTriangles: [0],
            MaterialIndices: [0],
            Center: Vector3.Zero,
            Radius: 1f);

        PhysicsLedgeTreeNode node = PhysicsLedgeTree.SingleLedge(ledge);

        node.Box.ShouldBe(((byte)255, (byte)255, (byte)255));
    }
}
