using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// The leaf list a view produces from a tree small enough to predict by hand.
/// </summary>
/// <remarks>
/// **A real BSP cannot answer this, which is why the tree is built here.** Asking cp_process which
/// leaves are visible from a point means trusting the walk to check the walk — the same reason
/// `BspLeafTreeTests` builds a one-node tree rather than loading a map. What a real map CAN answer
/// is whether the numbers are plausible at scale, and that is a separate test on a separate
/// subject.
///
/// **The tree below is two leaves either side of the plane x = 0**, which is the smallest shape
/// that distinguishes every property worth asserting: which side the camera is on, which child is
/// walked first, and whether a subtree can be rejected whole.
/// </remarks>
public sealed class WorldVisibilityTests
{
    /// <summary>Leaf 0 spans x −512..0, leaf 1 spans 0..512; both are 1024 tall and deep.</summary>
    /// <remarks>
    /// **Encoded as `−(leaf + 1)`, so the children are −1 and −2.** Writing 0 and 1 would send the
    /// walk back into the node array and loop, which is precisely the encoding Valve's comment
    /// warns about.
    ///
    /// The node's own cull box covers both leaves, so it rejects nothing on its own — a subtree
    /// rejection needs the deeper tree built in its own case below.
    /// </remarks>
    private static BspLeafTree Split()
    {
        byte[] node = Node(plane: 0, front: -2, back: -1, (-512, -512, -512), (512, 512, 512));

        // The plane x = 0, normal down +X. Front (child 0) is the +x side.
        byte[] planes = Plane(1f, 0f, 0f, 0f);

        byte[] leaves =
        [
            .. Leaf(cluster: 0, (-512, -512, -512), (0, 512, 512)),
            .. Leaf(cluster: 1, (0, -512, -512), (512, 512, 512)),
        ];

        return BspLeafTree.FromLumps(node, planes, leaves);
    }

    /// <summary>A frustum at a point looking down a direction, wide enough to see the whole tree.</summary>
    internal static ViewFrustum Looking(
        (float X, float Y, float Z) from, (float X, float Y, float Z) towards) =>
        ViewFrustum.PerspectiveFromAspect(
            from,
            towards,
            right: (towards.Y, -towards.X, 0f),
            up: (0f, 0f, 1f),
            nearZ: 1f,
            farZ: 10_000f,
            fovX: 90f,
            aspect: 1f);

    /// <summary>That both leaves come back when the whole tree is in view.</summary>
    /// <remarks>
    /// **The control for every cull assertion below.** Standing well back on the −x side and looking
    /// along +x puts both halves on screen; if this did not hold, a later "leaf 1 was dropped" would
    /// prove nothing about the frustum.
    /// </remarks>
    [Test]
    public void Leaves_WithTheWholeTreeInView_ReturnsBoth()
    {
        IReadOnlyList<int> visible = new WorldVisibility(Split(), BspVisibility.None)
            .Leaves(-2000f, 0f, 0f, Looking((-2000f, 0f, 0f), (1f, 0f, 0f)));

        visible.Order().ShouldBe([0, 1]);
    }

    /// <summary>That the nearer leaf is listed first, which is what the walk is for.</summary>
    /// <remarks>
    /// **Front-to-back order is the reason the camera's side of each plane is computed at all**, and
    /// a walk that took the children in file order would return the same SET in the wrong sequence.
    /// A set comparison cannot see that, so this asserts the sequence.
    ///
    /// From −2000 the near half is leaf 0; from +2000 it is leaf 1. Both directions, because a walk
    /// that always took one child first would pass one of them.
    /// </remarks>
    [Test]
    public void Leaves_FromEitherSide_ListsTheNearerLeafFirst()
    {
        WorldVisibility visibility = new(Split(), BspVisibility.None);

        visibility.Leaves(-2000f, 0f, 0f, Looking((-2000f, 0f, 0f), (1f, 0f, 0f)))
            .ShouldBe([0, 1]);

        visibility.Leaves(2000f, 0f, 0f, Looking((2000f, 0f, 0f), (-1f, 0f, 0f)))
            .ShouldBe([1, 0]);
    }

