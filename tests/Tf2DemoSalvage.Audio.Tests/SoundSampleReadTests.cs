using Tf2DemoSalvage.Audio;

namespace Tf2DemoSalvage.Audio.Tests;

/// <summary>Opening a sound file's bytes, on hand-built RIFF/WAVE files (B217).</summary>
/// <remarks>
/// `SoundSample.Read` had no test that ran without the TF2 install, so the mutation box scored it
/// NoCoverage. Fixtures come from <see cref="RiffConformanceTests.Wave"/>, authored from riff.h.
/// </remarks>
public sealed class SoundSampleReadTests
{
    private const int Pcm = 1;

    [Test]
    public void Read_FewerThanFourBytes_IsRefused()
    {
        SoundSampleReader.Read(new byte[] { (byte)'R', (byte)'I', (byte)'F' }).Refusal.ShouldBe("shorter than any header");
    }

    [Test]
    public void Read_NeitherRiffNorMp3_IsRefused()
    {
        SoundSampleReader.Read("RIFX"u8.ToArray()).Refusal.ShouldBe("not RIFF and no MP3 frame sync");
        SoundSampleReader.Read(new byte[] { 0xFF, 0xC0, 0, 0 }).Refusal.ShouldBe("not RIFF and no MP3 frame sync");
    }

    [Test]
    public void Read_SixteenBitPcm_DividesBy32768()
    {
        // -32768, 16384, 32767 little-endian.
        SoundSample sample = SoundSampleReader.Read(
            RiffConformanceTests.Wave(Pcm, 2, 22050, 16, [0x00, 0x80, 0x00, 0x40, 0xFF, 0x7F, 0, 0])).Sample.ShouldNotBeNull();

        sample.SampleRate.ShouldBe(22050);
        sample.Channels.ShouldBe(2);
        sample.Samples.ToArray().ShouldBe([-1f, 0.5f, 32767f / 32768f, 0f]);
    }

    [Test]
    public void Read_EightBitPcm_IsUnsignedAroundOneTwentyEight()
    {
        SoundSampleReader.Read(RiffConformanceTests.Wave(Pcm, 1, 11025, 8, [0, 128, 192, 255]))
            .Sample.ShouldNotBeNull().Samples.ToArray().ShouldBe([-1f, 0f, 0.5f, 127f / 128f]);
    }

    [Test]
    public void Read_AdpcmOrBadShape_IsRefusedByName()
    {
        SoundSampleReader.Read(RiffConformanceTests.Wave(2, 1, 22050, 4, [0, 0])).Refusal.ShouldBe("unsupported wave format Adpcm");
        SoundSampleReader.Read(RiffConformanceTests.Wave(Pcm, 3, 22050, 16, [0, 0, 0, 0, 0, 0])).Refusal
            .ShouldBe("unusable format: 3 channels at 22050 Hz");
        // Zero channels or a zero rate never reach this check: RiffWave refuses both first.
        SoundSampleReader.Read(RiffConformanceTests.Wave(Pcm, 1, 0, 16, [0, 0])).Refusal
            .ShouldBe("RIFF header did not parse");
        SoundSampleReader.Read(RiffConformanceTests.Wave(Pcm, 1, 22050, 24, [0, 0, 0])).Refusal
            .ShouldBe("unsupported bit depth 24");
    }

    [Test]
    public void Read_ARiffThatDoesNotParse_IsRefused()
    {
        SoundSampleReader.Read("RIFF\0\0\0\0JUNK"u8.ToArray()).Refusal.ShouldBe("RIFF header did not parse");
    }
}
