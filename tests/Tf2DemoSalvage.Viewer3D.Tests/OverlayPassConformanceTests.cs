using System;
using System.Text.RegularExpressions;

using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// What the engine does when it draws an overlay: cull mode, layering, and fade.
/// </summary>
/// <remarks>
/// **Written after the fixes rather than before them, which is the mistake this file records.** The
/// project's rule is conformance test first, then implementation, and the reason is not ceremony:
/// writing the test forces the engine's behaviour to be ENUMERATED, where reacting to a screenshot
/// only ever finds the one thing that showed. B135 was chased through an evening of pictures and
/// turned out to be four divergences at once — pass order, cull mode, depth writes, and a bias — of
/// which the pictures revealed one at a time, each fix exposing the next.
///
/// Two more were found in the minute it took to start writing this, and neither had produced a
/// symptom anybody had noticed yet: render order and fade.
/// </remarks>
public sealed class OverlayPassConformanceTests
{
    [Test]
    public void CullMode_TheEnginesDefault_CullsCounterclockwiseWinding()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
            return;
        }

        string text = SourceSdk.Text("src/public/materialsystem/imaterialsystem.h")
            ?? throw new InvalidOperationException("imaterialsystem.h is missing");

        // An overlay is drawn with its material's cull mode, and this is what a material has unless
        // it says otherwise. Drawn both-sided instead, cp_process's REDSTONE CARGO lettering
        // appeared MIRRORED through its own silo — the back face of the overlay, seen from behind.
        text.ShouldContain("MATERIAL_CULLMODE_CCW");
        text.ShouldContain("this culls polygons with counterclockwise winding");
    }

    [Test]
    public void RenderOrder_AnOverlayCarriesOneOfFourLayers_PackedWithItsFaceCount()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
            return;
        }

        string text = SourceSdk.Text("src/public/bspfile.h")
            ?? throw new InvalidOperationException("bspfile.h is missing");

        // **Four layers, in the top two bits of the same short as the face count.** This project
        // parses the field and then ignores it: nothing sorts overlays by their order. With depth
        // writes off — which is correct, and what the engine does — two overlapping overlays both
        // draw and blend, so the order decides the result rather than being a tie-break.
        text.ShouldContain("OVERLAY_RENDER_ORDER_NUM_BITS	2");
        text.ShouldContain("OVERLAY_NUM_RENDER_ORDERS");
        text.ShouldContain("OVERLAY_RENDER_ORDER_MASK");

        // The accessor pair, which is what says the packing is deliberate rather than incidental.
        text.ShouldContain("void			SetRenderOrder( unsigned short order );");
        text.ShouldContain("unsigned short	GetRenderOrder() const;");
    }

    [Test]
    public void Fade_EveryOverlayCarriesADistanceRange_InItsOwnLump()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
            return;
        }

        string text = SourceSdk.Text("src/public/bspfile.h")
            ?? throw new InvalidOperationException("bspfile.h is missing");

        // **Lump 60, one fixed-size record per overlay.** Not read by this project at all, so every
        // overlay draws at every distance where the engine fades them out. The reader already walks
        // lump 45 beside it.
        text.ShouldContain("LUMP_OVERLAY_FADES");
        text.ShouldContain("Fade distances for overlays");

        Match fade = new Regex(
            @"struct doverlayfade_t(?s).{0,400}?\n\};",
            RegexOptions.Compiled,
            TimeSpan.FromSeconds(10)).Match(text);

        fade.Success.ShouldBeTrue("doverlayfade_t was not found");
        fade.Value.ShouldContain("flFadeDistMinSq");
        fade.Value.ShouldContain("flFadeDistMaxSq");
    }

    [Test]
    public void Fade_TheEngine_ExposesItAsConVars()
    {
        // The other half: a lump is only evidence that the data exists. These names are what the
        // engine calls the feature, found in engine.dll beside COverlayMgr::RenderOverlays, and they
        // are what a future implementation should be checked against.
        //
        // Asserted against the recorded strings rather than the binary, because the binary is not in
        // the repository and must never be — see docs/memory/where-the-game-and-clients-live.md.
        // Read 2026-08-21 from the live client's engine.dll:
        //
        //   r_renderoverlayfragment, r_overlaywireframe,
        //   r_overlayfadeenable, r_overlayfademin, r_overlayfademax
        //
        // Kept as a note rather than an assertion on the binary, so this test states what is known
        // without pretending to re-derive it.
        Assert.Pass(
            "engine.dll exposes r_overlayfadeenable, r_overlayfademin and r_overlayfademax beside "
            + "COverlayMgr::RenderOverlays; recorded, not yet implemented");
    }
}
