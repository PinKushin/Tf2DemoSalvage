using System;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// IVP's own quaternion arithmetic, which is not Valve's mathlib (B58, D142, B369).
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
/// **In doubles, as the engine keeps them.** A core's two orientations at `core+0x180` and `+0x1a0` are doubles and every
/// routine here takes `double *`; only a core's spin is float. Every operation names the binary's destination first
/// (<see cref="IvpMath.Addsd"/>), read per instruction from the disassembly, and every routine is pinned to the shipped
/// `vphysics.dll` called in process by the `vphysics-rotation` probe (`IvpRotationConformanceTests`).
/// </remarks>
public static class IvpQuaternion
{
    /// <summary><c>DAT_1800ee388</c>: <c>0.5</c>.</summary>
    private const double Half = 0.5d;

    /// <summary><c>DAT_1800ea9c0</c>: <c>1.5</c>, where both reciprocal square roots start.</summary>
    private const double NewtonStart = 1.5d;

    /// <summary><c>DAT_1800eb148</c>, a FLOAT: the series' <c>1/6</c>.</summary>
    private const float Sixth = 0.16666667f;

    /// <summary><c>DAT_1800f4f28</c>: <c>1e-12</c>, how far off unit length a rotation must be before it is rescaled.</summary>
    private const double UnitTolerance = 1e-12d;

    /// <summary><c>DAT_1800fcea0</c>: the float <c>0.999</c>, widened — the dot at or above which a lerp is taken.</summary>
    private const double LerpCutOver = 0.999f;

    /// <summary><c>DAT_1800fcea8</c>: one less a little, what <c>FUN_180070f50</c> shrinks a vector part longer than one to.</summary>
    private static readonly double SineShrink = BitConverter.Int64BitsToDouble(0x3feffffffaa19c47);

    /// <summary>The Hamilton product, with no alignment — <c>FUN_180070d60</c>.</summary>
    /// <param name="first">The left rotation.</param>
    /// <param name="second">The right rotation.</param>
    /// <returns>Their product.</returns>
    /// <remarks>
    /// <code>
    /// out0 = ((b0·a3 + a0·b3) + b2·a1) − a2·b1      out1 = ((b1·a3 + b3·a1) + a2·b0) − a0·b2
    /// out2 = ((a2·b3 + a3·b2) + b1·a0) − a1·b0      out3 = ((a3·b3 − a0·b0) − b1·a1) − b2·a2
    /// </code>
    /// **Every lane of both is read before the first is written**, so the integrator's `FUN_180070d60(q, q, delta)` is safe.
    /// **No `QuaternionAlign`.** Valve's mathlib version calls one and this does not, which is the
    /// whole reason this method exists next to `StudioBones.Multiply` rather than delegating to it.
    /// </remarks>
    public static (double X, double Y, double Z, double W) Product(
        (double X, double Y, double Z, double W) first, (double X, double Y, double Z, double W) second)
    {
        (double a0, double a1, double a2, double a3) = first;
        (double b0, double b1, double b2, double b3) = second;

        return (
            IvpMath.Addsd(IvpMath.Addsd(IvpMath.Mulsd(b0, a3), IvpMath.Mulsd(a0, b3)), IvpMath.Mulsd(b2, a1)) - IvpMath.Mulsd(a2, b1),
            IvpMath.Addsd(IvpMath.Addsd(IvpMath.Mulsd(b1, a3), IvpMath.Mulsd(b3, a1)), IvpMath.Mulsd(a2, b0)) - IvpMath.Mulsd(a0, b2),
            IvpMath.Addsd(IvpMath.Addsd(IvpMath.Mulsd(a2, b3), IvpMath.Mulsd(a3, b2)), IvpMath.Mulsd(b1, a0)) - IvpMath.Mulsd(a1, b0),
            ((IvpMath.Mulsd(a3, b3) - IvpMath.Mulsd(a0, b0)) - IvpMath.Mulsd(b1, a1)) - IvpMath.Mulsd(b2, a2));
    }

