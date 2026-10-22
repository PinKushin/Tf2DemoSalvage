using System;
using System.IO;

using NLayer;

namespace Tf2DemoSalvage.Audio.Tests;

/// <summary>The authored MP3 is what it says it is, to NLayer itself, before any test leans on it.</summary>
public sealed class SyntheticMp3Tests
{
    // Two frames at least: NLayer 2.0.1 refuses a single frame ("Not a valid MPEG file!" from MpegStreamReader's
    // constructor), measured while authoring this builder; it locks on only when a second header follows.
    [TestCase(2, false)]
    [TestCase(5, false)]
    [TestCase(3, true)]
    public void NLayer_ASyntheticFile_DecodesTheFramesItWasGiven(int frames, bool stereo)
    {
        using MemoryStream stream = new(SyntheticMp3.Silent(frames, stereo), writable: false);
        using MpegFile mpeg = new(stream);

        mpeg.SampleRate.ShouldBe(SyntheticMp3.SampleRate);
        mpeg.Channels.ShouldBe(stereo ? 2 : 1);

        float[] buffer = new float[SyntheticMp3.SamplesPerFrame * 2 * (frames + 1)];
        int total = 0;
        int read;

        while ((read = mpeg.ReadSamples(buffer, total, buffer.Length - total)) > 0)
        {
            total += read;
        }

        total.ShouldBe(frames * SyntheticMp3.SamplesPerFrame * (stereo ? 2 : 1));
        buffer.AsSpan(0, total).ToArray().ShouldAllBe(sample => sample == 0f);
    }

    /// <summary>The production path: content-routed to the MP3 decoder, the values the builder put there.</summary>
    [Test]
    public void Read_ASyntheticMp3_DecodesItsRateChannelsAndFrames()
    {
        SoundSampleResult result = SoundSampleReader.Read(SyntheticMp3.Silent(4, stereo: true));

        SoundSample sample = result.Sample.ShouldNotBeNull();
        sample.SampleRate.ShouldBe(SyntheticMp3.SampleRate);
        sample.Channels.ShouldBe(2);
        sample.FrameCount.ShouldBe(4 * SyntheticMp3.SamplesPerFrame);
    }
}
