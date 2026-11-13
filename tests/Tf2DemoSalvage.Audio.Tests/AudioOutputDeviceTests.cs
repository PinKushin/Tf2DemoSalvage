using System;
using System.Linq;

using Tf2DemoSalvage.Audio;

namespace Tf2DemoSalvage.Audio.Tests;

/// <summary>
/// The parts of <see cref="AudioOutput"/> that need a device, on a device that needs no sound card.
/// </summary>
/// <remarks>
/// **79 of this type's mutants had no test touching them at all** (B217), because every suite here
/// was written device-free on a premise `AudioOutputMixTests` states plainly: *"The measurement
/// boxes and CI have no sound card, so a test that needed one would skip everywhere it matters"*.
///
/// **That premise is wrong, and the owner spotted it:** *"openAl is corss platform isnt it, so as
/// long as we dont need game audio file it should be good right?"*. Two things make it right —
///
/// - **OpenAL Soft has a null backend.** `ALSOFT_DRIVERS=null` selects an output that renders to
///   nothing, so `alcOpenDevice` succeeds with no hardware, no server and no display. That is what
///   it is for.
/// - **The library already ships for the boxes.** `Silk.NET.OpenAL.Soft.Native` carries
///   `runtimes/linux-arm64/native/libopenal.so`, which is `mutation-box`'s architecture. Nothing
///   needs installing; it simply was never asked for.
///
/// So these are not "device tests that will skip on CI" — they are device tests that run anywhere,
/// which is the only kind worth writing given
/// `docs/memory/read-the-trx-total-not-the-console.md#a-skip-is-not-a-pass-or-a-failure`.
///
/// **They still degrade honestly.** If a machine somehow cannot open even the null device, every
/// test here ignores with a reason rather than passing vacuously.
///
/// **No game audio is involved.** Every sample is synthesised, so nothing here depends on an
/// install — which is the other half of why the boxes could not run the existing audio tests.
///
/// The null backend is selected by <see cref="AudioDriverPolicy"/>, which is assembly-scoped because
/// OpenAL reads the driver list once per process; see that type for what a per-fixture version cost.
/// </remarks>
[NonParallelizable]
public sealed class AudioOutputDeviceTests
{
    /// <summary>A quarter second of tone, so a source has something real to play.</summary>
    private static SoundSample Tone(int frames = 11025)
    {
        float[] samples = new float[frames];

        for (int at = 0; at < frames; at++)
        {
            samples[at] = MathF.Sin(at * 0.05f) * 0.25f;
        }

        return new SoundSample(44100, 1, samples);
    }

    private static AudioOutput Open()
    {
        if (AudioOutput.TryCreate(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance) is not { } output)
        {
            Assert.Ignore("no OpenAL device could be opened, not even the null one");
            throw new InvalidOperationException("unreachable: Assert.Ignore throws");
        }

        return output;
    }

    /// <summary>A cache whose install holds two different 16-bit WAVs, long enough to still be playing when stopped.</summary>
    private static SoundCache TwoWaves(long capacityBytes = SoundCache.CapacityBytes)
    {
        byte[] first = SoundCacheTests.WaveBytes(16, 1, 22050, new byte[22050 * 2]);
        byte[] second = SoundCacheTests.WaveBytes(16, 1, 22050, [.. Enumerable.Repeat((byte)7, 22050 * 2)]);

        return new SoundCache(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, capacityBytes)
        {
            Read = path => path.EndsWith("a.wav", StringComparison.Ordinal) ? first : second,
        };
    }

    /// <remarks>
    /// **A stop names one sfx, and only a voice playing that sfx matches** (`S_AlterChannel`, FUN_18002aa20; B416, B545).
    /// Every WAV is held as 16-bit with its float memory empty, so an identity on the float memory matched the first WAV
    /// voice on the channel whatever it played (finding 80). CHAN_STATIC, so both play at once.
    /// </remarks>
    [Test]
    public void Silence_TwoWavesOnOneStaticChannel_StopsTheOneNamed()
    {
        using AudioOutput output = Open();
        IAudioSink sink = output;
        SoundCache cache = TwoWaves();

        int first = output.Play(cache.Sample("a.wav")!.Value, 1f, 1f, entity: 1, channel: 6);
        int second = output.Play(cache.Sample("b.wav")!.Value, 1f, 1f, entity: 1, channel: 6);

        sink.Silence(1, 6, cache.Sample("b.wav")!.Value).ShouldBe(second);
        output.SetVoiceGain(first, 0.5f).ShouldBeTrue("the other wave plays on");
    }

