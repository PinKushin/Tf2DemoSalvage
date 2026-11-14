using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>
/// `snd_musicvolume` at the sink: the gain a `#` one-shot at `SNDLVL_NONE` is started with, and that it follows a
/// change while it plays, as the engine's per-mix channel gain does (FUN_1800292f0, B545;
/// <c>SoundMusicVolumeConformanceTests</c> holds the citation).
/// </summary>
/// <remarks>
/// The sink here keeps voices as the engine keeps channels: each start is its own voice, a named channel's next start
/// ends the one before (`SND_PickDynamicChannel`, FUN_18002a480, hands back the busy channel), CHAN_AUTO never does,
/// and a stop ends the first voice on its key playing that sound (`S_AlterChannel`, FUN_18002aa20).
/// </remarks>
public sealed class SoundPresenterMusicVolumeTests
{
    private const int Entity = 3;
    private const int AutoChannel = 0;
    private const int WeaponChannel = 1;

    [Test]
    public void Update_AMusicOneShot_PlaysAtVolumeTimesMusicVolumeAndFollowsAChange()
    {
        float musicVolume = 0.25f;
        (SoundPresenter presenter, Sink sink) = Presenter(() => musicVolume, Sound(50, "#music/song.wav", 0.8f));

        Run(presenter, sink, 40, 60);
        sink.Started.ShouldBe([0.2f], "volume 0.8 × snd_musicvolume 0.25");

        musicVolume = 0.5f;
        Run(presenter, sink, 61);

        sink.Gained.ShouldBe([0.4f], "re-gained on the next pass while it plays");
    }

    /// <remarks>`_Host_RunFrame_Sound` (engine.dll FUN_18002fbe0) has no skip test: what is playing keeps its gain current through a skip's frames (B708).</remarks>
    [Test]
    public void FollowListener_OnAFrameOfASkip_GainsWhatPlaysAndStartsNothing()
    {
        float musicVolume = 0.25f;
        (SoundPresenter presenter, Sink sink) = Presenter(() => musicVolume, Sound(50, "#music/song.wav", 0.8f), Sound(70, "#music/next.wav", 0.8f));

        Run(presenter, sink, 40, 60);
        int started = sink.Started.Count;

        musicVolume = 0.5f;
        presenter.FollowListener(sink, (0f, 0f, 0f), (0f, -1f, 0f));

        sink.Gained.ShouldBe([0.4f]);
        sink.Started.Count.ShouldBe(started, "the sound at tick 70 is not reached by a frame of a skip");
    }

    [Test]
    public void Update_AMusicOneShotAtMusicVolumeZero_StillStartsSoRaisingItIsHeard()
    {
        float musicVolume = 0f;
        (SoundPresenter presenter, Sink sink) = Presenter(() => musicVolume, Sound(50, "#music/song.wav", 0.8f));

        Run(presenter, sink, 40, 60);
        musicVolume = 1f;
        Run(presenter, sink, 61);

        sink.Started.ShouldBe([0f]);
        sink.Gained[^1].ShouldBe(0.8f);
    }

    /// <remarks>The headphone law (SpeakerPan, B724): soundlevel 0 is fully mono, 0.9 in each ear; a sound to the right
    /// at soundlevel 75 is all in the right ear. The listener in Run looks down +X, so its right is −Y.</remarks>
    [Test]
    public void Update_AGlobalSoundAndOneToTheRight_ArePannedByTheHeadphoneLaw()
    {
        (SoundPresenter presenter, Sink sink) = Presenter(
            static () => 1f,
            new SceneSound(50, "ui/beep.wav", 1, Entity, AutoChannel, 1f, 0, 100, 0f, 0f, -100f, 0f),
            new SceneSound(55, "ui/right.wav", 1, Entity, AutoChannel, 1f, 75, 100, 0f, 0f, -100f, 0f));

        Run(presenter, sink, 40, 60);

        sink.Pans.ShouldBe([(0.9f, 0.9f), (0f, 1f)]);
    }

