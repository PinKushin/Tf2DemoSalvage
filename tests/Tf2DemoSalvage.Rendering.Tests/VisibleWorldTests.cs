using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// Turning a set of visible leaves into the runs the renderer draws.
/// </summary>
/// <remarks>
/// **Hand-built spans and a hand-built leaf table, because a real map cannot say what the answer
/// should be.** Asking cp_process which runs are correct means trusting the gather to check itself.
/// What a real map contributes is scale, and that has its own test.
///
/// **The interesting behaviours are all about the SEAMS**: two faces of one material that merge into
/// one run, two of different materials that must not, and one face named by two leaves that must be
/// drawn once. Each has a case here and each has a partner that would pass without it.
/// </remarks>
public sealed class VisibleWorldTests
{
    /// <summary>A leaf table where leaf N owns a slice of the flat face list.</summary>
    /// <remarks>
    /// Leaf 0 is the solid leaf every map has and owns nothing, matching a real BSP — so the tests
    /// below number their leaves from one and a reader that lost leaf zero would not be flattered.
    /// </remarks>
    private static BspLeafTree Leaves(params (int First, int Count)[] ranges)
    {
        byte[] leaves = new byte[32 * (ranges.Length + 1)];

        for (int leaf = 0; leaf < ranges.Length; leaf++)
        {
            int at = 32 * (leaf + 1);

            BitConverter.TryWriteBytes(leaves.AsSpan(at + 20), (ushort)ranges[leaf].First);
            BitConverter.TryWriteBytes(leaves.AsSpan(at + 22), (ushort)ranges[leaf].Count);
        }

        return BspLeafTree.FromLumps(new byte[32], new byte[20], leaves);
    }

    private static BspLeafFaces FaceList(params int[] faces)
    {
        byte[] lump = new byte[faces.Length * 2];

        for (int at = 0; at < faces.Length; at++)
        {
            BitConverter.TryWriteBytes(lump.AsSpan(at * 2), (ushort)faces[at]);
        }

        return BspLeafFaces.FromLump(lump);
    }

    /// <summary>Spans laid out end to end in the buffer, three corners each.</summary>
    private static WorldFaceSpan[] Spans(params (int Face, int Material)[] faces)
    {
        WorldFaceSpan[] spans = new WorldFaceSpan[faces.Length];

        for (int at = 0; at < faces.Length; at++)
        {
            spans[at] = new WorldFaceSpan(
                faces[at].Face, at * 3, 3, faces[at].Material, SurfaceCategory.Brush);
        }

        return spans;
    }

    /// <summary>That neighbouring faces of one material become a single run.</summary>
    /// <remarks>
    /// **The whole reason this produces batches rather than one draw per face.** Three faces of
    /// material 5, adjacent in the buffer and all visible, are nine corners from vertex zero — one
    /// bind and one draw, exactly what the uncalled build would have produced.
    /// </remarks>
    [Test]
    public void Batches_ForAdjacentFacesOfOneMaterial_MergeIntoOneRun()
    {
        VisibleWorld world = new(
            Spans((10, 5), (11, 5), (12, 5)),
            Leaves((0, 3)),
            FaceList(10, 11, 12));

        IReadOnlyList<WorldBatch> batches = world.Batches([1], default);

        batches.Count.ShouldBe(1);
        batches[0].MaterialIndex.ShouldBe(5);
        batches[0].FirstVertex.ShouldBe(0);
        batches[0].VertexCount.ShouldBe(9);
    }

    /// <summary>That a different material breaks the run, even when the vertices are adjacent.</summary>
    /// <remarks>
    /// **The partner to the case above, and it is not hypothetical.** Merging on adjacency alone
    /// would draw the second material's triangles with the first material's texture bound — a
    /// wrong picture rather than a slow one.
    /// </remarks>
    [Test]
    public void Batches_WhenTheMaterialChanges_StartsANewRun()
    {
        VisibleWorld world = new(
            Spans((10, 5), (11, 6), (12, 6)),
            Leaves((0, 3)),
            FaceList(10, 11, 12));

        IReadOnlyList<WorldBatch> batches = world.Batches([1], default);

        batches.Select(batch => (batch.MaterialIndex, batch.FirstVertex, batch.VertexCount))
            .ShouldBe([(5, 0, 3), (6, 3, 6)]);
    }

