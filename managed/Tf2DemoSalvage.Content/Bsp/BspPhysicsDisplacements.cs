using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Tf2DemoSalvage.Content.Bsp;

/// <summary>
/// <c>LUMP_PHYSDISP</c> (28): each displacement's packed hull, the <c>pHull</c> the engine's virtual-mesh handler hands vphysics (B369).
/// </summary>
/// <remarks>
/// **Read from the decompiled `engine.dll`**, `FUN_18016f6d0` (`docs/findings/51`):
/// <code>
/// u16 count  — must equal the displacement count, else Error("LevelInit: Bad map data - …")
/// u16 size[count]  — 0xffff → offset −1 (no hull), else the running sum
/// the blobs, back to back
/// </code>
/// The blob format is <c>PhysicsVirtualMesh</c>'s. **A map is a stranger's file (D32)**, so a size that overruns the lump is refused
/// rather than trusted.
/// </remarks>
public static class BspPhysicsDisplacements
{
    /// <summary>Reads the lump.</summary>
    /// <param name="lump">The lump's bytes, decompressed; empty when the map has none.</param>
    /// <param name="displacements">The map's displacement count.</param>
    /// <returns>One blob per displacement, null for none; empty for an empty lump.</returns>
    /// <exception cref="InvalidDataException">The count disagrees with the map, or a size overruns the lump.</exception>
    public static IReadOnlyList<byte[]?> Read(ReadOnlyMemory<byte> lump, int displacements)
    {
        ReadOnlySpan<byte> bytes = lump.Span;

        if (bytes.IsEmpty)
        {
            return [];
        }

        int count = BinaryPrimitives.ReadUInt16LittleEndian(bytes);

        if (count != displacements)
        {
            throw new InvalidDataException("LevelInit: Bad map data - displacement data does not match displacement collision data");
        }

        int at = 2 + (2 * count);

        if (at > bytes.Length)
        {
            throw new InvalidDataException("The displacement collision sizes run past the lump.");
        }

        List<byte[]?> blobs = new(count);

        for (int index = 0; index < count; index++)
        {
            int size = BinaryPrimitives.ReadUInt16LittleEndian(bytes[(2 + (2 * index))..]);

            if (size == 0xffff)
            {
                blobs.Add(null);
                continue;
            }

            if (at + size > bytes.Length)
            {
                throw new InvalidDataException($"Displacement {index}'s collision runs past the lump.");
            }

            blobs.Add(bytes.Slice(at, size).ToArray());
            at += size;
        }

        return blobs;
    }
}
