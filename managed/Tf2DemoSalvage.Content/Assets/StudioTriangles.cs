using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>
/// A model's triangles, from its <c>.dx90.vtx</c>.
/// </summary>
/// <remarks>
/// **The third file, and the one that says which vertices form surfaces.** The <c>.vvd</c> holds
/// positions and the <c>.mdl</c> says which runs belong to which material; this holds the indices.
///
/// Layout from Valve's published <c>optimize.h</c> — note the name, since the header is often
/// called <c>optimized_model.h</c> and under that name it is not in <c>source-sdk-2013</c> at all.
/// Everything is <c>#pragma pack(1)</c>, so the sizes are the sums of their fields with no padding:
///
/// <code>
///   FileHeader_t        36  0 version  4 vertCacheSize  8 maxBonesPerStrip 10 maxBonesPerTri
///                           12 maxBonesPerVert  16 checkSum  20 numLODs
///                           24 materialReplacementListOffset  28 numBodyParts  32 bodyPartOffset
///   BodyPartHeader_t     8  0 numModels  4 modelOffset
///   ModelHeader_t        8  0 numLODs  4 lodOffset
///   ModelLODHeader_t    12  0 numMeshes  4 meshOffset  8 switchPoint
///   MeshHeader_t         9  0 numStripGroups  4 stripGroupHeaderOffset  8 flags
///   StripGroupHeader_t  25  0 numVerts  4 vertOffset  8 numIndices 12 indexOffset
///                           16 numStrips 20 stripOffset 24 flags
///   StripHeader_t       27  0 numIndices  4 indexOffset  8 numVerts 12 vertOffset
///                           16 numBones 18 flags 19 numBoneStateChanges 23 boneStateChangeOffset
///   Vertex_t             9  0 boneWeightIndex[3]  3 numBones  4 origMeshVertID  6 boneID[3]
/// </code>
///
/// **Every offset is relative to the struct that contains it**, the same convention as the
/// <c>.mdl</c> and the opposite of the <c>.vvd</c>'s data starts.
///
/// **A strip is a list or a strip, and the flag decides.** A triangle list is three indices per
/// triangle; a triangle strip shares two vertices with the previous triangle and alternates winding
/// every other one. Drawing a strip as a list produces a third of the triangles, scattered — a
/// model that is recognisably itself with holes through it, which is why the flag is read rather
/// than assumed.
///
/// **The structure sizes grew in later Source games** (CS:GO adds topology fields to the strip and
/// strip-group headers). Rather than key off a version table, this reader tries the classic layout
/// and checks whether the offsets it produces actually lie inside the data, falling back to the
/// larger one only when they do not. That is enumeration of two known layouts settled by
/// measurement, not a guess with a fallback: a wrong stride does not produce coherent offsets.
/// </remarks>
/// <summary>One triangle corner of a model.</summary>
/// <param name="Vertex">Index into the mesh's vertices, for position and texture coordinates.</param>
/// <param name="LightingGroup">Which strip group it belongs to, counted across the whole model.</param>
/// <param name="LightingVertex">Index into that strip group's own vertices, for baked colour.</param>
/// <remarks>
/// **Two indices, because a model stores position and lighting in different orders.** A strip
/// group's vertex table holds <c>origMeshVertID</c>, which addresses the mesh's vertices in the
/// <c>.vvd</c> - that is where a position comes from. But vrad writes a placement's baked colours
/// indexed by the STRIP GROUP's own vertex number, with one <c>.vhv</c> mesh header per strip
/// group rather than per mesh:
///
/// <code>
///   m_VertexColors.AddMultipleToTail( pStripGroup-&gt;numVerts );
///   int nIndex = pMesh-&gt;vertexoffset + pStripGroup-&gt;pVertex( nVertex )-&gt;origMeshVertID;
///   m_VertexColors[nVertex] = (*colorVerts)[nIndex].m_Color;
/// </code>
///
/// Using <c>origMeshVertID</c> for both is right only while a strip group's ordering happens to
/// match its mesh's, which is usually and not always - and where it differs, colours land on the
/// wrong vertices and the prop draws speckled with black.
/// </remarks>
public readonly record struct StudioCorner(int Vertex, int LightingGroup, int LightingVertex);

