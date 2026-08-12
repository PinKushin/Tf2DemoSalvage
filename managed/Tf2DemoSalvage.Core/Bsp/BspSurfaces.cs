using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Tf2DemoSalvage.Core.Bsp;

/// <summary>One corner of a surface, with everywhere it has to be sampled.</summary>
/// <param name="X">Position east, in Source units.</param>
/// <param name="Y">Position north.</param>
/// <param name="Z">Height.</param>
/// <param name="U">Texture coordinate across, already divided by the texture's width.</param>
/// <param name="V">Texture coordinate down.</param>
/// <param name="LightU">Lightmap coordinate across, in this face's own lightmap.</param>
/// <param name="LightV">Lightmap coordinate down.</param>
public readonly record struct SurfaceVertex(
    float X, float Y, float Z, float U, float V, float LightU, float LightV);

/// <summary>A face ready to draw: its corners, its material and its baked lighting.</summary>
/// <param name="FaceIndex">Position in the faces lump, so other lumps can be reached.</param>
/// <param name="Vertices">The polygon's corners, in winding order.</param>
/// <param name="MaterialIndex">Index into the map's texture table.</param>
/// <param name="Lightmap">The face's baked lighting, empty when it has none.</param>
/// <param name="Normal">Unit normal, already corrected for the face's side.</param>
/// <param name="Flags">Surface flags from the face's texinfo.</param>
/// <param name="DisplacementIndex">Index into DISPINFO, or -1 for an ordinary face.</param>
public sealed record BspSurface(
    int FaceIndex,
    IReadOnlyList<SurfaceVertex> Vertices,
    int MaterialIndex,
    BspLightmap Lightmap,
    (float X, float Y, float Z) Normal,
    SurfaceProperties Flags,
    int DisplacementIndex)
{
    /// <summary>Whether this face is the base quad of a displacement.</summary>
    /// <remarks>
    /// **A displacement's entry in FACES is not its geometry.** The quad here is the surface the
    /// terrain was built on; the real shape is a heightfield in DISPINFO and DISP_VERTS, and its
    /// lightmap covers the SUBDIVIDED surface rather than this quad. So a displacement's corners
    /// legitimately fall outside the lightmap coordinates computed from them — see B37.
    /// </remarks>
    public bool IsDisplacement => DisplacementIndex >= 0;

    /// <summary>Whether this is a surface a player would actually see.</summary>
    public bool IsVisible => (Flags & BspFace.NotDrawnSurfaces) == SurfaceProperties.None;
}

/// <summary>
/// Faces with the coordinates needed to texture and light them.
/// </summary>
/// <remarks>
/// **This is the join between geometry and appearance, and the map states all of it.** A face knows
/// its corners; its <c>texinfo</c> knows how to turn a world position into a texture coordinate,
/// and how to turn the same position into a position in the face's own lightmap.
///
/// <code>
///   texinfo_t offset  0: textureVecs[2][4]   world position to texture coordinate
///   texinfo_t offset 32: lightmapVecs[2][4]  world position to luxel
///   texinfo_t offset 64: flags
///   texinfo_t offset 68: texdata             which material
/// </code>
///
/// Each vector is a plane equation: <c>u = dot(position, vec.xyz) + vec.w</c>. For the texture that
/// result is in **pixels**, so it is divided by the texture's own width and height — which is why
/// this needs the texture table and not just the geometry. For the lightmap the result is in
/// **luxels**, and the face's <c>LightmapTextureMinsInLuxels</c> has to be subtracted before
/// dividing by its size: the vectors are shared by every face using that texinfo, and the mins are
/// what place this particular face inside its own lightmap.
///
/// **Forgetting the mins produces a picture rather than an error.** Every face samples somewhere in
/// the lightmap, so the map is lit — with the wrong patch of light on every surface, which reads as
/// a strange but plausible lighting scheme.
/// </remarks>
public static class BspSurfaces
{
    private const int LumpVertexes = 3;
    private const int LumpTexinfo = 6;
    private const int LumpFaces = 7;
    private const int LumpEdges = 12;
    private const int LumpSurfedges = 13;
    private const int LumpPlanes = 1;

    private const int FaceStride = 56;
    private const int TexinfoStride = 72;
    private const int VertexStride = 12;
    private const int EdgeStride = 4;
    private const int SurfedgeStride = 4;
    private const int PlaneStride = 20;

    private const int FaceTexinfoOffset = 10;
    private const int FaceDisplacementOffset = 12;
    private const int FaceLuxelMinsOffset = 28;
    private const int FaceLuxelSizeOffset = 36;
    private const int TexinfoFlagsOffset = 64;
    private const int TexinfoTexdataOffset = 68;
    private const int TexinfoLightmapVecsOffset = 32;

