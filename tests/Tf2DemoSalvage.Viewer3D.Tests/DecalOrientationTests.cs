using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Viewer3D;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// Which way round a decal's texture goes on its quad.
/// </summary>
/// <remarks>
/// **The corner order is not answerable from the SDK.** vbsp copies an overlay's <c>uv0</c>–
/// <c>uv3</c> straight through from the VMF (<c>utils/vbsp/overlay.cpp</c>) and nothing in the
/// released source reads them back — the renderer that consumes them is engine-side and was never
/// published. So the map itself had to answer it, and it can, because an overlay's texture and its
/// quad are the same shape:
///
/// <code>
///   signs/capture_zone      texture 512x128 (4.000)   quad 128x32 along BasisU:BasisV (4.000)
///   signs/sign069           texture 256x512 (0.500)   quad  36x70                     (0.511)
///   signs/factory_label02   texture 256x256 (1.000)   quad  43x43                     (1.007)
///   overlays/floor_stain003 texture 512x512 (1.000)   quad 128x128                    (1.000)
/// </code>
///
/// flU spans the whole texture width and flV its height, so U runs along the corners' first
/// component and V along their second. Transposed, capture_zone would be 0.25 against 4 — wrong by
/// a factor of sixteen, and every other overlay disagrees too.
///
/// Measured on cp_process_f12, and it decided a defect the owner reported three separate times:
/// the CAPTURE ZONE lettering drawn ninety degrees out and squeezed into a narrow column.
/// </remarks>
public sealed class DecalOrientationTests
{
    /// <summary>The real capture_zone overlay: 128 along BasisU, 32 along BasisV.</summary>
    /// <remarks>
    /// Deliberately NOT square. A square quad is the input for which the right answer and the
    /// transposed one predict the same texture coordinates, so it could not fail.
    /// </remarks>
    private static BspOverlay CaptureZone() =>
        new(
            Id: 1,
            TexInfo: 0,
            MaterialIndex: 0,
            RenderOrder: 0,
            Faces: [0],
            U: (0f, 1f),
            V: (1f, 0f),
            Corners: [(-64f, -16f), (-64f, 16f), (64f, 16f), (64f, -16f)],
            Origin: (0f, 0f, 0f),
            BasisNormal: (0f, 0f, 1f),
            BasisU: (1f, 0f, 0f),
            BasisV: (0f, 1f, 0f));

    [Test]
    public void DecalOrientation_AWideQuad_RunsTheTextureAcrossIt()
    {
        // The quad is four times as long along BasisU as along BasisV, and so is the texture. So
        // the two corners that share a BasisU coordinate must share a U coordinate: nothing varies
        // along U between them.
        IReadOnlyList<WorldVertex> quad = DecalQuad();

        (float X, float Y, float Z)[] placed = [.. CaptureZone().WorldCorners];

        float uAtStart = UAt(quad, placed[0]);

        UAt(quad, placed[1]).ShouldBe(
            uAtStart,
            0.0001f,
            "corners 0 and 1 sit at the same place along BasisU, so U cannot change between them");

        // And the far end of the long axis must be the far end of the texture, or the lettering is
        // drawn back to front rather than merely rotated.
        UAt(quad, placed[2]).ShouldBe(1f, 0.0001f);
        UAt(quad, placed[3]).ShouldBe(1f, 0.0001f);
    }

    [Test]
    public void DecalOrientation_ATallQuad_RunsTheTextureDownIt()
    {
        // The control for the test above. Asserting only about U would pass against an
        // implementation that gave every corner the same V, which draws a single row of texels
        // smeared down the quad - which is what the owner was actually looking at.
        IReadOnlyList<WorldVertex> quad = DecalQuad();

        (float X, float Y, float Z)[] placed = [.. CaptureZone().WorldCorners];

        VAt(quad, placed[0]).ShouldBe(1f, 0.0001f);
        VAt(quad, placed[1]).ShouldBe(0f, 0.0001f);
        VAt(quad, placed[2]).ShouldBe(0f, 0.0001f);
        VAt(quad, placed[3]).ShouldBe(1f, 0.0001f);
    }

    /// <summary>Builds the world and hands back the decal's vertices.</summary>
    private static IReadOnlyList<WorldVertex> DecalQuad()
    {
        BspMaterial[] materials = [new BspMaterial("signs/capture_zone", (0.5f, 0.5f, 0.5f), 512, 128)];

        // The floor the decal sits on, flat and facing up, so the overlay finds it.
        List<SurfaceVertex> floor =
        [
            new SurfaceVertex(-256f, -256f, 0f, 0f, 0f, 0f, 0f),
            new SurfaceVertex(-256f, 256f, 0f, 0f, 1f, 0f, 1f),
            new SurfaceVertex(256f, 256f, 0f, 1f, 1f, 1f, 1f),
            new SurfaceVertex(256f, -256f, 0f, 1f, 0f, 1f, 0f),
        ];

        BspSurface surface = new(
            0, floor, 0, default, (0f, 0f, 1f), SurfaceProperties.None, -1);

        MapWorld world = MapWorldBuilder.Build(
            null,
            [surface],
            materials,
            LightmapAtlas.Pack([]),
            [],
            null,
            false,
            [CaptureZone()]);

        world.Decals.Count.ShouldBe(1, "the overlay lies flat on the floor, so it should be placed");

        return world.Vertices;
    }

    private static float UAt(
        IReadOnlyList<WorldVertex> quad, (float X, float Y, float Z) corner) =>
        Nearest(quad, corner).U;

    private static float VAt(
        IReadOnlyList<WorldVertex> quad, (float X, float Y, float Z) corner) =>
        Nearest(quad, corner).V;

    /// <summary>The decal vertex standing at a world corner.</summary>
    /// <remarks>
    /// Found by position rather than by index so the test says nothing about the order the builder
    /// emits triangles in — that is free to change, and the texture coordinates are not.
    /// </remarks>
    private static WorldVertex Nearest(
        IReadOnlyList<WorldVertex> quad, (float X, float Y, float Z) corner) =>
        quad
            .Where(vertex => MathF.Abs(vertex.X - corner.X) < 0.5f &&
                MathF.Abs(vertex.Y - corner.Y) < 0.5f)
            .Select(vertex => (WorldVertex?)vertex)
            .FirstOrDefault()
            ?? throw new InvalidOperationException(
                $"No decal vertex stands at ({corner.X}, {corner.Y}).");
}