    /// <remarks>
    /// `SND_Spatialize` re-pans every channel every frame (B724): a loop to the listener's right moves to the left ear when
    /// the listener turns round, on the next pass, to its own voice.
    /// </remarks>
    [Test]
    public void Update_AListenerTurningRoundALoop_RepansItsVoiceEachPass()
    {
        (SoundPresenter presenter, Sink sink) = Presenter(
            static () => 1f,
            new SceneSound(50, "ambient/hum_loop.wav", 1, Entity, AutoChannel, 1f, 75, 100, 0f, 0f, -100f, 0f));

        Run(presenter, sink, 40, 60);
        presenter.Update(sink, 61, (0f, 0f, 0f), (0f, 1f, 0f), now: 0.61);

        sink.Pans.ShouldBe([(0f, 1f)], "started in the right ear");
        sink.Panned[^1].ShouldBe((1, 1f, 0f), "and re-panned to the left once the listener faced the other way");
    }

    /// <remarks>
    /// The device's law decides the pan (B724): a sound 45° to the front right is 0.35 and 0.875 on headphones and only in
    /// the right speaker on two (its front right virtual speaker straight at it).
    /// </remarks>
    [TestCase(true, 0.35002905f, 0.875f)]
    [TestCase(false, 0f, 1f)]
    public void Update_ASoundAheadRight_IsPannedByTheDevicesLaw(bool headphones, float left, float right)
    {
        SceneSound sound = new(50, "ui/right.wav", 1, Entity, AutoChannel, 1f, 75, 100, 0f, 100f, -100f, 0f);
        (SoundPresenter presenter, Sink sink) = Presenter(static () => 1f, headphones, sound);

        Run(presenter, sink, 40, 60);

        sink.Pans.ShouldHaveSingleItem().Left.ShouldBe(left, 1e-6f);
        sink.Pans[0].Right.ShouldBe(right, 1e-6f);
    }

    /// <remarks>`S_AlterChannel` (engine.dll FUN_18002aa20) acts on any channel playing the sound, not only a loop (B724).</remarks>
    [Test]
    public void Update_AVolumeChangeOnAOneShotStillPlaying_AltersItRatherThanStartingAnother()
    {
        (SoundPresenter presenter, Sink sink) = Presenter(
            static () => 1f,
            Sound(50, "ui/beep.wav", 0.5f, WeaponChannel),
            Sound(52, "ui/beep.wav", 0.2f, WeaponChannel) with { ChangesVolume = true });

        Run(presenter, sink, 40, 51, 53);

        sink.Started.ShouldBe([0.5f], "one start; the change is not a second");
        sink.GainedBy[^1].ShouldBe((1, 0.2f));
    }

    /// <remarks>
    /// `SND_Spatialize` asks the client where each channel's entity is every frame (`GetSoundSpatialization`,
    /// `c_baseentity.cpp:1864`), so a sound moves with what made it: here from the listener's right to its left (B724).
    /// </remarks>
    [Test]
    public void Update_AnEntityMovingAcrossTheListener_TakesItsSoundWithIt()
    {
        (float X, float Y, float Z) at = (0f, -100f, 0f);
        (SoundPresenter presenter, Sink sink) = Presenter(
            static () => 1f,
            new SceneSound(50, "npc/hum.wav", 1, Entity, AutoChannel, 1f, 75, 100, 0f, 0f, -100f, 0f));
        presenter.EntityOrigin = (entity, _) => entity == Entity ? (at.X, at.Y, at.Z, 0f) : null;

        Run(presenter, sink, 40, 60);
        at = (0f, 100f, 0f);
        Run(presenter, sink, 61);

        sink.Pans.ShouldBe([(0f, 1f)], "started in the right ear");
        sink.Panned[^1].ShouldBe((1, 1f, 0f), "and followed its entity to the left");
    }

    /// <remarks>
    /// The view entity's own sound is 12 units ahead and, inside half its model's radius, in both ears at 0.9 + 0.9
    /// (SpeakerPan.Spatialize, B724), however far to the side its origin is: soundlevel 75 at 12 units is full gain.
    /// </remarks>
    [Test]
    public void Update_TheViewEntitysOwnSound_PlaysAheadInBothEars()
    {
        (SoundPresenter presenter, Sink sink) = Presenter(
            static () => 1f,
            new SceneSound(50, "player/step.wav", 1, Entity, AutoChannel, 1f, 75, 100, 0f, 0f, -800f, 0f));
        presenter.ViewEntity = Entity;
        presenter.EntityOrigin = (entity, _) => entity == Entity ? (0f, -800f, 0f, 48f) : null;

        Run(presenter, sink, 40, 60);

        sink.Started.ShouldBe([1f]);
        sink.Pans.ShouldHaveSingleItem().Left.ShouldBe(1.8f, 1e-6f);
        sink.Pans[0].Right.ShouldBe(1.8f, 1e-6f);
    }

