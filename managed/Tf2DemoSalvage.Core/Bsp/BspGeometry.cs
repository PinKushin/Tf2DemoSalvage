using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Tf2DemoSalvage.Core.Bsp;

/// <summary>One drawable surface: its outline, in Source world units, and which way it faces.</summary>
/// <param name="Points">The polygon's vertices, in winding order.</param>
/// <param name="Normal">Unit normal, already corrected for the face's side.</param>
/// <param name="Flags">Surface flags from the face's texinfo.</param>
public sealed record BspFace(
    IReadOnlyList<(float X, float Y, float Z)> Points,
    (float X, float Y, float Z) Normal,
    SurfaceProperties Flags)
{
    /// <summary>Surfaces that exist for the compiler rather than for the player.</summary>
    /// <remarks>
    /// Sky and Sky2D are the skybox, which is irrelevant to a map overview. NoDraw, Hint, Skip and
    /// Trigger are tool surfaces: invisible in game, and drawn here they would be solid walls and
    /// trigger boxes sitting on top of the map.
    /// </remarks>
    private const SurfaceProperties NotDrawn =
        SurfaceProperties.Sky | SurfaceProperties.Sky2D | SurfaceProperties.NoDraw |
        SurfaceProperties.Hint | SurfaceProperties.Skip | SurfaceProperties.Trigger;

    /// <summary>Whether this surface is one a player would actually see.</summary>
    public bool IsVisible => (Flags & NotDrawn) == SurfaceProperties.None;
}

/// <summary>
/// The world's visible surfaces, read from a BSP.
/// </summary>
/// <remarks>
/// **FACES, not BRUSHES.** Brushes are convex collision volumes; the surfaces that get drawn live
/// in the faces lump, confirmed against a real map in <c>docs/RENDERING_NOTES.md</c> section 2.
/// The path is FACES to SURFEDGES to EDGES to VERTEXES, and a surfedge's SIGN says which way to
/// read its edge.
///
/// **Every index is bounds-checked at the point of use**, which D32 requires and which matters
/// here more than anywhere: a face names a plane, a range of surfedges, and through them edges and
/// vertices, so a single bad number reaches four lumps. The lump directory being valid says
/// nothing about the numbers inside it.
///
/// **Coordinates stay in Source's space** — right-handed, Z up. Converting to Direct3D's
/// left-handed Y-up space is deliberately NOT done here. The notes warn that mirroring an axis
/// reverses triangle winding, and that the fix belongs in exactly one place; putting the
/// conversion in the renderer keeps this class a faithful reading of the file, which is also what
/// makes it testable against numbers taken straight from the format.
/// </remarks>
public sealed class BspGeometry
{
    private const int LumpPlanes = 1;
    private const int LumpVertexes = 3;
    private const int LumpFaces = 7;
    private const int LumpTexinfo = 6;
    private const int LumpEdges = 12;
    private const int LumpSurfedges = 13;

    private const int PlaneStride = 20;
    private const int VertexStride = 12;
    private const int EdgeStride = 4;
    private const int SurfedgeStride = 4;
    private const int FaceStride = 56;
    private const int TexinfoStride = 72;

    /// <summary>Byte offset of the flags field inside a texinfo record.</summary>
    private const int TexinfoFlagsOffset = 64;

    /// <summary>Byte offset of the texinfo index inside a face record.</summary>
    private const int FaceTexinfoOffset = 10;

    private BspGeometry(IReadOnlyList<BspFace> faces)
    {
        Faces = faces;

        // Tool and sky surfaces go first: they are invisible in game, and drawn here the skybox
        // would cover the map and trigger volumes would appear as solid boxes on top of it.
        //
        // A ceiling's normal points down into the room it encloses, so dropping downward-facing
        // surfaces is exactly "freecam looking down, without the roof in the way". Walls are kept:
        // their normals are horizontal, and they are what gives an overhead view its outlines.
        //
        // **This is the engine's own rule, not a stylistic choice.** Source draws a face only from
        // its front side, which is why noclipping beneath a TF2 map makes the world go
        // transparent - the floors' backs are culled and only walls, whose normals are horizontal,
        // still face you. So this is backface culling for a fixed downward camera, precomputed
        // once instead of evaluated per frame, and the result should look like the game rather
        // than like an interpretation of it.
        //
        // It also settles the "roofs players stand on" question by construction: a hut's walkable
        // top faces up and survives, while the ceiling inside that same hut faces down and does
        // not. Solid brushwork has both surfaces, and they are filtered independently.
        OverheadFaces = [.. faces.Where(face => face.IsVisible && face.Normal.Z >= 0f)];
    }

    /// <summary>Every drawable face in the file.</summary>
    public IReadOnlyList<BspFace> Faces { get; }

    /// <summary>Faces visible from directly above: everything except downward-facing surfaces.</summary>
    public IReadOnlyList<BspFace> OverheadFaces { get; }

