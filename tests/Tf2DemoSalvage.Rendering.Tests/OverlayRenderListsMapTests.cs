using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// The overlay draws a real map hands the renderer, queued from the view's own walk (B457).
/// </summary>
/// <remarks>
/// **What the synthetic suite cannot reach is the wiring**: that the surfaces the cull walked are the ones the
/// queue reads, that every fragment on a reached face is drawn exactly once, and that the order is a property
/// of the VIEW rather than of the build. Built and culled through the same calls the viewer makes.
/// </remarks>
public sealed class OverlayRenderListsMapTests
{
    [Test]
    public void Order_ForCpProcessFromOneEye_DrawsEveryReachedFragmentOnceAndTurnsWithTheView()
    {
        (MapLevel level, MapWorld world) = WorldCullingMapTests.Built();

        BspLeafTree tree = level.Leaves.ShouldNotBeNull("cp_process has a tree");
        (_, (float X, float Y, float Z) eye) = WorldCullingMapTests.SomewhereInside(tree);

        WorldCulling culling = level.Culling(world.FaceSpans).ShouldNotBeNull("cp_process can be culled");
        OverlayRenderLists queue = new(world.OverlayFragments, world.FaceSpans);

        culling.Batches(eye.X, eye.Y, eye.Z, default);

        IReadOnlyList<int> reached = [.. culling.Surfaces.ShouldNotBeNull("the walk ran")];
        IReadOnlyList<ReachedDisplacement> terrain = [.. culling.Displacements];
        IReadOnlyList<WorldBatch> drawn = [.. queue.Order(reached, terrain, null, _ => false)];

        // Reached and drawn by the opaque passes: not the texinfo-translucent faces, which the translucent pass draws.
        HashSet<int> translucent =
            [.. world.FaceSpans.Where(span => (span.Flags & SurfaceProperties.Translucent) != 0).Select(span => span.Face)];

        HashSet<int> faces =
        [
            .. reached.Where(face => !translucent.Contains(face)),
            .. terrain.Where(reach => reach.InView && !translucent.Contains(reach.Face)).Select(reach => reach.Face),
        ];

        terrain.ShouldNotBeEmpty("cp_process has displacements in view, so the displacement path ran");
        int expected = world.OverlayFragments.Where(fragment => faces.Contains(fragment.Face)).Sum(fragment => fragment.VertexCount);

        // The control: a view of the whole tree from inside the map must reach some overlay.
        expected.ShouldBeGreaterThan(0, "no reached face carries an overlay, so this cannot fail");
        drawn.Sum(batch => batch.VertexCount).ShouldBe(expected);

        HashSet<int> covered = [];

        foreach (WorldBatch batch in drawn)
        {
            for (int at = batch.FirstVertex; at < batch.FirstVertex + batch.VertexCount; at++)
            {
                covered.Add(at).ShouldBeTrue($"vertex {at} drawn twice");
            }
        }

        // Reversing the walk must reorder the draws: the order belongs to the frame, not to the lump.
        IReadOnlyList<WorldBatch> reversed = queue.Order([.. reached.Reverse()], [.. terrain.Reverse()], null, _ => false);

        reversed.Select(batch => batch.FirstVertex).ShouldNotBe(drawn.Select(batch => batch.FirstVertex));
    }
}
