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
///   appends the sort ID to the group's list the first time it gets a surface; a TRANSLUCENT surface (flag 0x20, set
///   by <c>0x1800fa3d0</c> from texinfo SURF_TRANS or a translucent material) goes to the other list.
///   <c>0x1800da3a0</c>, called per sort group from <c>0x1800e5e10</c>, walks that list — sort IDs in order, each
///   chain in order — and queues every surface's fragment list.
/// - **Displacements are queued and drawn FIRST, as their own batch.** <c>0x1800e5e10</c> calls
///   <c>Shader_DrawDispChain</c> (<c>0x1800e3a90</c>) before the brush chains: it flattens the displacement sort list
///   (filled by <c>0x1800db7b0</c> the same way) and <c>0x1800c61f0</c> queues each displacement whose box survives
///   the frustum (<c>0x1800c0f90</c>) — a culled one keeps its sort position and queues nothing — then calls
///   <c>RenderOverlays</c> and clears the lists before any brush overlay is queued.
/// - **Queueing PREPENDS twice.** <c>0x18010a580</c> skips a fragment faded past its maximum, prepends the rest
///   to their material's bucket, and prepends the bucket to the group's list the first time it gets a fragment
///   that frame.
/// - **The draw walks both lists from their heads.** <c>COverlayMgr::RenderOverlays</c> (<c>0x180110630</c>)
///   walks the bucket list and each bucket's fragments in list order, once per render order (B138).
///
/// So within a render order, materials draw in the reverse of the order the frame first queued them, and a
/// material's fragments in the reverse of the order they were queued.
/// </remarks>
public sealed class OverlayRenderListsConformanceTests
{
    /// <summary>One span per face, three corners each, in the order given.</summary>
    private static WorldFaceSpan[] Spans(params (int Face, int Material)[] faces) =>
        [.. faces.Select((face, at) => new WorldFaceSpan(face.Face, at * 3, 3, face.Material, SurfaceCategory.Brush))];

    /// <summary>A fragment of six corners at a given place in the overlay vertices.</summary>
    private static OverlayFragment Fragment(int face, int material, int first, OverlayFade? fade = null, int order = 0) =>
        new(face, order, material, first, 6, fade);

    private static IReadOnlyList<int> Materials(IReadOnlyList<WorldBatch> batches) =>
        [.. batches.Select(batch => batch.MaterialIndex)];

    private static bool Opaque(int material) => false;

    [Test]
    public void Order_TwoMaterialsReached_DrawsTheLastReachedFirst()
    {
        OverlayRenderLists queue = new(
            [Fragment(face: 0, material: 20, first: 100), Fragment(face: 1, material: 21, first: 106)],
            Spans((0, 10), (1, 11)));

        Materials(queue.Order([0, 1], [], null, Opaque)).ShouldBe([21, 20]);
        Materials(queue.Order([1, 0], [], null, Opaque)).ShouldBe([20, 21]);
    }

    /// <remarks>
    /// **Each sort group is its own queue and render, groups 3 down to 0** (<c>0x1800e5e10</c>: the counter starts at 3,
    /// <c>0x1800e5f38</c>, and indexes the group table <c>0x18038ea98</c> = {0, 1, 2, 3} from its end, <c>0x1800e5f31</c>
    /// / <c>0x1800e6109</c>; per group the displacement chain <c>0x1800e3a90</c>, the brush chains, <c>0x1800da3a0</c>'s
    /// queue and <c>RenderOverlays</c>). Face 0 is group 2, face 1 group 0, reached 0 then 1: one queue would draw 1
    /// first (the last reached); by group, 0's overlays render in their own batch before group 0's.
    /// </remarks>
    [Test]
    public void Order_SurfacesInTwoSortGroups_RenderTheHigherGroupFirst()
    {
        OverlayRenderLists queue = new(
            [Fragment(face: 0, material: 20, first: 100), Fragment(face: 1, material: 21, first: 106)],
            Spans((0, 10), (1, 11)));

        Materials(queue.Order([0, 1], [], null, Opaque, face => face == 0 ? 2 : 0)).ShouldBe([20, 21]);
        Materials(queue.Order([0, 1], [], null, Opaque)).ShouldBe([21, 20]);
    }