    /// <summary>That a leaf behind the camera is dropped by its own cull box.</summary>
    /// <remarks>
    /// Standing at the origin looking down +x: leaf 1 is ahead and leaf 0 is entirely behind. The
    /// node box still straddles the camera, so this is the LEAF test doing the work, not the node
    /// test.
    /// </remarks>
    [Test]
    public void Leaves_WithOneHalfBehindTheCamera_DropsIt()
    {
        IReadOnlyList<int> visible = new WorldVisibility(Split(), BspVisibility.None)
            .Leaves(0f, 0f, 0f, Looking((0f, 0f, 0f), (1f, 0f, 0f)));

        visible.ShouldBe([1]);
    }

    /// <summary>That a node whose box is out of view rejects everything under it.</summary>
    /// <remarks>
    /// **The saving the walk exists for, asserted by its observable effect.** A tree of a root and
    /// two sub-nodes: the far sub-node's box sits entirely behind the camera, so its two leaves are
    /// never examined. Because a leaf-by-leaf scan would reach the same answer, the assertion is on
    /// the answer — what a node rejection buys is time, and what it must not change is the result.
    ///
    /// So this is really a regression guard on the node box being read at the right offset: a
    /// mis-read box that happened to be huge would keep everything, and one that happened to be
    /// tiny would drop the visible half.
    /// </remarks>
    [Test]
    public void Leaves_WithASubtreeOutOfView_KeepsOnlyTheVisibleHalf()
    {
        IReadOnlyList<int> visible =
            new WorldVisibility(Quarters(), BspVisibility.None)
                .Leaves(0f, 0f, 0f, Looking((0f, 0f, 0f), (1f, 0f, 0f)));

        visible.ShouldBe([1, 2], ignoreOrder: false);
    }

    /// <summary>A root at x = 0 over two sub-nodes, four leaves in clusters 0 to 3.</summary>
    /// <param name="cluster0">Leaf 0's cluster, so a case can give it none (−1).</param>
    /// <param name="solidPlusX">Whether leaves 1 and 2, the whole +x half, are solid (contents 1).</param>
    /// <param name="extraLeaves">Leaves appended after the four, which no node reaches.</param>
    /// <param name="plusXNoCluster">Whether leaves 1 and 2 have no cluster (−1), as a compiled solid leaf has.</param>
    /// <remarks>
    /// Node 1 is the +x half (x = 256 splits it into leaf 1 behind, leaf 2 in front); node 2 is the −x half (x = −256
    /// splits it into leaf 3 behind, leaf 0 in front). From x = −400 the walk is leaf 3, node 2, leaf 0, node 0, then
    /// the +x half.
    /// </remarks>
    internal static BspLeafTree Quarters(
        short cluster0 = 0, bool solidPlusX = false, int extraLeaves = 0, bool plusXNoCluster = false)
    {
        int plusX = solidPlusX ? BspLeafTree.ContentsSolid : 0;
        short cluster1 = plusXNoCluster ? (short)-1 : (short)1;
        short cluster2 = plusXNoCluster ? (short)-1 : (short)2;

        byte[] nodes =
        [
            // Root at x = 0: front (+x) is node 1, back (−x) is node 2.
            .. Node(plane: 0, front: 1, back: 2, (-512, -512, -512), (512, 512, 512)),

            // The +x half, split again at x = 256 into leaves 2 and 1.
            .. Node(plane: 1, front: -3, back: -2, (0, -512, -512), (512, 512, 512)),

            // The −x half, split at x = −256 into leaves 0 and 3. Entirely behind the camera.
            .. Node(plane: 2, front: -1, back: -4, (-512, -512, -512), (0, 512, 512)),
        ];

        byte[] planes =
        [
            .. Plane(1f, 0f, 0f, 0f),
            .. Plane(1f, 0f, 0f, 256f),
            .. Plane(1f, 0f, 0f, -256f),
        ];

        byte[] leaves =
        [
            .. Leaf(cluster: cluster0, (-256, -512, -512), (0, 512, 512)),
            .. Leaf(cluster: cluster1, (0, -512, -512), (256, 512, 512), plusX),
            .. Leaf(cluster: cluster2, (256, -512, -512), (512, 512, 512), plusX),
            .. Leaf(cluster: 3, (-512, -512, -512), (-256, 512, 512)),
        ];

        for (int extra = 0; extra < extraLeaves; extra++)
        {
            leaves = [.. leaves, .. Leaf(cluster: 0, (-512, -512, -512), (512, 512, 512))];
        }

        return BspLeafTree.FromLumps(nodes, planes, leaves);
    }

