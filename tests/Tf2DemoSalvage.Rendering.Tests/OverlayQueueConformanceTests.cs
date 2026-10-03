using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// The order the overlay pass draws in within one render order: queued per frame from the visible surfaces (B457).
/// </summary>
/// <remarks>
/// **Read from engine.dll (x64, live), in disassembly:**
///
/// - **Each surface's fragment list is built by PREPENDING.** <c>0x18010b1c0</c> walks the overlays in lump order
///   and each overlay's faces in its own order; the fragment writer (<c>0x180111b30</c>, at <c>0x1801123a1</c> ..
///   <c>0x180112421</c>) links the new fragment before the surface's list head (surface +0x14) and stores it as the
///   new head. So a surface lists its overlays LAST first.
/// - **The surfaces are visited by material sort, first reached first.** <c>R_DrawSurface</c> (<c>0x1800dfbb0</c>)
///   adds an opaque surface to the sort list with <c>0x1800d1140</c>, which appends it to its sort ID's chain and
///   appends the sort ID to the group's list the first time it gets a surface; a TRANSLUCENT surface (flag 0x20)
///   goes to the other list. <c>0x1800da3a0</c>, called per sort group from <c>0x1800e5e10</c>, walks that list —
///   sort IDs in order, each chain in order — and queues every surface's fragment list.
/// - **Queueing PREPENDS twice.** <c>0x18010a580</c> skips a fragment faded past its maximum, prepends the rest
///   to their material's bucket, and prepends the bucket to the group's list the first time it gets a fragment
///   that frame.
/// - **The draw walks both lists from their heads.** <c>COverlayMgr::RenderOverlays</c> (<c>0x180110630</c>)
///   walks the bucket list and each bucket's fragments in list order, once per render order (B138).
///
/// So within a render order, materials draw in the reverse of the order the frame first queued them, and a
/// material's fragments in the reverse of the order they were queued.
/// </remarks>
public sealed class OverlayQueueConformanceTests
{
    /// <summary>One span per face, three corners each, in the order given.</summary>
    private static WorldFaceSpan[] Spans(params (int Face, int Material)[] faces) =>
        [.. faces.Select((face, at) => new WorldFaceSpan(face.Face, at * 3, 3, face.Material, SurfaceCategory.Brush))];

    /// <summary>A fragment of six corners at a given place in the overlay vertices.</summary>
    private static OverlayFragment Fragment(int face, int material, int first, OverlayFade? fade = null) =>
        new(face, RenderOrder: 0, material, first, 6, fade);

    private static IReadOnlyList<int> Materials(IReadOnlyList<WorldBatch> batches) =>
        [.. batches.Select(batch => batch.MaterialIndex)];

    private static bool Opaque(int material) => false;

    [Test]
    public void Order_TwoMaterialsReached_DrawsTheLastReachedFirst()
    {
        OverlayQueue queue = new(
            [Fragment(face: 0, material: 20, first: 100), Fragment(face: 1, material: 21, first: 106)],
            Spans((0, 10), (1, 11)));

        Materials(queue.Order([0, 1], null, Opaque)).ShouldBe([21, 20]);
        Materials(queue.Order([1, 0], null, Opaque)).ShouldBe([20, 21]);
    }

    [Test]
    public void Order_SurfacesOfOneMaterialApart_AreQueuedTogetherByMaterialSort()
    {
        // Faces 0 and 2 share surface material 10, so 0x1800da3a0 queues 0, 2, then 1 — draw 1, 2, 0.
        // Queued in walk order instead, it would draw 2, 1, 0.
        OverlayQueue queue = new(
            [
                Fragment(face: 0, material: 20, first: 100),
                Fragment(face: 1, material: 21, first: 106),
                Fragment(face: 2, material: 22, first: 112),
            ],
            Spans((0, 10), (1, 11), (2, 10)));

        Materials(queue.Order([0, 1, 2], null, Opaque)).ShouldBe([21, 22, 20]);
    }

    [Test]
    public void Order_TwoOverlaysOnOneSurface_DrawInLumpOrder()
    {
        // Built A then B, listed B then A, queued B then A, drawn A then B.
        OverlayQueue queue = new(
            [Fragment(face: 0, material: 20, first: 100), Fragment(face: 0, material: 21, first: 106)],
            Spans((0, 10)));

        Materials(queue.Order([0], null, Opaque)).ShouldBe([20, 21]);
    }

    [Test]
    public void Order_AFragmentFadedPastItsMaximum_TakesNoBucketPosition()
    {
        // A would claim material 20's bucket first; unqueued, C claims it after B, so 20 draws first.
        OverlayFade gone = new(100f, 0f, 0f, -1f, 1f);

        OverlayQueue queue = new(
            [
                Fragment(face: 0, material: 20, first: 100, gone),
                Fragment(face: 1, material: 21, first: 106),
                Fragment(face: 2, material: 20, first: 112),
            ],
            Spans((0, 10), (1, 11), (2, 12)));

        IReadOnlyList<WorldBatch> drawn = queue.Order([0, 1, 2], (0f, 0f, 0f), Opaque);

        drawn.Select(batch => (batch.MaterialIndex, batch.FirstVertex)).ShouldBe([(20, 112), (21, 106)]);
    }

    [Test]
    public void Order_ASurfaceOfATranslucentMaterial_TakesNoSortPosition()
    {
        // Face 0 is translucent, so R_DrawSurface puts it on the other list and B is queued first.
        OverlayQueue queue = new(
            [Fragment(face: 0, material: 20, first: 100), Fragment(face: 1, material: 21, first: 106)],
            Spans((0, 10), (1, 11)));

        Materials(queue.Order([0, 1], null, material => material == 10)).ShouldBe([20, 21]);
    }

    [Test]
    public void Order_WithNoWalk_QueuesEveryFaceInBufferOrder()
    {
        OverlayQueue queue = new(
            [Fragment(face: 0, material: 20, first: 100), Fragment(face: 1, material: 21, first: 106)],
            Spans((1, 11), (0, 10)));

        Materials(queue.Order(null, null, Opaque)).ShouldBe([20, 21]);
    }

    [Test]
    public void Order_AdjacentFragmentsOfOneMaterial_MergeIntoOneDraw()
    {
        // Listed 106 then 100, queued so, drawn 100 then 106: one run of twelve.
        OverlayQueue queue = new(
            [Fragment(face: 0, material: 20, first: 100), Fragment(face: 0, material: 20, first: 106)],
            Spans((0, 10)));

        queue.Order([0], null, Opaque).Select(batch => (batch.FirstVertex, batch.VertexCount)).ShouldBe([(100, 12)]);
    }

    [Test]
    public void Constructor_ForNullFragments_Throws() =>
        Should.Throw<ArgumentNullException>(() => new OverlayQueue(null!, []));
}
