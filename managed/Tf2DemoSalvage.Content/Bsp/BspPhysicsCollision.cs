using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Bsp;

/// <summary>Where one collision hull lives inside the physics lump.</summary>
/// <param name="Offset">Its first byte, from the start of the lump.</param>
/// <param name="Length">How many bytes it occupies, excluding its own length prefix.</param>
/// <remarks>
/// **An extent rather than the bytes, because the bytes are not readable yet.** A hull is Havok's
/// closed `IVPS` compact-ledge format — the same one a model's `.phy` carries and this project
/// already skips. Recording where each one sits costs nothing and is what a later reader will need;
/// copying them would be pretending to understand them.
/// </remarks>
public readonly record struct PhysicsBrushSolid(int Offset, int Length);

/// <summary>One brush model's baked physics collision.</summary>
/// <param name="ModelIndex">
/// Which brush model this belongs to. **Zero is the world**; the rest are `func_` brush entities,
/// named `*1`, `*2` and so on in the entity lump.
/// </param>
/// <param name="SolidCount">How many hulls the header declares.</param>
/// <param name="Solids">Where each of those hulls sits in the lump.</param>
/// <param name="Text">The KeyValues half, which is plain ASCII and fully readable.</param>
public sealed record MapPhysicsModel(
    int ModelIndex,
    int SolidCount,
    IReadOnlyList<PhysicsBrushSolid> Solids,
    string Text);

/// <summary>
/// The map's baked physics collision — <c>LUMP_PHYSCOLLIDE</c> (B58, B369).
/// </summary>
/// <remarks>
/// **This is what a corpse lands on.** A TF2 ragdoll is simulated by the client against the map's
/// own collision, which the compiler bakes into lump 29 (`bspfile.h:310`) and the engine feeds to
/// `CreatePolyObjectStatic` at level load (`physics_shared.cpp:602-667`).
///
/// **The layout is Valve's, taken from the loader rather than guessed** (`bsplib.cpp:1577-1600`):
///
/// <code>
/// // physics data is variable length.  The last physmodel is a NULL pointer
/// // with modelIndex -1, dataSize -1
/// struct dphysmodel_t { int modelIndex; int dataSize; int keydataSize; int solidCount; };
/// </code>
///
/// Each entry is a header, then `dataSize` bytes holding `solidCount` length-prefixed hulls, then
/// `keydataSize` bytes of KeyValues text.
///
/// **It splits exactly the way a model's `.phy` does**, which is the useful part: the hulls are the
/// closed `IVPS` format this project already skips there, and beside them is plain text carrying
/// contents flags and a surface-material table. **One closed format, needed in two places.**
///
/// **Measured over every installed map: 234 of 234 yield at least one model**, which is the control
/// that must hold because every compiled map has a world brush model with collision.
/// </remarks>
public static class BspPhysicsCollision
{
    /// <summary>Bytes of <c>dphysmodel_t</c> — four ints.</summary>
    private const int ModelHeaderSize = 16;

    /// <summary>Bytes of a hull's own length prefix.</summary>
    private const int SolidPrefixSize = 4;

    /// <summary>Reads every brush model's collision out of the lump.</summary>
    /// <param name="lump">The lump's bytes, already decompressed.</param>
    /// <returns>One entry per brush model, in file order; empty when the lump is absent.</returns>
    /// <remarks>
    /// **Every length is checked against the lump before it is used**, because a `.bsp` is a
    /// stranger's file (D32): maps arrive from fastdl, supplied by whoever runs the server and
    /// reviewed by nobody, and this walk is driven entirely by sizes the file itself declares. A
    /// declaration that overruns stops the walk and keeps what was read, rather than throwing —
    /// a truncated map should draw what it has, the way the rest of this reader treats damage.
    ///
    /// **The terminator is tested before the sizes are trusted for anything.** Valve's final entry
    /// carries `modelIndex -1, dataSize -1`, and a reader that missed it would take that entry's
    /// own fields as lengths.
    /// </remarks>
    public static IReadOnlyList<MapPhysicsModel> Read(ReadOnlyMemory<byte> lump)
    {
        ReadOnlySpan<byte> bytes = lump.Span;

        List<MapPhysicsModel> models = [];

        int at = 0;

        while (at + ModelHeaderSize <= bytes.Length)
        {
            int modelIndex = BinaryPrimitives.ReadInt32LittleEndian(bytes[at..]);
            int dataSize = BinaryPrimitives.ReadInt32LittleEndian(bytes[(at + 4)..]);
            int keydataSize = BinaryPrimitives.ReadInt32LittleEndian(bytes[(at + 8)..]);
            int solidCount = BinaryPrimitives.ReadInt32LittleEndian(bytes[(at + 12)..]);

            at += ModelHeaderSize;

            // **Valve's terminator, and it is currently EQUIVALENT to the bounds guard below.**
            // Sabotage confirmed it: breaking this test reddens nothing, because a terminator
            // declares `dataSize -1` and the `dataSize < 0` check rejects it anyway. No input
            // distinguishes the two — a terminator written with a non-negative `dataSize` would be
            // read as a model by both readings alike.
            //
            // **Kept because it is what the engine tests**, not because it is load-bearing today. It
            // becomes load-bearing the moment the bounds guard is loosened or reordered, and a
            // reader that had only the guard would be relying on an accident of Valve's chosen
            // sentinel value.
            if (modelIndex == -1 && dataSize == -1)
            {
                break;
            }

            if (dataSize < 0 || keydataSize < 0 || solidCount < 0 ||
                (long)at + dataSize + keydataSize > bytes.Length)
            {
                break;
            }

            models.Add(new MapPhysicsModel(
                modelIndex,
                solidCount,
                Hulls(bytes, at, dataSize, solidCount),
                Encoding.ASCII.GetString(bytes.Slice(at + dataSize, keydataSize))));

            at += dataSize + keydataSize;
        }

        return models;
    }