    /// <summary>Reads every drawable face with its texture and lightmap coordinates.</summary>
    /// <param name="file">The map's bytes.</param>
    /// <returns>The surfaces, in face order.</returns>
    /// <exception cref="InvalidDataException">An index points outside the lump it addresses.</exception>
    /// <remarks>
    /// Degenerate and tool surfaces are kept rather than filtered here, so a caller can decide:
    /// the overhead view drops downward-facing ones, a free camera does not.
    /// </remarks>
    public static IReadOnlyList<BspSurface> Read(ReadOnlyMemory<byte> file)
    {
        BspHeader header = BspHeader.Parse(file.Span);

        ReadOnlySpan<byte> faces = Lump(file, header, LumpFaces, FaceStride, "faces");
        ReadOnlySpan<byte> texinfo = Lump(file, header, LumpTexinfo, TexinfoStride, "texinfo");
        ReadOnlySpan<byte> vertexes = Lump(file, header, LumpVertexes, VertexStride, "vertexes");
        ReadOnlySpan<byte> edges = Lump(file, header, LumpEdges, EdgeStride, "edges");
        ReadOnlySpan<byte> surfedges = Lump(file, header, LumpSurfedges, SurfedgeStride, "surfedges");
        ReadOnlySpan<byte> planes = Lump(file, header, LumpPlanes, PlaneStride, "planes");

        IReadOnlyList<BspMaterial> materials = BspMaterials.Read(file);
        IReadOnlyList<BspLightmap> lightmaps = BspLightmaps.Read(file);

        int faceCount = faces.Length / FaceStride;
        int texinfoCount = texinfo.Length / TexinfoStride;
        int vertexCount = vertexes.Length / VertexStride;
        int edgeCount = edges.Length / EdgeStride;
        int surfedgeCount = surfedges.Length / SurfedgeStride;
        int planeCount = planes.Length / PlaneStride;

        List<BspSurface> surfaces = new(faceCount);

        for (int index = 0; index < faceCount; index++)
        {
            ReadOnlySpan<byte> face = faces.Slice(index * FaceStride, FaceStride);

            int planeIndex = BinaryPrimitives.ReadUInt16LittleEndian(face);
            bool flipped = face[2] != 0;
            int firstEdge = BinaryPrimitives.ReadInt32LittleEndian(face[4..]);
            int edgesInFace = BinaryPrimitives.ReadInt16LittleEndian(face[8..]);
            int texinfoIndex = BinaryPrimitives.ReadInt16LittleEndian(face[FaceTexinfoOffset..]);

            if (edgesInFace <= 0 || texinfoIndex < 0 || texinfoIndex >= texinfoCount)
            {
                // Degenerate faces reach here from real maps, and a face with no texinfo has no
                // appearance to describe.
                continue;
            }

            Require(planeIndex < planeCount, $"Face {index} names plane {planeIndex} of {planeCount}.");
            Require(
                firstEdge >= 0 && (long)firstEdge + edgesInFace <= surfedgeCount,
                $"Face {index} claims surfedges {firstEdge} to {firstEdge + edgesInFace}.");

            ReadOnlySpan<byte> info = texinfo.Slice(texinfoIndex * TexinfoStride, TexinfoStride);
            int materialIndex = BinaryPrimitives.ReadInt32LittleEndian(info[TexinfoTexdataOffset..]);

            Require(
                materialIndex >= 0 && materialIndex < materials.Count,
                $"Face {index} names material {materialIndex} of {materials.Count}.");

            BspMaterial material = materials[materialIndex];

            // A texture size of zero would divide every coordinate by nothing. Real maps do carry
            // materials with no dimensions - tool textures among them.
            float textureWidth = material.Width > 0 ? material.Width : 1f;
            float textureHeight = material.Height > 0 ? material.Height : 1f;

            int luxelMinU = BinaryPrimitives.ReadInt32LittleEndian(face[FaceLuxelMinsOffset..]);
            int luxelMinV = BinaryPrimitives.ReadInt32LittleEndian(face[(FaceLuxelMinsOffset + 4)..]);
            int luxelWidth = BinaryPrimitives.ReadInt32LittleEndian(face[FaceLuxelSizeOffset..]) + 1;
            int luxelHeight = BinaryPrimitives.ReadInt32LittleEndian(face[(FaceLuxelSizeOffset + 4)..]) + 1;

            List<SurfaceVertex> vertices = new(edgesInFace);

            for (int step = 0; step < edgesInFace; step++)
            {
                int surfedge = BinaryPrimitives.ReadInt32LittleEndian(
                    surfedges[((firstEdge + step) * SurfedgeStride)..]);

                // The sign picks the direction; the magnitude is the edge index.
                int edgeIndex = Math.Abs(surfedge);
                Require(edgeIndex < edgeCount, $"Face {index} names edge {edgeIndex} of {edgeCount}.");

                ReadOnlySpan<byte> edge = edges.Slice(edgeIndex * EdgeStride, EdgeStride);
                int first = BinaryPrimitives.ReadUInt16LittleEndian(edge);
                int second = BinaryPrimitives.ReadUInt16LittleEndian(edge[2..]);
                int vertexIndex = surfedge >= 0 ? first : second;

                Require(vertexIndex < vertexCount, $"Face {index} names vertex {vertexIndex}.");

                (float x, float y, float z) = ReadVertex(vertexes, vertexIndex);

                // Texture coordinates come out in pixels, so they are divided by the texture's own
                // size to reach the 0..1 the sampler wants. Values outside that range are normal
                // and correct: a wall repeats its texture, which is what the tiling is for.
                float u = (Project(info, 0, x, y, z) / textureWidth);
                float v = (Project(info, 16, x, y, z) / textureHeight);

                // Lightmap coordinates come out in luxels of a grid SHARED by every face using this
                // texinfo. Subtracting this face's own mins is what places it inside its own
                // lightmap; without that every face samples the wrong patch of light, and the map
                // still looks lit.
                float lightU = luxelWidth > 0
                    ? (Project(info, TexinfoLightmapVecsOffset, x, y, z) - luxelMinU) / luxelWidth
                    : 0f;
                float lightV = luxelHeight > 0
                    ? (Project(info, TexinfoLightmapVecsOffset + 16, x, y, z) - luxelMinV) / luxelHeight
                    : 0f;

                vertices.Add(new SurfaceVertex(x, y, z, u, v, lightU, lightV));
            }

            surfaces.Add(new BspSurface(
                index,
                vertices,
                materialIndex,
                index < lightmaps.Count ? lightmaps[index] : default,
                ReadNormal(planes, planeIndex, flipped),
                (SurfaceProperties)BinaryPrimitives.ReadInt32LittleEndian(info[TexinfoFlagsOffset..]),
                BinaryPrimitives.ReadInt16LittleEndian(face[FaceDisplacementOffset..])));
        }

        return surfaces;
    }

