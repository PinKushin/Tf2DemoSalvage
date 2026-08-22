using System;
using System.Buffers.Binary;
using System.IO;

using NLayer;

namespace Tf2DemoSalvage.Audio;

/// <summary>Decoded audio, ready to mix.</summary>
/// <param name="SampleRate">Frames per second, as the file declares it.</param>
/// <param name="Channels">1 for mono, 2 for stereo.</param>
/// <param name="Samples">
/// Interleaved samples normalised to −1…1, channel 0 first within each frame.
/// </param>
/// <remarks>
/// **One type for both containers, because the mixer must not care which it got.** TF2 ships 82% of
/// its sounds as MP3 and 18% as WAV, and the same weapon can be either across eras — a mixer that
/// branched on container would carry that distinction into every downstream decision for no reason.
///
/// **Float rather than the 16-bit the WAVs actually hold.** The mixer sums many sounds at once and
/// applies a gain per sound, so intermediate values leave the source range routinely; doing that in
/// <c>short</c> means clipping at every step rather than once at the output. It is also what NLayer
/// produces, so the MP3 path costs no conversion. This is a representation choice and not a
/// departure from D51 — reproducing Valve's mixing is about the gains and the attenuation, not about
/// the width of the accumulator.
/// </remarks>
public readonly record struct SoundSample(int SampleRate, int Channels, ReadOnlyMemory<float> Samples)
{
    /// <summary>How long this sound lasts.</summary>
    /// <remarks>
    /// Computed rather than stored, so it cannot disagree with the samples. Guards a zero rate
    /// because a malformed header can carry one and the division would otherwise be the crash.
    /// </remarks>
    public TimeSpan Duration =>
        SampleRate > 0 && Channels > 0
            ? TimeSpan.FromSeconds((double)Samples.Length / Channels / SampleRate)
            : TimeSpan.Zero;

    /// <summary>Number of frames, one per instant regardless of channel count.</summary>
    public int FrameCount => Channels > 0 ? Samples.Length / Channels : 0;
}

/// <summary>The outcome of trying to decode a sound, including why it was refused.</summary>
/// <param name="Sample">The decoded audio, or null when it could not be decoded.</param>
/// <param name="Refusal">Why it was refused, or null on success.</param>
/// <remarks>
/// **The refusal is carried rather than dropped, and that is a recorded requirement.** Two of TF2's
/// 2,817 WAVs are ADPCM, and the decision to defer ADPCM was taken explicitly *"provided it is
/// reported rather than silently skipped"* (`docs/findings/31-game-audio.md`). A decoder returning
/// bare null makes "this format is not implemented" indistinguishable from "this file is corrupt"
/// and from "nothing was playing" — and silence that reports nothing is the characteristic failure
/// of this whole area.
/// </remarks>
public readonly record struct SoundSampleResult(SoundSample? Sample, string? Refusal)
{
    /// <summary>Whether there is audio to play.</summary>
    public bool Succeeded => Sample is not null;

    /// <summary>A refusal carrying its reason.</summary>
    internal static SoundSampleResult Refused(string reason) => new(null, reason);

    /// <summary>A success.</summary>
    internal static SoundSampleResult Decoded(SoundSample sample) => new(sample, null);
}

/// <summary>
/// Decodes a sound file into samples, whichever container TF2 shipped it in.
/// </summary>
/// <remarks>
/// **The container is sniffed from the bytes, never from the path.** A precached name's extension is
/// what the demo *asked* for, and <see cref="SoundFile"/> may have satisfied it from the other
/// container entirely — 60 of the corpus's 63 unopenable sounds are `.wav` names served by `.mp3`
/// files. Trusting the extension here would hand MP3 bytes to the RIFF walk and get a refusal for a
/// file that is perfectly good.
/// </remarks>
public static class SoundSampleReader
{
    /// <summary>Decodes a sound file.</summary>
    /// <param name="file">The whole file.</param>
    /// <returns>The samples, or a refusal naming why not.</returns>
    /// <remarks>
    /// Nothing here throws on bad input. These bytes come from the user's archives but the path that
    /// selected them comes from a demo (D32), so a malformed file must cost its own sound and
    /// nothing else.
    /// </remarks>
    public static SoundSampleResult Read(ReadOnlyMemory<byte> file)
    {
        if (file.Length < 4)
        {
            return SoundSampleResult.Refused("shorter than any header");
        }

        ReadOnlySpan<byte> head = file.Span;

        // "RIFF". Checked before the MP3 sync, because a RIFF header cannot be mistaken for one and
        // the reverse is not guaranteed.
        if (head[0] == (byte)'R' && head[1] == (byte)'I' && head[2] == (byte)'F' && head[3] == (byte)'F')
        {
            return FromWave(file);
        }

        return LooksLikeMp3(head)
            ? FromMp3(file)
            : SoundSampleResult.Refused("not RIFF and no MP3 frame sync");
    }

