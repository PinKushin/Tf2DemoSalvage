using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Audio;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>Demo voice reaching a sink as the engine's voice channels play it.</summary>
/// <remarks>
/// **The sound** (engine.dll x64 <c>VoiceSE_StartChannel</c>, <c>FUN_18005a670</c>): <c>StartSoundParams_t</c> with
/// <c>entchannel = index + 8</c>, <c>fvol = 1.0</c>, <c>pitch = 100</c>, and
///
/// <code>
/// if ( bProximity == 1 ) { params.soundlevel = 0x50; params.soundsource = speaker entity; }   // SNDLVL_80dB
/// else                   { params.soundlevel = 0x3c; params.soundsource = view entity;    }   // SNDLVL_60dB
/// </code>
///
/// **The timing is the buffer's.** <c>Voice_Idle</c> (FUN_180058090) is called with the host frame time
/// (<c>_Host_RunFrame_Client</c> at 1801a59c6 loads it before the call), so the pad drains on demo time and a pause
/// holds it. The buffer holds only what has arrived, so a stalled demo plays out what arrived and then starves the
/// channel; the next packet assigns a fresh one behind a fresh pad (FUN_180057350).
/// </remarks>
public sealed class VoicePresenterConformanceTests
{
    private const int View = 1;

    private static readonly (float X, float Y, float Z) Origin = (0f, 0f, 0f);

    /// <summary>The listener's right vector: −Y, so a speaker at −Y is to the right.</summary>
    private static readonly (float X, float Y, float Z) Ears = (0f, -1f, 0f);

    [Test]
    public void Update_CrossingASpurtsStart_PlaysItCentredAtFullVolumeFromTheViewEntity()
    {
        Recording sink = new();
        VoicePresenter presenter = Presenter(Spurt(proximity: false));

        Step(presenter, sink, 10, 0d);
        Step(presenter, sink, 17, 0.1d);

        Recording.Play played = sink.Played.ShouldHaveSingleItem();
        (played.Entity, played.Channel, played.Gain, played.Pitch).ShouldBe((View, 8, 1f, 1f));
        played.Left.ShouldBe(played.Right, "a non-proximity voice is sourced at the view entity");
    }

    [Test]
    public void Update_AProximitySpurt_IsAttenuatedAt80DecibelsFromTheSpeaker()
    {
        Recording sink = new();
        VoicePresenter presenter = Presenter(Spurt(proximity: true));

        Step(presenter, sink, 10, 0d);
        presenter.Update(sink, 17, jumped: false, Origin, Ears, 0.1d, View, entity => entity == 3 ? (0f, -1000f, 0f) : null);

        Recording.Play played = sink.Played.ShouldHaveSingleItem();
        (played.Entity, played.Gain).ShouldBe((3, SoundGain.AtDistance(80, 1000f)));
        played.Right.ShouldBeGreaterThan(played.Left, "the speaker stands to the listener's right");
    }

    [Test]
    public void Update_AJumpIntoASpurt_ResumesAtTheNextPacketBehindAFreshPad()
    {
        Recording sink = new();
        VoicePresenter presenter = Presenter(Spurt(proximity: false));

        presenter.Update(sink, 11, jumped: true, Origin, Ears, 0d, View, Nowhere);
        sink.Played.ShouldBeEmpty("the pad has not drained");

        Step(presenter, sink, 18, 0.1d);

        sink.Played.ShouldHaveSingleItem().Sample.FrameCount.ShouldBe(1600, "packet two of two, from its own samples");
    }

    /// <summary>Stalled at 17, the packets of ticks 10–17 have arrived, 1.6 s, so 1.0 s into the stall it still plays.</summary>
    [Test]
    public void Update_APauseShorterThanWhatArrived_LeavesTheVoicePlaying()
    {
        Recording sink = new();
        VoicePresenter presenter = Presenter(Spurt(proximity: false, packets: 12));

        Step(presenter, sink, 10, 0d);
        Step(presenter, sink, 17, 0.1d);
        Step(presenter, sink, 17, 1.1d);

        sink.Silenced.ShouldBeEmpty();
    }

    [Test]
    public void Update_APauseLongerThanWhatArrived_SilencesTheVoice()
    {
        Recording sink = new();
        VoicePresenter presenter = Presenter(Spurt(proximity: false, packets: 12));

        Step(presenter, sink, 10, 0d);
        Step(presenter, sink, 17, 0.1d);
        Step(presenter, sink, 17, 1.8d);

        sink.Silenced.ShouldBe([(View, 8)]);
    }

