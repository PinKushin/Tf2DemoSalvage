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
