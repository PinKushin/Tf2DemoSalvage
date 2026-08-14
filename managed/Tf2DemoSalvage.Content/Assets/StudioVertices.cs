using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>One vertex of a model.</summary>
/// <param name="X">Position, east-west, in the model's own space.</param>
/// <param name="Y">Position, north-south.</param>
/// <param name="Z">Position, vertically.</param>
/// <param name="NormalX">Surface normal, east-west.</param>
/// <param name="NormalY">Surface normal, north-south.</param>
/// <param name="NormalZ">Surface normal, vertically.</param>
/// <param name="U">Texture coordinate.</param>
/// <param name="V">Texture coordinate.</param>
/// <param name="Bones">Which bones move this vertex, up to three.</param>
/// <param name="Weights">How much each of those bones moves it; sums to one.</param>
public readonly record struct StudioVertex(
    float X, float Y, float Z,
    float NormalX, float NormalY, float NormalZ,
    float U, float V,
    (byte First, byte Second, byte Third) Bones = default,
    (float First, float Second, float Third) Weights = default);

/// <summary>
/// A model's vertices, from its <c>.vvd</c>.
/// </summary>
/// <remarks>
/// **Read from Valve's published <c>studio.h</c>**, not from a decompiler — see
/// <c>docs/findings/11-models.md</c> for the offsets and where they came from.
///
/// <code>
///   vertexFileHeader_t  64 bytes   0 id 'IDSV'  4 version  8 checksum  12 numLODs
///                                  16 numLODVertexes[8]  48 numFixups
///                                  52 fixupTableStart  56 vertexDataStart  60 tangentDataStart
///   vertexFileFixup_t   12 bytes   0 lod  4 sourceVertexID  8 numVertexes
///   mstudiovertex_t     48 bytes   0 boneWeights (float[3], char[3], byte)
///                                  16 position  28 normal  40 texCoord
/// </code>
///
/// **Two traps, both of which produce a plausible model rather than an error.**
///
/// The first is that <c>vertexDataStart</c> is a FILE offset, while every index inside a
/// <c>.mdl</c> is relative to the struct containing it. Adjacent files, opposite conventions, and
/// nothing in either marks which is which.
///
/// The second is the fixup table. When <c>numFixups</c> is non-zero the vertex array is not stored
/// in LOD order: the fixups name runs of source vertices and which LOD each belongs to, and the
/// array for a given LOD is those runs concatenated. Ignoring them yields an array of the right
/// LENGTH and the wrong contents — a model that draws, is recognisable, and has its surfaces
/// rearranged. That is why this reader applies them rather than treating them as an optimisation.
/// </remarks>
public static class StudioVertices
{
    /// <summary>'IDSV', the identifier at the front of a vertex file.</summary>
    private const int Identifier = 0x56534449;

    /// <summary>The version every Source game writes.</summary>
    private const int SupportedVersion = 4;

    private const int HeaderBytes = 64;
    private const int FixupBytes = 12;

    /// <summary>Where the three bone indices sit, after three floats of weight.</summary>
    private const int BoneIndexOffset = 12;
    private const int VertexBytes = 48;

    /// <summary>How many levels of detail a model may declare, from <c>MAX_NUM_LODS</c>.</summary>
    private const int MaximumLods = 8;

    private const int PositionOffset = 16;
    private const int NormalOffset = 28;
    private const int TexCoordOffset = 40;

    /// <summary>The most vertices this reader will build for one model.</summary>
    /// <remarks>
    /// A model from a downloaded map is untrusted input (D32). Valve's own limit is 65,536 per
    /// mesh and models run to a few tens of thousands; the ceiling is well clear of anything real
    /// and still refuses a header that asks for a gigabyte.
    /// </remarks>
    private const int MaximumVertices = 4_000_000;

    /// <summary>Reads a model's vertices at its most detailed level.</summary>
    /// <param name="file">The <c>.vvd</c>'s bytes.</param>
    /// <returns>The vertices, in the order the index data expects them.</returns>
    /// <exception cref="InvalidDataException">The file is not a readable vertex file.</exception>
    /// <remarks>
    /// LOD 0 always, because this draws a map from overhead and the lower levels exist to save
    /// work at distance the renderer is not paying anyway.
    /// </remarks>
    public static IReadOnlyList<StudioVertex> Read(ReadOnlyMemory<byte> file) => Read(file, lod: 0);

    /// <summary>Reads a model's vertices at one level of detail.</summary>
    /// <param name="file">The <c>.vvd</c>'s bytes.</param>
    /// <param name="lod">Which level, zero being the most detailed.</param>
    /// <returns>The vertices for that level.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="lod"/> is negative.</exception>
    /// <exception cref="InvalidDataException">The file is not a readable vertex file.</exception>
    public static IReadOnlyList<StudioVertex> Read(ReadOnlyMemory<byte> file, int lod)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(lod);

        ReadOnlySpan<byte> bytes = file.Span;

