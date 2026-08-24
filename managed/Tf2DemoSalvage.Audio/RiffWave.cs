using System;
using System.Buffers.Binary;

namespace Tf2DemoSalvage.Audio;

/// <summary>Audio encodings a RIFF/WAVE file may declare, as <c>tier2/riff.h</c> names them.</summary>
/// <remarks>
/// **Measured across both shipped sound archives before this was written**: of 2,817 WAVs, **2,815
/// are <see cref="Pcm"/> and two are <see cref="Adpcm"/>**. The two console formats do not occur —
/// they are Xbox 360 and out of scope by D46 — but they are named here so an encountered file is
/// REPORTED rather than falling into a default nobody reads.
/// </remarks>
public enum WaveFormat
{
    /// <summary>Not a recognised encoding.</summary>
    Unknown = 0,

    /// <summary><c>WAVE_FORMAT_PCM</c>: uncompressed samples. 2,815 of TF2's 2,817 WAVs.</summary>
    Pcm = 0x0001,

    /// <summary><c>WAVE_FORMAT_ADPCM</c>: Microsoft ADPCM. Two files in the whole game.</summary>
    Adpcm = 0x0002,

    /// <summary><c>WAVE_FORMAT_XBOX_ADPCM</c>: console only, and out of scope (D46).</summary>
    XboxAdpcm = 0x0069,

    /// <summary><c>WAVE_FORMAT_XMA</c>: console only, and out of scope (D46).</summary>
    Xma = 0x0165,
}