    /// <remarks>
    /// A channel takes the client's origin at its start and keeps the last it took when the client stops answering (a
    /// dormant entity, B724): it does not fall back to the origin its message carried.
    /// </remarks>
    [Test]
    public void Update_AnEntityThatStopsAnsweringAfterTheStart_KeepsTheOriginTheStartTook()
    {
        bool answering = true;
        (SoundPresenter presenter, Sink sink) = Presenter(
            static () => 1f,
            new SceneSound(50, "npc/hum.wav", 1, Entity, AutoChannel, 1f, 75, 100, 0f, 0f, 100f, 0f));
        presenter.EntityOrigin = (entity, _) => answering && entity == Entity ? (0f, -100f, 0f, 0f) : null;

        Run(presenter, sink, 40, 60);
        answering = false;
        Run(presenter, sink, 61);

        sink.Pans.ShouldBe([(0f, 1f)], "started where the client put it, to the right");
        sink.Panned[^1].ShouldBe((1, 0f, 1f), "and stays there, not at the message's origin to the left");
    }

    /// <remarks>
    /// The crossing log keys a followed voice by its handle. A loop replaced on its named channel is dropped by
    /// `ActiveLoops.Track` without its voice ever failing a gain, so only a per-pass rebuild lets its state go.
    /// </remarks>
    [Test]
    public void Update_ALoopReplacedOnItsChannel_LeavesOneCrossingState()
    {
        (SoundPresenter presenter, Sink sink) = Presenter(
            static () => 1f,
            headphones: true,
            new RecordingLogger(),
            Sound(50, "npc/a_loop.wav", 1f, channel: 1),
            Sound(60, "npc/b_loop.wav", 1f, channel: 1));

        Run(presenter, sink, 40, 50, 51, 60, 61);

        sink.Loops.Count.ShouldBe(1);
        presenter.CrossingStates.ShouldBe(1, "the replaced voice's state goes with it");
    }

    [Test]
    public void Update_AnOrdinaryOneShot_IgnoresMusicVolume()
    {
        (SoundPresenter presenter, Sink sink) = Presenter(static () => 0.25f, Sound(50, "music/song.wav", 0.8f));

        Run(presenter, sink, 40, 60);

        sink.Started.ShouldBe([0.8f]);
    }

    /// <remarks>CHAN_AUTO gives each start its own channel, so the music keeps following and the beep keeps its own gain.</remarks>
    [Test]
    public void Update_AnOrdinarySoundOverlappingMusicOnAuto_LeavesBothTheirOwnGains()
    {
        (SoundPresenter presenter, Sink sink) = Presenter(
            static () => 0.25f,
            Sound(50, "#music/song.wav", 0.8f),
            Sound(55, "ui/beep.wav", 0.6f));

        Run(presenter, sink, 40, 60, 61);

        sink.Started.ShouldBe([0.2f, 0.6f]);
        sink.GainedBy.ShouldBe([(1, 0.2f), (2, 0.6f)], "every channel is re-gained each pass (B724), each to its own level");
    }

    [Test]
    public void Update_AnOrdinarySoundReplacingMusicOnANamedChannel_EndsTheFollowing()
    {
        (SoundPresenter presenter, Sink sink) = Presenter(
            static () => 0.25f,
            Sound(50, "#music/song.wav", 0.8f, WeaponChannel),
            Sound(55, "ui/beep.wav", 0.6f, WeaponChannel));

        Run(presenter, sink, 40, 60, 61);

        sink.Started.ShouldBe([0.2f, 0.6f]);
        sink.Gained.ShouldBe([0.6f], "the beep took the music's channel and is re-gained to its own 0.6, never music × 0.25");
    }