public static class StudioTriangles
{
    private const int SupportedVersion = 7;

    private const int HeaderBytes = 36;
    private const int BodyPartBytes = 8;
    private const int ModelBytes = 8;
    private const int LodBytes = 12;
    private const int MeshBytes = 9;
    private const int VertexBytes = 9;

    /// <summary>Strip and strip-group sizes, classic first and CS:GO's larger pair second.</summary>
    private const int StripGroupBytes = 25;
    private const int StripBytes = 27;
    private const int StripGroupBytesWithTopology = 33;
    private const int StripBytesWithTopology = 35;

    private const int VertexOriginalIdOffset = 4;

    /// <summary>The strip flags that say how its indices are arranged.</summary>
    private const byte TriangleList = 1;

    /// <summary>The most indices this reader will build for one model.</summary>
    /// <remarks>A model from a downloaded map is untrusted input (D32).</remarks>
    private const int MaximumIndices = 8_000_000;

    /// <summary>Reads a model's triangles at its most detailed level.</summary>
    /// <param name="file">The <c>.dx90.vtx</c>'s bytes.</param>
    /// <param name="model">The structure from the matching <c>.mdl</c>.</param>
    /// <returns>Indices into the model's vertex array, three per triangle, per mesh.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> is null.</exception>
    /// <exception cref="InvalidDataException">The file is not readable index data.</exception>
    /// <remarks>
    /// Returned per mesh and in the same order the <c>.mdl</c> lists them, because that is what
    /// carries the material: the index data itself names no materials at all.
    /// </remarks>
    public static IReadOnlyList<IReadOnlyList<StudioCorner>> Read(
        ReadOnlyMemory<byte> file, StudioModelInfo model)
    {
        ArgumentNullException.ThrowIfNull(model);

        ReadOnlySpan<byte> bytes = file.Span;

        if (bytes.Length < HeaderBytes)
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"An index file of {bytes.Length:N0} bytes is too short to hold its header."));
        }

        int version = BinaryPrimitives.ReadInt32LittleEndian(bytes);

        if (version != SupportedVersion)
        {
            throw new InvalidDataException(
                $"An index file declares version {version}, and only {SupportedVersion} is known.");
        }

        int checksum = BinaryPrimitives.ReadInt32LittleEndian(bytes[16..]);

        if (checksum != model.Checksum)
        {
            // **The engine's own check, and worth keeping.** The compiler stamps one number into
            // all three files so a mismatched set is refused rather than drawn as nonsense.
            throw new InvalidDataException(
                $"An index file's checksum {checksum} does not match the model's {model.Checksum}.");
        }

        foreach (bool topology in new[] { false, true })
        {
            if (TryRead(bytes, model, topology, out List<List<StudioCorner>> meshes))
            {
                return meshes;
            }
        }

        throw new InvalidDataException(
            "An index file's strip groups do not fit either known layout.");
    }

    /// <summary>Walks the file with one candidate layout, reporting whether it held together.</summary>
    private static bool TryRead(
        ReadOnlySpan<byte> file,
        StudioModelInfo model,
        bool topology,
        out List<List<StudioCorner>> meshes)
    {
        int stripGroupStride = topology ? StripGroupBytesWithTopology : StripGroupBytes;
        int stripStride = topology ? StripBytesWithTopology : StripBytes;

        meshes = [];

        // **Counted across the whole model, because that is how the .vhv headers are ordered.**
        // vrad appends one mesh header per strip group as it walks meshes and then groups, so
        // the Nth strip group encountered here is the Nth header there.
        int group = 0;

        try
        {
            int parts = BinaryPrimitives.ReadInt32LittleEndian(file[28..]);
            int partsAt = BinaryPrimitives.ReadInt32LittleEndian(file[32..]);

            for (int part = 0; part < parts; part++)
            {
                int partAt = At(file, partsAt, part, BodyPartBytes);

                int models = BinaryPrimitives.ReadInt32LittleEndian(file[partAt..]);
                int modelsAt = partAt + BinaryPrimitives.ReadInt32LittleEndian(file[(partAt + 4)..]);

                for (int index = 0; index < models; index++)
                {
                    // **Every model of every part, matching the .mdl.** The two files mirror each
                    // other part by part and model by model, so both readers walk all of them and
                    // the bodygroup choice is made later, per entity, at draw time. Picking here
                    // desynchronises them, and it surfaces as "strip groups do not fit either known
                    // layout" — a corrupt-file message for two walks disagreeing about a structure
                    // they both read correctly.
                    int modelAt = At(file, modelsAt, index, ModelBytes);

                    // **The most detailed level only**, which is level zero. The rest exist to
                    // save work at a distance an overhead camera is not paying anyway.
                    int lods = BinaryPrimitives.ReadInt32LittleEndian(file[modelAt..]);

                    if (lods <= 0)
                    {
                        continue;
                    }

                    // Bounds-checked like every other array walk, rather than trusted because it
                    // is the first element.
                    int lodAt = At(
                        file,
                        modelAt + BinaryPrimitives.ReadInt32LittleEndian(file[(modelAt + 4)..]),
                        0,
                        LodBytes);

                    ReadLod(file, lodAt, stripGroupStride, stripStride, meshes, ref group);
                }
            }
        }
        catch (Exception failure) when (
            failure is InvalidDataException or ArgumentOutOfRangeException or IndexOutOfRangeException)
        {
            // The candidate layout produced offsets that do not lie inside the file. That is the
            // measurement this method exists to make, not an error to report.
            return false;
        }

        // A layout that produces no triangles at all has not held together either, whatever it did
        // without throwing.
        return meshes.Count == model.Meshes.Count && meshes.Exists(mesh => mesh.Count > 0);
    }

    private static void ReadLod(
        ReadOnlySpan<byte> file,
        int lodAt,
        int stripGroupStride,
        int stripStride,
        List<List<StudioCorner>> into,
        ref int group)
    {
        int meshes = BinaryPrimitives.ReadInt32LittleEndian(file[lodAt..]);
        int meshesAt = lodAt + BinaryPrimitives.ReadInt32LittleEndian(file[(lodAt + 4)..]);

        for (int mesh = 0; mesh < meshes; mesh++)
        {
            int meshAt = At(file, meshesAt, mesh, MeshBytes);

            List<StudioCorner> corners = [];

            int groups = BinaryPrimitives.ReadInt32LittleEndian(file[meshAt..]);
            int groupsAt = meshAt + BinaryPrimitives.ReadInt32LittleEndian(file[(meshAt + 4)..]);

            for (int index = 0; index < groups; index++)
            {
                ReadStripGroup(
                    file,
                    At(file, groupsAt, index, stripGroupStride),
                    stripStride,
                    group,
                    corners);

                group++;
            }

            into.Add(corners);
        }
    }

    private static void ReadStripGroup(
        ReadOnlySpan<byte> file,
        int groupAt,
        int stripStride,
        int group,
        List<StudioCorner> into)
    {
        int vertices = BinaryPrimitives.ReadInt32LittleEndian(file[groupAt..]);
        int verticesAt = groupAt + BinaryPrimitives.ReadInt32LittleEndian(file[(groupAt + 4)..]);
        int indices = BinaryPrimitives.ReadInt32LittleEndian(file[(groupAt + 8)..]);
        int indicesAt = groupAt + BinaryPrimitives.ReadInt32LittleEndian(file[(groupAt + 12)..]);
        int strips = BinaryPrimitives.ReadInt32LittleEndian(file[(groupAt + 16)..]);
        int stripsAt = groupAt + BinaryPrimitives.ReadInt32LittleEndian(file[(groupAt + 20)..]);

        Check(file, verticesAt, vertices, VertexBytes);
        Check(file, indicesAt, indices, sizeof(ushort));

        for (int strip = 0; strip < strips; strip++)
        {
            int stripAt = At(file, stripsAt, strip, stripStride);

            int stripIndices = BinaryPrimitives.ReadInt32LittleEndian(file[stripAt..]);
            int firstIndex = BinaryPrimitives.ReadInt32LittleEndian(file[(stripAt + 4)..]);
            byte flags = file[stripAt + 18];

            if (stripIndices < 0 || firstIndex < 0 || firstIndex + stripIndices > indices)
            {
                throw new InvalidDataException("A strip runs outside its group's indices.");
            }

            if (into.Count + stripIndices > MaximumIndices)
            {
                throw new InvalidDataException("An index file asks for more indices than is credible.");
            }

            AddStrip(
                file,
                verticesAt,
                vertices,
                indicesAt + (firstIndex * sizeof(ushort)),
                stripIndices,
                (flags & TriangleList) != 0,
                group,
                into);
        }
    }

    /// <summary>Turns one strip's indices into triangles, whichever arrangement it uses.</summary>
    /// <remarks>
    /// **A triangle strip alternates winding.** Every second triangle has its first two indices
    /// swapped, because the strip shares an edge with the one before it and the shared edge runs
    /// the other way. Emitting them all the same way leaves every other triangle facing backwards,
    /// which under backface culling is a model with half its surface missing — the same failure
    /// that hid the map's terrain.
    /// </remarks>
    private static void AddStrip(
        ReadOnlySpan<byte> file,
        int verticesAt,
        int vertexCount,
        int indicesAt,
        int count,
        bool isList,
        int group,
        List<StudioCorner> into)
    {
        if (isList)
        {
            for (int index = 0; index + 2 < count; index += 3)
            {
                into.Add(Corner(file, verticesAt, vertexCount, group, Index(file, indicesAt, index)));
                into.Add(Corner(file, verticesAt, vertexCount, group, Index(file, indicesAt, index + 1)));
                into.Add(Corner(file, verticesAt, vertexCount, group, Index(file, indicesAt, index + 2)));
            }

            return;
        }

        for (int index = 0; index + 2 < count; index++)
        {
            int first = Index(file, indicesAt, index);
            int second = Index(file, indicesAt, index + 1);
            int third = Index(file, indicesAt, index + 2);

            if (first == second || second == third || first == third)
            {
                // A degenerate triangle, which is how a strip stitches two runs together. It draws
                // nothing, and passing it on costs three indices per occurrence.
                continue;
            }

            if ((index & 1) != 0)
            {
                (first, second) = (second, first);
            }

            into.Add(Corner(file, verticesAt, vertexCount, group, first));
            into.Add(Corner(file, verticesAt, vertexCount, group, second));
            into.Add(Corner(file, verticesAt, vertexCount, group, third));
        }
    }

    /// <summary>One index from a strip group's index array.</summary>
    private static int Index(ReadOnlySpan<byte> file, int at, int index) =>
        BinaryPrimitives.ReadUInt16LittleEndian(file[(at + (index * sizeof(ushort)))..]);

    /// <summary>
    /// The vertex a strip-group index really means.
    /// </summary>
    /// <remarks>
    /// **Two levels of indirection, and skipping one is invisible.** A strip's indices address the
    /// strip GROUP's own vertex array, and each entry there carries <c>origMeshVertID</c>, which is
    /// the index into the mesh's vertices in the <c>.vvd</c>. Using the strip index directly still
    /// lands on real vertices of the same model, so the result is a recognisable shape with its
    /// surfaces shuffled rather than an error.
    /// </remarks>
    private static StudioCorner Corner(
        ReadOnlySpan<byte> file, int verticesAt, int vertexCount, int group, int index)
    {
        if (index < 0 || index >= vertexCount)
        {
            throw new InvalidDataException("A strip index names a vertex its group does not have.");
        }

        int original = BinaryPrimitives.ReadUInt16LittleEndian(
            file[(verticesAt + (index * VertexBytes) + VertexOriginalIdOffset)..]);

        // The strip group index is kept alongside, because baked lighting is stored in that
        // order while positions are stored in the mesh's.
        return new StudioCorner(original, group, index);
    }

    /// <summary>The address of one element of an array, checked to be inside the file.</summary>
    private static int At(ReadOnlySpan<byte> file, int start, int index, int stride)
    {
        long at = (long)start + ((long)index * stride);

        if (start < HeaderBytes || at < 0 || at + stride > file.Length)
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"An index file addresses {at:N0} of {file.Length:N0} bytes."));
        }

        return (int)at;
    }

    /// <summary>Checks an array lies inside the file without addressing an element of it.</summary>
    private static void Check(ReadOnlySpan<byte> file, int start, int count, int stride)
    {
        if (count < 0 || start < HeaderBytes ||
            (long)start + ((long)count * stride) > file.Length)
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"An index file puts {count:N0} entries at {start:N0} of {file.Length:N0} bytes."));
        }
    }
}
