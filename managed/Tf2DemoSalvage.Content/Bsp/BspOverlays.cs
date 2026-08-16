using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.IO;

using static Tf2DemoSalvage.Content.Bsp.BspStructLayout;

namespace Tf2DemoSalvage.Content.Bsp;

/// <summary>One decal painted onto the world.</summary>
/// <param name="Id">The overlay's own identifier.</param>
/// <param name="TexInfo">Which texinfo it draws through.</param>
/// <param name="MaterialIndex">Which material, resolved through that texinfo's texdata.</param>
/// <param name="RenderOrder">Which of four layers it belongs to; higher draws later.</param>
/// <param name="Faces">The world faces it is pinned to.</param>
/// <param name="U">Texture coordinate range across.</param>
/// <param name="V">Texture coordinate range down.</param>
/// <param name="Corners">The quad's four corners, two dimensional, in the overlay's own basis.</param>
/// <param name="BasisU">The basis axis across, recovered from the corners' unused z components.</param>
/// <param name="BasisV">The basis axis down, derived from the normal and U.</param>
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
    int MaterialIndex,
    int RenderOrder,
    IReadOnlyList<int> Faces,
    (float Start, float End) U,
    (float Start, float End) V,
    IReadOnlyList<(float X, float Y)> Corners,
    (float X, float Y, float Z) Origin,
    (float X, float Y, float Z) BasisNormal,
    (float X, float Y, float Z) BasisU,
    (float X, float Y, float Z) BasisV)
{
    /// <summary>How many world faces the overlay is pinned to.</summary>
    public int FaceCount => Faces.Count;

    /// <summary>The quad's four corners in world coordinates.</summary>
    /// <remarks>
    /// **The corners are two numbers in the overlay's own plane**, so placing them is
    /// <c>origin + x·U + y·V</c>. That the result lands on the surfaces the overlay names — same
    /// orientation, and within a few units of their plane — is what
    /// <c>OverlayPlacementTests</c> measures, and it is the only check available: the engine's own
    /// placement code was never released.
    /// </remarks>
    public IReadOnlyList<(float X, float Y, float Z)> WorldCorners =>
    [
        .. Corners.Select(corner => (
            Origin.X + (corner.X * BasisU.X) + (corner.Y * BasisV.X),
            Origin.Y + (corner.X * BasisU.Y) + (corner.Y * BasisV.Y),
            Origin.Z + (corner.X * BasisU.Z) + (corner.Y * BasisV.Z))),
    ];
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
///
/// **The corners' z components are not coordinates.** vbsp smuggles the basis across in them,
/// because the lump has nowhere else to put it:
///
/// <code>
/// // Encode the BasisU into the unused z component of the vecUVPoints 0, 1, 2
/// pOverlay-&gt;vecUVPoints[0].z = pMapOverlay-&gt;vecBasis[0].x;
/// pOverlay-&gt;vecUVPoints[1].z = pMapOverlay-&gt;vecBasis[0].y;
/// pOverlay-&gt;vecUVPoints[2].z = pMapOverlay-&gt;vecBasis[0].z;
///
/// // Encode whether or not the v axis should be flipped.
/// Vector vecCross = pMapOverlay-&gt;vecBasis[2].Cross( pMapOverlay-&gt;vecBasis[0] );
/// if ( vecCross.Dot( pMapOverlay-&gt;vecBasis[1] ) &lt; 0.0f )
///     pOverlay-&gt;vecUVPoints[3].z = 1.0f;
/// </code>
///
/// The map file carries three basis vectors and the lump stores one, so U comes out of those three
/// z values and V is the cross product of the normal and U, flipped when the fourth says so. A
/// reader treating the corners as three-dimensional points gets a quad standing on edge.
///
/// **An overlay's texinfo has no texture mapping in it either.** vbsp zeroes every texture vector
/// and writes -99999 into the last component, so the material comes through <c>texdata</c> and the
/// texture coordinates come from <c>flU</c>, <c>flV</c> and the corners. Anything projecting a
/// position through this texinfo, as an ordinary face would, gets nonsense.
/// </remarks>
public static class BspOverlays
{
    /// <summary>Most faces one overlay can name: <c>OVERLAY_BSP_FACE_COUNT</c>.</summary>
    private const int MaximumFaces = 64;

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
            .ReadStructures(file, header.Lump(BspLumpIndex.Overlays), OverlayStride, "overlays").Span;

        ReadOnlySpan<byte> texinfo = BspLumpData
            .ReadStructures(file, header.Lump(BspLumpIndex.Texinfo), TexinfoStride, "texinfo").Span;

        int count = overlays.Length / OverlayStride;
        List<BspOverlay> read = new(count);

        for (int index = 0; index < count; index++)
        {
            ReadOnlySpan<byte> entry = overlays.Slice(index * OverlayStride, OverlayStride);

            // **Face count and render order share sixteen bits**, the order in the top two. Reading
            // the whole field as a count gives tens of thousands for any overlay with a non-zero
            // order, and then the face loop walks straight off the end of the struct.
            int packed = BinaryPrimitives.ReadUInt16LittleEndian(entry[OverlayFaceCountOffset..]);
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
                faces.Add(BinaryPrimitives.ReadInt32LittleEndian(entry[(OverlayFacesOffset + (face * 4))..]));
            }

            // Two dimensional on purpose: the z of each corner carries the basis, not a height.
            List<(float X, float Y)> corners = new(4);

            for (int corner = 0; corner < 4; corner++)
            {
                corners.Add((
                    Float(entry, OverlayCornersOffset + (corner * 12)),
                    Float(entry, OverlayCornersOffset + (corner * 12) + 4)));
            }

            (float X, float Y, float Z) basisU = (
                Float(entry, OverlayCornersOffset + 8),
                Float(entry, OverlayCornersOffset + 12 + 8),
                Float(entry, OverlayCornersOffset + 24 + 8));

            (float X, float Y, float Z) normal = Vector(entry, OverlayBasisNormalOffset);

            // V is the cross product of the normal and U, flipped when the fourth corner's z says
            // so - which is the one bit of information vbsp had left to encode it in.
            bool flipped = Float(entry, OverlayCornersOffset + 36 + 8) != 0f;

            (float X, float Y, float Z) basisV = Cross(normal, basisU);

            if (flipped)
            {
                basisV = (-basisV.X, -basisV.Y, -basisV.Z);
            }

            short texInfoIndex = BinaryPrimitives.ReadInt16LittleEndian(entry[OverlayTexinfoOffset..]);

            // **An overlay's texinfo carries no mapping, only a material.** vbsp zeroes every
            // texture vector in it and writes -99999 into the last component, so texdata is the
            // only field worth reading - the texture coordinates come from flU, flV and the quad.
            int materialIndex = texInfoIndex >= 0 &&
                ((texInfoIndex + 1) * TexinfoStride) <= texinfo.Length
                ? BinaryPrimitives.ReadInt32LittleEndian(
                    texinfo[((texInfoIndex * TexinfoStride) + TexinfoTexdataOffset)..])
                : -1;

            read.Add(new BspOverlay(
                BinaryPrimitives.ReadInt32LittleEndian(entry),
                texInfoIndex,
                materialIndex,
                renderOrder,
                faces,
                (Float(entry, OverlayUOffset), Float(entry, OverlayUOffset + 4)),
                (Float(entry, OverlayVOffset), Float(entry, OverlayVOffset + 4)),
                corners,
                Vector(entry, OverlayOriginOffset),
                normal,
                basisU,
                basisV));
        }

        return read;
    }

    private static (float X, float Y, float Z) Cross(
        (float X, float Y, float Z) left, (float X, float Y, float Z) right) =>
        (
            (left.Y * right.Z) - (left.Z * right.Y),
            (left.Z * right.X) - (left.X * right.Z),
            (left.X * right.Y) - (left.Y * right.X));

    private static (float X, float Y, float Z) Vector(ReadOnlySpan<byte> entry, int at) =>
        (Float(entry, at), Float(entry, at + 4), Float(entry, at + 8));

    private static float Float(ReadOnlySpan<byte> entry, int at) =>
        BinaryPrimitives.ReadSingleLittleEndian(entry[at..]);
}
