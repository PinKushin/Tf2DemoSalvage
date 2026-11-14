using System;

using Tf2DemoSalvage.Audio;

namespace Tf2DemoSalvage.Audio.Tests;

/// <summary>A start's delay, and a loop started again where it already plays, on the loopback mix (B732).</summary>
/// <remarks>
/// x64 <c>engine.dll</c> build 11191711.
/// <para>
/// <c>S_StartDynamicSound</c> (<c>FUN_18002e6e0</c>), after the channel is set up and found audible, with a delay
/// <c>params+0x4c</c> other than 0 (<c>0x18002ef7c-0x18002f063</c>): samples = <c>(int)(delay × source rate)</c>
/// (<c>cvttss2si</c>, 0x18002efaa). Above 0, the mixer is
/// told to wait that many samples (vtable +0x50, <c>FUN_18004f6d0</c>, which stores it at mixer +0x2c) and the channel is
/// flagged 0x20. <c>CAudioMixerWave::MixDataToDevice</c> (<c>FUN_18004f0c0</c>) pays the wait from +0x2c in the pass's
/// OUTPUT samples, writing silence, before it reads a sample; and <c>MIX_PaintChannels</c> (<c>FUN_180042870</c>) mixes each
/// channel in the pass of its own rate — <c>FUN_180040ae0(…, 0x2b11, 0x2b11)</c>, then 0x5622, then 0xac44 — so the wait
/// is samples / rate seconds whatever the pitch. Below 0, skip =
/// −samples, taken modulo the sample count when the wave loops; a skip of the whole wave or more frees the channel and
/// starts nothing; otherwise the mixer skips that many source samples at pitch 1 (vtable +0x10, <c>FUN_18004f6f0</c>) and
/// the channel is flagged 0x20 too.
/// </para>
/// <para>
/// <c>S_StartStaticSound</c> (<c>FUN_18002f4c0</c>) differs: samples = <c>(int)(pitch × 0.01 × rate × delay)</c>, and only
/// a positive delay sets 0x20 (<c>0x18002fbf3</c>; the negative path's flag write is <c>and 0x7f</c>, 0x18002fc71).
/// </para>
/// <para>
/// Loop keep, <c>0x18002e8a4-0x18002e8df</c>: the channel the pick gave still holds an sfx whose source loops (+0x58), on
/// the same source (+0xac), the same entchannel (+0xb0, as each start gave it) and the same sfx (+0x8): the start returns
/// 0 and leaves it playing.
/// Otherwise the channel is freed (<c>FUN_180045cc0</c>) and the new sound takes it.
/// </para>
/// <para>
/// A delay-started channel told by the pick to free itself (+0xa8, at the new start's painted time) is freed by the mix that
/// reaches that time (<c>MIX_MixChannelsToPaintbuffer</c>, <c>FUN_180040ae0</c> at <c>0x180040ef1</c>).
/// </para>
/// </remarks>
[NonParallelizable]
public sealed class AudioOutputDelayConformanceTests
{
    private const int Body = 4;
    private const int Static = 6;

    private static AudioOutput Open() => AudioOutputDeviceTests.OpenLoopback();

    private static SoundSample Tone(int frames = 44100) => AudioOutputDeviceTests.Tone(frames) with { Key = "tone" };

    private static SoundPlace Delayed(float delay) => new(null, (0f, 0f, 0f), false, Delay: delay);

    [Test]
    public void Play_APositiveDelay_HoldsItsChannelSilentUntilTheMixerReachesIt()
    {
        using AudioOutput output = Open();
        int voice = output.Play(Tone(), 1f, 1f, entity: 1, channel: Body, place: Delayed(0.1f));

        voice.ShouldNotBe(0);
        output.StartsAt(voice).ShouldBe(4410L);
        AudioOutputDeviceTests.Level(output.Render(4096)).ShouldBe(0d, "waiting out its delay");
        output.Reclaim().ShouldBe(0, "a waiting channel is held");
        output.Playing.ShouldBe(1);

        _ = output.Render(4410 - 4096);
        _ = output.Reclaim();
        output.StartsAt(voice).ShouldBeNull("started when the mix reached it");
        AudioOutputDeviceTests.Level(output.Render(8192)).ShouldBeGreaterThan(0.05);
    }

