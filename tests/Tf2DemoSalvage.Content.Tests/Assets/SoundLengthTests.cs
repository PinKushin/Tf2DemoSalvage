using System;
using System.Buffers.Binary;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// A sound file's own length, from hand-built headers whose answers the test put there (D38, B513). The shipped control
/// — 2,815 WAVs against the cache's counts, 300 voice MP3s against a full decode, all within half a millisecond — is
/// the <c>flex lengths</c> probe.
/// </summary>
public sealed class SoundLengthTests
{
    [Test]
    public void Seconds_APcmWave_IsTheDataChunkOverTheBlockSize()
    {
        // 16-bit stereo at 22050: four bytes a frame, 22050 frames of data is one second.
        Seconds(Wave(format: 1, rate: 22050, blockAlign: 4, dataBytes: 22050 * 4)).ShouldBe(1f);
    }

    [Test]
    public void Seconds_AnAdpcmWave_CountsSamplesPerBlock()
    {
        // 512-byte blocks of 1012 samples (wSamplesPerBlock): ten blocks at 11025 Hz.
        Seconds(Wave(format: 2, rate: 11025, blockAlign: 512, dataBytes: 5120, samplesPerBlock: 1012)).ShouldBe(10120f / 11025f, 1e-6f);
    }

    [Test]
    public void Seconds_AnMp3AfterAnId3Tag_IsItsFramesTimesSamplesPerFrame()
    {
        // MPEG-1 Layer III, 128 kbps, 44100 Hz: 417-byte frames of 1152 samples; three of them after a 20-byte tag.
        List<byte> file = [(byte)'I', (byte)'D', (byte)'3', 3, 0, 0, 0, 0, 0, 20, .. new byte[20]];

        for (int frame = 0; frame < 3; frame++)
        {
            byte[] bytes = new byte[417];
            bytes[0] = 0xFF;
            bytes[1] = 0xFB;
            bytes[2] = 0x90;
            file.AddRange(bytes);
        }

        Seconds(file.ToArray()).ShouldBe(3 * 1152f / 44100f, 1e-6f);
    }

    [Test]
    public void Seconds_NeitherWaveNorMp3_IsNull() => SoundLength.Seconds(new byte[64]).ShouldBeNull();

    private static float Seconds(byte[] file) => SoundLength.Seconds(file).ShouldNotBeNull();

    private static byte[] Wave(int format, int rate, int blockAlign, int dataBytes, int samplesPerBlock = 0)
    {
        int fmt = format == 2 ? 20 : 16;
        byte[] file = new byte[12 + 8 + fmt + 8 + dataBytes];
        Span<byte> s = file;

        "RIFF"u8.CopyTo(s);
        BinaryPrimitives.WriteInt32LittleEndian(s[4..], file.Length - 8);
        "WAVE"u8.CopyTo(s[8..]);
        "fmt "u8.CopyTo(s[12..]);
        BinaryPrimitives.WriteInt32LittleEndian(s[16..], fmt);
        BinaryPrimitives.WriteUInt16LittleEndian(s[20..], (ushort)format);
        BinaryPrimitives.WriteInt32LittleEndian(s[24..], rate);
        BinaryPrimitives.WriteUInt16LittleEndian(s[32..], (ushort)blockAlign);

        if (format == 2)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(s[38..], (ushort)samplesPerBlock);
        }

        int data = 20 + fmt;
        "data"u8.CopyTo(s[data..]);
        BinaryPrimitives.WriteInt32LittleEndian(s[(data + 4)..], dataBytes);
        return file;
    }
}