    /// <remarks>
    /// `S_AlterChannel` matches by <c>CSfxTable</c>, which `S_FindName` keys on the whole name lower-cased with its
    /// separators fixed (FUN_18002b290, `utlsymbol.cpp:312`): case and slash direction do not matter, the `#` does.
    /// </remarks>
    [TestCase("#music/song.wav", true)]
    [TestCase("#MUSIC/Song.WAV", true)]
    [TestCase("#music\\song.wav", true)]
    [TestCase("#music//song.wav", true)]
    [TestCase("music/song.wav", false)]
    public void Update_AStopOnAMusicChannel_ForgetsTheMusicWhenItIsTheSameSfx(string stop, bool forgets)
    {
        (SoundPresenter presenter, Sink sink) = Presenter(
            static () => 0.25f,
            Sound(50, "#music/song.wav", 0.8f),
            Sound(55, stop, 0.8f) with { IsStop = true });

        Run(presenter, sink, 40, 52, 53);
        sink.Gained.ShouldBe([0.2f], "followed before the stop");

        // Pass 60 re-gains before the stop it carries, so 2; pass 61 adds one only for a stop that missed.
        Run(presenter, sink, 60, 61);

        sink.Gained.Count.ShouldBe(forgets ? 2 : 3);
    }

    [Test]
    public void Update_AStopNamingAnotherSound_KeepsFollowingTheMusic()
    {
        (SoundPresenter presenter, Sink sink) = Presenter(
            static () => 0.25f,
            Sound(50, "#music/song.wav", 0.8f),
            Sound(55, "#music/other.wav", 0.8f) with { IsStop = true });

        Run(presenter, sink, 40, 60, 61);

        sink.Gained.ShouldBe([0.2f], "S_AlterChannel stops only the sound a stop names");
    }

    [Test]
    public void Update_AVolumeChangeOnAMusicOneShot_AltersItAndKeepsTheNewVolume()
    {
        (SoundPresenter presenter, Sink sink) = Presenter(
            static () => 0.25f,
            Sound(50, "#music/song.wav", 0.8f),
            Sound(55, "#music/song.wav", 0.4f) with { ChangesVolume = true });

        Run(presenter, sink, 40, 60, 61);

        sink.Started.ShouldBe([0.2f], "altered in place, not restarted");
        sink.Gained.ShouldBe([0.1f, 0.1f], "the change, then the next pass keeping it");
    }

    [Test]
    public void Update_AVolumeChangeAfterTheMusicEnded_StartsItAgain()
    {
        (SoundPresenter presenter, Sink sink) = Presenter(
            static () => 0.25f,
            Sound(50, "#music/song.wav", 0.8f),
            Sound(55, "#music/song.wav", 0.4f) with { ChangesVolume = true });

        Run(presenter, sink, 40, 52);

        // The voice survives this pass's re-gain and has ended by the change.
        sink.GainsLeft = 1;
        Run(presenter, sink, 60);

        sink.Started.ShouldBe([0.2f, 0.1f], "S_AlterChannel matched nothing, so S_StartSound starts it");
    }

    [Test]
    public void Update_APitchChangeAloneOnMusic_KeepsItsVolume()
    {
        (SoundPresenter presenter, Sink sink) = Presenter(
            static () => 0.25f,
            Sound(50, "#music/song.wav", 0.8f),
            Sound(55, "#music/song.wav", 0.1f) with { ChangesPitch = true, Pitch = 120 });

        Run(presenter, sink, 40, 60, 61);

        sink.Started.ShouldBe([0.2f]);
        sink.Pitched.ShouldBe([1.2f]);
        sink.Gained[^1].ShouldBe(0.2f, "a pitch-only change carries no volume to take");
    }

    [Test]
    public void Update_AChangeNamingAnotherSound_StartsIt()
    {
        (SoundPresenter presenter, Sink sink) = Presenter(
            static () => 0.25f,
            Sound(50, "#music/song.wav", 0.8f),
            Sound(55, "#music/other.wav", 0.4f) with { ChangesVolume = true });

        Run(presenter, sink, 40, 60);

        sink.Started.ShouldBe([0.2f, 0.1f]);
    }

    [Test]
    public void Update_WhilePaused_AppliesAMusicVolumeChange()
    {
        float musicVolume = 0.25f;
        (SoundPresenter presenter, Sink sink) = Presenter(() => musicVolume, Sound(50, "#music/song.wav", 0.8f));

        Run(presenter, sink, 40, 60);
        musicVolume = 0.5f;
        Run(presenter, sink, 60);

        sink.Gained.ShouldBe([0.4f], "a paused pass is the same tick again, and still re-gains");
    }

