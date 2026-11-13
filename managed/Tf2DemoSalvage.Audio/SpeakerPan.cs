using System;

using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Audio;

/// <summary>
/// How engine.dll splits a channel across a two-channel device opened as headphones, TF2's default (B724).
/// </summary>
/// <remarks>
/// **Read in disassembly**, x64 engine.dll build 11191711; every address and constant is in
/// <c>SpeakerPanConformanceTests</c>. <c>SND_Spatialize</c> (<c>FUN_180029440</c>) hands the panner
/// (<c>FUN_18001e0b0</c>) the direction to the sound and a mono weight. The panner reads the sound's bearing from the
/// listener's RIGHT vector, so 0 is the right ear and 180 the left, and on a headphone device (<c>+0x1d</c>, set when
/// <c>snd_surround_speakers</c> asks for 0 speakers, as its default "-1" does) gives each ear
/// <c>s + (0.9 − s) × mono</c> with <c>s = 1 − FastPow(d / 180, 1.5)</c> (<c>FUN_18001efb0</c>, <c>FUN_18001f980</c>).
/// The share is then × master volume × gain and clamped to 0..255 by the caller.
///
/// **Not ported yet** (filed under B724): the two-speaker law <c>snd_surround_speakers 2</c> selects (four virtual
/// speakers at 45/135/225/315 folded to two), the local player's own sounds (placed 12 units ahead, and summed into
/// both ears when fully mono, <c>FUN_180027560</c>), and the model radius that raises the mono weight inside it.
/// </remarks>
public static class SpeakerPan
{
    /// <summary>The weight a fully mono sound gives each ear: the 0.9 at <c>0x18035fd38</c>.</summary>
    private const float MonoShare = 0.9f;

    /// <summary>1 / 180 as the float at <c>0x18035fd14</c>, which an ear's angle is scaled by.</summary>
    private const float PerHalfTurn = 0.0055555557f;

    /// <summary>1 / 45 as the float at <c>0x18035fd28</c>: the mono weight added per degree of elevation past 45.</summary>
    private const float PerElevationDegree = 0.022222223f;

    /// <summary>The panner's exponent: the constant 1.5 at <c>0x18035fd50</c>, not <c>snd_headphone_pan_exponent</c>.</summary>
    private const float Exponent = 1.5f;

    /// <summary>FastPow's log2 correction, the float at <c>0x1803d61f4</c> (≈ 0.346607).</summary>
    private static readonly float LogCorrection = BitConverter.Int32BitsToSingle(0x3eb17679);

    /// <summary>FastPow's 2^x correction, the float at <c>0x1803d61f0</c> (≈ 0.33971).</summary>
    private static readonly float ExpCorrection = BitConverter.Int32BitsToSingle(0x3eadee78);

    /// <summary>The mono weight a sound starts its spatialisation with.</summary>
    /// <param name="soundLevel">The channel's soundlevel; 0 has no distance multiplier.</param>
    /// <param name="name">The wave as played, sound characters included.</param>
    /// <param name="stereo">Whether the wave has two channels.</param>
    /// <returns>1 for a sound with no distance or an omni (<c>'@'</c>) one; otherwise 0.</returns>
    /// <remarks>
    /// <c>SND_Spatialize</c> sets 1 when the distance multiplier is 0 or the spatial character (<c>+0x12a</c>) is
    /// <c>'@'</c>. That character is the leading sound character of highest rank, <c>')'</c> over <c>'@'</c> over
    /// <c>'^'</c> over <c>'&lt;'</c> over <c>'&gt;'</c> (<c>FUN_18002dc10</c> rescans the prefix once for each), and
    /// <c>FUN_18002db70</c> makes a stereo wave <c>'@'</c> when it has none, is not <c>'#'</c> music and has a distance
    /// multiplier. That function also skips the local player's sounds; this does not know the local player yet.
    /// </remarks>
    public static float Mono(int soundLevel, string name, bool stereo)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (soundLevel <= 0)
        {
            return 1f;
        }

        char spatial = '\0';
        bool music = false;

        foreach (char character in name)
        {
            if (!SoundName.IsSoundChar(character))
            {
                break;
            }

            music |= character == '#';

            if (Rank(character) > Rank(spatial))
            {
                spatial = character;
            }
        }

        if (spatial == '\0' && stereo && !music)
        {
            spatial = '@';
        }

