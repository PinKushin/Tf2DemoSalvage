using System;
using System.Buffers.Binary;
using System.IO;

namespace Tf2DemoSalvage.Content.Bsp;

/// <summary>
/// The BSP tree, walked to find which leaf a point is in.
/// </summary>
/// <remarks>
/// **Ten lines, and they are Valve's.** From <c>PointLeafnum</c> in <c>bsplib.cpp</c>:
///
/// <code>
/// int node = 0;
/// while( node >= 0 )
/// {
///     dnode_t* pNode = &amp;dnodes[node];
///     dplane_t* pPlane = &amp;dplanes[pNode->planenum];
///
///     if (DotProduct( pPlane->normal, pt ) &lt;= pPlane->dist)
///         node = pNode->children[1];
///     else
///         node = pNode->children[0];
/// }
/// return - node - 1;
/// </code>
///
/// **A negative child is a leaf, encoded as <c>-(leaf + 1)</c>**, which is why the loop ends on a
/// negative and the answer is <c>-node - 1</c>. Reading it as an index would walk off into the node
/// array and answer confidently.
///
/// This exists to light models: an entity is lit from the ambient cube of the leaf it stands in
/// (see <see cref="BspAmbientLight"/>), and finding that leaf is this walk.
/// </remarks>
public sealed class BspLeafTree
{
    private const int LumpPlanes = 1;
    private const int LumpNodes = 5;

    private const int PlaneStride = 20;
    private const int NodeStride = 32;

    private readonly ReadOnlyMemory<byte> _nodes;
    private readonly ReadOnlyMemory<byte> _planes;

    private BspLeafTree(ReadOnlyMemory<byte> nodes, ReadOnlyMemory<byte> planes)
    {
        _nodes = nodes;
        _planes = planes;
    }

    /// <summary>Whether the map carried a tree to walk.</summary>
    public bool IsEmpty => _nodes.IsEmpty || _planes.IsEmpty;

    /// <summary>Builds a tree from lumps already in hand.</summary>
    /// <param name="nodes">The NODES lump.</param>
    /// <param name="planes">The PLANES lump.</param>
    /// <returns>The tree.</returns>
    /// <remarks>
    /// **Separate from <see cref="Read"/> so the walk can be tested without a map.** A real BSP
    /// cannot say which leaf is the right answer without already trusting this code, so the tests
    /// build a tree of one node and assert where each side lands.
    /// </remarks>
    public static BspLeafTree FromLumps(ReadOnlyMemory<byte> nodes, ReadOnlyMemory<byte> planes) =>
        new(nodes, planes);

    /// <summary>Reads the nodes and planes.</summary>
    /// <param name="file">The whole map file.</param>
    /// <returns>The tree; empty when the map has no nodes.</returns>
    /// <exception cref="InvalidDataException">The header or a lump is malformed.</exception>
    public static BspLeafTree Read(ReadOnlyMemory<byte> file)
    {
        BspHeader header = BspHeader.Parse(file.Span);

        return new BspLeafTree(
            BspLumpData.Read(file, header.Lump(LumpNodes)),
            BspLumpData.Read(file, header.Lump(LumpPlanes)));
    }

    /// <summary>Which leaf contains a point.</summary>
    /// <param name="x">World position.</param>
    /// <param name="y">World position.</param>
    /// <param name="z">World position.</param>
    /// <returns>The leaf index, or −1 when the map has no tree.</returns>
    /// <remarks>
    /// **No bounds test on the way in.** A point outside the map still lands in a leaf — the solid
    /// one that surrounds everything — and that leaf carries no light, which is the right answer
    /// for something outside the world rather than an error.
    /// </remarks>
    public int LeafAt(float x, float y, float z)
    {
        if (IsEmpty)
        {
            return -1;
        }

        ReadOnlySpan<byte> nodes = _nodes.Span;
        ReadOnlySpan<byte> planes = _planes.Span;

        int node = 0;

        // Bounded by the node count as well as by the sign: a malformed tree can otherwise loop
        // for ever, and a viewer that hangs on one map is worse than one that draws it unlit.
        for (int step = 0; node >= 0 && step <= nodes.Length / NodeStride; step++)
        {
            int at = node * NodeStride;

            if (at + NodeStride > nodes.Length)
            {
                return -1;
            }

            int planeIndex = BinaryPrimitives.ReadInt32LittleEndian(nodes[at..]);
            int planeAt = planeIndex * PlaneStride;

            if (planeAt < 0 || planeAt + PlaneStride > planes.Length)
            {
                return -1;
            }

            float normalX = BinaryPrimitives.ReadSingleLittleEndian(planes[planeAt..]);
            float normalY = BinaryPrimitives.ReadSingleLittleEndian(planes[(planeAt + 4)..]);
            float normalZ = BinaryPrimitives.ReadSingleLittleEndian(planes[(planeAt + 8)..]);
            float distance = BinaryPrimitives.ReadSingleLittleEndian(planes[(planeAt + 12)..]);

            float side = (normalX * x) + (normalY * y) + (normalZ * z);

            // In front takes child 0, behind or on the plane takes child 1 - Valve's comparison,
            // including which side "on the plane" belongs to.
            node = side <= distance
                ? BinaryPrimitives.ReadInt32LittleEndian(nodes[(at + 8)..])
                : BinaryPrimitives.ReadInt32LittleEndian(nodes[(at + 4)..]);
        }

        return node >= 0 ? -1 : -node - 1;
    }
}
