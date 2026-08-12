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
        IReadOnlyList<((float X, float Y) From, (float X, float Y) To)> segments, MapBounds bounds)
    {
        Segments = segments;
        Bounds = bounds;
    }

    /// <summary>Every edge to draw, as a pair of ground-plane points.</summary>
    public IReadOnlyList<((float X, float Y) From, (float X, float Y) To)> Segments { get; }

    /// <summary>The extent of everything in <see cref="Segments"/>.</summary>
    public MapBounds Bounds { get; }

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

        return new MapOutline(segments, bounds);
    }
}
