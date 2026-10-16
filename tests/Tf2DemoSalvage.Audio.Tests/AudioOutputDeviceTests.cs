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
        if (AudioOutput.TryCreate() is not { } output)
        {
            Assert.Ignore("no OpenAL device could be opened, not even the null one");
            throw new InvalidOperationException("unreachable: Assert.Ignore throws");
        }

        return output;
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
        if (AudioOutput.TryCreateLoopback(44100) is not { } output)
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
