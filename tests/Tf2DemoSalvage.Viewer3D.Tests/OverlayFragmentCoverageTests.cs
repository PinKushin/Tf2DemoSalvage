using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// A wall stripe is a band of uniform height that tiles across the faces it crosses.
/// </summary>
/// <remarks>
/// **B134, and the shape of the defect is what these inputs are chosen to expose.** The builder used
/// to clip the overlay's QUAD against each face's edges and then drop the survivor onto the face's
/// plane. Every fragment was therefore bounded by BSP splits rather than by the overlay, so a band
/// that should be one height arrived as trapezoids of differing heights with gaps between them; and
/// on a face not parallel to the overlay, dropping each corner onto the plane moved them by
/// different distances and skewed the piece.
///
/// It now clips the FACE to the volume the overlay projects. Two of the four clip planes are the
/// band's own long edges, so the height is the overlay's V extent on every face it touches — which
/// is the property tested here, and the one a screenshot of the real game shows most plainly.
///
/// **The wall is split in two, which is the condition that made the old code fail.** A single face
/// spanning the whole band cannot tell the two algorithms apart: clip-quad-to-face and
/// clip-face-to-quad agree when the face is larger than the quad and parallel to it. Splitting the
/// wall the way a BSP does, at an arbitrary vertical seam, is what produces the trapezoids — so that
/// is the input.
/// </remarks>
public sealed class OverlayFragmentCoverageTests
{
    /// <summary>Half the band's height; the overlay spans -32..32 about its origin in V.</summary>
    private const float HalfHeight = 32f;

    [Test]
    public void ClipFaceToOverlay_AWallSplitInTwo_GivesBothHalvesTheSameBandHeight()
    {
        BspOverlay overlay = Band();

        // One wall, split at x = 100 into an uneven pair. Both faces are 256 tall; the band is 64.
        BspSurface left = Wall(fromX: 0f, toX: 100f, bottomZ: -128f, topZ: 128f);
        BspSurface right = Wall(fromX: 100f, toX: 400f, bottomZ: -128f, topZ: 128f);

        List<(float X, float Y, float Z)> onLeft =
            MapWorldBuilder.ClipFaceToOverlay(left, overlay, overlay.WorldCorners);

        List<(float X, float Y, float Z)> onRight =
            MapWorldBuilder.ClipFaceToOverlay(right, overlay, overlay.WorldCorners);

        onLeft.Count.ShouldBeGreaterThanOrEqualTo(3, "the band crosses the left half");
        onRight.Count.ShouldBeGreaterThanOrEqualTo(3, "the band crosses the right half");

        // **The band's height, on both halves.** This is the assertion the old algorithm failed:
        // its fragments took the FACE's height where it was narrower than the band and its own
        // trapezoid where the face edge sloped.
        Height(onLeft).ShouldBe(HalfHeight * 2f, 0.01f);
        Height(onRight).ShouldBe(HalfHeight * 2f, 0.01f);

        // **And they meet at the seam rather than leaving a gap.** The old clip needed a unit of
        // slack to hide seams; these share the split plane exactly, so the right-hand fragment
        // starts where the left one ends.
        onLeft.Max(corner => corner.X).ShouldBe(100f, 0.01f);
        onRight.Min(corner => corner.X).ShouldBe(100f, 0.01f);
    }

    [Test]
    public void ClipFaceToOverlay_AFaceOutsideTheBand_IsNotMarked()
    {
        // The control. Without it, an implementation returning the whole face every time would pass
        // every assertion above — the band height would be the face height, and faces would still
        // meet at the seam.
        BspOverlay overlay = Band();

        BspSurface below = Wall(fromX: 0f, toX: 400f, bottomZ: -512f, topZ: -256f);

        MapWorldBuilder.ClipFaceToOverlay(below, overlay, overlay.WorldCorners).ShouldBeEmpty();
    }

    [Test]
    public void ClipFaceToOverlay_EveryCorner_LiesOnTheFaceItMarks()
    {
        // **Why a fragment can no longer hover off a wall.** The old code built its polygon from the
        // overlay's own corners and then pushed them onto the face plane; this one starts from the
        // face's corners and only ever cuts them down, so lying on the surface is a property of the
        // construction rather than of a correction applied afterwards.
        BspOverlay overlay = Band();

        BspSurface wall = Wall(fromX: 0f, toX: 400f, bottomZ: -128f, topZ: 128f);

        List<(float X, float Y, float Z)> fragment =
            MapWorldBuilder.ClipFaceToOverlay(wall, overlay, overlay.WorldCorners);

        fragment.Count.ShouldBeGreaterThanOrEqualTo(3);

        foreach ((float X, float Y, float Z) corner in fragment)
        {
            // The wall lies at y = 0, so its plane is y = 0 and every marked point must be on it.
            corner.Y.ShouldBe(0f, 0.001f);
        }
    }

    /// <summary>A 400-long, 64-tall band on the y = 0 wall, facing -Y.</summary>
    private static BspOverlay Band() =>
        new(
            Id: 1,
            TexInfo: 0,
            MaterialIndex: 0,
            RenderOrder: 0,
            Faces: [0, 1],
            U: (0f, 1f),
            V: (0f, 1f),
            Corners: [(-200f, -HalfHeight), (200f, -HalfHeight), (200f, HalfHeight), (-200f, HalfHeight)],
            Origin: (200f, 0f, 0f),
            BasisNormal: (0f, -1f, 0f),
            BasisU: (1f, 0f, 0f),
            BasisV: (0f, 0f, 1f));

    /// <summary>A rectangle of wall in the y = 0 plane, facing -Y.</summary>
    private static BspSurface Wall(float fromX, float toX, float bottomZ, float topZ) =>
        new(
            FaceIndex: 0,
            Vertices:
            [
                new SurfaceVertex(fromX, 0f, bottomZ, 0f, 0f, 0f, 0f),
                new SurfaceVertex(toX, 0f, bottomZ, 1f, 0f, 1f, 0f),
                new SurfaceVertex(toX, 0f, topZ, 1f, 1f, 1f, 1f),
                new SurfaceVertex(fromX, 0f, topZ, 0f, 1f, 0f, 1f),
            ],
            MaterialIndex: 0,
            Lightmap: default,
            Normal: (0f, -1f, 0f),
            Flags: SurfaceProperties.None,
            DisplacementIndex: -1);

    private static float Height(IReadOnlyList<(float X, float Y, float Z)> polygon) =>
        polygon.Max(corner => corner.Z) - polygon.Min(corner => corner.Z);
}
