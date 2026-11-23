using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Scene;

/// <summary>A BSP face as the fan of triangles both builders cut it into, and which of their edges are its own (B803).</summary>
/// <remarks>
/// **Kept because `mat_wireframe 3` draws the polygon, not the triangles.** `engine.dll` (2026-10-07) `FUN_1800dc720` hands a
/// surface list to `FUN_1800e6780` when the value is 3. That builds one `MATERIAL_LINES` mesh of every surface with three or
/// more vertices, an edge from each vertex's predecessor to it, starting from the last (the map's vertex-index table at
/// `+0x108`, positions at `+0x38`), and draws it with `debugworldwireframe` (`0x18069b3e0`). The fan's diagonals are not in
/// that loop. The faces are gone by draw time, so each corner carries the answer (<see cref="WorldVertex.FaceEdge"/>).
/// </remarks>
public static class FaceFan
{
    /// <summary>Whether a corner's edge to the next corner of its triangle is one of the face's own.</summary>
    /// <param name="triangle">Which triangle of the fan (0, i + 1, i + 2), from 0.</param>
    /// <param name="triangles">How many the face has: its corners less two.</param>
    /// <param name="corner">Which corner of the triangle, 0 to 2.</param>
    /// <returns>False for a diagonal the fan added.</returns>
    /// <remarks>
    /// The middle edge (i + 1, i + 2) is always the polygon's; corner 0's edge (0, 1) only in the first triangle, and the
    /// closing edge (n − 1, 0) only in the last.
    /// </remarks>
    public static bool IsFaceEdge(int triangle, int triangles, int corner) => corner switch
    {
        0 => triangle == 0,
        1 => true,
        _ => triangle == triangles - 1,
    };

    /// <summary>A triangle list's outline as a line list: two indices per vertex, so a run of vertices is a run of lines.</summary>
    /// <param name="vertices">The triangles, three corners each.</param>
    /// <returns>Each vertex's edge to the next corner of its triangle, or a zero-length line on itself where that is no face edge.</returns>
    /// <remarks>
    /// The zero-length line lies on a corner of the face, which is on its outline already, so it can add nothing the loop
    /// does not draw. A fixed two per vertex is what lets a draw take any run of the buffer as <c>2 × first, 2 × count</c>.
    /// </remarks>
    /// <exception cref="ArgumentException">The count is not whole triangles: a last vertex's partner would lie past the end.</exception>
    public static uint[] Outline(IReadOnlyList<WorldVertex> vertices)
    {
        ArgumentNullException.ThrowIfNull(vertices);

        if (vertices.Count % 3 != 0)
        {
            throw new ArgumentException($"{vertices.Count} vertices are not whole triangles", nameof(vertices));
        }

        uint[] lines = new uint[vertices.Count * 2];

        for (int at = 0; at < vertices.Count; at++)
        {
            int first = at - (at % 3);

            lines[2 * at] = (uint)at;
            lines[(2 * at) + 1] = vertices[at].FaceEdge ? (uint)(first + ((at - first + 1) % 3)) : (uint)at;
        }

        return lines;
    }

    /// <summary>The faces a fanned triangle list was cut from, each with its plane, in order.</summary>
    /// <param name="vertices">The triangles, three corners each, marked as <see cref="IsFaceEdge"/> marks them.</param>
    /// <returns>One plane per face: a face starts at each triangle whose first corner's edge is the polygon's.</returns>
    /// <remarks>
    /// **For the brush-model cull under <c>mat_wireframe 3</c>** (B803 review 1): `engine.dll` `FUN_1800dcb20` tests each
    /// surface's plane against the eye before drawing it and leaves a back-facing one out of the list `FUN_1800e6780`
    /// outlines; a line list is never culled by the rasteriser, so the outline needs the plane. The plane is the first
    /// triangle's, on the side of its first corner's normal: a brush vertex's normal is the face's, or smoothed from it,
    /// so the two agree in sign where the winding would need the camera's handedness to read. A triangle with no area, or
    /// a normal along the face, gives a zero normal, which faces every eye.
    /// </remarks>
    public static FacePlane[] Faces(IReadOnlyList<WorldVertex> vertices)
    {
        ArgumentNullException.ThrowIfNull(vertices);

        List<FacePlane> faces = [];

        for (int at = 0; at + 2 < vertices.Count; at += 3)
        {
            if (faces.Count > 0 && !vertices[at].FaceEdge)
            {
                FacePlane open = faces[^1];
                faces[^1] = open with { Count = open.Count + 3 };
                continue;
            }

            faces.Add(PlaneOf(vertices[at], vertices[at + 1], vertices[at + 2], at));
        }

        return [.. faces];
    }

    private static FacePlane PlaneOf(WorldVertex a, WorldVertex b, WorldVertex c, int first)
    {
        (float ux, float uy, float uz) = (b.X - a.X, b.Y - a.Y, b.Depth - a.Depth);
        (float vx, float vy, float vz) = (c.X - a.X, c.Y - a.Y, c.Depth - a.Depth);
        (float nx, float ny, float nz) = ((uy * vz) - (uz * vy), (uz * vx) - (ux * vz), (ux * vy) - (uy * vx));
        float side = (nx * a.NormalX) + (ny * a.NormalY) + (nz * a.NormalZ);
        float length = MathF.Sqrt((nx * nx) + (ny * ny) + (nz * nz));

        if (side == 0f || length == 0f)
        {
            return new FacePlane(first, 3, 0f, 0f, 0f, 0f);
        }

        float scale = MathF.CopySign(1f / length, side);

        (nx, ny, nz) = (nx * scale, ny * scale, nz * scale);

        return new FacePlane(first, 3, nx, ny, nz, (nx * a.X) + (ny * a.Y) + (nz * a.Depth));
    }
}

/// <summary>One fanned face: its run of vertices and its plane (B803 review 1).</summary>
/// <param name="First">Its first vertex.</param>
/// <param name="Count">How many vertices its triangles take.</param>
/// <param name="NormalX">The plane's unit normal, on the face's front.</param>
/// <param name="NormalY">The plane's unit normal.</param>
/// <param name="NormalZ">The plane's unit normal.</param>
/// <param name="Distance">The plane's distance from the origin along the normal.</param>
public readonly record struct FacePlane(int First, int Count, float NormalX, float NormalY, float NormalZ, float Distance)
{
    /// <summary>Whether an eye sees the face's front, as `engine.dll` `FUN_1800dcb20` tests a brush-model surface.</summary>
    /// <param name="x">The eye, in the face's own space.</param>
    /// <param name="y">The eye.</param>
    /// <param name="z">The eye.</param>
    /// <returns><c>dot(normal, eye) − distance ≥ −0.01</c>: the engine's tolerance, the `-0.01` that surface loop compares against.</returns>
    public bool FacesEye(float x, float y, float z) =>
        (NormalX * x) + (NormalY * y) + (NormalZ * z) - Distance >= -0.01f;
}
