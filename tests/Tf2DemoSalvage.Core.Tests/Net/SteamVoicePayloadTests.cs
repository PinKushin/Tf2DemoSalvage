using System;
using System.IO;
using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// Tests the framing inside a <c>steam</c>-codec <c>svc_VoiceData</c> body.
/// </summary>
/// <remarks>
/// Every case here is built to a byte layout established by exact consumption over the corpus, not
/// from a specification — Valve publishes none for this. The layout is recorded in
/// `docs/findings/02-net-messages.md`.
/// </remarks>
public sealed class SteamVoicePayloadTests
{
    private const ulong SteamId = 76561198000000001UL;
    private const int SampleRate = 24000;

    [Test]
    public void SteamVoice_TheSpeaker_IsTheSteamAccountNotTheClientSlot()
    {
        // The reason this decoding is worth having at all. svc_VoiceData already gives a client
        // slot, but a slot is only meaningful against the roster at that instant and is reused
        // when players leave. The account is not.
        byte[] payload = Build(Opus(seq: 0, data: [0x68, 0x11, 0x22]));

        VoicePacket packet = SteamVoicePayload.Decode(payload);

        packet.SteamId.ShouldBe(SteamId);
        packet.SampleRate.ShouldBe(SampleRate);
        packet.Chunks.Count.ShouldBe(1);
        packet.Chunks[0].Sequence.ShouldBe(0);
        packet.Chunks[0].Data.ToArray().ShouldBe(new byte[] { 0x68, 0x11, 0x22 });
    }

    [Test]
    public void SteamVoice_AnFfffSize_TerminatesTheBlock()
    {
        // The finding that closed B31. Sixty-three of 1397 corpus blocks ended two bytes short of
        // their declared length, and those two bytes were FFFF every time - a terminator read
        // through the size field. Treating it as a chunk length asks for 65535 bytes that are not
        // there; treating it as a parse failure discards a block that is perfectly well formed.
        byte[] payload = Build(
            [.. Opus(seq: 4, data: [0x68, 0xAA]), .. (byte[])[0xFF, 0xFF]]);

        VoicePacket packet = SteamVoicePayload.Decode(payload);

        packet.Chunks.Count.ShouldBe(1);
        packet.Chunks[0].Sequence.ShouldBe(4);
        packet.IsTerminated.ShouldBeTrue();
    }

    [Test]
    public void SteamVoice_ABlockWithoutATerminator_IsStillComplete()
    {
        // The control for the case above. Most blocks - 1334 of 1397 - simply run to the end of
        // their declared length with no sentinel, so a decoder that required one would reject the
        // common case.
        byte[] payload = Build(Opus(seq: 9, data: [0x68, 0x01]));

        VoicePacket packet = SteamVoicePayload.Decode(payload);

        packet.Chunks.Count.ShouldBe(1);
        packet.IsTerminated.ShouldBeFalse();
    }

    [Test]
    public void SteamVoice_SeveralChunksInOneBlock_KeepTheirSequence()
    {
        // A voice packet carries a burst of frames, and the sequence numbers are what order them
        // when packets are dropped or arrive out of order. Losing them would leave audio that
        // decodes and plays back scrambled.
        byte[] payload = Build(
            [.. Opus(26, [0x68, 0x01]), .. Opus(27, [0x68, 0x02, 0x03]), .. Opus(28, [0x68])]);

        VoicePacket packet = SteamVoicePayload.Decode(payload);

        packet.Chunks.Count.ShouldBe(3);
        packet.Chunks[0].Sequence.ShouldBe(26);
        packet.Chunks[1].Sequence.ShouldBe(27);
        packet.Chunks[2].Sequence.ShouldBe(28);
        packet.Chunks[1].Data.Length.ShouldBe(3);
        packet.Chunks[2].Data.Length.ShouldBe(1);
    }

    [Test]
    public void SteamVoice_ASilencePacket_CarriesNoAudio()
    {
        // The 18-byte packets: a sample rate, a type 0x00 with a 16-bit payload, and the tail.
        // 55 of the corpus's 1452 look like this. They must decode rather than be rejected,
        // because a talk burst begins and ends with them.
        byte[] payload = BuildRaw([0x00, 0x40, 0x01]);

        VoicePacket packet = SteamVoicePayload.Decode(payload);

        packet.Chunks.ShouldBeEmpty();
        packet.SampleRate.ShouldBe(SampleRate);
        packet.SteamId.ShouldBe(SteamId);
    }

