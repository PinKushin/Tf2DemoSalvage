using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// The leaf system on a real tree (cp_process), against the per-frame walk it replaced (B262).
/// </summary>
/// <remarks>
/// **A differential, and the two sides are independent routes.** The old walk asked the tree
/// <see cref="BspLeafTree.TouchesAny"/> against the visible-leaf flags and <see cref="WorldCulling.PositionOf"/>
/// for a translucent model's place, every frame; the leaf system links each box into leaves once through
/// <see cref="BspLeafTree.LeavesTouchingBox"/> and collates the view's list. Where the engine's structure agrees
/// with the walk — which renderables are reached, at which leaf — the answers must be equal; the ORDER is the
/// engine's and is asserted on its own terms.
/// </remarks>
public sealed class ClientLeafSystemMapTests
{
    [Test]
    public void BuildRenderablesList_ForCpProcessFromOneEye_ReachesWhatTheOldWalkReachedAtTheSamePlaces()
    {
        (MapLevel level, MapWorld world) = WorldCullingMapTests.Built();

        BspLeafTree tree = level.Leaves.ShouldNotBeNull("cp_process has a tree");
        (_, (float X, float Y, float Z) eye) = WorldCullingMapTests.SomewhereInside(tree);
        WorldCulling culling = level.Culling(world.FaceSpans).ShouldNotBeNull("cp_process can be culled");

        culling.Batches(eye.X, eye.Y, eye.Z, default);

        // A 40-unit box at the middle of every leaf that has bounds, and a long one across each fifth: the long
        // ones straddle leaves, which is the case per-leaf lists exist for.
        List<(float, float, float, float, float, float)> boxes = [];

        for (int leaf = 0; leaf < tree.LeafCount; leaf++)
        {
            // Where an entity can stand: an empty leaf. A box wholly inside solid is filed nowhere by the engine
            // (contents 1 ends the branch, 0x1800dd6a7) and anywhere by a plane-only walk.
            if (tree.Contents(leaf) == BspLeafTree.ContentsSolid || tree.Bounds(leaf) is not { } bounds)
            {
                continue;
            }

            // Off the grid by a fraction of a unit: a face ON a node plane is where the two walks' contact rules
            // differ by design (the old one files it on one side, EnumerateLeavesInBox_R on both), and that is
            // asserted synthetically rather than here.
            // Centres are on the half-unit grid, so +0.3 ± 20.1 puts every face at least 0.1 off it — past the
            // 1/32 contact band.
            float x = ((bounds.Min.X + bounds.Max.X) * 0.5f) + 0.3f;
            float y = ((bounds.Min.Y + bounds.Max.Y) * 0.5f) + 0.3f;
            float z = ((bounds.Min.Z + bounds.Max.Z) * 0.5f) + 0.3f;
            float half = leaf % 5 == 0 ? 300.1f : 20.1f;

            boxes.Add((x - half, y - 20.1f, z - 20.1f, x + half, y + 20.1f, z + 20.1f));
        }

        ClientLeafSystem system = new(tree.LeafCount, tree.EnumerateLeavesInBox);
        int[] handles = [.. boxes.Select(box => system.AddRenderable(box))];
        system.PreRender();

        CollatedRenderables collated = new();
        system.BuildRenderablesList(culling.MainLeaves, default, static _ => LeafRenderGroup.Translucent, collated);

        // **The view lists solid leaves, and the engine's never does** — so the old route's place (PositionOf, which
        // ranks every listed leaf) is compared over the listed leaves an entity can be in. See RISKS B262.
        Dictionary<int, int> placeOf = [];

        for (int place = 0; place < culling.MainLeaves.Count; place++)
        {
            if (tree.Contents(culling.MainLeaves[place]) != BspLeafTree.ContentsSolid)
            {
                placeOf.TryAdd(culling.MainLeaves[place], place);
            }
        }

        Dictionary<int, int> expected = [];
        List<int> touched = [];

        for (int at = 0; at < boxes.Count; at++)
        {
            (float minX, float minY, float minZ, float maxX, float maxY, float maxZ) = boxes[at];

            touched.Clear();
            tree.LeavesTouchingBox((minX, minY, minZ), (maxX, maxY, maxZ), touched);

            // And whose own box the entity's overlaps — the engine tests a leaf's bounds before filing (0x180172540).
            int[] places =
            [
                .. touched
                    .Where(leaf => placeOf.ContainsKey(leaf) && tree.Bounds(leaf) is { } box &&
                        box.Min.X <= maxX && minX <= box.Max.X &&
                        box.Min.Y <= maxY && minY <= box.Max.Y &&
                        box.Min.Z <= maxZ && minZ <= box.Max.Z)
                    .Select(leaf => placeOf[leaf]),
            ];

            if (places.Length > 0)
            {
                expected[handles[at]] = places.Min();
            }
        }

        // The control: from inside the map some box must be reached and some not, or this cannot fail either way.
        expected.Count.ShouldBeGreaterThan(0, "nothing is visible, so this cannot fail");
        expected.Count.ShouldBeLessThan(boxes.Count, "everything is visible, so the leaf list is not being read");

        Dictionary<int, int> reached = collated.Translucent.ToDictionary(entry => entry.Handle, entry => entry.Place);
        string Describe(int handle) => $"{handle} {boxes[System.Array.IndexOf(handles, handle)]} exp {expected.GetValueOrDefault(handle, -9)} got {reached.GetValueOrDefault(handle, -9)}";
        List<string> diff = [.. expected.Keys.Union(reached.Keys).Where(h => expected.GetValueOrDefault(h, -9) != reached.GetValueOrDefault(h, -9)).Select(Describe)];
        diff.ShouldBeEmpty($"{expected.Count} expected, {reached.Count} reached:\n{string.Join('\n', diff.Take(12))}");

        // The engine's order: leaves front to back (`BuildRenderablesList`, clientleafsystem.cpp:1822).
        collated.Translucent.Select(entry => entry.Place).ShouldBeInOrder();
    }

