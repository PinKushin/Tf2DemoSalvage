using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Audio.Tests;

/// <summary>The engine's client voice path, as TF2's x64 engine.dll runs it, between a packet and the mixer.</summary>
/// <remarks>
/// **Read from the binary, because the SDK has none of it** — <c>voice.cpp</c> is engine code. Every function named here is
/// in the 2026 x64 <c>engine.dll</c>:
///
/// | function | what it is |
/// |---|---|
/// | <c>FUN_1801fa0d0</c> | <c>CClientState::ProcessVoiceData</c> — entity <c>client + 1</c>, then get-or-assign a channel |
/// | <c>FUN_1801fa2b0</c> | <c>ProcessVoiceInit</c> → <c>Voice_Init(codec, rate)</c> (<c>FUN_180058700</c>), which calls <c>Voice_Deinit</c> first |
/// | <c>FUN_180057350</c> | <c>Voice_AssignChannel</c> — a free one of five; resets the buffer, the codec, the gain and the pad |
/// | <c>FUN_1800591d0</c> | <c>CAutoGain::Reset(128, voice_maxgain, voice_avggain, voice_scale)</c> |
/// | <c>FUN_180056eb0</c> | <c>Voice_AddIncomingData</c> — decompress, <c>CAutoGain::ProcessSamples</c>, upsample into the buffer |
/// | <c>FUN_180058fe0</c> | <c>CAutoGain::ProcessSamples</c> |
/// | <c>FUN_180016fd0</c> | the channel buffer's write: on overflow the read point jumps, so the oldest unread samples are lost |
/// | <c>FUN_180002bb0</c> | the five buffers, <c>0xac44</c> bytes each |
/// | <c>FUN_180058090</c> | <c>Voice_Idle</c> — counts each pad down by frame time, starts the channel at zero, ends it when starved |
/// | <c>FUN_180057dc0</c> | <c>Voice_GetOutputData</c> — the mixer's read, with the linear fade over the last <c>voice_fadeouttime</c> |
/// | <c>FUN_18005a670</c> | <c>VoiceSE_StartChannel</c> — the sound: channel <c>8 + index</c>, volume 1, pitch 100, its soundlevel |
/// </remarks>
public sealed class VoiceConformanceTests
{
    private const float Interval = 0.015f;

    // ── CAutoGain ────────────────────────────────────────────────────────────────────────────────

    /// <summary><c>Reset</c> starts the fixed gain at <c>0x80</c>, 1.0, with no ramp, so the first block passes unchanged.</summary>
    [Test]
    public void Process_TheFirstBlock_PassesUnchanged()
    {
        short[] samples = Constant(128, 1000);

        new VoiceAutoGain(scale: 0f).Process(samples);

        samples.ShouldAllBe(sample => sample == 1000);
    }

    /// <summary>
    /// The next gain is <c>min(32767 / (avg + (max - avg) × voice_avggain), voice_maxgain)</c>; a steady 1000 asks 32.767,
    /// capped at 10, and block two ramps to it in steps of <c>(int)(9 / 127 × 128) = 9</c>:
    /// <c>(127 × 9 + 128) × 1000 >> 7 = 9929</c>.
    /// </summary>
    [Test]
    public void Process_TheSecondBlock_RampsTowardTheCappedGain()
    {
        short[] samples = Constant(256, 1000);

        new VoiceAutoGain(scale: 1f).Process(samples);

        (samples[128], samples[255]).ShouldBe(((short)1000, (short)9929));
    }

    /// <summary><c>voice_scale</c> lands twice — <c>cur = next × scale</c> with <c>next</c> already scaled: at 0.5, block three starts at 2.5.</summary>
    [Test]
    public void Process_AtScaleHalf_TheThirdBlockStartsAtTwoAndAHalf()
    {
        short[] samples = Constant(384, 1000);

        new VoiceAutoGain(scale: 0.5f).Process(samples);

        samples[256].ShouldBe((short)2500);
    }

    [Test]
    public void Process_AGainPastFullScale_ClampsToShortRange()
    {
        short[] samples = Constant(384, 20000);

        new VoiceAutoGain(scale: 10f).Process(samples);

        samples[383].ShouldBe(short.MaxValue);
    }