    /// <summary>Reads the world geometry from a whole BSP file.</summary>
    /// <param name="file">The file's bytes.</param>
    /// <returns>The geometry.</returns>
    /// <exception cref="InvalidDataException">
    /// The header is invalid, or an index inside it points outside the lump it addresses.
    /// </exception>
    public static BspGeometry Read(ReadOnlyMemory<byte> file)
    {
        BspHeader header = BspHeader.Parse(file.Span);

        // Read rather than sliced: a shipped TF2 map stores every one of these lumps LZMA
        // compressed, and nothing in the lump directory says so. See BspLumpData.
        ReadOnlySpan<byte> planes = Lump(file, header, LumpPlanes, PlaneStride, "planes");
        ReadOnlySpan<byte> vertexes = Lump(file, header, LumpVertexes, VertexStride, "vertexes");
        ReadOnlySpan<byte> edges = Lump(file, header, LumpEdges, EdgeStride, "edges");
        ReadOnlySpan<byte> surfedges = Lump(file, header, LumpSurfedges, SurfedgeStride, "surfedges");
        ReadOnlySpan<byte> faces = Lump(file, header, LumpFaces, FaceStride, "faces");
        ReadOnlySpan<byte> texinfo = Lump(file, header, LumpTexinfo, TexinfoStride, "texinfo");

        // Counts come from lump LENGTH, never from a count stored in the data. A length is at
        // least cross-checkable against the file; a declared count is not.
        //
        // The division is exact because ReadStructures has already refused a length that is not a
        // whole number of entries — which is the check that would have caught the compression
        // immediately, instead of reading compressed bytes as faces and believing the result.
        int planeCount = planes.Length / PlaneStride;
        int vertexCount = vertexes.Length / VertexStride;
        int edgeCount = edges.Length / EdgeStride;
        int surfedgeCount = surfedges.Length / SurfedgeStride;
        int faceCount = faces.Length / FaceStride;
        int texinfoCount = texinfo.Length / TexinfoStride;

        List<BspFace> read = new(faceCount);

        for (int index = 0; index < faceCount; index++)
        {
            ReadOnlySpan<byte> face = faces.Slice(index * FaceStride, FaceStride);

            int planeIndex = BinaryPrimitives.ReadUInt16LittleEndian(face);
            bool flipped = face[2] != 0;
            int firstEdge = BinaryPrimitives.ReadInt32LittleEndian(face[4..]);
            int edgesInFace = BinaryPrimitives.ReadInt16LittleEndian(face[8..]);
            int texinfoIndex = BinaryPrimitives.ReadInt16LittleEndian(face[FaceTexinfoOffset..]);

            // Degenerate faces exist in real maps. One should not cost the rest of the map.
            if (edgesInFace <= 0)
            {
                continue;
            }

            Require(planeIndex < planeCount, $"Face {index} names plane {planeIndex} of {planeCount}.");
            Require(
                firstEdge >= 0 && (long)firstEdge + edgesInFace <= surfedgeCount,
                $"Face {index} claims surfedges {firstEdge} to {(long)firstEdge + edgesInFace} " +
                $"of {surfedgeCount}.");

            List<(float X, float Y, float Z)> points = new(edgesInFace);

            for (int step = 0; step < edgesInFace; step++)
            {
                int surfedge = BinaryPrimitives.ReadInt32LittleEndian(
                    surfedges[((firstEdge + step) * SurfedgeStride)..]);

                // The sign picks the direction; the magnitude is the edge index. Reading it as
                // unsigned would send a backwards edge to a wildly wrong index instead.
                int edgeIndex = Math.Abs(surfedge);
                Require(edgeIndex < edgeCount, $"Face {index} names edge {edgeIndex} of {edgeCount}.");

                ReadOnlySpan<byte> edge = edges.Slice(edgeIndex * EdgeStride, EdgeStride);
                int first = BinaryPrimitives.ReadUInt16LittleEndian(edge);
                int second = BinaryPrimitives.ReadUInt16LittleEndian(edge[2..]);
                int vertexIndex = surfedge >= 0 ? first : second;

                Require(
                    vertexIndex < vertexCount,
                    $"Face {index} names vertex {vertexIndex} of {vertexCount}.");

                points.Add(ReadVertex(vertexes, vertexIndex));
            }

            read.Add(new BspFace(
                points,
                ReadNormal(planes, planeIndex, flipped),
                ReadFlags(texinfo, texinfoIndex, texinfoCount)));
        }

        return new BspGeometry(read);
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

        // A face on the back side of its plane faces the other way. Ignoring this makes half the
        // world's ceilings look like floors, which is the exact distinction the overhead filter
        // depends on.
        return flipped ? (-x, -y, -z) : (x, y, z);
    }

    /// <summary>Reads a face's surface flags, tolerating a face with no texinfo.</summary>
    /// <remarks>
    /// A texinfo index of -1 is legal and means the face has no texture information. Treated as
    /// "no flags" rather than as corruption: it is not a claim about anything, and rejecting the
    /// file over it would lose a whole map to one unusual face.
    /// </remarks>
    private static SurfaceProperties ReadFlags(ReadOnlySpan<byte> texinfo, int index, int count)
    {
        if (index < 0)
        {
            return SurfaceProperties.None;
        }

        Require(index < count, $"A face names texinfo {index} of {count}.");

        return (SurfaceProperties)BinaryPrimitives.ReadInt32LittleEndian(
            texinfo[((index * TexinfoStride) + TexinfoFlagsOffset)..]);
    }

    /// <summary>Reads one lump, decompressed if needed, and checks it holds whole entries.</summary>
    private static ReadOnlySpan<byte> Lump(
        ReadOnlyMemory<byte> file, BspHeader header, int index, int stride, string what) =>
        BspLumpData.ReadStructures(file, header.Lump(index), stride, what).Span;

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture, $"{message}"));
        }
    }
}