    /// <summary>
    /// engine.dll's <c>CEngineBSPTree::EnumerateLeavesInBox</c> (vtable slot 2 at <c>0x18038e778</c> →
    /// <c>0x1800d96a0</c>, walk <c>0x1800dd690</c>): back alone when <c>far &lt;= dist</c> (<c>0x1800dd80a</c>), front
    /// alone when <c>near &gt;= dist</c> (<c>0x1800dd82d</c>) — NO epsilon, unlike the tools copy in
    /// <c>bsplib.cpp:3403</c>. A box ending exactly on a plane is behind it only.
    /// </summary>
    [Test]
    public void EnumerateLeavesInBox_ForABoxEndingOnANodePlane_ExcludesTheLeafJustInFront()
    {
        (MapLevel level, _) = WorldCullingMapTests.Built();
        BspLeafTree tree = level.Leaves.ShouldNotBeNull("cp_process has a tree");

        // A +X plane with open space on both sides of it at the node's middle: two different empty leaves.
        bool Open(float x, float y, float z) => tree.Contents(tree.LeafAt(x, y, z)) != BspLeafTree.ContentsSolid;

        BspNode split = Enumerable.Range(0, tree.NodeCount)
            .Select(node => tree.Node(node)!.Value)
            .First(node =>
            {
                float y = (node.Min.Y + node.Max.Y) * 0.5f;
                float z = (node.Min.Z + node.Max.Z) * 0.5f;

                return node.NormalX > 0.9999f && System.MathF.Abs(node.NormalY) + System.MathF.Abs(node.NormalZ) < 1e-6f &&
                    Open(node.Distance + 0.01f, y, z) && Open(node.Distance - 5f, y, z) &&
                    tree.LeafAt(node.Distance + 0.01f, y, z) != tree.LeafAt(node.Distance - 5f, y, z);
            });

        float y = (split.Min.Y + split.Max.Y) * 0.5f;
        float z = (split.Min.Z + split.Max.Z) * 0.5f;
        (float, float, float) min = (split.Distance - 10f, y - 1f, z - 1f);
        (float, float, float) max = (split.Distance, y + 1f, z + 1f);
        int justInFront = tree.LeafAt(split.Distance + 0.01f, y, z);

        List<int> filed = [];
        tree.EnumerateLeavesInBox(min, max, filed);

        // The control: a hundredth of a unit further and it is in front too, so the leaf is reachable at all.
        List<int> past = [];
        tree.EnumerateLeavesInBox(min, (split.Distance + 0.01f, y + 1f, z + 1f), past);

        past.ShouldContain(justInFront);
        filed.ShouldNotContain(justInFront);
    }

    /// <summary>
    /// The same walk tests each node's and leaf's own box first (<c>0x1800dd6f0</c> → <c>0x180172540</c>,
    /// <c>|c1 − c2| &gt; e1 + e2</c> rejects) and never enters a leaf whose contents are 1, solid (<c>0x1800dd6a7</c>):
    /// a box outside the world reaches nothing, where a plane-only descent always ends in some leaf.
    /// </summary>
    [Test]
    public void EnumerateLeavesInBox_ForABoxOutsideTheWorld_ReachesNoLeaf()
    {
        (MapLevel level, _) = WorldCullingMapTests.Built();
        BspLeafTree tree = level.Leaves.ShouldNotBeNull("cp_process has a tree");
        (float, float, float) min = (60000f, 60000f, 60000f);
        (float, float, float) max = (60010f, 60010f, 60010f);

        List<int> planesOnly = [];
        tree.LeavesTouchingBox(min, max, planesOnly);

        List<int> filed = [];
        tree.EnumerateLeavesInBox(min, max, filed);

        planesOnly.ShouldNotBeEmpty("the control: a plane-only walk files every box somewhere");
        filed.ShouldBeEmpty();
    }

