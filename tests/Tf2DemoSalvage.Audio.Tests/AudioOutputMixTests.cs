using System;

using Tf2DemoSalvage.Audio;

namespace Tf2DemoSalvage.Audio.Tests;

/// <summary>
/// Turning a decoded sound into the stereo the sink plays.
/// </summary>
/// <remarks>
/// **The only part of <see cref="AudioOutput"/> with a decision in it.** Opening a device, queuing a
/// buffer and reclaiming a source are calls into OpenAL that either work or do not; this is the
/// arithmetic, and it is where a wrong answer would be audible rather than fatal.
///
/// It is also device-free on purpose. The measurement boxes and CI have no sound card, so a test
/// that needed one would skip everywhere it matters — and a skip is not a pass
/// (`docs/memory/a-skip-is-not-a-pass-or-a-failure.md`).
/// </remarks>
public sealed class AudioOutputMixTests
{
    private static SoundSample Mono(params float[] samples) => new(44100, 1, samples);

    private static SoundSample Stereo(params float[] samples) => new(44100, 2, samples);

    [Test]
    public void ToStereo16_AMonoSample_FeedsBothEarsThroughTheirOwnGains()
    {
        short[] mixed = AudioOutput.ToStereo16(Mono(1f, 1f), leftGain: 1f, rightGain: 0.5f);

        // **Exact values, because the conversion is exact.** Full scale is short.MaxValue and half
        // gain is half of it; predicting "greater than zero" would pass against a mixer that
        // ignored the gains entirely, which is the failure this exists to catch.
        mixed.Length.ShouldBe(4, "two mono frames become two stereo frames");
        mixed[0].ShouldBe(short.MaxValue);
        mixed[1].ShouldBe((short)(short.MaxValue / 2));
        mixed[2].ShouldBe(short.MaxValue);
        mixed[3].ShouldBe((short)(short.MaxValue / 2));
    }

    [Test]
    public void ToStereo16_AStereoSample_KeepsItsOwnChannels()
    {
        // Left full, right silent — an image the source already carries.
        short[] mixed = AudioOutput.ToStereo16(Stereo(1f, 0f), leftGain: 1f, rightGain: 1f);

        mixed.Length.ShouldBe(2);
        mixed[0].ShouldBe(short.MaxValue);

        // **The control that separates "kept its channels" from "spread the first one".** A mixer
        // treating stereo as mono would put full scale in both, and every other assertion here
        // would still pass.
        mixed[1].ShouldBe((short)0, "a stereo source already carries its image and must not be re-panned");
    }

    [Test]
    public void ToStereo16_AGainAboveOne_SaturatesRatherThanWrapping()
    {
        // **Legitimate input, not a guard against nonsense.** Valve's volume and the distance curve
        // can combine past unity on a close, loud sound, and an unclamped float through the short
        // conversion turns that into a burst of noise rather than a loud sound.
        short[] mixed = AudioOutput.ToStereo16(Mono(1f), leftGain: 4f, rightGain: 4f);

        mixed[0].ShouldBe(short.MaxValue, "an over-unity gain should saturate");
        mixed[1].ShouldBe(short.MaxValue);
    }

    [Test]
    public void ToStereo16_ANegativeSample_SaturatesAtTheNegativeLimit()
    {
        // The other side of the clamp, which a symmetric-looking implementation can still get
        // wrong: -1 * short.MaxValue is not short.MinValue, and clamping after conversion would
        // wrap here while passing the positive case.
        short[] mixed = AudioOutput.ToStereo16(Mono(-4f), leftGain: 1f, rightGain: 1f);

        mixed[0].ShouldBeLessThan((short)0);
        mixed[0].ShouldBe((short)(-short.MaxValue));
    }

    [Test]
    public void ToStereo16_AnEmptySample_ProducesNothing()
    {
        AudioOutput.ToStereo16(Mono(), leftGain: 1f, rightGain: 1f).ShouldBeEmpty();
    }

    [Test]
    public void Dispose_OneOutputWhileAnotherIsOpen_LeavesTheOtherWorking()
    {
        // **The one test here that needs a real device, and B178 is why.** Everything above is
        // deliberately device-free so it runs on CI and the measurement boxes, which have no sound
        // card. This defect lives entirely in the device path: the viewer test suite builds and
        // disposes a `MainForm` per test, each opening its own `AudioOutput`, and the host crashed
        // roughly one run in three — twice inside `AudioOutput.Dispose` and once in D3D device
        // creation afterwards, which is the shape of native teardown damaging something else.
        //
        // Two overlapping lifetimes is the smallest arrangement that reproduces it, and it asserts
        // rather than merely surviving: a process-wide teardown taken by the FIRST output is
        // observable as the SECOND one no longer working.
        if (AudioOutput.TryCreate() is not { } first)
        {
            Assert.Ignore("no audio device on this machine, so the device path cannot be exercised");
            return;
        }

        AudioOutput? second = AudioOutput.TryCreate();

        second.ShouldNotBeNull("a second output must open while the first is still alive");

        first.Dispose();

        // **The survivor has to still function.** `Reclaim` and `StopAll` both call into AL, so if
        // disposing the first unloaded the shared library or cleared the process-wide current
        // context, this is where it shows — as a failure if we are lucky, and as a host crash if we
        // are not.
        second.Reclaim();
        second.StopAll();
        second.Dispose();

        // And a third must still open afterwards: if the library were unloaded, `TryCreate` would
        // catch `DllNotFoundException` and hand back null, which is silence with no explanation.
        AudioOutput? third = AudioOutput.TryCreate();

        third.ShouldNotBeNull("the device must still open after every earlier output was disposed");
        third.Dispose();
    }
}
