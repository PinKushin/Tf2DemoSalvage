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

        HashSet<int> materials =
        [
            .. world.FaceSpans.Where(span => (span.Flags & SurfaceProperties.Translucent) != 0).Select(span => span.MaterialIndex),
        ];

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
}