    /// <summary>A PVS lump for up to eight clusters, one row byte each, every byte non-zero.</summary>
    /// <param name="rows">Cluster <c>c</c>'s row: bit <c>k</c> set means it sees cluster <c>k</c>.</param>
    /// <remarks>
    /// <c>dvis_t</c> (<c>bspfile.h:904</c>): a count, then a PVS and a PAS offset per cluster from the lump's start.
    /// The compression only run-lengths ZERO bytes, so a non-zero byte is its own encoding.
    /// </remarks>
    internal static BspVisibility Pvs(params byte[] rows)
    {
        foreach (byte row in rows)
        {
            row.ShouldNotBe((byte)0, "a zero byte would be read as a run");
        }

        return PvsOfRows([.. rows.Select(row => new[] { row })]);
    }

    /// <summary>A PVS lump whose rows are written as given, already compressed — so a case can write a bad one.</summary>
    private static BspVisibility PvsOfRows(params byte[][] rows)
    {
        int table = 4 + (rows.Length * 8);
        List<byte> lump = [.. BitConverter.GetBytes(rows.Length)];
        List<byte> data = [];

        foreach (byte[] row in rows)
        {
            lump.AddRange(BitConverter.GetBytes(table + data.Count));
            lump.AddRange(BitConverter.GetBytes(table + data.Count));
            data.AddRange(row);
        }

        byte[] bytes = [.. lump, .. data];

        return BspVisibility.FromLump(bytes);
    }

    internal static ViewFrustum AheadFromMinus400 => Looking((-400f, 0f, 0f), (1f, 0f, 0f));

    internal static (int Index, bool IsNode)[] Steps(WorldVisibility visibility) =>
        [.. visibility.Walk.Select(step => (step.Index, step.IsNode))];

    /// <summary>That the PVS drops a leaf in view.</summary>
    /// <remarks>
    /// From x = −400 (leaf 3, cluster 3) looking +x, every leaf is in the frustum. Cluster 3 sees 0 and 3 only, so
    /// leaves 1 and 2 go. The control: with no PVS all four leaves listed, so the frustum is not what dropped them.
    /// </remarks>
    [Test]
    public void Leaves_WithAPvs_ListsOnlyThePotentiallyVisibleLeaves()
    {
        new WorldVisibility(Quarters(), BspVisibility.None).Leaves(-400f, 0f, 0f, AheadFromMinus400)
            .ShouldBe([3, 0, 1, 2]);

        new WorldVisibility(Quarters(), Pvs(0x0F, 0x0F, 0x0F, 0b1001)).Leaves(-400f, 0f, 0f, AheadFromMinus400)
            .ShouldBe([3, 0]);
    }

    /// <summary>That moving into another cluster re-marks the tree from that cluster's row.</summary>
    /// <remarks>
    /// The marks are kept while the eye's cluster stays the same, so a stale mark would answer for the old cluster.
    /// From cluster 3 (sees 3, 0) to cluster 2 (sees 2, 1) and back, and the same cluster twice.
    /// </remarks>
    [Test]
    public void Leaves_AfterTheEyeChangesCluster_AnswersForTheNewCluster()
    {
        WorldVisibility visibility = new(Quarters(), Pvs(0x0F, 0x0F, 0b0110, 0b1001));

        visibility.Leaves(-400f, 0f, 0f, default).ShouldBe([3, 0]);
        visibility.Leaves(-300f, 0f, 0f, default).ShouldBe([3, 0]);
        visibility.Leaves(400f, 0f, 0f, default).ShouldBe([2, 1]);
        visibility.Leaves(-400f, 0f, 0f, default).ShouldBe([3, 0]);
    }

