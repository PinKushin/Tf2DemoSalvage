using System;
using System.Buffers.Binary;
using System.Globalization;
using System.IO;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>Pixel formats a VTF stores its images in.</summary>
/// <remarks>
/// Valve's full list is longer; these are the ones TF2's world materials actually use. An unknown
/// format is reported rather than guessed, because a wrong guess decodes to plausible colours.
/// </remarks>
public enum VtfFormat
{
    /// <summary>Not a format this reader understands.</summary>
    Unknown = -2,

    /// <summary>No image at this slot.</summary>
    None = -1,

    /// <summary>32-bit RGBA.</summary>
    Rgba8888 = 0,

    /// <summary>24-bit RGB.</summary>
    Rgb888 = 2,

    /// <summary>24-bit BGR.</summary>
    Bgr888 = 3,

    /// <summary>32-bit BGRA.</summary>
    Bgra8888 = 12,

    /// <summary>Block compressed, 4 bits per pixel, one-bit alpha.</summary>
    Dxt1 = 13,

    /// <summary>Block compressed, 8 bits per pixel, explicit alpha.</summary>
    Dxt3 = 14,

    /// <summary>Block compressed, 8 bits per pixel, interpolated alpha.</summary>
    Dxt5 = 15,

    /// <summary>DXT1 with one bit of alpha.</summary>
    /// <remarks>
    /// **This was 26, and 26 is <c>IMAGE_FORMAT_UVLX8888</c>.** The real value is 20, counted by
    /// position in <c>public/bitmap/imageformat.h</c> — the enum assigns a number to only two of its
    /// forty members, so every other format is defined by where it sits in the list and cannot be
    /// checked by reading one line.
    ///
    /// The error cost both directions. A VTF declaring 20 — a genuine DXT1-with-alpha texture — fell
    /// through to <c>Unknown</c> and was reported unsupported, so the surface went untextured. A VTF
    /// declaring 26 would have had a 32-bit uncompressed image decoded as 4-bit block compression,
    /// which is not a subtle difference but is also not an error.
    ///
    /// Found by <c>ImageFormatConformanceTests</c> the first time it ran.
    /// </remarks>
    Dxt1OneBitAlpha = 20,
}

/// <summary>
/// Reads a Valve Texture File down to RGBA pixels.
/// </summary>
/// <remarks>
/// **The game's own textures, at the game's own resolution.** A VTF holds a mip chain, smallest
/// first, and the renderer picks a level: full size for a close camera, a smaller mip for an
/// overhead view of a whole map. That choice is what makes drawing 13,000 textured faces
/// affordable, and it is Valve's own data doing the work rather than anything downsampled here.
///
/// <code>
///   header: "VTF\0", version, headerSize, width, height, flags, frames,
///           firstFrame, reflectivity, bumpScale, highResFormat, mipCount,
///           lowResFormat, lowResWidth, lowResHeight
///   then:   the low-res thumbnail, then every mip of every frame, SMALLEST FIRST
/// </code>
///
/// **Mips are stored smallest first, and that ordering is the thing to get right.** Reading the
/// data at the start of the image section gives a 1x1 texture that decodes perfectly and looks
/// like a solid colour — an error that produces a picture rather than an exception.
///
/// **DXT is decoded here rather than uploaded compressed**, because the caller wants pixels it can
/// also sample on the CPU. D3D11 could take the blocks directly and that is a later optimisation;
/// the decode is a few lines per block and runs once per texture.
/// </remarks>
public sealed class VtfTexture
{
    /// <summary>The four bytes a VTF starts with.</summary>
    private static ReadOnlySpan<byte> Magic => "VTF\0"u8;

    /// <summary>The bit that marks a texture as a self-shadowing bump map.</summary>
    /// <remarks>
    /// <c>TEXTUREFLAGS_SSBUMP</c> from <c>src/public/vtf/vtf.h</c>.
    /// </remarks>
    internal const uint SelfShadowBumpFlag = 0x08000000;

