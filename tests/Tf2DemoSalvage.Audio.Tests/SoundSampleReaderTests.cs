using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Tf2DemoSalvage.Audio.Tests;

/// <summary>
/// Decoding a sound file to samples, whichever container it arrived in.
/// </summary>
/// <remarks>
/// **Synthetic, and the WAV cases are authored byte by byte.** A RIFF file small enough to write out
/// in full is the smallest input that can distinguish a correct reader from a wrong one, which is
/// the point of `docs/memory/real-data-hides-bugs-small-inputs-expose.md`: a real 40 KB voice line
/// has enough samples that an off-by-one or a sign error hides in the noise, and a four-sample
/// fixture does not.
///
/// **MP3 cannot be authored this way and is not faked.** There is no encoder here, and inventing
/// bytes that merely look like an MP3 would test the sniffer while pretending to test the decoder.
/// So the MP3 cases below cover only what synthetic bytes can honestly settle — that the container
/// is recognised, that the extension is not trusted, and that malformed input is refused with a
/// reason rather than thrown. The decode itself is measured against the game's own files by
/// `SoundResolutionProbe`.
/// </remarks>
public sealed class SoundSampleReaderTests
{
    [Test]
    public void Read_SixteenBitPcm_IsNormalisedAgainst32768()
    {
        // **32768, not 32767.** Two's complement runs -32768..32767, so dividing by 32767 lets the
        // most negative sample reach -1.000031 and clip. One value in the whole range is affected,
        // which is exactly why it needs a test rather than an eyeball.
        SoundSampleResult result = SoundSampleReader.Read(
            Wave(16, 1, 22050, [0, short.MaxValue, short.MinValue, -16384]));

        result.Refusal.ShouldBeNull();
        SoundSample sample = result.Sample!.Value;

        sample.SampleRate.ShouldBe(22050);
        sample.Channels.ShouldBe(1);
        sample.Samples.Span[0].ShouldBe(0f);
        sample.Samples.Span[1].ShouldBe(32767f / 32768f, 0.000001);
        sample.Samples.Span[2].ShouldBe(-1f, "the most negative sample reaches exactly -1");
        sample.Samples.Span[3].ShouldBe(-0.5f, 0.000001);
    }

    [Test]
    public void Read_EightBitPcm_IsTreatedAsUnsignedAndCentredOn128()
    {
        // **8-bit WAV is unsigned; every wider depth is signed.** Read as signed it comes out
        // inverted and offset — a click and a hum, not an error. The values below are chosen so a
        // signed reading gives visibly different numbers: 0 would be 0.0 signed but is -1.0 here.
        SoundSampleResult result = SoundSampleReader.Read(
            SoundSampleReaderTests.WaveBytes(8, 1, 11025, [0, 128, 255, 64]));

        result.Refusal.ShouldBeNull();
        ReadOnlySpan<float> samples = result.Sample!.Value.Samples.Span;

        samples[0].ShouldBe(-1f, "byte 0 is full negative, not silence");
        samples[1].ShouldBe(0f, "byte 128 is the centre");
        samples[2].ShouldBe(255f / 128f - 1f, 0.000001);
        samples[3].ShouldBe(-0.5f, 0.000001);
    }

    [Test]
    public void Read_StereoPcm_KeepsFramesInterleavedAndCountsThemPerFrame()
    {
        // A stereo file has half as many frames as samples, and the mixer positions frames rather
        // than samples. Getting this wrong halves or doubles every duration.
        SoundSampleResult result = SoundSampleReader.Read(
            Wave(16, 2, 44100, [1000, -1000, 2000, -2000]));

        SoundSample sample = result.Sample!.Value;

        sample.Channels.ShouldBe(2);
        sample.Samples.Length.ShouldBe(4, "four samples");
        sample.FrameCount.ShouldBe(2, "but only two frames");
        sample.Samples.Span[0].ShouldBeGreaterThan(0f, "left of frame 0");
        sample.Samples.Span[1].ShouldBeLessThan(0f, "right of frame 0");
    }

    [Test]
    public void Duration_FromRateAndFrames_IsTheFramesOverTheRate()
    {
        // Computed rather than stored so it cannot disagree with the samples.
        SoundSample sample = SoundSampleReader.Read(
            Wave(16, 2, 1000, [.. Enumerable.Repeat((short)0, 2000)])).Sample!.Value;

        sample.FrameCount.ShouldBe(1000);
        sample.Duration.TotalSeconds.ShouldBe(1.0, 0.0001);
    }

    [Test]
    public void Duration_AZeroSampleRate_IsZeroRatherThanADivideByZero()
    {
        // A malformed header can carry a zero rate, and the division would otherwise be the crash.
        // Constructed directly because the reader rejects such a file — the guard is on the type,
        // so it has to be tested on the type.
        new SoundSample(0, 1, new float[10]).Duration.ShouldBe(TimeSpan.Zero);
        new SoundSample(44100, 0, new float[10]).Duration.ShouldBe(TimeSpan.Zero);
        new SoundSample(44100, 0, new float[10]).FrameCount.ShouldBe(0);
    }