    /// <summary>Applies one of texinfo's plane equations to a world position.</summary>
    /// <remarks>
    /// Each is four floats: a direction and a distance, used as
    /// <c>dot(position, xyz) + w</c>. The same form serves both the texture and the lightmap,
    /// which is why one helper reads all four vectors.
    /// </remarks>
    private static float Project(ReadOnlySpan<byte> info, int offset, float x, float y, float z)
    {
        float vectorX = BinaryPrimitives.ReadSingleLittleEndian(info[offset..]);
        float vectorY = BinaryPrimitives.ReadSingleLittleEndian(info[(offset + 4)..]);
        float vectorZ = BinaryPrimitives.ReadSingleLittleEndian(info[(offset + 8)..]);
        float distance = BinaryPrimitives.ReadSingleLittleEndian(info[(offset + 12)..]);

        return (x * vectorX) + (y * vectorY) + (z * vectorZ) + distance;
    }

    private static (float X, float Y, float Z) ReadVertex(ReadOnlySpan<byte> vertexes, int index)
    {
        ReadOnlySpan<byte> vertex = vertexes.Slice(index * VertexStride, VertexStride);

        return (
            BinaryPrimitives.ReadSingleLittleEndian(vertex),
            BinaryPrimitives.ReadSingleLittleEndian(vertex[4..]),
            BinaryPrimitives.ReadSingleLittleEndian(vertex[8..]));
    }

    private static (float X, float Y, float Z) ReadNormal(
        ReadOnlySpan<byte> planes, int index, bool flipped)
    {
        ReadOnlySpan<byte> plane = planes.Slice(index * PlaneStride, PlaneStride);

        float x = BinaryPrimitives.ReadSingleLittleEndian(plane);
        float y = BinaryPrimitives.ReadSingleLittleEndian(plane[4..]);
        float z = BinaryPrimitives.ReadSingleLittleEndian(plane[8..]);

        return flipped ? (-x, -y, -z) : (x, y, z);
    }

    private static ReadOnlySpan<byte> Lump(
        ReadOnlyMemory<byte> file, BspHeader header, int index, int stride, string what) =>
        BspLumpData.ReadStructures(file, header.Lump(index), stride, what).Span;

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
        }
    }
}