    [Test]
    public void Order_SurfacesOfOneMaterialApart_AreQueuedTogetherByMaterialSort()
    {
        // Faces 0 and 2 share surface material 10, so 0x1800da3a0 queues 0, 2, then 1 — draw 1, 2, 0.
        // Queued in walk order instead, it would draw 2, 1, 0.
        OverlayRenderLists queue = new(
            [
                Fragment(face: 0, material: 20, first: 100),
                Fragment(face: 1, material: 21, first: 106),
                Fragment(face: 2, material: 22, first: 112),
            ],
            Spans((0, 10), (1, 11), (2, 10)));

        Materials(queue.Order([0, 1, 2], [], null, Opaque)).ShouldBe([21, 22, 20]);
    }

    [Test]
    public void Order_TwoOverlaysOnOneSurface_DrawInLumpOrder()
    {
        // Built A then B, listed B then A, queued B then A, drawn A then B.
        OverlayRenderLists queue = new(
            [Fragment(face: 0, material: 20, first: 100), Fragment(face: 0, material: 21, first: 106)],
            Spans((0, 10)));

        Materials(queue.Order([0], [], null, Opaque)).ShouldBe([20, 21]);
    }

    [Test]
    public void Order_AFragmentFadedPastItsMaximum_TakesNoBucketPosition()
    {
        // A would claim material 20's bucket first; unqueued, C claims it after B, so 20 draws first.
        OverlayFade gone = new(100f, 0f, 0f, -1f, 1f);

        OverlayRenderLists queue = new(
            [
                Fragment(face: 0, material: 20, first: 100, gone),
                Fragment(face: 1, material: 21, first: 106),
                Fragment(face: 2, material: 20, first: 112),
            ],
            Spans((0, 10), (1, 11), (2, 12)));

        IReadOnlyList<WorldBatch> drawn = queue.Order([0, 1, 2], [], (0f, 0f, 0f), Opaque);

        drawn.Select(batch => (batch.MaterialIndex, batch.FirstVertex)).ShouldBe([(20, 112), (21, 106)]);
    }

    [Test]
    public void Order_ASurfaceOfATranslucentMaterial_IsNotInTheOpaquePass()
    {
        // Face 0 is translucent: R_DrawSurface puts it on the other list, and 0x1800e4fd0 draws its overlays.
        OverlayRenderLists queue = new(
            [Fragment(face: 0, material: 20, first: 100), Fragment(face: 1, material: 21, first: 106)],
            Spans((0, 10), (1, 11)));

        Materials(queue.Order([0, 1], [], null, material => material == 10)).ShouldBe([21]);
    }

    [Test]
    public void Order_ASurfaceWithTheTranslucentTexinfoFlag_IsNotInTheOpaquePass()
    {
        WorldFaceSpan[] spans = Spans((0, 10), (1, 11));

        spans[0] = spans[0] with { Flags = SurfaceProperties.Translucent };

        OverlayRenderLists queue = new(
            [Fragment(face: 0, material: 20, first: 100), Fragment(face: 1, material: 21, first: 106)],
            spans);

        Materials(queue.Order([0, 1], [], null, Opaque)).ShouldBe([21]);
    }

    [Test]
    public void Order_WithNoWalk_QueuesEveryFaceInBufferOrder()
    {
        OverlayRenderLists queue = new(
            [Fragment(face: 0, material: 20, first: 100), Fragment(face: 1, material: 21, first: 106)],
            Spans((1, 11), (0, 10)));

        Materials(queue.Order(null, null, null, Opaque)).ShouldBe([20, 21]);
    }

    [Test]
    public void Order_AdjacentFragmentsOfOneMaterial_MergeIntoOneDraw()
    {
        // Listed 106 then 100, queued so, drawn 100 then 106: one run of twelve.
        OverlayRenderLists queue = new(
            [Fragment(face: 0, material: 20, first: 100), Fragment(face: 0, material: 20, first: 106)],
            Spans((0, 10)));

        queue.Order([0], [], null, Opaque).Select(batch => (batch.FirstVertex, batch.VertexCount)).ShouldBe([(100, 12)]);
    }

