using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene;

/// <summary>The BSP leaf the camera stands in, as projected line segments — <c>mat_leafvis</c>.</summary>
/// <remarks>
/// **This was <c>MainForm.LeafBoxLines</c>** (B188, D90). Walking a BSP tree to a leaf and
/// projecting a box through a view matrix are both things a second frontend would have to
/// reimplement, so neither is view work however few lines it takes.
///
/// **The leaf the CAMERA is in, which is what Valve draws.** A BSP leaf is the unit the engine culls
/// and traces against, so "which one am I in and how big is it" is the question behind every
/// visibility oddity — a prop that vanishes at a doorway, a sound that carries too far. Drawing all
/// of them would be a wireframe of the whole tree and would answer nothing.
///
/// **The box is the leaf's own culling bound rather than a tight fit**, which is Valve's own
/// framing: `dleaf_t`'s mins/maxs are "for frustum culling". A picture of a tighter shape would
/// answer a question nobody asks, because the loose box is what the engine actually tests.
///
/// **Parity note, and it is a divergence stated rather than hidden.** `mat_leafvis` itself lives in
/// the closed engine renderer and is not in `source-sdk-2013`; the published analogue is
/// `cl_drawleaf` (`clientleafsystem.cpp:32`), a `FCVAR_CHEAT` debug convar which filters the
/// renderables list down to one leaf rather than outlining it. So the intent — "show me what this
/// leaf contains or covers" — is Valve's; the outline is ours, and it is drawn in clip space
/// because our line channel is a screen-space overlay pass that ignores depth. A box half-hidden by
/// the wall it describes would be worse than useless, which is the same reason the player markers
/// ignore depth.
/// </remarks>
public static class LeafVis
{
    /// <summary>How large w must be before a corner is considered to be in front of the eye.</summary>
    /// <remarks>
    /// **Not zero, because dividing by a w at or below zero MIRRORS the point through the camera**
    /// and the edge then streaks across the screen from somewhere it is not. A small positive
    /// epsilon also keeps a corner exactly on the near plane from projecting to infinity.
    /// </remarks>
    private const float InFront = 0.0001f;

    /// <summary>How many floats a view-projection has.</summary>
    private const int MatrixElements = 16;

    /// <summary>The twelve edges of the leaf containing a point, projected.</summary>
    /// <param name="tree">The map's BSP tree, or null when no map is loaded.</param>
    /// <param name="eye">Where the viewer is standing, in world units.</param>
    /// <param name="viewProjection">The view-projection the world is drawn with, row major.</param>
    /// <returns>Clip-space segments, or nothing when there is no tree or the leaf has no box.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="viewProjection"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="viewProjection"/> is too short.</exception>
    /// <remarks>
    /// **Nothing rather than a box at the origin when the leaf has no bounds.** A tree built without
    /// the leaf lump can still say WHICH leaf a point is in — the walk needs only nodes and planes —
    /// but it cannot say how big that leaf is, and drawing a guess would be drawing a lie.
    /// </remarks>
    public static IReadOnlyList<((float X, float Y) From, (float X, float Y) To)> Lines(
        BspLeafTree? tree,
        (float X, float Y, float Z) eye,
        float[] viewProjection)
    {
        CheckMatrix(viewProjection);

        if (tree is null || tree.Bounds(tree.LeafAt(eye.X, eye.Y, eye.Z)) is not { } box)
        {
            return [];
        }

        return Edges(box, viewProjection);
    }

    /// <summary>The twelve edges of a box, projected.</summary>
    /// <param name="box">Its minimum and maximum corner, in world units.</param>
    /// <param name="viewProjection">The view-projection the world is drawn with, row major.</param>
    /// <returns>Clip-space segments; an edge with either end behind the eye is dropped.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="viewProjection"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="viewProjection"/> is too short.</exception>
    public static IReadOnlyList<((float X, float Y) From, (float X, float Y) To)> Edges(
        ((float X, float Y, float Z) Min, (float X, float Y, float Z) Max) box,
        float[] viewProjection)
    {
        CheckMatrix(viewProjection);

        List<((float X, float Y) From, (float X, float Y) To)> lines = [];

        // The twelve edges of a box: every pair of corners differing in exactly one axis bit, taken
        // from the end where that bit is clear so each edge is emitted once rather than twice.
        for (int from = 0; from < 8; from++)
        {
            foreach (int axis in Axes)
            {
                int to = from | axis;

                if (to == from)
                {
                    continue;
                }

                if (Project(Corner(box, from), viewProjection) is { } a &&
                    Project(Corner(box, to), viewProjection) is { } b)
                {
                    lines.Add((a, b));
                }
            }
        }

        return lines;
    }

    /// <summary>The three axis bits of a corner index.</summary>
    /// <remarks>A field rather than a literal in the loop, so the allocation happens once.</remarks>
    private static ReadOnlySpan<int> Axes => [1, 2, 4];

    /// <summary>One of the eight corners of a box, chosen by its three axis bits.</summary>
    private static (float X, float Y, float Z) Corner(
        ((float X, float Y, float Z) Min, (float X, float Y, float Z) Max) box, int which) => (
            (which & 1) == 0 ? box.Min.X : box.Max.X,
            (which & 2) == 0 ? box.Min.Y : box.Max.Y,
            (which & 4) == 0 ? box.Min.Z : box.Max.Z);

    /// <summary>A world point in clip space, or null when it is behind the eye.</summary>
    /// <remarks>
    /// **Row-vector, which is what the shader does: `mul(world, viewProjection)` with the matrix
    /// declared `row_major`.** So a point multiplies the matrix from the LEFT, the translation lives
    /// in elements 12-14, and w comes from element 11 — <see cref="FreeCamera.ToMatrix"/> sets
    /// `projection[11] = 1`, which is the giveaway.
    ///
    /// The first version of this indexed the matrix as a column-vector transform, taking w from
    /// 12-15. That does not fail; it produces A projection. The owner saw the box as "a dot that
    /// gets kinda triangular", which is a room-sized box collapsed through the wrong transform.
    /// This project already carries a memory about the two matrix conventions it uses on purpose;
    /// that is what mixing them looks like from the outside.
    /// </remarks>
    private static (float X, float Y)? Project((float X, float Y, float Z) point, float[] matrix)
    {
        float x = (point.X * matrix[0]) + (point.Y * matrix[4]) + (point.Z * matrix[8]) + matrix[12];
        float y = (point.X * matrix[1]) + (point.Y * matrix[5]) + (point.Z * matrix[9]) + matrix[13];
        float w = (point.X * matrix[3]) + (point.Y * matrix[7]) + (point.Z * matrix[11]) + matrix[15];

        return w > InFront ? (x / w, y / w) : null;
    }

    /// <summary>Refuses a matrix that cannot be indexed, rather than failing inside the projection.</summary>
    private static void CheckMatrix(float[] viewProjection)
    {
        ArgumentNullException.ThrowIfNull(viewProjection);

        if (viewProjection.Length < MatrixElements)
        {
            throw new ArgumentException(
                $"a view-projection has {MatrixElements} elements, not {viewProjection.Length}",
                nameof(viewProjection));
        }
    }
}
