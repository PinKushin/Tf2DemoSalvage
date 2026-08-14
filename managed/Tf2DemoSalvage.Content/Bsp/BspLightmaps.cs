using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Tf2DemoSalvage.Content.Bsp;

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
/// <summary>Everything one face's lighting carries, flat and directional.</summary>
/// <param name="Flat">The single lightmap every lit face has.</param>
/// <param name="Directional">Three more, in the bump basis, or empty when the face is not bump lit.</param>
/// <remarks>
/// **The flat set is not a base the other three add to.** When a face is bump lit Source's shader
/// reads sets 1, 2 and 3 and never touches set 0 - the three ARE the lighting, weighted per pixel
/// by which way the normal map says the surface faces. Set 0 is what an unbumped renderer draws,
/// and it is why bumped faces look plausible here today.
/// </remarks>
public readonly record struct BspFaceLighting(
    BspLightmap Flat, IReadOnlyList<BspLightmap> Directional)
{
    /// <summary>Whether the face carries directional lighting.</summary>
    public bool IsBumped => Directional.Count > 0;
}

public static class BspLightmaps
{
    private const int LumpFaces = 7;
    private const int LumpTexinfo = 6;
    private const int LumpLighting = 8;
    private const int LumpLightingHdr = 53;

    private const int FaceStride = 56;
    private const int StylesOffset = 16;
    private const int LightOffsetOffset = 20;
    private const int LuxelSizeOffset = 36;
    private const int TexinfoIndexOffset = 10;

    private const int TexinfoStride = 72;
    private const int TexinfoFlagsOffset = 64;

    /// <summary>How many lightmaps a bump-lit face carries: one flat plus one per basis vector.</summary>
    /// <remarks>
    /// <c>NUM_BUMP_VECTS + 1</c> in Valve's own arithmetic, from <c>bumpvects.h</c>.
    /// </remarks>
    private const int BumpedSets = 4;

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

    /// <summary>Where one set of one style's samples begins.</summary>
    /// <param name="lightOffset">The face's own <c>lightofs</c>.</param>
    /// <param name="style">Which light style, zero for the map's normal appearance.</param>
    /// <param name="set">Which bump set, zero for the flat one.</param>
    /// <param name="luxels">Samples in one set, which is width times height.</param>
    /// <param name="sets">Sets per style: four when bump lit, one otherwise.</param>
    /// <returns>A byte offset into the lighting lump.</returns>
    /// <remarks>
    /// **Style-major, then bump set, then luxels** - four contiguous full lightmaps rather than
    /// four values interleaved per luxel. Not inferred; <c>vrad</c>'s radial.cpp states it:
    ///
    /// <code>
    /// pdata[bumpSample] = &amp;(*pdlightdata)[f->lightofs +
    ///     (k * bumpSampleCount + bumpSample) * fl->numluxels * 4];
    /// </code>
    ///
    /// With one set the whole thing collapses to what the single-set reader always did, which is
    /// the property that keeps every unbumped face in the map exactly where it was.
    /// </remarks>
    public static long SetOffset(long lightOffset, int style, int set, int luxels, int sets) =>
        lightOffset + ((((long)style * sets) + set) * luxels * SampleBytes);