    [Test]
    public void Read_AdpcmWave_IsRefusedByNameRatherThanAsCorruption()
    {
        // **Two of TF2's 2,817 WAVs are ADPCM**, and deferring it was agreed only "provided it is
        // reported rather than silently skipped". A bare null would make "not implemented"
        // indistinguishable from "corrupt file" and from "nothing was playing".
        SoundSampleResult result = SoundSampleReader.Read(
            WaveBytes(4, 1, 22050, [0, 0, 0, 0], format: 2));

        result.Sample.ShouldBeNull();
        result.Succeeded.ShouldBeFalse();
        result.Refusal.ShouldNotBeNull();
        result.Refusal.ShouldContain("Adpcm");
    }

    [Test]
    public void Read_AnUnsupportedBitDepth_IsRefusedByNumber()
    {
        SoundSampleResult result = SoundSampleReader.Read(
            WaveBytes(24, 1, 22050, [0, 0, 0, 0, 0, 0]));

        result.Refusal.ShouldNotBeNull();
        result.Refusal.ShouldContain("24");
    }

    [Test]
    public void Read_Mp3Bytes_AreRoutedByContentNotByAnyExtension()
    {
        // **The sniff is on the bytes, and this is not academic**: SoundFile serves 60 of the
        // corpus's `.wav` names from `.mp3` files, so the name says nothing about the container.
        // Both MP3 openings must be recognised — a bare frame sync, and the ID3 tag that most of
        // TF2's voice lines actually start with.
        //
        // These are not valid MP3s, so both must be REFUSED. What is being tested is that they were
        // refused by the mp3 path rather than as unrecognised bytes.
        SoundSampleResult sync = SoundSampleReader.Read(new byte[] { 0xFF, 0xFB, 0x90, 0x00 });
        byte[] id3 = [.. Encoding.ASCII.GetBytes("ID3"), 3, 0, 0, 0, 0, 0, 0];
        SoundSampleResult tagged = SoundSampleReader.Read(id3);

        sync.Refusal.ShouldNotBeNull();
        sync.Refusal.ShouldNotContain("no MP3 frame sync", Case.Insensitive);

        tagged.Refusal.ShouldNotBeNull();
        tagged.Refusal.ShouldNotContain("no MP3 frame sync", Case.Insensitive);
    }

    [Test]
    public void Read_BytesThatAreNeitherContainer_AreRefusedAsUnrecognised()
    {
        // **The control for the test above.** Without it, "routed to the mp3 path" and "refused by
        // everything" produce the same observation, since both end in a refusal.
        SoundSampleResult result = SoundSampleReader.Read(
            Encoding.ASCII.GetBytes("NOPE not a sound file at all"));

        result.Refusal.ShouldNotBeNull();
        result.Refusal.ShouldContain("no MP3 frame sync");
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(3)]
    public void Read_ShorterThanAnyHeader_IsRefusedRatherThanThrowing(int length)
    {
        // Hostile input the corpus cannot supply: every demo was written by the engine, so a
        // truncated sound file has to be authored. The requirement is that it costs its own sound
        // and nothing else.
        SoundSampleReader.Read(new byte[length]).Refusal.ShouldNotBeNull();
    }

    [Test]
    public void Read_ARiffHeaderWithNothingAfterIt_IsRefusedRatherThanThrowing()
    {
        SoundSampleReader.Read(Encoding.ASCII.GetBytes("RIFF")).Refusal.ShouldNotBeNull();
        SoundSampleReader.Read(Encoding.ASCII.GetBytes("RIFF\0\0\0\0WAVE")).Refusal.ShouldNotBeNull();
    }

    /// <summary>A minimal RIFF/WAVE file carrying the given 16-bit samples.</summary>
    private static byte[] Wave(int bits, int channels, int rate, short[] samples)
    {
        List<byte> data = [];

        foreach (short sample in samples)
        {
            byte[] pair = new byte[2];
            BinaryPrimitives.WriteInt16LittleEndian(pair, sample);
            data.AddRange(pair);
        }

        return WaveBytes(bits, channels, rate, [.. data]);
    }

    /// <summary>
    /// A minimal RIFF/WAVE file, written by hand from the format rather than from our reader.
    /// </summary>
    /// <remarks>
    /// **Authored from the RIFF specification and `tier2/riff.h`, not from `RiffWave`.** A fixture
    /// generated by the code under test proves the two agree and nothing else —
    /// `docs/memory/put-the-real-file-in-the-fixture.md`.
    /// </remarks>
    private static byte[] WaveBytes(int bits, int channels, int rate, byte[] data, int format = 1)
    {
        int blockAlign = channels * ((bits + 7) / 8);
        List<byte> file = [];

        void Ascii(string text) => file.AddRange(Encoding.ASCII.GetBytes(text));
        void U32(int value)
        {
            byte[] four = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(four, (uint)value);
            file.AddRange(four);
        }
        void U16(int value)
        {
            byte[] two = new byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(two, (ushort)value);
            file.AddRange(two);
        }

        Ascii("RIFF");
        U32(36 + data.Length);
        Ascii("WAVE");

        Ascii("fmt ");
        U32(16);
        U16(format);
        U16(channels);
        U32(rate);
        U32(rate * blockAlign);
        U16(blockAlign);
        U16(bits);

        Ascii("data");
        U32(data.Length);
        file.AddRange(data);

        return [.. file];
    }
}
