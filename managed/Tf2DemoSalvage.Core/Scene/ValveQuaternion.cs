using System;
using System.Numerics;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>Valve's quaternion blends as <c>mathlib_base.cpp</c> writes them, in the one place every layer reaches (B629).</summary>
/// <remarks>
/// **Not <c>System.Numerics</c>, which is close and not the same.** <c>Quaternion.Slerp</c> aligns by the dot's sign rather
/// than <c>QuaternionAlign</c>'s sums of squares and weighs by <c>sin · (1 / sin ω)</c> where Valve divides by
/// <c>sin ω</c>, so it misses the exact input at <c>t = 0</c> and the exact aligned target at <c>t = 1</c>. Core holds it
/// because the demo's own viewpoint interpolation (<c>ScenePropTrack</c>) needs it as much as the skeleton does.
/// </remarks>
public static class ValveQuaternion
{
    /// <summary><c>QuaternionAlign</c> (<c>mathlib_base.cpp:1509-1538</c>): the rotation negated when it points the long way round.</summary>
    /// <param name="to">Valve's <c>p</c>, aligned against.</param>
    /// <param name="rotation">Valve's <c>q</c>.</param>
    /// <returns><c>qt</c>.</returns>
    /// <remarks>Compared as sums of squares, as written, rather than by a dot product, which Valve marks as possible.</remarks>
    public static (float X, float Y, float Z, float W) Align(
        (float X, float Y, float Z, float W) to,
        (float X, float Y, float Z, float W) rotation)
    {
        float apart =
            ((to.X - rotation.X) * (to.X - rotation.X)) +
            ((to.Y - rotation.Y) * (to.Y - rotation.Y)) +
            ((to.Z - rotation.Z) * (to.Z - rotation.Z)) +
            ((to.W - rotation.W) * (to.W - rotation.W));

        float together =
            ((to.X + rotation.X) * (to.X + rotation.X)) +
            ((to.Y + rotation.Y) * (to.Y + rotation.Y)) +
            ((to.Z + rotation.Z) * (to.Z + rotation.Z)) +
            ((to.W + rotation.W) * (to.W + rotation.W));

        return apart > together ? (-rotation.X, -rotation.Y, -rotation.Z, -rotation.W) : rotation;
    }

    /// <summary><c>QuaternionSlerp</c> (<c>mathlib_base.cpp:1605-1614</c>): align, then <see cref="SlerpNoAlign"/>.</summary>
    /// <param name="from">Valve's <c>p</c>, the rotation at <paramref name="fraction"/> zero.</param>
    /// <param name="to">Valve's <c>q</c>, at one.</param>
    /// <param name="fraction">Valve's <c>t</c>.</param>
    /// <returns><c>qt</c>.</returns>
    public static (float X, float Y, float Z, float W) Slerp(
        (float X, float Y, float Z, float W) from,
        (float X, float Y, float Z, float W) to,
        float fraction) =>
        SlerpNoAlign(from, Align(from, to), fraction);

    /// <summary>The same over <see cref="Quaternion"/>s.</summary>
    /// <param name="from">Valve's <c>p</c>.</param>
    /// <param name="to">Valve's <c>q</c>.</param>
    /// <param name="fraction">Valve's <c>t</c>.</param>
    /// <returns><c>qt</c>.</returns>
    public static Quaternion Slerp(Quaternion from, Quaternion to, float fraction)
    {
        (float x, float y, float z, float w) = Slerp((from.X, from.Y, from.Z, from.W), (to.X, to.Y, to.Z, to.W), fraction);

        return new Quaternion(x, y, z, w);
    }

    /// <summary><c>QuaternionSlerpNoAlign</c> (<c>mathlib_base.cpp:1617-1658</c>).</summary>
    /// <param name="from">Valve's <c>p</c>.</param>
    /// <param name="to">Valve's <c>q</c>.</param>
    /// <param name="fraction">Valve's <c>t</c>.</param>
    /// <returns><c>qt</c>.</returns>
    /// <remarks>
    /// **The antipodal arm is a different rotation, not a rounding guard**: a perpendicular built from the target, swept
    /// a quarter turn. Its loop runs to THREE, leaving <c>qt[3]</c> as the perpendicular's own <c>q[2]</c>; reproduced.
    /// </remarks>
    public static (float X, float Y, float Z, float W) SlerpNoAlign(
        (float X, float Y, float Z, float W) from,
        (float X, float Y, float Z, float W) to,
        float fraction)
    {
        const float Epsilon = 0.000001f;

        float cosine = (from.X * to.X) + (from.Y * to.Y) + (from.Z * to.Z) + (from.W * to.W);

        if (1f + cosine > Epsilon)
        {
            float fromShare;
            float toShare;

            if (1f - cosine > Epsilon)
            {
                float omega = MathF.Acos(cosine);
                float sine = MathF.Sin(omega);

                fromShare = MathF.Sin((1f - fraction) * omega) / sine;
                toShare = MathF.Sin(fraction * omega) / sine;
            }
            else
            {
                fromShare = 1f - fraction;
                toShare = fraction;
            }

            return (
                (fromShare * from.X) + (toShare * to.X),
                (fromShare * from.Y) + (toShare * to.Y),
                (fromShare * from.Z) + (toShare * to.Z),
                (fromShare * from.W) + (toShare * to.W));
        }

        (float X, float Y, float Z, float W) perpendicular = (-to.Y, to.X, -to.W, to.Z);

        float quarter = 0.5f * MathF.PI;
        float fromArc = MathF.Sin((1f - fraction) * quarter);
        float toArc = MathF.Sin(fraction * quarter);

        return (
            (fromArc * from.X) + (toArc * perpendicular.X),
            (fromArc * from.Y) + (toArc * perpendicular.Y),
            (fromArc * from.Z) + (toArc * perpendicular.Z),
            perpendicular.W);
    }

