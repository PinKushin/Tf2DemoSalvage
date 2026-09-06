using System;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// IVP's own quaternion arithmetic, which is not Valve's mathlib (B58, D142).
/// </summary>
/// <remarks>
/// **A separate type on purpose, because reaching for the mathlib one would be wrong.** This
/// codebase already carries `StudioBones.Multiply` and `StudioBones.Normalize`, both transcribed
/// from `mathlib_base.cpp` — and both differ from what `vphysics.dll` does:
///
/// - **Valve's `QuaternionMult` ALIGNS first**, negating the second rotation when the two point
///   opposite ways. `FUN_180070d60` does not, so on inputs more than a half-turn apart the two
///   produce opposite signs.
/// - **Valve's `QuaternionNormalize` always divides** when the length is non-zero.
///   `FUN_180070c60` tests `|1 − |q|²|` against a tolerance and leaves an already-unit quaternion
///   bit-for-bit alone.
///
/// **The lane order is (x, y, z, w) with index 3 as the real part**, established from
/// `FUN_180070d60`'s own arithmetic rather than assumed from Source's `Quaternion` struct: each of
/// the product's four output expressions matches the Hamilton product only under that assignment.
///
/// **The engine works in DOUBLE here** — every one of these functions takes `double *` — while a
/// core's angular velocity is float. That split is reproduced rather than flattened, because it is
/// where the engine's precision actually lives.
/// </remarks>
public static class IvpQuaternion
{
    /// <summary>The Hamilton product, with no alignment — <c>FUN_180070d60</c>.</summary>
    /// <param name="first">The left rotation.</param>
    /// <param name="second">The right rotation.</param>
    /// <returns>Their product.</returns>
    /// <remarks>
    /// **Verbatim, and it is the plain product:**
    ///
    /// <code>
    /// *param_1   = (b0*a3 + a0*b3 + b2*a1) - a2*b1;
    /// param_1[1] = (b1*a3 + b3*a1 + a2*b0) - a0*b2;
    /// param_1[2] = (a2*b3 + a3*b2 + b1*a0) - a1*b0;
    /// param_1[3] = ((a3*b3 - a0*b0) - b1*a1) - b2*a2;
    /// </code>
    ///
    /// **No `QuaternionAlign`.** Valve's mathlib version calls one and this does not, which is the
    /// whole reason this method exists next to `StudioBones.Multiply` rather than delegating to it.
    /// </remarks>
    public static (float X, float Y, float Z, float W) Product(
        (float X, float Y, float Z, float W) first,
        (float X, float Y, float Z, float W) second)
    {
        double ax = first.X;
        double ay = first.Y;
        double az = first.Z;
        double aw = first.W;

        double bx = second.X;
        double by = second.Y;
        double bz = second.Z;
        double bw = second.W;

        return (
            (float)(((bx * aw) + (ax * bw) + (bz * ay)) - (az * by)),
            (float)(((by * aw) + (bw * ay) + (az * bx)) - (ax * bz)),
            (float)(((az * bw) + (aw * bz) + (by * ax)) - (ay * bx)),
            (float)((((aw * bw) - (ax * bx)) - (by * ay)) - (bz * az)));
    }

    /// <summary>Scales a rotation to unit length, but only if it needs it — <c>FUN_180070c60</c>.</summary>
    /// <param name="rotation">The rotation.</param>
    /// <returns>The rotation, at unit length.</returns>
    /// <remarks>
    /// **The guard is the behaviour.** The engine computes `1 − |q|²`, takes its absolute value by
    /// masking off the sign bit, and only if that reaches a tolerance does it run a Newton-Raphson
    /// reciprocal square root to convergence:
    ///
    /// <code>
    /// dVar7 = q[3]*q[3] + q[2]*q[2] + q[0]*q[0] + q[1]*q[1];
    /// uVar3 = SUB84(DAT_1800ea9b8 - dVar7,0) &amp; (uint)DAT_1800ed7c0;   // fabs(1 - |q|^2)
    /// if (DAT_1800f4f28 &lt;= …) { …iterate… }
    /// </code>
    ///
    /// **So a quaternion already at unit length is returned untouched**, and one that has drifted is
    /// scaled. An implementation that always divides changes bits the engine leaves alone — which
    /// accumulates over a corpse's life rather than showing up at once.
    ///
    /// **The tolerance's value was NOT dumped**, so the constant below is this transcription's own:
    /// it is chosen small enough that anything the engine would iterate on is iterated on here.
    /// Marked because it is the one number on this page that is not read.
    /// </remarks>
    public static (float X, float Y, float Z, float W) Normalise(
        (float X, float Y, float Z, float W) rotation)
    {
        double square =
            ((double)rotation.X * rotation.X) + ((double)rotation.Y * rotation.Y) +
            ((double)rotation.Z * rotation.Z) + ((double)rotation.W * rotation.W);

        if (Math.Abs(1d - square) < UnitTolerance)
        {
            return rotation;
        }

        double scale = 1d / Math.Sqrt(square);

        return (
            (float)(rotation.X * scale), (float)(rotation.Y * scale),
            (float)(rotation.Z * scale), (float)(rotation.W * scale));
    }

