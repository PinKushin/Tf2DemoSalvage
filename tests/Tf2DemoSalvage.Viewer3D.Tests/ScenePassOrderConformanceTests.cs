using System;
using System.Text.RegularExpressions;

using Tf2DemoSalvage.SdkReference;

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
/// The test pins Valve's order rather than ours, deliberately: it is a statement about the engine
/// that stays true whatever this renderer does, and it reddens if a future SDK snapshot reorders the
/// passes.
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