    /// <remarks>
    /// **An evicted sound is the same sfx when it decodes again** (finding 80). A voice still playing a sound the cache let
    /// go must stop when the demo names it, though the stop's sample is a fresh decode in new memory.
    /// </remarks>
    [Test]
    public void Silence_ASoundEvictedWhilePlaying_StillStopsItsVoice()
    {
        using AudioOutput output = Open();
        IAudioSink sink = output;
        SoundCache cache = TwoWaves(capacityBytes: 22050 * 2);

        output.Play(cache.Sample("b.wav")!.Value, 1f, 1f, entity: 1, channel: 6);
        int voice = output.Play(cache.Sample("a.wav")!.Value, 1f, 1f, entity: 1, channel: 6);
        cache.Sample("b.wav");
        cache.Held.Sounds.ShouldBe(1, "the control: the cap holds one, so a was evicted for b");

        sink.Silence(1, 6, cache.Sample("a.wav")!.Value).ShouldBe(voice);
    }

    [Test]
    public void Play_ASample_CountsAsPlaying()
    {
        using AudioOutput output = Open();

        output.Playing.ShouldBe(0, "a fresh output holds no voices");

        output.Play(Tone(), leftPan: 1f, rightPan: 1f, entity: 1, channel: 1);

        output.Playing.ShouldBe(1, "the sample became a voice");
    }

    /// <remarks>
    /// Each start is its own voice, as each is its own engine channel: two CHAN_AUTO starts on one entity are two
    /// (`SND_PickDynamicChannel`, engine.dll FUN_18002a330, never hands a CHAN_AUTO request a busy channel), and a
    /// named channel's next start ends the voice before it.
    /// </remarks>
    [Test]
    public void SetVoiceGain_TwoAutoVoicesAndANamedReplacement_ReachesOnlyVoicesStillPlaying()
    {
        using AudioOutput output = Open();

        int first = output.Play(Tone(), 1f, 1f, entity: 1, channel: 0);
        int second = output.Play(Tone(), 1f, 1f, entity: 1, channel: 0);
        int named = output.Play(Tone(), 1f, 1f, entity: 1, channel: 1);
        int replacing = output.Play(Tone(), 1f, 1f, entity: 1, channel: 1);

        new[] { first, second, named, replacing }.Distinct().Count().ShouldBe(4);
        output.SetVoiceGain(first, 0.5f).ShouldBeTrue("CHAN_AUTO overlaps");
        output.SetVoiceGain(second, 0.5f).ShouldBeTrue();
        output.SetVoiceGain(named, 0.5f).ShouldBeFalse("the named channel's next start replaced it");
        output.SetVoicePitch(replacing, 1.2f).ShouldBeTrue();
        output.SetVoiceGain(0, 0.5f).ShouldBeFalse("0 names no voice");
    }

    /// <remarks>
    /// A source that has finished stays in the list until <c>Reclaim</c>; "alive" must mean audible, or a change to
    /// ended music alters nothing instead of starting it, as `S_AlterChannel` matching nothing does (FUN_18002aa20).
    /// Synchronised on the condition with a deadline, never on a clock.
    /// </remarks>
    [Test]
    public void SetVoiceGain_AVoiceThatHasFinished_IsGoneAndReaped()
    {
        using AudioOutput output = Open();

        int voice = output.Play(Tone(frames: 64), 1f, 1f, entity: 1, channel: 0);
        System.Diagnostics.Stopwatch waited = System.Diagnostics.Stopwatch.StartNew();
        bool alive = true;

        while (alive && waited.Elapsed < TimeSpan.FromSeconds(5))
        {
            alive = output.SetVoiceGain(voice, 0.5f);
        }

        alive.ShouldBeFalse("64 frames at 44.1 kHz end in under 2 ms of playback");
        output.Playing.ShouldBe(0, "the finished voice was reaped by the call that found it finished");
        output.SetVoicePitch(voice, 1f).ShouldBeFalse();
    }

    /// <remarks>
    /// A finished voice is no channel — the engine's mixer freed it — so a stop passes over it to the first voice still
    /// playing that sound (`S_AlterChannel`, FUN_18002aa20), and reaps it on the way.
    /// </remarks>
    [Test]
    public void Silence_AFinishedVoiceBeforeAPlayingOne_StopsThePlayingOne()
    {
        using AudioOutput output = Open();
        IAudioSink sink = output;
        SoundSample once = Tone(frames: 64);

        int finished = output.Play(once, 1f, 1f, entity: 1, channel: 0);
        int probe = output.Play(Tone(frames: 64), 1f, 1f, entity: 2, channel: 0);
        int playing = output.Play(once with { Loops = true }, 1f, 1f, entity: 1, channel: 0);

        // The probe started after the first voice and is as long, so once it has ended the first has too.
        System.Diagnostics.Stopwatch waited = System.Diagnostics.Stopwatch.StartNew();

        while (output.SetVoiceGain(probe, 1f) && waited.Elapsed < TimeSpan.FromSeconds(5))
        {
            // Synchronising on the probe's own state.
        }

        sink.Silence(1, 0, once).ShouldBe(playing, "the finished voice is passed over");
        finished.ShouldNotBe(playing);
        output.Playing.ShouldBe(0, "the finished voice was reaped on the way, the probe by its gain");
    }

