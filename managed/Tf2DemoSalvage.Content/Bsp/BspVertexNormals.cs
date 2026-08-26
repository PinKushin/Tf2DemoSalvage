using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Tf2DemoSalvage.Content.Bsp;

/// <summary>The per-vertex normals a map was compiled with, and the indices faces use.</summary>
/// <param name="Normals">Unit normals, in the order the compiler emitted them.</param>
/// <param name="Indices">One index per face vertex, into <paramref name="Normals"/>.</param>
/// <remarks>
/// **These are NOT the face plane's normal, despite what the compiler first writes.** `vbsp` fills
/// the lump with `dplanes[f->planenum].normal` and says why in a comment:
///
/// <code>
/// // Add this face plane's normal.
/// // Note: this doesn't do an exhaustive vertex normal match because the vrad does it.
/// g_vertnormals[g_numvertnormals] = dplanes[f->planenum].normal;
/// </code>
///
/// — `src/utils/vbsp/normals.cpp:38`. By the time a map ships, **vrad has replaced them** with true
/// smoothed normals wherever a smoothing group applies. So the lump and the plane agree on flat
/// unsmoothed brushwork and nowhere else, which is why deriving a normal from the plane is not a
/// substitute for reading this.
/// </remarks>
public readonly record struct VertexNormals(
    IReadOnlyList<(float X, float Y, float Z)> Normals,
    IReadOnlyList<int> Indices);

/// <summary>Reads the vertex-normal lumps.</summary>
/// <remarks>
/// **Read but not yet drawn, and that is deliberate (D93).** Nothing consumes these today: the world
/// pass is lit by the map's baked lightmaps, and Valve's own bumped path takes its normal from the
/// bump map against `g_localBumpBasis` rather than from a vertex. They are read because decoding is
/// total and rendering is not — the file is understood now, and the consumer arrives with the
/// feature that needs it (per-pixel world lighting, specular, or a tangent basis for `$bumpmap` on
/// brushwork). See B194.
/// </remarks>
public static class BspVertexNormals
{
    /// <summary>Bytes per normal: three floats.</summary>
    private const int NormalStride = 12;

    /// <summary>Bytes per index: <c>unsigned short</c>.</summary>
    private const int IndexStride = 2;

    /// <summary>Reads both lumps.</summary>
    /// <param name="file">The whole map file.</param>
    /// <returns>The normals and the indices; both empty when the map carries none.</returns>
    /// <exception cref="InvalidDataException">The header or a lump is malformed.</exception>
    /// <remarks>
    /// **Empty rather than throwing when the lumps are absent**, because they legitimately can be:
    /// a map compiled without running vrad has no smoothed normals to store, and every other reader
    /// here treats an absent lump the same way.
    /// </remarks>
    public static VertexNormals Read(ReadOnlyMemory<byte> file)
    {
        BspHeader header = BspHeader.Parse(file.Span);

        ReadOnlySpan<byte> normals =
            BspLumpData.Read(file, header.Lump(BspLumpIndex.VertNormals)).Span;

        ReadOnlySpan<byte> indices =
            BspLumpData.Read(file, header.Lump(BspLumpIndex.VertNormalIndices)).Span;

        List<(float X, float Y, float Z)> read = new(normals.Length / NormalStride);

        for (int at = 0; at + NormalStride <= normals.Length; at += NormalStride)
        {
            read.Add((
                BinaryPrimitives.ReadSingleLittleEndian(normals[at..]),
                BinaryPrimitives.ReadSingleLittleEndian(normals[(at + 4)..]),
                BinaryPrimitives.ReadSingleLittleEndian(normals[(at + 8)..])));
        }

        List<int> referenced = new(indices.Length / IndexStride);

        for (int at = 0; at + IndexStride <= indices.Length; at += IndexStride)
        {
            referenced.Add(BinaryPrimitives.ReadUInt16LittleEndian(indices[at..]));
        }

        return new VertexNormals(read, referenced);
    }
}
