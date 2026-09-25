using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>
/// A placed prop's baked lighting, from the map's own <c>.vhv</c> files.
/// </summary>
/// <remarks>
/// **This is how the engine lights a static prop, and there is no substitute for it.** A brush face
/// carries a lightmap; a model cannot, because the same model stands in a dozen places under a
/// dozen different lights. So the compiler bakes a colour per vertex per PLACEMENT and writes it
/// into the map's embedded pakfile as <c>sp_hdr_&lt;index&gt;.vhv</c>, indexed by the prop's
/// position in the static prop lump.
///
/// Without it a prop draws at its texture's own brightness, which on a dark rock texture is a black
/// blob — measured on cp_process, where the props filled the holes left by invisible displacement
/// and then read as blobs of their own.
///
/// Layout from Valve's published <c>public/materialsystem/hardwareverts.h</c>, <c>#pragma pack(1)</c>:
///
/// <code>
///   FileHeader_t  40  0 version  4 checksum  8 vertexFlags 12 vertexSize
///                     16 vertexes 20 meshes 24 unused[4]
///   MeshHeader_t  28  0 lod  4 vertexes  8 offset 12 unused[4]
/// </code>
///
/// **<c>m_nOffset</c> is FILE-relative**, unlike the struct-relative offsets throughout the
/// <c>.mdl</c> and <c>.vtx</c>. The format mixes conventions across files with nothing marking
/// which is which, and this is the third convention in the model chain.
///
/// **The checksum must match the model's**, which is the engine's own guard: a map recompiled
/// against a changed model leaves lighting that no longer corresponds to its vertices, and applying
/// it silently would light the wrong parts of the prop.
///
/// **Colours are stored BGRA**, in the order Direct3D wanted them when this was written.
/// </remarks>
public static class StudioVertexLighting
{
    /// <summary>The version the format has carried since it was introduced.</summary>
    private const int SupportedVersion = 2;

    private const int HeaderBytes = 40;
    private const int MeshHeaderBytes = 28;

    private const int VersionOffset = 0;
    private const int ChecksumOffset = 4;
    private const int VertexSizeOffset = 12;
    private const int VertexCountOffset = 16;
    private const int MeshCountOffset = 20;

    private const int MeshLodOffset = 0;
    private const int MeshVertexCountOffset = 4;
    private const int MeshOffsetOffset = 8;

    /// <summary>The most meshes a lighting file may claim.</summary>
    /// <remarks>A map is untrusted input (D32), and this arrives from inside one.</remarks>
    private const int MaximumMeshes = 4096;

    /// <summary>Reads one placement's baked lighting.</summary>
    /// <param name="file">The <c>.vhv</c>'s bytes.</param>
    /// <param name="checksum">The model's checksum, which this must match.</param>
    /// <returns>One list per mesh, each holding a colour per vertex of that mesh.</returns>
    /// <exception cref="InvalidDataException">The file is not readable lighting for this model.</exception>
    /// <remarks>
    /// **Per mesh rather than flattened, because that is how vrad writes it.** Valve's
    /// <c>CVradStaticPropMgr::SerializeLighting</c> writes one <c>MeshHeader_t</c> per mesh per
    /// LOD, and each mesh's colours are indexed by that MESH's vertex index — the same space
    /// <c>origMeshVertID</c> lands in. Flattening them works only while every mesh's count matches
    /// the model's, and where it does not the whole run shifts and every colour after it lands on
    /// the wrong vertex.
    ///
    /// The first level of detail only, matching the geometry this project draws.
    /// </remarks>
    public static IReadOnlyList<IReadOnlyList<(byte Red, byte Green, byte Blue)>> Read(
        ReadOnlyMemory<byte> file, int checksum)
    {
        ReadOnlySpan<byte> bytes = file.Span;

        if (bytes.Length < HeaderBytes)
        {
            // Stryker disable all : the String mutator wraps the interpolated literal in a ternary
            // that cannot bind to string.Create's interpolated-string handler (CS1620), and Safe
            // Mode then drops every mutation in this method — B410.
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"A lighting file of {bytes.Length:N0} bytes is too short to hold its header."));

