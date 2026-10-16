using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Audio;

namespace Tf2DemoSalvage.Audio.Tests;

/// <summary>The engine's client voice path, as TF2's x64 engine.dll runs it, between a decoded packet and the mixer.</summary>
/// <remarks>
/// **Read from the binary, because the SDK has none of it** — <c>voice.cpp</c> is engine code. Every function named here is
/// in the 2026 x64 <c>engine.dll</c>:
///
/// | function | what it is |
/// |---|---|
/// | <c>FUN_1801fa0d0</c> | <c>CClientState::ProcessVoiceData</c> — entity <c>client + 1</c>, then get-or-assign a channel |
/// | <c>FUN_180057350</c> | <c>Voice_AssignChannel</c> — a free one of five; resets the buffer, the gain and the pad |
/// | <c>FUN_1800591d0</c> | <c>CAutoGain::Reset(128, voice_maxgain, voice_avggain, voice_scale)</c> |
/// | <c>FUN_180056eb0</c> | <c>Voice_AddIncomingData</c> — decompress, <c>CAutoGain::ProcessSamples</c>, upsample into the buffer |
/// | <c>FUN_180058fe0</c> | <c>CAutoGain::ProcessSamples</c> |
/// | <c>FUN_180058090</c> | <c>Voice_Idle</c> — counts each channel's pad down by frame time, starts it at zero, ends it when starved |
/// | <c>FUN_180057dc0</c> | <c>Voice_GetOutputData</c> — the mixer's read, with the linear fade over the last <c>voice_fadeouttime</c> |
/// | <c>FUN_18005a670</c> | <c>VoiceSE_StartChannel</c> — the sound: channel <c>8 + index</c>, volume 1, pitch 100, its soundlevel |
/// </remarks>
public sealed class VoiceConformanceTests
{
    private const float Interval = 0.015f;

    // ── CAutoGain ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// <c>Reset</c> starts the fixed-point gain at <c>0x80</c> — 1.0 — with no ramp, so the first block passes unchanged
    /// whatever <c>voice_scale</c> says (FUN_1800591d0: <c>param_1[9] = 0x80; param_1[10] = 0</c>).
    /// </summary>
    [Test]
    public void Process_TheFirstBlock_PassesUnchanged()
    {
        short[] samples = Constant(128, 1000);

        new VoiceAutoGain(scale: 0f).Process(samples);

        samples.ShouldAllBe(sample => sample == 1000);
    }

    /// <summary>
    /// At a block's end the next gain is <c>min(32767 / (avg + (max - avg) * voice_avggain), voice_maxgain)</c>, and
    /// the block after ramps to it from the current gain in <c>(next - cur) / 127</c> steps of 1/128 (FUN_180058fe0).
    /// A steady 1000 asks 32.767, capped at <c>voice_maxgain</c> 10, so block two ramps 1.0 → 10 in steps of
    /// <c>(int)(9 / 127 * 128) = 9</c>: <c>(127 * 9 + 128) * 1000 >> 7 = 9929</c>.
    /// </summary>
    [Test]
    public void Process_TheSecondBlock_RampsTowardTheCappedGain()
    {
        short[] samples = Constant(256, 1000);

        new VoiceAutoGain(scale: 1f).Process(samples);

        (samples[128], samples[255]).ShouldBe(((short)1000, (short)9929));
    }

    /// <summary>
    /// **<c>voice_scale</c> is applied twice, and that is the engine's.** <c>m_CurGain = m_NextGain * m_flScale</c> where
    /// <c>m_NextGain</c> already carried the scale (FUN_180058fe0: <c>fVar6 = param_1[6] * param_1[8]</c>, then
    /// <c>param_1[8] = fVar5 * param_1[6]</c>). At 0.5, block three starts at <c>0.5 × (10 × 0.5) = 2.5</c>.
    /// </summary>
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

    // ── Voice_AssignChannel / Voice_Idle: the plan ────────────────────────────────────────────────

    /// <summary>
    /// <c>voice_buffer_ms</c> 100 is the pad (FUN_180057350: <c>m_TimePad = clamp(ms, 1, 5000) * 0.001</c>), and
    /// <c>Voice_Idle</c> starts the channel on the first frame it reaches zero: <c>ceil(0.1 / 0.015) = 7</c> ticks late.
    /// </summary>
    [Test]
    public void Build_OnePacket_StartsSevenTicksLateOnTheFirstChannel()
    {
        VoiceSpurt spurt = VoicePlan.Build([Packet(10, entity: 3, samples: 160)], Interval).ShouldHaveSingleItem();

        (spurt.Entity, spurt.Channel, spurt.StartTick, spurt.Pcm.Length).ShouldBe((3, 0, 17, 160));
    }

