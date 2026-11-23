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
    /// <param name="stored">
    /// Each face's stored plane, in the same face order, or null where the vertices are all there is (a test's model).
    /// </param>
    /// <returns>One plane per face: a face starts at each triangle whose first corner's edge is the polygon's.</returns>
    /// <remarks>
    /// **For the brush-model cull under <c>mat_wireframe 3</c>** (B803 review 1): `engine.dll` `FUN_1800dcb20` tests each
    /// surface's plane against the eye before drawing it and leaves a back-facing one out of the list `FUN_1800e6780`
    /// outlines; a line list is never culled by the rasteriser, so the outline needs the plane.
    ///
    /// **The plane is the surface's stored one** (B803 review 6, Fable): `FUN_1800df1a0` keeps the surface record's plane
    /// pointer (`+4`) for `FUN_1800dcb20`'s test, and `FUN_1800dbdb0` the node's plane with the surface's plane-back flag
    /// (`0x40`). A plane derived from the fan's triangles is wrong in exactly the case vbsp leaves: a T-junction corner up
    /// to `OFF_EPSILON` 0.25 off its edge (`utils/vbsp/faces.cpp:31`, `:458`) as a brush entity's fan start, which only the
    /// world re-triangulates (`:636-641`, `:652`, `entity_num == 0`), so the first triangle is a sliver at an arbitrary
    /// angle about that edge, and no floor on its sine separates it from a real thin triangle. So
    /// <paramref name="stored"/> decides wherever it is given, and a count that does not match the faces is refused.
    ///
    /// **Without it** the plane is the face's largest triangle, on its first corner's normal side; a face whose triangles
    /// all have no area, or no side, gets the zero plane, drawn whole (the safe error, `WorldRenderer.ModelEye`'s rule).
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="stored"/> does not have one plane per face.</exception>
    public static FacePlane[] Faces(IReadOnlyList<WorldVertex> vertices, IReadOnlyList<StoredPlane>? stored = null)
    {
        ArgumentNullException.ThrowIfNull(vertices);

        List<FacePlane> faces = [];
        List<float> areas = [];

        for (int at = 0; at + 2 < vertices.Count; at += 3)
        {
            // Nothing is derived where the stored planes are given: the triangles only say where each face starts.
            (FacePlane triangle, float area) = stored is null
                ? PlaneOf(vertices[at], vertices[at + 1], vertices[at + 2], at)
                : (Undecided(at), 0f);

            if (faces.Count > 0 && !vertices[at].FaceEdge)
            {
                FacePlane last = faces[^1];

                // The larger triangle's plane, the run the face's own.
                faces[^1] = area > areas[^1]
                    ? triangle with { First = last.First, Count = last.Count + 3 }
                    : last with { Count = last.Count + 3 };
                areas[^1] = MathF.Max(area, areas[^1]);
                continue;
            }

            faces.Add(triangle);
            areas.Add(area);
        }

        if (stored is null)
        {
            return [.. faces];
        }

        if (stored.Count != faces.Count)
        {
            throw new ArgumentException($"{stored.Count} stored planes for {faces.Count} faces", nameof(stored));
        }

        for (int face = 0; face < faces.Count; face++)
        {
            StoredPlane plane = stored[face];

            faces[face] = faces[face] with
            {
                NormalX = plane.NormalX,
                NormalY = plane.NormalY,
                NormalZ = plane.NormalZ,
                Distance = plane.Distance,
                PlaneBack = plane.PlaneBack,
            };
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
    /// A triangle's plane on its first corner's normal side, with its area (its cross product's length); the zero plane,
    /// which faces every eye, with no area, where it has none or no side can be chosen.
    /// </summary>
    /// <remarks>
    /// **An undecidable face is drawn whole, the safe error** (`WorldRenderer.ModelEye`'s rule): a normal lying along the
    /// face (side 0), or a non-finite corner, gives no front (B803 review 3). Only where no stored plane is given.
    /// </remarks>
    private static (FacePlane Plane, float Area) PlaneOf(WorldVertex a, WorldVertex b, WorldVertex c, int first)
    {
        (float ux, float uy, float uz) = (b.X - a.X, b.Y - a.Y, b.Depth - a.Depth);
        (float vx, float vy, float vz) = (c.X - a.X, c.Y - a.Y, c.Depth - a.Depth);
        (float nx, float ny, float nz) = ((uy * vz) - (uz * vy), (uz * vx) - (ux * vz), (ux * vy) - (uy * vx));
        float side = (nx * a.NormalX) + (ny * a.NormalY) + (nz * a.NormalZ);
        float length = MathF.Sqrt((nx * nx) + (ny * ny) + (nz * nz));

        if (!float.IsFinite(length) || !float.IsFinite(side) || length == 0f)
        {
            return (Undecided(first), 0f);
        }

        if (side == 0f)
        {
            return (Undecided(first), length);
        }

        float scale = MathF.CopySign(1f / length, side);

        (nx, ny, nz) = (nx * scale, ny * scale, nz * scale);

        return (Finite(new FacePlane(first, 3, nx, ny, nz, (nx * a.X) + (ny * a.Y) + (nz * a.Depth))), length);
    }

    /// <summary>The zero plane, which faces every eye: a face drawn whole.</summary>
    private static FacePlane Undecided(int first) => new(first, 3, 0f, 0f, 0f, 0f);

    /// <summary>The plane, or the zero plane where any term is not finite (a NaN corner, B803 review 3).</summary>
    private static FacePlane Finite(FacePlane plane) =>
        float.IsFinite(plane.NormalX) && float.IsFinite(plane.NormalY) && float.IsFinite(plane.NormalZ) && float.IsFinite(plane.Distance)
            ? plane
            : Undecided(plane.First);
}

/// <summary>A BSP face's stored plane, as the engine's surface record holds it (B803 review 6).</summary>
/// <param name="NormalX">
/// The plane's normal: `dplane_t` by the face's `planenum`, which vbsp writes as the face's own plane, facing out
/// (`writebsp.cpp:465-467`); measured: the triangles' plane agrees with it on every brush face of two maps.
/// </param>
/// <param name="NormalY">The plane's normal.</param>
/// <param name="NormalZ">The plane's normal.</param>
/// <param name="Distance">The plane's `dist`.</param>
/// <param name="PlaneBack">The face's `side`: it lies on the back of its plane, the surface's `0x40` flag.</param>
public readonly record struct StoredPlane(float NormalX, float NormalY, float NormalZ, float Distance, bool PlaneBack);

/// <summary>One fanned face: its run of vertices and its plane (B803 review 1).</summary>
/// <param name="First">Its first vertex.</param>
/// <param name="Count">How many vertices its triangles take.</param>
/// <param name="NormalX">The plane's unit normal, facing out of the face: its stored plane's, else its triangles'.</param>
/// <param name="NormalY">The plane's unit normal.</param>
/// <param name="NormalZ">The plane's unit normal.</param>
/// <param name="Distance">The plane's distance from the origin along the normal.</param>
/// <param name="PlaneBack">Whether the face lies on the back of that plane (`0x40`); never for a derived plane.</param>
public readonly record struct FacePlane(
    int First, int Count, float NormalX, float NormalY, float NormalZ, float Distance, bool PlaneBack = false)
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
    /// The plane is the face's own, as stored: vbsp writes `planenum` as the face's plane and `side` as its low bit
    /// (`utils/vbsp/writebsp.cpp:465-467`), so a plane-back face's stored plane is its node's flipped. The opaque test
    /// reads it as is. The translucent walk measures the eye against the NODE's plane and keeps a face whose plane-back
    /// flag equals `dist &lt; 0`; the node's distance is the face's, negated for a plane-back face, so the two agree except
    /// for an eye exactly on the plane, which a plane-back face does not keep. That a face's node plane is its plane's
    /// even twin is *interpolated* (vbsp's split planes, not read for this).
    /// </remarks>
    public bool OutlinedFrom(float x, float y, float z, bool translucent, bool noCull)
    {
        if (!translucent)
        {
            return FacesEye(x, y, z);
        }

        float node = PlaneBack ? -Ahead(x, y, z) : Ahead(x, y, z);

        return noCull || PlaneBack == (node < 0f);
    }

    /// <summary>The eye's signed distance from the plane, along its normal.</summary>
    private float Ahead(float x, float y, float z) => (NormalX * x) + (NormalY * y) + (NormalZ * z) - Distance;
}
