using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

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

    /// <summary>
    /// The mixer fades the last <c>voice_fadeouttime</c> (0.1 s) of what is buffered (FUN_180057dc0): 50 ms before the
    /// 1.6 s that arrived runs out, the voice is at half its gain.
    /// </summary>
    [Test]
    public void Update_CloseToRunningDry_FadesTheVoice()
    {
        Recording sink = new();
        VoicePresenter presenter = Presenter(Spurt(proximity: false, packets: 12));

        Step(presenter, sink, 10, 0d);
        Step(presenter, sink, 17, 0.1d);
        Step(presenter, sink, 17, 1.65d);

        sink.VoiceGains[^1].Voice.ShouldBe(7);
        sink.VoiceGains[^1].Gain.ShouldBe(0.5f, 1e-3f);
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

    /// <summary>
    /// **Slow playback starves, as the engine's does.** Packets of one tick's audio (120 samples, 15 ms) arrive one per
    /// tick; at a quarter speed a tick takes 60 ms of the mixer's real time, so the buffer runs dry and the channel ends.
    /// </summary>
    [Test]
    public void Update_PlayingAtAQuarterSpeed_Starves()
    {
        Recording sink = new();
        VoicePresenter presenter = Presenter(Spurt(proximity: false, packets: 40, samples: 120));

        for (int tick = 10; tick <= 40; tick++)
        {
            Step(presenter, sink, tick, (tick - 10) * 0.06d);
        }

        sink.Silenced.ShouldNotBeEmpty();
    }

    /// <summary>The control: the same packets at the recording's own speed arrive as fast as they play, and never starve.</summary>
    [Test]
    public void Update_PlayingAtFullSpeed_NeverStarves()
    {
        Recording sink = new();
        VoicePresenter presenter = Presenter(Spurt(proximity: false, packets: 40, samples: 120));

        for (int tick = 10; tick <= 40; tick++)
        {
            Step(presenter, sink, tick, (tick - 10) * 0.015d);
        }

        sink.Silenced.ShouldBeEmpty();
    }

    /// <summary>A resume decodes from its own packet with a fresh decoder: <c>Voice_AssignChannel</c> resets the codec.</summary>
    [Test]
    public void Update_AJumpIntoASpurt_DecodesFreshFromTheResumePacket()
    {
        Recording sink = new();
        List<int> decodedFrom = [];
        VoicePresenter presenter = new()
        {
            Plan = [Spurt(proximity: false, packets: 3)],
            Decoders = _ => new Counting(decodedFrom),
            Renderer = render => Task.FromResult(render()),
        };

        presenter.Update(sink, 10, jumped: true, Origin, Ears, 0d, View, Nowhere);
        presenter.Update(sink, 12, jumped: true, Origin, Ears, 0.03d, View, Nowhere);

        decodedFrom.ShouldBe([3, 1], "a decoder per start, each fed only the packets from its own start");
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

    /// <summary>A decode that finishes while the demo is paused past its pad starts then, as the engine's buffered start would.</summary>
    [Test]
    public void Update_ADecodeFinishingWhilePausedPastThePad_StartsThen()
    {
        Recording sink = new();
        TaskCompletionSource<SoundSample> decode = new();
        Func<SoundSample>? render = null;
        VoicePresenter presenter = new()
        {
            Plan = [Spurt(proximity: false)],
            Decoders = _ => new Steady(),
            Renderer = work => { render = work; return decode.Task; },
        };

        Step(presenter, sink, 10, 0d);
        Step(presenter, sink, 17, 0.1d);
        sink.Played.ShouldBeEmpty("still decoding");

        decode.SetResult(render!());
        Step(presenter, sink, 17, 0.2d);

        sink.Played.Count.ShouldBe(1);
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

    private static VoicePresenter Presenter(VoiceSpurt spurt) => new() { Plan = [spurt], Decoders = _ => new Steady(), Renderer = render => Task.FromResult(render()) };

    /// <summary>1600-sample packets (0.2 s at 8 kHz) from entity 3, one a tick from tick 10: starts at 17.</summary>
    private static VoiceSpurt Spurt(bool proximity, int packets = 2, int samples = 1600) =>
        VoicePlan.Build(
            [.. Enumerable.Range(0, packets).Select(i =>
                new VoiceArrival(10 + i, 3, proximity, new VoiceFraming(VoiceKind.Speex, 8000, samples), Array.Empty<byte>()))],
            0.015f).ShouldHaveSingleItem();

    /// <summary>Records how many packets each fresh decoder was fed.</summary>
    private sealed class Counting(List<int> fed) : IVoiceStreamDecoder
    {
        private int _packets;

        public int Decode(ReadOnlySpan<byte> body, Span<short> into)
        {
            _packets++;
            into.Fill(1000);
            return 0;
        }

        public void Dispose() => fed.Add(_packets);
    }

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

        int IAudioSink.Play(SoundSample sample, float leftPan, float rightPan, float gain, float pitch, int entity, int channel)
        {
            Played.Add(new Play(sample, leftPan, rightPan, gain, pitch, entity, channel));
            return 7;
        }

        public bool SetGain(int entity, int channel, float gain) => false;

        public List<(int Voice, float Gain)> VoiceGains { get; } = [];

        public bool SetVoiceGain(int voice, float gain)
        {
            VoiceGains.Add((voice, gain));
            return true;
        }

        public bool SetVoicePitch(int voice, float pitch) => false;

        public bool SetPitch(int entity, int channel, float pitch) => false;

        public void SilenceAll() => Silenced.Add((-1, -1));

        public int Reclaim() => 0;

        public void Silence(int entity, int channel) => Silenced.Add((entity, channel));

        public int Silence(int entity, int channel, SoundSample sample)
        {
            Silenced.Add((entity, channel));
            return 0;
        }
    }
}
