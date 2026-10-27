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
    /// <remarks>
    /// Node 1 is the +x half (x = 256 splits it into leaf 1 behind, leaf 2 in front); node 2 is the −x half (x = −256
    /// splits it into leaf 3 behind, leaf 0 in front). From x = −400 the walk is leaf 3, node 2, leaf 0, node 0, then
    /// the +x half.
    /// </remarks>
    internal static BspLeafTree Quarters(short cluster0 = 0)
    {
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
            .. Leaf(cluster: 1, (0, -512, -512), (256, 512, 512)),
            .. Leaf(cluster: 2, (256, -512, -512), (512, 512, 512)),
            .. Leaf(cluster: 3, (-512, -512, -512), (-256, 512, 512)),
        ];

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
        int table = 4 + (rows.Length * 8);
        byte[] lump = new byte[table + rows.Length];

        BitConverter.TryWriteBytes(lump.AsSpan(0), rows.Length);

        for (int cluster = 0; cluster < rows.Length; cluster++)
        {
            rows[cluster].ShouldNotBe((byte)0, "a zero byte would be read as a run");
            BitConverter.TryWriteBytes(lump.AsSpan(4 + (cluster * 8)), table + cluster);
            BitConverter.TryWriteBytes(lump.AsSpan(8 + (cluster * 8)), table + cluster);
            lump[table + cluster] = rows[cluster];
        }

        return BspVisibility.FromLump(lump);
    }

    /// <summary>That the PVS drops a leaf in view, and keeps one with no cluster.</summary>
    /// <remarks>
    /// From x = −400 (leaf 3, cluster 3) looking +x, every leaf is in the frustum. Cluster 3 sees 0 and 3 only, so
    /// leaves 1 and 2 go. The control: with no PVS all four leaves listed, so the frustum is not what dropped them.
    /// </remarks>
    [Test]
    public void Leaves_WithAPvs_ListsOnlyThePotentiallyVisibleLeaves()
    {
        ViewFrustum ahead = Looking((-400f, 0f, 0f), (1f, 0f, 0f));

        new WorldVisibility(Quarters(), BspVisibility.None).Leaves(-400f, 0f, 0f, ahead)
            .ShouldBe([3, 0, 1, 2]);

        new WorldVisibility(Quarters(), Pvs(0x0F, 0x0F, 0x0F, 0b1001)).Leaves(-400f, 0f, 0f, ahead)
            .ShouldBe([3, 0]);
    }

    /// <summary>That a non-solid leaf with no cluster still keeps its subtree walked.</summary>
    /// <remarks>
    /// Ours, the conservative reading of <c>PVSCheck</c> (<c>vrad.h:372</c>, "we assume the sample is in the PVS"):
    /// a leaf with cluster −1 is listed whatever the eye's row says, so the prune must not take the node above it.
    /// Leaf 0 has no cluster and cluster 3's row sees only itself.
    /// </remarks>
    [Test]
    public void Leaves_ForALeafWithNoCluster_KeepsItAndItsParent()
    {
        WorldVisibility visibility = new(Quarters(cluster0: -1), Pvs(0x0F, 0x0F, 0x0F, 0b1000));

        visibility.Leaves(-400f, 0f, 0f, Looking((-400f, 0f, 0f), (1f, 0f, 0f))).ShouldBe([3, 0]);
    }

    /// <summary>That moving into another cluster re-marks the tree from that cluster's row.</summary>
    /// <remarks>
    /// The marks are kept while the eye's cluster stays the same, so a stale mark would answer for the old cluster.
    /// From cluster 3 (sees 3, 0) to cluster 2 (sees 2, 1) and back.
    /// </remarks>
    [Test]
    public void Leaves_AfterTheEyeChangesCluster_AnswersForTheNewCluster()
    {
        WorldVisibility visibility = new(Quarters(), Pvs(0x0F, 0x0F, 0b0110, 0b1001));

        visibility.Leaves(-400f, 0f, 0f, default).ShouldBe([3, 0]);
        visibility.Leaves(400f, 0f, 0f, default).ShouldBe([2, 1]);
        visibility.Leaves(-400f, 0f, 0f, default).ShouldBe([3, 0]);
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
