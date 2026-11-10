using System.Collections.Generic;
using System.Linq;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// The 3D skybox room is drawn as its own pass, so the cull has to hand back two sets.
/// </summary>
/// <remarks>
/// **<c>CSkyboxView::DrawInternal</c> swaps the area bits before it draws**
/// (<c>viewrender.cpp:4877</c>):
///
/// <code>
///   tmpbits[m_pSky3dParams-&gt;area&gt;&gt;3] |= 1 &lt;&lt; (m_pSky3dParams-&gt;area&amp;7);
///   *areabits = tmpbits;
/// </code>
///
/// so the sky pass draws exactly one area and the main view draws the rest. **Both halves matter
/// and only one of them is obvious**: without the sky pass the miniature room is missing, and
/// without EXCLUDING it from the main pass it is still out there in the world at its literal size,
/// which is what this viewer does today (B152).
///
/// **Measured on the corpus maps before this was built**: `koth_harvest_final` puts its sky camera
/// in area 1, which holds 9 of 2074 leaves; `cp_fulgur` in area 16, holding 18 of 14264. So the
/// area names a small room, which is what makes it the right discriminator — an area holding most
/// of the map would mean filtering by it deletes the level.
/// </remarks>
public sealed class SkyAreaCullingTests
{
    [Test]
    public void Batches_WithNoSkyArea_DrawsEveryLeafInTheMainPass()
    {
        // **The control.** Every map without a sky_camera, and every map before this feature, must
        // behave exactly as it did — one pass holding everything.
        WorldCulling culling = Culled(skyArea: -1);

        culling.Batches(0f, 0f, 0f, Everything()).ShouldNotBeNull().Count.ShouldBe(2);

        culling.SkyRunsFrom(0f, 0f, 0f, Everything()).ShouldBeEmpty(
            "there is no sky room to draw");
    }

    [Test]
    public void Batches_WithASkyArea_LeavesTheSkyRoomOutOfTheMainPass()
    {
        WorldCulling culling = Culled(skyArea: 1);

        culling.Batches(0f, 0f, 0f, Everything()).ShouldNotBeNull().Count.ShouldBe(
            1, "the sky room's leaf is drawn by the sky pass instead");
    }

    /// <remarks>
    /// **Asked from the SKY view's eye, which is the whole reason this is a separate call.**
    /// `CSkyboxView::DrawInternal` runs `ViewSetupVis` at the sky camera's own origin
    /// (<c>viewrender.cpp:4900</c>): the room sits far outside the level and is in nobody's PVS but
    /// its own. Partitioning the PLAYER's visible leaves instead gave `sky area 1 contributes 0
    /// runs` on a real map — correct, and useless.
    /// </remarks>
    [Test]
    public void SkyRunsFrom_TheSkyViewsOwnEye_HoldsTheSkyRoomAlone()
    {
        WorldCulling culling = Culled(skyArea: 1);

        IReadOnlyList<WorldBatch> sky = culling.SkyRunsFrom(0f, 0f, 0f, Everything());

        sky.Count.ShouldBe(1, "one run, for the one face in the sky area");
        sky[0].MaterialIndex.ShouldBe(9, "and it is the SKY's material, not the world's");
    }

    /// <remarks>
    /// **The two sets must not share storage, which a single reused buffer would make them do.**
    /// `VisibleWorld` clears and refills one list per call, so running the sky pass through the same
    /// instance would leave the main pass pointing at the sky's runs — a failure that shows as the
    /// world vanishing and the sky being drawn twice, and one that only appears once both passes
    /// exist. `WorldVisibility` is duplicated for a sharper reason still: it owns `VisibleByLeaf`,
    /// which the ENTITY cull reads, so a sky query through it would tell the entity cull the player
    /// can see whatever the sky room can.
    /// </remarks>
    [Test]
    public void Batches_AfterTheSkyPass_StillHoldsTheMainPassRuns()
    {
        WorldCulling culling = Culled(skyArea: 1);

        IReadOnlyList<WorldBatch> main = culling.Batches(0f, 0f, 0f, Everything()).ShouldNotBeNull();

        _ = culling.SkyRunsFrom(0f, 0f, 0f, Everything());

        main.Count.ShouldBe(1, "the main runs survive the sky pass being built");
        main[0].MaterialIndex.ShouldBe(7, "and they are the WORLD's material, not the sky's");
    }