        if (bytes.Length < HeaderBytes)
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"A vertex file of {bytes.Length:N0} bytes is too short to hold its header."));
        }

        if (BinaryPrimitives.ReadInt32LittleEndian(bytes) != Identifier)
        {
            throw new InvalidDataException("This is not a vertex file: it does not begin 'IDSV'.");
        }

        int version = BinaryPrimitives.ReadInt32LittleEndian(bytes[4..]);

        if (version != SupportedVersion)
        {
            throw new InvalidDataException(
                $"A vertex file declares version {version}, and only {SupportedVersion} is known.");
        }

        int lods = BinaryPrimitives.ReadInt32LittleEndian(bytes[12..]);

        if (lods is < 1 or > MaximumLods)
        {
            throw new InvalidDataException(
                $"A vertex file declares {lods} levels of detail, and the format allows 1 to {MaximumLods}.");
        }

        if (lod >= lods)
        {
            return [];
        }

        int wanted = BinaryPrimitives.ReadInt32LittleEndian(bytes[(16 + (lod * sizeof(int)))..]);
        int fixups = BinaryPrimitives.ReadInt32LittleEndian(bytes[48..]);
        int fixupStart = BinaryPrimitives.ReadInt32LittleEndian(bytes[52..]);
        int vertexStart = BinaryPrimitives.ReadInt32LittleEndian(bytes[56..]);

        if (wanted is < 0 or > MaximumVertices)
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"A vertex file declares {wanted:N0} vertices at level {lod}."));
        }

        ReadOnlySpan<byte> vertices = Region(bytes, vertexStart, "vertex data");

        if (fixups <= 0)
        {
            // No fixups means the array is already in order, and the first `wanted` of it are the
            // level asked for. That is the common case for a simple prop.
            return ReadRange(vertices, 0, wanted);
        }

        return ApplyFixups(bytes, vertices, fixupStart, fixups, lod, wanted);
    }

    /// <summary>Reassembles one level's vertices from the runs the fixup table names.</summary>
    /// <remarks>
    /// A fixup applies to its own level AND to every level more detailed than it — the field names
    /// the LOWEST level the run appears in, so a run marked 2 is present in 2, 1 and 0. Reading it
    /// as an exact match drops most of a model's surface while leaving a valid, smaller one behind.
    /// </remarks>
    private static List<StudioVertex> ApplyFixups(
        ReadOnlySpan<byte> file,
        ReadOnlySpan<byte> vertices,
        int fixupStart,
        int fixups,
        int lod,
        int wanted)
    {
        if (fixups > MaximumVertices)
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture, $"A vertex file declares {fixups:N0} fixups."));
        }

        ReadOnlySpan<byte> table = Region(file, fixupStart, "fixup table");

        if ((long)fixups * FixupBytes > table.Length)
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"A vertex file declares {fixups:N0} fixups, beyond its own length."));
        }

        List<StudioVertex> assembled = new(wanted);

        for (int index = 0; index < fixups; index++)
        {
            ReadOnlySpan<byte> fixup = table.Slice(index * FixupBytes, FixupBytes);

            int fixupLod = BinaryPrimitives.ReadInt32LittleEndian(fixup);
            int source = BinaryPrimitives.ReadInt32LittleEndian(fixup[4..]);
            int count = BinaryPrimitives.ReadInt32LittleEndian(fixup[8..]);

            if (fixupLod < lod)
            {
                continue;
            }

            assembled.AddRange(ReadRange(vertices, source, count));
        }

        return assembled;
    }

    /// <summary>Reads a run of vertices, checking it lies inside the data.</summary>
    private static List<StudioVertex> ReadRange(ReadOnlySpan<byte> vertices, int first, int count)
    {
        if (first < 0 || count < 0 || (long)(first + count) * VertexBytes > vertices.Length)
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"A vertex file names vertices {first:N0} to {first + count:N0}, beyond the " +
                $"{vertices.Length / VertexBytes:N0} it holds."));
        }

        List<StudioVertex> range = new(count);

        for (int index = 0; index < count; index++)
        {
            ReadOnlySpan<byte> vertex = vertices.Slice((first + index) * VertexBytes, VertexBytes);

            // **The bone weights, which a static prop does not need and an animated model
            // cannot do without.** mstudioboneweight_t opens the vertex: three floats of weight,
            // then three bone indices as bytes, then how many of them are used.
            range.Add(new StudioVertex(
                BinaryPrimitives.ReadSingleLittleEndian(vertex[PositionOffset..]),
                BinaryPrimitives.ReadSingleLittleEndian(vertex[(PositionOffset + 4)..]),
                BinaryPrimitives.ReadSingleLittleEndian(vertex[(PositionOffset + 8)..]),
                BinaryPrimitives.ReadSingleLittleEndian(vertex[NormalOffset..]),
                BinaryPrimitives.ReadSingleLittleEndian(vertex[(NormalOffset + 4)..]),
                BinaryPrimitives.ReadSingleLittleEndian(vertex[(NormalOffset + 8)..]),
                BinaryPrimitives.ReadSingleLittleEndian(vertex[TexCoordOffset..]),
                BinaryPrimitives.ReadSingleLittleEndian(vertex[(TexCoordOffset + 4)..]),
                (vertex[BoneIndexOffset], vertex[BoneIndexOffset + 1], vertex[BoneIndexOffset + 2]),
                (
                    BinaryPrimitives.ReadSingleLittleEndian(vertex),
                    BinaryPrimitives.ReadSingleLittleEndian(vertex[4..]),
                    BinaryPrimitives.ReadSingleLittleEndian(vertex[8..]))));
        }

        return range;
    }

    /// <summary>The rest of the file from a declared start, checked to be inside it.</summary>
    private static ReadOnlySpan<byte> Region(ReadOnlySpan<byte> file, int start, string what)
    {
        if (start < HeaderBytes || start > file.Length)
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"A vertex file puts its {what} at {start:N0} of {file.Length:N0} bytes."));
        }

        return file[start..];
    }
}