    [Test]
    public void Update_AMusicOneShotAndALoopOnOneKey_KeepTheirOwnGains()
    {
        (SoundPresenter presenter, Sink sink) = Presenter(
            static () => 0.25f,
            Sound(50, "ambient/hum_loop.wav", 0.6f),
            Sound(55, "#music/song.wav", 0.8f));

        Run(presenter, sink, 40, 60, 61);

        sink.GainedBy.ShouldBe([(1, 0.6f), (2, 0.2f)], "each voice at its own gain, in start order as the engine walks its channels");
    }

    [Test]
    public void Update_AStopNamingTheMusicOnALoopsKey_LeavesTheLoopFollowed()
    {
        (SoundPresenter presenter, Sink sink) = Presenter(
            static () => 0.25f,
            Sound(50, "ambient/hum_loop.wav", 0.6f),
            Sound(51, "#music/song.wav", 0.8f),
            Sound(55, "#music/song.wav", 0.8f) with { IsStop = true });

        Run(presenter, sink, 40, 60);
        sink.GainedBy.Clear();
        Run(presenter, sink, 61);

        sink.GainedBy.ShouldBe([(1, 0.6f)], "only the loop is re-gained");
    }

    /// <remarks>`S_AlterChannel` acts on the first match in channel order, so after a voice ends and another starts, a change
    /// still reaches the oldest one still playing — not the newest, which a hash map reusing the freed slot would list first.</remarks>
    [Test]
    public void Update_AVolumeChangeAfterAVoiceEndedAndAnotherStarted_AltersTheOldestStillPlaying()
    {
        (SoundPresenter presenter, Sink sink) = Presenter(
            static () => 1f,
            Sound(50, "#music/song.wav", 0.5f),
            Sound(51, "#music/song.wav", 0.6f),
            Sound(52, "#music/song.wav", 0.7f),
            Sound(56, "#music/song.wav", 0.8f),
            Sound(58, "#music/song.wav", 0.1f) with { ChangesVolume = true });

        Run(presenter, sink, 40, 53);
        sink.Finish(1);
        Run(presenter, sink, 54, 57);
        sink.GainedBy.Clear();
        Run(presenter, sink, 59);

        // The pass re-gains every music voice first (2, 3, 4 at their volumes), then the change alters one.
        sink.GainedBy.ShouldBe([(2, 0.6f), (3, 0.7f), (4, 0.8f), (2, 0.1f)], "the change reaches voice 2, the first still playing");
    }

    [Test]
    public void Update_AVolumeChangeOnALoopItsChannelReplaced_StartsTheLoopAgain()
    {
        (SoundPresenter presenter, Sink sink) = Presenter(
            static () => 1f,
            Sound(50, "ambient/hum_loop.wav", 0.6f, WeaponChannel),
            Sound(52, "ui/beep.wav", 0.5f, WeaponChannel),
            Sound(55, "ambient/hum_loop.wav", 0.3f, WeaponChannel) with { ChangesVolume = true });

        // Pass 53 starts the beep after its re-gain, so pass 54 is the first to find the loop's voice gone.
        Run(presenter, sink, 40, 51, 53, 54);
        sink.Loops.Count.ShouldBe(1, "the replaced loop is no longer followed; the beep that took its channel is");

        Run(presenter, sink, 60);

        sink.Started.ShouldBe([0.6f, 0.5f, 0.3f], "the loop's voice was replaced, so the change matched nothing and started it");
    }

    [Test]
    public void Update_AVolumeChangeOnALoopThatEndedThisPass_StartsTheLoopAgain()
    {
        (SoundPresenter presenter, Sink sink) = Presenter(
            static () => 1f,
            Sound(50, "ambient/hum_loop.wav", 0.6f, WeaponChannel),
            Sound(55, "ambient/hum_loop.wav", 0.3f, WeaponChannel) with { ChangesVolume = true });

        Run(presenter, sink, 40, 51);

        // The voice survives this pass's re-gain and has gone by the change.
        sink.GainsLeft = 1;
        Run(presenter, sink, 60);

        sink.Started.ShouldBe([0.6f, 0.3f], "S_AlterChannel matched nothing, so the change starts the loop");
    }