    /// <summary>Scales a rotation to unit length, but only if it needs it — <c>FUN_180070c60</c>.</summary>
    /// <param name="rotation">The rotation.</param>
    /// <returns>The rotation, at unit length.</returns>
    /// <remarks>
    /// <code>
    /// n = (w·w + z·z) + (x·x + y·y);   !(|1 − n| > 1e-12) → unchanged   (COMISD/JBE: a NaN leaves it too)
    /// s = 1.5 − n·0.5;  r = 1 − (s·s)·n
    /// do  s = s + r·0.5;  r = 1 − (s·s)·n   while |r| > 1e-12
    /// q = q·s
    /// </code>
    /// **The guard is the behaviour**: a quaternion already at unit length keeps its bits, where one that always divides
    /// changes bits the engine leaves alone and accumulates over a corpse's life.
    ///
    /// **The loop is a fixed-point iteration, not Newton's, and it never returns for `n` of four or more**: its slope at the
    /// root is `1 − √n`, so from four up it oscillates outward, overflows to infinities and keeps `|r|` infinite. A zero
    /// quaternion never returns either, `s` growing by a half each pass. Both are the binary's, carried as they are; the
    /// integrator only hands it a product of unit rotations.
    /// </remarks>
    public static (double X, double Y, double Z, double W) Normalise((double X, double Y, double Z, double W) rotation)
    {
        (double x, double y, double z, double w) = rotation;
        double square = IvpMath.Addsd(
            IvpMath.Addsd(IvpMath.Mulsd(w, w), IvpMath.Mulsd(z, z)), IvpMath.Addsd(IvpMath.Mulsd(x, x), IvpMath.Mulsd(y, y)));

        if (Math.Abs(1d - square) > UnitTolerance)
        {
            double scale = NewtonStart - IvpMath.Mulsd(square, Half);
            double residual = 1d - IvpMath.Mulsd(IvpMath.Mulsd(scale, scale), square);

            do
            {
                scale = IvpMath.Addsd(scale, IvpMath.Mulsd(residual, Half));
                residual = 1d - IvpMath.Mulsd(IvpMath.Mulsd(scale, scale), square);
            }
            while (Math.Abs(residual) > UnitTolerance);

            return (IvpMath.Mulsd(x, scale), IvpMath.Mulsd(y, scale), IvpMath.Mulsd(z, scale), IvpMath.Mulsd(w, scale));
        }

        return rotation;
    }

    /// <summary>A rotation part-way between two, the short way round — <c>FUN_180071060</c>, on the path the runtime chose.</summary>
    /// <param name="from">The rotation at a fraction of zero.</param>
    /// <param name="to">The rotation at a fraction of one.</param>
    /// <param name="fraction">How far along.</param>
    /// <returns>The interpolated rotation.</returns>
    public static (double X, double Y, double Z, double W) Interpolate(
        (double X, double Y, double Z, double W) from, (double X, double Y, double Z, double W) to, double fraction) =>
        Interpolate(from, to, fraction, IvpMath.FusedPath);