    [TestCase(Body, 1f, 4410)]
    [TestCase(Body, 2f, 4410)]
    [TestCase(Static, 2f, 8820)]
    public void Play_ANegativeDelay_StartsPartWayIn(int channel, float pitch, int offset)
    {
        using AudioOutput output = Open();
        int voice = output.Play(Tone(), 1f, 1f, pitch: pitch, entity: 1, channel: channel, place: Delayed(-0.1f));

        output.StartsAt(voice).ShouldBeNull("playing at once");
        output.OffsetOf(voice).ShouldBe(offset, "a dynamic skip is the source's own samples; a static one is pitched");
    }

    [Test]
    public void Play_ANegativeDelayPastTheEnd_StartsNothingAndALoopWrapsIt()
    {
        using AudioOutput output = Open();

        output.Play(Tone(frames: 11025), 1f, 1f, entity: 1, channel: Body, place: Delayed(-0.3f)).ShouldBe(0);
        output.Refusal.ShouldBe(StartRefusal.SkippedPastTheEnd);
        output.Playing.ShouldBe(0);

        int looped = output.Play(Tone(frames: 11025) with { Loops = true }, 1f, 1f, entity: 1, channel: Body, place: Delayed(-0.3f));
        output.OffsetOf(looped).ShouldBe(13230 % 11025);
    }

    [Test]
    public void Play_ALoopAgainOnItsOwnSourceAndChannel_KeepsTheOnePlaying()
    {
        using AudioOutput output = Open();
        SoundSample hum = Tone() with { Loops = true };
        int first = output.Play(hum, 1f, 1f, entity: 1, channel: Body);

        output.Play(hum, 1f, 1f, entity: 1, channel: Body).ShouldBe(0);
        output.Refusal.ShouldBe(StartRefusal.LoopKept);
        output.SetVoicePitch(first, 1f).ShouldBeTrue("the loop plays on");
        output.Playing.ShouldBe(1);

        output.Play(hum, 1f, 1f, entity: 2, channel: Body).ShouldNotBe(0, "another source is no match");
        int once = output.Play(Tone(), 1f, 1f, entity: 3, channel: Body);
        output.Play(Tone(), 1f, 1f, entity: 3, channel: Body).ShouldNotBe(0, "the control: a one-shot restarts");
        output.SetVoicePitch(once, 1f).ShouldBeFalse();
    }

    [Test]
    public void Play_OverAChannelWaitingOutItsDelay_FreesItWhenTheNewSoundStarts()
    {
        using AudioOutput output = Open();
        int waiting = output.Play(Tone(), 1f, 1f, entity: 1, channel: Body, place: Delayed(1f));
        int next = output.Play(Tone() with { Key = "next" }, 1f, 1f, entity: 1, channel: Body, place: Delayed(0.05f));

        next.ShouldNotBe(0, "one waiting match is not reused: a free channel is taken");
        output.Playing.ShouldBe(2, "both held until the new one starts");

        _ = output.Render(2000);
        _ = output.Reclaim();
        output.SetVoicePitch(waiting, 1f).ShouldBeTrue("the control: before the new start, held");

        _ = output.Render(400);
        _ = output.Reclaim();
        output.SetVoicePitch(waiting, 1f).ShouldBeFalse("freed at the new start's time");
        output.SetVoicePitch(next, 1f).ShouldBeTrue();
    }

    /// <summary>A start timed on the server clock, as the presenter dispatches it: the delay asked once, then played.</summary>
    private static int PlayServerTimed(AudioOutput output, float time, int entity, int channel = Body, bool fromServer = true) =>
        output.Play(
            Tone(),
            1f,
            1f,
            entity: entity,
            channel: channel,
            place: new SoundPlace(null, (0f, 0f, 0f), false, Delay: output.DelayFor(new ServerSoundTime(time, 0.015f)), FromServer: fromServer));

    /// <summary>One tick (661 samples) plus snd_delay_sound_shift: the latching sound's wait in samples.</summary>
    private const long Latched = (int)(((661f / 44100f) + 0.01f) * 44100f);

    /// <summary>0.1 s of server time past a latch at 10 s: 5071 samples past its base, as samples of wait.</summary>
    private const long Later = (int)(((5071f / 44100f) + 0.01f) * 44100f);