    [Test]
    public void Order_AFragmentOnAFaceTheWorldDoesNotDraw_IsNeverDrawn()
    {
        // A brush entity's face has no world span: R_DrawBrushModel (0x1800df3d0) and its three paths
        // (0x1800db920, 0x1800dd600, 0x1800df720) never reach the overlay manager, so its overlays never draw.
        OverlayRenderLists queue = new(
            [Fragment(face: 0, material: 20, first: 100), Fragment(face: 7, material: 21, first: 106)],
            Spans((0, 10)));

        Materials(queue.Order([0, 7], [], null, Opaque)).ShouldBe([20]);
        Materials(queue.Order(null, null, null, Opaque)).ShouldBe([20]);
    }

    [Test]
    public void Order_DisplacementOverlays_DrawBeforeEveryBrushOverlayWhateverTheirLayer()
    {
        OverlayRenderLists queue = new(
            [Fragment(face: 0, material: 20, first: 100), Fragment(face: 30, material: 40, first: 106, order: 1)],
            Spans((0, 10), (30, 11)));

        Materials(queue.Order([0], [new ReachedDisplacement(30, InView: true)], null, Opaque)).ShouldBe([40, 20]);
    }

    [Test]
    public void Order_ACulledDisplacement_KeepsItsSortPositionAndQueuesNothing()
    {
        // Reached A (sort 10, culled), B (sort 11), C (sort 10): the list is 10:[A, C], 11:[B], so C then B are
        // queued and B's material, queued last, draws first. Dropped before sorting, B would be queued first.
        OverlayRenderLists queue = new(
            [
                Fragment(face: 30, material: 40, first: 100),
                Fragment(face: 31, material: 41, first: 106),
                Fragment(face: 32, material: 42, first: 112),
            ],
            Spans((30, 10), (31, 11), (32, 10)));

        IReadOnlyList<WorldBatch> drawn = queue.Order(
            [],
            [new ReachedDisplacement(30, InView: false), new ReachedDisplacement(31, true), new ReachedDisplacement(32, true)],
            null,
            Opaque);

        Materials(drawn).ShouldBe([41, 42]);
    }

    [Test]
    public void ForTranslucentLeaves_AFaceRunAndALeafsDisplacements_DrawTheirOverlaysAfterThem()
    {
        // Leaf 0: run 0 is face 0 (with overlays), run 1 merged faces, run 2 displacement 30. Leaf 1: nothing.
        // 0x1800e4fd0 draws each translucent surface's overlays straight after it, and the leaf's displacements'
        // overlays together after the displacements (0x1800c61f0).
        OverlayRenderLists queue = new(
            [Fragment(face: 0, material: 20, first: 100), Fragment(face: 30, material: 40, first: 106)],
            Spans((0, 10), (30, 11)));

        TranslucentLeafRuns runs = new(
            [new WorldBatch(10, 0, 3), new WorldBatch(12, 3, 6), new WorldBatch(11, 9, 3)],
            [0, 3, 3],
            [0, -1, 30],
            [false, false, true]);

        IReadOnlyList<IReadOnlyList<WorldBatch>> after = queue.ForTranslucentLeaves(runs, null);

        after.Select(Materials).ShouldBe([[20], [], [40]]);
    }

    [Test]
    public void ForTranslucentLeaves_DisplacementsOfTwoSortGroups_DrawTheirOverlaysAfterEachGroup()
    {
        // 0x1800e4fd0 calls 0x1800c61f0 once per sort group (0x1800e54b6, inside the group loop), so displacements
        // 30 (group 0) and 31 (group 1) in one leaf each get their overlays straight after them, not together.
        OverlayRenderLists queue = new(
            [Fragment(face: 30, material: 40, first: 100), Fragment(face: 31, material: 41, first: 106)],
            Spans((30, 10), (31, 11)));

        TranslucentLeafRuns runs = new(
            [new WorldBatch(10, 0, 3), new WorldBatch(11, 3, 3)],
            [0, 2],
            [30, 31],
            [true, true],
            [0, 1]);

        queue.ForTranslucentLeaves(runs, null).Select(Materials).ShouldBe([[40], [41]]);
    }

    [Test]
    public void Constructor_ForNullFragments_Throws() =>
        Should.Throw<ArgumentNullException>(() => new OverlayRenderLists(null!, []));
}
