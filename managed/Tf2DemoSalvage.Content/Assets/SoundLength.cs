using System;
using System.Buffers.Binary;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>
/// How long a sound file plays, from its own header — what the sound cache stores as the sample count, for a file the
/// cache does not list (B513).
/// </summary>
/// <remarks>
/// **A channel ends when the mixer runs out of the source's data**, so its length is the file's sample count over its
/// rate: a wave's <c>data</c> chunk over its block size, an MP3's frames times the samples each frame decodes to. The
/// cache's own sample counts are the control — see the <c>flex lengths</c> probe.
/// </remarks>
public static class SoundLength
{
    /// <summary>The file's length in seconds, or null when the bytes are neither a readable wave nor an MP3.</summary>
    /// <param name="file">The whole file.</param>
    /// <returns>Seconds, or null.</returns>
    public static float? Seconds(ReadOnlySpan<byte> file) =>
        file.Length >= 12 && file[..4].SequenceEqual("RIFF"u8) && file.Slice(8, 4).SequenceEqual("WAVE"u8)
            ? Wave(file)
            : Mp3(file);

    /// <summary>A RIFF/WAVE file: the <c>data</c> chunk's bytes over the block size, in sample frames.</summary>
    private static float? Wave(ReadOnlySpan<byte> file)
    {
        int at = 12, rate = 0, blockAlign = 0, samplesPerBlock = 0, format = 0;
        long dataSize = -1;

        while (at + 8 <= file.Length)
        {
            ReadOnlySpan<byte> id = file.Slice(at, 4);
            int size = BinaryPrimitives.ReadInt32LittleEndian(file.Slice(at + 4, 4));

            if (size < 0 || at + 8 + (long)size > file.Length)
            {
                break;
            }

            int body = at + 8;

            if (id.SequenceEqual("fmt "u8) && size >= 16)
            {
                format = BinaryPrimitives.ReadUInt16LittleEndian(file.Slice(body, 2));
                rate = BinaryPrimitives.ReadInt32LittleEndian(file.Slice(body + 4, 4));
                blockAlign = BinaryPrimitives.ReadUInt16LittleEndian(file.Slice(body + 12, 2));

                // WAVE_FORMAT_ADPCM: wSamplesPerBlock follows cbSize.
                if (format == 2 && size >= 20)
                {
                    samplesPerBlock = BinaryPrimitives.ReadUInt16LittleEndian(file.Slice(body + 18, 2));
                }
            }
            else if (id.SequenceEqual("data"u8))
            {
                dataSize = size;
            }

            at = body + size + (size % 2);
        }

        if (rate <= 0 || blockAlign <= 0 || dataSize < 0)
        {
            return null;
        }

        long samples = format == 2 && samplesPerBlock > 0
            ? dataSize / blockAlign * samplesPerBlock
            : dataSize / blockAlign;

        return (float)samples / rate;
    }

    /// <summary>MPEG audio: every frame's samples, walked from the first frame header past any ID3v2 tag.</summary>
    private static float? Mp3(ReadOnlySpan<byte> file)
    {
        int at = 0;

        // ID3v2: "ID3", version, flags, then a four-byte syncsafe size; a footer (flag 0x10) adds ten more.
        if (file.Length >= 10 && file[..3].SequenceEqual("ID3"u8))
        {
            int tag = (file[6] << 21) | (file[7] << 14) | (file[8] << 7) | file[9];
            at = 10 + tag + ((file[5] & 0x10) != 0 ? 10 : 0);
        }

        int rate = 0;
        long samples = 0;

        while (at + 4 <= file.Length)
        {
            if (Frame(file.Slice(at, 4)) is not { } frame)
            {
                if (samples > 0)
                {
                    break;
                }

                at++;
                continue;
            }

            rate = rate == 0 ? frame.Rate : rate;
            samples += frame.Samples;
            at += frame.Length;
        }

        return rate > 0 ? (float)samples / rate : null;
    }

    private static readonly int[] Mpeg1Layer3Kbps = [0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320];

    private static readonly int[] Mpeg2Layer3Kbps = [0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160];

    /// <summary>One Layer III frame header: its byte length, its sample count and rate; null when it is not one.</summary>
    private static (int Length, int Samples, int Rate)? Frame(ReadOnlySpan<byte> header)
    {
        if (header[0] != 0xFF || (header[1] & 0xE0) != 0xE0)
        {
            return null;
        }

        int version = (header[1] >> 3) & 3; // 3 MPEG1, 2 MPEG2, 0 MPEG2.5
        int layer = (header[1] >> 1) & 3; // 1 Layer III
        int bitrateIndex = header[2] >> 4;
        int rateIndex = (header[2] >> 2) & 3;
        int padding = (header[2] >> 1) & 1;

        if (version == 1 || layer != 1 || bitrateIndex is 0 or 15 || rateIndex == 3)
        {
            return null;
        }

        int baseRate = rateIndex switch { 0 => 44100, 1 => 48000, _ => 32000 };
        int rate = version switch { 3 => baseRate, 2 => baseRate / 2, _ => baseRate / 4 };
        bool mpeg1 = version == 3;
        int kbps = (mpeg1 ? Mpeg1Layer3Kbps : Mpeg2Layer3Kbps)[bitrateIndex];
        int samples = mpeg1 ? 1152 : 576;
        int length = (samples / 8 * kbps * 1000 / rate) + padding;

        return length < 4 ? null : (length, samples, rate);
    }
}