    [Test]
    public void SteamVoice_APayloadNotConsumedExactly_IsRejected()
    {
        // A voice body has no checksum over its framing, so a wrong field width shows up only as
        // leftover bytes. Reporting that is the difference between refusing a packet and handing
        // an Opus decoder bytes that are not a frame boundary - which produces noise, not an
        // error.
        byte[] good = Build(Opus(0, [0x68, 0x11]));

        Should.NotThrow(() => SteamVoicePayload.Decode(good));
        Should.Throw<InvalidDataException>(() => SteamVoicePayload.Decode([.. good, 0x00]));

        // Too short to hold even the steamID and the tail.
        Should.Throw<InvalidDataException>(() => SteamVoicePayload.Decode(new byte[8]));

        // A declared chunk length running past the end of its block.
        byte[] overrun = BuildRaw([0x06, 0x06, 0x00, 0xFF, 0x00, 0x01, 0x00, 0x68, 0x11]);
        Should.Throw<InvalidDataException>(() => SteamVoicePayload.Decode(overrun));
    }

    [Test]
    public void SteamVoice_AnAudioBlockLongerThanThePayload_NamesBothLengths()
    {
        // **The declared length is the only thing that says where a block ends**, so one longer
        // than what remains is unrecoverable rather than merely odd. Slicing it anyway reads the
        // four-byte tail as audio, and a CRC decoded as Opus is noise rather than an error.
        //
        // 0xFF00 bytes declared with three left, so no arithmetic slip makes it fit.
        InvalidDataException failure = Should.Throw<InvalidDataException>(
            () => SteamVoicePayload.Decode(BuildRaw([0x06, 0x00, 0xFF])));

        failure.Message.ShouldContain("65280");
    }

    [Test]
    public void SteamVoice_ATerminatorWithBytesBehindIt_IsNotATerminator()
    {
        // **0xFFFF means "the block ends here", so it can only be the last thing in the block.**
        // Anything behind it means the 0xFFFF was a chunk length that happens to be the sentinel
        // value — or that the block was framed wrongly — and treating it as an end would silently
        // discard the rest.
        Should.Throw<InvalidDataException>(
            () => SteamVoicePayload.Decode(Build([0xFF, 0xFF, 0x00, 0x00])))
            .Message.ShouldContain("not a terminator");
    }

    [Test]
    public void SteamVoice_ABlockWithATailTooShortForAChunk_IsRejected()
    {
        // One byte behind the last chunk: too short to be a chunk header and not the two-byte
        // sentinel. The loop simply stops on it, so without this check the byte would vanish and
        // the block would look complete.
        Should.Throw<InvalidDataException>(
            () => SteamVoicePayload.Decode(Build([.. Opus(0, [0x68, 0x11]), 0x00])))
            .Message.ShouldContain("after its last chunk");
    }

    [Test]
    public void SteamVoice_ASubPacketWhoseValueRunsPastTheEnd_NamesWhatWasExpected()
    {
        // A sub-packet type commits the payload to the fields that follow it. One byte where two
        // are declared means the type byte was misread or the payload is truncated, and the
        // message says which field was being read so the two can be told apart.
        Should.Throw<InvalidDataException>(
            () => SteamVoicePayload.Decode(BuildRaw([0x00, 0x01])))
            .Message.ShouldContain("a silence value");
    }

    [Test]
    public void Decode_ASilkRecord_YieldsEachSelfLengthPrefixedFrame()
    {
        // B441: Steam Voice before Opus. A type 0x04 record is u16 length, then frames that are
        // each u16 length plus bytes — no sequence number — and FFFF ending speech. Measured on
        // gullywash and process, every packet: `0B 803E 04 3701 2800 <40 bytes> …`.
        byte[] payload = BuildRaw(
            Silk([.. SilkFrame([0xA5, 0x57, 0x2E]), .. SilkFrame([0x9D, 0x59]), 0xFF, 0xFF]));

        VoicePacket packet = SteamVoicePayload.Decode(payload);

        packet.Codec.ShouldBe(SteamVoiceCodec.Silk);
        packet.Chunks.Count.ShouldBe(2);
        packet.Chunks[0].Data.ToArray().ShouldBe(new byte[] { 0xA5, 0x57, 0x2E });
        packet.Chunks[1].Data.ToArray().ShouldBe(new byte[] { 0x9D, 0x59 });
        packet.Chunks[1].Sequence.ShouldBe(1);
        packet.IsTerminated.ShouldBeTrue();
    }

    [Test]
    public void Decode_ASilkRecordHoldingOnlyTheTerminator_IsAnEmptyEndOfSpeech()
    {
        // The census's 33 twenty-byte packets: 8 + 3 (rate) + 1 + 2 + 2 (FFFF) + 4.
        byte[] payload = BuildRaw(Silk([0xFF, 0xFF]));

        payload.Length.ShouldBe(20);

        VoicePacket packet = SteamVoicePayload.Decode(payload);

        packet.Codec.ShouldBe(SteamVoiceCodec.Silk);
        packet.Chunks.ShouldBeEmpty();
        packet.IsTerminated.ShouldBeTrue();
    }