        return spatial == '@' ? 1f : 0f;
    }

    /// <summary>The order <c>FUN_18002dc10</c>'s rescans leave the spatial character in: the later scan wins.</summary>
    private static int Rank(char character) => character switch
    {
        '>' => 1,
        '<' => 2,
        '^' => 3,
        '@' => 4,
        ')' => 5,
        _ => 0,
    };

    /// <summary>The distance and each ear's share <c>SND_Spatialize</c> gives one channel.</summary>
    /// <param name="origin">Where the channel is.</param>
    /// <param name="listener">Where the ears are.</param>
    /// <param name="right">The listener's right vector.</param>
    /// <param name="mono">The channel's starting mono weight, from <see cref="Mono"/>.</param>
    /// <param name="local">Whether the channel is the view entity's own.</param>
    /// <param name="radius">Its model's radius (<c>GetModelRadius</c>), 0 for none.</param>
    /// <param name="headphones">Whether the device is headphones (<see cref="IsHeadphones"/>), the default.</param>
    /// <returns>The distance its gain is taken at, and each ear's share.</returns>
    /// <remarks>
    /// **Inside its model's radius a sound turns mono** (<c>SND_Spatialize</c>, engine.dll FUN_180029440): the weight is
    /// <c>1 − max(d − r/2, 0) / (r/2)</c> while <c>d &lt; r</c>, replaced by 1 for a sound with no distance multiplier or an
    /// omni one (<see cref="Mono"/>).
    ///
    /// **The view entity's own sounds are not where it stands.** A channel from the view entity is put 12 units along the
    /// listener's forward (FUN_180021c20: the right vector's yaw + 90, flattened), so its distance is 12, and when its
    /// weight reaches 1 FUN_180027560 writes the sum of the ears into both: 0.9 + 0.9.
    /// </remarks>
    public static (float Distance, float Left, float Right) Spatialize(
        (float X, float Y, float Z) origin,
        (float X, float Y, float Z) listener,
        (float X, float Y, float Z) right,
        float mono,
        bool local,
        float radius = 0f,
        bool headphones = true)
    {
        float distance = Distance(origin, listener, local);
        float weight = mono >= 1f ? 1f : InsideRadius(distance, radius);

        if (local)
        {
            (float ahead, float other) = Pan(headphones, (-right.Y, right.X, 0f), right, weight);

            return weight >= 1f ? (distance, ahead + other, ahead + other) : (distance, ahead, other);
        }

        (float left, float rightEar) = Pan(
            headphones, (origin.X - listener.X, origin.Y - listener.Y, origin.Z - listener.Z), right, weight);

        return (distance, left, rightEar);
    }

    /// <summary>The mono weight a channel takes inside its model's radius: <c>1 − max(d − r/2, 0) / (r/2)</c> while <c>d &lt; r</c>.</summary>
    /// <param name="distance">How far the channel is.</param>
    /// <param name="radius">Its model's radius; 0 or less has none.</param>
    /// <returns>The weight, 0 outside the radius.</returns>
    public static float InsideRadius(float distance, float radius)
    {
        if (!(radius > 0f && distance < radius))
        {
            return 0f;
        }

        float past = distance - (radius * 0.5f);

        if (past <= 0f)
        {
            past = 0f;
        }

        return 1f - (past / (radius * 0.5f));
    }

    /// <summary>The distance a channel's gain is taken at: 12 for the view entity's own, else from the listener.</summary>
    /// <param name="origin">Where the channel is.</param>
    /// <param name="listener">Where the ears are.</param>
    /// <param name="local">Whether the channel is the view entity's own.</param>
    /// <returns>The distance.</returns>
    public static float Distance((float X, float Y, float Z) origin, (float X, float Y, float Z) listener, bool local)
    {
        if (local)
        {
            return LocalDistance;
        }

        float dx = origin.X - listener.X;
        float dy = origin.Y - listener.Y;
        float dz = origin.Z - listener.Z;

        return MathF.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }

    /// <summary>How far ahead the view entity's own sounds are put: 12 (<c>FUN_180029440</c>, <c>local_198 × 12.0</c>).</summary>
    private const float LocalDistance = 12f;

    /// <summary>Whether a <c>snd_surround_speakers</c> value opens the device as headphones.</summary>
    /// <param name="surroundSpeakers">The setting; "-1" by default.</param>
    /// <returns>True at 0 and below.</returns>
    /// <remarks>
    /// The sound init (engine.dll FUN_18002c300) asks the device for max(it, 0) speakers, and the XAudio2 device
    /// (FUN_1802807a0) sets its headphone flag when that is 0. *Interpolated:* 4, 5 and 6 open a surround device of 6 or 8
    /// channels (FUN_18027fe80), which this stereo output cannot play; they take the two-speaker law.
    /// </remarks>
    public static bool IsHeadphones(int surroundSpeakers) => surroundSpeakers <= 0;

    /// <summary>Each ear's share on the device <paramref name="headphones"/> names.</summary>
    /// <param name="headphones">Whether the device is headphones (<see cref="IsHeadphones"/>).</param>
    /// <param name="direction">From the listener to the sound.</param>
    /// <param name="listenerRight">The listener's right vector.</param>
    /// <param name="mono">The mono weight.</param>
    /// <returns>Left and right.</returns>
    public static (float Left, float Right) Pan(
        bool headphones,
        (float X, float Y, float Z) direction,
        (float X, float Y, float Z) listenerRight,
        float mono) =>
        headphones ? Headphones(direction, listenerRight, mono) : Speakers(direction, listenerRight, mono);

    /// <summary>Each ear's share on a two-speaker device (<c>snd_surround_speakers 2</c>).</summary>
    /// <param name="direction">From the listener to the sound; its length does not matter.</param>
    /// <param name="listenerRight">The listener's right vector; only its yaw is read.</param>
    /// <param name="mono">The mono weight.</param>
    /// <returns>Left and right, 0..1.</returns>
    /// <remarks>
    /// FUN_18001e0b0's two-channel branch without the headphone flag: four virtual speakers at bearings 45 (front right),
    /// 135 (front left), 315 (rear right) and 225 (rear left), each <c>1 − FastPow(d / 90, 1.5)</c> within 90° of the
    /// sound; a front blends toward 0.9 by the mono weight and a rear is scaled by <c>1 − mono</c>; each ear is its front
    /// plus 0.75 (the double at <c>0x18035fd58</c>) × its rear, summed in double and clamped to 0..1.
    /// </remarks>
    public static (float Left, float Right) Speakers(
        (float X, float Y, float Z) direction,
        (float X, float Y, float Z) listenerRight,
        float mono)
    {
        (float pitch, float yaw) = Angles(direction);
        (_, float rightYaw) = Angles((listenerRight.X, listenerRight.Y, 0f));

        float bearing = yaw - rightYaw;

        if (bearing < 0f)
        {
            bearing += 360f;
        }

        float weight = Elevated(pitch, mono);

        float frontRight = Speaker(bearing, 45f);
        float frontLeft = Speaker(bearing, 135f);
        float rearRight = Speaker(bearing, 315f);
        float rearLeft = Speaker(bearing, 225f);

        float right = Fold(((MonoShare - frontRight) * weight) + frontRight, rearRight - (rearRight * weight));
        float left = Fold(((MonoShare - frontLeft) * weight) + frontLeft, rearLeft - (rearLeft * weight));

        return (left, right);

        static float Fold(float front, float rear)
        {
            double sum = (rear * 0.75) + front;

            return (float)(sum >= 0.0 ? Math.Min(sum, 1.0) : 0.0);
        }
    }

    /// <summary>One virtual speaker's share: <c>1 − FastPow(d × 1/90, 1.5)</c> within 90° of the sound, else 0.</summary>
    private static float Speaker(float bearing, float at)
    {
        float away = MathF.Abs(bearing - at);

        if (away > 180f)
        {
            away = 360f - away;
        }

        return away < 90f ? 1f - FastPow(away * PerQuarterTurn, Exponent) : 0f;
    }

    /// <summary>1 / 90 as the float at <c>0x18035fd20</c>, which a virtual speaker's angle is scaled by.</summary>
    private const float PerQuarterTurn = 0.011111111f;

    /// <summary>Each ear's share of a sound on a headphone device.</summary>
    /// <param name="direction">From the listener to the sound; its length does not matter.</param>
    /// <param name="listenerRight">The listener's right vector; only its yaw is read.</param>
    /// <param name="mono">The mono weight, from <see cref="Mono"/>.</param>
    /// <returns>Left and right, 0..1.</returns>
    /// <remarks>
    /// The engine normalises both vectors first (<c>rsqrtss</c> and one Newton step); an angle is the same for any
    /// positive scale, so this does not, and differs from it only in the last bits of the products the angle is taken
    /// from.
    /// </remarks>
    public static (float Left, float Right) Headphones(
        (float X, float Y, float Z) direction,
        (float X, float Y, float Z) listenerRight,
        float mono)
    {
        (float pitch, float yaw) = Angles(direction);
        (_, float rightYaw) = Angles((listenerRight.X, listenerRight.Y, 0f));

        float bearing = yaw - rightYaw;

        if (bearing < 0f)
        {
            bearing += 360f;
        }

        float weight = Elevated(pitch, mono);

        return (Ear(bearing, 180f, weight), Ear(bearing, 0f, weight));
    }

    /// <summary><c>FUN_18001efb0</c> for two channels, then <c>FUN_18001f980</c>'s blend toward mono.</summary>
    private static float Ear(float bearing, float at, float mono)
    {
        float away = MathF.Abs(bearing - at);

        if (away > 180f)
        {
            away = 360f - away;
        }

        float share = 1f - FastPow(away * PerHalfTurn, Exponent);

        return ((MonoShare - share) * mono) + share;
    }

    /// <summary>The mono weight with the elevation's: past 45° it rises 1/45 a degree, clamped to 0..1.</summary>
    private static float Elevated(float pitch, float mono)
    {
        float elevation = pitch < 0f ? pitch + 360f : pitch;

        if (elevation > 180f)
        {
            elevation = 360f - elevation;
        }

        // Unreachable from VectorAngles' 0..90 ∪ 270..360 once folded above; kept as the engine has it.
        if (elevation > 90f)
        {
            elevation = 90f - (elevation - 90f);
        }

        if (!(elevation > 45f))
        {
            return mono;
        }

        float weight = ((elevation - 45f) * PerElevationDegree) + mono;

        if (weight <= 0f)
        {
            weight = 0f;
        }

        return 1f <= weight ? 1f : weight;
    }

    /// <summary><c>FUN_18027add0</c>, <c>VectorAngles</c>: pitch and yaw in degrees, each 0..360.</summary>
    private static (float Pitch, float Yaw) Angles((float X, float Y, float Z) v)
    {
        if (v.Y == 0f && v.X == 0f)
        {
            return (v.Z <= 0f ? 90f : 270f, 0f);
        }

        float yaw = (float)(Math.Atan2(v.Y, v.X) * 57.29577951308232);

        if (yaw < 0f)
        {
            yaw += 360f;
        }

        float pitch = (float)(Math.Atan2(-v.Z, MathF.Sqrt((v.X * v.X) + (v.Y * v.Y))) * 57.29577951308232);

        if (pitch < 0f)
        {
            pitch += 360f;
        }

        return (pitch, yaw);
    }

    /// <summary>
    /// <c>FUN_18027b2a0</c>, mathlib's <c>FastPow</c>: <c>2^(power × log2 x)</c> with both halves approximated from the
    /// float's bits.
    /// </summary>
    /// <param name="x">The base; at or below 2^-23 the answer is 0.</param>
    /// <param name="power">The exponent.</param>
    /// <returns>The engine's approximation, bit for bit.</returns>
    public static float FastPow(float x, float power)
    {
        // 2^-23 (0x180480040): the log is the float's bits read as an integer, scaled to the exponent's units.
        const float Ulp = 1.1920929e-7f;

        if (!(x > Ulp))
        {
            return 0f;
        }

        float log = (BitConverter.SingleToInt32Bits(x) * Ulp) - 127f;
        float fraction = log - Floor(log);
        log = ((fraction - (fraction * fraction)) * LogCorrection) + log;

        float scaled = log * power;
        float part = scaled - Floor(scaled);
        float bits = (scaled + 127f) - ((part - (part * part)) * ExpCorrection);

        return BitConverter.Int32BitsToSingle(Truncate(bits * 8388608f));
    }

    /// <summary>
    /// The floor <c>FastPow</c> builds from <c>cvttss2si</c>: one less for a negative non-integer, and the value itself
    /// when the conversion has no answer.
    /// </summary>
    private static float Floor(float value)
    {
        if (!(value >= -2147483648f && value < 2147483648f))
        {
            return value;
        }

        int whole = (int)value;

        // ucomiss of the conversion against the value: an integer is its own floor.
        if (float.IsInteger(value))
        {
            return whole;
        }

        return float.IsNegative(value) ? whole - 1 : whole;
    }

    /// <summary><c>cvttss2si</c>: toward zero, and <c>0x80000000</c> when out of range or NaN.</summary>
    private static int Truncate(float value) =>
        value >= -2147483648f && value < 2147483648f ? (int)value : int.MinValue;
}
