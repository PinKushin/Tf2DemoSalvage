using System;
using System.Collections.Generic;
using System.Numerics;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>One convex piece of a collision hull — Ipion's <c>IVP_Compact_Ledge</c>.</summary>
/// <param name="Points">Every point the ledge's triangles index, in IVP metres.</param>
/// <param name="Triangles">Three point indices per triangle, in the file's own order.</param>
/// <param name="EdgeOffsets">
/// **Each edge word's bits 16–30, per triangle, as a signed count of four-byte edge words** (B369) — the hop
/// `FUN_1800a1b50` takes from an edge to walk the edges around a point, relative to the edge's own address. Kept
/// as the file stores it, because the walk is address arithmetic over the ledge's triangle array.
/// </param>
/// <param name="PierceTriangles">
/// **Each triangle's header word, bits 12–23: the index of a triangle across the ledge** (B369), where the minimize's
/// backside walk `FUN_180094e30` starts. Kept as the file stores it; the walk refuses one past the ledge.
/// </param>
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
    IReadOnlyList<(int A, int B, int C)> EdgeOffsets,
    IReadOnlyList<int> PierceTriangles,
    Vector3 Center,
    float Radius);

/// <summary>The mass center and rotational inertia a hull's compact surface carries in its header (B403).</summary>
/// <param name="MassCenter">Where the hull's mass center is, in IVP metres in the solid's own frame.</param>
/// <param name="RotationInertia">
/// The hull's rotational inertia along IVP's three object axes, per unit mass — the core multiplies it by the
/// factor and the mass (<c>FUN_180073df0</c>).
/// </param>
/// <remarks>
/// **Read by the engine through the surface manager, not by a deserialiser**: its virtual `+8` copies
/// `IVP_Compact_Surface+0x00..0x08` and its `+0x18` copies `+0x0C..0x14` (`docs/findings/51`, *Which hull bytes
/// the surface manager returns*). *That the inertia is taken about the mass center is inferred* from the core
/// being placed there; the bytes say only which three numbers they are.
/// </remarks>
public readonly record struct PhysicsMassProperties(Vector3 MassCenter, Vector3 RotationInertia);

/// <summary>What vphysics' per-solid loader makes of one solid — the three ends of <c>FUN_18000a100</c> (B404).</summary>
public enum PhysicsSolidLoad
{
    /// <summary>A collide, built from the solid's compact surface.</summary>
    Collide,

    /// <summary>
    /// A NULL collide, which keeps the solid's slot: a <c>VPHY</c> solid of any type but 0, or an untagged solid
    /// whose magic is <c>MOPP</c> or one the loader does not name.
    /// </summary>
    Null,