    /// <summary>Reads every face's baked lighting, directional sets included.</summary>
    /// <param name="file">The map's bytes.</param>
    /// <returns>One entry per face.</returns>
    /// <exception cref="InvalidDataException">A face's samples fall outside the lighting lump.</exception>
    /// <remarks>
    /// **<see cref="Read"/> is not built on this, and that is deliberate.** The two run
    /// independently so a test can assert they agree on the flat set; if this one supplied that
    /// answer as well, the agreement would be a tautology. Set 0 of a bumped face sits at the same
    /// byte offset as an unbumped face's only set, so wrong arithmetic here still draws most of a
    /// map correctly and only a comparison against the older reader can see it.
    /// </remarks>
    public static IReadOnlyList<BspFaceLighting> ReadAll(ReadOnlyMemory<byte> file)
    {
        BspHeader header = BspHeader.Parse(file.Span);

        ReadOnlySpan<byte> faces = BspLumpData
            .ReadStructures(file, header.Lump(LumpFaces), FaceStride, "faces").Span;

        ReadOnlySpan<byte> texinfo = BspLumpData
            .ReadStructures(file, header.Lump(LumpTexinfo), TexinfoStride, "texinfo").Span;

        ReadOnlyMemory<byte> ldr = BspLumpData.Read(file, header.Lump(LumpLighting));
        ReadOnlySpan<byte> lighting = ldr.Length > 0
            ? ldr.Span
            : BspLumpData.Read(file, header.Lump(LumpLightingHdr)).Span;

        int count = faces.Length / FaceStride;
        List<BspFaceLighting> read = new(count);

        for (int index = 0; index < count; index++)
        {
            ReadOnlySpan<byte> face = faces.Slice(index * FaceStride, FaceStride);
            int offset = BinaryPrimitives.ReadInt32LittleEndian(face[LightOffsetOffset..]);

            if (offset == Unlit || lighting.Length == 0)
            {
                read.Add(new BspFaceLighting(default, []));
                continue;
            }

            int width = BinaryPrimitives.ReadInt32LittleEndian(face[LuxelSizeOffset..]) + 1;
            int height = BinaryPrimitives.ReadInt32LittleEndian(face[(LuxelSizeOffset + 4)..]) + 1;

            if (width is < 1 or > MaximumLuxels || height is < 1 or > MaximumLuxels)
            {
                throw new InvalidDataException(
                    $"Face {index} claims a {width}x{height} lightmap, which is not a real size.");
            }

            bool bumped = IsBumpLit(face, texinfo);
            int sets = bumped ? BumpedSets : 1;
            int luxels = width * height;

            BspLightmap flat = Set(lighting, offset, 0, luxels, sets, width, height, index);
            List<BspLightmap> directional = [];

            for (int set = 1; bumped && set < BumpedSets; set++)
            {
                directional.Add(Set(lighting, offset, set, luxels, sets, width, height, index));
            }

            read.Add(new BspFaceLighting(flat, directional));
        }

        return read;
    }

    /// <summary>Where each lit face's lighting begins, and how many bytes it occupies.</summary>
    /// <param name="file">The map's bytes.</param>
    /// <returns>One entry per lit face: its offset and its whole span, styles and sets included.</returns>
    /// <exception cref="InvalidDataException">A face claims a lightmap that is not a real size.</exception>
    /// <remarks>
    /// **Lengths are the only thing that can falsify the set count.** Set 0 sits at
    /// <c>lightofs + (0 * sets + 0) * luxels * 4</c>, so the set count cancels whenever the style
    /// is zero - which is every read this project makes. Comparing the flat set against the older
    /// single-set reader therefore cannot see a wrong set count at all, however obviously it looks
    /// like it should.
    ///
    /// A span can. vrad writes faces one after another with no padding, so a face's whole extent
    /// has to reach exactly the next face's offset. One face given the wrong number of sets and
    /// the arithmetic stops meeting its neighbour.
    ///
    /// Exposed rather than private because the test that uses it is the reason the numbers here
    /// are trustworthy, and a private version would have to be reached through something else that
    /// shares its assumptions.
    /// </remarks>
    public static IReadOnlyList<(int Offset, long Bytes, int Styles)> Spans(ReadOnlyMemory<byte> file)
    {
        BspHeader header = BspHeader.Parse(file.Span);

        ReadOnlySpan<byte> faces = BspLumpData
            .ReadStructures(file, header.Lump(LumpFaces), FaceStride, "faces").Span;

        ReadOnlySpan<byte> texinfo = BspLumpData
            .ReadStructures(file, header.Lump(LumpTexinfo), TexinfoStride, "texinfo").Span;

        int count = faces.Length / FaceStride;
        List<(int Offset, long Bytes, int Styles)> spans = new(count);

        for (int index = 0; index < count; index++)
        {
            ReadOnlySpan<byte> face = faces.Slice(index * FaceStride, FaceStride);
            int offset = BinaryPrimitives.ReadInt32LittleEndian(face[LightOffsetOffset..]);

            if (offset == Unlit)
            {
                continue;
            }

            int width = BinaryPrimitives.ReadInt32LittleEndian(face[LuxelSizeOffset..]) + 1;
            int height = BinaryPrimitives.ReadInt32LittleEndian(face[(LuxelSizeOffset + 4)..]) + 1;

            if (width is < 1 or > MaximumLuxels || height is < 1 or > MaximumLuxels)
            {
                throw new InvalidDataException(
                    $"Face {index} claims a {width}x{height} lightmap, which is not a real size.");
            }

            int sets = IsBumpLit(face, texinfo) ? BumpedSets : 1;

            int styles = StyleCount(face);

            spans.Add((offset, (long)styles * sets * width * height * SampleBytes, styles));
        }

        return spans;
    }

