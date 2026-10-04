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
            if (tree.Bounds(leaf) is not { } bounds)
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

        bool[] visible = new bool[tree.LeafCount];

        foreach (int leaf in culling.MainLeaves)
        {
            visible[leaf] = true;
        }

        Dictionary<int, int> expected = [];

        for (int at = 0; at < boxes.Count; at++)
        {
            (float minX, float minY, float minZ, float maxX, float maxY, float maxZ) = boxes[at];

            if (tree.TouchesAny(minX, minY, minZ, maxX, maxY, maxZ, visible))
            {
                expected[handles[at]] = culling.PositionOf(minX, minY, minZ, maxX, maxY, maxZ);
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
    /// <c>EnumerateLeavesInBox_R</c> (<c>utils/common/bsplib.cpp:3486-3494</c>, <c>TEST_EPSILON</c> 1/32 at <c>:3403</c>)
    /// files a box whose face lies ON a plane in the leaves on both sides of it; the old walk filed it behind only.
    /// </summary>
    [Test]
    public void EnumerateLeavesInBox_ForABoxEndingOnANodePlane_IncludesTheLeafJustInFront()
    {
        (MapLevel level, _) = WorldCullingMapTests.Built();
        BspLeafTree tree = level.Leaves.ShouldNotBeNull("cp_process has a tree");

        BspNode split = Enumerable.Range(0, tree.NodeCount)
            .Select(node => tree.Node(node)!.Value)
            .First(node => node.NormalX > 0.9999f && System.MathF.Abs(node.NormalY) + System.MathF.Abs(node.NormalZ) < 1e-6f);

        float y = (split.Min.Y + split.Max.Y) * 0.5f;
        float z = (split.Min.Z + split.Max.Z) * 0.5f;
        (float, float, float) min = (split.Distance - 10f, y - 1f, z - 1f);
        (float, float, float) max = (split.Distance, y + 1f, z + 1f);
        int justInFront = tree.LeafAt(split.Distance + 0.01f, y, z);

        List<int> filed = [];
        tree.EnumerateLeavesInBox(min, max, filed);

        List<int> oldWalk = [];
        tree.LeavesTouchingBox(min, max, oldWalk);

        // The control: the old rule must leave it out, or this cannot tell the two apart.
        oldWalk.ShouldNotContain(justInFront);
        filed.ShouldContain(justInFront);
    }
}