    /// <summary>That one instance alternating two eyes walks exactly as two fresh instances do.</summary>
    [Test]
    public void Walk_OneInstanceAlternatingTwoClusters_MatchesAFreshInstanceForEach()
    {
        BspVisibility pvs = Pvs(0x0F, 0x0F, 0b0110, 0b1001);
        WorldVisibility shared = new(Quarters(), pvs);

        foreach (float x in new[] { -400f, 400f, -400f, 400f, 400f })
        {
            WorldVisibility fresh = new(Quarters(), pvs);

            shared.Leaves(x, 0f, 0f, default).ShouldBe(fresh.Leaves(x, 0f, 0f, default), $"from x = {x}");
            Steps(shared).ShouldBe(Steps(fresh), $"from x = {x}");
        }
    }

    /// <summary>That a PVS row that throws mid-marking throws again, rather than leaving half-made stamps in use.</summary>
    /// <remarks>
    /// Cluster 3's row carries a zero repeat count, which <see cref="BspVisibility"/> refuses as Valve's
    /// `DecompressVis` does. Had the cluster been recorded before the stamps were made, the second call would find it
    /// current and walk on stamps the first never finished.
    /// </remarks>
    [Test]
    public void Leaves_WhenAPvsRowThrowsMidMarking_ThrowsAgainOnTheNextCall()
    {
        WorldVisibility visibility = new(Quarters(), PvsOfRows([0x0F], [0x0F], [0x0F], [0x00, 0x00]));

        Should.Throw<System.IO.InvalidDataException>(() => visibility.Leaves(-400f, 0f, 0f, default));
        Should.Throw<System.IO.InvalidDataException>(() => visibility.Leaves(-400f, 0f, 0f, default));
    }

    /// <summary>That a leaf no node reaches — a brush model's — is never listed, though the PVS sees it.</summary>
    [Test]
    public void Leaves_ForALeafNoNodeReaches_NeverListIt()
    {
        new WorldVisibility(Quarters(extraLeaves: 1), Pvs(0x0F, 0x0F, 0x0F, 0x0F)).Leaves(-400f, 0f, 0f, default)
            .ShouldBe([3, 0, 1, 2]);
    }

    /// <summary>That a child index past the leaf lump is never listed.</summary>
    /// <remarks>
    /// A change from the walk this replaced, which listed leaf 9 here, and <see cref="VisibleWorld.Surfaces"/> then
    /// indexed its displacement table out of range. Malformed trees only (B259, 2026-10-27).
    /// </remarks>
    [Test]
    public void Leaves_ForAChildPastTheLeafLump_NeverListIt()
    {
        byte[] node = Node(plane: 0, front: -10, back: -1, (-512, -512, -512), (512, 512, 512));
        byte[] leaves = [.. Leaf(cluster: 0, (-512, -512, -512), (0, 512, 512))];
        BspLeafTree tree = BspLeafTree.FromLumps(node, Plane(1f, 0f, 0f, 0f), leaves);

        new WorldVisibility(tree, BspVisibility.None).Leaves(-2000f, 0f, 0f, default).ShouldBe([0]);
        new WorldVisibility(tree, Pvs(0x01)).Leaves(-2000f, 0f, 0f, default).ShouldBe([0]);
    }

    /// <summary>That a root node whose plane cannot be read leaves the walk at leaf 0, as before.</summary>
    [Test]
    public void Leaves_ForARootWithNoReadablePlane_ListsLeafZero()
    {
        byte[] node = Node(plane: 5, front: -2, back: -1, (-512, -512, -512), (512, 512, 512));
        byte[] leaves = [.. Leaf(cluster: 0, (-512, -512, -512), (0, 512, 512))];
        BspLeafTree tree = BspLeafTree.FromLumps(node, Plane(1f, 0f, 0f, 0f), leaves);

        tree.Node(0).ShouldBeNull("the control: the root's plane is out of range");

        new WorldVisibility(tree, BspVisibility.None).Leaves(0f, 0f, 0f, default).ShouldBe([0]);
        new WorldVisibility(tree, Pvs(0x01)).Leaves(0f, 0f, 0f, default).ShouldBe([0]);
    }