    /// <summary>
    /// **A gain past the int range is <c>CVTTSS2SI</c>'s <c>0x80000000</c>, then a wrapping <c>IMUL</c>.** At scale 1e9 the
    /// fixed gain is <c>int.MinValue</c>, and <c>int.MinValue × 1001</c> wraps to <c>int.MinValue</c> (1001 is odd), which
    /// shifts to −16777216 and clamps to −32768. A saturating cast would give <c>int.MaxValue</c> and the opposite sign.
    /// </summary>
    [Test]
    public void Process_AScaleBeyondTheIntRange_WrapsAsTheEngineDoes()
    {
        short[] samples = Constant(256, 1001);

        new VoiceAutoGain(scale: 1e9f).Process(samples);

        samples[128].ShouldBe(short.MinValue);
    }

    // ── Voice_AssignChannel / Voice_Idle: the plan ────────────────────────────────────────────────

    /// <summary>The pad, <c>voice_buffer_ms</c> 100, drained by <c>Voice_Idle</c>: <c>ceil(0.1 / 0.015) = 7</c> ticks late.</summary>
    [Test]
    public void Build_OnePacket_StartsSevenTicksLateOnTheFirstChannel()
    {
        VoiceSpurt spurt = VoicePlan.Build([Packet(10, entity: 3, samples: 160)], Interval).ShouldHaveSingleItem();

        (spurt.Entity, spurt.Channel, spurt.StartTick, spurt.Length).ShouldBe((3, 0, 17, 160));
    }

    [Test]
    public void EngineChannel_ForTheThirdSlot_IsTen() =>
        VoicePlan.EngineChannel(2).ShouldBe(10, "VoiceSE_StartChannel: entchannel = index + 8, CHAN_VOICE_BASE");

    [Test]
    public void Build_APacketBeforeTheBufferEmpties_JoinsTheSameSpurt()
    {
        VoiceSpurt spurt = VoicePlan.Build([Packet(10, 3, 160), Packet(11, 3, 160)], Interval).ShouldHaveSingleItem();

        spurt.Packets.Select(packet => (packet.Tick, packet.Offset)).ShouldBe([(10, 0), (11, 160)]);
    }

    /// <summary>A channel that runs dry is ended (<c>Voice_EndChannel</c>, FUN_1800578c0); 20 ms from 0.25 s is dry by 0.27 s.</summary>
    [Test]
    public void Build_APacketAfterTheBufferRanDry_StartsANewSpurt()
    {
        IReadOnlyList<VoiceSpurt> spurts = VoicePlan.Build([Packet(10, 3, 160), Packet(30, 3, 160)], Interval);

        spurts.Select(spurt => (spurt.Channel, spurt.StartTick)).ShouldBe([(0, 17), (0, 37)]);
    }

    /// <summary><c>Voice_AddIncomingData</c> refuses a channel above 4: five at once, no sixth.</summary>
    [Test]
    public void Build_SixSpeakersAtOnce_PlaysTheFirstFive()
    {
        IReadOnlyList<VoiceSpurt> spurts = VoicePlan.Build([.. Enumerable.Range(1, 6).Select(entity => Packet(10, entity, 160))], Interval);

        spurts.Select(spurt => (spurt.Entity, spurt.Channel)).ShouldBe([(1, 0), (2, 1), (3, 2), (4, 3), (5, 4)]);
    }

    [Test]
    public void Build_ASixthSpeakerAfterAChannelRanDry_TakesTheFreedChannel()
    {
        IReadOnlyList<VoiceSpurt> spurts = VoicePlan.Build(
            [.. Enumerable.Range(1, 5).Select(entity => Packet(10, entity, 160)), Packet(30, 6, 160)], Interval);

        spurts.Single(spurt => spurt.Entity == 6).Channel.ShouldBe(0);
    }

    /// <summary><c>Voice_Init</c> calls <c>Voice_Deinit</c> (FUN_180057510) before anything else, ending every channel.</summary>
    [Test]
    public void Build_AVoiceInitBetweenTwoPackets_EndsTheChannel()
    {
        IReadOnlyList<VoiceSpurt> spurts = VoicePlan.Build([Packet(10, 3, 160), Packet(11, 3, 160) with { Init = 1 }], Interval);

        spurts.Select(spurt => spurt.StartTick).ShouldBe([17, 18]);
    }

