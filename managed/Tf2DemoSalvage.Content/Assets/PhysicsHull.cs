using System;
using System.Collections.Generic;
using System.Numerics;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>One convex piece of a collision hull — Ipion's <c>IVP_Compact_Ledge</c>.</summary>
/// <param name="Points">Every point the ledge's triangles index, in IVP metres.</param>
/// <param name="Triangles">Three point indices per triangle, in the file's own order.</param>
/// <remarks>
/// **A ledge is the convex unit the engine's narrow phase works on**, not the whole solid: a
/// concave shape is a TREE of them, and the point array can be SHARED between siblings — every one
/// of `ladder001`'s ten ledges indexes the same array. They are kept separate here because the
/// collision test is per convex piece.
/// </remarks>
/// <param name="Center">The centre of the bounding sphere its tree node carries.</param>
/// <param name="Radius">That sphere's radius, in the same IVP metres as the points.</param>
public readonly record struct PhysicsLedge(
    IReadOnlyList<Vector3> Points,
    IReadOnlyList<(int A, int B, int C)> Triangles,
    Vector3 Center,
    float Radius);

/// <summary>
/// The collision hull inside a <c>.phy</c> solid or a map's <c>LUMP_PHYSCOLLIDE</c> entry (B58).
/// </summary>
/// <remarks>
/// **The format is Havok/Ipion's `IVPS` compact-ledge structure, and this project used to skip
/// it** — `PhysicsModel` finds its KeyValues by scanning for a block name precisely so it would
/// never have to understand these bytes. That was correct while nothing collided; it is what stood
/// between a corpse and the floor.
///
/// **Every offset below is read from the decompiled deserialiser and then verified against shipped
/// files**, which is the part that makes it a measurement — the full account, including the
/// readings that were wrong, is `docs/findings/51-vphysics-is-ivp-and-it-is-readable.md` under
/// *The collision hull format*. The chain is `FUN_18000a100` → `FUN_18000c600` → `FUN_18000bcf0`
/// → `FUN_18000c1c0` in `vphysics.dll`.
///
/// <code>
/// solid blob, from its "VPHY" tag:
///   +0x00  "VPHY"
///   +0x04  short type       0 normal, 1 "Null physics model"
///   +0x08  int32 dataSize   guarded by `if (param_2 &lt; 0x30) Error("Corrupt physics model")`
///   +0x1C  IVP_Compact_Surface
///
/// IVP_Compact_Surface, 0x30 bytes:
///   +0x20  int32 offset from the SURFACE's own base to the ledge-tree root
///   +0x2C  magic: IVPS, SPVI (byte-swapped), MOPP (refused), or 0 (an old .phy, loaded anyway)
///
/// IVP_Compact_Ledgetree_Node:
///   +0x00  offset_right_node     0 means LEAF
///   +0x04  offset_compact_ledge  leaf only, and usually NEGATIVE — ledges precede the tree
///   +0x1C  the LEFT child, inline
///
/// IVP_Compact_Ledge:
///   +0x00  c_point_offset  ADD to the ledge's own address; the array can be SHARED
///   +0x0C  low 16 bits = n_triangles
///   +0x10  IVP_Compact_Triangle[n], sixteen bytes each: a header word nothing reads,
///          then three four-byte edges whose LOW 16 BITS are the start point index
/// </code>
///
/// **Triangles start at `+0x10` and the decompiled bound says `+0x14`.** The validator's scan
/// pointer begins one word into triangle 0 because it only bounds-checks the three edge words —
/// the FILES settled it against a plausible misreading of the code, and `ladder001`'s ten ledges
/// stepping by exactly 208 bytes (16 + 12 × 16) is the arithmetic that proves it.
///
/// **Points are in IVP METRES, and this reader does not convert.** `CPhysicsEnvironment` converts
/// at its own boundary, and this project's simulation deliberately runs in Source units — so the
/// conversion belongs at the one seam that knows both, not smuggled in here where it would be
/// invisible ([[ivp-is-a-third-convention]]).
///
/// **A `.phy` is a stranger's file (D32)**, so every offset is bounds-checked against the blob and
/// a structure that walks outside it yields nothing rather than throwing on arithmetic.
/// </remarks>
public static class PhysicsHull
{
    /// <summary>Where <c>IVP_Compact_Surface</c> begins, from the solid's <c>VPHY</c> tag.</summary>
    private const int SurfaceOffset = 0x1C;

    /// <summary>Bytes of <c>IVP_Compact_Surface</c>, and the minimum a solid can declare.</summary>
    private const int SurfaceSize = 0x30;

    /// <summary>Offset within the surface of the ledge tree's root offset.</summary>
    private const int LedgeTreeOffset = 0x20;

    /// <summary>Offset within the surface of the format magic.</summary>
    private const int MagicOffset = 0x2C;

    /// <summary>A ledge-tree node's header, after which its LEFT child sits inline.</summary>
    private const int NodeHeaderSize = 0x1C;