    /// <summary><c>Error("Corrupt physics model")</c>, which stops the load: an untagged solid under 0x30 bytes.</summary>
    Corrupt,
}

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
/// a solid, after its size prefix, as FUN_18000a100 tells the two kinds apart (B404):
///   tagged     +0x00  "VPHY"
///              +0x04  short, which the loader does not read
///              +0x06  short type       0 builds; 1 "Null physics model", NULL; anything else NULL
///              +0x08  int32 dataSize   how many bytes of surface the loader copies
///              +0x0C  three floats the loader copies into the collide (meaning not decoded)
///              +0x1C  IVP_Compact_Surface
///   untagged   +0x00  IVP_Compact_Surface, when the solid is at least 0x30 bytes;
///                     under that, Error("Corrupt physics model") and the load stops
///
/// IVP_Compact_Surface, 0x30 bytes:
///   +0x20  int32 offset from the SURFACE's own base to the ledge-tree root
///   +0x2C  magic, read ONLY for an untagged solid: IVPS or SPVI builds, 0 builds as an
///          "Old format .PHY", MOPP or anything else is NULL
///
/// IVP_Compact_Ledgetree_Node:
///   +0x00  offset_right_node     0 means LEAF
///   +0x04  offset_compact_ledge  leaf only, and usually NEGATIVE — ledges precede the tree
///   +0x1C  the LEFT child, inline
///
/// IVP_Compact_Ledge:
///   +0x00  c_point_offset  ADD to the ledge's own address; the array can be SHARED
///   +0x0C  low 16 bits = n_triangles
///   +0x10  IVP_Compact_Triangle[n], sixteen bytes each: a header word whose low 12 bits are
///          the triangle's own index and bits 12–23 a triangle across the ledge, then three
///          four-byte edges whose LOW 16 BITS are the start point index
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
    /// <summary><c>VPHY</c>, little-endian — what <c>FUN_18000a100</c> compares a solid's first word against.</summary>
    private const int VphyTag = 0x59485056;

    /// <summary>Offset of a tagged solid's type word, which the loader reads signed: <c>MOVSX ECX, word ptr [RDI + 0x6]</c>.</summary>
    private const int TypeOffset = 0x06;

    /// <summary>Offset of a tagged solid's data size — the length the loader copies from <see cref="SurfaceOffset"/>.</summary>
    private const int DataSizeOffset = 0x08;

    /// <summary>Where a TAGGED solid's <c>IVP_Compact_Surface</c> begins; an untagged solid's begins at its first byte.</summary>
    private const int SurfaceOffset = 0x1C;

    /// <summary>Bytes of <c>IVP_Compact_Surface</c>, and the minimum a solid can declare.</summary>
    private const int SurfaceSize = 0x30;

    /// <summary>Offset within the surface of the mass center, which the surface manager's virtual <c>+8</c> copies.</summary>
    private const int MassCenterOffset = 0x00;

    /// <summary>Offset within the surface of the rotation inertia, which the surface manager's virtual <c>+0x18</c> copies.</summary>
    private const int RotationInertiaOffset = 0x0C;

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

    /// <summary><c>MOPP</c>, little-endian — Havok's own tree, which the loader nulls.</summary>
    private const int Mopp = 0x50504F4D;

    /// <summary>
    /// **How deep a ledge tree may go before it is treated as malformed.** Not a Valve constant:
    /// the engine's own walk is recursive with no depth guard because it trusts its input, and this
    /// reader does not. A tree of 2^64 leaves is not a shape any compiler emits.
    /// </summary>
    private const int MaximumDepth = 64;

    /// <summary>Which end of vphysics' per-solid loader one solid reaches (B404).</summary>
    /// <param name="solid">The solid's bytes, after its size prefix.</param>
    /// <returns>Whether the loader builds a collide from it, leaves its collide NULL, or refuses the file.</returns>
    /// <remarks>
    /// **Asked by the readers that walk solids, because only they can act on a refusal.** <see cref="Read(ReadOnlySpan{byte})"/>
    /// and <see cref="MassProperties"/> answer nothing for a NULL and for a corrupt solid alike — neither has a surface —
    /// so a caller that must stop the load when the engine would asks here.
    /// </remarks>
    public static PhysicsSolidLoad Load(ReadOnlySpan<byte> solid) => Locate(solid).Load;

    /// <summary>Reads the ledges of one solid.</summary>
    /// <param name="solid">The solid's bytes, after its size prefix.</param>
    /// <returns>Every convex ledge, or an empty list when there is nothing readable.</returns>
    /// <remarks>
    /// **Only from a surface the loader would build** (<see cref="Load"/>). `MOPP` on an untagged solid is refused
    /// rather than guessed at — it is Havok's own tree and a different structure entirely — while a tagged solid is
    /// read whatever its magic, because the loader never looks.
    /// </remarks>
    public static IReadOnlyList<PhysicsLedge> Read(ReadOnlySpan<byte> solid)
    {
        ReadOnlySpan<byte> surface = Surface(solid);

        if (surface.IsEmpty)
        {
            return [];
        }

        int root = BitConverter.ToInt32(surface[LedgeTreeOffset..]);

        if (root < 0 || root + NodeHeaderSize > surface.Length)
        {
            return [];
        }

        List<PhysicsLedge> ledges = [];

        Walk(surface, root, ledges, MaximumDepth);

        return ledges;
    }

    /// <summary>The same read, reporting how many leaves it could not turn into a ledge.</summary>
    /// <param name="solid">The solid's bytes, after its size prefix.</param>
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

        ReadOnlySpan<byte> surface = Surface(solid);

        if (surface.IsEmpty)
        {
            return [];
        }

        int root = BitConverter.ToInt32(surface[LedgeTreeOffset..]);

        if (root < 0 || root + NodeHeaderSize > surface.Length)
        {
            return [];
        }

        List<PhysicsLedge> ledges = [];
        int leaves = 0;

        Count(surface, root, ledges, MaximumDepth, ref leaves);

        dropped = leaves - ledges.Count;

        return ledges;
    }

    /// <summary>Reads the mass center and rotation inertia from one solid's surface header.</summary>
    /// <param name="solid">The solid's bytes, after its size prefix.</param>
    /// <returns>The mass properties, or null when there is no readable surface.</returns>
    /// <remarks>
    /// **Refused wherever <see cref="Read(ReadOnlySpan{byte})"/> refuses**: a solid whose collide the loader leaves
    /// NULL or whose file it refuses, or a surface too short to hold these six floats. Returned as IVP writes them —
    /// metres, IVP axes — for the one seam that converts.
    /// </remarks>
    public static PhysicsMassProperties? MassProperties(ReadOnlySpan<byte> solid)
    {
        ReadOnlySpan<byte> surface = Surface(solid);

        return surface.IsEmpty
            ? null
            : new PhysicsMassProperties(Triple(surface, MassCenterOffset), Triple(surface, RotationInertiaOffset));
    }

    /// <summary>The bytes the loader builds a collide from, or empty when it builds none.</summary>
    /// <remarks>
    /// **Empty too when those bytes cannot hold a surface** — a tagged data size under 0x30. The loader copies
    /// that many and reads the surface anyway, which past the copy is uninitialised memory; this reads nothing (D32).
    /// </remarks>
    private static ReadOnlySpan<byte> Surface(ReadOnlySpan<byte> solid)
    {
        (PhysicsSolidLoad load, int surface, int length) = Locate(solid);

        return load == PhysicsSolidLoad.Collide && length >= SurfaceSize
            ? solid.Slice(surface, length)
            : ReadOnlySpan<byte>.Empty;
    }

    /// <summary>Which of the loader's branches a solid takes, and where the surface it builds from lies.</summary>
    /// <returns>
    /// The outcome, and for <see cref="PhysicsSolidLoad.Collide"/> the surface's offset in the solid and the bytes
    /// the loader copies from there: a tagged solid's data size, or an untagged solid's whole length.
    /// </returns>
    /// <remarks>
    /// **Read from the disassembly of `FUN_18000a100`**, whose single-buffer twin `FUN_18000c600` takes the same
    /// branches in the same order (`docs/findings/51`, *What the loader does with a solid it cannot use*):
    ///
    /// <code>
    /// CMP   dword ptr [RDI], 0x59485056     ; "VPHY"
    /// JNZ   untagged
    /// MOVSX ECX, word ptr [RDI + 0x6]       ; 0: build from RDI + 0x1C, [RDI + 0x8] bytes
    ///                                       ; 1: DevMsg(2, "Null physics model"), NULL; else NULL
    /// untagged:
    /// CMP   R14D, 0x30                      ; the size prefix, unsigned
    /// JC    Error("Corrupt physics model")
    /// MOV   EAX, dword ptr [RDI + 0x2c]     ; MOPP: NULL. IVPS, SPVI: build from RDI.
    ///                                       ; 0: DevMsg(1, "Old format .PHY file loaded!!!"), build. else NULL
    /// </code>
    ///
    /// **Two departures, both D32.** A tagged solid too short for its own header is NULL here, where the loader reads
    /// past it; and a data size larger than the solid is cut to the solid, where the loader copies past it.
    /// </remarks>
    private static (PhysicsSolidLoad Load, int Surface, int Length) Locate(ReadOnlySpan<byte> solid)
    {
        if (solid.Length >= sizeof(int) && BitConverter.ToInt32(solid) == VphyTag)
        {
            if (solid.Length < SurfaceOffset)
            {
                return (PhysicsSolidLoad.Null, 0, 0);
            }

            // Type 1 is the `"Null physics model"` the loader announces; every type but 0 ends NULL alike.
            if (BitConverter.ToInt16(solid[TypeOffset..]) != 0)
            {
                return (PhysicsSolidLoad.Null, 0, 0);
            }

            int dataSize = BitConverter.ToInt32(solid[DataSizeOffset..]);

            return (PhysicsSolidLoad.Collide, SurfaceOffset, Math.Clamp(dataSize, 0, solid.Length - SurfaceOffset));
        }

        if (solid.Length < SurfaceSize)
        {
            return (PhysicsSolidLoad.Corrupt, 0, 0);
        }

        int magic = BitConverter.ToInt32(solid[MagicOffset..]);

        // **In the loader's order: `MOPP` is compared first.** It changes no answer here, since the fall-through
        // below nulls it too — and it is kept because it is what the engine tests, so loosening the last branch
        // cannot quietly start building Havok's format. No input tells the two apart.
        if (magic == Mopp)
        {
            return (PhysicsSolidLoad.Null, 0, 0);
        }

        return magic is Ivps or Spvi or 0
            ? (PhysicsSolidLoad.Collide, 0, solid.Length)
            : (PhysicsSolidLoad.Null, 0, 0);
    }

    /// <summary>Three little-endian floats.</summary>
    private static Vector3 Triple(ReadOnlySpan<byte> solid, int at) =>
        new(
            BitConverter.ToSingle(solid[at..]),
            BitConverter.ToSingle(solid[(at + 4)..]),
            BitConverter.ToSingle(solid[(at + 8)..]));

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
        List<(int A, int B, int C)> offsets = new(count);
        List<int> pierces = new(count);

        for (int index = 0; index < count; index++)
        {
            int triangle = ledge + LedgeHeaderSize + (index * TriangleSize);

            // The header word: the engine finds a ledge from its low twelve bits, the triangle's own index, which is
            // the triangle's position here; bits 12–23 name where the backside walk starts. The three edges follow.
            int header = BitConverter.ToInt32(solid[triangle..]);
            int first = BitConverter.ToInt32(solid[(triangle + 4)..]);
            int second = BitConverter.ToInt32(solid[(triangle + 8)..]);
            int third = BitConverter.ToInt32(solid[(triangle + 12)..]);

            int a = Point(solid, points, first & 0xFFFF, renumbered, kept);
            int b = Point(solid, points, second & 0xFFFF, renumbered, kept);
            int c = Point(solid, points, third & 0xFFFF, renumbered, kept);

            if (a < 0 || b < 0 || c < 0)
            {
                return null;
            }

            triangles.Add((a, b, c));
            offsets.Add((EdgeOffset(first), EdgeOffset(second), EdgeOffset(third)));
            pierces.Add(PierceTriangle(header));
        }

        return new PhysicsLedge(kept, triangles, offsets, pierces, centre, radius);
    }

    /// <summary>An edge word's bits 16–30, sign-extended: <c>(int)(word &lt;&lt; 1) &gt;&gt; 17</c>, as <c>FUN_1800a1b50</c> reads it.</summary>
    private static int EdgeOffset(int word) => (word << 1) >> 17;

    /// <summary>A header word's bits 12–23: <c>(header &gt;&gt; 12) &amp; 0xFFF</c>, as <c>FUN_180094e30</c> reads it.</summary>
    private static int PierceTriangle(int header) => (header >> 12) & 0xFFF;

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