    /// <summary>That a tree deeper than the walk's bound is walked to the bound, with or without a PVS.</summary>
    /// <remarks>
    /// A chain of 300 nodes, each front child the next node and each back child its own leaf; the eye is on every
    /// back side, so it stands in leaf 0 (cluster 0) and each leaf is walked before the node below it. Node <c>d</c>
    /// sits at depth <c>d</c> and its leaf at <c>d + 1</c>, so leaves 0 to 255 are inside the bound of 256. The parent
    /// links stop at the same bound, so a deeper leaf the PVS sees stamps nothing above it.
    /// </remarks>
    [Test]
    public void Leaves_ForATreeDeeperThanTheBound_ListsTheLeavesWithinIt()
    {
        const int Depth = 300;
        List<byte> nodes = [];
        List<byte> leaves = [];

        for (int at = 0; at < Depth; at++)
        {
            int front = at + 1 < Depth ? at + 1 : -(Depth + 1);

            nodes.AddRange(Node(plane: 0, front, back: -(at + 1), (-512, -512, -512), (512, 512, 512)));
            leaves.AddRange(Leaf(cluster: 0, (-512, -512, -512), (512, 512, 512)));
        }

        leaves.AddRange(Leaf(cluster: 0, (-512, -512, -512), (512, 512, 512)));

        BspLeafTree tree = BspLeafTree.FromLumps(nodes.ToArray(), Plane(1f, 0f, 0f, -100_000f), leaves.ToArray());
        int[] expected = [.. Enumerable.Range(0, 256)];

        tree.ClusterAt(-200_000f, 0f, 0f).ShouldBe(0, "the control: the eye is placed, so the PVS applies");
        new WorldVisibility(tree, BspVisibility.None).Leaves(-200_000f, 0f, 0f, default).ShouldBe(expected);
        new WorldVisibility(tree, Pvs(0x01)).Leaves(-200_000f, 0f, 0f, default).ShouldBe(expected);
    }

    /// <summary>That a cycle in a malformed tree ends, and walks the same with or without a PVS.</summary>
    /// <remarks>Node 1's front child is node 0 again; the walk's depth bound is what ends it. The eye is on every back
    /// side, in leaf 0 (cluster 0), so the PVS path is the one marking.</remarks>
    [Test]
    public void Leaves_ForATreeWithACycle_EndsAndMatchesTheUnprunedWalk()
    {
        byte[] nodes =
        [
            .. Node(plane: 0, front: 1, back: -1, (-512, -512, -512), (512, 512, 512)),
            .. Node(plane: 0, front: 0, back: -2, (-512, -512, -512), (512, 512, 512)),
        ];

        byte[] leaves =
        [
            .. Leaf(cluster: 0, (-512, -512, -512), (512, 512, 512)),
            .. Leaf(cluster: 0, (-512, -512, -512), (512, 512, 512)),
        ];

        BspLeafTree tree = BspLeafTree.FromLumps(nodes, Plane(1f, 0f, 0f, -100_000f), leaves);

        WorldVisibility open = new(tree, BspVisibility.None);
        WorldVisibility pruned = new(tree, Pvs(0x01));

        tree.ClusterAt(-200_000f, 0f, 0f).ShouldBe(0, "the control: the eye is placed, so the PVS applies");
        open.Leaves(-200_000f, 0f, 0f, default).Distinct().ShouldBe([0, 1]);
        pruned.Leaves(-200_000f, 0f, 0f, default).ShouldBe(open.Leaves(-200_000f, 0f, 0f, default));
    }

    /// <summary>That an unbuilt frustum applies no frustum test at all.</summary>
    /// <remarks>
    /// The same safe default the cull itself carries: a caller with no camera gets everything rather
    /// than nothing. Standing at the origin, leaf 0 is behind — and with no frustum it survives.
    /// </remarks>
    [Test]
    public void Leaves_WithNoFrustum_KeepsWhatIsBehindTheCamera()
    {
        IReadOnlyList<int> visible = new WorldVisibility(Split(), BspVisibility.None)
            .Leaves(0f, 0f, 0f, default);

        visible.Order().ShouldBe([0, 1]);
    }