    [Test]
    public void EngineChannel_ForTheThirdSlot_IsTen() =>
        VoicePlan.EngineChannel(2).ShouldBe(10, "VoiceSE_StartChannel: entchannel = index + 8, CHAN_VOICE_BASE");

    [Test]
    public void Build_APacketBeforeTheBufferEmpties_JoinsTheSameSpurt()
    {
        VoiceSpurt spurt = VoicePlan.Build([Packet(10, 3, 160), Packet(11, 3, 160)], Interval).ShouldHaveSingleItem();

        spurt.Pcm.Length.ShouldBe(320);
        spurt.Packets.Select(packet => (packet.Tick, packet.Offset)).ShouldBe([(10, 0), (11, 160)]);
    }

    /// <summary>
    /// A channel whose buffer runs dry is ended (<c>Voice_Idle</c>: <c>m_bStarved</c> → <c>Voice_EndChannel</c>,
    /// FUN_1800578c0), so the next packet assigns a fresh one with a fresh pad. 20 ms starting at 0.25 s is dry by 0.27 s;
    /// a packet at 0.45 s begins again.
    /// </summary>
    [Test]
    public void Build_APacketAfterTheBufferRanDry_StartsANewSpurt()
    {
        IReadOnlyList<VoiceSpurt> spurts = VoicePlan.Build([Packet(10, 3, 160), Packet(30, 3, 160)], Interval);

        spurts.Select(spurt => (spurt.Channel, spurt.StartTick)).ShouldBe([(0, 17), (0, 37)]);
    }

    /// <summary><c>Voice_AddIncomingData</c> refuses a channel above 4 (<c>if (4 &lt; param_1) return 0</c>): five at once, no sixth.</summary>
    [Test]
    public void Build_SixSpeakersAtOnce_PlaysTheFirstFive()
    {
        IReadOnlyList<VoiceSpurt> spurts = VoicePlan.Build([.. Enumerable.Range(1, 6).Select(entity => Packet(10, entity, 160))], Interval);

        spurts.Select(spurt => (spurt.Entity, spurt.Channel)).ShouldBe([(1, 0), (2, 1), (3, 2), (4, 3), (5, 4)]);
    }

    // ── Voice_GetOutputData: the fade ───────────────────────────────────────────────────────────

    /// <summary>
    /// The last <c>voice_fadeouttime × rate</c> samples (at least 2) fall linearly to zero: FUN_180057dc0 multiplies each by
    /// <c>1 - k / (N - 1)</c>, <c>k</c> counting from N samples before the end. A 100-sample spurt at 8 kHz is inside the
    /// 800-sample fade, so it starts 700 steps down: <c>(int)((1 - 700 / 799) × 1000) = (int)123.9 = 123</c>, and ends at zero.
    /// </summary>
    [Test]
    public void Render_AShortSpurt_StartsPartWayDownTheFadeAndEndsSilent()
    {
        VoiceSpurt spurt = VoicePlan.Build([Packet(10, 3, 100)], Interval)[0];

        SoundSample sample = VoicePlan.Render(spurt, fromOffset: 0, scale: 1f);

        sample.Samples.Span[0].ShouldBe(123 / 32768f);
        sample.Samples.Span[99].ShouldBe(0f);
        (sample.SampleRate, sample.Channels, sample.Loops).ShouldBe((8000, 1, false));
    }

    [Test]
    public void Render_FromALaterPacket_StartsAtItsSamples()
    {
        VoiceSpurt spurt = VoicePlan.Build([Packet(10, 3, 1600), Packet(11, 3, 1600)], Interval)[0];

        VoicePlan.Render(spurt, fromOffset: 1600, scale: 1f).FrameCount.ShouldBe(1600);
    }

    private static DecodedVoice Packet(int tick, int entity, int samples) =>
        new(tick, entity, Proximity: false, Constant(samples, 1000), SampleRate: 8000);

    private static short[] Constant(int count, short value) => [.. Enumerable.Repeat(value, count)];
}