    private VtfTexture(
        int width, int height, VtfFormat format, int mipCount, byte[] pixels, int level, uint flags)
    {
        Width = width;
        Height = height;
        Format = format;
        MipCount = mipCount;
        Pixels = pixels;
        Level = level;
        Flags = flags;
    }

    /// <summary>Width of the decoded image.</summary>
    public int Width { get; }

    /// <summary>Height of the decoded image.</summary>
    public int Height { get; }

    /// <summary>Format the image was stored in.</summary>
    public VtfFormat Format { get; }

    /// <summary>How many mip levels the file holds.</summary>
    public int MipCount { get; }

    /// <summary>Which mip was decoded; 0 is full size.</summary>
    public int Level { get; }

    /// <summary>The header's flags word, as written.</summary>
    /// <remarks>
    /// Kept whole rather than unpacked into properties. Most of the bits describe how the texture
    /// was compiled — clamping, point sampling, no mipmaps — and matter to the engine rather than
    /// to a viewer; the one that changes what is drawn is exposed as
    /// <see cref="IsSelfShadowBump"/>.
    /// </remarks>
    public uint Flags { get; }

    /// <summary>Whether the texture is a self-shadowing bump map rather than a colour.</summary>
    /// <remarks>
    /// **This overrides what a material says.** Valve's own helper reads the detail texture's flags
    /// and forces the combine mode to 10 or 11 whatever <c>$detailblendmode</c> asked for:
    ///
    /// <code>
    /// if ( pDetailTexture-&gt;GetFlags() &amp; TEXTUREFLAGS_SSBUMP )
    ///     nDetailBlendMode = hasBump ? 10 : 11;
    /// </code>
    ///
    /// So an absent <c>$detailblendmode</c> does not mean mode 0, and a caller that trusts the
    /// material alone applies a mod2x to what is actually a normal map — a pattern that looks like
    /// grain rather than like a defect.
    /// </remarks>
    public bool IsSelfShadowBump => (Flags & SelfShadowBumpFlag) != 0;

