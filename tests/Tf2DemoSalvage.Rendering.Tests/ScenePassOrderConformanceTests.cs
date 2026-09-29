using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// The order the engine draws a scene in, pinned — world, then opaque renderables, then translucent.
/// </summary>
/// <remarks>
/// **This should have existed before any of B135 was chased through screenshots.** An evening went
/// into why a pipe drew behind the stripe on the wall behind it, through two reverted bias changes
/// and a depth-format change, when the answer was a pass order that Valve publishes and this project
/// had never compared itself against.
///
/// <c>CBaseWorldView::DrawExecute</c>, <c>game/client/viewrender.cpp:5487</c>:
///
/// <code>
/// DrawWorld( waterZAdjust );
/// DrawOpaqueRenderables( DepthMode );
/// ...
/// DrawTranslucentRenderables( false, false );
/// DrawNoZBufferTranslucentRenderables();
/// </code>
///
/// **`DrawWorld` includes the overlay fragments** — an overlay is part of the world surface it is
/// clipped to, which is why <c>COverlayMgr::RenderOverlays</c> is called from the world list rather
/// than from the renderable list. **`DrawOpaqueRenderables` is where static props, brush models and
/// studio models go**, and it comes AFTER.
///
/// So the engine's order is: world surfaces and their overlays, then everything that stands in front
/// of them, then translucency.
///
/// **This project merges static props into the world vertex buffer**, so they are drawn in the same
/// pass as the surfaces — *before* the overlays rather than after. With any depth bias on the overlay
/// pass, an overlay then wins against a prop that is genuinely nearer, and a pipe an inch off the
/// wall disappears behind the stripe painted on it. That is a divergence in ORDER, and no amount of
/// tuning the bias fixes it.
///
/// **Two of the three tests here assert Valve's source and one asserts ours, and the split is the
/// point.** An SDK checkout does not change and Valve tested that code, so a test of it alone cannot
/// fail for any reason that concerns this renderer — the flaw the owner named after this file was
/// committed saying, in its own remarks, that it could not go red on our side.
///
/// The citations stay because they are the reference an assertion needs. What was added is the
/// assertion: static props must be their own run, which is the structural condition for reproducing
/// the engine's order at all. The behavioural half — a prop actually occluding a marking on the wall
/// behind it — is measured in pixels by <c>OverlayOcclusionRenderTests</c>.
/// </remarks>
public sealed class ScenePassOrderConformanceTests
{
    [Test]
    public void DrawExecute_TheEnginesPassOrder_IsWorldThenOpaqueThenTranslucent()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
            return;
        }

        string text = SourceSdk.Text("src/game/client/viewrender.cpp")
            ?? throw new InvalidOperationException("viewrender.cpp is missing from the SDK");

        Match body = new Regex(
            @"void CBaseWorldView::DrawExecute\([^)]*\)(?s).{0,4000}?\n\}",
            RegexOptions.Compiled,
            TimeSpan.FromSeconds(10)).Match(text);

        body.Success.ShouldBeTrue("CBaseWorldView::DrawExecute was not found");

        int world = body.Value.IndexOf("DrawWorld(", StringComparison.Ordinal);
        int opaque = body.Value.IndexOf("DrawOpaqueRenderables(", StringComparison.Ordinal);
        int translucent =
            body.Value.IndexOf("DrawTranslucentRenderables(", StringComparison.Ordinal);

        world.ShouldBeGreaterThanOrEqualTo(0, "DrawWorld is not called in DrawExecute");
        opaque.ShouldBeGreaterThanOrEqualTo(0, "DrawOpaqueRenderables is not called in DrawExecute");
        translucent.ShouldBeGreaterThanOrEqualTo(0, "DrawTranslucentRenderables is not called");

        // **The world before the things that stand in front of it.** This is the line that matters:
        // static props are opaque renderables, so they are drawn AFTER the world and its overlays.
        world.ShouldBeLessThan(
            opaque,
            "the engine draws the world — overlays included — before opaque renderables");

        opaque.ShouldBeLessThan(
            translucent,
            "the engine draws opaque renderables before translucent ones");
    }


    [Test]
    public void DrawTranslucentRenderables_TheTranslucentWorld_IsDrawnThereNotWithTheWorld()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
            return;
        }

        string text = SourceSdk.Text("src/game/client/viewrender.cpp")
            ?? throw new InvalidOperationException("viewrender.cpp is missing from the SDK");

        // viewrender.cpp:4465 — the translucent world is drawn per leaf inside the translucent pass,
        // before each leaf's translucent entities and again for the leaves left after the loop (B426).
        Match body = new Regex(
            @"void CRendering3dView::DrawTranslucentRenderables\([^)]*\)(?s).{0,12000}?\n\}",
            RegexOptions.Compiled,
            TimeSpan.FromSeconds(10)).Match(text);

        body.Success.ShouldBeTrue("CRendering3dView::DrawTranslucentRenderables was not found");

        int world = body.Value.IndexOf("DrawTranslucentWorldAndDetailPropsInLeaves( iPrevLeaf, iThisLeaf", StringComparison.Ordinal);
        int entity = body.Value.IndexOf("DrawTranslucentRenderable( pRenderable", StringComparison.Ordinal);
        int rest = body.Value.IndexOf("DrawTranslucentWorldAndDetailPropsInLeaves( iPrevLeaf, 0", StringComparison.Ordinal);

        world.ShouldBeGreaterThanOrEqualTo(0, "the per-leaf world draw is missing");
        world.ShouldBeLessThan(entity, "the leaf's translucent world goes before its translucent entities");
        entity.ShouldBeLessThan(rest, "the remaining leaves' translucent world goes after the loop");
    }

    /// <remarks>
    /// **The interleave itself, then ours against it** (B426). <c>DrawTranslucentRenderables</c> starts at the
    /// last leaf (<c>viewrender.cpp:4554</c>), draws down to and including each entity's leaf (<c>:4583</c>, the
    /// <c>&gt;=</c> loop at <c>:4302</c>), steps past it (<c>:4586</c>) and finishes at leaf zero (<c>:4695</c>).
    /// An entity's leaf is the nearest one it touches (<c>ComputeTranslucentRenderLeaf</c>,
    /// <c>clientleafsystem.cpp:1400</c>), and entities are sorted within each leaf only (<c>:1833</c>).
    /// </remarks>
    [Test]
    public void DrawTranslucentRenderables_TheLeafInterleave_IsWhatThePlannerEmits()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
            return;
        }

        string view = SourceSdk.Text("src/game/client/viewrender.cpp")
            ?? throw new InvalidOperationException("viewrender.cpp is missing from the SDK");
        string leaves = SourceSdk.Text("src/game/client/clientleafsystem.cpp")
            ?? throw new InvalidOperationException("clientleafsystem.cpp is missing from the SDK");

        view.ShouldContain("int iPrevLeaf = info.m_LeafCount - 1;");
        view.ShouldContain("for( ; iCurLeafIndex >= iFinalLeafIndex; iCurLeafIndex-- )");
        view.ShouldContain("iPrevLeaf = iThisLeaf - 1;");
        view.ShouldContain("DrawTranslucentWorldAndDetailPropsInLeaves( iPrevLeaf, 0,");
        leaves.ShouldContain("we're gonna choose the leaf that is closest to the camera");
        leaves.ShouldContain("SortEntities( vecRenderOrigin, vecRenderForward, &pTranslucentEntries[nTranslucent], nNewTranslucent );");

        // Ours: leaves 0..2 front to back, one entity in leaf 1. The engine draws leaves 2 and 1, the entity, then 0.
        List<InterleaveStep> steps = [];

        TranslucentInterleave.Plan(3, [1], steps);

        steps.ShouldBe(
        [
            new InterleaveStep(InterleaveKind.World, 2),
            new InterleaveStep(InterleaveKind.World, 1),
            new InterleaveStep(InterleaveKind.Entity, 0),
            new InterleaveStep(InterleaveKind.World, 0),
        ]);
    }

    /// <remarks>
    /// **Detail sprites draw inside the translucent pass, never before the opaque models** (B434).
    /// <c>RenderOpaqueDetailObjects</c> has an empty body marked unimplemented (<c>detailobjectsystem.cpp:1954</c>),
    /// so no sprite is ever drawn opaque. <c>DrawTranslucentWorldAndDetailPropsInLeaves</c> queues each leaf's
    /// sprites after its surfaces (<c>viewrender.cpp:4316-4320</c>) and flushes the queue before the next leaf's
    /// surfaces (<c>:4308-4310</c>); an entity's leaf draws the other queued leaves "up to but not including this
    /// leaf" (<c>:4594-4598</c>), then per entity the sprites farther than it (<c>:4605-4607</c>), then the rest
    /// (<c>:4639</c>); what is queued after the loop goes last (<c>:4697-4698</c>). Within a leaf the sprites are
    /// sorted farthest first (<c>SortLessFunc</c>'s <c>&gt;</c>, <c>:2037</c>) and drawn while
    /// <c>m_flDistance &gt;= flMinDistance</c> (<c>:2708</c>).
    /// </remarks>
    [Test]
    public void DrawTranslucentRenderables_DetailSprites_AreInterleavedPerLeafAroundEachEntity()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
            return;
        }

        string view = SourceSdk.Text("src/game/client/viewrender.cpp")
            ?? throw new InvalidOperationException("viewrender.cpp is missing from the SDK");
        string detail = SourceSdk.Text("src/game/client/detailobjectsystem.cpp")
            ?? throw new InvalidOperationException("detailobjectsystem.cpp is missing from the SDK");

        Regex.IsMatch(
            detail,
            @"RenderOpaqueDetailObjects\( int nLeafCount, LeafIndex_t \*pLeafList \)\s*\{\s*// FIXME: Implement!\s*\}")
            .ShouldBeTrue("RenderOpaqueDetailObjects is no longer empty, so some sprites may draw opaque");
        detail.ShouldContain("return TREATASINT( left.m_flDistance ) > TREATASINT( right.m_flDistance );");
        detail.ShouldContain("while ( m_nFirstSprite < m_nSpriteCount && m_pSortInfo[m_nFirstSprite].m_flDistance >= flMinDistance )");
        view.ShouldContain("DetailObjectSystem()->BeginTranslucentDetailRendering();");
        view.ShouldContain("// Draw detail props up to but not including this leaf");
        view.ShouldContain("// Draw any detail props in this leaf that's farther than the entity");
        view.ShouldContain("DetailObjectSystem()->RenderTranslucentDetailObjectsInLeaf( CurrentViewOrigin(), CurrentViewForward(), CurrentViewRight(), CurrentViewUp(), nLeaf, NULL );");

        // Ours: leaves 0..2, sprites in all three, one entity in leaf 1.
        List<InterleaveStep> steps = [];

        TranslucentInterleave.Plan(3, [1], static _ => true, steps);

        steps.ShouldBe(
        [
            new InterleaveStep(InterleaveKind.World, 2),
            new InterleaveStep(InterleaveKind.Detail, 2),
            new InterleaveStep(InterleaveKind.World, 1),
            new InterleaveStep(InterleaveKind.DetailBeyond, 0),
            new InterleaveStep(InterleaveKind.Entity, 0),
            new InterleaveStep(InterleaveKind.Detail, 1),
            new InterleaveStep(InterleaveKind.World, 0),
            new InterleaveStep(InterleaveKind.Detail, 0),
        ]);
    }

    [Test]
    public void DrawOpaqueRenderables_IsWhereStaticPropsAreDrawn()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
            return;
        }

        string text = SourceSdk.Text("src/game/client/viewrender.cpp")
            ?? throw new InvalidOperationException("viewrender.cpp is missing from the SDK");

        // The claim the test above rests on: that "opaque renderables" really does mean props and
        // brush models, not some narrower category. Without this, "world before opaque" could be
        // true and irrelevant to where a pipe is drawn.
        text.ShouldContain("DrawOpaqueRenderables_DrawStaticProps");
        text.ShouldContain("DrawOpaqueRenderables_DrawBrushModels");
    }
}