    [Test]
    public void Play_ASampleWithNoFrames_IsIgnoredRatherThanQueued()
    {
        // The guard at the top of `Play`. An empty buffer would be uploaded and started, and would
        // then sit in `_playing` until something reclaimed a source that never plays.
        using AudioOutput output = Open();

        output.Play(new SoundSample(44100, 1, Array.Empty<float>()), leftPan: 1f, rightPan: 1f);

        output.Playing.ShouldBe(0, "there is nothing to play");
    }

    [Test]
    public void Play_ASampleWithNoSampleRate_IsIgnoredRatherThanQueued()
    {
        // A rate of zero reaches OpenAL as a buffer frequency of zero, which is undefined rather
        // than silent. The guard is what keeps a malformed WAV from becoming a native problem.
        using AudioOutput output = Open();

        output.Play(new SoundSample(0, 1, new[] { 0.5f, 0.5f }), leftPan: 1f, rightPan: 1f);

        output.Playing.ShouldBe(0);
    }

    [Test]
    public void Play_TwiceOnOneNamedChannel_ReplacesRatherThanLayers()
    {
        // **How the engine cuts a voice line off with the next one.** A named channel holds one
        // sound per entity; starting a second on the same pair stops the first.
        using AudioOutput output = Open();

        output.Play(Tone(), leftPan: 1f, rightPan: 1f, entity: 7, channel: 3);
        output.Play(Tone(), leftPan: 1f, rightPan: 1f, entity: 7, channel: 3);

        output.Playing.ShouldBe(1, "the second sound replaced the first on that channel");
    }

    /// <remarks>
    /// **`CHAN_STATIC` is the exception, read out of `engine.dll`** (B415): `S_StartStaticSound` takes the first free
    /// slot from index 64 (`MAX_DYNAMIC_CHANNELS`) or appends one, and never compares the source or the channel — where
    /// `SND_PickDynamicChannel` returns the busy (source, channel) slot for reuse. So two static sounds from one entity
    /// both play. The Original's explosion is `CHAN_STATIC` from the world, and two blasts close together are the case.
    /// </remarks>
    [Test]
    public void Play_TwiceOnTheStaticChannel_LayersRatherThanReplaces()
    {
        using AudioOutput output = Open();

        output.Play(Tone(), leftPan: 1f, rightPan: 1f, entity: 0, channel: 6);
        output.Play(Tone(), leftPan: 1f, rightPan: 1f, entity: 0, channel: 6);

        output.Playing.ShouldBe(2, "a static sound takes a slot of its own");
    }

    /// <remarks>
    /// `S_StartStaticSound` (engine.dll FUN_18002f4c0) takes a channel from 0x40 to `MAX_CHANNELS` 0x80 and, with none
    /// free, says "total_channels == MAX_CHANNELS" and starts nothing (B550). A named channel is not one of them.
    /// </remarks>
    [Test]
    public void Play_OnTheStaticChannelWithAllSixtyFourTaken_StartsNothingAndAStopFreesOne()
    {
        using AudioOutput output = Open();
        SoundSample loop = Tone() with { Loops = true, Key = "loop" };

        for (int slot = 0; slot < AudioOutput.StaticChannels; slot++)
        {
            output.Play(loop, leftPan: 1f, rightPan: 1f, entity: slot, channel: 6).ShouldNotBe(0, "the control: a free slot");
        }

        output.Play(loop, leftPan: 1f, rightPan: 1f, entity: 99, channel: 6).ShouldBe(0, "the sixty-fifth has no channel");
        output.Play(loop, leftPan: 1f, rightPan: 1f, entity: 99, channel: 4).ShouldNotBe(0, "a dynamic sound is not a static slot");

        ((IAudioSink)output).Silence(0, 6, loop).ShouldNotBe(0);
        output.Play(loop, leftPan: 1f, rightPan: 1f, entity: 99, channel: 6).ShouldNotBe(0, "the stop freed a slot");
    }

