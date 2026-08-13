using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Tf2DemoSalvage.Core.Bsp;

/// <summary>One face's baked lighting.</summary>
/// <param name="Width">Samples across.</param>
/// <param name="Height">Samples down.</param>
/// <param name="Pixels">The samples, four bytes each, red first, alpha always 255.</param>
/// <remarks>
/// The pixels are a <see cref="ReadOnlyMemory{T}"/> rather than an array because they exist to be
/// copied into a lightmap atlas and uploaded; handing out a defensive copy of every face's samples
/// would allocate a second copy of the map's entire lighting for no reader that wants one.
/// </remarks>
public readonly record struct BspLightmap(int Width, int Height, ReadOnlyMemory<byte> Pixels)
{
    /// <summary>Whether the face had any lighting at all.</summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;
}

/// <summary>
/// The lighting <c>vrad</c> baked into a map.
/// </summary>
/// <remarks>
/// **This is why a TF2 map looks like a TF2 map.** The textures give a surface its colour; the
/// lightmap gives it the shadow under a ledge, the shaft of light through a doorway and the
/// darkness of an interior. Drawing the textures flat-lit produces something that reads as a
/// texture atlas rather than as a level.
///
/// It is entirely static: computed once when the map was compiled, stored in lump 8, and identical
/// for every player in every match on that map. So it can be decoded once at load and never
/// touched again, which is the cheapest possible lighting model — there is nothing to compute per
/// frame, per camera or per tick.
///
/// Each face states where its samples begin and how many there are:
///
/// <code>
///   dface_t offset 16: styles[4]                      which light styles are present
///   dface_t offset 20: lightofs                       byte offset into lump 8, or -1 for unlit
///   dface_t offset 28: LightmapTextureMinsInLuxels[2]
///   dface_t offset 36: LightmapTextureSizeInLuxels[2] samples are size + 1 in each direction
/// </code>
///
/// **Those offsets were established by arithmetic rather than from memory**, and the check is worth
/// keeping: with them, every lit face's samples land inside the lighting lump and the highest
/// sample ends at exactly 100.0% of its length — measured on cp_process_final (13,108 lit faces),
/// cp_badlands (13,154) and pl_upward (15,110). A wrong layout produces luxel counts in the
/// millions immediately.
///
/// **A sample is <c>ColorRGBExp32</c>: three bytes and a shared exponent**, which is an HDR format.
/// The exponent is signed and routinely negative, so treating the bytes as ordinary colour gives a
/// uniformly bright map with no shadows — a picture, not an error.
/// </remarks>
public static class BspLightmaps
{
    private const int LumpFaces = 7;
    private const int LumpLighting = 8;
    private const int LumpLightingHdr = 53;

    private const int FaceStride = 56;
    private const int StylesOffset = 16;
    private const int LightOffsetOffset = 20;
    private const int LuxelSizeOffset = 36;

    /// <summary>Bytes per sample: red, green, blue, exponent.</summary>
    private const int SampleBytes = 4;

    /// <summary>Faces with no lighting use this offset.</summary>
    private const int Unlit = -1;

    /// <summary>A style slot that is not in use.</summary>
    private const byte NoStyle = 255;

    /// <summary>
    /// Largest lightmap a single face may claim, per side.
    /// </summary>
    /// <remarks>
    /// The engine's own limit is 32 luxels per side for a normal face, and displacements reach 128.
    /// 512 is far above anything real and bounds what a hostile map (D32) can make this allocate.
    /// </remarks>
    private const int MaximumLuxels = 512;

