using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Bsp;

namespace Tf2DemoSalvage.Viewer3D;

/// <summary>One triangle corner on the ground plane, with the brightness of its surface.</summary>
/// <param name="X">Position east, in Source units.</param>
/// <param name="Y">Position north, in Source units.</param>
/// <param name="Shade">Brightness in 0..1, from the surface's height.</param>
internal readonly record struct MapTriangle(float X, float Y, float Shade);

/// <summary>
/// A map's faces as filled triangles, ordered so the higher surface wins.
/// </summary>
/// <remarks>
/// **The BSP already holds these polygons; nothing here invents geometry.** A face is a convex
/// polygon in winding order, so filling it needs a triangle fan and no more. The outline view is
/// the same data reduced to its edges — which is what <c>mat_wireframe</c> draws in game, from the
/// same lumps — so the two are two readings of one source rather than two sources.
///
/// What this layer has to decide is only what a top-down projection cannot get from the file:
///
/// - **Which surface is on top.** Seen from above, a roof and the floor beneath it cover the same
///   pixels. There is no depth buffer and none is wanted for a flat view, so draw order is the
///   depth test and the list is sorted by height.
/// - **How bright each one is.** Filled flat, a map from above is one silhouette; the shapes that
///   carry the information are the height changes. Shading by height is what makes a roof read as
///   something above the floor rather than a shape painted on it.
/// </remarks>
internal sealed class MapSurfaces
{
    private MapSurfaces(IReadOnlyList<MapTriangle> triangles) => Triangles = triangles;

    /// <summary>Darkest a surface is drawn.</summary>
    /// <remarks>
    /// Not zero: the floor of a map is most of its area, and a black floor makes the outlines the
    /// only thing visible, which is the wireframe view again with extra steps.
    /// </remarks>
    private const float MinimumShade = 0.28f;

    /// <summary>Brightest a surface is drawn.</summary>
    private const float MaximumShade = 1f;

    /// <summary>Every triangle corner, three per triangle, in draw order.</summary>
    public IReadOnlyList<MapTriangle> Triangles { get; }

    /// <summary>Whether there is anything to fill.</summary>
    public bool IsEmpty => Triangles.Count == 0;

    /// <summary>Builds filled triangles from map faces.</summary>
    /// <param name="faces">Faces to fill, typically <c>BspGeometry.OverheadFaces</c>.</param>
    /// <returns>The surfaces.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="faces"/> is null.</exception>
    public static MapSurfaces FromFaces(IReadOnlyList<BspFace> faces) =>
        FromFaces(faces, area: null);

    /// <summary>Builds filled triangles from the map faces inside an area.</summary>
    /// <param name="faces">Faces to fill, typically <c>BspGeometry.OverheadFaces</c>.</param>
    /// <param name="area">Ground-plane area to keep, or null for all of it.</param>
    /// <returns>The surfaces.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="faces"/> is null.</exception>
    /// <remarks>
    /// **The area matters for the SHADE, not for the drawing.** Geometry outside the main cluster
    /// falls outside the view anyway, so excluding it saves a little work. What it really does is
    /// keep the 3D skybox room out of the height range: that room sits at a Z far from the map's
    /// own, and a range stretched to reach it compresses every real surface into one narrow band,
    /// which is a filled map that reads as a flat silhouette. Measured on cp_process_final.
    ///
    /// A face is judged on ANY corner being inside. Judging on all of them would eat the outermost
    /// ring of faces on every map, since the cluster bound is derived from those same points.
    /// </remarks>
    public static MapSurfaces FromFaces(IReadOnlyList<BspFace> faces, MapBounds? area)
    {
        ArgumentNullException.ThrowIfNull(faces);

        List<BspFace> kept = [];
        float lowest = float.PositiveInfinity;
        float highest = float.NegativeInfinity;

        foreach (BspFace face in faces)
        {
            if (!Touches(face, area))
            {
                continue;
            }

            kept.Add(face);

            foreach ((float _, float _, float z) in face.Points)
            {
                lowest = Math.Min(lowest, z);
                highest = Math.Max(highest, z);
            }
        }

        // Sorted by the face's own height, lowest first, so a roof is written over the floor it
        // covers. Faces are compared on their highest point: a ramp belongs where it ends up, and
        // sorting on an average would let a long slope sink under a floor it climbs above.
        List<BspFace> ordered = kept;
        ordered.Sort((left, right) => Top(left).CompareTo(Top(right)));

        List<MapTriangle> triangles = [];

        foreach (BspFace face in ordered)
        {
            IReadOnlyList<(float X, float Y, float Z)> points = face.Points;

            if (points.Count < 3)
            {
                // An edge or a point has no area. Real maps carry these.
                continue;
            }

            float shade = Shade(Top(face), lowest, highest);

            // A fan from the first vertex: (0,1,2), (0,2,3), ... Faces out of a BSP are convex by
            // construction - the compiler splits anything that is not - so a fan is exact here and
            // needs no general triangulation.
            for (int index = 1; index + 1 < points.Count; index++)
            {
                Add(triangles, points[0], shade);
                Add(triangles, points[index], shade);
                Add(triangles, points[index + 1], shade);
            }
        }

        return new MapSurfaces(triangles);
    }

    private static bool Touches(BspFace face, MapBounds? area)
    {
        if (area is null)
        {
            return true;
        }

        MapBounds bounds = area.Value;

        foreach ((float x, float y, float _) in face.Points)
        {
            if (x >= bounds.MinX && x <= bounds.MaxX && y >= bounds.MinY && y <= bounds.MaxY)
            {
                return true;
            }
        }

        return false;
    }

    private static void Add(
        List<MapTriangle> triangles, (float X, float Y, float Z) point, float shade) =>
        triangles.Add(new MapTriangle(point.X, point.Y, shade));

    private static float Top(BspFace face)
    {
        float top = float.NegativeInfinity;

        foreach ((float _, float _, float z) in face.Points)
        {
            top = Math.Max(top, z);
        }

        return top;
    }

    /// <summary>Maps a height onto the shade range.</summary>
    /// <remarks>
    /// **A flat map has a zero range, and dividing by it would give NaN** — which reaches the
    /// vertex buffer as a corner at no position and takes the whole triangle with it. A map with
    /// every surface at one height is unusual but a map with two is not, and the midpoint is the
    /// honest answer for both.
    /// </remarks>
    private static float Shade(float height, float lowest, float highest)
    {
        float range = highest - lowest;

        if (!float.IsFinite(range) || range <= 0f)
        {
            return (MinimumShade + MaximumShade) / 2f;
        }

        float fraction = Math.Clamp((height - lowest) / range, 0f, 1f);

        return MinimumShade + (fraction * (MaximumShade - MinimumShade));
    }
}