    [Test]
    public void Play_TwiceOnDifferentEntities_LayersRatherThanReplaces()
    {
        // **The control for the case above**, and it is what separates "replaced" from "only ever
        // holds one voice". Same channel number, different entities: both must sound.
        using AudioOutput output = Open();

        output.Play(Tone(), leftPan: 1f, rightPan: 1f, entity: 7, channel: 3);
        output.Play(Tone(), leftPan: 1f, rightPan: 1f, entity: 8, channel: 3);

        output.Playing.ShouldBe(2, "a channel is held per entity, not globally");
    }

    [Test]
    public void Stop_AVoiceThatIsPlaying_RemovesIt()
    {
        using AudioOutput output = Open();

        output.Play(Tone(), leftPan: 1f, rightPan: 1f, entity: 4, channel: 2);
        output.Playing.ShouldBe(1);

        output.Stop(entity: 4, channel: 2);

        output.Playing.ShouldBe(0);
    }

    [Test]
    public void Stop_AChannelNothingIsUsing_LeavesTheOthersAlone()
    {
        // **The bystander**, without which "stopped the right one" and "stopped everything" are the
        // same observation.
        using AudioOutput output = Open();

        output.Play(Tone(), leftPan: 1f, rightPan: 1f, entity: 4, channel: 2);

        output.Stop(entity: 4, channel: 9);

        output.Playing.ShouldBe(1, "a channel nobody is using must not take a voice with it");
    }

    [Test]
    public void SetGain_AVoiceThatIsPlaying_IsAcceptedAndAnAbsentOneIsNot()
    {
        // The return value is the whole observable: it says whether the voice was found. A mutant
        // returning the wrong one makes the distance curve silently stop following the listener.
        using AudioOutput output = Open();

        output.Play(Tone(), leftPan: 1f, rightPan: 1f, entity: 5, channel: 1);

        output.SetGain(entity: 5, channel: 1, gain: 0.25f)
            .ShouldBeTrue("the voice is playing, so its gain can be set");

        output.SetGain(entity: 6, channel: 1, gain: 0.25f)
            .ShouldBeFalse("nothing is playing for that entity");
    }

    [Test]
    public void StopAll_WithSeveralVoices_LeavesNoneAndTheOutputStillUsable()
    {
        using AudioOutput output = Open();

        output.Play(Tone(), leftPan: 1f, rightPan: 1f, entity: 1, channel: 1);
        output.Play(Tone(), leftPan: 1f, rightPan: 1f, entity: 2, channel: 1);
        output.Playing.ShouldBe(2);

        output.StopAll();

        output.Playing.ShouldBe(0);

        // Still usable afterwards: `StopAll` releases sources, and releasing them wrongly would
        // show up as the next play failing rather than as an error at the time.
        output.Play(Tone(), leftPan: 1f, rightPan: 1f, entity: 3, channel: 1);
        output.Playing.ShouldBe(1);
    }

    [Test]
    public void Reclaim_WithEverythingStillPlaying_FreesNothing()
    {
        // **`Reclaim` walks backwards and removes what has finished.** A quarter-second tone has
        // not, so nothing may be taken — which is the half a mutant flipping the loop bound or the
        // decrement would break, and which no test reached at all before this.
        using AudioOutput output = Open();

        output.Play(Tone(), leftPan: 1f, rightPan: 1f, entity: 1, channel: 1);
        output.Play(Tone(), leftPan: 1f, rightPan: 1f, entity: 2, channel: 1);

        output.Reclaim().ShouldBe(0, "both are still playing");
        output.Playing.ShouldBe(2, "and both are still held");
    }

    /// <summary>The mix itself, rendered into memory by `ALC_SOFT_loopback`: the instrument for `volume`.</summary>
    private static AudioOutput OpenLoopback()
    {
        if (AudioOutput.TryCreateLoopback(44100, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance) is not { } output)
        {
            Assert.Ignore("this OpenAL has no loopback device");
            throw new InvalidOperationException("unreachable: Assert.Ignore throws");
        }

        return output;
    }

    /// <summary>Mean absolute level of a rendered block, past the first 1024 frames a gain change is smoothed over.</summary>
    private static double Level(float[] mix) => mix.Skip(2048).Average(static value => Math.Abs(value));

    [Test]
    public void MasterGain_OnASoundAlreadyPlaying_ScalesTheRenderedMixAndZeroIsSilent()
    {
        using AudioOutput output = OpenLoopback();

        output.Play(Tone(frames: 44100 * 4), leftPan: 1f, rightPan: 1f, entity: 1, channel: 1);

        output.MasterGain = 1f;
        double full = Level(output.Render(8192));

        // The control: at unity there is sound, so a silent half below is the gain and not a dead instrument.
        full.ShouldBeGreaterThan(0.05);

        output.MasterGain = 0.5f;
        output.MasterGain.ShouldBe(0.5f, "read back from the device");
        (Level(output.Render(8192)) / full).ShouldBe(0.5, 0.02, "volume scales what is already playing");

        output.MasterGain = 0f;
        _ = output.Render(2048);
        output.Render(8192).Max(static value => Math.Abs(value)).ShouldBe(0f, "volume 0 is silence");
    }