    [Test]
    public void DelayFor_WithNoServerDelayedVoiceAlive_LatchesTheClockAfresh()
    {
        using AudioOutput output = Open();

        int first = PlayServerTimed(output, 10f, entity: 1);
        output.StartsAt(first).ShouldBe(Latched);

        // 0.1 s later in server time while the first waits: on the same latch.
        int second = PlayServerTimed(output, 10.1f, entity: 2);
        output.StartsAt(second).ShouldNotBeNull().ShouldBeInRange(Later - 1, Later + 1, "the control: still latched");

        // Out of step by 40 s: the delay is 0, made 1e-6: no wait, but a delayed start.
        int far = PlayServerTimed(output, 50f, entity: 3);
        output.StartsAt(far).ShouldBeNull();

        output.StopAll();
        _ = PlayServerTimed(output, 10f, entity: 4);
        output.StopAll();
        int unpassed = PlayServerTimed(output, 10.1f, entity: 5);
        output.StartsAt(unpassed).ShouldNotBeNull().ShouldBeInRange(Later - 1, Later + 1, "the control: no mix pass yet, still latched");

        output.StopAll();
        _ = output.Reclaim();

        int again = PlayServerTimed(output, 10.1f, entity: 6);
        output.StartsAt(again).ShouldBe(Latched, "a pass with nothing delayed alive: latched afresh, one tick ahead");
    }

    /// <remarks>
    /// The channel list holds the server latch only for a channel with 0x20 AND 0x40 (`params.fromserver`):
    /// FUN_180040420's `bVar2` (0x18004082f). A client-delayed voice does not hold it.
    /// </remarks>
    [Test]
    public void DelayFor_WithOnlyAClientDelayedVoiceAlive_LatchesTheClockAfresh()
    {
        using AudioOutput output = Open();

        _ = PlayServerTimed(output, 10f, entity: 1);
        output.StopAll();
        int client = output.Play(Tone(), 1f, 1f, entity: 2, channel: Body, place: Delayed(0.5f));
        output.StartsAt(client).ShouldNotBeNull("the client voice waits, delay-started");
        _ = output.Reclaim();

        int next = PlayServerTimed(output, 10.1f, entity: 3);
        output.StartsAt(next).ShouldBe(Latched, "a client's delay holds no server latch");

        output.StopAll();
        _ = output.Reclaim();
        _ = PlayServerTimed(output, 10f, entity: 4);
        _ = output.Reclaim();
        int held = PlayServerTimed(output, 10.1f, entity: 5);
        output.StartsAt(held).ShouldNotBeNull().ShouldBeInRange(Later - 1, Later + 1, "the control: a server's holds it");
    }

    /// <remarks>
    /// `host_frametime &lt; host_frametime_unbounded` drops the latch (FUN_180040420, 0x180040911-0x180040919): the frame was
    /// clamped, which `Host_FilterTime` (FUN_1801a1920) does to a frame past 0.1 s. Here the frame is the painted time
    /// between mix passes.
    /// </remarks>
    [Test]
    public void DelayFor_AfterAFrameLongerThanATenth_LatchesTheClockAfresh()
    {
        using AudioOutput output = Open();

        // A server voice waiting far ahead holds the latch throughout, so only the frame can drop it.
        _ = PlayServerTimed(output, 10f, entity: 1);
        _ = output.Play(Tone(), 1f, 1f, entity: 9, channel: Body, place: new SoundPlace(null, (0f, 0f, 0f), false, Delay: 5f, FromServer: true));
        _ = output.Render(2000);

        // 4400 painted in one block, under a tenth: still latched, so 10.2 waits its server time ahead of the mixer.
        _ = output.Render(4400);
        int steady = PlayServerTimed(output, 10.2f, entity: 2);
        output.StartsAt(steady).ShouldNotBeNull().ShouldBeGreaterThan(6400 + Latched + 1000, "the control: a 0.0998 s frame keeps it");
        _ = output.Render(3600);

        // 4500 in one block, past a tenth: dropped, so 30 s latches afresh rather than falling out of step.
        _ = output.Render(4500);
        int hitch = PlayServerTimed(output, 30f, entity: 3);
        output.StartsAt(hitch).ShouldBe(14500 + Latched, "a 0.102 s frame drops it");
    }

