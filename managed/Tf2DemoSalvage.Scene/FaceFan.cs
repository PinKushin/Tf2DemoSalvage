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
    public static uint[] Outline(IReadOnlyList<WorldVertex> vertices)
    {
        ArgumentNullException.ThrowIfNull(vertices);

        uint[] lines = new uint[vertices.Count * 2];

        for (int at = 0; at < vertices.Count; at++)
        {
            int first = at - (at % 3);

            lines[2 * at] = (uint)at;
            lines[(2 * at) + 1] = vertices[at].FaceEdge ? (uint)(first + ((at - first + 1) % 3)) : (uint)at;
        }

        return lines;
    }
}
