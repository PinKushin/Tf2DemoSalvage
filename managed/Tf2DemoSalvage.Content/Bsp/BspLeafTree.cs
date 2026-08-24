using System;
using System.Buffers.Binary;
using System.IO;

using static Tf2DemoSalvage.Content.Bsp.BspStructLayout;

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
    /// <summary><c>CONTENTS_SOLID</c> from <c>bspflags.h</c>: "an eye is never valid in a solid".</summary>
    /// <remarks>Internal so <c>SurfaceFlagTests</c> checks this value rather than a copy of it.</remarks>
    internal const int ContentsSolid = 0x1;

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
            BspLumpData.Read(file, header.Lump(BspLumpIndex.Nodes)),
            BspLumpData.Read(file, header.Lump(BspLumpIndex.Planes)),
            BspLumpData.Read(file, header.Lump(BspLumpIndex.Leafs)),
            header.Lump(BspLumpIndex.Leafs).Version >= 1 ? 32 : 56);
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

    /// <summary>Whether nothing solid stands between two points.</summary>
    /// <param name="fromX">Where the segment starts, in world units.</param>
    /// <param name="fromY">Where the segment starts.</param>
    /// <param name="fromZ">Where the segment starts.</param>
    /// <param name="toX">Where it ends.</param>
    /// <param name="toY">Where it ends.</param>
    /// <param name="toZ">Where it ends.</param>
    /// <returns><c>true</c> when the segment is unobstructed.</returns>
    /// <remarks>
    /// **Valve's own test for which soundscape a listener is in.**
    /// <c>CEnvSoundscape::UpdateForPlayer</c> (<c>soundscape.cpp:271</c>) traces from the entity to
    /// the player and accepts it only when <c>tr.fraction == 1 &amp;&amp; !tr.startsolid</c>. The
    /// mask is <c>MASK_SOLID_BRUSHONLY|MASK_WATER</c> — **brushes only, no props** — which is why a
    /// leaf test suffices here and a prop-aware trace is not needed.
    ///
    /// **Sampled rather than clipped, and that is an approximation with a known failure.** A real
    /// trace splits the segment against each BSP plane; this walks it and asks which leaf each step
    /// lands in, exactly as <see cref="SeesSky"/> does. A wall thinner than the step can be tunnelled
    /// through, reporting clear when the engine would report blocked — which for a soundscape means
    /// hearing the room next door.
    ///
    /// **The step is therefore finer than the sky trace's**, 4 units against 16: Source walls are
    /// commonly 8 units and occasionally less, and the sky trace can afford to be coarse because
    /// ceilings are thick. The cost is bounded by the caller rather than here — soundscape selection
    /// runs a few times a second and stops at the first entity that qualifies, not once per frame
    /// per entity.
    ///
    /// **True when the map has no leaves**, matching <see cref="SeesSky"/>: a viewer that decided
    /// everything was blocked would report silence everywhere, which is a worse failure than
    /// occasionally hearing through a wall.
    /// </remarks>
    public bool IsClear(
        float fromX, float fromY, float fromZ,
        float toX, float toY, float toZ)
    {
        if (_leaves.IsEmpty || IsEmpty)
        {
            return true;
        }

        float dx = toX - fromX;
        float dy = toY - fromY;
        float dz = toZ - fromZ;

        float length = MathF.Sqrt((dx * dx) + (dy * dy) + (dz * dz));

        if (!float.IsFinite(length) || length <= SegmentStep)
        {
            return true;
        }

        ReadOnlySpan<byte> leaves = _leaves.Span;

        float stepX = dx / length;
        float stepY = dy / length;
        float stepZ = dz / length;

        // **Both ends are skipped, and each for its own reason.** The far end is the listener, who
        // is never inside solid; the near end is the entity, which map authors routinely place
        // flush against a surface — starting on it would report every soundscape blocked, which is
        // what `!tr.startsolid` exists to distinguish in the engine.
        for (float distance = SegmentStep; distance < length; distance += SegmentStep)
        {
            int leaf = LeafAt(
                fromX + (stepX * distance),
                fromY + (stepY * distance),
                fromZ + (stepZ * distance));

            if (leaf < 0)
            {
                continue;
            }

            int at = leaf * _leafStride;

            if (at + 4 > leaves.Length)
            {
                continue;
            }

            if ((BinaryPrimitives.ReadInt32LittleEndian(leaves[at..]) & ContentsSolid) != 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>How finely a line-of-sight segment is sampled, in world units.</summary>
    /// <remarks>
    /// Four rather than the sky trace's sixteen, because this one has to notice walls rather than
    /// ceilings. See <see cref="IsClear"/> for what a step too coarse costs.
    /// </remarks>
    private const float SegmentStep = 4f;

    /// <summary>Which visibility cluster a leaf belongs to, or −1 when it belongs to none.</summary>
    /// <param name="leaf">The leaf index, as <see cref="LeafAt"/> returns.</param>
    /// <returns>The cluster, or −1.</returns>
    /// <remarks>
    /// **`dleaf_t.cluster`, a `short` at offset 4** — after the four-byte `contents` and before the
    /// packed `area:9, flags:7`. The mins this type already reads sit at offset 8, which is the
    /// same layout confirming itself.
    ///
    /// **−1 is an ordinary answer, not an error.** `vvis` gives solid leaves no cluster, and a point
    /// inside the world's shell or outside the map lands in one — which the viewer's free camera
    /// does constantly. A caller filtering by visibility has to treat that as "no filter available
    /// here" rather than as "nothing is visible".
    /// </remarks>
    public int Cluster(int leaf)
    {
        if (leaf < 0)
        {
            return -1;
        }

        ReadOnlySpan<byte> leaves = _leaves.Span;
        int at = leaf * _leafStride;

        if (at < 0 || at + _leafStride > leaves.Length)
        {
            return -1;
        }

        return BinaryPrimitives.ReadInt16LittleEndian(leaves[(at + 4)..]);
    }

    /// <summary>Which visibility cluster contains a point, or −1.</summary>
    /// <param name="x">World position.</param>
    /// <param name="y">World position.</param>
    /// <param name="z">World position.</param>
    /// <returns>The cluster, or −1 when the point is in solid space or the map has no tree.</returns>
    public int ClusterAt(float x, float y, float z) => Cluster(LeafAt(x, y, z));

    /// <summary>The world-space box a leaf occupies, or null when there is no such leaf.</summary>
    /// <param name="leaf">The leaf index, as <see cref="LeafAt"/> returns.</param>
    /// <returns>Its minimum and maximum corner, in world units.</returns>
    /// <remarks>
    /// **Shorts, and at the same offsets in both versions of the struct**, which is the only reason
    /// one reader serves both. `bspfile.h` declares `short mins[3]` at offset 8 and `short maxs[3]`
    /// at 14 in `dleaf_t` and in `dleaf_version_0_t` alike; version 0 is larger only because it
    /// carries a `CompressedLightCube` AFTER those fields. So the stride differs and the offsets do
    /// not, and this needs no version test of its own.
    ///
    /// **A leaf's box is for frustum culling** — Valve's own comment on the field — so it is a
    /// conservative bound rather than a tight one. That is what `mat_leafvis` draws, and it is the
    /// right thing to draw: the box is what the engine tests, and a picture of a tighter shape
    /// would answer a question nobody asks.
    /// </remarks>
    public ((float X, float Y, float Z) Min, (float X, float Y, float Z) Max)? Bounds(int leaf)
    {
        if (leaf < 0)
        {
            return null;
        }

        ReadOnlySpan<byte> leaves = _leaves.Span;
        int at = leaf * _leafStride;

        if (at < 0 || at + _leafStride > leaves.Length)
        {
            return null;
        }

        static float Read(ReadOnlySpan<byte> from, int offset) =>
            BinaryPrimitives.ReadInt16LittleEndian(from[offset..]);

        return (
            (Read(leaves, at + 8), Read(leaves, at + 10), Read(leaves, at + 12)),
            (Read(leaves, at + 14), Read(leaves, at + 16), Read(leaves, at + 18)));
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