    [Test]
    public void TryCreateLoopback_OpenedAndDisposed_LeavesTheContextItFoundCurrent()
    {
        // Two loopbacks, because both are OpenAL Soft by name: a speaker from TryCreate is the system router on
        // Windows, a separate library whose context a loopback could never take, so it would make no control.
        using AudioOutput first = OpenLoopback();
        first.MasterGain = 0.5f;

        using (AudioOutput second = OpenLoopback())
        {
            second.MasterGain = 0.25f;
            first.MasterGain.ShouldBe(0.25f, "the control: while the second is open, the shared context IS the second's");
        }

        first.MasterGain.ShouldBe(0.5f, "a disposed loopback puts back the context it found");
    }

    [Test]
    public void TryCreateLoopback_DisposedOutOfOrder_LeavesTheNewerContextCurrent()
    {
        AudioOutput first = OpenLoopback();
        using AudioOutput second = OpenLoopback();
        second.MasterGain = 0.25f;

        // The first goes while the second holds the thread: it must not put its own predecessor back over it.
        first.Dispose();

        second.MasterGain.ShouldBe(0.25f, "the thread's context was the second's, not the first's, so it stays");
    }

    [Test]
    public void Play_AfterDispose_ThrowsRatherThanUsingAFreedDevice()
    {
        // Every public method opens with `ObjectDisposedException.ThrowIf`. Calling into a closed
        // device is a native crash rather than an exception, so this guard is the difference
        // between a stack trace and losing the process.
        AudioOutput output = Open();

        output.Dispose();

        Should.Throw<ObjectDisposedException>(() => output.Play(Tone(), 1f, 1f));
        Should.Throw<ObjectDisposedException>(() => output.Reclaim());
        Should.Throw<ObjectDisposedException>(() => output.StopAll());
    }

    /// <summary>The ducker on, as a config sets `snd_ducking_off 0` over TF2's other defaults (B719).</summary>
    private static readonly SpeechDucking DuckingOn = SpeechDucking.Engine with { Off = false };

    /// <summary>A tone the engine counts as speech: a voice-chat source, or a wave whose sentence says `voice_duck 1`.</summary>
    private static SoundSample Speech() => Tone(frames: 44100 * 4) with { IsVoiceSource = true };

    [Test]
    public void Update_ASpeechVoiceAboveThreshold_ScalesEveryOtherVoiceByTheChannelGainAndNotItself()
    {
        // One 0.1 s step from unity: the duck is 0.91, scale (int)232.96 = 232, so a channel's gain is 232/256 = 0.90625
        // (SpeechDuckerConformanceTests). The speech is (int)(1 × 0.5 × 255) = 127 (B724), over the threshold's 38; the
        // other voice (int)(0.8 × 255) = 204, which is 0.8 of full exactly.
        using AudioOutput output = OpenLoopback();
        int speech = output.Play(Speech(), leftPan: 1f, rightPan: 1f, gain: 0.5f, entity: 1, channel: 2);
        int other = output.Play(Tone(frames: 44100 * 4), leftPan: 1f, rightPan: 1f, gain: 0.8f, entity: 2, channel: 1);

        output.Update(DuckingOn, 0.1f);

        output.GainOf(speech).ShouldBe(127f / 255f, "a voice source is never ducked (FUN_180041fb0 skips it)");
        output.GainOf(other).ShouldBe(0.8f * 0.90625f, 1e-6f);

        // A voice started while ducked starts ducked.
        int later = output.Play(Tone(frames: 44100 * 4), leftPan: 1f, rightPan: 1f, gain: 1f, entity: 3, channel: 1);
        output.GainOf(later).ShouldBe(0.90625f, 1e-6f);

        // A gain set while ducked is ducked too, from the next frame's spatialisation: (int)(0.79 × 255) = 201, 3 below
        // 204 so it snaps, under the second frame's duck 0.82, scale 209.
        output.SetVoiceGain(other, 0.79f).ShouldBeTrue();
        output.GainOf(other).ShouldBe(0.8f * 0.90625f, 1e-6f, "set, not yet spatialised");

        output.Update(DuckingOn, 0.1f);
        output.GainOf(other).ShouldBe(201f / 255f * (209f / 256f), 1e-6f);
    }