    /// <summary>That a gap left by a culled face breaks the run.</summary>
    /// <remarks>
    /// **What makes the merge correct rather than merely tidy.** Faces 10 and 12 are visible and 11
    /// is not; merging them anyway would draw face 11's triangles, which is drawing something the
    /// cull removed. Two runs of three corners, not one of nine.
    ///
    /// **Face 11 must be named by SOME leaf, and the first version of this test forgot that.** It
    /// gave face 11 to no leaf at all, which used to mean "culled" and now means "orphaned" — a
    /// surface the tree cannot speak for, drawn unless its own box is off screen. That change is
    /// what stopped displacements disappearing, and it turned this test's setup into a description
    /// of a different thing. Leaf 2 owns face 11 and is not visible.
    /// </remarks>
    [Test]
    public void Batches_WithACulledFaceBetweenTwoVisibleOnes_LeavesAGap()
    {
        VisibleWorld world = new(
            Spans((10, 5), (11, 5), (12, 5)),
            Leaves((0, 2), (2, 1)),
            FaceList(10, 12, 11));

        IReadOnlyList<WorldBatch> batches = world.Batches([1], default);

        batches.Select(batch => (batch.FirstVertex, batch.VertexCount))
            .ShouldBe([(0, 3), (6, 3)]);
    }

    /// <summary>That a face no leaf names is drawn when its own box is in view.</summary>
    /// <remarks>
    /// **The displacement case, in miniature, and the defect it was written for.** `EmitLeaf` builds
    /// a leaf's face list from its portals and detail faces; a displacement is neither, so on a real
    /// map not one of them is named by any leaf. Culling by leaf alone therefore deleted the ground
    /// — measured on cp_process as 36,864 corners, a quarter of the world.
    ///
    /// Here face 11 belongs to no leaf and its box sits in front of the camera, so it must be drawn
    /// even though no visible leaf asked for it.
    /// </remarks>
    [Test]
    public void Batches_ForAFaceNoLeafNames_DrawsItWhenItsBoxIsInView()
    {
        WorldFaceSpan[] spans =
        [
            new(10, 0, 3, 5, SurfaceCategory.Brush, (-8f, -8f, -8f), (8f, 8f, 8f)),
            new(11, 3, 3, 5, SurfaceCategory.Terrain, (90f, -8f, -8f), (110f, 8f, 8f)),
        ];

        VisibleWorld world = new(spans, Leaves((0, 1)), FaceList(10));

        world.UnreachableSpans.ShouldBe(1);

        IReadOnlyList<WorldBatch> batches = world.Batches([1], Looking());

        batches.Sum(batch => batch.VertexCount).ShouldBe(6, "both faces should be drawn");
    }

    /// <summary>That a face no leaf names is still dropped when its box is behind the camera.</summary>
    /// <remarks>
    /// **The partner, and without it the case above is satisfied by drawing every orphan always.**
    /// That would be safe and would give up the saving entirely — on a map whose ground is a quarter
    /// of its geometry, that is most of what world culling is worth.
    /// </remarks>
    [Test]
    public void Batches_ForAFaceNoLeafNames_DropsItWhenItsBoxIsBehindTheCamera()
    {
        WorldFaceSpan[] spans =
        [
            new(10, 0, 3, 5, SurfaceCategory.Brush, (-8f, -8f, -8f), (8f, 8f, 8f)),
            new(11, 3, 3, 5, SurfaceCategory.Terrain, (-110f, -8f, -8f), (-90f, 8f, 8f)),
        ];

        VisibleWorld world = new(spans, Leaves((0, 1)), FaceList(10));

        IReadOnlyList<WorldBatch> batches = world.Batches([1], Looking());

        batches.Sum(batch => batch.VertexCount).ShouldBe(3, "only the leaf's own face");
    }

    /// <summary>A camera at the origin looking down +X.</summary>
    private static ViewFrustum Looking() =>
        ViewFrustum.PerspectiveFromAspect(
            origin: (0f, 0f, 0f),
            forward: (1f, 0f, 0f),
            right: (0f, -1f, 0f),
            up: (0f, 0f, 1f),
            nearZ: 7f,
            farZ: 1000f,
            fovX: 90f,
            aspect: 1f);

