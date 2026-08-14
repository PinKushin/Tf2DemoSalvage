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
    private const int LumpLeafs = 10;

    /// <summary><c>CONTENTS_SOLID</c> from <c>bspflags.h</c>: "an eye is never valid in a solid".</summary>
    private const int ContentsSolid = 0x1;

    /// <summary>How far to look for the sky before giving up, in world units.</summary>
    /// <remarks>
    /// A TF2 map fits comfortably inside this; a ray that has travelled it without meeting solid
    /// has left the world. Bounded rather than unbounded so a malformed tree cannot spin.
    /// </remarks>
    private const float SkyReach = 16384f;

    /// <summary>How far apart the trace samples, in world units.</summary>
    /// <remarks>
    /// **Sampled rather than a true plane-by-plane sweep, and this is where it can be wrong.** A
    /// solid thinner than this between a point and the sky is missed, and the point is treated as
    /// lit. Sixteen units is half the thickness of the thinnest wall a mapper would build and a
    /// quarter of TF2's grid, so the case is rare; it is stated rather than hidden because a
    /// missed occluder shows as one object lit indoors, which reads as a lighting bug.
    /// </remarks>
    private const float SkyStep = 16f;

    private const int PlaneStride = 20;
    private const int NodeStride = 32;

    private readonly ReadOnlyMemory<byte> _nodes;
    private readonly ReadOnlyMemory<byte> _planes;
    private readonly ReadOnlyMemory<byte> _leaves;
    private readonly int _leafStride;

    private BspLeafTree(
        ReadOnlyMemory<byte> nodes,
        ReadOnlyMemory<byte> planes,
        ReadOnlyMemory<byte> leaves = default,
        int leafStride = 32)
    {
        _nodes = nodes;
        _planes = planes;
        _leaves = leaves;
        _leafStride = leafStride;
    }

    /// <summary>Whether the map carried a tree to walk.</summary>
    public bool IsEmpty => _nodes.IsEmpty || _planes.IsEmpty;

    /// <summary>Builds a tree from lumps already in hand.</summary>
    /// <param name="nodes">The NODES lump.</param>
    /// <param name="planes">The PLANES lump.</param>
    /// <param name="leaves">The LEAFS lump, needed only for the sky trace.</param>
    /// <returns>The tree.</returns>
    /// <remarks>
    /// **Separate from <see cref="Read"/> so the walk can be tested without a map.** A real BSP
    /// cannot say which leaf is the right answer without already trusting this code, so the tests
    /// build a tree of one node and assert where each side lands.
    /// </remarks>
    public static BspLeafTree FromLumps(
        ReadOnlyMemory<byte> nodes,
        ReadOnlyMemory<byte> planes,
        ReadOnlyMemory<byte> leaves = default) =>
        new(nodes, planes, leaves);

    /// <summary>Reads the nodes and planes.</summary>
    /// <param name="file">The whole map file.</param>
    /// <returns>The tree; empty when the map has no nodes.</returns>
    /// <exception cref="InvalidDataException">The header or a lump is malformed.</exception>
    public static BspLeafTree Read(ReadOnlyMemory<byte> file)
    {
        BspHeader header = BspHeader.Parse(file.Span);

        return new BspLeafTree(
            BspLumpData.Read(file, header.Lump(LumpNodes)),
            BspLumpData.Read(file, header.Lump(LumpPlanes)),
            BspLumpData.Read(file, header.Lump(LumpLeafs)),
            header.Lump(LumpLeafs).Version >= 1 ? 32 : 56);
    }

    /// <summary>Whether a point can see the sky along a direction.</summary>
    /// <param name="x">World position.</param>
    /// <param name="y">World position.</param>
    /// <param name="z">World position.</param>
    /// <param name="towardsX">Direction to look, which for the sun is away from its normal.</param>
    /// <param name="towardsY">Direction to look.</param>
    /// <param name="towardsZ">Direction to look.</param>
    /// <returns><c>true</c> when nothing solid stands in the way.</returns>
    /// <remarks>
    /// **Valve's parenthesis, made real.** <c>bspfile.h</c> describes a sky light as a
    /// "directional light with no falloff (surface must trace to SKY texture)" — the trace is not
    /// an optimisation, it is the difference between sunlight and a sun that shines through
    /// ceilings.
    ///
    /// Answers true when the map has no leaves to test, since a viewer that decided everything was
    /// in shadow would be worse than one that lit everything: the first hides the map, the second
    /// merely flatters it.
    /// </remarks>
    public bool SeesSky(float x, float y, float z, float towardsX, float towardsY, float towardsZ)
    {
        if (_leaves.IsEmpty || IsEmpty)
        {
            return true;
        }

        ReadOnlySpan<byte> leaves = _leaves.Span;

        // Started clear of the surface the model stands on, which is otherwise the first thing the
        // trace hits: a pack sitting on the floor is a point on a solid plane.
        for (float distance = SkyStep; distance <= SkyReach; distance += SkyStep)
        {
            int leaf = LeafAt(
                x + (towardsX * distance),
                y + (towardsY * distance),
                z + (towardsZ * distance));

            if (leaf < 0)
            {
                return true;
            }

            int at = leaf * _leafStride;

            if (at + 4 > leaves.Length)
            {
                return true;
            }

            if ((BinaryPrimitives.ReadInt32LittleEndian(leaves[at..]) & ContentsSolid) != 0)
            {
                return false;
            }
        }

        return true;
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
