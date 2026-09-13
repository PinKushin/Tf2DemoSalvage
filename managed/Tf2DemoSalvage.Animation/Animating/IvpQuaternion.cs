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
    /// **The tolerance is read: `DAT_1800f4f28` dumps as `1e-12`** (B369, `docs/findings/51`). It was
    /// `1e-9` here for a while, marked as this transcription's own because it had not been dumped.
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

    /// <summary>A rotation part-way between two, the short way round — <c>FUN_180071060</c>.</summary>
    /// <param name="from">The rotation at a fraction of zero.</param>
    /// <param name="to">The rotation at a fraction of one.</param>
    /// <param name="fraction">How far along, from zero to one.</param>
    /// <returns>The interpolated rotation.</returns>
    /// <remarks>
    /// **Read from the disassembly, because the decompiler dropped both `sin` arguments** (B369,
    /// `docs/findings/51`). It is what `FUN_1800734e0` rotates a body through when IVP evaluates it at a
    /// lattice time inside a step — the first layer of the time-of-impact search.
    ///
    /// <code>
    /// dot = from · to;  sign = dot > 0 ? +1 : (dot = −dot, −1)
    /// dot ≥ 0.999:  out = from + (sign·to − from)·t;  s = 0.5·|out|²;  x = 1.5 − s;
    ///               x = x + (0.5 − x²·s), twice;  out = out·x
    /// otherwise:    out = sin((1 − t)θ)/sin θ · from + sign · sin(tθ)/sin θ · to,  θ = acos(dot)
    /// </code>
    ///
    /// **The two branch tests are written to fall the same way the instructions do on a NaN.**
    /// `COMISD` then `JBE` negates when the dot is at or below zero OR unordered, which is the `else` of
    /// `dot > 0`; `JNC` takes the lerp only when the dot is at least the cut-over AND ordered, which is
    /// `dot >= cut`. Rewriting either as its apparent opposite would change which branch a NaN takes.
    ///
    /// **Computed in doubles as the engine computes it, stored back as floats as this project stores a
    /// rotation.** Above the cut-over the two branches differ by at most `8e-12`, which a float cannot
    /// hold; the lerp is carried anyway, because it is the engine's.
    /// </remarks>
    public static (float X, float Y, float Z, float W) Interpolate(
        (float X, float Y, float Z, float W) from,
        (float X, float Y, float Z, float W) to,
        float fraction)
    {
        double t = fraction;

        double dot =
            ((double)from.X * to.X) + ((double)from.Y * to.Y) +
            ((double)from.Z * to.Z) + ((double)from.W * to.W);

        double sign;

        if (dot > 0d)
        {
            sign = 1d;
        }
        else
        {
            dot = -dot;
            sign = -1d;
        }

        if (dot >= LerpCutOver)
        {
            double x = from.X + (((sign * to.X) - from.X) * t);
            double y = from.Y + (((sign * to.Y) - from.Y) * t);
            double z = from.Z + (((sign * to.Z) - from.Z) * t);
            double w = from.W + (((sign * to.W) - from.W) * t);

            double half = ((x * x) + (y * y) + (z * z) + (w * w)) * NewtonHalf;

            double scale = NewtonStart - half;
            scale += NewtonHalf - (scale * scale * half);
            scale += NewtonHalf - (scale * scale * half);

            return ((float)(x * scale), (float)(y * scale), (float)(z * scale), (float)(w * scale));
        }

        double angle = Math.Acos(dot);
        double inverseSine = 1d / Math.Sqrt(1d - (dot * dot));

        double fromWeight = Math.Sin((1d - t) * angle) * inverseSine;
        double toWeight = sign * Math.Sin(t * angle) * inverseSine;

        return (
            (float)((fromWeight * from.X) + (toWeight * to.X)),
            (float)((fromWeight * from.Y) + (toWeight * to.Y)),
            (float)((fromWeight * from.Z) + (toWeight * to.Z)),
            (float)((fromWeight * from.W) + (toWeight * to.W)));
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
    /// **`DAT_1800f4f28`, dumped as `1e-12`.** It replaced an invented `1e-9`.
    ///
    /// **No test separates the two, and that is arithmetic, not an omission.** A float quaternion
    /// inside the gap exists — `(0.3631, 0, 0, 0.9317502)` is `7.8e-10` off unit — but rescaling by
    /// `1/√(1 − 7.8e-10)` moves a component near `0.93` by about `4e-10`, and a float's spacing there is
    /// `6e-8`, so the result rounds back to the same bits. That holds for every input under `1e-9`:
    /// through this float API the value cannot change an output. It is carried because it is the
    /// engine's number, not because anything here could observe it.
    /// </remarks>
    private const double UnitTolerance = 1e-12d;

    /// <summary><c>DAT_1800fcea0</c>: the float <c>0.999</c>, widened — the dot above which a lerp is taken.</summary>
    private const double LerpCutOver = 0.999f;

    /// <summary><c>DAT_1800ee388</c>, <c>0.5</c>: the renormalisation's half.</summary>
    private const double NewtonHalf = 0.5d;

    /// <summary><c>DAT_1800ea9c0</c>, <c>1.5</c>: the renormalisation's starting point.</summary>
    private const double NewtonStart = 1.5d;
}
