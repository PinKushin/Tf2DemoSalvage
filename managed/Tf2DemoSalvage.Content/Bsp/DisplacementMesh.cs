using System;
using System.Collections.Generic;
using System.Numerics;

namespace Tf2DemoSalvage.Content.Bsp;

/// <summary>A displacement as `CDispInfo` keeps it for its decals: the grid, each vertex's normal, and the quadtree's runs.</summary>
/// <param name="Side">Vertices along each edge, <c>2^power + 1</c>.</param>
/// <param name="Positions">The grid's positions, row-major, <c>row · side + column</c>.</param>
/// <param name="Normals">Each vertex's normal, <see cref="DisplacementMesh.NormalsOf"/>.</param>
/// <param name="Indices">The tesselation (<see cref="DisplacementTesselation"/>), three a triangle.</param>
/// <param name="Nodes">Each node's own run of <paramref name="Indices"/>, in walk order.</param>
public sealed record DisplacementMesh(
    int Side,
    IReadOnlyList<Vector3> Positions,
    IReadOnlyList<Vector3> Normals,
    IReadOnlyList<int> Indices,
    IReadOnlyList<DisplacementNode> Nodes)
{
    /// <summary>
    /// Each vertex's normal — <c>CCoreDispInfo::GenerateDispSurfNormals</c> and <c>CalcNormalFromEdges</c>
    /// (`builddisp.cpp:1732-1913`): the two triangles of each quad around the vertex that exists, each normalised, averaged
    /// — NOT normalised again, so a vertex on a crease has a normal shorter than one.
    /// </summary>
    /// <param name="positions">The grid, row-major.</param>
    /// <param name="side">Its side.</param>
    /// <returns>A normal per vertex, in the same order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="positions"/> is null.</exception>
    /// <remarks>
    /// Valve's names, kept: <c>indexCol</c> is the ROW and <c>indexRow</c> the column. Quadrant I is (+column, +row), II
    /// (−column, +row), III (−column, −row), IV (+column, −row); each needs both of its edges to exist (`DoesEdgeExist`).
    /// </remarks>
    public static Vector3[] NormalsOf(IReadOnlyList<Vector3> positions, int side)
    {
        ArgumentNullException.ThrowIfNull(positions);

        Vector3[] normals = new Vector3[side * side];

        for (int row = 0; row < side; row++)
        {
            for (int column = 0; column < side; column++)
            {
                bool left = column - 1 >= 0;
                bool top = row + 1 <= side - 1;
                bool right = column + 1 <= side - 1;
                bool bottom = row - 1 >= 0;

                Vector3 sum = Vector3.Zero;
                int count = 0;

                Vector3 V(int r, int c) => positions[(r * side) + c];

                void Add(Vector3 first, Vector3 second)
                {
                    Vector3 normal = Vector3.Cross(second, first);
                    float length = normal.Length();

                    sum += length > 0f ? normal / length : normal;
                    count++;
                }

                if (top && right)
                {
                    Add(V(row + 1, column) - V(row, column), V(row, column + 1) - V(row, column));
                    Add(V(row + 1, column) - V(row, column + 1), V(row + 1, column + 1) - V(row, column + 1));
                }

                if (left && top)
                {
                    Add(V(row + 1, column - 1) - V(row, column - 1), V(row, column) - V(row, column - 1));
                    Add(V(row + 1, column - 1) - V(row, column), V(row + 1, column) - V(row, column));
                }

                if (left && bottom)
                {
                    Add(V(row, column - 1) - V(row - 1, column - 1), V(row - 1, column) - V(row - 1, column - 1));
                    Add(V(row, column - 1) - V(row - 1, column), V(row, column) - V(row - 1, column));
                }

                if (right && bottom)
                {
                    Add(V(row, column) - V(row - 1, column), V(row - 1, column + 1) - V(row - 1, column));
                    Add(V(row, column) - V(row - 1, column + 1), V(row, column + 1) - V(row - 1, column + 1));
                }

                normals[(row * side) + column] = count > 0 ? sum / count : Vector3.Zero;
            }
        }

        return normals;
    }
}