            // Stryker restore all
        }

        int version = BinaryPrimitives.ReadInt32LittleEndian(bytes[VersionOffset..]);

        if (version != SupportedVersion)
        {
            throw new InvalidDataException(
                $"A lighting file declares version {version}, and only {SupportedVersion} is known.");
        }

        int stamped = BinaryPrimitives.ReadInt32LittleEndian(bytes[ChecksumOffset..]);

        if (stamped != checksum)
        {
            // The engine's own guard. Lighting baked against a different build of the model would
            // light the wrong parts of it, and silently.
            throw new InvalidDataException(
                $"A lighting file's checksum {stamped} does not match the model's {checksum}.");
        }

        int vertexSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes[VertexSizeOffset..]);
        int vertices = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes[VertexCountOffset..]);

        int meshes = BinaryPrimitives.ReadInt32LittleEndian(bytes[MeshCountOffset..]);

        if (vertexSize is < 4 or > 64)
        {
            // Stryker disable all : the String mutator wraps the interpolated literal in a ternary
            // that cannot bind to string.Create's interpolated-string handler (CS1620), and Safe
            // Mode then drops every mutation in this method — B410.
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"A lighting file declares {vertexSize:N0} bytes per vertex."));

            // Stryker restore all
        }

        if (meshes is < 0 or > MaximumMeshes || vertices < 0)
        {
            // Stryker disable all : the String mutator wraps the interpolated literal in a ternary
            // that cannot bind to string.Create's interpolated-string handler (CS1620), and Safe
            // Mode then drops every mutation in this method — B410.
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"A lighting file declares {meshes:N0} meshes and {vertices:N0} vertices."));

            // Stryker restore all
        }

        if (HeaderBytes + ((long)meshes * MeshHeaderBytes) > bytes.Length)
        {
            throw new InvalidDataException("A lighting file's mesh headers do not fit inside it.");
        }

        List<IReadOnlyList<(byte Red, byte Green, byte Blue)>> byMesh = [];

        for (int mesh = 0; mesh < meshes; mesh++)
        {
            ReadOnlySpan<byte> header = bytes.Slice(
                HeaderBytes + (mesh * MeshHeaderBytes), MeshHeaderBytes);

            // **Only the level of detail being drawn.** A .vhv carries a mesh per LOD per mesh,
            // and MeshHeader_t names which - so flattening them all gives more colours than the
            // model has vertices, in an order that no longer corresponds to anything. Caught by
            // comparing the count against the model's, which is why that test exists.
            if (BinaryPrimitives.ReadUInt32LittleEndian(header[MeshLodOffset..]) != 0)
            {
                continue;
            }

            int count = (int)BinaryPrimitives.ReadUInt32LittleEndian(
                header[MeshVertexCountOffset..]);

            // File-relative, unlike everything in the .mdl and .vtx.
            int at = (int)BinaryPrimitives.ReadUInt32LittleEndian(header[MeshOffsetOffset..]);

            if (count < 0 || at < HeaderBytes ||
                (long)at + ((long)count * vertexSize) > bytes.Length)
            {
                // Stryker disable all : the String mutator wraps the interpolated literal in a
                // ternary that cannot bind to string.Create's interpolated-string handler (CS1620),
                // and Safe Mode then drops every mutation in this method — B410.
                throw new InvalidDataException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"A lighting file puts {count:N0} colours at {at:N0} of {bytes.Length:N0} bytes."));

                // Stryker restore all
            }

            List<(byte Red, byte Green, byte Blue)> colours = new(count);

            for (int index = 0; index < count; index++)
            {
                ReadOnlySpan<byte> colour = bytes.Slice(at + (index * vertexSize), vertexSize);

                // BGRA, in the order Direct3D wanted when this was written. Read as RGB and the
                // map's warm lighting comes out blue, which looks like a deliberate art choice
                // rather than a channel swap.
                colours.Add((colour[2], colour[1], colour[0]));
            }

            byMesh.Add(colours);
        }

        return byMesh;
    }

    /// <summary>Where a placement's lighting lives inside the map's pakfile: one name, or none.</summary>
    /// <param name="propIndex">The placement's position in the static prop lump.</param>
    /// <param name="mapFlags">The map's `LUMP_MAP_FLAGS` (<see cref="Bsp.BspMapFlags"/>).</param>
    /// <returns>Nothing when the map declares no bake; otherwise the one file the engine reads.</returns>
    /// <remarks>
    /// **`engine.dll` 0x1800f4760, the static prop lighting load**, gated by the flags 0x1800ffa10 reads from lump 59:
    /// no `.vhv` unless `flags &amp; 3`; `sp_hdr_%d%s.vhv` when HDR is on and the HDR bake bit is set; `sp_%d%s.vhv`
    /// otherwise. One name and no fallback — a missing file is runtime lighting, as a checksum mismatch is. TF2's
    /// default is HDR, and so is this viewer's.
    ///
    /// **This used to try LDR first, then HDR, on the claim that HDR "is authored brighter and expects a tone-mapping
    /// pass".** Measured on `koth_harvest_final` by the `vhv-pair` probe: all 652 pairs hold identical colours.
    /// </remarks>
    public static IEnumerable<string> PathsFor(int propIndex, uint mapFlags)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(propIndex);

        if ((mapFlags & (Bsp.BspMapFlags.BakedStaticPropLightingNonHdr | Bsp.BspMapFlags.BakedStaticPropLightingHdr)) == 0)
        {
            yield break;
        }

        // Stryker disable once : the String mutator wraps the interpolated literal in a ternary
        // that cannot bind to string.Create's interpolated-string handler (CS1620), and Safe Mode
        // then drops every mutation in this method — B410.
        yield return (mapFlags & Bsp.BspMapFlags.BakedStaticPropLightingHdr) != 0
            ? string.Create(CultureInfo.InvariantCulture, $"sp_hdr_{propIndex}.vhv")
            : string.Create(CultureInfo.InvariantCulture, $"sp_{propIndex}.vhv");
    }
}