    [Test]
    public void Update_ARefusedAutoLoopStart_LeavesTheLiveLoopsOnTheEntityFollowed()
    {
        (SoundPresenter presenter, Sink sink) = Presenter(
            static () => 1f,
            Sound(50, "ambient/hum_loop.wav", 0.6f),
            Sound(51, "ambient/fan_loop.wav", 0.4f),
            Sound(52, "ambient/drip_loop.wav", 0.2f));
        sink.RefuseStart = 3;

        Run(presenter, sink, 40, 60, 61);

        sink.Loops.Count.ShouldBe(2, "a refused start plays nothing and is not followed");
        sink.GainedBy.ShouldBe([(1, 0.6f), (2, 0.4f)]);
    }

    /// <remarks>`S_AlterChannel` walks every channel and acts on the first still playing the sound (FUN_18002aa20).</remarks>
    [Test]
    public void Update_AChangeWhoseFirstMatchHasEnded_AltersTheNextMatchInsteadOfStarting()
    {
        (SoundPresenter presenter, Sink sink) = Presenter(
            static () => 1f,
            Sound(50, "#music/song.wav", 0.5f),
            Sound(51, "#music/song.wav", 0.6f),
            Sound(55, "#music/song.wav", 0.1f) with { ChangesVolume = true });

        Run(presenter, sink, 40, 52);

        // Voice 1 lives through this pass's re-gain and has ended by the change.
        sink.GainsLeftFor[1] = 1;
        sink.GainedBy.Clear();
        Run(presenter, sink, 60);

        sink.Started.ShouldBe([0.5f, 0.6f], "a later match still plays, so nothing is started");
        sink.GainedBy[^1].ShouldBe((2, 0.1f));
    }

    [Test]
    public void Update_TwoAutoLoopsOnOneEntity_AreBothFollowed()
    {
        (SoundPresenter presenter, Sink sink) = Presenter(
            static () => 1f,
            Sound(50, "ambient/hum_loop.wav", 0.6f),
            Sound(51, "ambient/fan_loop.wav", 0.4f));

        Run(presenter, sink, 40, 60, 61);

        sink.GainedBy.ShouldBe([(1, 0.6f), (2, 0.4f)], "each CHAN_AUTO loop is its own channel");
    }

    private static void Run(SoundPresenter presenter, Sink sink, params int[] ticks)
    {
        foreach (int tick in ticks)
        {
            presenter.Update(sink, tick, (0f, 0f, 0f), (0f, -1f, 0f), now: tick / 100d);
        }
    }

    private static SceneSound Sound(int tick, string name, float volume, int channel = AutoChannel) =>
        new(tick, name, 1, Entity, channel, volume, 0, 100, 0f, 0f, 0f, 0f);

    private static (SoundPresenter Presenter, Sink Sink) Presenter(Func<float> musicVolume, params SceneSound[] sounds) =>
        Presenter(musicVolume, headphones: true, sounds);

    private static (SoundPresenter Presenter, Sink Sink) Presenter(Func<float> musicVolume, bool headphones, params SceneSound[] sounds) =>
        Presenter(musicVolume, headphones, NullLogger.Instance, sounds);

    private static (SoundPresenter Presenter, Sink Sink) Presenter(
        Func<float> musicVolume, bool headphones, ILogger audio, params SceneSound[] sounds)
    {
        ActiveLoops loops = new();

        // One decode per name, as SoundCache keeps it, so a stop's sample names the sound it stops.
        Dictionary<string, SoundSample> samples = new(StringComparer.Ordinal);

        SoundPresenter presenter = new(
            new SoundscapeSystem(new ActiveLoops(), _ => null, NullLogger.Instance),
            loops,
            name =>
            {
                // Keyed as SoundCache keys it, by the engine's sfx identity.
                string key = Tf2DemoSalvage.Core.Net.SoundName.SfxKey(name);

                if (!samples.TryGetValue(key, out SoundSample sample))
                {
                    sample = new(
                        SampleRate: 22050,
                        Channels: 1,
                        Samples: new float[] { 0.5f, -0.5f },
                        Loops: name.EndsWith("_loop.wav", StringComparison.Ordinal));
                    samples[key] = sample;
                }

                return sample;
            },
            audio)
        {
            Schedule = new SoundSchedule(sounds),
            MusicVolume = musicVolume,
            Headphones = () => headphones,

            // A stop asks what is held and loads nothing: here, what an earlier start decoded.
            Held = name => samples.TryGetValue(Tf2DemoSalvage.Core.Net.SoundName.SfxKey(name), out SoundSample sample) ? sample : null,
        };

        return (presenter, new Sink { Loops = loops });
    }

