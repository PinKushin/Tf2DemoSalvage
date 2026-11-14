using Tf2DemoSalvage.Audio;

namespace Tf2DemoSalvage.Audio.Tests;

/// <summary>A start's delay, and a loop started again where it already plays, on the loopback mix (B732).</summary>
/// <remarks>
/// x64 <c>engine.dll</c> build 11191711.
/// <para>
/// <c>S_StartDynamicSound</c> (<c>FUN_18002e6e0</c>), after the channel is set up and found audible, with a delay
/// <c>params+0x4c</c> other than 0 (<c>0x18002ef7c-0x18002f063</c>): samples = <c>(int)(delay × source rate)</c>
/// (<c>cvttss2si</c>, 0x18002efaa). Above 0, the mixer is
/// told to wait that many samples (vtable +0x50, <c>FUN_18004f6d0</c>, consumed as silence at the mix rate in
/// <c>CAudioMixerWave::MixDataToDevice</c>, <c>FUN_18004f0c0</c>) and the channel is flagged 0x20. Below 0, skip =
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
        output.StartsAt(voice).ShouldBe(4410);
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

    [Test]
    public void Play_ServerTimedWithNoDelayedVoiceAlive_LatchesTheClockAfresh()
    {
        using AudioOutput output = Open();
        SoundPlace At(float time) => new(null, (0f, 0f, 0f), false, ServerTime: new ServerSoundTime(time, 0.015f));

        // One tick (661 samples) plus snd_delay_sound_shift: the delay in samples, (int)(delay × 44100).
        int latched = (int)(((661f / 44100f) + 0.01f) * 44100f);
        int first = output.Play(Tone(), 1f, 1f, entity: 1, channel: Body, place: At(10f));
        output.StartsAt(first).ShouldBe(latched);

        // 0.1 s later in server time while the first waits: on the same latch, 5071 samples past its base.
        int second = output.Play(Tone(), 1f, 1f, entity: 2, channel: Body, place: At(10.1f));
        int later = (int)(((5071f / 44100f) + 0.01f) * 44100f);
        output.StartsAt(second).ShouldNotBeNull().ShouldBeInRange(later - 1, later + 1, "the control: still latched");

        // Out of step by 40 s: the delay is 0, made 1e-6: no wait, but a delayed start.
        int far = output.Play(Tone(), 1f, 1f, entity: 3, channel: Body, place: At(50f));
        output.StartsAt(far).ShouldBeNull();

        output.StopAll();
        _ = output.Play(Tone(), 1f, 1f, entity: 4, channel: Body, place: At(10f));
        output.StopAll();

        int again = output.Play(Tone(), 1f, 1f, entity: 5, channel: Body, place: At(10.1f));
        output.StartsAt(again).ShouldBe(latched, "nothing delayed alive: latched afresh, one tick ahead");
    }
}