    /// <summary>A ledge's header, after which its triangles sit.</summary>
    private const int LedgeHeaderSize = 0x10;

    /// <summary>Bytes of one <c>IVP_Compact_Triangle</c> — a header word and three edges.</summary>
    private const int TriangleSize = 0x10;

    /// <summary>Bytes of one point: three floats and four that were zero in every sample.</summary>
    private const int PointStride = 0x10;

    /// <summary><c>IVPS</c>, little-endian.</summary>
    private const int Ivps = 0x53505649;

    /// <summary><c>SPVI</c> — the same thing, byte-swapped.</summary>
    private const int Spvi = 0x49565053;

    /// <summary>
    /// **How deep a ledge tree may go before it is treated as malformed.** Not a Valve constant:
    /// the engine's own walk is recursive with no depth guard because it trusts its input, and this
    /// reader does not. A tree of 2^64 leaves is not a shape any compiler emits.
    /// </summary>
    private const int MaximumDepth = 64;

    /// <summary>Reads the ledges of one solid.</summary>
    /// <param name="solid">The solid's bytes, starting at its <c>VPHY</c> tag.</param>
    /// <returns>Every convex ledge, or an empty list when there is nothing readable.</returns>
    /// <remarks>
    /// **`MOPP` is refused rather than guessed at.** It is Havok's own tree format and a different
    /// structure entirely; the deserialiser rejects it too. A magic of `0` is an old `.phy` and the
    /// engine loads it anyway, so this does as well.
    /// </remarks>
    public static IReadOnlyList<PhysicsLedge> Read(ReadOnlySpan<byte> solid)
    {
        if (solid.Length < SurfaceOffset + SurfaceSize)
        {
            return [];
        }

        int surface = SurfaceOffset;
        int magic = BitConverter.ToInt32(solid[(surface + MagicOffset)..]);

        if (magic is not (Ivps or Spvi or 0))
        {
            return [];
        }

        int root = surface + BitConverter.ToInt32(solid[(surface + LedgeTreeOffset)..]);

        if (root < 0 || root + NodeHeaderSize > solid.Length)
        {
            return [];
        }

        List<PhysicsLedge> ledges = [];

        Walk(solid, root, ledges, MaximumDepth);

        return ledges;
    }

    /// <summary>The same read, reporting how many leaves it could not turn into a ledge.</summary>
    /// <param name="solid">The solid's bytes, from <c>VPHY</c> onward.</param>
    /// <param name="dropped">How many tree LEAVES produced no ledge.</param>
    /// <returns>The ledges the tree yields.</returns>
    /// <remarks>
    /// **A dropped leaf is silent otherwise, and a silent drop here is a hole in the floor.** The
    /// tree says a ledge is there; <see cref="ReadLedge"/> answers null on a bad offset, an
    /// impossible triangle count or a point index outside the blob, and <see cref="Walk"/> then
    /// skips it without a word. The result is collision that is missing in scattered places while
    /// every count still looks plausible — which is exactly how a corpse came to fall through a
    /// floor the map plainly has (B369).
    ///
    /// **So the number is reported rather than inferred**, per B243: a caller comparing ledge
    /// counts against some other route would be deriving this by a second path, and the second path
    /// is free to be wrong.
    /// </remarks>
    public static IReadOnlyList<PhysicsLedge> Read(ReadOnlySpan<byte> solid, out int dropped)
    {
        dropped = 0;

        if (solid.Length < SurfaceOffset + SurfaceSize)
        {
            return [];
        }

        int surface = SurfaceOffset;

        if (BitConverter.ToInt32(solid[(surface + MagicOffset)..]) is not (Ivps or Spvi or 0))
        {
            return [];
        }

        int root = surface + BitConverter.ToInt32(solid[(surface + LedgeTreeOffset)..]);

        if (root < 0 || root + NodeHeaderSize > solid.Length)
        {
            return [];
        }

        List<PhysicsLedge> ledges = [];
        int leaves = 0;

        Count(solid, root, ledges, MaximumDepth, ref leaves);

        dropped = leaves - ledges.Count;

        return ledges;
    }

    /// <summary>Walks the tree exactly as <see cref="Walk"/> does, counting the leaves it meets.</summary>
    private static void Count(
        ReadOnlySpan<byte> solid, int node, List<PhysicsLedge> into, int budget, ref int leaves)
    {
        if (budget <= 0 || node < 0 || node + NodeHeaderSize > solid.Length)
        {
            return;
        }

        int right = BitConverter.ToInt32(solid[node..]);

        if (right == 0)
        {
            leaves++;

            int ledge = node + BitConverter.ToInt32(solid[(node + 4)..]);

            Vector3 centre = new(
                BitConverter.ToSingle(solid[(node + 0x08)..]),
                BitConverter.ToSingle(solid[(node + 0x0C)..]),
                BitConverter.ToSingle(solid[(node + 0x10)..]));

            if (ReadLedge(solid, ledge, centre, BitConverter.ToSingle(solid[(node + 0x14)..]))
                is { } read)
            {
                into.Add(read);
            }

            return;
        }

        Count(solid, node + NodeHeaderSize, into, budget - 1, ref leaves);
        Count(solid, node + right, into, budget - 1, ref leaves);
    }