    /// <summary>Reads every face's baked lighting.</summary>
    /// <param name="file">The map's bytes.</param>
    /// <returns>One entry per face, empty where the face is unlit.</returns>
    /// <exception cref="InvalidDataException">A face's samples fall outside the lighting lump.</exception>
    /// <remarks>
    /// **The LDR lump is used when the map has one, and that is a deliberate reversal.** A map
    /// compiled for both carries LDR in lump 8 and HDR in lump 53, and preferring HDR seemed
    /// obviously right — TF2 runs in HDR by default.
    ///
    /// It produced a map washed out to white. The two forms are scaled differently: an LDR sample
    /// is meant to be multiplied by two on the way out, which is what Source's own shaders do and
    /// what this project's shader does, while an HDR sample already carries that range in its
    /// exponent. Applying the LDR convention to HDR data doubles something that was not halved.
    ///
    /// So LDR is the lump that matches the renderer, and HDR is the fallback for a map that has
    /// only that one. Rendering HDR properly needs a tone map rather than a multiply, and that is
    /// worth doing when there is a reason to — not before.
    /// </remarks>
    public static IReadOnlyList<BspLightmap> Read(ReadOnlyMemory<byte> file)
    {
        BspHeader header = BspHeader.Parse(file.Span);

        ReadOnlySpan<byte> faces = BspLumpData
            .ReadStructures(file, header.Lump(LumpFaces), FaceStride, "faces").Span;

        ReadOnlyMemory<byte> ldr = BspLumpData.Read(file, header.Lump(LumpLighting));
        ReadOnlySpan<byte> lighting = ldr.Length > 0
            ? ldr.Span
            : BspLumpData.Read(file, header.Lump(LumpLightingHdr)).Span;

        int count = faces.Length / FaceStride;
        List<BspLightmap> lightmaps = new(count);

        for (int index = 0; index < count; index++)
        {
            ReadOnlySpan<byte> face = faces.Slice(index * FaceStride, FaceStride);
            int offset = BinaryPrimitives.ReadInt32LittleEndian(face[LightOffsetOffset..]);

            if (offset == Unlit || lighting.Length == 0)
            {
                lightmaps.Add(default);
                continue;
            }

            int width = BinaryPrimitives.ReadInt32LittleEndian(face[LuxelSizeOffset..]) + 1;
            int height = BinaryPrimitives.ReadInt32LittleEndian(face[(LuxelSizeOffset + 4)..]) + 1;

            if (width is < 1 or > MaximumLuxels || height is < 1 or > MaximumLuxels)
            {
                throw new InvalidDataException(
                    $"Face {index} claims a {width}x{height} lightmap, which is not a real size.");
            }

            long bytes = (long)width * height * SampleBytes;

            if (offset < 0 || offset + bytes > lighting.Length)
            {
                throw new InvalidDataException(
                    $"Face {index} needs {bytes} lighting bytes at {offset} of {lighting.Length}.");
            }

            lightmaps.Add(new BspLightmap(
                width, height, Decode(lighting.Slice(offset, (int)bytes), width * height)));
        }

        return lightmaps;
    }

    /// <summary>How many light styles a face uses.</summary>
    /// <param name="face">The face's 56 bytes.</param>
    /// <returns>The count, at least one for a lit face.</returns>
    /// <remarks>
    /// A face's samples are repeated once per active style — a flickering light stores every state.
    /// Only the first set is drawn here, which is the map's normal appearance; the rest matter only
    /// for animated lights, which a demo overview has no use for. The count is still needed to walk
    /// past them when reading anything that follows.
    /// </remarks>
    public static int StyleCount(ReadOnlySpan<byte> face)
    {
        int styles = 0;

        for (int slot = 0; slot < 4; slot++)
        {
            if (face[StylesOffset + slot] != NoStyle)
            {
                styles++;
            }
        }

        return Math.Max(1, styles);
    }

    /// <summary>Turns <c>ColorRGBExp32</c> samples into ordinary sRGB pixels.</summary>
    /// <remarks>
    /// **The exponent is signed, and usually negative.** A sample is
    /// <c>channel * 2^exponent</c> in linear light, so the stored bytes are not a colour: reading
    /// them directly gives a map lit uniformly at full brightness, with every shadow gone. That is
    /// a picture rather than an error, which is the failure this codebase keeps meeting.
    ///
    /// The result is clamped and gamma-corrected, because the renderer's target is sRGB. This is
    /// the same curve that reconciles a texture's decoded average with the map's stored
    /// reflectivity, and it is applied here for the same reason.
    /// </remarks>
    private static byte[] Decode(ReadOnlySpan<byte> samples, int count)
    {
        byte[] pixels = new byte[count * 4];

        for (int index = 0; index < count; index++)
        {
            ReadOnlySpan<byte> sample = samples.Slice(index * SampleBytes, SampleBytes);
            float scale = MathF.Pow(2f, (sbyte)sample[3]);

            pixels[(index * 4) + 0] = ToSrgb(sample[0] * scale);
            pixels[(index * 4) + 1] = ToSrgb(sample[1] * scale);
            pixels[(index * 4) + 2] = ToSrgb(sample[2] * scale);
            pixels[(index * 4) + 3] = 255;
        }

        return pixels;
    }

    /// <summary>Takes one linear sample into display space.</summary>
    /// <remarks>
    /// Normalised against a byte's range before the curve: a sample of 255 at exponent 0 is full
    /// brightness, and anything above it is over-range HDR to be clamped. The curve itself lives in
    /// <see cref="SourceGamma"/>, because static prop vertex lighting needs the same one and two
    /// copies would drift apart.
    /// </remarks>
    private static byte ToSrgb(float linear) => SourceGamma.ToDisplayByte(linear);
}