    [Test]
    public void Decode_AnOpusRecord_IsOpusAndASilencePacketIsNeither()
    {
        SteamVoicePayload.Decode(Build(Opus(0, [0x68]))).Codec.ShouldBe(SteamVoiceCodec.Opus);
        SteamVoicePayload.Decode(BuildRaw([0x00, 0x40, 0x01])).Codec.ShouldBe(SteamVoiceCodec.None);
    }

    [Test]
    public void Decode_ASilkFrameRunningPastItsRecord_IsRejected()
    {
        Should.Throw<InvalidDataException>(
            () => SteamVoicePayload.Decode(BuildRaw(Silk([0x05, 0x00, 0xAA]))))
            .Message.ShouldContain("runs past");
    }

    [Test]
    public void Decode_OpusAndSilkInOnePacket_IsRejected()
    {
        // One stream, one codec: a packet carrying both would need two decoders for one speaker.
        Should.Throw<InvalidDataException>(
            () => SteamVoicePayload.Decode(BuildRaw([
                .. Silk([.. SilkFrame([0x01])]),
                0x06, 0x05, 0x00, .. Opus(0, [0x68]),
            ])))
            .Message.ShouldContain("both");
    }

    [Test]
    public void TryDecode_ATailThatIsTheCrc32OfTheBody_IsSteamVoice()
    {
        byte[] payload = WithCrc(BuildRaw(Silk([.. SilkFrame([0xA5, 0x57]), 0xFF, 0xFF])));

        SteamVoicePayload.TryDecode(payload, out VoicePacket? packet).ShouldBeTrue();
        packet.Codec.ShouldBe(SteamVoiceCodec.Silk);
    }

    [Test]
    public void TryDecode_ATailThatIsNotTheCrc32_IsNotSteamVoice()
    {
        // The engine picks the path by sv_use_steam_voice, which no demo records; svc_voiceinit
        // says vaudio_speex either way. The payload's own CRC is what the demo does show.
        byte[] payload = WithCrc(BuildRaw(Silk([.. SilkFrame([0xA5, 0x57]), 0xFF, 0xFF])));
        payload[^1] ^= 0x01;

        SteamVoicePayload.TryDecode(payload, out VoicePacket? packet).ShouldBeFalse();
        packet.ShouldBeNull();
    }

    [Test]
    public void TryDecode_WholeSpeexFrames_AreNotSteamVoice()
    {
        // Two 28-byte frames, the 2007 shape, which do not frame as Steam Voice at all.
        byte[] speex = new byte[56];
        for (int i = 0; i < speex.Length; i++)
        {
            speex[i] = (byte)(i * 37 + 11);
        }

        SteamVoicePayload.TryDecode(speex, out _).ShouldBeFalse();
        SteamVoicePayload.TryDecode(new byte[3], out _).ShouldBeFalse();
    }

    /// <summary>A SILK frame: u16 length, then the bytes.</summary>
    private static byte[] SilkFrame(byte[] data) =>
        [(byte)(data.Length & 0xFF), (byte)(data.Length >> 8), .. data];

    /// <summary>A type 0x04 record around SILK frame bytes.</summary>
    private static byte[] Silk(byte[] frames) =>
        [0x04, (byte)(frames.Length & 0xFF), (byte)(frames.Length >> 8), .. frames];

    /// <summary>Replaces the placeholder tail with the CRC32 of everything before it.</summary>
    private static byte[] WithCrc(byte[] payload)
    {
        BitConverter.GetBytes(System.IO.Hashing.Crc32.HashToUInt32(payload.AsSpan(0, payload.Length - 4)))
            .CopyTo(payload, payload.Length - 4);
        return payload;
    }

    /// <summary>A chunk: length, sequence, then the Opus bytes.</summary>
    private static byte[] Opus(int seq, byte[] data) =>
    [
        (byte)(data.Length & 0xFF), (byte)(data.Length >> 8),
        (byte)(seq & 0xFF), (byte)(seq >> 8),
        .. data,
    ];

    /// <summary>Wraps chunk bytes in a type 0x06 sub-packet, then in a payload.</summary>
    private static byte[] Build(byte[] chunks) =>
        BuildRaw([0x06, (byte)(chunks.Length & 0xFF), (byte)(chunks.Length >> 8), .. chunks]);

    /// <summary>steamID, the sample-rate sub-packet, the given bytes, then the four-byte tail.</summary>
    private static byte[] BuildRaw(byte[] subPackets) =>
    [
        .. BitConverter.GetBytes(SteamId),
        0x0B, (byte)(SampleRate & 0xFF), (byte)(SampleRate >> 8),
        .. subPackets,
        0xDE, 0xAD, 0xBE, 0xEF,
    ];
}