    [Test]
    public void Play_ALateFreeTimeOfZero_IsUnsetAndFreesNothing()
    {
        using AudioOutput output = Open();
        int waiting = output.Play(Tone(), 1f, 1f, entity: 1, channel: Body, place: Delayed(1f));

        // At painted 0 with no delay, the new start's `+0xa8` is (uint)(long)(0 × 44100 + 0) = 0: unset.
        _ = output.Play(Tone() with { Key = "next" }, 1f, 1f, entity: 1, channel: Body);
        _ = output.Render(100);
        _ = output.Reclaim();

        output.SetVoicePitch(waiting, 1f).ShouldBeTrue("the mixer's `!= 0` guard before 0x180040ef1");
    }

    [Test]
    public void Play_ANegativeDelay_FlagsADynamicVoiceSoAFollowUpPassesItOver()
    {
        using AudioOutput output = Open();
        _ = output.Render(100);
        int skipped = output.Play(Tone(), 1f, 1f, entity: 1, channel: Body, place: Delayed(-0.1f));

        _ = output.Play(Tone() with { Key = "next" }, 1f, 1f, entity: 1, channel: Body);
        output.SetVoicePitch(skipped, 1f).ShouldBeTrue("delay-started: gathered, not reused");
        _ = output.Reclaim();
        output.SetVoicePitch(skipped, 1f).ShouldBeFalse("freed at the new start, painted 100");

        int plain = output.Play(Tone(), 1f, 1f, entity: 2, channel: Body);
        _ = output.Play(Tone() with { Key = "next" }, 1f, 1f, entity: 2, channel: Body);
        output.SetVoicePitch(plain, 1f).ShouldBeFalse("the control: no delay, reused at once");
    }

    /// <remarks>A static voice never enters the dynamic pick; its 0x20 shows in the server latch it holds or does not.</remarks>
    [TestCase(0.5f, true)]
    [TestCase(-0.1f, false)]
    public void Play_AStaticServerVoiceDelayed_HoldsTheLatchOnlyWhenPositive(float delay, bool holds)
    {
        using AudioOutput output = Open();

        // Latched by a dynamic server voice, then stopped, while a static server voice of the given delay plays.
        int latch = PlayServerTimed(output, 10f, entity: 1);
        _ = output.Play(Tone(), 1f, 1f, entity: 2, channel: Static, place: new SoundPlace(null, (0f, 0f, 0f), false, Delay: delay, FromServer: true));
        output.SetVoicePitch(latch, 1f).ShouldBeTrue();
        ((IAudioSink)output).Silence(1, Body);
        output.SetVoicePitch(latch, 1f).ShouldBeFalse();
        _ = output.Reclaim();

        int next = PlayServerTimed(output, 10.1f, entity: 3);

        if (holds)
        {
            output.StartsAt(next).ShouldNotBeNull().ShouldBeInRange(Later - 1, Later + 1);
        }
        else
        {
            output.StartsAt(next).ShouldBe(Latched);
        }
    }

    [Test]
    public void PaintedAt_FourteenHours_IsPastAnInt()
    {
        AudioOutput.PaintedAt(14 * 3600).ShouldBe(14L * 3600 * 44100);
    }

    [Test]
    public void Reclaim_WithAVoiceWaiting_AllocatesNothing()
    {
        using AudioOutput output = Open();
        _ = output.Play(Tone(), 1f, 1f, entity: 1, channel: Body, place: Delayed(1f));
        _ = output.Play(Tone() with { Key = "next" }, 1f, 1f, entity: 1, channel: Body, place: Delayed(0.5f));
        _ = output.Reclaim();
        _ = output.Reclaim();

        long before = GC.GetAllocatedBytesForCurrentThread();
        _ = output.Reclaim();

        (GC.GetAllocatedBytesForCurrentThread() - before).ShouldBe(0L);
    }

    [Test]
    public void Render_ABlockReachingADelayedStart_BeginsItInsideTheBlock()
    {
        using AudioOutput output = Open();
        _ = output.Play(Tone(), 1f, 1f, entity: 1, channel: Body, place: Delayed(0.1f));

        float[] mix = output.Render(8820);

        AudioOutputDeviceTests.Level(mix[..(4410 * 2)]).ShouldBe(0d, "before its time");
        AudioOutputDeviceTests.Level(mix[(4410 * 2)..]).ShouldBeGreaterThan(0.05, "from its time, in the same block");
    }

    [Test]
    public void OffsetOf_AVoiceGone_IsNull()
    {
        using AudioOutput output = Open();

        output.OffsetOf(12345).ShouldBeNull();
    }
}
