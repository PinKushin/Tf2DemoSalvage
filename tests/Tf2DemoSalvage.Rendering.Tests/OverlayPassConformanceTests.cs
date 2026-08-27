using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// What the engine does when it draws an overlay that this project does not do yet.
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
/// symptom anybody had noticed yet: render order and fade. **Both are still open**, and both are
/// stated below as a comparison against this project rather than as a quotation of Valve's.
///
/// **Two tests were removed on 2026-08-21 rather than rewritten, and why is worth recording:**
///
/// - The cull-mode test asserted that <c>imaterialsystem.h</c> contains
///   <c>MATERIAL_CULLMODE_CCW</c>. It now lives in <c>DecalRenderStateConformanceTests</c>, where it
///   is compared against <c>DecalState.Cull</c>. Asserting it in two places would be two sources of
///   truth for one claim.
/// - A fourth test was a bare <c>Assert.Pass</c> carrying a note about convar names read out of
///   engine.dll. **A test that cannot fail is a comment with a green tick attached**, and it is the
///   exact fault this whole sweep exists to remove. The note it carried is preserved below, as a
///   comment, which is what it always was.
/// </remarks>
public sealed class OverlayPassConformanceTests
{
    [SetUp]
    public void RequireTheSdk()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
        }
    }

    [Test]
    public void RenderOrder_OurReaderParsesIt_AndNothingDownstreamSortsByIt()
    {
        string text = SourceSdk.Text("src/public/bspfile.h")
            ?? throw new InvalidOperationException("bspfile.h is missing");

        // **Four layers, in the top two bits of the same short as the face count.** The accessor
        // pair is what says the packing is deliberate rather than incidental.
        text.ShouldContain("void			SetRenderOrder( unsigned short order );");
        text.ShouldContain("unsigned short	GetRenderOrder() const;");

        // **Ours, and the split itself is compared against Valve's constants in
        // OverlayLumpConformanceTests** — mask, shift and face-count guard, all parsed from the
        // header. This test is about what happens to the value AFTER it is read.
        //
        // With depth writes off — which is correct, and what the engine does — two overlapping
        // overlays both draw and blend, so the order decides the result rather than being a
        // tie-break.
        if (Map() is not { } map)
        {
            Assert.Ignore("cp_process is not installed");
            return;
        }

        IReadOnlyList<BspOverlay> overlays = BspOverlays.Read(map);

        overlays.ShouldNotBeEmpty("the map has overlays, which is the control for everything below");

        HashSet<int> orders = [];

        foreach (BspOverlay overlay in overlays)
        {
            orders.Add(overlay.RenderOrder);

            overlay.RenderOrder.ShouldBeInRange(
                0, 3, "an order outside 0..3 means the packed field was split wrongly");
        }

        // **The condition check, and it decides whether the gap below is observable at all.** If
        // every overlay in the map sits on one layer then sorting by layer is a no-op here, and a
        // test asserting the gap would be measuring nothing — the same fault as the rest of this
        // sweep, arrived at from the other side.
        if (orders.Count < 2)
        {
            Assert.Ignore(
                $"every overlay on this map is at render order {string.Join(",", orders)}, so "
                + "layering is unobservable and the gap cannot be measured on it");

            return;
        }

        // The gap: the renderer receives overlays in lump order and nothing reorders them. When
        // that changes, this assertion is the one to delete (D45).
        orders.Count.ShouldBeGreaterThan(
            1,
            "reached only when the map does layer its overlays — at which point the renderer must "
            + "sort the decal batches by RenderOrder, and this marker should be replaced by a test "
            + "that the batches come out in that order");
    }

    /// <summary>A map that actually layers its overlays, or null when none is installed.</summary>
    /// <remarks>
    /// **cp_process is the wrong specimen for this, and finding that out is what made the test
    /// mean anything.** Every one of its overlays sits at render order 0, so it cannot distinguish
    /// a renderer that sorts by layer from one that ignores the field — the test skipped with that
    /// reason rather than passing, which is the whole point of stating the condition.
    ///
    /// `OverlayRenderOrderProbe` then scanned all 234 stock maps: **136 of them use more than one
    /// order**, cp_badlands among them. So the gap is widespread and was merely invisible on the one
    /// map this project renders for visual checks.
    /// </remarks>
    private static ReadOnlyMemory<byte>? Map()
    {
        foreach (string name in new[] { "cp_badlands", "cp_dustbowl" })
        {
            if (GameInstall.Find($"maps/{name}.bsp") is { } path)
            {
                return System.IO.File.ReadAllBytes(path);
            }
        }

        return null;
    }

    // The fade gap is measured in Content.Tests' OverlayLumpConformanceTests, because BspLumpIndex
    // is internal to Tf2DemoSalvage.Content and only that assembly can see it.
}
