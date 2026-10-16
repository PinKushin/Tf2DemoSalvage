using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Audio;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>Demo voice reaching a sink as the engine's <c>VoiceSE_StartChannel</c> starts it.</summary>
/// <remarks>
/// **The sound a voice channel plays** (engine.dll x64, <c>FUN_18005a670</c>): <c>StartSoundParams_t</c> with
/// <c>entchannel = index + 8</c>, <c>fvol = 1.0</c>, <c>pitch = 100</c>, and then one of two sources:
///
/// <code>
/// if ( bProximity == 1 ) { params.soundlevel = 0x50; params.soundsource = speaker entity; }   // SNDLVL_80dB
/// else                   { params.soundlevel = 0x3c; params.soundsource = view entity;    }   // SNDLVL_60dB
/// </code>
///
/// So proximity voice is spatialised from the speaker at 80 dB, and everything else plays from the listener's own
/// entity — centred and unattenuated, which is how TF2's ordinary team voice sounds.
///
/// **Seek and pause are what the engine's buffer does, not a rule of ours.** <c>Voice_Idle</c> (FUN_180058090) ends a
/// channel whose buffer has run dry, and the buffer only ever holds what has ARRIVED: paused, no packet arrives and the
/// voice stops once the pad has drained; after a jump, speech resumes with the next packet that arrives, behind a fresh
/// <c>voice_buffer_ms</c> pad (FUN_180057350).
/// </remarks>
public sealed class VoicePresenterConformanceTests
{
    private static readonly (float X, float Y, float Z) Origin = (0f, 0f, 0f);
    /// <summary>The listener's right vector: −Y, so a speaker at −Y is to the right.</summary>
    private static readonly (float X, float Y, float Z) Ears = (0f, -1f, 0f);

    [Test]
    public void Update_CrossingASpurtsStart_PlaysItCentredAtFullVolumeOnItsVoiceChannel()
    {
        Recording sink = new();
        VoicePresenter presenter = new() { Plan = [Spurt(proximity: false)] };

        presenter.Update(sink, 10, jumped: false, Origin, Ears, now: 0d, Nowhere);
        presenter.Update(sink, 17, jumped: false, Origin, Ears, now: 0.1d, Nowhere);

        Recording.Play played = sink.Played.ShouldHaveSingleItem();
        (played.Entity, played.Channel, played.Gain, played.Pitch).ShouldBe((3, 8, 1f, 1f));
        played.Left.ShouldBe(played.Right, "a non-proximity voice is sourced at the listener");
    }

    [Test]
    public void Update_AProximitySpurt_IsAttenuatedAt80DecibelsFromTheSpeaker()
    {
        Recording sink = new();
        VoicePresenter presenter = new() { Plan = [Spurt(proximity: true)] };

        presenter.Update(sink, 10, jumped: false, Origin, Ears, now: 0d, Nowhere);
        presenter.Update(sink, 17, jumped: false, Origin, Ears, now: 0.1d, entity => entity == 3 ? (0f, -1000f, 0f) : null);

        Recording.Play played = sink.Played.ShouldHaveSingleItem();
        played.Gain.ShouldBe(SoundGain.AtDistance(80, 1000f));
        played.Right.ShouldBeGreaterThan(played.Left, "the speaker stands to the listener's right");
    }

    [Test]
    public void Update_AJumpIntoASpurt_ResumesAtTheNextPacketBehindAFreshPad()
    {
        Recording sink = new();
        VoicePresenter presenter = new() { Plan = [Spurt(proximity: false)] };

        presenter.Update(sink, 11, jumped: true, Origin, Ears, now: 0d, Nowhere);
        sink.Played.ShouldBeEmpty("the pad has not drained");

        presenter.Update(sink, 18, jumped: false, Origin, Ears, now: 0.1d, Nowhere);

        sink.Played.ShouldHaveSingleItem().Sample.FrameCount.ShouldBe(1600, "packet two of two, from its own samples");
    }

    [Test]
    public void Update_PausedLongerThanThePad_SilencesThePlayingVoice()
    {
        Recording sink = new();
        VoicePresenter presenter = new() { Plan = [Spurt(proximity: false)] };

        presenter.Update(sink, 10, jumped: false, Origin, Ears, now: 0d, Nowhere);
        presenter.Update(sink, 17, jumped: false, Origin, Ears, now: 0.1d, Nowhere);
        presenter.Update(sink, 17, jumped: false, Origin, Ears, now: 0.15d, Nowhere);
        sink.Silenced.ShouldBeEmpty("50 ms is inside the pad");

        presenter.Update(sink, 17, jumped: false, Origin, Ears, now: 0.25d, Nowhere);

        sink.Silenced.ShouldBe([(3, 8)]);
    }

    [Test]
    public void Update_AtVoiceScaleZero_SilencesEverythingAfterTheSecondBlock()
    {
        Recording sink = new();
        VoicePresenter presenter = new() { Plan = [Spurt(proximity: false)], Scale = 0f };

        presenter.Update(sink, 10, jumped: false, Origin, Ears, now: 0d, Nowhere);
        presenter.Update(sink, 17, jumped: false, Origin, Ears, now: 0.1d, Nowhere);

        float[] samples = sink.Played.ShouldHaveSingleItem().Sample.Samples.ToArray();
        samples[0].ShouldBeGreaterThan(0f, "Reset's gain is 1.0 for the first block");
        samples.Skip(256).ShouldAllBe(sample => sample == 0f);
    }

    private static (float X, float Y, float Z)? Nowhere(int entity) => null;

    /// <summary>Two 1600-sample packets of a steady 1000 from entity 3, ticks 10 and 11: starts at 17.</summary>
    private static VoiceSpurt Spurt(bool proximity) =>
        VoicePlan.Build(
            [
                new DecodedVoice(10, 3, proximity, Enumerable.Repeat((short)1000, 1600).ToArray(), 8000),
                new DecodedVoice(11, 3, proximity, Enumerable.Repeat((short)1000, 1600).ToArray(), 8000),
            ],
            0.015f).ShouldHaveSingleItem();

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
