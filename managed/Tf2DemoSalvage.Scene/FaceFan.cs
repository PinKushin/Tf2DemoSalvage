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
    /// outlines; a line list is never culled by the rasteriser, so the outline needs the plane. The plane is the face's
    /// first triangle with area, on the side of its first corner's normal: a brush vertex's normal is the face's, or
    /// smoothed from it, so the two agree in sign where the winding would need the camera's handedness to read.
    ///
    /// **A face with no area still has a plane** (B803 review 2, Grok): the engine tests the plane each surface stores
    /// (`FUN_1800df1a0` keeps the surface's plane pointer, `+4`, for `FUN_1800dcb20`'s test), whatever its area, so a
    /// sliver seen from behind is culled like any face. With no triangle to take a plane from, it is the first corner's
    /// normal through that corner, the nearest the vertices hold to the stored one. Only a corner with no normal either
    /// gives a zero plane, which faces every eye.
    /// </remarks>
    public static FacePlane[] Faces(IReadOnlyList<WorldVertex> vertices)
    {
        ArgumentNullException.ThrowIfNull(vertices);

        List<FacePlane> faces = [];
        bool open = false;

        for (int at = 0; at + 2 < vertices.Count; at += 3)
        {
            FacePlane? triangle = PlaneOf(vertices[at], vertices[at + 1], vertices[at + 2], at);

            if (faces.Count > 0 && !vertices[at].FaceEdge)
            {
                FacePlane last = faces[^1];

                // A face still on its corner's normal takes the first triangle of it that has area.
                faces[^1] = open && triangle is { } better
                    ? better with { First = last.First, Count = last.Count + 3 }
                    : last with { Count = last.Count + 3 };
                open &= triangle is null;
                continue;
            }

            faces.Add(triangle ?? NormalPlane(vertices[at], at));
            open = triangle is null;
        }

        return [.. faces];
    }

    /// <summary>The ranges of a run's vertices whose faces reach the outline from an eye, for a line-list draw.</summary>
    /// <param name="faces">The buffer's faces, in vertex order (<see cref="Faces"/>).</param>
    /// <param name="first">The run's first vertex.</param>
    /// <param name="end">One past its last, after any cap.</param>
    /// <param name="eye">The eye, in the faces' space.</param>
    /// <param name="translucent">Whether the run draws in the translucent pass (<see cref="FacePlane.OutlinedFrom"/>).</param>
    /// <param name="noCull">Whether its material is <c>$nocull</c>.</param>
    /// <param name="into">Cleared, then filled with each range, first to end, consecutive outlined faces merged.</param>
    /// <remarks>
    /// **Every range is inside the run and not empty** (B803 review 3, Grok): a face that starts before the run (a run cut
    /// mid-face, as the vertex cap can leave the one before it) contributes only its part inside, and one that ends past
    /// the cap only its part before it, so a later run never resubmits an earlier run's lines and a draw is never handed a
    /// count that wraps. The faces are found by a binary search for the first ending after <paramref name="first"/>.
    /// </remarks>
    public static void Runs(
        FacePlane[] faces,
        int first,
        int end,
        (float X, float Y, float Z) eye,
        bool translucent,
        bool noCull,
        ICollection<(int First, int End)> into)
    {
        ArgumentNullException.ThrowIfNull(faces);
        ArgumentNullException.ThrowIfNull(into);

        into.Clear();

        int start = -1;

        for (int at = FirstEnding(faces, first); at < faces.Length && faces[at].First < end; at++)
        {
            FacePlane face = faces[at];

            if (face.OutlinedFrom(eye.X, eye.Y, eye.Z, translucent, noCull))
            {
                start = start < 0 ? Math.Max(face.First, first) : start;
            }
            else if (start >= 0)
            {
                into.Add((start, face.First));
                start = -1;
            }
        }

        if (start >= 0)
        {
            into.Add((start, end));
        }
    }

    /// <summary>The first face, in vertex order, that ends after <paramref name="vertex"/>.</summary>
    private static int FirstEnding(FacePlane[] faces, int vertex)
    {
        int low = 0;
        int high = faces.Length;

        while (low < high)
        {
            int middle = (low + high) / 2;

            if (faces[middle].First + faces[middle].Count <= vertex)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    /// <summary>
    /// A triangle's plane on its first corner's normal side; null only for one with no area; the zero plane, which faces
    /// every eye, where the triangle has area but no side can be chosen (B803 review 3).
    /// </summary>
    /// <remarks>
    /// **An undecidable face is drawn whole, the safe error** (`WorldRenderer.ModelEye`'s rule): a normal lying along the
    /// face (side 0), or a non-finite corner, gives no front. Taking the corner normal's plane there, as review 2 did,
    /// stood a plane across a visible face and culled it from half of space; a NaN plane failed every test.
    /// </remarks>
    private static FacePlane? PlaneOf(WorldVertex a, WorldVertex b, WorldVertex c, int first)
    {
        (float ux, float uy, float uz) = (b.X - a.X, b.Y - a.Y, b.Depth - a.Depth);
        (float vx, float vy, float vz) = (c.X - a.X, c.Y - a.Y, c.Depth - a.Depth);
        (float nx, float ny, float nz) = ((uy * vz) - (uz * vy), (uz * vx) - (ux * vz), (ux * vy) - (uy * vx));
        float side = (nx * a.NormalX) + (ny * a.NormalY) + (nz * a.NormalZ);
        float length = MathF.Sqrt((nx * nx) + (ny * ny) + (nz * nz));

        if (!float.IsFinite(length) || !float.IsFinite(side))
        {
            return Undecided(first);
        }

        if (length == 0f)
        {
            return null;
        }

        if (side == 0f)
        {
            return Undecided(first);
        }

        float scale = MathF.CopySign(1f / length, side);

        (nx, ny, nz) = (nx * scale, ny * scale, nz * scale);

        return Finite(new FacePlane(first, 3, nx, ny, nz, (nx * a.X) + (ny * a.Y) + (nz * a.Depth)));
    }

    /// <summary>The plane through a corner along its own normal; the zero plane for a corner with none.</summary>
    private static FacePlane NormalPlane(WorldVertex corner, int first)
    {
        float length = MathF.Sqrt((corner.NormalX * corner.NormalX) + (corner.NormalY * corner.NormalY) + (corner.NormalZ * corner.NormalZ));

        if (length == 0f || !float.IsFinite(length))
        {
            return Undecided(first);
        }

        (float nx, float ny, float nz) = (corner.NormalX / length, corner.NormalY / length, corner.NormalZ / length);

        return Finite(new FacePlane(first, 3, nx, ny, nz, (nx * corner.X) + (ny * corner.Y) + (nz * corner.Depth)));
    }

    /// <summary>The zero plane, which faces every eye: a face drawn whole.</summary>
    private static FacePlane Undecided(int first) => new(first, 3, 0f, 0f, 0f, 0f);

    /// <summary>The plane, or the zero plane where any term is not finite (a NaN corner, B803 review 3).</summary>
    private static FacePlane Finite(FacePlane plane) =>
        float.IsFinite(plane.NormalX) && float.IsFinite(plane.NormalY) && float.IsFinite(plane.NormalZ) && float.IsFinite(plane.Distance)
            ? plane
            : Undecided(plane.First);
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
    public bool FacesEye(float x, float y, float z) => Ahead(x, y, z) >= -0.01f;

    /// <summary>Whether a brush entity's face reaches the outline from this eye, by the pass that draws it (B803 review 3).</summary>
    /// <param name="x">The eye, in the face's own space.</param>
    /// <param name="y">The eye.</param>
    /// <param name="z">The eye.</param>
    /// <param name="translucent">Whether the face draws in the translucent pass.</param>
    /// <param name="noCull">Whether its material is <c>$nocull</c>.</param>
    /// <returns>
    /// An opaque face: <see cref="FacesEye"/>, `FUN_1800dcb20`'s test, no flag read. A translucent one: `FUN_1800dbdb0`'s,
    /// which walks the model's node tree back to front and keeps a translucent surface that is NOCULL (`0x200`) or whose
    /// plane-back flag (`0x40`) matches the eye's side of its node's plane, `dist &lt; 0`: no tolerance, NOCULL exempt.
    /// </returns>
    /// <remarks>
    /// The plane here is the face's front, so the plane-back comparison reads as the eye at or ahead of it. A face whose
    /// node plane points away from it (plane-back) is kept only strictly ahead, which this cannot tell from at: the two
    /// differ only for an eye exactly on the plane (*interpolated* as negligible).
    /// </remarks>
    public bool OutlinedFrom(float x, float y, float z, bool translucent, bool noCull) =>
        translucent ? noCull || Ahead(x, y, z) >= 0f : FacesEye(x, y, z);

    /// <summary>The eye's signed distance ahead of the face, along its front normal.</summary>
    private float Ahead(float x, float y, float z) => (NormalX * x) + (NormalY * y) + (NormalZ * z) - Distance;
}
