using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Bsp;
using Tf2DemoSalvage.Viewer3D;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// Tests turning BSP faces into line segments for the overhead view.
/// </summary>
/// <remarks>
/// The map is drawn as outlines rather than filled polygons. Filled, an overhead view of a TF2
/// map is a solid blob - every floor overlaps every other and the shapes that matter, the walls
/// and doorways, vanish into it. Outlines give the radar look the viewer wants, and they need no
/// triangulation of arbitrary polygons.
/// </remarks>
public sealed class MapOutlineTests
{
    private static BspFace Face(params (float X, float Y, float Z)[] points) =>
        new(points, (0f, 0f, 1f), SurfaceProperties.None);

    [Test]
    public void APolygonBecomesAClosedLoopOfSegments()
    {
        // Three points make three segments, not two: the last joins back to the first, and a
        // reader that forgets that draws a map full of gaps at every corner.
        MapOutline outline = MapOutline.FromFaces(
            [Face((0f, 0f, 0f), (10f, 0f, 0f), (10f, 10f, 0f))]);

        outline.Segments.Count.ShouldBe(3);
        outline.Segments[2].ShouldBe(((10f, 10f), (0f, 0f)));
    }

    [Test]
    public void HeightIsDroppedButXAndYSurvive()
    {
        // A top-down view is the XY plane of Source's Z-up space, so Z is what goes.
        MapOutline outline = MapOutline.FromFaces(
            [Face((1f, 2f, 999f), (3f, 4f, -999f))]);

        outline.Segments[0].ShouldBe(((1f, 2f), (3f, 4f)));
    }

    [Test]
    public void TheBoundsCoverEveryPoint()
    {
        // The camera fits to these, so a bound that missed a corner would push part of the map
        // off screen with nothing to say why.
        MapOutline outline = MapOutline.FromFaces(
        [
            Face((-500f, -250f, 0f), (100f, 50f, 0f)),
            Face((2000f, 1000f, 0f), (0f, 0f, 0f)),
        ]);

        outline.Bounds.MinX.ShouldBe(-500f);
        outline.Bounds.MinY.ShouldBe(-250f);
        outline.Bounds.MaxX.ShouldBe(2000f);
        outline.Bounds.MaxY.ShouldBe(1000f);
    }

    [Test]
    public void AFaceWithOneVertexProducesNoSegments()
    {
        // Degenerate faces reach here from real maps. A single point has no edge, and emitting a
        // zero-length segment would put a stray dot on the map.
        MapOutline outline = MapOutline.FromFaces([Face((5f, 5f, 0f))]);

        outline.Segments.ShouldBeEmpty();
    }

    [Test]
    public void NoFacesGivesAnEmptyOutlineRatherThanThrowing()
    {
        // A map that could not be found or read still has to leave the viewer usable.
        MapOutline outline = MapOutline.FromFaces([]);

        outline.Segments.ShouldBeEmpty();
        outline.IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void ATwoPointFaceIsASingleSegmentNotTwo()
    {
        // Closing a two-point polygon would emit the same edge twice, doubling the work for every
        // such face and drawing over itself.
        MapOutline outline = MapOutline.FromFaces([Face((0f, 0f, 0f), (10f, 0f, 0f))]);

        outline.Segments.ShouldHaveSingleItem();
    }
}
