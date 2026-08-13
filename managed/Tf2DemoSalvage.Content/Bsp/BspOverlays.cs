using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Tf2DemoSalvage.Content.Bsp;

/// <summary>One decal painted onto the world.</summary>
/// <param name="Id">The overlay's own identifier.</param>
/// <param name="TexInfo">Which texinfo, and through it which material, it draws.</param>
/// <param name="RenderOrder">Which of four layers it belongs to; higher draws later.</param>
/// <param name="Faces">The world faces it is pinned to.</param>
/// <param name="U">Texture coordinate range across.</param>
/// <param name="V">Texture coordinate range down.</param>
/// <param name="Corners">The quad's four corners, in the overlay's own basis.</param>
/// <param name="Origin">Where it sits in the world.</param>
/// <param name="BasisNormal">The direction it faces.</param>
/// <remarks>
/// **An overlay is a decal that survived compilation.** Signs, scorch marks, the arrows painted on
/// a floor, the numbers on a control point — all of them are quads pinned to the faces underneath
/// rather than geometry of their own, which is how they follow a surface around a corner without
/// the mapper building one.
/// </remarks>
public sealed record BspOverlay(
    int Id,
    short TexInfo,
    int RenderOrder,
    IReadOnlyList<int> Faces,
    (float Start, float End) U,
    (float Start, float End) V,
    IReadOnlyList<(float X, float Y, float Z)> Corners,
    (float X, float Y, float Z) Origin,
    (float X, float Y, float Z) BasisNormal)
{
    /// <summary>How many world faces the overlay is pinned to.</summary>
    public int FaceCount => Faces.Count;
}

/// <summary>
/// Reads a map's decals from lump 45.
/// </summary>
/// <remarks>
/// **The layout is confirmed by arithmetic before a byte is interpreted.** The field order comes
/// from <c>bsplib.cpp</c>'s byteswap descriptor, and summing it gives exactly 352 bytes:
///
/// <code>
///    0  nId                          int
///    4  nTexInfo                     short
///    6  m_nFaceCountAndRenderOrder   short
///    8  aFaces[64]                   int      256 bytes
///  264  flU[2]                       float      8
///  272  flV[2]                       float      8
///  280  vecUVPoints[4]               Vector    48
///  328  vecOrigin                    Vector    12
///  340  vecBasisNormal               Vector    12
/// </code>
///
/// The lump's decompressed length on cp_process_final divides by 352 exactly 243 times, so the
/// stride is not a guess. A wrong field OFFSET would still parse cleanly, which is what the tests
/// are for — the basis normal having length one is the check that pins the tail of the struct.
/// </remarks>
public static class BspOverlays
{
    private const int LumpOverlays = 45;

    /// <summary>Bytes per overlay, and the arithmetic above is the reason it is this.</summary>
    private const int OverlayStride = 352;

    /// <summary>Most faces one overlay can name.</summary>
    private const int MaximumFaces = 64;

    private const int TexInfoOffset = 4;
    private const int FaceCountOffset = 6;
    private const int FacesOffset = 8;
    private const int UOffset = 264;
    private const int VOffset = 272;
    private const int CornersOffset = 280;
    private const int OriginOffset = 328;
    private const int BasisNormalOffset = 340;

    /// <summary>The top two bits of the packed field hold the render order.</summary>
    private const int RenderOrderMask = 0xC000;

    /// <summary>Reads every overlay in a map.</summary>
    /// <param name="file">The map's bytes.</param>
    /// <returns>The overlays, in lump order.</returns>
    /// <exception cref="InvalidDataException">An overlay names more faces than it can hold.</exception>
    public static IReadOnlyList<BspOverlay> Read(ReadOnlyMemory<byte> file)
    {
        BspHeader header = BspHeader.Parse(file.Span);

        ReadOnlySpan<byte> overlays = BspLumpData
            .ReadStructures(file, header.Lump(LumpOverlays), OverlayStride, "overlays").Span;

        int count = overlays.Length / OverlayStride;
        List<BspOverlay> read = new(count);

        for (int index = 0; index < count; index++)
        {
            ReadOnlySpan<byte> entry = overlays.Slice(index * OverlayStride, OverlayStride);

            // **Face count and render order share sixteen bits**, the order in the top two. Reading
            // the whole field as a count gives tens of thousands for any overlay with a non-zero
            // order, and then the face loop walks straight off the end of the struct.
            int packed = BinaryPrimitives.ReadUInt16LittleEndian(entry[FaceCountOffset..]);
            int faceCount = packed & ~RenderOrderMask;
            int renderOrder = (packed & RenderOrderMask) >> 14;

            if (faceCount is < 0 or > MaximumFaces)
            {
                throw new InvalidDataException(
                    $"An overlay names {faceCount} faces, and the lump holds room for {MaximumFaces}.");
            }

            List<int> faces = new(faceCount);

            for (int face = 0; face < faceCount; face++)
            {
                faces.Add(BinaryPrimitives.ReadInt32LittleEndian(entry[(FacesOffset + (face * 4))..]));
            }

            List<(float X, float Y, float Z)> corners = new(4);

            for (int corner = 0; corner < 4; corner++)
            {
                corners.Add(Vector(entry, CornersOffset + (corner * 12)));
            }

            read.Add(new BspOverlay(
                BinaryPrimitives.ReadInt32LittleEndian(entry),
                BinaryPrimitives.ReadInt16LittleEndian(entry[TexInfoOffset..]),
                renderOrder,
                faces,
                (Float(entry, UOffset), Float(entry, UOffset + 4)),
                (Float(entry, VOffset), Float(entry, VOffset + 4)),
                corners,
                Vector(entry, OriginOffset),
                Vector(entry, BasisNormalOffset)));
        }

        return read;
    }

    private static (float X, float Y, float Z) Vector(ReadOnlySpan<byte> entry, int at) =>
        (Float(entry, at), Float(entry, at + 4), Float(entry, at + 8));

    private static float Float(ReadOnlySpan<byte> entry, int at) =>
        BinaryPrimitives.ReadSingleLittleEndian(entry[at..]);
}