    /// <summary>The decoded image, four bytes per pixel, red first.</summary>
    /// <remarks>
    /// An array rather than a copy-returning property on purpose: this is a megabyte or more per
    /// texture and its whole reason for existing is to be handed to <c>UpdateSubresource</c>.
    /// Copying it defensively would double the cost of the one operation it exists for.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1819:Properties should not return arrays",
        Justification = "The pixel buffer is uploaded to the GPU; a defensive copy would double the cost.")]
    public byte[] Pixels { get; }

    /// <summary>Decodes a texture.</summary>
    /// <param name="file">The VTF's bytes.</param>
    /// <param name="maximumSize">
    /// Largest edge to decode; the smallest mip at least this size is chosen. Zero means full size.
    /// </param>
    /// <returns>The decoded image.</returns>
    /// <exception cref="InvalidDataException">The file is not a VTF, or uses a format not read here.</exception>
    /// <remarks>
    /// **The size limit picks a mip rather than resampling.** Valve already generated the chain, so
    /// asking for a 256-pixel version of a 2048-pixel texture is a smaller read and a smaller
    /// upload, not a downscale of something already paid for.
    /// </remarks>
    public static VtfTexture Decode(ReadOnlyMemory<byte> file, int maximumSize = 0)
    {
        ReadOnlySpan<byte> span = file.Span;

        if (span.Length < 64 || !span[..4].SequenceEqual(Magic))
        {
            throw new InvalidDataException("This is not a VTF: the file does not start with 'VTF'.");
        }

        int headerSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(span[12..]);
        int width = BinaryPrimitives.ReadUInt16LittleEndian(span[16..]);
        int height = BinaryPrimitives.ReadUInt16LittleEndian(span[18..]);
        uint flags = BinaryPrimitives.ReadUInt32LittleEndian(span[20..]);
        int frames = BinaryPrimitives.ReadUInt16LittleEndian(span[24..]);
        VtfFormat format = ToFormat(BinaryPrimitives.ReadInt32LittleEndian(span[52..]));
        int mipCount = span[56];
        VtfFormat lowResFormat = ToFormat(BinaryPrimitives.ReadInt32LittleEndian(span[57..]));
        int lowResWidth = span[61];
        int lowResHeight = span[62];

        if (width <= 0 || height <= 0 || mipCount <= 0)
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"A VTF of {width}x{height} with {mipCount} mips is not readable."));
        }

        if (format is VtfFormat.Unknown)
        {
            throw new InvalidDataException(
                $"VTF pixel format {BinaryPrimitives.ReadInt32LittleEndian(span[52..])} is not supported.");
        }

        // The thumbnail sits between the header and the images, and is always DXT1 when present.
        int at = headerSize;

        if (lowResFormat is not VtfFormat.None && lowResWidth > 0 && lowResHeight > 0)
        {
            at += SizeOf(VtfFormat.Dxt1, lowResWidth, lowResHeight);
        }

        int level = ChooseLevel(width, height, mipCount, maximumSize);

        // **Smallest mip first.** Level mipCount-1 is 1x1, level 0 is full size, so the wanted
        // level's data sits after every level below it.
        for (int smaller = mipCount - 1; smaller > level; smaller--)
        {
            at += SizeOf(format, MipSize(width, smaller), MipSize(height, smaller)) * frames;
        }

        int levelWidth = MipSize(width, level);
        int levelHeight = MipSize(height, level);
        int bytes = SizeOf(format, levelWidth, levelHeight);

        if (at < 0 || (long)at + bytes > span.Length)
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"Mip {level} of a VTF needs {bytes} bytes at {at} in a {span.Length}-byte file."));
        }

        byte[] pixels = Decode(span.Slice(at, bytes), format, levelWidth, levelHeight);

        return new VtfTexture(levelWidth, levelHeight, format, mipCount, pixels, level, flags);
    }

    /// <summary>Picks the smallest mip whose longest edge still reaches a size.</summary>
    private static int ChooseLevel(int width, int height, int mipCount, int maximumSize)
    {
        if (maximumSize <= 0)
        {
            return 0;
        }

        for (int level = mipCount - 1; level > 0; level--)
        {
            if (Math.Max(MipSize(width, level), MipSize(height, level)) >= maximumSize)
            {
                return level;
            }
        }

        return 0;
    }

    /// <summary>Size of one dimension at a mip level, never below one.</summary>
    private static int MipSize(int size, int level) => Math.Max(1, size >> level);

    private static int SizeOf(VtfFormat format, int width, int height) => format switch
    {
        VtfFormat.Dxt1 or VtfFormat.Dxt1OneBitAlpha => BlockCount(width, height) * 8,
        VtfFormat.Dxt3 or VtfFormat.Dxt5 => BlockCount(width, height) * 16,
        VtfFormat.Rgba8888 or VtfFormat.Bgra8888 => width * height * 4,
        VtfFormat.Rgb888 or VtfFormat.Bgr888 => width * height * 3,
        _ => throw new InvalidDataException($"VTF format {format} has no known size."),
    };

    /// <summary>Blocks in a DXT image; a 1x1 image still costs one whole 4x4 block.</summary>
    private static int BlockCount(int width, int height) =>
        Math.Max(1, (width + 3) / 4) * Math.Max(1, (height + 3) / 4);

    private static byte[] Decode(
        ReadOnlySpan<byte> source, VtfFormat format, int width, int height)
    {
        byte[] pixels = new byte[width * height * 4];

        switch (format)
        {
            case VtfFormat.Rgba8888:
                source.CopyTo(pixels);
                break;

            case VtfFormat.Bgra8888:
                for (int index = 0; index < width * height; index++)
                {
                    pixels[(index * 4) + 0] = source[(index * 4) + 2];
                    pixels[(index * 4) + 1] = source[(index * 4) + 1];
                    pixels[(index * 4) + 2] = source[(index * 4) + 0];
                    pixels[(index * 4) + 3] = source[(index * 4) + 3];
                }

                break;

            case VtfFormat.Rgb888:
            case VtfFormat.Bgr888:
                bool swap = format == VtfFormat.Bgr888;

                for (int index = 0; index < width * height; index++)
                {
                    byte first = source[(index * 3) + 0];
                    byte third = source[(index * 3) + 2];

                    pixels[(index * 4) + 0] = swap ? third : first;
                    pixels[(index * 4) + 1] = source[(index * 3) + 1];
                    pixels[(index * 4) + 2] = swap ? first : third;
                    pixels[(index * 4) + 3] = 255;
                }

                break;

            case VtfFormat.Dxt1:
            case VtfFormat.Dxt1OneBitAlpha:
                DecodeDxt(source, pixels, width, height, blockBytes: 8, hasAlphaBlock: false, dxt5: false);
                break;

            case VtfFormat.Dxt3:
                DecodeDxt(source, pixels, width, height, blockBytes: 16, hasAlphaBlock: true, dxt5: false);
                break;

            case VtfFormat.Dxt5:
                DecodeDxt(source, pixels, width, height, blockBytes: 16, hasAlphaBlock: true, dxt5: true);
                break;

            default:
                throw new InvalidDataException($"VTF format {format} cannot be decoded.");
        }

        return pixels;
    }

    /// <summary>Expands DXT blocks into RGBA.</summary>
    /// <remarks>
    /// Each 4x4 block stores two 16-bit endpoint colours and sixteen two-bit indices. DXT1 gets a
    /// one-bit alpha when the first endpoint is not greater than the second, which is why the
    /// comparison decides the interpolation rather than the format alone.
    /// </remarks>
    private static void DecodeDxt(
        ReadOnlySpan<byte> source,
        byte[] pixels,
        int width,
        int height,
        int blockBytes,
        bool hasAlphaBlock,
        bool dxt5)
    {
        int blocksAcross = Math.Max(1, (width + 3) / 4);
        int blocksDown = Math.Max(1, (height + 3) / 4);
        Span<byte> alpha = stackalloc byte[16];
        Span<int> reds = stackalloc int[4];
        Span<int> greens = stackalloc int[4];
        Span<int> blues = stackalloc int[4];
        Span<int> alphas = stackalloc int[4];

        for (int blockY = 0; blockY < blocksDown; blockY++)
        {
            for (int blockX = 0; blockX < blocksAcross; blockX++)
            {
                int at = ((blockY * blocksAcross) + blockX) * blockBytes;
                ReadOnlySpan<byte> block = source.Slice(at, blockBytes);

                alpha.Fill(255);

                if (hasAlphaBlock)
                {
                    ReadAlpha(block[..8], alpha, dxt5);
                    block = block[8..];
                }

                ushort first = BinaryPrimitives.ReadUInt16LittleEndian(block);
                ushort second = BinaryPrimitives.ReadUInt16LittleEndian(block[2..]);
                uint indices = BinaryPrimitives.ReadUInt32LittleEndian(block[4..]);

                Unpack565(first, out reds[0], out greens[0], out blues[0]);
                Unpack565(second, out reds[1], out greens[1], out blues[1]);
                alphas[0] = 255;
                alphas[1] = 255;

                if (first > second || hasAlphaBlock)
                {
                    reds[2] = ((2 * reds[0]) + reds[1]) / 3;
                    greens[2] = ((2 * greens[0]) + greens[1]) / 3;
                    blues[2] = ((2 * blues[0]) + blues[1]) / 3;
                    reds[3] = (reds[0] + (2 * reds[1])) / 3;
                    greens[3] = (greens[0] + (2 * greens[1])) / 3;
                    blues[3] = (blues[0] + (2 * blues[1])) / 3;
                    alphas[2] = 255;
                    alphas[3] = 255;
                }
                else
                {
                    // The one-bit-alpha form: index 3 is transparent black.
                    reds[2] = (reds[0] + reds[1]) / 2;
                    greens[2] = (greens[0] + greens[1]) / 2;
                    blues[2] = (blues[0] + blues[1]) / 2;
                    alphas[2] = 255;
                    reds[3] = 0;
                    greens[3] = 0;
                    blues[3] = 0;
                    alphas[3] = 0;
                }

                for (int row = 0; row < 4; row++)
                {
                    for (int column = 0; column < 4; column++)
                    {
                        int x = (blockX * 4) + column;
                        int y = (blockY * 4) + row;

                        if (x >= width || y >= height)
                        {
                            // A block at the edge of a non-multiple-of-four image runs past it.
                            continue;
                        }

                        int pixel = (row * 4) + column;
                        int selector = (int)((indices >> (pixel * 2)) & 3);
                        int destination = ((y * width) + x) * 4;

                        pixels[destination + 0] = (byte)reds[selector];
                        pixels[destination + 1] = (byte)greens[selector];
                        pixels[destination + 2] = (byte)blues[selector];
                        pixels[destination + 3] = (byte)Math.Min(alphas[selector], alpha[pixel]);
                    }
                }
            }
        }
    }

    /// <summary>Reads a block's alpha, either four-bit explicit or interpolated.</summary>
    private static void ReadAlpha(ReadOnlySpan<byte> block, Span<byte> alpha, bool dxt5)
    {
        if (!dxt5)
        {
            // DXT3: sixteen four-bit values, expanded to eight bits.
            for (int pixel = 0; pixel < 16; pixel++)
            {
                int nibble = (block[pixel / 2] >> ((pixel % 2) * 4)) & 0xF;
                alpha[pixel] = (byte)((nibble << 4) | nibble);
            }

            return;
        }

        Span<int> table = stackalloc int[8];
        table[0] = block[0];
        table[1] = block[1];

        if (table[0] > table[1])
        {
            for (int step = 1; step < 7; step++)
            {
                table[step + 1] = (((7 - step) * table[0]) + (step * table[1])) / 7;
            }
        }
        else
        {
            for (int step = 1; step < 5; step++)
            {
                table[step + 1] = (((5 - step) * table[0]) + (step * table[1])) / 5;
            }

            table[6] = 0;
            table[7] = 255;
        }

        // Sixteen three-bit indices packed into six bytes, read as two 24-bit runs.
        long packed = block[2] | ((long)block[3] << 8) | ((long)block[4] << 16) |
            ((long)block[5] << 24) | ((long)block[6] << 32) | ((long)block[7] << 40);

        for (int pixel = 0; pixel < 16; pixel++)
        {
            alpha[pixel] = (byte)table[(int)((packed >> (pixel * 3)) & 7)];
        }
    }

    private static void Unpack565(ushort colour, out int red, out int green, out int blue)
    {
        // Five and six-bit channels expanded to eight by repeating the high bits, so full-scale
        // values reach 255 rather than 248.
        red = ((colour >> 11) & 0x1F) * 255 / 31;
        green = ((colour >> 5) & 0x3F) * 255 / 63;
        blue = (colour & 0x1F) * 255 / 31;
    }

    private static VtfFormat ToFormat(int value) => value switch
    {
        -1 => VtfFormat.None,
        0 => VtfFormat.Rgba8888,
        2 => VtfFormat.Rgb888,
        3 => VtfFormat.Bgr888,
        12 => VtfFormat.Bgra8888,
        13 => VtfFormat.Dxt1,
        14 => VtfFormat.Dxt3,
        15 => VtfFormat.Dxt5,
        26 => VtfFormat.Dxt1OneBitAlpha,
        _ => VtfFormat.Unknown,
    };
}
