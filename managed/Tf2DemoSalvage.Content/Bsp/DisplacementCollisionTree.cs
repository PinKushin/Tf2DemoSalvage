using System;
using System.Collections.Generic;
using System.Numerics;

namespace Tf2DemoSalvage.Content.Bsp;

/// <summary>
/// A displacement's collision tree as the engine keeps it — <c>CDispCollTree</c>, whose vertices and triangles are what
/// <c>GetVirtualMeshList</c> hands vphysics and whose tree answers <c>GetTrianglesInSphere</c> (B369).
/// </summary>
/// <remarks>
/// **Read from published source.** The vertices are <c>CCoreDispInfo::GenerateDispSurf</c> (`public/builddisp.cpp:1918-1994`), the
/// triangles <c>GenerateCollisionSurface</c> (`:934-966`) — the FULL grid, an even index split bottom-left to top-right and an odd one
/// top-left to bottom-right, not the allowed-vertex tessellation the renderer and vbsp's hull use. The tree is
/// <c>AABBTree_CreateLeafs</c> and <c>AABBTree_GenerateBoxes_r</c> (`public/dispcoll_common.cpp:406-465`): a 4-ary tree whose node
/// <c>n</c> has children <c>4n + 1..4n + 4</c>, a leaf per grid cell at the bit-interleaved index of its column and row, each leaf
/// two triangles. The runtime handler reads exactly this (`engine.dll` `FUN_18017cb00`: <c>2·4^power</c> triangles from
/// <c>m_aTris</c>, each vertex index <c>&amp; 0x1ff</c>).
/// </remarks>
public sealed class DisplacementCollisionTree
{
    private readonly (Vector3[] Mins, Vector3[] Maxs)[] _nodes;
    private readonly (int First, int Second)[] _leaves;

    private DisplacementCollisionTree(
        Vector3[] vertices, (int A, int B, int C)[] triangles, (Vector3[] Mins, Vector3[] Maxs)[] nodes, (int, int)[] leaves)
    {
        Vertices = vertices;
        Triangles = triangles;
        _nodes = nodes;
        _leaves = leaves;
    }

    /// <summary>Every grid vertex, row by row — <c>m_aVerts</c>.</summary>
    public IReadOnlyList<Vector3> Vertices { get; }

    /// <summary>Every triangle's vertex indices — <c>m_aTris</c>.</summary>
    public IReadOnlyList<(int A, int B, int C)> Triangles { get; }

    /// <summary>The base face's four corners, the grid's start corner first: rows run from 0 to 1, columns from 0 to 3.</summary>
    public IReadOnlyList<Vector3> Corners { get; private init; } = [];

    /// <summary>Grid vertices a side, <c>2^power + 1</c>.</summary>
    public int Side { get; private init; }

    /// <summary>Builds the tree over a displacement.</summary>
    /// <param name="points">The base face's four corners, already rotated so the grid's start corner is first.</param>
    /// <param name="power">The displacement's power, 2 to 4.</param>
    /// <param name="field">Each grid vertex's field direction and distance, row by row.</param>
    /// <returns>The tree.</returns>
    /// <exception cref="ArgumentNullException">A list is null.</exception>
    /// <exception cref="ArgumentException">There are not four points, or the field is not one entry per vertex.</exception>
    public static DisplacementCollisionTree Build(
        IReadOnlyList<Vector3> points, int power, IReadOnlyList<(Vector3 Direction, float Distance)> field)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(field);

        int side = (1 << power) + 1;

        if (points.Count != 4 || field.Count != side * side)
        {
            throw new ArgumentException("A displacement is four corners and one field entry per grid vertex.");
        }

        Vector3[] vertices = Surface(points, side, field);
        (int A, int B, int C)[] triangles = CollisionSurface(side);

        int cells = side - 1;
        (int, int)[] leaves = new (int, int)[cells * cells];

        for (int row = 0; row < cells; row++)
        {
            for (int column = 0; column < cells; column++)
            {
                int triangle = ((row * cells) + column) * 2;
                leaves[Interleave(column, row)] = (triangle, triangle + 1);
            }
        }

        int nodeCount = ((1 << ((power + 1) << 1)) / 3) - leaves.Length;
        (Vector3[] Mins, Vector3[] Maxs)[] nodes = new (Vector3[], Vector3[])[nodeCount];

        for (int index = 0; index < nodeCount; index++)
        {
            nodes[index] = (new Vector3[4], new Vector3[4]);
        }

        DisplacementCollisionTree tree = new(vertices, triangles, nodes, leaves) { Corners = [.. points], Side = side };
        tree.Boxes(0, out Vector3 mins, out Vector3 maxs);

        // AABBTree_CalcBounds: "Bloat a little."
        tree.Mins = mins - Vector3.One;
        tree.Maxs = maxs + Vector3.One;