    /// <summary>Whether a face's texinfo asks for bump lighting.</summary>
    /// <remarks>
    /// <c>SURF_BUMPLIGHT</c>, which vbsp sets from the material rather than from the map author. A
    /// face whose material has no bump map does not carry the extra sets at all, so this decides
    /// how many bytes the face occupies as well as how it is drawn.
    /// </remarks>
    private static bool IsBumpLit(ReadOnlySpan<byte> face, ReadOnlySpan<byte> texinfo)
    {
        int index = BinaryPrimitives.ReadInt16LittleEndian(face[TexinfoIndexOffset..]);

        if (index < 0 || ((index + 1) * TexinfoStride) > texinfo.Length)
        {
            // A face without usable texinfo cannot be bump lit, and saying so here keeps the byte
            // arithmetic honest rather than reading three sets that are not there.
            return false;
        }

        int flags = BinaryPrimitives.ReadInt32LittleEndian(
            texinfo[((index * TexinfoStride) + TexinfoFlagsOffset)..]);

        return ((SurfaceProperties)flags & SurfaceProperties.BumpLight) != SurfaceProperties.None;
    }

    private static BspLightmap Set(
        ReadOnlySpan<byte> lighting,
        int lightOffset,
        int set,
        int luxels,
        int sets,
        int width,
        int height,
        int face)
    {
        long at = SetOffset(lightOffset, 0, set, luxels, sets);
        long bytes = (long)luxels * SampleBytes;

        if (at < 0 || at + bytes > lighting.Length)
        {
            throw new InvalidDataException(
                $"Face {face} needs {bytes} lighting bytes at {at} of {lighting.Length} for set {set}.");
        }

        return new BspLightmap(width, height, Decode(lighting.Slice((int)at, (int)bytes), luxels));
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
    /// **Left LINEAR, and halved.** Light is not a picture: the gamma curve belongs at the end of
    /// the pipeline, applied once by the sRGB render target, so applying it here put every later
    /// multiply in the wrong space (B54). Halving is Valve's overbright - a lightmap holds light
    /// brighter than white, and storing it halved is how that survives eight bits. The shader
    /// doubles it back, which is what Source's own shaders do.
    ///
    /// Both halves have to move together: gamma here with doubling in the shader blows the map
    /// out, and that is exactly the wrong turn this comment used to record.
    /// </remarks>
    private static byte[] Decode(ReadOnlySpan<byte> samples, int count)
    {
        byte[] pixels = new byte[count * 4];

        for (int index = 0; index < count; index++)
        {
            ReadOnlySpan<byte> sample = samples.Slice(index * SampleBytes, SampleBytes);
            float scale = MathF.Pow(2f, (sbyte)sample[3]);

            pixels[(index * 4) + 0] = Overbright(sample[0] * scale);
            pixels[(index * 4) + 1] = Overbright(sample[1] * scale);
            pixels[(index * 4) + 2] = Overbright(sample[2] * scale);
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
    /// <summary>Stores one linear channel halved, so the shader's overbright restores it.</summary>
    /// <remarks>
    /// A sample of 255 at exponent 0 is full brightness, so the range is a byte; halving leaves
    /// room for light above white, which is what "overbright" means and why the shader doubles.
    /// </remarks>
    private static byte Overbright(float linear) =>
        (byte)Math.Clamp(linear / 2f, 0f, 255f);
}
