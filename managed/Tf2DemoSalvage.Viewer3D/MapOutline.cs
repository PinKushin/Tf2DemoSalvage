using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Bsp;

namespace Tf2DemoSalvage.Viewer3D;

/// <summary>The extent of a map on the ground plane, in Source units.</summary>
/// <param name="MinX">Westmost point.</param>
/// <param name="MinY">Southmost point.</param>
/// <param name="MaxX">Eastmost point.</param>
/// <param name="MaxY">Northmost point.</param>
internal readonly record struct MapBounds(float MinX, float MinY, float MaxX, float MaxY);

/// <summary>
/// A map reduced to line segments on the ground plane.
/// </summary>
/// <remarks>
/// **Outlines rather than filled polygons, and that is a deliberate choice about legibility.**
/// Filled from above, a TF2 map is a solid blob: every floor overlaps every other, and the shapes
/// that carry the information — walls, doorways, the gaps between buildings — disappear into it.
/// Drawn as edges it reads like a radar overview, which is what someone reviewing a match wants.
///
/// It is also much less work. Filling would mean triangulating arbitrary convex polygons and then
/// depth-sorting them; edges need neither, and the renderer already draws primitives.
///
/// **Z is dropped, not converted.** A top-down view is the XY plane of Source's Z-up space, so
/// height simply does not participate — which conveniently sidesteps the handedness conversion
/// entirely for this view. When the free 3D camera arrives it will need the real conversion, and
/// <c>RENDERING_NOTES.md</c> section 1 is explicit that it must happen in exactly one place.
/// </remarks>
internal sealed class MapOutline
{
    private MapOutline(
        IReadOnlyList<((float X, float Y) From, (float X, float Y) To)> segments,
        MapBounds bounds,
        MapBounds mainBounds)
    {
        Segments = segments;
        Bounds = bounds;
        MainBounds = mainBounds;
    }

    /// <summary>Grid cell used to decide what counts as connected, in Source units.</summary>
    /// <remarks>
    /// 256 units is four player-widths: coarse enough that a doorway or a gap between two brushes
    /// does not split a map into pieces, fine enough that a 3D skybox room sitting thousands of
    /// units away never touches it. The rooms measured on shipped maps are tens of thousands of
    /// units clear, so nothing here is close to the boundary.
    /// </remarks>
    private const int ClusterCell = 256;

    /// <summary>Every edge to draw, as a pair of ground-plane points.</summary>
    public IReadOnlyList<((float X, float Y) From, (float X, float Y) To)> Segments { get; }

    /// <summary>The extent of everything in <see cref="Segments"/>.</summary>
    public MapBounds Bounds { get; }

    /// <summary>The extent of the map proper, ignoring detached outlying geometry.</summary>
    /// <remarks>
    /// **What the camera should frame.** A TF2 map carries its 3D skybox as ordinary world
    /// geometry, built at reduced scale and placed far outside the playable space, so
    /// <see cref="Bounds"/> is much larger than the map anyone wants to look at — on
    /// <c>cp_process_final</c> that pushed the real map into a third of the viewport.
    ///
    /// Measured over nine shipped maps, the largest connected cluster of geometry holds between
    /// 91.1% and 99.7% of all points and the outliers are single-digit percentages, so "the
    /// biggest connected piece" identifies the map without needing to know what the other pieces
    /// are.
    ///
    /// **Two rules were tried and rejected before this one.** Trimming to a vertex percentile cut
    /// a third off the height of real maps, because vertex density is not extent — detail
    /// concentrates in the middle of a map and its edges are sparse. Using the <c>sky_camera</c>
    /// entity as a marker looked exact, and is not: the entity is placed to *view* the skybox room
    /// rather than to sit in it, and on four of the nine maps it falls outside every cluster of
    /// geometry altogether.
    ///
    /// Nothing is discarded — <see cref="Segments"/> still holds every edge, and outlying geometry
    /// simply falls outside the view.
    /// </remarks>
    public MapBounds MainBounds { get; }

    /// <summary>Whether there is anything to draw.</summary>
    public bool IsEmpty => Segments.Count == 0;

