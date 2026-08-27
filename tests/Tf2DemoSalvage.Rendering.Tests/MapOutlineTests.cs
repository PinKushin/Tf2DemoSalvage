using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Rendering.Tests;

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
    public void MapOutline_APolygon_BecomesAClosedLoopOfSegments()
    {
        // Three points make three segments, not two: the last joins back to the first, and a
        // reader that forgets that draws a map full of gaps at every corner.
        MapOutline outline = MapOutline.FromFaces(
            [Face((0f, 0f, 0f), (10f, 0f, 0f), (10f, 10f, 0f))]);

        outline.Segments.Count.ShouldBe(3);
        outline.Segments[2].ShouldBe(((10f, 10f), (0f, 0f)));
    }

    [Test]
    public void MapOutline_Height_IsDroppedWhileXAndYSurvive()
    {
        // A top-down view is the XY plane of Source's Z-up space, so Z is what goes.
        MapOutline outline = MapOutline.FromFaces(
            [Face((1f, 2f, 999f), (3f, 4f, -999f))]);

        outline.Segments[0].ShouldBe(((1f, 2f), (3f, 4f)));
    }

    [Test]
    public void MapOutline_ADistantDetachedRoom_IsIgnoredByTheMainBounds()
    {
        // **The 3D skybox room.** It is ordinary world geometry placed far from the map at reduced
        // scale, so from above it lands in a corner of its own and stretches the extent the camera
        // is fitted to - on cp_process_final it squeezed the real map into a third of the viewport.
        //
        // Measured across nine shipped maps, the largest connected cluster of geometry holds
        // between 91.1% and 99.7% of all points, and the outlying rooms are single-digit
        // percentages. So the main cluster is what the camera follows.
        List<BspFace> faces =
        [
            // The map: a run of touching quads.
            Face((0f, 0f, 0f), (500f, 0f, 0f), (500f, 500f, 0f), (0f, 500f, 0f)),
            Face((500f, 0f, 0f), (1000f, 0f, 0f), (1000f, 500f, 0f), (500f, 500f, 0f)),
            Face((1000f, 0f, 0f), (1500f, 0f, 0f), (1500f, 500f, 0f), (1000f, 500f, 0f)),

            // The skybox room, far away and small.
            Face((20000f, 20000f, 0f), (20200f, 20000f, 0f), (20200f, 20200f, 0f)),
        ];

        MapOutline outline = MapOutline.FromFaces(faces);

        // The full extent still covers everything - nothing is thrown away, it is simply not what
        // the camera frames.
        outline.Bounds.MaxX.ShouldBe(20200f);

        outline.MainBounds.MinX.ShouldBe(0f);
        outline.MainBounds.MinY.ShouldBe(0f);
        outline.MainBounds.MaxX.ShouldBe(1500f);
        outline.MainBounds.MaxY.ShouldBe(500f);
    }

    [Test]
    public void MapOutline_ASingleConnectedMap_IsKeptWhole()
    {
        // The control, and the one that matters most. A map with no detached room must not be
        // trimmed at all - a rule that shrinks every map to its densest part would be the vertex
        // percentile that was tried first and measured cutting a third off real maps.
        MapOutline outline = MapOutline.FromFaces(
        [
            Face((0f, 0f, 0f), (500f, 0f, 0f), (500f, 500f, 0f)),
            Face((500f, 0f, 0f), (1000f, 0f, 0f), (1000f, 500f, 0f)),
        ]);

        outline.MainBounds.ShouldBe(outline.Bounds);
    }

    [Test]
    public void MapOutline_TwoClusters_FollowTheLargerNotTheFirst()
    {
        // Order must not decide it. The small cluster is built first, so a rule that kept whichever
        // component it happened to visit first would pass every other test in this file.
        MapOutline outline = MapOutline.FromFaces(
        [
            Face((30000f, 30000f, 0f), (30100f, 30000f, 0f), (30100f, 30100f, 0f)),

            Face((0f, 0f, 0f), (500f, 0f, 0f), (500f, 500f, 0f), (0f, 500f, 0f)),
            Face((500f, 0f, 0f), (1000f, 0f, 0f), (1000f, 500f, 0f), (500f, 500f, 0f)),
        ]);

        outline.MainBounds.MaxX.ShouldBe(1000f);
    }

    [Test]
    public void MapOutline_AnEmptyMap_HasMainBoundsEqualToFull()
    {
        MapOutline outline = MapOutline.FromFaces([]);

        outline.MainBounds.ShouldBe(outline.Bounds);
    }

    [Test]
    public void MapOutline_TheBounds_CoverEveryPoint()
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
    public void MapOutline_AFaceWithOneVertex_ProducesNoSegments()
    {
        // Degenerate faces reach here from real maps. A single point has no edge, and emitting a
        // zero-length segment would put a stray dot on the map.
        MapOutline outline = MapOutline.FromFaces([Face((5f, 5f, 0f))]);

        outline.Segments.ShouldBeEmpty();
    }

    [Test]
    public void MapOutline_NoFaces_GivesAnEmptyOutline()
    {
        // A map that could not be found or read still has to leave the viewer usable.
        MapOutline outline = MapOutline.FromFaces([]);

        outline.Segments.ShouldBeEmpty();
        outline.IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void MapOutline_ATwoPointFace_IsASingleSegment()
    {
        // Closing a two-point polygon would emit the same edge twice, doubling the work for every
        // such face and drawing over itself.
        MapOutline outline = MapOutline.FromFaces([Face((0f, 0f, 0f), (10f, 0f, 0f))]);

        outline.Segments.ShouldHaveSingleItem();
    }
}
