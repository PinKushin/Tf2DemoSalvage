using System;

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
/// `docs/memory/a-skip-is-not-a-pass-or-a-failure.md`.
///
/// **They still degrade honestly.** If a machine somehow cannot open even the null device, every
/// test here ignores with a reason rather than passing vacuously.
///
/// **No game audio is involved.** Every sample is synthesised, so nothing here depends on an
/// install — which is the other half of why the boxes could not run the existing audio tests.
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

    /// <remarks>
    /// **Static because the assembly runs `InstancePerTestCase`** — NUnit rejects an instance
    /// `[OneTimeSetUp]` in that mode outright, and the failure arrives as every test in the fixture
    /// erroring rather than as anything about setup.
    /// </remarks>
    [OneTimeSetUp]
    public static void UseTheNullDevice()
    {
        // **Set before the first OpenAL call, because the driver list is read once.** OpenAL Soft
        // reads `ALSOFT_DRIVERS` when the library initialises, so a later change is ignored — which
        // would leave these tests quietly driving the developer's real sound card and playing a
        // tone during every run.
        Environment.SetEnvironmentVariable("ALSOFT_DRIVERS", "null");
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