    /// <summary>Finds each length-prefixed hull inside one model's data block.</summary>
    /// <param name="lump">The whole lump.</param>
    /// <param name="at">Where this model's data begins.</param>
    /// <param name="dataSize">How long that data is.</param>
    /// <param name="solidCount">How many hulls the header declares.</param>
    /// <returns>The extents, which may be fewer than declared on a damaged map.</returns>
    /// <remarks>
    /// **The declared count is a bound, not a promise.** Valve's own loop reads `solidCount` sizes
    /// and trusts them; here the running offset is checked against the block as well, so a count
    /// that disagrees with the bytes yields what is actually there.
    /// </remarks>
    private static List<PhysicsBrushSolid> Hulls(
        ReadOnlySpan<byte> lump, int at, int dataSize, int solidCount)
    {
        List<PhysicsBrushSolid> solids = [];

        int end = at + dataSize;
        int cursor = at;

        for (int solid = 0; solid < solidCount; solid++)
        {
            if (cursor + SolidPrefixSize > end)
            {
                break;
            }

            int size = BinaryPrimitives.ReadInt32LittleEndian(lump[cursor..]);

            cursor += SolidPrefixSize;

            if (size <= 0 || cursor + size > end)
            {
                break;
            }

            solids.Add(new PhysicsBrushSolid(cursor, size));

            cursor += size;
        }

        return solids;
    }
}

/// <summary>
/// What a brush model's collision text declares — its solids and its surface materials.
/// </summary>
/// <param name="StaticSolids">Each hull's index and its <c>BSPFlags.h</c> contents mask.</param>
/// <param name="Materials">
/// **Keyed by the INDEX a hull references, giving the surface property NAME** — that direction and
/// not the other, which real data settled. `koth_harvest_final` maps both slot 2 and slot 8 to
/// `default`, so a name-keyed table loses one of them; slots are unique and names are not, because
/// the table exists to resolve what a hull stores.
///
/// **This is what says whether a corpse landed on wood or metal**, which decides both the friction
/// it settles with and the impact sound the engine would play.
/// </param>
/// <param name="HasVirtualTerrain">
/// Whether the model carries a <c>virtualterrain</c> block. **An empty block whose presence is the
/// fact** — it has no keys at all, so a reader gathering only key/value pairs loses it.
/// </param>
public sealed record MapSurfaceTable(
    IReadOnlyList<(int Index, int Contents)> StaticSolids,
    IReadOnlyDictionary<int, string> Materials,
    bool HasVirtualTerrain)
{
    /// <summary>Parses one brush model's collision text.</summary>
    /// <param name="text">The KeyValues half of a <see cref="MapPhysicsModel"/>.</param>
    /// <returns>What it declares.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
    /// <remarks>
    /// **A separate parser from a `.phy`'s, because the block names are different.** A model's
    /// `.phy` opens `solid` and `ragdollconstraint`; a map's collision opens `staticsolid`,
    /// `virtualterrain` and `materialtable`. Sharing one reader would mean one of them silently
    /// accepting the other's vocabulary.
    /// </remarks>
    public static MapSurfaceTable Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        List<(int Index, int Contents)> solids = [];
        Dictionary<int, string> materials = [];

        bool virtualTerrain = false;

        string block = string.Empty;

        int index = 0;
        int contents = 0;
        bool hasIndex = false;

        void Close()
        {
            if (hasIndex && block.Equals("staticsolid", StringComparison.OrdinalIgnoreCase))
            {
                solids.Add((index, contents));
            }

            block = string.Empty;
            index = 0;
            contents = 0;
            hasIndex = false;
        }

        KeyValuesReader.Read(Encoding.ASCII.GetBytes(text), (key, value, depth) =>
        {
            if (value is null)
            {
                Close();

                block = key;

                // **`virtualterrain` carries no keys**, so it is noticed when it OPENS or it is not
                // noticed at all.
                if (key.Equals("virtualterrain", StringComparison.OrdinalIgnoreCase))
                {
                    virtualTerrain = true;
                }

                return true;
            }

            if (depth <= 0)
            {
                return true;
            }

            if (block.Equals("materialtable", StringComparison.OrdinalIgnoreCase))
            {
                // **Slot to name, because a name repeats and a slot does not.** `koth_harvest_final`
                // writes `"default" "2"` and `"default" "8"`; keyed the other way round, the first
                // of those disappears.
                if (int.TryParse(
                    value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int slot))
                {
                    materials[slot] = key;
                }
            }
            else if (block.Equals("staticsolid", StringComparison.OrdinalIgnoreCase))
            {
                if (key.Equals("index", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(
                        value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int read))
                {
                    index = read;
                    hasIndex = true;
                }
                else if (key.Equals("contents", StringComparison.OrdinalIgnoreCase) &&
                         int.TryParse(
                             value,
                             NumberStyles.Integer,
                             CultureInfo.InvariantCulture,
                             out int mask))
                {
                    contents = mask;
                }
            }

            return true;
        });

        // **The last block has no successor to close it**, the same trap a `.phy` has.
        Close();

        return new MapSurfaceTable(solids, materials, virtualTerrain);
    }
}
