using System;
using Tf2DemoSalvage.Audio;

namespace Tf2DemoSalvage.Audio.Tests;

/// <summary>Tests <see cref="SilkVoiceDecoder"/>: lifecycle, errors, and a packet the SDK itself encoded.</summary>
public sealed class SilkVoiceDecoderTests
{
    /// <summary>Samples in one 20 ms SILK frame at Steam Voice's 16 kHz.</summary>
    private const int FrameSamples = 320;

    [Test]
    public void Construction_LoadsTheNativeLibraryAndSucceeds()
    {
        using SilkVoiceDecoder decoder = new();
        decoder.ShouldNotBeNull();
    }

    [Test]
    public void Dispose_IsIdempotent()
    {
        SilkVoiceDecoder decoder = new();
        decoder.Dispose();
        Should.NotThrow(decoder.Dispose);
    }

    [Test]
    public void Decode_AfterDispose_Throws()
    {
        SilkVoiceDecoder decoder = new();
        decoder.Dispose();

        Should.Throw<ObjectDisposedException>(() => decoder.Decode(new byte[40]));
    }

    [Test]
    public void Decode_AnEmptyPacket_ConcealsOnePacketOfLoss()
    {
        // Real Steam Voice carries zero-length SILK frames (gullywash and process both). The SDK's
        // own test/Decoder.c reads a zero-byte packet as LOST and decodes framesPerPacket frames
        // with lostFlag 1 — concealment, not an error.
        short[] tone = new short[FrameSamples * 4];
        for (int i = 0; i < tone.Length; i++)
        {
            tone[i] = (short)(8000 * Math.Sin(2 * Math.PI * 400 * i / SilkVoiceDecoder.SampleRate));
        }

        byte[][] packets = SilkTestEncoder.Encode(tone, FrameSamples * 2);

        using SilkVoiceDecoder decoder = new();
        decoder.Decode(packets[0]).Length.ShouldBe(FrameSamples * 2);

        // The last packet held two frames, so the loss covers two.
        decoder.Decode([]).Length.ShouldBe(FrameSamples * 2);
    }

    [Test]
    public void Decode_AnEmptyPacketBeforeAnyOther_ConcealsOneFrame()
    {
        using SilkVoiceDecoder decoder = new();

        decoder.Decode([]).Length.ShouldBe(FrameSamples);
    }

    [Test]
    public void Decode_FramesTheSdkEncodedFromATone_Returns320SamplesEachThatCarryTheTone()
    {
        // The ground truth a hand-built byte fixture cannot give (D38): a 400 Hz tone, encoded by
        // the same SDK at Steam Voice's 16 kHz, decodes back to 20 ms frames of real signal.
        short[] tone = new short[FrameSamples * 10];
        for (int i = 0; i < tone.Length; i++)
        {
            tone[i] = (short)(8000 * Math.Sin(2 * Math.PI * 400 * i / SilkVoiceDecoder.SampleRate));
        }

        byte[][] packets = SilkTestEncoder.Encode(tone, FrameSamples);
        packets.Length.ShouldBe(10);

        using SilkVoiceDecoder decoder = new();
        double energy = 0;

        foreach (byte[] packet in packets)
        {
            short[] pcm = decoder.Decode(packet);
            pcm.Length.ShouldBe(FrameSamples);

            foreach (short sample in pcm)
            {
                energy += (double)sample * sample;
            }
        }

        // RMS of an 8000-amplitude sine is 5657; a codec at speech bitrate keeps it within a factor.
        double rms = Math.Sqrt(energy / tone.Length);
        rms.ShouldBeInRange(2000, 8000);
    }
}