    /// <summary>
    /// The engine's world walk never reaches a solid leaf: engine.dll <c>0x1800e0600</c> returns at entry when the
    /// node's contents are 1 (<c>0x1800e060f</c>) and stops before a far child that is (<c>0x1800e079c</c>), so
    /// <c>R_DrawLeaf</c> (<c>0x1800df9d0</c>) — which appends the leaf to the world list — is never called for one.
    /// </summary>
    [Test]
    public void Batches_ForCpProcessFromInside_ListsNoSolidLeaf()
    {
        (MapLevel level, MapWorld world) = WorldCullingMapTests.Built();
        BspLeafTree tree = level.Leaves.ShouldNotBeNull("cp_process has a tree");
        (_, (float X, float Y, float Z) eye) = WorldCullingMapTests.SomewhereInside(tree);
        WorldCulling culling = level.Culling(world.FaceSpans).ShouldNotBeNull();

        culling.Batches(eye.X, eye.Y, eye.Z, default);

        culling.MainLeaves.ShouldNotBeEmpty("the control: the walk lists something");
        culling.MainLeaves.ShouldNotContain(leaf => tree.Contents(leaf) == BspLeafTree.ContentsSolid);
    }

    /// <summary>
    /// **An eye in solid sees every leaf** — engine.dll's mark-leaves (<c>0x1801c8cb0</c>) flags any view cluster of −1
    /// (<c>0x1801c8dc1</c>) and then marks EVERY leaf and node visible (<c>0x1801c9113</c>-<c>0x1801c918a</c>), as
    /// <c>r_novis</c> does. The walk then lists every non-solid leaf in the frustum.
    /// </summary>
    [Test]
    public void Batches_ForAnEyeInSolid_ListsEveryNonSolidLeaf()
    {
        (MapLevel level, MapWorld world) = WorldCullingMapTests.Built();
        BspLeafTree tree = level.Leaves.ShouldNotBeNull("cp_process has a tree");
        WorldCulling culling = level.Culling(world.FaceSpans).ShouldNotBeNull();

        // Leaf 0 is the solid leaf every BSP has; its box's middle is inside it.
        (float X, float Y, float Z) inSolid = Enumerable.Range(0, tree.LeafCount)
            .Where(leaf => tree.Contents(leaf) == BspLeafTree.ContentsSolid && tree.Bounds(leaf) is not null)
            .Select(leaf => tree.Bounds(leaf)!.Value)
            .Select(box => ((box.Min.X + box.Max.X) * 0.5f, (box.Min.Y + box.Max.Y) * 0.5f, (box.Min.Z + box.Max.Z) * 0.5f))
            .First(at => tree.Cluster(tree.LeafAt(at.Item1, at.Item2, at.Item3)) < 0);

        culling.Batches(inSolid.X, inSolid.Y, inSolid.Z, default);

        int open = Enumerable.Range(0, tree.LeafCount).Count(leaf => tree.Contents(leaf) != BspLeafTree.ContentsSolid);

        culling.LeafCount.ShouldBe(open);
    }

    /// <summary>A box over the whole map is filed in no solid leaf (<c>CMP dword ptr [RCX],0x1</c>, <c>0x1800dd6a7</c>).</summary>
    [Test]
    public void EnumerateLeavesInBox_ForTheWholeMap_FilesNoSolidLeaf()
    {
        (MapLevel level, _) = WorldCullingMapTests.Built();
        BspLeafTree tree = level.Leaves.ShouldNotBeNull("cp_process has a tree");
        BspNode root = tree.Node(0)!.Value;

        List<int> planesOnly = [];
        tree.LeavesTouchingBox(root.Min, root.Max, planesOnly);

        List<int> filed = [];
        tree.EnumerateLeavesInBox(root.Min, root.Max, filed);

        planesOnly.ShouldContain(leaf => tree.Contents(leaf) == BspLeafTree.ContentsSolid, "the control");
        filed.ShouldNotBeEmpty();
        filed.ShouldNotContain(leaf => tree.Contents(leaf) == BspLeafTree.ContentsSolid);
    }
}