        return tree;
    }

    /// <summary>The whole tree's lower bound, bloated by one unit — <c>m_mins</c>, the handler's world bounds.</summary>
    public Vector3 Mins { get; private set; }

    /// <summary>Its upper bound, likewise — <c>m_maxs</c>.</summary>
    public Vector3 Maxs { get; private set; }

    /// <summary>The triangles whose leaf boxes a sphere reaches — <c>AABBTree_BuildTreeTrisInSphere_r</c>.</summary>
    /// <param name="center">The sphere's centre, in the map's units.</param>
    /// <param name="radius">Its radius.</param>
    /// <param name="max">The most indices to answer — the handler passes <c>0xc00</c>.</param>
    /// <returns>Triangle indices, both of a leaf at a time, breadth first.</returns>
    /// <remarks>
    /// <code>
    /// the list starts [root]; pop:  a leaf → it and every entry after it are leaves: each whose two fit under max, both triangles
    /// a node → IntersectFourBoxSpherePairs: per child, d = max(max(0, mins − c), c − maxs) per axis;  d·d &lt;= r² → child pushed, 0..3
    /// </code>
    /// </remarks>
    public IReadOnlyList<int> TrianglesInSphere(Vector3 center, float radius, int max)
    {
        List<int> nodeList = [0];
        List<int> found = [];
        float radiusSquared = radius * radius;

        int at = 0;

        while (at < nodeList.Count)
        {
            int node = nodeList[at];

            if (node >= _nodes.Length)
            {
                // "the rest are all leaves"
                foreach (int leaf in nodeList.GetRange(at, nodeList.Count - at))
                {
                    if (found.Count + 2 <= max)
                    {
                        (int first, int second) = _leaves[leaf - _nodes.Length];
                        found.Add(first);
                        found.Add(second);
                    }
                }

                break;
            }

            at++;

            (Vector3[] mins, Vector3[] maxs) = _nodes[node];

            for (int child = 0; child < 4; child++)
            {
                Vector3 minimum = Vector3.Max(Vector3.Zero, mins[child] - center);
                Vector3 total = Vector3.Max(minimum, center - maxs[child]);
                float distance = (total.X * total.X) + (total.Y * total.Y) + (total.Z * total.Z);

                if (distance <= radiusSquared)
                {
                    nodeList.Add((node << 2) + child + 1);
                }
            }
        }

        return found;
    }

    /// <summary><c>AABBTree_GenerateBoxes_r</c>: a node's bounds, its children's stored per lane.</summary>
    private void Boxes(int node, out Vector3 mins, out Vector3 maxs)
    {
        mins = new Vector3(99999f);
        maxs = new Vector3(-99999f);

        if (node >= _nodes.Length)
        {
            (int first, int second) = _leaves[node - _nodes.Length];

            foreach (int triangle in (int[])[first, second])
            {
                (int a, int b, int c) = Triangles[triangle];

                foreach (int vertex in (int[])[a, b, c])
                {
                    mins = Vector3.Min(mins, Vertices[vertex]);
                    maxs = Vector3.Max(maxs, Vertices[vertex]);
                }
            }

            return;
        }

        for (int child = 0; child < 4; child++)
        {
            Boxes((node << 2) + child + 1, out Vector3 childMins, out Vector3 childMaxs);
            _nodes[node].Mins[child] = childMins;
            _nodes[node].Maxs[child] = childMaxs;
            // AddPointToBounds( childMins ), then AddPointToBounds( childMaxs ).
            mins = Vector3.Min(mins, childMins);
            maxs = Vector3.Max(maxs, childMins);
            mins = Vector3.Min(mins, childMaxs);
            maxs = Vector3.Max(maxs, childMaxs);
        }
    }

    /// <summary><c>GenerateDispSurf</c>, with no elevation and no subdivision offset — neither is in the map's lumps.</summary>
    private static Vector3[] Surface(IReadOnlyList<Vector3> points, int side, IReadOnlyList<(Vector3 Direction, float Distance)> field)
    {
        float interval = 1f / (side - 1);
        Vector3 first = (points[1] - points[0]) * interval;
        Vector3 second = (points[2] - points[3]) * interval;
        Vector3[] vertices = new Vector3[side * side];

        for (int row = 0; row < side; row++)
        {
            Vector3 start = (first * row) + points[0];
            Vector3 end = (second * row) + points[3];
            Vector3 step = (end - start) * interval;

            for (int column = 0; column < side; column++)
            {
                int index = (row * side) + column;
                (Vector3 direction, float distance) = field[index];

                vertices[index] = start + (step * column) + (direction * distance);
            }
        }

        return vertices;
    }

    /// <summary><c>GenerateCollisionSurface</c>: odd grid index <c>BuildTriTLtoBR</c>, even <c>BuildTriBLtoTR</c>.</summary>
    private static (int A, int B, int C)[] CollisionSurface(int side)
    {
        List<(int, int, int)> triangles = new((side - 1) * (side - 1) * 2);

        for (int row = 0; row < side - 1; row++)
        {
            for (int column = 0; column < side - 1; column++)
            {
                int index = (row * side) + column;

                if (index % 2 == 1)
                {
                    triangles.Add((index, index + side, index + 1));
                    triangles.Add((index + 1, index + side, index + side + 1));
                }
                else
                {
                    triangles.Add((index, index + side, index + side + 1));
                    triangles.Add((index, index + side + 1, index + 1));
                }
            }
        }

        return [.. triangles];
    }

    /// <summary><c>Nodes_GetIndexFromComponents</c>: x's bits at the even places, y's at the odd.</summary>
    private static int Interleave(int x, int y)
    {
        int index = 0;

        for (int shift = 0; x != 0; shift += 2, x >>= 1)
        {
            index |= (x & 1) << shift;
        }

        for (int shift = 1; y != 0; shift += 2, y >>= 1)
        {
            index |= (y & 1) << shift;
        }

        return index;
    }
}
