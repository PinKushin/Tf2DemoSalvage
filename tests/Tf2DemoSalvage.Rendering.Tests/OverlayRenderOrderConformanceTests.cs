using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// The order the overlay pass draws in: by render order first, by material within it (B138).
/// </summary>
/// <remarks>
/// **Read from engine.dll (x64, live), `COverlayMgr::RenderOverlays` at 0x180110630** — found by its
/// own VProf scope string. The function is one loop over render orders wrapped around the material
/// walk: a pass counter starts at 0, every fragment's overlay is asked for its order (a short at
/// +6), the largest seen is kept, and a fragment is drawn only when its order equals the pass. The
/// loop repeats while the pass is at or below that maximum. So every order-0 overlay on the map is
/// drawn before any order-1 one, whatever its material — and within a pass, fragments go by the
/// material buckets as before.
///
/// The four layers themselves are `OVERLAY_NUM_RENDER_ORDERS`, `bspfile.h:1003`, set by vbsp from
/// the mapper's `RenderOrder` key (`utils/vbsp/overlay.cpp:57`).
/// </remarks>
public sealed class OverlayRenderOrderConformanceTests
{
    [Test]
    public void Build_OverlaysOnTwoLayers_EmitsEveryLayerZeroBatchBeforeLayerOne()
    {
        // Lump order puts the layer-1 overlay FIRST, and gives it the same material as a layer-0
        // one, so grouping by material alone would both draw it first and merge it into the
        // layer-0 batch — the two ways of getting this wrong.
        BspMaterial[] materials =
        [
            new BspMaterial("overlays/stripe_red", (0.5f, 0.5f, 0.5f), 64, 64),
            new BspMaterial("signs/sign069", (0.5f, 0.5f, 0.5f), 64, 64),
        ];

        MapWorld world = MapWorldBuilder.Build(
            null,
            [Floor()],
            materials,
            LightmapAtlas.Pack([]),
            null,
            [Overlay(1, material: 0, order: 1), Overlay(2, material: 1, order: 0), Overlay(3, material: 0, order: 0)]);

        // Layer 0 holds materials 1 then 0 (first seen within the layer), then layer 1 holds 0.
        world.Decals.Select(batch => batch.MaterialIndex).ShouldBe([1, 0, 0]);

        // Six vertices each — one quad as a two-triangle fan — so no batch swallowed another.
        world.Decals.Select(batch => batch.VertexCount).ShouldBe([6, 6, 6]);
    }

    internal static BspSurface Floor() =>
        new(
            0,
            [
                new SurfaceVertex(-256f, -256f, 0f, 0f, 0f, 0f, 0f),
                new SurfaceVertex(-256f, 256f, 0f, 0f, 1f, 0f, 1f),
                new SurfaceVertex(256f, 256f, 0f, 1f, 1f, 1f, 1f),
                new SurfaceVertex(256f, -256f, 0f, 1f, 0f, 1f, 0f),
            ],
            0,
            default,
            (0f, 0f, 1f),
            SurfaceProperties.None,
            -1);

    internal static BspOverlay Overlay(int id, int material, int order) =>
        new(
            Id: id,
            TexInfo: 0,
            MaterialIndex: material,
            RenderOrder: order,
            Faces: [0],
            U: (0f, 1f),
            V: (1f, 0f),
            Corners: [(-16f, -16f), (-16f, 16f), (16f, 16f), (16f, -16f)],
            Origin: (0f, 0f, 0f),
            BasisNormal: (0f, 0f, 1f),
            BasisU: (1f, 0f, 0f),
            BasisV: (0f, 1f, 0f));
}