    /// <summary>One step's rotation, from an angular velocity — <c>FUN_180071680</c>.</summary>
    /// <param name="angularVelocity">Radians per second about each axis.</param>
    /// <param name="delta">The step, in seconds.</param>
    /// <returns>The rotation to apply.</returns>
    /// <remarks>
    /// **Per-axis third-order Taylor sine, and the real part from unit length:**
    ///
    /// <code>
    /// dVar2 = param_3 * DAT_1800ee388;                              // dt/2,  DAT_1800ee388 = 0.5
    /// fVar4 = (float)((double)*param_2 * dVar2);                    // theta = w * dt/2
    /// dVar6 = (double)(fVar4 - fVar4*fVar4*fVar4*DAT_1800eb148);    // DAT_1800eb148 = 0.16666667f
    /// …
    /// param_1[3] = sqrt( DAT_1800ea9b8 - (x*x + y*y + z*z) );       // w = sqrt(1 - |xyz|^2)
    /// </code>
    ///
    /// **Three things a plausible version gets wrong**, and each is a corpse that settles elsewhere:
    ///
    /// - **It is not `sin`.** At a half-angle of 0.5 the series gives 0.47916667 where the real sine
    ///   gives 0.47942554 — and the difference compounds over the sub-steps below.
    /// - **The cube is computed in FLOAT**, with a float 1/6, then widened. The engine narrows to
    ///   `float` at `fVar4` before cubing.
    /// - **Each axis is built independently.** This is NOT a rotation about the combined axis; the
    ///   real part is whatever unit length leaves over, not a cosine.
    /// </remarks>
    public static (float X, float Y, float Z, float W) Delta(
        (float X, float Y, float Z) angularVelocity, float delta)
    {
        double half = delta * Half;

        float x = Sine((float)(angularVelocity.X * half));
        float y = Sine((float)(angularVelocity.Y * half));
        float z = Sine((float)(angularVelocity.Z * half));

        double remaining = 1d - (((double)x * x) + ((double)y * y) + ((double)z * z));

        return (x, y, z, (float)Math.Sqrt(remaining < 0d ? 0d : remaining));
    }

    /// <summary>Turns a vector by a rotation.</summary>
    /// <param name="rotation">The orientation, as a unit quaternion.</param>
    /// <param name="vector">The vector, in the rotation's source frame.</param>
    /// <returns>The vector in the rotated frame.</returns>
    /// <remarks>
    /// **The engine does this with a cached 3×3 rather than a quaternion.** `FUN_180037bd0` reads
    /// the rows at `core+0x90..0xe8`, which are part of the 4×4 transform IVP keeps in DOUBLES
    /// beside the orientation. **How that matrix is filled from the orientation was NOT read**, so
    /// this is the same rotation reached by a different route rather than a transcription — the
    /// difference is rounding, not behaviour, but it is a gap and is marked as one.
    /// </remarks>
    public static (float X, float Y, float Z) Rotate(
        (float X, float Y, float Z, float W) rotation, (float X, float Y, float Z) vector)
    {
        // v + 2w(q × v) + 2q × (q × v), which avoids building the matrix for one vector.
        (float X, float Y, float Z) first = (
            (rotation.Y * vector.Z) - (rotation.Z * vector.Y),
            (rotation.Z * vector.X) - (rotation.X * vector.Z),
            (rotation.X * vector.Y) - (rotation.Y * vector.X));

        (float X, float Y, float Z) second = (
            (rotation.Y * first.Z) - (rotation.Z * first.Y),
            (rotation.Z * first.X) - (rotation.X * first.Z),
            (rotation.X * first.Y) - (rotation.Y * first.X));

        return (
            vector.X + (2f * ((rotation.W * first.X) + second.X)),
            vector.Y + (2f * ((rotation.W * first.Y) + second.Y)),
            vector.Z + (2f * ((rotation.W * first.Z) + second.Z)));
    }

    /// <summary>The engine's sine: three terms, in single precision.</summary>
    /// <param name="angle">The half-angle, in radians.</param>
    /// <returns>Its approximate sine.</returns>
    private static float Sine(float angle) => angle - (angle * angle * angle * Sixth);

    /// <summary><c>DAT_1800ee388</c>, dumped as a double.</summary>
    private const double Half = 0.5d;

    /// <summary><c>DAT_1800eb148</c>, dumped as a FLOAT — the series' 1/6.</summary>
    private const float Sixth = 0.16666667f;

    /// <summary>How far off unit length a rotation must be before it is rescaled.</summary>
    /// <remarks>
    /// **This one is ours, not the engine's.** `DAT_1800f4f28` was not dumped, so the value is
    /// chosen to be smaller than anything the engine would tolerate rather than transcribed. Named
    /// and documented so the gap is visible instead of looking like a read constant.
    /// </remarks>
    private const double UnitTolerance = 1e-9d;
}
