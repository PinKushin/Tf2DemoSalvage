using System;

namespace Tf2DemoSalvage.Audio.Tests;

/// <summary>Authors MPEG-1 Layer III files from the format alone: valid frame headers over zeroed side info and main data.</summary>
/// <remarks>
/// **Authored, not taken** (`docs/memory/author-the-specimen-the-corpus-lacks.md`): no Valve bytes, nothing from the game
/// folder. A frame is the 4-byte header ISO/IEC 11172-3 §2.4.1.3 gives — sync `0xFFF`, ID 1 (MPEG-1), layer `01`
/// (Layer III), protection bit 1 (no CRC), then bitrate index 9 (128 kbit/s), sampling index 0 (44.1 kHz), no padding,
/// and the mode — followed by zeros. Zeroed side info means zero `part2_3_length` and zero `global_gain` for every
/// granule, so each frame is 1,152 samples of silence per channel. Frame length is 144 × 128,000 / 44,100 = 417 bytes.
/// </remarks>
internal static class SyntheticMp3
{
    /// <summary>Samples per channel in one MPEG-1 Layer III frame.</summary>
    public const int SamplesPerFrame = 1152;

    /// <summary>The sampling rate the header declares.</summary>
    public const int SampleRate = 44100;

    private const int FrameBytes = 417;

    /// <summary>A file of <paramref name="frames"/> silent frames.</summary>
    /// <param name="frames">How many frames.</param>
    /// <param name="stereo">Two channels (mode 0) rather than one (mode 3).</param>
    public static byte[] Silent(int frames, bool stereo = false)
    {
        byte[] file = new byte[frames * FrameBytes];

        for (int frame = 0; frame < frames; frame++)
        {
            int at = frame * FrameBytes;
            file[at] = 0xFF;
            file[at + 1] = 0xFB;
            file[at + 2] = 0x90;
            file[at + 3] = stereo ? (byte)0x00 : (byte)0xC0;
        }

        return file;
    }
}