    /// <summary>Flattens faces into ground-plane line segments.</summary>
    /// <param name="faces">Faces to draw, typically <c>BspGeometry.OverheadFaces</c>.</param>
    /// <returns>The outline.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="faces"/> is null.</exception>
    public static MapOutline FromFaces(IReadOnlyList<BspFace> faces)
    {
        ArgumentNullException.ThrowIfNull(faces);

        List<((float X, float Y) From, (float X, float Y) To)> segments = [];

        float minX = float.PositiveInfinity;
        float minY = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float maxY = float.NegativeInfinity;

        foreach (IReadOnlyList<(float X, float Y, float Z)> points in faces.Select(face => face.Points))
        {
            if (points.Count < 2)
            {
                // A single point has no edge, and emitting a zero-length segment would leave a
                // stray dot on the map. Degenerate faces do reach here from real files.
                continue;
            }

            for (int index = 0; index < points.Count; index++)
            {
                (float x, float y, _) = points[index];

                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }

            // A polygon of N points has N edges: the last joins back to the first. Forgetting the
            // closing edge leaves a gap at one corner of every face in the map.
            //
            // Except when N is 2, where the closing edge IS the opening one - emitting it twice
            // doubles the work and draws over itself.
            int edges = points.Count == 2 ? 1 : points.Count;

            for (int index = 0; index < edges; index++)
            {
                (float fromX, float fromY, _) = points[index];
                (float toX, float toY, _) = points[(index + 1) % points.Count];

                segments.Add(((fromX, fromY), (toX, toY)));
            }
        }

        MapBounds bounds = segments.Count == 0
            ? new MapBounds(0f, 0f, 0f, 0f)
            : new MapBounds(minX, minY, maxX, maxY);

        return new MapOutline(segments, bounds, MainCluster(segments, bounds));
    }

    /// <summary>Finds the extent of the largest connected group of segments.</summary>
    /// <remarks>
    /// Occupancy on a coarse grid, then a flood fill over occupied cells with eight-way
    /// neighbours. Grid-based rather than geometric because the question is only "is there
    /// geometry near here", and a cell lookup answers it in constant time where a distance test
    /// between every pair of segments would not.
    ///
    /// **Whole segments are marked, not just their endpoints.** A 500-unit edge crosses cells that
    /// hold no vertex, and skipping them splits a single connected map into pieces at every long
    /// wall. On a real map the sheer density of vertices hides this almost everywhere, which is
    /// exactly what makes it worth a test: the bug survives the map and dies on three quads.
    /// </remarks>
    private static MapBounds MainCluster(
        List<((float X, float Y) From, (float X, float Y) To)> segments, MapBounds full)
    {
        if (segments.Count == 0)
        {
            return full;
        }

        Dictionary<(int X, int Y), int> occupancy = [];

        foreach (((float X, float Y) from, (float X, float Y) to) in segments)
        {
            Occupy(occupancy, from, to);
        }

        HashSet<(int X, int Y)> unvisited = [.. occupancy.Keys];
        HashSet<(int X, int Y)> best = [];
        int bestPoints = 0;

        while (unvisited.Count > 0)
        {
            (int X, int Y) seed = unvisited.First();
            unvisited.Remove(seed);

            Queue<(int X, int Y)> pending = new();
            pending.Enqueue(seed);

            HashSet<(int X, int Y)> cluster = [];
            int points = 0;

            while (pending.Count > 0)
            {
                (int X, int Y) cell = pending.Dequeue();
                cluster.Add(cell);
                points += occupancy[cell];

                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (unvisited.Remove((cell.X + dx, cell.Y + dy)))
                        {
                            pending.Enqueue((cell.X + dx, cell.Y + dy));
                        }
                    }
                }
            }

            // Strictly greater, so the largest wins rather than whichever was visited first.
            if (points > bestPoints)
            {
                bestPoints = points;
                best = cluster;
            }
        }

        // Exact extents of the points inside the winning cluster, not the cluster's cell edges.
        // Snapping to a 256-unit grid would report a map as up to a cell larger than it is, and
        // would make an undivided map's main extent differ from its full extent for no reason.
        float minX = float.PositiveInfinity;
        float minY = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float maxY = float.NegativeInfinity;

        foreach (((float X, float Y) from, (float X, float Y) to) in segments)
        {
            foreach ((float X, float Y) point in new[] { from, to })
            {
                if (!best.Contains(Cell(point)))
                {
                    continue;
                }

                minX = Math.Min(minX, point.X);
                minY = Math.Min(minY, point.Y);
                maxX = Math.Max(maxX, point.X);
                maxY = Math.Max(maxY, point.Y);
            }
        }

        return float.IsInfinity(minX) ? full : new MapBounds(minX, minY, maxX, maxY);
    }

    /// <summary>Marks every cell a segment passes through.</summary>
    private static void Occupy(
        Dictionary<(int X, int Y), int> occupancy, (float X, float Y) from, (float X, float Y) to)
    {
        float length = MathF.Max(MathF.Abs(to.X - from.X), MathF.Abs(to.Y - from.Y));

        // Half a cell per step, so no cell on the line can be stepped over.
        int steps = Math.Max(1, (int)MathF.Ceiling(length / (ClusterCell / 2f)));

        for (int step = 0; step <= steps; step++)
        {
            float fraction = (float)step / steps;
            (int X, int Y) cell = Cell((
                from.X + ((to.X - from.X) * fraction),
                from.Y + ((to.Y - from.Y) * fraction)));

            occupancy[cell] = occupancy.TryGetValue(cell, out int seen) ? seen + 1 : 1;
        }
    }

    private static (int X, int Y) Cell((float X, float Y) point) => (
        (int)MathF.Floor(point.X / ClusterCell),
        (int)MathF.Floor(point.Y / ClusterCell));
}
