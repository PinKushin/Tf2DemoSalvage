using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// The translucent world runs a real map hands the per-leaf pass, filed from the view's own walk (B261).
/// </summary>
/// <remarks>
/// **What the synthetic suite cannot reach is the wiring**: that the runs the translucent pass draws are exactly the
/// translucent surfaces the walk reached — none it skipped as facing away, none twice — on a real tree, through the
/// same calls the viewer makes. <c>R_DrawSurface</c> (<c>0x1800dfbb0</c>) is the only way onto a translucent chain,
/// so anything the walk did not hand it cannot be drawn by <c>0x1800e4fd0</c>.
/// </remarks>
public sealed class TranslucentLeafRunsMapTests
{
    [Test]
    public void BlendedRuns_ForCpProcessFromOneEye_DrawEveryReachedTranslucentSurfaceOnceAndNothingElse()
    {
        (MapLevel level, MapWorld world) = WorldCullingMapTests.Built();

        BspLeafTree tree = level.Leaves.ShouldNotBeNull("cp_process has a tree");
        (_, (float X, float Y, float Z) eye) = WorldCullingMapTests.SomewhereInside(tree);

        WorldCulling culling = level.Culling(world.FaceSpans).ShouldNotBeNull("cp_process can be culled");
        HashSet<int> materials = Translucent(world);

        culling.Batches(eye.X, eye.Y, eye.Z, default);

        HashSet<int> reached =
        [
            .. culling.Surfaces.ShouldNotBeNull("the walk ran"),
            .. culling.Displacements.Where(reach => reach.InView).Select(reach => reach.Face),
        ];

        TranslucentLeafRuns runs = culling.BlendedRuns(materials.Contains).ShouldNotBeNull();

        List<int> expected =
        [
            .. world.FaceSpans
                .Where(span => reached.Contains(span.Face) && materials.Contains(span.MaterialIndex))
                .SelectMany(span => Enumerable.Range(span.FirstVertex, span.VertexCount)),
        ];

        // The control: a view of the whole tree from inside the map must reach some translucent surface.
        expected.ShouldNotBeEmpty("no reached surface is translucent, so this cannot fail");

        List<int> drawn = [.. runs.Runs.SelectMany(run => Enumerable.Range(run.FirstVertex, run.VertexCount))];

        drawn.Count.ShouldBe(drawn.Distinct().Count(), "a translucent vertex is drawn twice");
        drawn.Order().ShouldBe(expected.Order());
    }