    private sealed class Sink : IAudioSink
    {
        private readonly List<(int Id, int Entity, int Channel, SoundSample Sample, bool Alive)> _voices = [];

        public float MasterGain { get; set; } = 1f;

        /// <summary>The presenter's loops, so a test can see what is still followed.</summary>
        public ActiveLoops Loops { get; init; } = new();

        public List<float> Started { get; } = [];

        public List<(float Left, float Right)> Pans { get; } = [];

        public List<(int Voice, float Left, float Right)> Panned { get; } = [];

        public bool SetVoicePan(int voice, float leftPan, float rightPan)
        {
            if (!Alive(voice))
            {
                return false;
            }

            Panned.Add((voice, leftPan, rightPan));
            return true;
        }

        public List<(int Voice, float Gain)> GainedBy { get; } = [];

        public List<float> Gained => [.. GainedBy.Select(static gain => gain.Gain)];

        public List<float> Pitched { get; } = [];

        /// <summary>How many more voice gains the voices live through before they have ended.</summary>
        public int GainsLeft { get; set; } = int.MaxValue;

        public bool HandsOutVoices => true;

        /// <summary>Which start, counting from 1, the sink refuses — answering 0 and playing nothing.</summary>
        public int RefuseStart { get; set; }

        /// <summary>How many more gains a given voice lives through before it has ended.</summary>
        public Dictionary<int, int> GainsLeftFor { get; } = [];

        private int _attempts;

        public int Play(SoundSample sample, float leftPan, float rightPan, float gain, float pitch, int entity, int channel)
        {
            if (++_attempts == RefuseStart)
            {
                return 0;
            }

            if (channel != AutoChannel)
            {
                End(index => _voices[index].Entity == entity && _voices[index].Channel == channel, all: true);
            }

            Started.Add(gain);
            Pans.Add((leftPan, rightPan));
            _voices.Add((_voices.Count + 1, entity, channel, sample, true));

            return _voices.Count;
        }

        public bool SetVoiceGain(int voice, float gain)
        {
            if (!Alive(voice))
            {
                return false;
            }

            bool doomed = GainsLeftFor.TryGetValue(voice, out int left) && left <= 0;

            if (GainsLeftFor.ContainsKey(voice))
            {
                GainsLeftFor[voice] = left - 1;
            }

            if (doomed || GainsLeft-- <= 0)
            {
                End(index => _voices[index].Id == voice, all: true);
                return false;
            }

            GainedBy.Add((voice, gain));
            return true;
        }

        public bool SetVoicePitch(int voice, float pitch)
        {
            if (!Alive(voice))
            {
                return false;
            }

            Pitched.Add(pitch);
            return true;
        }

        public bool SetGain(int entity, int channel, float gain) => false;

        public bool SetPitch(int entity, int channel, float pitch) => false;

        public void SilenceAll() => End(static _ => true, all: true);

        public int Reclaim() => 0;

        public void Silence(int entity, int channel) =>
            End(index => _voices[index].Entity == entity && _voices[index].Channel == channel, all: true);

        public int Silence(int entity, int channel, SoundSample sample) =>
            End(
                index => _voices[index].Entity == entity && _voices[index].Channel == channel &&
                         _voices[index].Sample.Samples.Equals(sample.Samples),
                all: false);

        /// <summary>One voice finishes on its own.</summary>
        public void Finish(int voice) => End(index => _voices[index].Id == voice, all: true);

        private bool Alive(int voice) => voice > 0 && voice <= _voices.Count && _voices[voice - 1].Alive;

        private int End(Func<int, bool> matches, bool all)
        {
            int first = 0;

            for (int index = 0; index < _voices.Count; index++)
            {
                if (_voices[index].Alive && matches(index))
                {
                    _voices[index] = _voices[index] with { Alive = false };
                    first = first == 0 ? _voices[index].Id : first;

                    if (!all)
                    {
                        break;
                    }
                }
            }

            return first;
        }
    }
}
