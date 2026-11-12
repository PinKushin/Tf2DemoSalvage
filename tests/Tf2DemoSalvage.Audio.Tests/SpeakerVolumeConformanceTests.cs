using Tf2DemoSalvage.Audio;

namespace Tf2DemoSalvage.Audio.Tests;

/// <summary>
/// A channel's speaker volume as engine.dll sets and ramps it each time the channel is spatialised (B724).
/// </summary>
/// <remarks>
/// Read in disassembly, x64 <c>engine.dll</c> build 11191711. <c>SND_Spatialize</c> (<c>FUN_180029440</c>) calls, per
/// channel per <c>S_Update</c> (<c>0x180030cf2</c>; every channel every frame with <c>snd_spatialize_roundrobin</c> "0"):
/// <code>
/// FUN_1800214a0  per speaker: v &lt; 2.0 (0x1803613c8) → v + (v − 2); max 0, min 255; cvttss2si; clamp 0..255 → target
///                (+0x48). Bit 7 of +0x13c set, or |target − current| &lt; 5.0 (double, 0x1803613d8): current = target,
///                increment (+0x78) = 0. Else increment = (float)(frametime × (target − current) × 14.285714285714286
///                (double, 0x1803613e8)), max −20, min 20. frametime is the sound services' +0x30 (0x180018f10), the
///                member SetSoundFrametime (0x180018eb0) sets to the REAL frame time unless a movie is recording.
/// FUN_180021670  per speaker with a nonzero increment: current += increment; at or past target, current = target and
///                the increment 0.
/// </code>
/// Bit 7 of <c>+0x13c</c> is set only by the channel's activation (<c>FUN_18002a3e0</c>, from both sound starts) and
/// cleared at the end of a spatialisation in game (<c>0x18002a337</c>), so a new channel's first volume is its target and
/// every later change ramps, a <c>SND_CHANGE_VOL</c> included. The paint reads the float current (<c>FUN_180021270</c>).
/// </remarks>
public sealed class SpeakerVolumeConformanceTests
{
    /// <summary>70 ms, where the ramp's rate is the whole difference a frame (0.07 × 14.2857 = 1), so the clamp decides.</summary>
    private const float SeventyMs = 0.07f;

    /// <summary>7/128 s, where the rate is 0.78125 of the difference exactly.</summary>
    private const float SevenOver128 = 0.0546875f;

    [TestCase(1.5f, 1)]
    [TestCase(1f, 0)]
    [TestCase(1.25f, 0)]
    [TestCase(0.9f, 0)]
    [TestCase(1.99f, 1)]
    [TestCase(2f, 2)]
    [TestCase(2.5f, 2)]
    [TestCase(127.5f, 127)]
    [TestCase(255f, 255)]
    [TestCase(300f, 255)]
    [TestCase(-4f, 0)]
    public void Quantize_ASpeakerVolume_IsTheEnginesInteger(float volume, int expected)
    {
        // Below 2 the volume is 2v − 2, so 1.5 is 1, 1 is 0, and 1.99 is 1.98; truncated, never rounded.
        SpeakerVolume.Quantize(volume).ShouldBe(expected);
    }

    [Test]
    public void Step_ANewChannel_StartsAtItsTarget()
    {
        SpeakerVolume volume = new();

        volume.Aim(200f);
        volume.Current.ShouldBe(200f, "the activation's first pass sets the volume, it does not ramp from 0");

        volume.Step(SeventyMs);
        volume.Current.ShouldBe(200f);
    }

    [Test]
    public void Step_AChangeOf200_ClimbsExactly20AFrameAndLandsOnTheTarget()
    {
        SpeakerVolume volume = Settled(50f);

        volume.Aim(250f);
        volume.Current.ShouldBe(50f, "a change after the first pass waits for the frame's spatialisation");

        float[] expected = [70f, 90f, 110f, 130f, 150f, 170f, 190f, 210f, 230f, 250f, 250f];

        foreach (float current in expected)
        {
            volume.Step(SeventyMs);
            volume.Current.ShouldBe(current);
        }
    }

    [Test]
    public void Step_AChangeDownward_FallsExactly20AFrame()
    {
        SpeakerVolume volume = Settled(250f);

        volume.Aim(190f);

        float[] expected = [230f, 210f, 190f, 190f];

        foreach (float current in expected)
        {
            volume.Step(SeventyMs);
            volume.Current.ShouldBe(current);
        }
    }

    [Test]
    public void Step_ARampFrom0To64_ClampsThenFollowsTheDifferenceThenSnapsUnder5()
    {
        // At 7/128 s the increment is 0.78125 × the difference: 64 → 50 clamped to 20; 44 → 34.375, 20; 24 → 18.75;
        // 5.25 → 4.1015625, not under 5 so still a step; 1.1484375 is under 5 and snaps.
        SpeakerVolume volume = Settled(0f);

        volume.Aim(64f);

        float[] expected = [20f, 40f, 58.75f, 62.8515625f, 64f];

        foreach (float current in expected)
        {
            volume.Step(SevenOver128);
            volume.Current.ShouldBe(current);
        }
    }

    [TestCase(104f, 104f)]
    [TestCase(96f, 96f)]
    [TestCase(105f, 103.90625f)]
    [TestCase(95f, 96.09375f)]
    public void Step_AChangeUnder5_SnapsAndOf5Ramps(float target, float afterOneFrame)
    {
        SpeakerVolume volume = Settled(100f);

        volume.Aim(target);
        volume.Step(SevenOver128);

        volume.Current.ShouldBe(afterOneFrame);
    }

    [Test]
    public void Step_AFrameLongerThan70Ms_LandsOnTheTargetNeverPast()
    {
        // 0.25 × 8 × 14.2857 = 28.6, clamped to 20, which passes 108: the step clamps at the target.
        SpeakerVolume volume = Settled(100f);

        volume.Aim(108f);
        volume.Step(0.25f);

        volume.Current.ShouldBe(108f);
    }

    [Test]
    public void Step_AZeroFrameTime_HoldsTheVolume()
    {
        // A paused frame: the increment is 0, which FUN_180021670 skips.
        SpeakerVolume volume = Settled(0f);

        volume.Aim(200f);
        volume.Step(0f);

        volume.Current.ShouldBe(0f);
    }

    [Test]
    public void Aim_AVolumeUnder2_TargetsTheMappedInteger()
    {
        SpeakerVolume volume = Settled(0f);

        volume.Aim(1.5f);

        volume.Target.ShouldBe(1);
    }

    /// <summary>A channel past its first pass, at <paramref name="at"/>.</summary>
    private static SpeakerVolume Settled(float at)
    {
        SpeakerVolume volume = new();
        volume.Aim(at);
        volume.Step(SeventyMs);
        volume.Current.ShouldBe(at, "the control: the first pass set it");

        return volume;
    }
}
