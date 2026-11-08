using System.Collections.Generic;
using System.Linq;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>The water reflection's world list: its own walk, from its mirrored camera, under the main view's PVS (B711).</summary>
/// <remarks>
/// **Read from published source and engine.dll.** `CReflectionView::Draw` runs `DrawSetup` (`viewrender.cpp:6009`), whose
/// `AdjustView` mirrors the camera (`:5282-5284`) and pushes it (`:5419`) before `BuildWorldRenderLists` (`:5427`).
/// `R_BuildWorldLists` (engine.dll `0x1800defb0`) walks the tree with `0x1800e0600` against the stamps of the last
/// `ViewSetupVis`, the main view's (`SetupVis`, `:1415`): no water view calls it. Its force-view leaf and water height go to
/// the area bits alone (`0x180113f00`), which this project does not model.
///
/// **The fixture** (`TwoFaceMap` with its one-way PVS): leaf 1 above the plane z = 0 in cluster 0, which sees only itself,
/// and leaf 2 below it in cluster 1, which sees both. The main eye stands in leaf 1 at z = 10, the mirrored eye in leaf 2
/// at z = −10, as a reflection in water at z = 0 puts it. Marked from the main eye only leaf 1's face is reachable; marked from the mirrored eye, both.
/// </remarks>
public sealed class ReflectionCullConformanceTests
{
    private static readonly (float X, float Y, float Z) MainEye = (0f, 0f, 10f);

    /// <summary>Below the plane, looking along +x at the fixture's boxes, which are all the point at the origin.</summary>
    private static readonly FreeCamera Mirrored = new() { Origin = (-50f, 0f, -10f), Angles = (0f, 0f, 0f), Aspect = 1f };

    [Test]
    public void Batches_FromTheMirroredEyeUnderTheMainEyesVisOrigin_AreTheMainEyesPvsAlone()
    {
        WorldCulling culling = TwoFaceMap.Culling(oneWayPvs: true);

        // The control: marked from the mirrored eye's own cluster, both faces are in the PVS.
        culling.Batches(Mirrored.Origin.X, Mirrored.Origin.Y, Mirrored.Origin.Z, TwoFaceMap.Everything())
            .ShouldNotBeNull().Select(batch => batch.MaterialIndex).ShouldBe([7, 9], ignoreOrder: true);

        culling.BatchesMarkedFrom(Mirrored.Origin.X, Mirrored.Origin.Y, Mirrored.Origin.Z, TwoFaceMap.Everything(), null, MainEye)
            .ShouldNotBeNull().Single().MaterialIndex.ShouldBe(7, "the reflection walked under its own PVS, not the main view's");
    }

    [Test]
    public void Cull_TheReflection_IsTheMainEyesPvsWalkedFromTheMirroredCamera()
    {
        ReflectionCull reflection = new(TwoFaceMap.Culling(oneWayPvs: true));

        ReflectionWorld world = reflection.Cull(Mirrored, MainEye, null, static _ => false, null);

        world.Batches.ShouldNotBeNull().Single().MaterialIndex.ShouldBe(7);
        reflection.Drawn.Surfaces.ShouldBe([0]);
        System.Func<int, bool> leaves = reflection.Drawn.LeafDrawn.ShouldNotBeNull();

        (leaves(1), leaves(2)).ShouldBe((true, false));
    }

    /// <remarks>Its own twin (`MonitorCull`'s reason): the main view's runs survive the reflection's cull, and the other way round.</remarks>
    [Test]
    public void Cull_AfterTheMainView_LeavesEachViewsRunsStanding()
    {
        WorldCulling main = TwoFaceMap.Culling(oneWayPvs: true);
        IReadOnlyList<WorldBatch> mainRuns = main.Batches(Mirrored.Origin.X, Mirrored.Origin.Y, Mirrored.Origin.Z, TwoFaceMap.Everything())
            .ShouldNotBeNull();
        ReflectionCull reflection = new(main);

        IReadOnlyList<WorldBatch> reflected = reflection.Cull(Mirrored, MainEye, null, static _ => false, null).Batches.ShouldNotBeNull();

        mainRuns.Count.ShouldBe(2, "the reflection's cull rewrote the main view's runs");

        (main.Batches(0f, 0f, 0f, ViewFrustum.Nothing)?.Count ?? 0).ShouldBe(0);
        reflected.Count.ShouldBe(1, "the main view's next cull rewrote the reflection's runs");
    }

    [Test]
    public void Cull_WithNoCulling_DrawsEveryBatch() =>
        new ReflectionCull(null).Cull(Mirrored, MainEye, null, static _ => false, null).Batches.ShouldBeNull();

    /// <remarks>
    /// The pruned leaves are bound once per water side (B711): two asks with the same side give the same delegate, whatever
    /// other flags ride along, so a frame allocates none; and that delegate keeps what <c>PruneWorldListInfo</c> keeps — the
    /// list's leaves on that side.
    /// </remarks>
    [Test]
    public void PrunedLeaves_AskedTwiceForOneSide_IsOneDelegateKeepingThatSidesListedLeaves()
    {
        WorldCulling culling = TwoFaceMap.Culling(oneWayPvs: true);

        culling.Batches(MainEye.X, MainEye.Y, MainEye.Z, TwoFaceMap.Everything()).ShouldNotBeNull("the control: the view walked");

        System.Func<int, bool>? above = culling.PrunedLeaves(walked: true, ViewDraws.RenderAboveWater);

        culling.PrunedLeaves(walked: true, ViewDraws.RenderAboveWater | ViewDraws.RenderReflection).ShouldBeSameAs(above);
        culling.PrunedLeaves(walked: true, ViewDraws.RenderUnderWater).ShouldNotBeSameAs(above);
        above.ShouldNotBeNull();

        for (int leaf = 0; leaf < 4; leaf++)
        {
            above(leaf).ShouldBe(culling.IsMainLeaf(leaf) && !culling.LeafInWaterTest(leaf), $"leaf {leaf}");
        }
    }
}