    /// <summary>That the returned list is reused, so a caller who keeps it must copy.</summary>
    /// <remarks>
    /// **Documented by a test because the alternative is a caller holding a list that changes under
    /// it.** This is called on every view change; allocating a fresh list each time would cost a few
    /// kilobytes a frame, and the contract that makes reuse safe is only real if something asserts
    /// it.
    /// </remarks>
    [Test]
    public void Leaves_CalledTwice_ReturnsTheSameListRewritten()
    {
        WorldVisibility visibility = new(Split(), BspVisibility.None);

        IReadOnlyList<int> first =
            visibility.Leaves(0f, 0f, 0f, Looking((0f, 0f, 0f), (1f, 0f, 0f)));

        first.ShouldBe([1]);

        IReadOnlyList<int> second =
            visibility.Leaves(0f, 0f, 0f, Looking((0f, 0f, 0f), (-1f, 0f, 0f)));

        second.ShouldBeSameAs(first);
        second.ShouldBe([0]);
    }

    [Test]
    public void Leaves_ForAMapWithNoTree_IsEmpty()
    {
        new WorldVisibility(BspLeafTree.FromLumps(default, default), BspVisibility.None)
            .Leaves(0f, 0f, 0f, default)
            .ShouldBeEmpty();
    }

    [Test]
    public void Constructor_ForANullTree_Throws()
    {
        Should.Throw<ArgumentNullException>(() => new WorldVisibility(null!, BspVisibility.None));
    }

    [Test]
    public void Constructor_ForANullVisibility_Throws()
    {
        Should.Throw<ArgumentNullException>(
            () => new WorldVisibility(BspLeafTree.FromLumps(default, default), null!));
    }

    /// <summary>One `dnode_t`: plane index, two children, a cull box, and no faces.</summary>
    internal static byte[] Node(
        int plane, int front, int back, (short X, short Y, short Z) min, (short X, short Y, short Z) max)
    {
        byte[] node = new byte[32];

        BitConverter.TryWriteBytes(node.AsSpan(0), plane);
        BitConverter.TryWriteBytes(node.AsSpan(4), front);
        BitConverter.TryWriteBytes(node.AsSpan(8), back);
        BitConverter.TryWriteBytes(node.AsSpan(12), min.X);
        BitConverter.TryWriteBytes(node.AsSpan(14), min.Y);
        BitConverter.TryWriteBytes(node.AsSpan(16), min.Z);
        BitConverter.TryWriteBytes(node.AsSpan(18), max.X);
        BitConverter.TryWriteBytes(node.AsSpan(20), max.Y);
        BitConverter.TryWriteBytes(node.AsSpan(22), max.Z);

        return node;
    }

    /// <summary>One `dplane_t`: a normal, a distance, and a type this reader ignores.</summary>
    internal static byte[] Plane(float x, float y, float z, float distance)
    {
        byte[] plane = new byte[20];

        BitConverter.TryWriteBytes(plane.AsSpan(0), x);
        BitConverter.TryWriteBytes(plane.AsSpan(4), y);
        BitConverter.TryWriteBytes(plane.AsSpan(8), z);
        BitConverter.TryWriteBytes(plane.AsSpan(12), distance);

        return plane;
    }

    /// <summary>One version-1 `dleaf_t`: contents, a cluster, a cull box, and no faces.</summary>
    internal static byte[] Leaf(
        short cluster, (short X, short Y, short Z) min, (short X, short Y, short Z) max, int contents = 0)
    {
        byte[] leaf = new byte[32];

        BitConverter.TryWriteBytes(leaf.AsSpan(0), contents);
        BitConverter.TryWriteBytes(leaf.AsSpan(4), cluster);
        BitConverter.TryWriteBytes(leaf.AsSpan(8), min.X);
        BitConverter.TryWriteBytes(leaf.AsSpan(10), min.Y);
        BitConverter.TryWriteBytes(leaf.AsSpan(12), min.Z);
        BitConverter.TryWriteBytes(leaf.AsSpan(14), max.X);
        BitConverter.TryWriteBytes(leaf.AsSpan(16), max.Y);
        BitConverter.TryWriteBytes(leaf.AsSpan(18), max.Z);

        return leaf;
    }
}