    /// <summary>Whether these bytes open as MP3.</summary>
    /// <remarks>
    /// **Two shapes, because a shipped MP3 usually does not start with audio.** An `ID3` tag comes
    /// first on most of TF2's voice lines; a bare stream starts with an 11-bit frame sync — `0xFF`
    /// then the top three bits of the next byte set. Checking only the sync would refuse every
    /// tagged file, which is most of them.
    /// </remarks>
    private static bool LooksLikeMp3(ReadOnlySpan<byte> head) =>
        (head[0] == (byte)'I' && head[1] == (byte)'D' && head[2] == (byte)'3') ||
        (head[0] == 0xFF && (head[1] & 0xE0) == 0xE0);

    /// <summary>Decodes a RIFF/WAVE file.</summary>
    private static SoundSampleResult FromWave(ReadOnlyMemory<byte> file)
    {
        if (RiffWave.Read(file) is not { } wave)
        {
            return SoundSampleResult.Refused("RIFF header did not parse");
        }

        if (!wave.IsPcm)
        {
            // Two of TF2's 2,817 WAVs are ADPCM. Named rather than lumped in with corruption, so a
            // report can say which of the two problems it is.
            return SoundSampleResult.Refused($"unsupported wave format {wave.Format}");
        }

        if (wave.Channels is < 1 or > 2 || wave.SampleRate <= 0)
        {
            return SoundSampleResult.Refused(
                $"unusable format: {wave.Channels} channels at {wave.SampleRate} Hz");
        }

        return wave.BitsPerSample switch
        {
            16 => SoundSampleResult.Decoded(
                new SoundSample(wave.SampleRate, wave.Channels, Sixteen(wave.Data.Span))),
            8 => SoundSampleResult.Decoded(
                new SoundSample(wave.SampleRate, wave.Channels, Eight(wave.Data.Span))),
            _ => SoundSampleResult.Refused($"unsupported bit depth {wave.BitsPerSample}"),
        };
    }

    /// <summary>Converts signed 16-bit little-endian PCM to normalised floats.</summary>
    /// <remarks>
    /// **Divided by 32768, not 32767.** Two's complement runs −32768…32767, so 32768 is the
    /// magnitude of the most negative sample; dividing by 32767 lets that one value reach −1.000031
    /// and clip. The asymmetry is in the format, not in the arithmetic.
    /// </remarks>
    private static float[] Sixteen(ReadOnlySpan<byte> data)
    {
        float[] samples = new float[data.Length / 2];

        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = BinaryPrimitives.ReadInt16LittleEndian(data[(i * 2)..]) / 32768f;
        }

        return samples;
    }

    /// <summary>Converts unsigned 8-bit PCM to normalised floats.</summary>
    /// <remarks>
    /// **8-bit WAV is UNSIGNED and centred on 128**, unlike every wider depth, which is signed. A
    /// reader treating it as signed produces audio that is inverted and offset — audible as a click
    /// and a hum rather than as an error.
    /// </remarks>
    private static float[] Eight(ReadOnlySpan<byte> data)
    {
        float[] samples = new float[data.Length];

        for (int i = 0; i < data.Length; i++)
        {
            samples[i] = (data[i] - 128) / 128f;
        }

        return samples;
    }

    /// <summary>Decodes an MP3 through NLayer.</summary>
    /// <remarks>
    /// NLayer reads from a <see cref="Stream"/>, so the bytes are wrapped rather than copied.
    /// Decoding is eager: a TF2 sound is a weapon effect or a voice line, seconds at most, and the
    /// mixer wants random access to it rather than a stream it must pump.
    /// </remarks>
    private static SoundSampleResult FromMp3(ReadOnlyMemory<byte> file)
    {
        try
        {
            using MemoryStream stream = new(file.ToArray(), writable: false);
            using MpegFile mpeg = new(stream);

            if (mpeg.Channels is < 1 or > 2 || mpeg.SampleRate <= 0)
            {
                return SoundSampleResult.Refused(
                    $"unusable mp3 format: {mpeg.Channels} channels at {mpeg.SampleRate} Hz");
            }

            // Length is in samples across all channels when known; when it is not, decode until the
            // stream stops giving. A VBR file without a Xing header reports no length, and refusing
            // those would lose real sounds.
            long declared = mpeg.Length;
            int capacity = declared is > 0 and < int.MaxValue ? (int)declared : 1 << 16;

            float[] buffer = new float[capacity];
            int filled = 0;

            while (true)
            {
                if (filled == buffer.Length)
                {
                    Array.Resize(ref buffer, buffer.Length * 2);
                }

                int read = mpeg.ReadSamples(buffer, filled, buffer.Length - filled);

                if (read <= 0)
                {
                    break;
                }

                filled += read;
            }

            return filled == 0
                ? SoundSampleResult.Refused("mp3 decoded to no samples")
                : SoundSampleResult.Decoded(
                    new SoundSample(mpeg.SampleRate, mpeg.Channels, buffer.AsMemory(0, filled)));
        }
        catch (Exception failure) when (
            failure is IOException or InvalidDataException or FormatException
                    or ArgumentException or IndexOutOfRangeException)
        {
            // NLayer is a port of a Java decoder and does not promise a single exception type for
            // malformed input. Caught narrowly rather than broadly, and reported rather than
            // swallowed, so a bad file costs its own sound.
            return SoundSampleResult.Refused($"mp3 decode failed: {failure.GetType().Name}");
        }
    }
}
