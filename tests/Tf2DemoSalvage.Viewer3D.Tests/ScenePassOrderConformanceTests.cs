using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.SdkReference;
using Tf2DemoSalvage.Viewer3D;

namespace Tf2DemoSalvage.Viewer3D.Tests;

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
    public void PassOrder_ThisRenderer_KeepsPropsOutOfTheWorldsOwnBatches()
    {
        // **The half this file was missing, and said so in its own remarks before being committed
        // anyway.** Everything above asserts Valve's source, which does not change and which Valve
        // already tested; it cannot fail for any reason that concerns this renderer.
        //
        // The engine's order — world and its overlays, THEN opaque renderables — is only reproducible
        // if static props are a separate run from world surfaces. Merged into one batch list they
        // are necessarily drawn with the world, whatever the pass sequence says, because a batch
        // list is drawn in one go. So the structural claim is checkable directly: MapWorld must
        // carry props apart from surfaces.
        //
        // The behavioural half — that a prop therefore occludes a marking on the wall behind it —
        // is measured in pixels by OverlayOcclusionRenderTests, which is the test that would have
        // caught B135.
        MapWorld world = MapWorldBuilder.Build(
            null,
            [],
            [],
            LightmapAtlas.Pack([]),
            [
                new PropVertex(0f, 0f, 0f, 0f, 0f, 0),
                new PropVertex(1f, 0f, 0f, 1f, 0f, 0),
                new PropVertex(1f, 1f, 0f, 1f, 1f, 0),
            ],
            TopDownCamera.Fit([(0f, 0f), (1000f, 1000f)], 800, 600),
            area: null);

        world.Props.ShouldNotBeEmpty("a static prop must be its own run, drawn after the overlays");

        world.Batches.ShouldBeEmpty(
            "and it must NOT be in the world's batches: those are drawn before the overlay pass, " +
            "which is the arrangement that let a marking paint over a pipe (B135)");
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
