using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// How much of each decal falls outside the faces it is pinned to.
/// </summary>
/// <remarks>
/// **Decides whether a clipper is needed before one is written.** The engine clips an overlay's
/// quad to the polygons it names, and that code was never released — so the question is not how
/// Valve does it but how much it matters here. A quad that already lies inside its faces needs no
/// clipping; one that spills over a floor's edge and up a wall does.
///
/// Sampled rather than solved: a grid of points across the quad, each tested against the named
/// faces' polygons in their own plane. That answers the question that matters — what share of the
/// decal would be painted where it should not be — without committing to a clipping algorithm
/// first.
/// </remarks>
public sealed class OverlayCoverageProbe
{
    private const int Samples = 12;

    private static string? MapFile => GameInstall.Find("maps/cp_process_final.bsp");

    [Test]
    public void OverlayCoverage_EachDecalOnItsNamedFaces_IsReported()
    {
        if (MapFile is not { } path)
        {
            Assert.Ignore("Team Fortress 2 is not installed; set TF2_FOLDER to run this.");
            return;
        }

        ReadOnlyMemory<byte> map = File.ReadAllBytes(path);

        Dictionary<int, BspSurface> surfaces = [];

        foreach (BspSurface surface in BspSurfaces.Read(map))
        {
            surfaces[surface.FaceIndex] = surface;
        }

        List<double> covered = [];
        int fullyCovered = 0;
        int barelyCovered = 0;

        foreach (BspOverlay overlay in BspOverlays.Read(map))
        {
            IReadOnlyList<(float X, float Y, float Z)> quad = overlay.WorldCorners;

            List<BspSurface> named = [.. overlay.Faces
                .Where(surfaces.ContainsKey)
                .Select(face => surfaces[face])];

            if (named.Count == 0)
            {
                continue;
            }

            int inside = 0;
            int total = 0;

            // A grid across the quad, bilinear between its four corners.
            for (int row = 0; row < Samples; row++)
            {
                for (int column = 0; column < Samples; column++)
                {
                    double u = (column + 0.5) / Samples;
                    double v = (row + 0.5) / Samples;

                    (double x, double y, double z) point = Bilinear(quad, u, v);

                    total++;

                    if (named.Any(surface => Covers(surface, point)))
                    {
                        inside++;
                    }
                }
            }

            double share = inside / (double)total;

            covered.Add(share);

            if (share > 0.999)
            {
                fullyCovered++;
            }

            if (share < 0.5)
            {
                barelyCovered++;
            }
        }

        covered.Sort();

        TestContext.Out.WriteLine(
            $"COVER {covered.Count} overlays measured, median {covered[covered.Count / 2]:P1} of each " +
            $"quad lands on a face it names");
        TestContext.Out.WriteLine(
            $"COVER {fullyCovered} fully covered, {barelyCovered} less than half covered");
        TestContext.Out.WriteLine(
            $"COVER worst {covered[0]:P1}, best {covered[^1]:P1}, mean {covered.Average():P1}");

        // What do the corner numbers actually look like? If they are world-unit offsets from the
        // origin they are tens; if they are texture coordinates they are around one.
        List<double> extents = [];
        List<double> originOffsets = [];

        foreach (BspOverlay overlay in BspOverlays.Read(map))
        {
            extents.Add(overlay.Corners.Max(
                corner => Math.Sqrt((corner.X * corner.X) + (corner.Y * corner.Y))));

            IReadOnlyList<(float X, float Y, float Z)> quad = overlay.WorldCorners;

            (double x, double y, double z) centre = (
                quad.Average(corner => (double)corner.X),
                quad.Average(corner => (double)corner.Y),
                quad.Average(corner => (double)corner.Z));

            originOffsets.Add(Math.Sqrt(
                ((centre.x - overlay.Origin.X) * (centre.x - overlay.Origin.X)) +
                ((centre.y - overlay.Origin.Y) * (centre.y - overlay.Origin.Y)) +
                ((centre.z - overlay.Origin.Z) * (centre.z - overlay.Origin.Z))));
        }

        extents.Sort();
        originOffsets.Sort();

        TestContext.Out.WriteLine(
            $"COVER corner magnitude: min {extents[0]:N2}, median {extents[extents.Count / 2]:N2}, " +
            $"max {extents[^1]:N2}");
        TestContext.Out.WriteLine(
            $"COVER quad centre sits {originOffsets[originOffsets.Count / 2]:N2} units from the origin " +
            $"(median), max {originOffsets[^1]:N2}");
    }

    /// <summary>A point on the quad, bilinear between its corners.</summary>
    private static (double X, double Y, double Z) Bilinear(
        IReadOnlyList<(float X, float Y, float Z)> quad, double u, double v)
    {
        (double x, double y, double z) top = (
            quad[0].X + ((quad[1].X - quad[0].X) * u),
            quad[0].Y + ((quad[1].Y - quad[0].Y) * u),
            quad[0].Z + ((quad[1].Z - quad[0].Z) * u));

        (double x, double y, double z) bottom = (
            quad[3].X + ((quad[2].X - quad[3].X) * u),
            quad[3].Y + ((quad[2].Y - quad[3].Y) * u),
            quad[3].Z + ((quad[2].Z - quad[3].Z) * u));

        return (
            top.x + ((bottom.x - top.x) * v),
            top.y + ((bottom.y - top.y) * v),
            top.z + ((bottom.z - top.z) * v));
    }

    /// <summary>Whether a point lies within a face, projected into the face's own plane.</summary>
    /// <remarks>
    /// The face is convex, so a point is inside when it sits on the same side of every edge — but
    /// **which** side depends on the winding, and that cannot be assumed here. BspSurface.Normal
    /// is corrected for which side of its plane the face is on; the vertex order is not corrected
    /// with it, so half the faces wind the other way relative to that normal.
    ///
    /// Assuming one winding reported 0% coverage on those and 100% on the rest — a bimodal split
    /// of 56 against 162 that reads exactly like a placement defect. Requiring consistency rather
    /// than a particular sign is what the convexity actually gives.
    /// </remarks>
    private static bool Covers(BspSurface surface, (double X, double Y, double Z) point)
    {
        if (surface.Vertices.Count < 3)
        {
            return false;
        }

        bool anyPositive = false;
        bool anyNegative = false;

        for (int index = 0; index < surface.Vertices.Count; index++)
        {
            SurfaceVertex from = surface.Vertices[index];
            SurfaceVertex to = surface.Vertices[(index + 1) % surface.Vertices.Count];

            (double x, double y, double z) edge = (to.X - from.X, to.Y - from.Y, to.Z - from.Z);
            (double x, double y, double z) toPoint =
                (point.X - from.X, point.Y - from.Y, point.Z - from.Z);

            double side =
                (((edge.y * toPoint.z) - (edge.z * toPoint.y)) * surface.Normal.X) +
                (((edge.z * toPoint.x) - (edge.x * toPoint.z)) * surface.Normal.Y) +
                (((edge.x * toPoint.y) - (edge.y * toPoint.x)) * surface.Normal.Z);

            if (side > 0.5)
            {
                anyPositive = true;
            }
            else if (side < -0.5)
            {
                anyNegative = true;
            }

            if (anyPositive && anyNegative)
            {
                return false;
            }
        }

        return true;
    }
}
