using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Bsp;
using Tf2DemoSalvage.Viewer3D;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// Turning BSP faces into filled triangles for the overhead view.
/// </summary>
/// <remarks>
/// **The BSP already holds the polygons; nothing here invents geometry.** A face is a convex
/// polygon in winding order, so filling it needs a triangle fan and no more — the outline drawing
/// that came first was a reduction of this data, not a different source for it.
///
/// What this layer decides is only what a top-down view cannot get from the file: which surface is
/// on top when two overlap, and how bright each one is.
/// </remarks>
public sealed class MapSurfacesTests
{
    private static BspFace Face(float z, params (float X, float Y)[] points) => new(
        [.. points.Select(point => (point.X, point.Y, z))],
        (0f, 0f, 1f),
        SurfaceProperties.None);

    [Test]
    public void APolygonBecomesATriangleFan()
    {
        // Four points is two triangles, six vertices. A quad drawn as one triangle loses half of
        // every floor in the map.
        MapSurfaces surfaces = MapSurfaces.FromFaces(
            [Face(0f, (0f, 0f), (10f, 0f), (10f, 10f), (0f, 10f))]);

        surfaces.Triangles.Count.ShouldBe(6);
    }

    [Test]
    public void ATriangleIsOneTriangle()
    {
        MapSurfaces.FromFaces([Face(0f, (0f, 0f), (10f, 0f), (10f, 10f))]).Triangles.Count.ShouldBe(3);
    }

    [Test]
    public void ADegenerateFaceContributesNothing()
    {
        // Two points cannot be filled. Real maps contain these.
        MapSurfaces.FromFaces([Face(0f, (0f, 0f), (10f, 0f))]).Triangles.ShouldBeEmpty();
    }

    [Test]
    public void TheFanKeepsTheFacesWinding()
    {
        // The fan must be (0,1,2), (0,2,3) - sharing the first vertex and walking the rest. A fan
        // built any other way produces bowties on a four-point face.
        MapSurfaces surfaces = MapSurfaces.FromFaces(
            [Face(0f, (0f, 0f), (10f, 0f), (10f, 10f), (0f, 10f))]);

        List<(float X, float Y)> corners = [.. surfaces.Triangles.Select(t => (t.X, t.Y))];

        corners[0].ShouldBe((0f, 0f));
        corners[1].ShouldBe((10f, 0f));
        corners[2].ShouldBe((10f, 10f));
        corners[3].ShouldBe((0f, 0f));
        corners[4].ShouldBe((10f, 10f));
        corners[5].ShouldBe((0f, 10f));
    }

    [Test]
    public void HigherSurfacesAreDrawnAfterLowerOnes()
    {
        // **The whole reason this is not just "fill the faces".** Seen from above, a roof and the
        // floor beneath it occupy the same pixels, and there is no depth buffer - so draw order IS
        // the depth test. The lower surface must go down first and be covered.
        //
        // The input is deliberately in the wrong order, or the test would pass on a renderer that
        // ignored height entirely.
        MapSurfaces surfaces = MapSurfaces.FromFaces(
        [
            Face(500f, (0f, 0f), (10f, 0f), (10f, 10f)),
            Face(0f, (0f, 0f), (10f, 0f), (10f, 10f)),
        ]);

        surfaces.Triangles[0].Shade.ShouldBeLessThan(surfaces.Triangles[3].Shade);
    }

    [Test]
    public void HeightDecidesShade()
    {
        // A flat grey map reads as a blob. Shading by height is what makes a roof legible as
        // something above the floor rather than a shape drawn on it.
        MapSurfaces surfaces = MapSurfaces.FromFaces(
        [
            Face(0f, (0f, 0f), (10f, 0f), (10f, 10f)),
            Face(1000f, (20f, 0f), (30f, 0f), (30f, 10f)),
        ]);

        float low = surfaces.Triangles[0].Shade;
        float high = surfaces.Triangles[3].Shade;

        low.ShouldBeLessThan(high);
        low.ShouldBeGreaterThan(0f);
        high.ShouldBeLessThanOrEqualTo(1f);
    }

    [Test]
    public void AFlatMapIsNotBlack()
    {
        // The degenerate case of shading by height: every face at one height gives a zero range,
        // and dividing by it would make the whole map the darkest possible shade - or NaN.
        MapSurfaces surfaces = MapSurfaces.FromFaces(
        [
            Face(64f, (0f, 0f), (10f, 0f), (10f, 10f)),
            Face(64f, (20f, 0f), (30f, 0f), (30f, 10f)),
        ]);

        foreach (MapTriangle triangle in surfaces.Triangles)
        {
            triangle.Shade.ShouldBeGreaterThan(0.2f);
            float.IsNaN(triangle.Shade).ShouldBeFalse();
        }
    }

    [Test]
    public void FacesOutsideTheAreaAreNotDrawn()
    {
        // The 3D skybox room again. Drawing it is harmless - it falls outside the view - but
        // letting it into the HEIGHT RANGE is not: it sits at a Z far from the map's own, so every
        // real surface compresses into one narrow band of shade and the fill reads as a flat
        // silhouette. Measured on cp_process_final before this filter existed.
        MapSurfaces surfaces = MapSurfaces.FromFaces(
            [
                Face(0f, (0f, 0f), (100f, 0f), (100f, 100f)),
                Face(200f, (0f, 0f), (100f, 0f), (100f, 100f)),
                Face(90000f, (50000f, 50000f), (50100f, 50000f), (50100f, 50100f)),
            ],
            new MapBounds(-1000f, -1000f, 1000f, 1000f));

        // Two triangles, not three.
        surfaces.Triangles.Count.ShouldBe(6);

        // And the surviving pair span the full shade range, rather than sharing the bottom of a
        // range stretched to 90,000.
        surfaces.Triangles[0].Shade.ShouldBeLessThan(0.35f);
        surfaces.Triangles[3].Shade.ShouldBeGreaterThan(0.9f);
    }

    [Test]
    public void AFacePartlyInsideTheAreaIsKept()
    {
        // Judged on any corner, not all of them. A face straddling the edge of the main cluster is
        // part of the map, and dropping it would eat the outermost ring of every map.
        MapSurfaces surfaces = MapSurfaces.FromFaces(
            [Face(0f, (900f, 0f), (1200f, 0f), (1200f, 100f))],
            new MapBounds(-1000f, -1000f, 1000f, 1000f));

        surfaces.Triangles.Count.ShouldBe(3);
    }

    [Test]
    public void NoFacesGivesNoTriangles()
    {
        MapSurfaces.FromFaces([]).Triangles.ShouldBeEmpty();
    }
}