    /// <summary>
    /// The buffer is <c>0xac44</c> bytes, 22050 samples at the output rate. At an 8 kHz output, a 30000-sample packet
    /// overflows it by 7950, and the write wins: the OLDEST 7950 are lost.
    /// </summary>
    [Test]
    public void Build_APacketPastTheBuffer_LosesItsOldestSamples()
    {
        VoiceSpurt spurt = VoicePlan.Build([Packet(10, 3, 30000)], Interval)[0];

        spurt.Cuts.ShouldBe([new VoiceCut(0, 7950)]);
        VoicePlan.Render(spurt, 0, 0, 1f, Steady).FrameCount.ShouldBe(22050);
    }

    [Test]
    public void OutputRate_SteamWithNoRate_Is44100() =>
        VoicePlan.OutputRate(new SceneVoiceInit(0, "steam", 5, null), 24000).ShouldBe(44100, "Voice_Init: 0xac44 when steam and no rate");

    // ── Framing, with no native library ───────────────────────────────────────────────────────────

    [TestCase(0xF8, 0, 480, TestName = "OpusPacketSamples_ACelt20msFrame_Is480")]
    [TestCase(0x08, 0, 480, TestName = "OpusPacketSamples_ASilk20msFrame_Is480")]
    [TestCase(0x80, 0, 60, TestName = "OpusPacketSamples_ACelt2point5msFrame_Is60")]
    [TestCase(0xFB, 3, 1440, TestName = "OpusPacketSamples_ThreeCelt20msFramesByCode3_Are1440")]
    public void OpusPacketSamples_ByToc_IsTheRfcCount(int toc, int count, int expected) =>
        VoiceDecoding.OpusPacketSamples([(byte)toc, (byte)count]).ShouldBe(expected);

    [Test]
    public void Frame_TwoCeltFrames_Are1024SamplesAt22050() =>
        VoiceDecoding.Frame("vaudio_celt", new byte[128]).ShouldBe(new VoiceFraming(VoiceKind.Celt, 22050, 1024));

    [Test]
    public void Frame_TwoRawSpeexFrames_Are320SamplesAt8000() =>
        VoiceDecoding.Frame("vaudio_speex", new byte[56]).ShouldBe(new VoiceFraming(VoiceKind.Speex, 8000, 320));

    /// <summary>Steam Voice decodes by its own audio record: SILK under a <c>steam</c> init is still SILK (B441).</summary>
    [TestCase("steam")]
    [TestCase("vaudio_speex")]
    public void Frame_SteamVoiceWithASilkRecord_IsSilkWhateverTheInitSays(string codec) =>
        VoiceDecoding.Frame(codec, SteamSilk(3)).ShouldBe(new VoiceFraming(VoiceKind.Silk, 16000, 960));

    [Test]
    public void Frame_AnUnknownCodec_IsNone() =>
        VoiceDecoding.Frame("vaudio_miles", new byte[56]).Kind.ShouldBe(VoiceKind.None);

    /// <summary>Each packet is framed by the codec in force when it ARRIVED, and the second init also ends the channel.</summary>
    [Test]
    public void From_ASecondVoiceInit_FramesLaterPacketsByTheNewCodec()
    {
        VoicePlanResult plan = VoicePlan.From(
            [new SceneVoiceInit(0, "vaudio_speex", 5, null), new SceneVoiceInit(11, "vaudio_celt", 255, 22050)],
            [new SceneVoice(10, 2, false, new byte[28], Init: 0), new SceneVoice(11, 2, false, new byte[64], Init: 1)],
            Interval,
            _ => null);

        plan.Spurts.Select(spurt => spurt.Kind).ShouldBe([VoiceKind.Speex, VoiceKind.Celt]);
    }