    [TestCase(0.3f, 0.8f)]
    [TestCase(0.32f, 0.8f * 0.90625f)]
    public void Update_ASpeechVoiceAtTheThreshold_DucksOnlyAbove38OnItsLouderEar(float louderPan, float expected)
    {
        // The louder ear times the gain times 255, truncated: 0.3 × 0.5 × 255 = 38.25 is 38, not above 38; 0.32 is 40.
        using AudioOutput output = OpenLoopback();
        _ = output.Play(Speech(), leftPan: 0.1f, rightPan: louderPan, gain: 0.5f, entity: 1, channel: 2);
        int other = output.Play(Tone(frames: 44100 * 4), leftPan: 1f, rightPan: 1f, gain: 0.8f, entity: 2, channel: 1);

        output.Update(DuckingOn, 0.1f);

        output.GainOf(other).ShouldBe(expected, 1e-6f);
    }

    [Test]
    public void Update_TurnedOffAfterDucking_PutsEveryGainBackAsSet()
    {
        using AudioOutput output = OpenLoopback();
        _ = output.Play(Speech(), leftPan: 1f, rightPan: 1f, gain: 1f, entity: 1, channel: 2);
        int other = output.Play(Tone(frames: 44100 * 4), leftPan: 1f, rightPan: 1f, gain: 0.8f, entity: 2, channel: 1);

        output.Update(DuckingOn, 0.1f);
        output.GainOf(other).ShouldBe(0.8f * 0.90625f, 1e-6f, "the control: ducked first");

        output.Update(SpeechDucking.Engine, 0.1f);

        output.GainOf(other).ShouldBe(0.8f, "snd_ducking_off 1 is unity at once, and the gain as set comes back");
    }

    [Test]
    public void Update_ANegativeDuckToVolume_SilencesTheOtherVoicesAsThePaintsClampDoes()
    {
        // snd_ducktovolume -0.5 is unbounded: 0.3 a frame from 1 is 0.7, 0.4, 0.1, -0.2, scale (int)-51.2 = -51. The paint
        // multiplies the channel's volumes by -51/256 and then clamps each to 0..255 (FUN_180041fb0, 0x180042347-), so 0.
        using AudioOutput output = OpenLoopback();
        int speech = output.Play(Speech(), leftPan: 1f, rightPan: 1f, gain: 1f, entity: 1, channel: 2);
        int other = output.Play(Tone(frames: 44100 * 4), leftPan: 1f, rightPan: 1f, gain: 0.8f, entity: 2, channel: 1);

        for (int frame = 0; frame < 4; frame++)
        {
            output.Update(DuckingOn with { ToVolume = -0.5f }, 0.1f);
        }

        output.GainOf(other).ShouldBe(0f);
        output.GainOf(speech).ShouldBe(1f, "speech is never scaled");
    }

    /// <summary>
    /// Each speaker's volume is its own integer, capped at 255 where it is spatialised, before the duck (B724): gain ×
    /// share × 255 per ear (engine.dll FUN_18001e0b0 at `0x18001ee40`, then FUN_1800214a0 per speaker).
    /// </summary>
    [TestCase(1.5f, 95f / 255f, 191f / 255f)]
    [TestCase(4f, 1f, 1f)]
    public void Play_AGainPastFull_IsEachEarsOwnVolumeCappedAt255(float gain, float left, float right)
    {
        // Pans 0.25 and 0.5: 1.5 is 95.625 and 191.25, kept as 95 and 191; 4 would be 255 and 510, each capped at 255.
        using AudioOutput output = OpenLoopback();

        int voice = output.Play(Tone(frames: 44100 * 4), leftPan: 0.25f, rightPan: 0.5f, gain: gain, entity: 1, channel: 1);

        output.GainsOf(voice).ShouldBe((left, right));
    }

    [Test]
    public void Play_AGainPastUnity_RendersEachEarUpToFull()
    {
        // Pans 0.25 and 0.5: at gain 1 the ears are 63 and 127 of 255, at 4 both 255, so the mix's mean level rises by
        // 510 / 190: the device plays what it is told, not clamped by the source's AL_MAX_GAIN.
        using AudioOutput output = OpenLoopback();

        int voice = output.Play(Tone(frames: 44100 * 4), leftPan: 0.25f, rightPan: 0.5f, gain: 1f, entity: 1, channel: 1);
        double unity = Level(output.Render(8192));
        unity.ShouldBeGreaterThan(0.005, "the control: there is sound at unity");

        output.SetVoiceGain(voice, 4f).ShouldBeTrue();
        output.GainsOf(voice).ShouldBe((1f, 1f));

        (Level(output.Render(8192)) / unity).ShouldBe(510.0 / 190.0, 0.03);
    }

