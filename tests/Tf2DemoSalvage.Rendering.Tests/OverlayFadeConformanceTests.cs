using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// How the engine fades an overlay by distance, with `r_overlayfadeenable` at its default 0.
/// </summary>
/// <remarks>
/// **Read from engine.dll (x64, live), 0x18010a580** — the fragment-queueing walk called from
/// `COverlayMgr`, the only reader of `r_overlayfadeenable` besides the ConVar-override setup at
/// 0x18010b000. `r_overlayfadeenable` defaults to `"0"` (string at 0x18035de18), and with it 0 each
/// overlay uses its OWN lump-60 distances:
///
/// - `maxSq` not positive — no fade; the fragment is queued untouched.
/// - `distSq` at or past `maxSq` — not queued at all, so not drawn.
/// - `minSq` negative, or `distSq` at or inside `minSq` — alpha 1.
/// - otherwise alpha `= (maxSq - distSq) · k`, clamped to [0, 1], written into every vertex's alpha.
///
/// `distSq` is from the view origin to the overlay's origin. `k` is a per-overlay field the loader
/// fills; the ConVar branch at 0x18010b000 computes its own as `1 / (maxSq - minSq)`, which is what
/// makes alpha reach exactly 1 at `minSq` — *interpolated* for the per-overlay field, which was not
/// read at its write site.
/// </remarks>
public sealed class OverlayFadeConformanceTests
{
    private static readonly OverlayFade Fade = new(0f, 0f, 0f, 250_000f, 1_000_000f);

    [Test]
    public void Alpha_InsideTheMinimum_IsOne() =>
        Fade.Alpha(400f, 0f, 0f).ShouldBe(1f);

    [Test]
    public void Alpha_BetweenMinimumAndMaximum_IsTheLinearRampInSquaredDistance() =>
        Fade.Alpha(750f, 0f, 0f)!.Value.ShouldBe(437_500f / 750_000f, 1e-6f);

    // **The fade reaches the screen only through the VERTEX alpha** (B329). engine.dll 0x180110630 packs each
    // overlay vertex's colour as `round(fade · 255) << 24 | 0xFFFFFF` — white, with the fade in alpha — and
    // 0x1801117c0 initialises that fade to 1. So a material whose shader ignores vertex alpha draws fully
    // opaque right up to the maximum, where 0x18010a580 stops queueing it.
    [Test]
    public void DrawnAlpha_BetweenMinimumAndMaximum_WithoutVertexAlpha_IsOne() =>
        Fade.DrawnAlpha(750f, 0f, 0f, takesVertexAlpha: false).ShouldBe(1f);

    [Test]
    public void DrawnAlpha_BetweenMinimumAndMaximum_WithVertexAlpha_IsTheRampQuantisedToAByte() =>
        Fade.DrawnAlpha(750f, 0f, 0f, takesVertexAlpha: true).ShouldBe(MathF.Round(437_500f / 750_000f * 255f) / 255f);

    [Test]
    public void DrawnAlpha_AtTheMaximum_WithoutVertexAlpha_IsNotDrawn() =>
        Fade.DrawnAlpha(0f, 1000f, 0f, takesVertexAlpha: false).ShouldBeNull();

    [Test]
    public void Alpha_AtTheMaximum_IsNotDrawn() =>
        Fade.Alpha(0f, 1000f, 0f).ShouldBeNull();

    [Test]
    public void Alpha_WithNoMaximum_NeverFades() =>
        new OverlayFade(0f, 0f, 0f, 0f, 0f).Alpha(1e6f, 0f, 0f).ShouldBe(1f);

    [Test]
    public void Alpha_WithANegativeMinimum_IsOneUntilTheMaximum() =>
        new OverlayFade(0f, 0f, 0f, -1f, 1_000_000f).Alpha(999f, 0f, 0f).ShouldBe(1f);

    [Test]
    public void Build_AFadingOverlay_GetsItsOwnBatchCarryingItsFade()
    {
        BspMaterial[] materials = [new BspMaterial("overlays/stripe_red", (0.5f, 0.5f, 0.5f), 64, 64)];

        MapWorld world = MapWorldBuilder.Build(
            null,
            [OverlayRenderOrderConformanceTests.Floor()],
            materials,
            LightmapAtlas.Pack([]),
            null,
            [
                OverlayRenderOrderConformanceTests.Overlay(1, material: 0, order: 0),
                OverlayRenderOrderConformanceTests.Overlay(2, material: 0, order: 0) with
                {
                    Origin = (8f, 0f, 0f),
                    FadeMinSquared = 250_000f,
                    FadeMaxSquared = 1_000_000f,
                },
            ]);

        // Merged with its neighbour, one alpha would fade both; the engine fades per overlay.
        IReadOnlyList<WorldBatch> drawn = OverlayRenderOrderConformanceTests.Drawn(world);

        drawn.Count.ShouldBe(2);
        drawn[0].Fade.ShouldBeNull();
        drawn[1].Fade.ShouldBe(new OverlayFade(8f, 0f, 0f, 250_000f, 1_000_000f));
        drawn.Select(batch => batch.VertexCount).ShouldBe([6, 6]);
    }
}