    [Test]
    public void From_AMissingLibrary_RefusesWithItsReason()
    {
        VoicePlanResult plan = VoicePlan.From(
            [new SceneVoiceInit(0, "vaudio_celt", 255, 22050)],
            [new SceneVoice(10, 2, false, new byte[64], Init: 0)],
            Interval,
            kind => kind + " is missing");

        plan.Spurts.ShouldBeEmpty();
        plan.Refusal.ShouldBe("Celt is missing");
    }

    // ── Voice_GetOutputData: the fade ───────────────────────────────────────────────────────────

    /// <summary>
    /// The last <c>N = max(2, rate × voice_fadeouttime)</c> samples fall as <c>1 - k / (N - 1)</c>. A 100-sample spurt at 8 kHz
    /// is inside the 800-sample fade, so it starts 700 steps down: <c>(int)((1 - 700 / 799) × 1000) = (int)123.9 = 123</c>.
    /// </summary>
    [Test]
    public void Render_AShortSpurt_StartsPartWayDownTheFadeAndEndsSilent()
    {
        SoundSample sample = VoicePlan.Render(VoicePlan.Build([Packet(10, 3, 100)], Interval)[0], 0, 0, 1f, Steady);

        sample.Samples.Span[0].ShouldBe(123 / 32768f);
        sample.Samples.Span[99].ShouldBe(0f);
        (sample.SampleRate, sample.Channels, sample.Loops).ShouldBe((8000, 1, false));
    }

    [Test]
    public void Render_FromALaterPacket_StartsAtItsSamples() =>
        VoicePlan.Render(VoicePlan.Build([Packet(10, 3, 1600), Packet(11, 3, 1600)], Interval)[0], 1, 0, 1f, Steady)
            .FrameCount.ShouldBe(1600);

    [Test]
    public void Render_WithSamplesAlreadyRead_SkipsThem() =>
        VoicePlan.Render(VoicePlan.Build([Packet(10, 3, 1600)], Interval)[0], 0, 360, 1f, Steady).FrameCount.ShouldBe(1240);

    /// <summary>A fresh decoder per start: <c>Voice_AssignChannel</c> resets the channel's codec.</summary>
    [Test]
    public void Render_EachStart_MakesAFreshDecoder()
    {
        VoiceSpurt spurt = VoicePlan.Build([Packet(10, 3, 160)], Interval)[0];
        int made = 0;

        VoicePlan.Render(spurt, 0, 0, 1f, _ => { made++; return new SteadyDecoder(); });
        VoicePlan.Render(spurt, 0, 0, 1f, _ => { made++; return new SteadyDecoder(); });

        made.ShouldBe(2);
    }

    private static IVoiceStreamDecoder Steady(VoiceKind kind) => new SteadyDecoder();

    /// <summary>A Steam Voice payload: steamID, a 16 kHz rate record, a SILK record of two-byte frames, its CRC32 tail.</summary>
    private static byte[] SteamSilk(int frames)
    {
        List<byte> block = [];

        for (int i = 0; i < frames; i++)
        {
            block.AddRange(BitConverter.GetBytes((ushort)2));
            block.AddRange([1, 2]);
        }

        block.AddRange(BitConverter.GetBytes((ushort)0xFFFF));

        List<byte> payload = [.. BitConverter.GetBytes(76561197960265728UL), 0x0B, .. BitConverter.GetBytes((ushort)16000)];
        payload.Add(0x04);
        payload.AddRange(BitConverter.GetBytes((ushort)block.Count));
        payload.AddRange(block);
        payload.AddRange(BitConverter.GetBytes(System.IO.Hashing.Crc32.HashToUInt32([.. payload])));

        return [.. payload];
    }

    private static VoiceArrival Packet(int tick, int entity, int samples) =>
        new(tick, entity, Proximity: false, new VoiceFraming(VoiceKind.Speex, 8000, samples), Array.Empty<byte>());

    private static short[] Constant(int count, short value) => [.. Enumerable.Repeat(value, count)];

    /// <summary>Decodes every packet to a steady 1000, so a test needs no native library.</summary>
    private sealed class SteadyDecoder : IVoiceStreamDecoder
    {
        public int Decode(ReadOnlySpan<byte> body, Span<short> into)
        {
            into.Fill(1000);
            return 0;
        }

        public void Dispose()
        {
            // Nothing native to release.
        }
    }
}