    /// <summary><c>FUN_180071060</c>, with vphysics' <c>sin</c> on a given path.</summary>
    /// <param name="from">The rotation at a fraction of zero.</param>
    /// <param name="to">The rotation at a fraction of one.</param>
    /// <param name="fraction">How far along.</param>
    /// <param name="fused">Whether <c>sin</c> takes its fused-multiply-add path.</param>
    /// <returns>The interpolated rotation.</returns>
    /// <remarks>
    /// **Read from the disassembly, because the decompiler dropped both `sin` arguments** (B369, `docs/findings/51`). It is
    /// what `FUN_1800734e0` rotates a body through when IVP evaluates it at a lattice time inside a step — the first layer of
    /// the time-of-impact search.
    ///
    /// <code>
    /// dot = (a3·b3 + a2·b2) + (a1·b1 + a0·b0);  dot > 0 → σ = 1f  else dot = −dot (the sign bit), σ = −1f
    /// dot ≥ (double)0.999f:  out = (σ·b − a)·t + a;  h = ((w² + z²) + (x² + y²))·0.5;  s = 1.5 − h;
    ///                        s = s + (0.5 − (s·s)·h), twice;  out = s·out
    /// otherwise:  θ = acos(dot);  k = 1/√(1 − dot·dot);  out = b·(σ·(sin(θ·t)·k)) + (sin((1 − t)·θ)·k)·a
    /// </code>
    ///
    /// **The two branch tests fall the way the instructions do on a NaN.** `COMISD` then `JBE` negates when the dot is at or
    /// below zero OR unordered, the `else` of `dot > 0`; `JNC` takes the lerp only when the dot is at least the cut-over AND
    /// ordered, `dot >= cut`. **`acos` and `sin` are vphysics' own** (<see cref="IvpMath.Acos"/>, <see cref="IvpMath.Sin(double, bool)"/>).
    /// </remarks>
    public static (double X, double Y, double Z, double W) Interpolate(
        (double X, double Y, double Z, double W) from, (double X, double Y, double Z, double W) to, double fraction, bool fused)
    {
        (double a0, double a1, double a2, double a3) = from;
        (double b0, double b1, double b2, double b3) = to;
        double dot = IvpMath.Addsd(
            IvpMath.Addsd(IvpMath.Mulsd(a3, b3), IvpMath.Mulsd(a2, b2)), IvpMath.Addsd(IvpMath.Mulsd(a1, b1), IvpMath.Mulsd(a0, b0)));
        float sign;

        if (dot > 0d)
        {
            sign = 1f;
        }
        else
        {
            dot = -dot;
            sign = -1f;
        }

        if (dot >= LerpCutOver)
        {
            double x = Lerp(a0, b0, sign, fraction);
            double y = Lerp(a1, b1, sign, fraction);
            double z = Lerp(a2, b2, sign, fraction);
            double w = Lerp(a3, b3, sign, fraction);
            double half = IvpMath.Mulsd(
                IvpMath.Addsd(IvpMath.Addsd(IvpMath.Mulsd(w, w), IvpMath.Mulsd(z, z)), IvpMath.Addsd(IvpMath.Mulsd(x, x), IvpMath.Mulsd(y, y))),
                Half);
            double scale = NewtonStart - half;

            scale = IvpMath.Addsd(scale, Half - IvpMath.Mulsd(IvpMath.Mulsd(scale, scale), half));
            scale = IvpMath.Addsd(scale, Half - IvpMath.Mulsd(IvpMath.Mulsd(scale, scale), half));

            return (IvpMath.Mulsd(scale, x), IvpMath.Mulsd(scale, y), IvpMath.Mulsd(scale, z), IvpMath.Mulsd(scale, w));
        }

        double angle = IvpMath.Acos(dot);
        double inverseSine = 1d / Math.Sqrt(1d - IvpMath.Mulsd(dot, dot));
        double fromWeight = IvpMath.Mulsd(IvpMath.Sin(IvpMath.Mulsd(1d - fraction, angle), fused), inverseSine);
        double toWeight = IvpMath.Mulsd(sign, IvpMath.Mulsd(IvpMath.Sin(IvpMath.Mulsd(angle, fraction), fused), inverseSine));

        return (
            Blend(a0, b0, fromWeight, toWeight), Blend(a1, b1, fromWeight, toWeight),
            Blend(a2, b2, fromWeight, toWeight), Blend(a3, b3, fromWeight, toWeight));
    }

    /// <summary>One step's rotation, from an angular velocity — <c>FUN_180071680</c>.</summary>
    /// <param name="angularVelocity">Radians per second about each axis, <c>core+0x130</c>.</param>
    /// <param name="delta">The step, in seconds.</param>
    /// <returns>The rotation to apply.</returns>
    /// <remarks>
    /// <code>
    /// h = dt·0.5;  per lane θ = (float)((double)ω·h);  l = (double)(θ − (θ·θ)·(θ·⅙f))     -- the series in FLOAT
    /// w = √(1 − ((y·y + x·x) + z·z))                                                    -- double, no clamp
    /// </code>
    /// **It is not `sin`**: at a half-angle of 0.5 the series gives 0.47916667 where the sine gives 0.47942554. **Each axis
    /// is built independently**, so this is not a rotation about the combined axis; the real part is whatever unit length
    /// leaves, and a vector part longer than one leaves the NaN `SQRTPD` gives.
    /// </remarks>
    public static (double X, double Y, double Z, double W) Delta((float X, float Y, float Z) angularVelocity, double delta)
    {
        double half = IvpMath.Mulsd(delta, Half);
        double x = Series(angularVelocity.X, half);
        double y = Series(angularVelocity.Y, half);
        double z = Series(angularVelocity.Z, half);

        return (x, y, z, Math.Sqrt(1d - SumOfSquares(x, y, z)));
    }

    /// <summary>One step's rotation by real sines — <c>FUN_180070f50</c>, on the path the runtime chose.</summary>
    /// <param name="angularVelocity">Radians per second about each axis.</param>
    /// <param name="delta">The step, in seconds.</param>
    /// <returns>The rotation to apply.</returns>
    public static (double X, double Y, double Z, double W) SineDelta((float X, float Y, float Z) angularVelocity, double delta) =>
        SineDelta(angularVelocity, delta, IvpMath.FusedPath);