    [Test]
    public void Update_AVoicePastFull_DucksTheCappedVolume()
    {
        // The cap is at spatialisation and the duck at the paint, after it: 255 × 232/256 in each ear, not 4 × 0.90625.
        using AudioOutput output = OpenLoopback();
        _ = output.Play(Speech(), leftPan: 1f, rightPan: 1f, gain: 1f, entity: 1, channel: 2);
        int other = output.Play(Tone(frames: 44100 * 4), leftPan: 0.25f, rightPan: 0.5f, gain: 4f, entity: 2, channel: 1);

        output.Update(DuckingOn, 0.1f);

        output.GainsOf(other).ShouldBe((0.90625f, 0.90625f));
    }

    /// <summary>One ear of a rendered block: every other float, from the left (0) or the right (1).</summary>
    private static double Ear(float[] mix, int ear) => Level([.. mix.Where((_, at) => at % 2 == ear)]);

    /// <remarks>
    /// Each ear is its own speaker volume, so a sound panned wholly to one ear is silent in the other, and a re-pan moves
    /// it: the left ramps down and the right up, 20 a frame each at 70 ms frames (FUN_1800214a0, FUN_180021670).
    /// </remarks>
    [Test]
    public void SetVoicePan_AfterTheFirstFrame_RampsEachEarOnItsOwn()
    {
        using AudioOutput output = OpenLoopback();
        int voice = output.Play(Tone(frames: 44100 * 4), leftPan: 1f, rightPan: 0f, gain: 1f, entity: 1, channel: 1);

        float[] left = output.Render(8192);
        Ear(left, 0).ShouldBeGreaterThan(0.05, "the control: the left ear sounds");
        // Measured 2.8e-9, about -145 dB under the loud ear: OpenAL Soft's own residue, not the sound.
        Ear(left, 1).ShouldBeLessThan(1e-6, "and the right is silent");

        output.Update(SpeechDucking.Engine, 0.07f);
        output.SetVoicePan(voice, 0f, 1f).ShouldBeTrue();
        output.GainsOf(voice).ShouldBe((1f, 0f), "set, not yet spatialised");

        output.Update(SpeechDucking.Engine, 0.07f);
        output.GainsOf(voice).ShouldBe((235f / 255f, 20f / 255f));

        output.Update(SpeechDucking.Engine, 0.07f);
        output.GainsOf(voice).ShouldBe((215f / 255f, 40f / 255f));
    }

    [Test]
    public void SetVoicePan_BeforeTheFirstFrame_MovesTheSoundToTheOtherEarAtOnce()
    {
        using AudioOutput output = OpenLoopback();
        int voice = output.Play(Tone(frames: 44100 * 4), leftPan: 1f, rightPan: 0f, gain: 1f, entity: 1, channel: 1);

        output.SetVoicePan(voice, 0f, 1f).ShouldBeTrue();
        _ = output.Render(4096);
        float[] right = output.Render(8192);

        Ear(right, 1).ShouldBeGreaterThan(0.05, "the right ear sounds");
        Ear(right, 0).ShouldBeLessThan(1e-6, "and the left is silent");
    }

    [Test]
    public void Update_TheEngineDefault_LeavesEveryGainAsSet()
    {
        using AudioOutput output = OpenLoopback();
        _ = output.Play(Speech(), leftPan: 1f, rightPan: 1f, gain: 1f, entity: 1, channel: 2);
        int other = output.Play(Tone(frames: 44100 * 4), leftPan: 1f, rightPan: 1f, gain: 0.8f, entity: 2, channel: 1);

        output.Update(SpeechDucking.Engine, 0.1f);

        output.GainOf(other).ShouldBe(0.8f, "snd_ducking_off 1 is TF2's default");
    }

    /// <summary>
    /// A channel's volume is an integer of 0..255 on its louder ear, set at once when the channel starts and ramped at
    /// each frame's spatialisation after (B724, engine.dll FUN_1800214a0 and FUN_180021670; SpeakerVolumeConformanceTests).
    /// </summary>
    [Test]
    public void Play_AGain_IsHeardAtTheTruncatedSpeakerVolume()
    {
        // (int)(0.25 × 255) = (int)63.75 = 63, at once: a new channel's first pass does not ramp.
        using AudioOutput output = OpenLoopback();

        int voice = output.Play(Tone(frames: 44100 * 4), leftPan: 1f, rightPan: 1f, gain: 0.25f, entity: 1, channel: 1);

        output.GainOf(voice).ShouldBe(63f / 255f);
    }