/// <summary>
/// A RIFF/WAVE file's format chunk and its sample data, read without decoding either.
/// </summary>
/// <param name="Format">The encoding the file declares.</param>
/// <param name="Channels">Channel count; 1 for the great majority of TF2's effects.</param>
/// <param name="SampleRate">Samples per second.</param>
/// <param name="BitsPerSample">Bits per sample per channel.</param>
/// <param name="Data">The <c>data</c> chunk's bytes, undecoded.</param>
/// <param name="LoopStart">
/// The sample the loop returns to, or -1 when the file does not loop. Source marks a looping wave
/// with a <c>cue </c> chunk (<c>tier2/riff.h:187</c>), so its presence IS the loop.
/// </param>
/// <remarks>
/// **The chunks are WALKED, never assumed.** A reader that takes the format at offset 20 works on
/// most files and produces a plausible wrong answer on the rest: Valve ships its own <c>VDAT</c>
/// and <c>PADD</c> chunks (both named in <c>tier2/riff.h</c>), and a sample rate read out of one of
/// those plays the sound at the wrong speed rather than failing.
///
/// **Odd-sized chunks carry a pad byte the size does not count.** RIFF is word-aligned, so skipping
/// exactly <c>size</c> lands one byte early on every subsequent chunk — and lands mid-chunk rather
/// than off the end, so it too fails as a wrong number rather than as an exception.
///
/// **Every length is treated as a stranger's.** A sound file comes out of the user's VPKs, but the
/// path that selects it comes out of a demo (D32), and a malformed file must not take down the
/// sound pass. Anything that does not parse yields <c>null</c>, which a caller can report as "this
/// one cannot be played" — the alternative being silence that reports nothing, which is the
/// characteristic failure of the whole audio area.
/// </remarks>
public readonly record struct RiffWave(
    WaveFormat Format,
    int Channels,
    int SampleRate,
    int BitsPerSample,
    ReadOnlyMemory<byte> Data,
    int LoopStart = -1)
{
    /// <summary>Whether the file is marked as looping.</summary>
    /// <remarks>
    /// A <c>cue </c> chunk is Source's loop marker, so its presence is the answer — see the walk in
    /// <c>Read</c>. -1 means the file carries none and plays once.
    /// </remarks>
    public bool Loops => LoopStart >= 0;

    /// <summary>Whether the samples can be used directly, without a decoder.</summary>
    public bool IsPcm => Format == WaveFormat.Pcm;

    /// <summary>Reads a RIFF/WAVE file.</summary>
    /// <param name="file">The whole file.</param>
    /// <returns>The parsed wave, or <c>null</c> if it is not a well-formed one.</returns>
    public static RiffWave? Read(ReadOnlyMemory<byte> file)
    {
        ReadOnlySpan<byte> bytes = file.Span;

        // RIFF header: "RIFF", size, "WAVE". Twelve bytes before any chunk, and the smallest
        // meaningful file also carries a 24-byte fmt and an 8-byte data header.
        if (bytes.Length < 44 ||
            !bytes[..4].SequenceEqual("RIFF"u8) ||
            !bytes.Slice(8, 4).SequenceEqual("WAVE"u8))
        {
            return null;
        }

        int at = 12;
        WaveFormat format = WaveFormat.Unknown;
        int channels = 0;
        int rate = 0;
        int bits = 0;
        ReadOnlyMemory<byte> data = default;
        int loopStart = -1;
        bool sawFormat = false;
        bool sawData = false;

        while (at + 8 <= bytes.Length)
        {
            ReadOnlySpan<byte> id = bytes.Slice(at, 4);
            int size = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(at + 4, 4));

            // **A malformed chunk STOPS the walk; it does not condemn the file.** A negative size,
            // or one past the end, is a length a stranger controls and must bound the read — but
            // refusing the whole file for it was wrong, and one shipped WAV proved it.
            //
            // `sound/player/taunt_eng_swoosh.wav` carries a valid `fmt ` at 12 and a valid `data`
            // at 36, then LIST, bext, and an `FLLR` filler chunk after which the chunk ids read as
            // `filr`, `ilrl` and finally four zero bytes — an authoring tool's padding that nothing
            // is meant to walk. Returning null there threw away audio that had already been read
            // correctly, and it was the single refusal out of 2,756 shipped files.
            //
            // The engine reads `fmt ` and `data` and does not care what trails them, so this stops
            // and lets the completeness check below decide. A file whose damage lands BEFORE both
            // chunks still yields null, which is what the hostile-length test asserts.
            if (size < 0 || at + 8 + (long)size > bytes.Length)
            {
                break;
            }

            int body = at + 8;

            if (id.SequenceEqual("fmt "u8))
            {
                // wFormatTag, nChannels, nSamplesPerSec, nAvgBytesPerSec, nBlockAlign,
                // wBitsPerSample — sixteen bytes, and an extended chunk may be longer.
                if (size < 16)
                {
                    return null;
                }

                format = (WaveFormat)BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(body, 2));
                channels = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(body + 2, 2));
                rate = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(body + 4, 4));
                bits = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(body + 14, 2));
                sawFormat = true;
            }
            else if (id.SequenceEqual("data"u8))
            {
                data = file.Slice(body, size);
                sawData = true;
            }
            else if (id.SequenceEqual("cue "u8) &&
                size >= 4 &&
                BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(body, 4)) is > 0 and { } points &&
                size >= 4 + (24 * (long)points))
            {
                // **`cue ` is how Source marks a looping sound**, `WAVE_CUE` in `tier2/riff.h:187`
                // and `soundcombiner.cpp:361`. Its presence is the loop; the first cue point's
                // sample offset is where the loop returns to.
                //
                // Without this a looping ambient plays its file once and the map falls silent —
                // six machine hums start at the beginning of cp_process and are meant to run for
                // the whole match (B169).
                //
                // Layout: a four-byte count, then 24 bytes per point. The sample offset is the last
                // field of the point, at +20. A count that does not fit the chunk is ignored rather
                // than read, since the count is a length a stranger controls.
                loopStart = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(body + 4 + 20, 4));
            }

            // **The pad byte.** RIFF aligns chunks to even offsets and the size excludes the pad,
            // so an odd size advances by one more than it says.
            at = body + size + (size % 2);
        }

        if (!sawFormat || !sawData || channels <= 0 || rate <= 0)
        {
            return null;
        }

        return new RiffWave(format, channels, rate, bits, data, loopStart);
    }
}