    /// <summary>
    /// <c>QuaternionBlend</c> (<c>mathlib_base.cpp:1545-1576</c>, the scalar path): align, a componentwise lerp, then
    /// <c>QuaternionNormalize</c>. A normalised lerp, not a slerp.
    /// </summary>
    /// <param name="from">Valve's <c>p</c>.</param>
    /// <param name="to">Valve's <c>q</c>.</param>
    /// <param name="fraction">Valve's <c>t</c>.</param>
    /// <returns><c>qt</c>.</returns>
    public static (float X, float Y, float Z, float W) Blend(
        (float X, float Y, float Z, float W) from,
        (float X, float Y, float Z, float W) to,
        float fraction)
    {
        (float X, float Y, float Z, float W) aligned = Align(from, to);
        float fromShare = 1f - fraction;

        return Normalize((
            (fromShare * from.X) + (fraction * aligned.X),
            (fromShare * from.Y) + (fraction * aligned.Y),
            (fromShare * from.Z) + (fraction * aligned.Z),
            (fromShare * from.W) + (fraction * aligned.W)));
    }

    /// <summary>
    /// <c>InterpolateAngles</c> (<c>cdll_util.cpp:206-221</c>): <c>AngleQuaternion</c> both, <c>QuaternionSlerp</c>,
    /// <c>QuaternionAngles</c>, with no equality shortcut.
    /// </summary>
    /// <param name="from">Valve's <c>start</c>, in degrees.</param>
    /// <param name="to">Valve's <c>end</c>.</param>
    /// <param name="fraction">Valve's <c>frac</c>.</param>
    /// <returns>Valve's <c>output</c>.</returns>
    public static (float Pitch, float Yaw, float Roll) InterpolateAngles(
        (float Pitch, float Yaw, float Roll) from, (float Pitch, float Yaw, float Roll) to, float fraction) =>
        QuaternionAngles(Slerp(
            AngleQuaternion(from.Pitch, from.Yaw, from.Roll), AngleQuaternion(to.Pitch, to.Yaw, to.Roll), fraction));

    /// <summary><c>AngleQuaternion</c> from <c>mathlib_base.cpp</c>.</summary>
    /// <param name="pitch">Degrees.</param>
    /// <param name="yaw">Degrees.</param>
    /// <param name="roll">Degrees.</param>
    /// <returns>The rotation.</returns>
    public static Quaternion AngleQuaternion(float pitch, float yaw, float roll)
    {
        (float sinYaw, float cosYaw) = MathF.SinCos(float.DegreesToRadians(yaw) * 0.5f);
        (float sinPitch, float cosPitch) = MathF.SinCos(float.DegreesToRadians(pitch) * 0.5f);
        (float sinRoll, float cosRoll) = MathF.SinCos(float.DegreesToRadians(roll) * 0.5f);

        float sinRollCosPitch = sinRoll * cosPitch;
        float cosRollSinPitch = cosRoll * sinPitch;
        float cosRollCosPitch = cosRoll * cosPitch;
        float sinRollSinPitch = sinRoll * sinPitch;

        return new Quaternion(
            (sinRollCosPitch * cosYaw) - (cosRollSinPitch * sinYaw),
            (cosRollSinPitch * cosYaw) + (sinRollCosPitch * sinYaw),
            (cosRollCosPitch * sinYaw) - (sinRollSinPitch * cosYaw),
            (cosRollCosPitch * cosYaw) + (sinRollSinPitch * sinYaw));
    }

    /// <summary>The inverse, as <c>QuaternionAngles</c> computes it.</summary>
    /// <param name="q">The rotation.</param>
    /// <returns>Pitch, yaw and roll in degrees.</returns>
    /// <remarks>
    /// The engine routes this through a matrix, but the direct form is in the same function under
    /// an <c>#else</c> and is the one transcribed here. Its noted singularity near pitch ±90 is
    /// real and shared: at straight up or down, yaw and roll describe the same rotation and the
    /// split between them is arbitrary.
    /// </remarks>
    public static (float Pitch, float Yaw, float Roll) QuaternionAngles(Quaternion q)
    {
        float m11 = (2f * q.W * q.W) + (2f * q.X * q.X) - 1f;
        float m12 = (2f * q.X * q.Y) + (2f * q.W * q.Z);
        float m13 = (2f * q.X * q.Z) - (2f * q.W * q.Y);
        float m23 = (2f * q.Y * q.Z) + (2f * q.W * q.X);
        float m33 = (2f * q.W * q.W) + (2f * q.Z * q.Z) - 1f;

        return (
            float.RadiansToDegrees(MathF.Asin(Math.Clamp(-m13, -1f, 1f))),
            float.RadiansToDegrees(MathF.Atan2(m12, m11)),
            float.RadiansToDegrees(MathF.Atan2(m23, m33)));
    }

    /// <summary><c>QuaternionNormalize</c>: unit length; an all-zero quaternion is returned as it is (<c>if ( radius )</c>).</summary>
    /// <param name="rotation">The rotation.</param>
    /// <returns>It, normalised.</returns>
    public static (float X, float Y, float Z, float W) Normalize((float X, float Y, float Z, float W) rotation)
    {
        float radius =
            (rotation.X * rotation.X) + (rotation.Y * rotation.Y) +
            (rotation.Z * rotation.Z) + (rotation.W * rotation.W);

        if (radius == 0f)
        {
            return rotation;
        }

        float inverse = 1f / MathF.Sqrt(radius);

        return (rotation.X * inverse, rotation.Y * inverse, rotation.Z * inverse, rotation.W * inverse);
    }
}