    [Test]
    public void SetVoiceGain_AfterTheFirstFrame_RampsTheDeviceGain20AFrame()
    {
        // 63 to 255 at 70 ms frames: an increment of the whole difference, clamped to 20.
        using AudioOutput output = OpenLoopback();
        int voice = output.Play(Tone(frames: 44100 * 4), leftPan: 1f, rightPan: 1f, gain: 0.25f, entity: 1, channel: 1);
        output.Update(SpeechDucking.Engine, 0.07f);

        output.SetVoiceGain(voice, 1f).ShouldBeTrue();
        output.GainOf(voice).ShouldBe(63f / 255f, "set, not yet spatialised");

        output.Update(SpeechDucking.Engine, 0.07f);
        output.GainOf(voice).ShouldBe(83f / 255f);

        output.Update(SpeechDucking.Engine, 0.07f);
        output.GainOf(voice).ShouldBe(103f / 255f);
    }

    [Test]
    public void SetGain_BeforeTheFirstFrame_SetsTheVolumeAtOnce()
    {
        // The first-pass flag holds until the channel's first frame of spatialisation, which sets whatever the target is.
        using AudioOutput output = OpenLoopback();
        int voice = output.Play(Tone(frames: 44100 * 4), leftPan: 1f, rightPan: 1f, gain: 0.25f, entity: 1, channel: 1);

        output.SetGain(1, 1, 1f).ShouldBeTrue();

        output.GainOf(voice).ShouldBe(1f);
    }

    [Test]
    public void Update_ASpeechVoiceTurnedDown_StillDucksWhileItsVolumeRampsDown()
    {
        // S_Update measures the speech after spatialising it (0x180030cf2, then 0x180030d0e reads the current volumes):
        // set to 0, the speech is at 255 - 20 = 235 that frame, over 38, so the other voice ducks: 204/255 × 232/256.
        using AudioOutput output = OpenLoopback();
        int speech = output.Play(Speech(), leftPan: 1f, rightPan: 1f, gain: 1f, entity: 1, channel: 2);
        int other = output.Play(Tone(frames: 44100 * 4), leftPan: 1f, rightPan: 1f, gain: 0.8f, entity: 2, channel: 1);
        output.Update(SpeechDucking.Engine, 0.07f);

        output.SetVoiceGain(speech, 0f).ShouldBeTrue();
        output.Update(DuckingOn, 0.1f);

        output.GainOf(speech).ShouldBe(235f / 255f);
        output.GainOf(other).ShouldBe(0.8f * 0.90625f, 1e-6f);
    }

    [Test]
    public void SetVoiceFade_APlayingVoice_ScalesItAtOnceOutsideTheRamp()
    {
        // The voice mixer's fade is in the source's samples (Voice_GetOutputData, FUN_1800590b0 in build 11191711), not the
        // channel's volume: no ramp.
        using AudioOutput output = OpenLoopback();
        int voice = output.Play(Speech(), leftPan: 1f, rightPan: 1f, gain: 1f, entity: 1, channel: 2);
        output.Update(SpeechDucking.Engine, 0.07f);

        output.SetVoiceFade(voice, 0.5f).ShouldBeTrue();

        output.GainOf(voice).ShouldBe(0.5f);
    }

    [Test]
    public void Play_TheNextSpurtOnAFadedChannel_StartsUnfadedUnderANewHandle()
    {
        // Each spurt is its own Play, so its own level: the fade at the end of the last does not carry, and the old handle
        // fades nothing once its channel has been replaced.
        using AudioOutput output = OpenLoopback();
        int first = output.Play(Speech(), leftPan: 1f, rightPan: 1f, gain: 1f, entity: 1, channel: 9);
        output.Update(SpeechDucking.Engine, 0.07f);
        output.SetVoiceFade(first, 0.25f).ShouldBeTrue();
        output.GainOf(first).ShouldBe(0.25f, "the control: faded");

        int next = output.Play(Speech(), leftPan: 1f, rightPan: 1f, gain: 1f, entity: 1, channel: 9);

        next.ShouldNotBe(first);
        output.GainOf(next).ShouldBe(1f);
        output.SetVoiceFade(first, 0.5f).ShouldBeFalse();
        output.GainOf(next).ShouldBe(1f);
    }

    [Test]
    public void Dispose_Twice_IsHarmless()
    {
        // `_disposed` guards the teardown, and disposing twice is ordinary in a `using` that also
        // disposes by hand. Releasing the same native context twice is not ordinary.
        AudioOutput output = Open();

        output.Dispose();

        Should.NotThrow(output.Dispose);
    }
}