    /// <summary>That a face named by two leaves is drawn once.</summary>
    /// <remarks>
    /// **The reason the stamp exists.** A wall spanning a doorway is listed by the leaves on both
    /// sides. Without a mark the gather takes it twice, and because the second copy is adjacent to
    /// nothing it becomes a second run over the same vertices — the same triangles submitted twice.
    ///
    /// Asserted as a total corner count as well as a run count, because a duplicate that happened to
    /// merge would keep the run count at one and double the corners.
    /// </remarks>
    [Test]
    public void Batches_ForAFaceInTwoLeaves_DrawsItOnce()
    {
        VisibleWorld world = new(
            Spans((10, 5)),
            Leaves((0, 1), (1, 1)),
            FaceList(10, 10));

        IReadOnlyList<WorldBatch> batches = world.Batches([1, 2], default);

        batches.Count.ShouldBe(1);
        batches[0].VertexCount.ShouldBe(3);
    }

    /// <summary>That the stamp does not leak between calls.</summary>
    /// <remarks>
    /// **A frame number rather than a flag, so nothing is cleared — and this is what proves it.** A
    /// boolean mark left set would make the second call return the first call's faces as well as its
    /// own, which is a cull that stops culling the longer the viewer runs. The two calls below ask
    /// for disjoint leaves and each must answer with only its own.
    /// </remarks>
    [Test]
    public void Batches_OnASecondCall_ForgetsTheFirstCallsFaces()
    {
        VisibleWorld world = new(
            Spans((10, 5), (11, 5)),
            Leaves((0, 1), (1, 1)),
            FaceList(10, 11));

        world.Batches([1], default).Select(batch => batch.FirstVertex).ShouldBe([0]);
        world.Batches([2], default).Select(batch => batch.FirstVertex).ShouldBe([3]);
    }

    /// <summary>That no visible leaves means nothing to draw.</summary>
    [Test]
    public void Batches_ForNoVisibleLeaves_IsEmpty()
    {
        VisibleWorld world = new(Spans((10, 5)), Leaves((0, 1)), FaceList(10));

        world.Batches([], default).ShouldBeEmpty();
    }

    /// <summary>That a leaf naming a face the build dropped is skipped, not indexed out of range.</summary>
    /// <remarks>
    /// **Every map does this.** Tool surfaces, brush-entity faces and anything outside the play area
    /// are dropped by the world build, so their face indices never appear in a span — while the
    /// leaves still name them, because the leaves come from the file. Face 99 is beyond the stamp
    /// array entirely and must simply be ignored.
    /// </remarks>
    [Test]
    public void Batches_WhenALeafNamesADroppedFace_IgnoresIt()
    {
        VisibleWorld world = new(
            Spans((10, 5)),
            Leaves((0, 2)),
            FaceList(99, 10));

        world.Batches([1], default).Count.ShouldBe(1);
    }

    /// <summary>That a map missing any of the three inputs reports it cannot cull.</summary>
    /// <remarks>
    /// **Three separate ways to end up drawing everything, and the caller needs one answer.** The
    /// alternative is a gather that returns an empty list for a map it cannot handle, which is a
    /// black screen wearing the shape of a successful cull.
    /// </remarks>
    [Test]
    public void CanCull_WithoutSpansOrFacesOrLeaves_IsFalse()
    {
        new VisibleWorld([], Leaves((0, 1)), FaceList(10)).CanCull.ShouldBeFalse();
        new VisibleWorld(Spans((10, 5)), Leaves((0, 1)), BspLeafFaces.None).CanCull.ShouldBeFalse();

        new VisibleWorld(Spans((10, 5)), BspLeafTree.FromLumps(default, default), FaceList(10))
            .CanCull.ShouldBeFalse();

        new VisibleWorld(Spans((10, 5)), Leaves((0, 1)), FaceList(10)).CanCull.ShouldBeTrue();
    }

    [Test]
    public void Constructor_ForNullArguments_Throws()
    {
        Should.Throw<ArgumentNullException>(
            () => new VisibleWorld(null!, Leaves((0, 1)), FaceList(10)));

        Should.Throw<ArgumentNullException>(
            () => new VisibleWorld(Spans((10, 5)), null!, FaceList(10)));

        Should.Throw<ArgumentNullException>(
            () => new VisibleWorld(Spans((10, 5)), Leaves((0, 1)), null!));
    }

    [Test]
    public void Batches_ForNullLeaves_Throws()
    {
        VisibleWorld world = new(Spans((10, 5)), Leaves((0, 1)), FaceList(10));

        Should.Throw<ArgumentNullException>(() => world.Batches(null!, default));
    }
}