    /// <remarks>
    /// **The sky view's own world list feeds its shadows** (B702): `CSkyboxView::DrawInternal` hands
    /// `ComputeShadowTextures` the leaves of its own `BuildWorldRenderLists` (`viewrender.cpp:4920-4924`), and its
    /// `DrawWorld` (`:4926`) gives `RenderShadows` its own surfaces. So the sky walk reports face 1 and leaf 2, the sky
    /// room's, and not the main view's face 0 or leaf 1 — and the main view's surface list, built first, is untouched.
    /// </remarks>
    [Test]
    public void SkyRunsFrom_TheSkyViewsWalk_ReportsTheSkyRoomsSurfaceAndLeafAlone()
    {
        WorldCulling culling = Culled(skyArea: 1);

        _ = culling.Batches(0f, 0f, 0f, Everything());
        IReadOnlyList<int> main = culling.Surfaces.ShouldNotBeNull();

        main.ShouldBe([0], "the control: the main view's walk reaches its own face");

        _ = culling.SkyRunsFrom(0f, 0f, 0f, Everything(), null);

        culling.SkySurfaces.ShouldBe([1]);
        (culling.IsSkyLeaf(2), culling.IsSkyLeaf(1)).ShouldBe((true, false));
        main.ShouldBe([0], "the sky walk rewrote the main view's surfaces");
    }

    /// <remarks>
    /// **The sky view's translucent world is its own walk's, by its own leaf places** (B711): `CSkyboxView::DrawInternal`'s
    /// `DrawTranslucentRenderables( true, false )` walks its own world list back to front, each leaf's translucent surfaces
    /// by `m_pActualLeafIndex` (`viewrender.cpp:4932`, `:4547-4701`). Both faces blended: the sky's runs hold face 1 alone,
    /// at its one place; the control, the main view's runs, hold face 0 alone after the sky walk.
    /// </remarks>
    [Test]
    public void SkyBlendedRuns_BothFacesBlended_AreTheSkyRoomsFaceAtItsPlace()
    {
        WorldCulling culling = Culled(skyArea: 1);
        static bool Blended(int material) => material is 7 or 9;

        _ = culling.Batches(0f, 0f, 0f, Everything());
        _ = culling.SkyRunsFrom(0f, 0f, 0f, Everything(), null);

        TranslucentLeafRuns sky = culling.SkyBlendedRuns(Blended).ShouldNotBeNull();

        sky.LeafCount.ShouldBe(1);
        sky.Runs.ShouldHaveSingleItem().MaterialIndex.ShouldBe(9);
        sky.Leaf(0).Count.ShouldBe(1);

        culling.BlendedRuns(Blended).ShouldNotBeNull().Runs.ShouldHaveSingleItem().MaterialIndex.ShouldBe(7, "the main view's runs are its own");
    }

    /// <remarks>The twin is one view half's culling (B581 rung 2): the same map, so the same answers, sky area included.</remarks>
    [Test]
    public void Twin_OfASkyMap_CullsAsTheOriginalDoes()
    {
        WorldCulling twin = Culled(skyArea: 1).Twin();

        twin.SkyArea.ShouldBe(1);
        twin.Batches(0f, 0f, 0f, Everything()).ShouldNotBeNull().Single().MaterialIndex.ShouldBe(7);
        twin.SkyRunsFrom(0f, 0f, 0f, Everything()).Single().MaterialIndex.ShouldBe(9);
    }

    /// <remarks>
    /// **The point of a twin: its culls never rewrite the original's lists** (B581 rung 2). One half's cull for the next
    /// frame runs while the other half's runs are being drawn; through one object it would empty them under the draw.
    /// </remarks>
    [Test]
    public void Twin_CullingNothing_LeavesTheOriginalsRunsStanding()
    {
        WorldCulling culling = Culled(skyArea: -1);
        IReadOnlyList<WorldBatch> main = culling.Batches(0f, 0f, 0f, Everything()).ShouldNotBeNull();
        WorldCulling twin = culling.Twin();

        (twin.Batches(0f, 0f, 0f, ViewFrustum.Nothing)?.Count ?? 0).ShouldBe(0, "the control: this cull, through one object, would empty the list");
        main.Count.ShouldBe(2);
    }

    private static WorldCulling Culled(int skyArea) => TwoFaceMap.Culling(skyArea);

    private static ViewFrustum Everything() => TwoFaceMap.Everything();
}