    /// <remarks>
    /// **Which leaf each surface was filed under** (<c>R_DrawSurface</c>, <c>0x1800dfbb0</c>: the world list's newest
    /// entry). A leaf face is listed by the leaf at its place; a node face's place is a leaf on the eye's side of that
    /// node, since the node draws its surfaces after its near child and before its far one (<c>0x1800e0600</c>).
    ///
    /// The eye is chosen, not sampled: in front of the first translucent leaf face whose view files a translucent
    /// surface at place 0 AND a translucent node surface at some place — the two conditions without which dropping the
    /// first leaf, or filing a node's surfaces one leaf late, would pass.
    /// </remarks>
    [Test]
    public void BlendedRuns_ForCpProcessInFrontOfGlass_FileEachSurfaceUnderTheLeafTheWalkWasIn()
    {
        (MapLevel level, MapWorld world) = WorldCullingMapTests.Built();

        BspLeafTree tree = level.Leaves.ShouldNotBeNull("cp_process has a tree");
        BspLeafFaces leafFaces = level.LeafFaces.ShouldNotBeNull("cp_process has leaf faces");
        WorldCulling culling = level.Culling(world.FaceSpans).ShouldNotBeNull("cp_process can be culled");
        HashSet<int> materials = Translucent(world);

        Dictionary<int, WorldFaceSpan> spanAt = [];

        foreach (WorldFaceSpan span in world.FaceSpans)
        {
            for (int at = span.FirstVertex; at < span.FirstVertex + span.VertexCount; at++)
            {
                spanAt[at] = span;
            }
        }

        Dictionary<int, int> nodeOf = [];

        for (int node = 0; node < tree.NodeCount; node++)
        {
            BspNode split = tree.Node(node)!.Value;

            for (int face = split.FirstFace; face < split.FirstFace + split.FaceCount; face++)
            {
                nodeOf[face] = node;
            }
        }

        int tried = 0;

        foreach (WorldFaceSpan glass in world.FaceSpans.Where(span =>
                     !span.OnNode && span.Displacement < 0 && materials.Contains(span.MaterialIndex)))
        {
            float sign = glass.PlaneBack ? -1f : 1f;
            (float X, float Y, float Z) eye = (
                ((glass.Min.X + glass.Max.X) / 2f) + (glass.Plane.X * sign * 8f),
                ((glass.Min.Y + glass.Max.Y) / 2f) + (glass.Plane.Y * sign * 8f),
                ((glass.Min.Z + glass.Max.Z) / 2f) + (glass.Plane.Z * sign * 8f));

            tried++;
            culling.Batches(eye.X, eye.Y, eye.Z, default);

            TranslucentLeafRuns runs = culling.BlendedRuns(materials.Contains).ShouldNotBeNull();
            List<(int Place, WorldFaceSpan Span)> filed = [];

            for (int place = 0; place < runs.LeafCount; place++)
            {
                (int first, int count) = runs.Leaf(place);

                for (int at = first; at < first + count; at++)
                {
                    WorldBatch run = runs.Runs[at];

                    for (int vertex = run.FirstVertex; vertex < run.FirstVertex + run.VertexCount; vertex++)
                    {
                        if (spanAt[vertex].FirstVertex == vertex)
                        {
                            filed.Add((place, spanAt[vertex]));
                        }
                    }
                }
            }

            if (!filed.Any(entry => entry.Place == 0) || !filed.Any(entry => entry.Span.OnNode))
            {
                continue;
            }

            TestContext.Out.WriteLine(
                $"eye {eye} in front of face {glass.Face}, candidate {tried}: {filed.Count} filed, " +
                $"{filed.Count(entry => entry.Place == 0)} at place 0, {filed.Count(entry => entry.Span.OnNode)} on nodes");

            foreach ((int place, WorldFaceSpan span) in filed)
            {
                int leaf = culling.LeafAt(place);

                if (span.Displacement >= 0)
                {
                    continue;
                }

                if (!span.OnNode)
                {
                    Lists(tree, leafFaces, leaf, span.Face).ShouldBeTrue($"face {span.Face} filed under leaf {leaf}, which does not list it");
                    continue;
                }

                BspNode split = tree.Node(nodeOf[span.Face])!.Value;
                float side = (split.NormalX * eye.X) + (split.NormalY * eye.Y) + (split.NormalZ * eye.Z);
                int near = side <= split.Distance ? split.Back : split.Front;

                Under(tree, near, leaf).ShouldBeTrue(
                    $"node face {span.Face} filed under leaf {leaf}, not on the eye's side of node {nodeOf[span.Face]}");
            }

            return;
        }

        Assert.Fail($"no eye in front of {tried} translucent faces files glass at place 0 and on a node, so this cannot fail");
    }

    private static HashSet<int> Translucent(MapWorld world) =>
    [
        .. world.FaceSpans.Where(span => (span.Flags & SurfaceProperties.Translucent) != 0).Select(span => span.MaterialIndex),
    ];

    private static bool Lists(BspLeafTree tree, BspLeafFaces leafFaces, int leaf, int face)
    {
        (int first, int count) = tree.LeafFaces(leaf);

        return Enumerable.Range(first, count).Any(entry => leafFaces.Face(entry) == face);
    }

    /// <summary>Whether a leaf is in the subtree under a child (negative: a leaf, encoded).</summary>
    private static bool Under(BspLeafTree tree, int child, int leaf)
    {
        if (child < 0)
        {
            return -child - 1 == leaf;
        }

        BspNode node = tree.Node(child)!.Value;

        return Under(tree, node.Front, leaf) || Under(tree, node.Back, leaf);
    }
}