    /// <summary>Walks one ledge-tree node, collecting the ledges beneath it.</summary>
    /// <remarks>
    /// **A leaf is `offset_right_node == 0`**, and its ledge offset is relative to the node and
    /// usually NEGATIVE, because the compiler writes the ledges before the tree that indexes them.
    /// The left child is INLINE at `+0x1C` rather than pointed to, which is what makes the node
    /// header's size load-bearing.
    /// </remarks>
    private static void Walk(
        ReadOnlySpan<byte> solid, int node, List<PhysicsLedge> into, int budget)
    {
        if (budget <= 0 || node < 0 || node + NodeHeaderSize > solid.Length)
        {
            return;
        }

        int right = BitConverter.ToInt32(solid[node..]);

        if (right == 0)
        {
            int ledge = node + BitConverter.ToInt32(solid[(node + 4)..]);

            // **The node's own bounding sphere, at `+0x08` centre and `+0x14` radius.** Twenty of
            // these bytes were filed as "plausibly a bounding volume, unconfirmed"; the files
            // settled it — see `docs/findings/51`, where the containment census is recorded.
            Vector3 centre = new(
                BitConverter.ToSingle(solid[(node + 0x08)..]),
                BitConverter.ToSingle(solid[(node + 0x0C)..]),
                BitConverter.ToSingle(solid[(node + 0x10)..]));

            float radius = BitConverter.ToSingle(solid[(node + 0x14)..]);

            if (ReadLedge(solid, ledge, centre, radius) is { } read)
            {
                into.Add(read);
            }

            return;
        }

        Walk(solid, node + NodeHeaderSize, into, budget - 1);
        Walk(solid, node + right, into, budget - 1);
    }

    /// <summary>Reads one ledge's triangles and the points they index.</summary>
    /// <remarks>
    /// **Only the points this ledge actually indexes are kept**, and they are renumbered. The array
    /// is shared between siblings, so carrying it whole would hand every ledge of `ladder001` the
    /// same several hundred points and make a convex test over one of them wrong as well as slow.
    /// </remarks>
    private static PhysicsLedge? ReadLedge(
        ReadOnlySpan<byte> solid, int ledge, Vector3 centre, float radius)
    {
        if (ledge < 0 || ledge + LedgeHeaderSize > solid.Length)
        {
            return null;
        }

        int points = ledge + BitConverter.ToInt32(solid[ledge..]);
        int count = BitConverter.ToInt32(solid[(ledge + 0x0C)..]) & 0xFFFF;

        if (count <= 0 || points < 0 ||
            ledge + LedgeHeaderSize + (count * TriangleSize) > solid.Length)
        {
            return null;
        }

        Dictionary<int, int> renumbered = [];
        List<Vector3> kept = [];
        List<(int A, int B, int C)> triangles = new(count);

        for (int index = 0; index < count; index++)
        {
            int triangle = ledge + LedgeHeaderSize + (index * TriangleSize);

            // **The triangle's own header word is skipped and never read**, which is not an
            // omission: nothing in the traced mindist path reads it either. The three edges follow.
            int a = Point(solid, points, BitConverter.ToInt32(solid[(triangle + 4)..]) & 0xFFFF, renumbered, kept);
            int b = Point(solid, points, BitConverter.ToInt32(solid[(triangle + 8)..]) & 0xFFFF, renumbered, kept);
            int c = Point(solid, points, BitConverter.ToInt32(solid[(triangle + 12)..]) & 0xFFFF, renumbered, kept);

            if (a < 0 || b < 0 || c < 0)
            {
                return null;
            }

            triangles.Add((a, b, c));
        }

        return new PhysicsLedge(kept, triangles, centre, radius);
    }

    /// <summary>One point, read and renumbered, or -1 when it lies outside the blob.</summary>
    private static int Point(
        ReadOnlySpan<byte> solid,
        int array,
        int index,
        Dictionary<int, int> renumbered,
        List<Vector3> kept)
    {
        if (renumbered.TryGetValue(index, out int already))
        {
            return already;
        }

        int at = array + (index * PointStride);

        if (at < 0 || at + 12 > solid.Length)
        {
            return -1;
        }

        kept.Add(new Vector3(
            BitConverter.ToSingle(solid[at..]),
            BitConverter.ToSingle(solid[(at + 4)..]),
            BitConverter.ToSingle(solid[(at + 8)..])));

        renumbered[index] = kept.Count - 1;

        return kept.Count - 1;
    }
}