    /// <summary>The pad drains on demo time: paused before it drains, the spurt still starts when the demo reaches it.</summary>
    [Test]
    public void Update_ASpurtWhosePadIsDrainingAcrossAPause_StillStartsWhole()
    {
        Recording sink = new();
        VoicePresenter presenter = Presenter(Spurt(proximity: false));

        Step(presenter, sink, 10, 0d);
        Step(presenter, sink, 12, 0.03d);
        Step(presenter, sink, 12, 5d);
        Step(presenter, sink, 17, 5.1d);

        sink.Played.ShouldHaveSingleItem().Sample.FrameCount.ShouldBe(3200);
    }

    /// <summary>Slow playback is not a stall: the demo keeps advancing, so the voice is never silenced.</summary>
    [Test]
    public void Update_PlayingAtATenthOfSpeed_NeverSilences()
    {
        Recording sink = new();
        VoicePresenter presenter = Presenter(Spurt(proximity: false, packets: 12));

        for (int tick = 10; tick <= 30; tick++)
        {
            Step(presenter, sink, tick, (tick - 10) * 0.15d);
        }

        sink.Silenced.ShouldBeEmpty();
    }

    [Test]
    public void Update_AStepBackWithoutAJump_SilencesBeforeResuming()
    {
        Recording sink = new();
        VoicePresenter presenter = Presenter(Spurt(proximity: false));

        Step(presenter, sink, 10, 0d);
        Step(presenter, sink, 17, 0.1d);
        Step(presenter, sink, 16, 0.11d);

        sink.Silenced.ShouldBe([(View, 8)]);
    }

    /// <summary>A step past the start starts part-way in: the mixer would have read <c>(20 - 17) × 0.015 × 8000 = 360</c>.</summary>
    [Test]
    public void Update_AStepThatOvershootsTheStart_SkipsWhatTheMixerHadRead()
    {
        Recording sink = new();
        VoicePresenter presenter = Presenter(Spurt(proximity: false));

        Step(presenter, sink, 10, 0d);
        Step(presenter, sink, 20, 0.15d);

        sink.Played.ShouldHaveSingleItem().Sample.FrameCount.ShouldBe(3200 - 360);
    }

    [Test]
    public void Update_WithVoiceDisabled_PlaysNothing()
    {
        Recording sink = new();
        VoicePresenter presenter = Presenter(Spurt(proximity: false));
        presenter.Enabled = false;

        Step(presenter, sink, 10, 0d);
        Step(presenter, sink, 17, 0.1d);

        sink.Played.ShouldBeEmpty();
    }

    [Test]
    public void Update_AtVoiceScaleZero_SilencesEverythingAfterTheSecondBlock()
    {
        Recording sink = new();
        VoicePresenter presenter = Presenter(Spurt(proximity: false));
        presenter.Scale = 0f;

        Step(presenter, sink, 10, 0d);
        Step(presenter, sink, 17, 0.1d);

        float[] samples = sink.Played.ShouldHaveSingleItem().Sample.Samples.ToArray();
        samples[0].ShouldBeGreaterThan(0f, "Reset's gain is 1.0 for the first block");
        samples.Skip(256).ShouldAllBe(sample => sample == 0f);
    }

    private static void Step(VoicePresenter presenter, Recording sink, int tick, double now) =>
        presenter.Update(sink, tick, jumped: false, Origin, Ears, now, View, Nowhere);

    private static (float X, float Y, float Z)? Nowhere(int entity) => null;

    private static VoicePresenter Presenter(VoiceSpurt spurt) => new() { Plan = [spurt], Decoders = _ => new Steady() };

    /// <summary>1600-sample packets (0.2 s at 8 kHz) from entity 3, one a tick from tick 10: starts at 17.</summary>
    private static VoiceSpurt Spurt(bool proximity, int packets = 2) =>
        VoicePlan.Build(
            [.. Enumerable.Range(0, packets).Select(i =>
                new VoiceArrival(10 + i, 3, proximity, new VoiceFraming(VoiceKind.Speex, 8000, 1600), Array.Empty<byte>()))],
            0.015f).ShouldHaveSingleItem();

    private sealed class Steady : IVoiceStreamDecoder
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

    private sealed class Recording : IAudioSink
    {
        public sealed record Play(SoundSample Sample, float Left, float Right, float Gain, float Pitch, int Entity, int Channel);

        public List<Play> Played { get; } = [];

        public List<(int Entity, int Channel)> Silenced { get; } = [];

        public float MasterGain { get; set; } = 1f;

        void IAudioSink.Play(SoundSample sample, float leftPan, float rightPan, float gain, float pitch, int entity, int channel) =>
            Played.Add(new Play(sample, leftPan, rightPan, gain, pitch, entity, channel));

        public bool SetGain(int entity, int channel, float gain) => false;

        public bool SetPitch(int entity, int channel, float pitch) => false;

        public void SilenceAll() => Silenced.Add((-1, -1));

        public int Reclaim() => 0;

        public void Silence(int entity, int channel) => Silenced.Add((entity, channel));

        public void Silence(int entity, int channel, SoundSample sample) => Silenced.Add((entity, channel));
    }
}
