using System;

namespace Tf2DemoSalvage.Audio;

/// <summary>
/// One channel's speaker volume on the engine's 0..255 scale: the integer it is aimed at, and the float it ramps toward
/// it a frame at a time (B724).
/// </summary>
/// <remarks>
/// **Read in disassembly**, x64 engine.dll build 11191711; the addresses are in <c>SpeakerVolumeConformanceTests</c>.
/// <c>SND_Spatialize</c> sets each speaker's target with <c>FUN_1800214a0</c> and steps it with <c>FUN_180021670</c>, once
/// per channel per <c>S_Update</c>. A new channel is flagged first-pass (bit 7 of <c>+0x13c</c>, set by its activation,
/// <c>FUN_18002a3e0</c>), which sets the volume at once; the flag is cleared by that frame's spatialisation, and every later
/// change, <c>SND_CHANGE_VOL</c> included, ramps. The paint reads the float.
/// </remarks>
public sealed class SpeakerVolume
{
    /// <summary>The ramp's rate per second of the difference: the double at <c>0x1803613e8</c>, 1 / 0.07.</summary>
    private const double Rate = 14.285714285714286;

    /// <summary>The largest step a frame, either way.</summary>
    private const float MaxStep = 20f;

    /// <summary>A difference below this is set at once, not ramped (the double at <c>0x1803613d8</c>).</summary>
    private const double SnapBelow = 5.0;

    /// <summary>The volume the paint reads, 0..255.</summary>
    public float Current { get; private set; }

    /// <summary>The integer the volume is moving to.</summary>
    public int Target { get; private set; }

    /// <summary>Whether the channel has not yet had a frame's spatialisation: bit 7 of <c>+0x13c</c>.</summary>
    public bool FirstPass { get; private set; } = true;

    /// <summary>A spatialised speaker volume as the channel's integer target (<c>FUN_1800214a0</c>'s head).</summary>
    /// <param name="volume">Master volume × gain × the speaker's share, on the 0..255 scale.</param>
    /// <returns>Below 2, <c>2v − 2</c>; clamped to 0..255; truncated.</returns>
    public static int Quantize(float volume)
    {
        if (volume < 2f)
        {
            volume += volume - 2f;
        }

        return (int)Minss(Maxss(volume, 0f), 255f);
    }

    /// <summary>
    /// <c>maxss a, b</c>: <paramref name="a"/> when it is greater, else <paramref name="b"/> — so <paramref name="b"/> for a
    /// NaN, where <see cref="Math.Max(float, float)"/> answers NaN. The engine's clamps are these, which is what keeps a NaN
    /// frame time or volume from reaching the device.
    /// </summary>
    private static float Maxss(float a, float b) => a > b ? a : b;

    /// <summary><c>minss a, b</c>: <paramref name="a"/> when it is less, else <paramref name="b"/>.</summary>
    private static float Minss(float a, float b) => a < b ? a : b;

    /// <summary>Aims the volume at a spatialised value; on the first pass the volume is set there at once.</summary>
    /// <param name="volume">The speaker volume, on the 0..255 scale.</param>
    public void Aim(float volume)
    {
        Target = Quantize(volume);

        if (FirstPass)
        {
            Current = Target;
        }
    }

    /// <summary>One frame's spatialisation: the step toward the target, which ends the first pass.</summary>
    /// <param name="frameTime">The frame's length in seconds.</param>
    public void Step(float frameTime)
    {
        float difference = Target - Current;

        if (FirstPass || Math.Abs((double)difference) < SnapBelow)
        {
            Current = Target;
            FirstPass = false;
            return;
        }

        // No guard on the frame time, as the engine has none: a negative one steps away from the target, which the landing
        // test below turns into a landing, and a NaN one is −20 by the clamps' operand order.
        float step = Minss(Maxss((float)(frameTime * (double)difference * Rate), -MaxStep), MaxStep);

        // FUN_180021670 skips a channel whose increment is 0, as a paused frame's is.
        if (step is 0f)
        {
            return;
        }

        Current += step;

        if (step > 0f ? Target <= Current : Current <= Target)
        {
            Current = Target;
        }
    }
}