    /// <summary><c>FUN_180070f50</c>, with vphysics' <c>sin</c> on a given path.</summary>
    /// <param name="angularVelocity">Radians per second about each axis.</param>
    /// <param name="delta">The step, in seconds.</param>
    /// <param name="fused">Whether <c>sin</c> takes its fused-multiply-add path.</param>
    /// <returns>The rotation to apply.</returns>
    /// <remarks>
    /// <code>
    /// h = dt·0.5;  per lane l = sin((double)ω·h);  s = (y·y + x·x) + z·z
    /// s > 1 (ordered) → each lane ·= DAT_1800fcea8/√s, and s summed again the same way
    /// w = √(1 − s)
    /// </code>
    /// The rotation step's second route (<see cref="IvpIntegrator.Rotate(IvpRigidBody, float, int, bool)"/>).
    /// </remarks>
    public static (double X, double Y, double Z, double W) SineDelta(
        (float X, float Y, float Z) angularVelocity, double delta, bool fused)
    {
        double half = IvpMath.Mulsd(delta, Half);
        double x = IvpMath.Sin(IvpMath.Mulsd(angularVelocity.X, half), fused);
        double y = IvpMath.Sin(IvpMath.Mulsd(angularVelocity.Y, half), fused);
        double z = IvpMath.Sin(IvpMath.Mulsd(angularVelocity.Z, half), fused);
        double square = SumOfSquares(x, y, z);

        if (square > 1d)
        {
            double scale = SineShrink / Math.Sqrt(square);

            x = IvpMath.Mulsd(x, scale);
            y = IvpMath.Mulsd(y, scale);
            z = IvpMath.Mulsd(z, scale);
            square = SumOfSquares(x, y, z);
        }

        return (x, y, z, Math.Sqrt(1d - square));
    }

    /// <summary>Turns a vector by a rotation.</summary>
    /// <param name="rotation">The orientation, as a unit quaternion.</param>
    /// <param name="vector">The vector, in the rotation's source frame.</param>
    /// <returns>The vector in the rotated frame.</returns>
    /// <remarks>
    /// **The engine does this with a cached 3×3 rather than a quaternion** — `FUN_180037bd0` reads the rows at
    /// `core+0x90..0xe8`, which <see cref="IvpMatrix.FromRotation"/> builds — so this is the same rotation reached by a
    /// different route, in float from the rotation narrowed first, rather than a transcription. The difference is rounding,
    /// not behaviour, but it is a gap and is marked as one.
    /// </remarks>
    public static (float X, float Y, float Z) Rotate(
        (double X, double Y, double Z, double W) rotation, (float X, float Y, float Z) vector)
    {
        float x = (float)rotation.X;
        float y = (float)rotation.Y;
        float z = (float)rotation.Z;
        float w = (float)rotation.W;

        // v + 2w(q × v) + 2q × (q × v), which avoids building the matrix for one vector.
        (float X, float Y, float Z) first = ((y * vector.Z) - (z * vector.Y), (z * vector.X) - (x * vector.Z), (x * vector.Y) - (y * vector.X));
        (float X, float Y, float Z) second = ((y * first.Z) - (z * first.Y), (z * first.X) - (x * first.Z), (x * first.Y) - (y * first.X));

        return (
            vector.X + (2f * ((w * first.X) + second.X)),
            vector.Y + (2f * ((w * first.Y) + second.Y)),
            vector.Z + (2f * ((w * first.Z) + second.Z)));
    }

    /// <summary><c>FUN_180071680</c>'s lane: <c>θ − (θ·θ)·(θ·⅙)</c> in float, from <c>θ = (float)((double)ω·h)</c>.</summary>
    private static double Series(float rate, double half)
    {
        float angle = (float)IvpMath.Mulsd(rate, half);

        return angle - IvpMath.Mulss(IvpMath.Mulss(angle, angle), IvpMath.Mulss(angle, Sixth));
    }

    /// <summary>The vector part's squared length as both delta routines sum it: <c>(y·y + x·x) + z·z</c>.</summary>
    private static double SumOfSquares(double x, double y, double z) =>
        IvpMath.Addsd(IvpMath.Addsd(IvpMath.Mulsd(y, y), IvpMath.Mulsd(x, x)), IvpMath.Mulsd(z, z));

    /// <summary><c>FUN_180071060</c>'s lerp lane: <c>(σ·b − a)·t + a</c>.</summary>
    private static double Lerp(double from, double to, float sign, double fraction) =>
        IvpMath.Addsd(IvpMath.Mulsd(IvpMath.Mulsd(sign, to) - from, fraction), from);

    /// <summary><c>FUN_180071060</c>'s slerp lane: <c>b·wb + wa·a</c>.</summary>
    private static double Blend(double from, double to, double fromWeight, double toWeight) =>
        IvpMath.Addsd(IvpMath.Mulsd(to, toWeight), IvpMath.Mulsd(fromWeight, from));
}
