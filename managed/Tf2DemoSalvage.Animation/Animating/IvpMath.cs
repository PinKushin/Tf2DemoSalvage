using System;
using System.Numerics;
using System.Runtime.Intrinsics.X86;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// The runtime-library math <c>vphysics.dll</c> links into its own image — <c>expf</c>, <c>exp</c>, <c>asinf</c> and
/// <c>atan</c> — ported instruction by instruction (B369).
/// </summary>
/// <remarks>
/// **These are not `MathF.Exp` or `Math.Atan`**, which call whatever library the platform ships. The engine's routines are
/// in its image (`docs/findings/51`, *The math routines, identified*): `FUN_1800d40f0`, `FUN_1800d3cf0`, `FUN_1800d4f9c` and
/// `FUN_1800d4398`, every constant and table dumped.
///
/// **`exp` and `expf` each carry two paths** — one with separate multiplies and adds, one with fused multiply-adds, which
/// round differently — chosen at start-up by `__acrt_initialize_fma3` when the processor has FMA and the operating system
/// saves AVX state. <see cref="FusedPath"/> asks the same question of the processor this runs on, so on any one machine the
/// port takes the path TF2's library takes there.
///
/// **The oracle is Valve's binary**: the `vphysics-math` probe loads the game's `vphysics.dll`, calls these four at their
/// addresses on both paths, and compares every answer with this class bit for bit.
///
/// **The error paths' own handlers are not read.** Where a routine hands an out-of-range argument to one, the answer here
/// is the one the probe observed the binary give.
/// </remarks>
public static class IvpMath
{
    /// <summary>Whether this processor takes the fused-multiply-add paths, as <c>__acrt_initialize_fma3</c> decides.</summary>
    public static bool FusedPath { get; } = Fma.IsSupported;

    /// <summary>
    /// <c>ADDSD destination, source</c>: the sum, and when both are NaN the destination's NaN, quieted — the one thing a C#
    /// <c>+</c> leaves to the JIT, which picks which operand of a commutative operation is the destination (B369).
    /// </summary>
    /// <remarks>
    /// A NaN destination is added to itself, which quiets it and keeps its payload whatever order the JIT emits; otherwise at most
    /// one operand is NaN and the sum carries that one either way. Every IVP port names the binary's destination first, read per
    /// instruction from the disassembly (`docs/findings/51`, *Ported and pinned*).
    /// </remarks>
    internal static double Addsd(double destination, double source) =>
        double.IsNaN(destination) ? destination + destination : destination + source;

    /// <summary><c>MULSD destination, source</c>: the product, with <see cref="Addsd"/>'s NaN rule.</summary>
    internal static double Mulsd(double destination, double source) =>
        double.IsNaN(destination) ? destination * destination : destination * source;

    /// <summary><c>ADDSS destination, source</c>: <see cref="Addsd"/>'s rule in float.</summary>
    internal static float Addss(float destination, float source) =>
        float.IsNaN(destination) ? destination + destination : destination + source;

    /// <summary><c>MULSS destination, source</c>: <see cref="Addsd"/>'s rule in float.</summary>
    internal static float Mulss(float destination, float source) =>
        float.IsNaN(destination) ? destination * destination : destination * source;

    /// <summary><c>1801045a0</c> and <c>180104508</c>: <c>64/ln 2</c>.</summary>
    private static readonly double InverseStep = BitConverter.Int64BitsToDouble(0x40571547652b82fe);

    /// <summary><c>1801045b0</c>: <c>ln 2/64</c>, <c>expf</c>'s single step.</summary>
    private static readonly double SingleStep = BitConverter.Int64BitsToDouble(0x3f862e42fefa39ef);

    /// <summary><c>180104530</c>: <c>−ln 2/64</c>'s high part, <c>exp</c>'s.</summary>
    private static readonly double StepHigh = BitConverter.Int64BitsToDouble(unchecked((long)0xbf862e42fefa0000));

    /// <summary><c>180104538</c>: <c>−ln 2/64</c>'s low part.</summary>
    private static readonly double StepLow = BitConverter.Int64BitsToDouble(unchecked((long)0xbd1cf79abc9e3b39));

    /// <summary><c>180104480</c>: <c>1/720</c>.</summary>
    private static readonly double SeventwentiethInverse = BitConverter.Int64BitsToDouble(0x3f56c16c16c16c17);

    /// <summary><c>180104490</c>: <c>1/120</c>.</summary>
    private static readonly double OneTwentiethInverse = BitConverter.Int64BitsToDouble(0x3f81111111111111);

    /// <summary><c>1801044c0</c>: <c>1/24</c>.</summary>
    private static readonly double TwentyFourthInverse = BitConverter.Int64BitsToDouble(0x3fa5555555555555);

    /// <summary><c>1801044a0</c> and <c>1801045c0</c>: <c>1/6</c>.</summary>
    private static readonly double Sixth = BitConverter.Int64BitsToDouble(0x3fc5555555555555);

    /// <summary><c>1801044f0</c>: <c>exp</c>'s largest argument, <c>709.78</c>.</summary>
    private static readonly double Overflow = BitConverter.Int64BitsToDouble(0x40862e42fefa39ef);

    /// <summary><c>1801044f8</c>: <c>exp</c>'s smallest argument on the table path, <c>−744.03</c>.</summary>
    private static readonly double TableFloor = BitConverter.Int64BitsToDouble(unchecked((long)0xc0874046dfefd9d0));

    /// <summary><c>180104500</c>: at or below it <c>exp</c> underflows to zero, <c>−745.13</c>.</summary>
    private static readonly double Underflow = BitConverter.Int64BitsToDouble(unchecked((long)0xc0874910d52d3051));

    /// <summary><c>180104580</c>: <c>expf</c>'s overflow, on the scaled argument.</summary>
    private const double SingleOverflow = 8192d;

    /// <summary><c>180104590</c>: <c>expf</c>'s underflow, on the scaled argument.</summary>
    private const double SingleUnderflow = -9600d;

    /// <summary><c>180104550</c>: at or under this magnitude <c>exp</c> answers <c>1 + x</c>.</summary>
    private const long TinyMagnitude = 0x3e50000000000000;

    /// <summary><c>180108740</c>: <c>2^(j/64)</c>.</summary>
    private static ReadOnlySpan<long> PowerBits =>
    [
        0x3ff0000000000000, 0x3ff02c9a3e778061, 0x3ff059b0d3158574, 0x3ff0874518759bc8,
        0x3ff0b5586cf9890f, 0x3ff0e3ec32d3d1a2, 0x3ff11301d0125b51, 0x3ff1429aaea92de0,
        0x3ff172b83c7d517b, 0x3ff1a35beb6fcb75, 0x3ff1d4873168b9aa, 0x3ff2063b88628cd6,
        0x3ff2387a6e756238, 0x3ff26b4565e27cdd, 0x3ff29e9df51fdee1, 0x3ff2d285a6e4030b,
        0x3ff306fe0a31b715, 0x3ff33c08b26416ff, 0x3ff371a7373aa9cb, 0x3ff3a7db34e59ff7,
        0x3ff3dea64c123422, 0x3ff4160a21f72e2a, 0x3ff44e086061892d, 0x3ff486a2b5c13cd0,
        0x3ff4bfdad5362a27, 0x3ff4f9b2769d2ca7, 0x3ff5342b569d4f82, 0x3ff56f4736b527da,
        0x3ff5ab07dd485429, 0x3ff5e76f15ad2148, 0x3ff6247eb03a5585, 0x3ff6623882552225,
        0x3ff6a09e667f3bcd, 0x3ff6dfb23c651a2f, 0x3ff71f75e8ec5f74, 0x3ff75feb564267c9,
        0x3ff7a11473eb0187, 0x3ff7e2f336cf4e62, 0x3ff82589994cce13, 0x3ff868d99b4492ed,
        0x3ff8ace5422aa0db, 0x3ff8f1ae99157736, 0x3ff93737b0cdc5e5, 0x3ff97d829fde4e50,
        0x3ff9c49182a3f090, 0x3ffa0c667b5de565, 0x3ffa5503b23e255d, 0x3ffa9e6b5579fdbf,
        0x3ffae89f995ad3ad, 0x3ffb33a2b84f15fb, 0x3ffb7f76f2fb5e47, 0x3ffbcc1e904bc1d2,
        0x3ffc199bdd85529c, 0x3ffc67f12e57d14b, 0x3ffcb720dcef9069, 0x3ffd072d4a07897c,
        0x3ffd5818dcfba487, 0x3ffda9e603db3285, 0x3ffdfc97337b9b5f, 0x3ffe502ee78b3ff6,
        0x3ffea4afa2a490da, 0x3ffefa1bee615a27, 0x3fff50765b6e4540, 0x3fffa7c1819e90d8,
    ];

    /// <summary><c>180107d60</c>: the low part of <c>2^(j/64)</c>, added by <c>exp</c>.</summary>
    private static ReadOnlySpan<long> PowerLowBits =>
    [
        0x0000000000000000, 0x3e6cef00c1dcdef9, 0x3e48ac2ba1d73e2a, 0x3e60eb37901186be,
        0x3e69f3121ec53172, 0x3e469e8d10103a17, 0x3df25b50a4ebbf1a, 0x3e6d525bbf668203,
        0x3e68faa2f5b9bef9, 0x3e66df96ea796d31, 0x3e368b9aa7805b80, 0x3e60c519ac771dd6,
        0x3e6ceac470cd83f5, 0x3e5789f37495e99c, 0x3e547f7b84b09745, 0x3e5b900c2d002475,
        0x3e64636e2a5bd1ab, 0x3e4320b7fa64e430, 0x3e5ceaa72a9c5154, 0x3e53967fdba86f24,
        0x3e682468446b6824, 0x3e3f72e29f84325b, 0x3e18624b40c4dbd0, 0x3e5704f3404f068e,
        0x3e54d8a89c750e5e, 0x3e5a74b29ab4cf62, 0x3e5a753e077c2a0f, 0x3e5ad49f699bb2c0,
        0x3e6a90a852b19260, 0x3e56b48521ba6f93, 0x3e0d2ac258f87d03, 0x3e42a91124893ecf,
        0x3e59fcef32422cbe, 0x3e68ca345de441c5, 0x3e61d8bee7ba46e1, 0x3e59099f22fdba6a,
        0x3e4f580c36bea881, 0x3e5b3d398841740a, 0x3e62999c25159f11, 0x3e668925d901c83b,
        0x3e415506dadd3e2a, 0x3e622aee6c57304e, 0x3e29b8bc9e8a0387, 0x3e6fbc9c9f173d24,
        0x3e451f8480e3e235, 0x3e66bbcac96535b5, 0x3e41f12ae45a1224, 0x3e55e7f6fd0fac90,
        0x3e62b5a75abd0e69, 0x3e609e2bf5ed7fa1, 0x3e47daf237553d84, 0x3e12f074891ee83d,
        0x3e6b0aa538444196, 0x3e6cafa29694426f, 0x3e69df20d22a0797, 0x3e640f12f71a1e45,
        0x3e69f7490e4bb40b, 0x3e4ed9942b84600d, 0x3e4bdcdaf5cb4656, 0x3e5e2cffd89cf44c,
        0x3e452486cc2c7b9d, 0x3e6cc2b44eee3fa4, 0x3e66dc8a80ce9f09, 0x3e39e90d82e90a7e,
    ];

    /// <summary><c>180107b60</c>: the high part of <c>2^(j/64)</c>, added last by <c>exp</c>.</summary>
    private static ReadOnlySpan<long> PowerHighBits =>
    [
        0x3ff0000000000000, 0x3ff02c9a30000000, 0x3ff059b0d0000000, 0x3ff0874510000000,
        0x3ff0b55860000000, 0x3ff0e3ec30000000, 0x3ff11301d0000000, 0x3ff1429aa0000000,
        0x3ff172b830000000, 0x3ff1a35be0000000, 0x3ff1d48730000000, 0x3ff2063b80000000,
        0x3ff2387a60000000, 0x3ff26b4560000000, 0x3ff29e9df0000000, 0x3ff2d285a0000000,
        0x3ff306fe00000000, 0x3ff33c08b0000000, 0x3ff371a730000000, 0x3ff3a7db30000000,
        0x3ff3dea640000000, 0x3ff4160a20000000, 0x3ff44e0860000000, 0x3ff486a2b0000000,
        0x3ff4bfdad0000000, 0x3ff4f9b270000000, 0x3ff5342b50000000, 0x3ff56f4730000000,
        0x3ff5ab07d0000000, 0x3ff5e76f10000000, 0x3ff6247eb0000000, 0x3ff6623880000000,
        0x3ff6a09e60000000, 0x3ff6dfb230000000, 0x3ff71f75e0000000, 0x3ff75feb50000000,
        0x3ff7a11470000000, 0x3ff7e2f330000000, 0x3ff8258990000000, 0x3ff868d990000000,
        0x3ff8ace540000000, 0x3ff8f1ae90000000, 0x3ff93737b0000000, 0x3ff97d8290000000,
        0x3ff9c49180000000, 0x3ffa0c6670000000, 0x3ffa5503b0000000, 0x3ffa9e6b50000000,
        0x3ffae89f90000000, 0x3ffb33a2b0000000, 0x3ffb7f76f0000000, 0x3ffbcc1e90000000,
        0x3ffc199bd0000000, 0x3ffc67f120000000, 0x3ffcb720d0000000, 0x3ffd072d40000000,
        0x3ffd5818d0000000, 0x3ffda9e600000000, 0x3ffdfc9730000000, 0x3ffe502ee0000000,
        0x3ffea4afa0000000, 0x3ffefa1be0000000, 0x3fff507650000000, 0x3fffa7c180000000,
    ];

    /// <summary><c>expf</c> — <c>FUN_1800d40f0</c>, on this machine's path.</summary>
    /// <param name="x">The argument.</param>
    /// <returns><c>e^x</c> as the engine's library answers it.</returns>
    public static float Expf(float x) => Expf(x, FusedPath);

    /// <summary><c>expf</c> — <c>FUN_1800d40f0</c>, on the named path.</summary>
    /// <param name="x">The argument.</param>
    /// <param name="fused">Whether to take the fused-multiply-add path.</param>
    /// <returns><c>e^x</c> as the engine's library answers it on that path.</returns>
    /// <remarks>
    /// <code>
    /// n = x·64/ln 2 rounded to nearest (CVTPD2DQ);  r = x − n·ln 2/64;  j = n &amp; 63;  m = (n − j) &gt;&gt; 6
    /// plain:  p = r²·(r/6 + 0.5) + r;   v = p·T[j] + T[j]
    /// fused:  r fused;  p = fused((fused(r, 1/6, 0.5)), r², r);  v = fused(p, T[j], T[j])
    /// (float)(v · 2^m)
    /// </code>
    /// </remarks>
    public static float Expf(float x, bool fused)
    {
        int raw = BitConverter.SingleToInt32Bits(x);

        if ((raw & int.MaxValue) >= 0x7f800000)
        {
            if (raw == 0x7f800000)
            {
                return x;
            }

            return raw == unchecked((int)0xff800000) ? 0f : BitConverter.Int32BitsToSingle(raw | 0x00400000);
        }

        double wide = x;
        double scaled = wide * InverseStep;

        if (scaled >= SingleOverflow)
        {
            return float.PositiveInfinity;
        }

        if (scaled < SingleUnderflow)
        {
            return 0f;
        }

        int n = (int)Math.Round(scaled, MidpointRounding.ToEven);
        double whole = n;
        int index = n & 0x3f;
        int exponent = (n - index) >> 6;
        double power = BitConverter.Int64BitsToDouble(PowerBits[index]);

        double value;

        if (fused)
        {
            double r = Math.FusedMultiplyAdd(-whole, SingleStep, wide);
            double polynomial = Math.FusedMultiplyAdd(Math.FusedMultiplyAdd(r, Sixth, 0.5d), r * r, r);
            value = Math.FusedMultiplyAdd(polynomial, power, power);
        }
        else
        {
            double r = wide - (SingleStep * whole);
            double polynomial = ((r * r) * ((Sixth * r) + 0.5d)) + r;
            value = (polynomial * power) + power;
        }

        return (float)(value * PowerOfTwo(exponent));
    }

    /// <summary><c>exp</c> — <c>FUN_1800d3cf0</c>, on this machine's path.</summary>
    /// <param name="x">The argument.</param>
    /// <returns><c>e^x</c> as the engine's library answers it.</returns>
    public static double Exp(double x) => Exp(x, FusedPath);

    /// <summary><c>exp</c> — <c>FUN_1800d3cf0</c>, on the named path.</summary>
    /// <param name="x">The argument.</param>
    /// <param name="fused">Whether to take the fused-multiply-add path.</param>
    /// <returns><c>e^x</c> as the engine's library answers it on that path.</returns>
    /// <remarks>
    /// <code>
    /// out of [−744.03, 709.78]:  over it +∞; under it zero, except that the plain path answers the least denormal above −745.13
    /// |x| ≤ 2^−26:  1 + x
    /// n = x·64/ln 2 truncated;  r = n·lo + (x + n·hi);  j = n &amp; 63;  m = n &gt;&gt; 6
    /// plain:  p = ((r/6 + 0.5)·r² + r) + (((r/720 + 1/120)·r) + 1/24)·r⁴
    /// fused:  p = fused(fused(r, fused(r, fused(r, fused(r, 1/720, 1/120), 1/24), 1/6), 0.5), r², r)
    /// v = ((p·T[j]) + Tlow[j]) + Thigh[j];  scaled by 2^m through the exponent bits, or into the denormals
    /// </code>
    /// </remarks>
    public static double Exp(double x, bool fused)
    {
        long raw = BitConverter.DoubleToInt64Bits(x);
        long magnitude = raw & long.MaxValue;

        if (magnitude >= 0x7ff0000000000000)
        {
            if (raw == 0x7ff0000000000000)
            {
                return x;
            }

            return raw == unchecked((long)0xfff0000000000000) ? 0d : BitConverter.Int64BitsToDouble(raw | 0x0008000000000000);
        }

        if (!(x <= Overflow) || x < TableFloor)
        {
            if (x > Overflow)
            {
                return double.PositiveInfinity;
            }

            // The fused path hands everything under the table's floor to the underflow handler; the plain path answers the
            // least denormal between -745.13 and -744.03 itself.
            return fused || x <= Underflow ? 0d : double.Epsilon;
        }

        if (magnitude <= TinyMagnitude)
        {
            return x + 1d;
        }

        int n = (int)Math.Truncate(x * InverseStep);
        double whole = n;
        int index = n & 0x3f;
        int exponent = n >> 6;

        double polynomial;

        if (fused)
        {
            double r = (whole * StepLow) + Math.FusedMultiplyAdd(whole, StepHigh, x);
            double chain = Math.FusedMultiplyAdd(r, SeventwentiethInverse, OneTwentiethInverse);
            chain = Math.FusedMultiplyAdd(r, chain, TwentyFourthInverse);
            chain = Math.FusedMultiplyAdd(r, chain, Sixth);
            chain = Math.FusedMultiplyAdd(r, chain, 0.5d);
            polynomial = Math.FusedMultiplyAdd(chain, r * r, r);
        }
        else
        {
            double r = (whole * StepLow) + (x + (whole * StepHigh));
            double square = r * r;
            double low = ((((SeventwentiethInverse * r) + OneTwentiethInverse) * r) + TwentyFourthInverse) * (square * square);
            polynomial = ((((Sixth * r) + 0.5d) * square) + r) + low;
        }

        double value = ((polynomial * BitConverter.Int64BitsToDouble(PowerBits[index])) +
            BitConverter.Int64BitsToDouble(PowerLowBits[index])) + BitConverter.Int64BitsToDouble(PowerHighBits[index]);

        if (exponent > -1022 || (exponent == -1022 && value >= 1d))
        {
            return BitConverter.Int64BitsToDouble(unchecked(BitConverter.DoubleToInt64Bits(value) + (long)((ulong)(uint)exponent << 52)));
        }

        return value * BitConverter.Int64BitsToDouble(1L << (exponent + 1074));
    }

    /// <summary><c>1801042fc..180104314</c> and neighbours: <c>asinf</c>'s rational function and its constants.</summary>
    private static readonly float SineNumeratorA = BitConverter.Int32BitsToSingle(0x3b81ce6b);

    private static readonly float SineNumeratorB = BitConverter.Int32BitsToSingle(0x3d678bdd);

    private static readonly float SineNumeratorC = BitConverter.Int32BitsToSingle(0x3e3c94dc);

    private static readonly float SineDenominatorSlope = BitConverter.Int32BitsToSingle(0x3f561f0d);

    private static readonly float SineDenominator = BitConverter.Int32BitsToSingle(0x3f8d6fa5);

    private static readonly float SineNumeratorStart = BitConverter.Int32BitsToSingle(unchecked((int)0xbc5b3fe1));

    /// <summary><c>180104814</c>: the high part of <c>π/4</c>.</summary>
    private static readonly float QuarterPiHigh = BitConverter.Int32BitsToSingle(0x3f490fda);

    /// <summary><c>180104810</c>: the low part of <c>π/4</c>.</summary>
    private static readonly float QuarterPiLow = BitConverter.Int32BitsToSingle(0x33a22168);

    /// <summary><c>180101f7c</c>: <c>π/2</c> in float.</summary>
    private static readonly float HalfPiSingle = BitConverter.Int32BitsToSingle(0x3fc90fdb);

    /// <summary><c>asinf</c> — <c>FUN_1800d4f9c</c>.</summary>
    /// <param name="x">The argument.</param>
    /// <returns><c>asin x</c> as the engine's library answers it.</returns>
    /// <remarks>
    /// <code>
    /// |x| under 2^−14: x;  ±1: ±π/2;  over one: the domain error's answer
    /// z = x² under one half, else z = (1 − |x|)·0.5 and s = √z
    /// w = ((((−0.013381929 − z·0.0039613745)·z − 0.05652987)·z + 0.1841616)·z) / (1.1049696 − z·0.8364113)
    /// under one half:  |x|·w + |x|
    /// else, f = s with its low sixteen bits cleared, c = (z − f²)/(f + s):  π/4ʰ − ((2s·w − (π/4ˡ − 2c)) − (π/4ʰ − 2f))
    /// signed like x
    /// </code>
    /// </remarks>
    public static float Asinf(float x)
    {
        int raw = BitConverter.SingleToInt32Bits(x);
        int exponent = (raw >> 23) & 0xff;

        if ((raw & int.MaxValue) > 0x7f800000)
        {
            return BitConverter.Int32BitsToSingle(raw | 0x00400000);
        }

        if (exponent < 0x71)
        {
            return x;
        }

        if (exponent >= 0x7f)
        {
            // UCOMISS against 1.0 and -1.0: for an argument this large, equal means the same bits.
            if (raw == 0x3f800000)
            {
                return HalfPiSingle;
            }

            return raw == unchecked((int)0xbf800000) ? -HalfPiSingle : BitConverter.Int32BitsToSingle(unchecked((int)0xffc00000));
        }

        float magnitude = raw < 0 ? -x : x;
        float root = 0f;
        float z;

        if (exponent >= 0x7e)
        {
            z = (1f - magnitude) * 0.5f;
            root = MathF.Sqrt(z);
            magnitude = root;
        }
        else
        {
            z = magnitude * magnitude;
        }

        float numerator = ((((SineNumeratorStart - (z * SineNumeratorA)) * z) - SineNumeratorB) * z + SineNumeratorC) * z;
        float ratio = numerator / (SineDenominator - (z * SineDenominatorSlope));

        float result;

        if (exponent < 0x7e)
        {
            result = (ratio * magnitude) + magnitude;
        }
        else
        {
            float truncated = BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(root) & unchecked((int)0xffff0000));
            float correction = (z - (truncated * truncated)) / (truncated + root);
            result = QuarterPiHigh - ((((root + root) * ratio) - (QuarterPiLow - (correction + correction))) - (QuarterPiHigh - (truncated * 2f)));
        }

        return raw < 0 ? -result : result;
    }

    /// <summary><c>180104678</c>: over this magnitude <c>atan</c> answers <c>±π/2</c> outright.</summary>
    private const long HugeTangentMagnitude = 0x43d0dc0000000000;

    private static readonly double HalfPi = BitConverter.Int64BitsToDouble(0x3ff921fb54442d18);

    private static readonly double QuarterPi = BitConverter.Int64BitsToDouble(0x3fe921fb54442d18);

    private static readonly double TangentHighThree = BitConverter.Int64BitsToDouble(0x3fef730bd281f69b);

    private static readonly double TangentHighHalf = BitConverter.Int64BitsToDouble(0x3fddac670561bb4f);

    private static readonly double TangentLowHalfPi = BitConverter.Int64BitsToDouble(0x3c91a62633145c06);

    private static readonly double TangentLowThree = BitConverter.Int64BitsToDouble(0x3c7007887af0cbbc);

    private static readonly double TangentLowQuarterPi = BitConverter.Int64BitsToDouble(0x3c81a62633145c06);

    private static readonly double TangentLowHalf = BitConverter.Int64BitsToDouble(0x3c7a2b7f222f65e0);

    private static ReadOnlySpan<long> TangentNumeratorBits =>
        [0x3f22a75ce41b9f87, 0x3f9f2d2116f053f2, 0x3fcc3de43db425c0, 0x3fdca6be4c993b3c, 0x3fd12bcb0a9169f3];

    private static ReadOnlySpan<long> TangentDenominatorBits =>
        [0x3fa3f197f1e85ed9, 0x3fdb2cb05bf9beff, 0x3ff699c644c48d2e, 0x3ffd372a17cdf5a0, 0x3fe9c1b08fda1eec];

    /// <summary><c>atan</c> — <c>FUN_1800d4398</c>.</summary>
    /// <param name="x">The argument.</param>
    /// <returns><c>atan x</c> as the engine's library answers it.</returns>
    /// <remarks>
    /// <code>
    /// a = |x|;  over 43d0dc00…: ±π/2
    /// over 39/16:  t = −1/a, against π/2;   over 19/16:  t = (a − 1.5)/(a·1.5 + 1);   over 11/16:  t = (a − 1)/(a + 1)
    /// over 7/16:   t = ((a + a) − 1)/(a + 2);   otherwise t = a against zero
    /// z = t²;  P and Q nested in z;  atan = hi − (((P·(z·t))/Q − lo) − t), signed like x
    /// </code>
    /// </remarks>
    public static double Atan(double x)
    {
        long raw = BitConverter.DoubleToInt64Bits(x);
        long magnitudeBits = raw & long.MaxValue;
        bool negative = raw != magnitudeBits;
        double magnitude = negative ? -x : x;

        double high;
        double low;
        double t;

        if ((ulong)magnitudeBits > 0x4003800000000000UL)
        {
            if ((ulong)magnitudeBits > 0x7ff0000000000000UL)
            {
                return BitConverter.Int64BitsToDouble(raw | 0x0008000000000000);
            }

            if (magnitude > BitConverter.Int64BitsToDouble(HugeTangentMagnitude))
            {
                return negative ? -HalfPi : HalfPi;
            }

            t = -1d / magnitude;
            high = HalfPi;
            low = TangentLowHalfPi;
        }
        else if ((ulong)magnitudeBits > 0x3ff3000000000000UL)
        {
            t = (magnitude - 1.5d) / ((magnitude * 1.5d) + 1d);
            high = TangentHighThree;
            low = TangentLowThree;
        }
        else if ((ulong)magnitudeBits > 0x3fe6000000000000UL)
        {
            t = (magnitude - 1d) / (magnitude + 1d);
            high = QuarterPi;
            low = TangentLowQuarterPi;
        }
        else if ((ulong)magnitudeBits > 0x3fdc000000000000UL)
        {
            t = ((magnitude + magnitude) - 1d) / (magnitude + 2d);
            high = TangentHighHalf;
            low = TangentLowHalf;
        }
        else
        {
            t = magnitude;
            high = 0d;
            low = 0d;
        }

        double z = t * t;
        double numerator = Nested(TangentNumeratorBits, z);
        double denominator = Nested(TangentDenominatorBits, z);

        double result = high - ((((numerator * (z * t)) / denominator) - low) - t);

        return negative ? -result : result;
    }

    private const long QuarterPiBits = 0x3fe921fb54442d18;
    private const long CosineSmallBits = 0x3f20000000000000;
    private const long CosineTinyBits = 0x3e40000000000000;
    private static readonly double CosineSeventwentieth = BitConverter.Int64BitsToDouble(unchecked((long)0xbf56c16c16c16967));
    private static readonly double CosineFortieth = BitConverter.Int64BitsToDouble(0x3efa01a019f4ec91);
    private static readonly double CosineTenth = BitConverter.Int64BitsToDouble(unchecked((long)0xbe927e4fa17f667b));
    private static readonly double CosineTwelfth = BitConverter.Int64BitsToDouble(0x3e21eeb690382eec);
    private static readonly double CosineFourteenth = BitConverter.Int64BitsToDouble(unchecked((long)0xbda907db47258aa7));

    /// <summary><c>FUN_1800d33b0</c>: <c>cos</c>, on the path the runtime chose.</summary>
    /// <param name="x">The argument.</param>
    /// <returns>The image's bits.</returns>
    public static double Cos(double x) => Cos(x, FusedPath);

    /// <summary><c>FUN_1800d33b0</c> on a given path.</summary>
    /// <param name="x">The argument.</param>
    /// <param name="fused">Whether to take the fused-multiply-add path.</param>
    /// <returns>The image's bits.</returns>
    /// <remarks>
    /// <code>
    /// |x| under 2^−27: 1;  under 2^−13: 1 − (x·x)·0.5 (fused: 1 − (x·0.5)·x);  under π/4: the series `180105f20..f70` in z = x²
    /// otherwise, infinity or NaN → the handler (infinity: the default NaN; NaN: quieted)
    ///     y = x reduced by π/2 (plain: inline under 500000, else FUN_1800daa70;  fused: FUN_1800dafb0 under 2·10⁷, else
    ///     FUN_1800dadc0), n its quadrant;  n even → the cosine kernel, odd → the sine kernel;  (n + 1) &amp; 2 → negated
    /// </code>
    /// Plain π/4 itself goes to the reduction (<c>JC</c>); fused π/4 stays on the series (<c>JG</c>). **The plain path negates by
    /// subtracting from zero, the fused by flipping the sign bit**, so the two answer a zero's sign differently.
    /// </remarks>
    public static double Cos(double x, bool fused)
    {
        long bits = BitConverter.DoubleToInt64Bits(x);
        long magnitude = bits & long.MaxValue;
        bool reduced = fused ? magnitude > QuarterPiBits : magnitude >= QuarterPiBits;

        if (reduced)
        {
            if (magnitude >= InfinityBits)
            {
                return NotFinite(bits);
            }

            (int quadrant, double high, double low) = Reduce(Math.Abs(x), magnitude, fused);
            double value = (quadrant & 1) == 0 ? CosineKernel(high, low, fused) : SineKernel(high, low, fused);

            return ((quadrant + 1) & 2) == 0 ? value : Negate(value, fused);
        }

        if (magnitude < CosineTinyBits)
        {
            return 1d;
        }

        if (magnitude < CosineSmallBits)
        {
            return fused ? Math.FusedMultiplyAdd(-(x * 0.5d), x, 1d) : 1d - (x * x * 0.5d);
        }

        double z = x * x;

        if (fused)
        {
            double r = Math.FusedMultiplyAdd(CosineFourteenth, z, CosineTwelfth);
            r = Math.FusedMultiplyAdd(r, z, CosineTenth);
            r = Math.FusedMultiplyAdd(r, z, CosineFortieth);
            r = Math.FusedMultiplyAdd(r, z, CosineSeventwentieth);
            r = Math.FusedMultiplyAdd(r, z, TwentyFourthInverse);
            r = Math.FusedMultiplyAdd(r, z, -0.5d);
            return Math.FusedMultiplyAdd(r, z, 1d);
        }

        // w carries the rounding of 1 − z/2 in e, as the plain path adds them back last.
        double squared = z * z;
        double fourth = squared * squared;
        double series = ((((CosineSeventwentieth * z) + TwentyFourthInverse) * squared) + (((CosineTenth * z) + CosineFortieth) * fourth)) +
                        (squared * fourth * ((CosineFourteenth * z) + CosineTwelfth));
        double t = z * -0.5d;
        double w = t + 1d;
        double e = (1d - w) + t;

        return (e + series) + w;
    }

    /// <summary><c>FUN_1800c8020</c>: <c>sin</c>, on the path the runtime chose.</summary>
    /// <param name="x">The argument.</param>
    /// <returns>The image's bits.</returns>
    public static double Sin(double x) => Sin(x, FusedPath);

    /// <summary><c>FUN_1800c8020</c> on a given path.</summary>
    /// <param name="x">The argument.</param>
    /// <param name="fused">Whether to take the fused-multiply-add path.</param>
    /// <returns>The image's bits.</returns>
    /// <remarks>
    /// <code>
    /// plain:  |x| under π/4:  at most 2^−27 → x;  else x + (x·z)·A,  A = ((c₅z + c₄)z + c₃)·z³ + ((c₂z + c₁)z + c₀)
    /// fused:  |x| under π/4:  under 2^−27 → x;  under 2^−13 → x − (x·x·x)·⅙;  else the same series in fused steps
    /// otherwise, infinity or NaN → the handler;  y = |x| reduced, n its quadrant;  n even → the sine kernel, odd → the cosine
    ///     kernel;  negated when bit 1 of n differs from x's sign (plain: from zero; fused: the sign bit)
    /// </code>
    /// **The plain path compares its tiny bound with `JG` and its π/4 with `JC`, the fused path both with `JGE`/`JNC`**, so each
    /// bound belongs to a different side on each path.
    /// </remarks>
    public static double Sin(double x, bool fused)
    {
        long bits = BitConverter.DoubleToInt64Bits(x);
        long magnitude = bits & long.MaxValue;

        if (magnitude < QuarterPiBits)
        {
            if (fused)
            {
                if (magnitude >= CosineSmallBits)
                {
                    double z = x * x;
                    double p = Math.FusedMultiplyAdd(z, SineCoefficient(5), SineCoefficient(4));
                    p = Math.FusedMultiplyAdd(z, p, SineCoefficient(3));
                    p = Math.FusedMultiplyAdd(z, p, SineCoefficient(2));
                    p = Math.FusedMultiplyAdd(z, p, SineCoefficient(1));
                    double t = x * z;
                    p = Math.FusedMultiplyAdd(z, p, SineCoefficient(0));

                    return Math.FusedMultiplyAdd(t, p, x);
                }

                if (magnitude >= CosineTinyBits)
                {
                    double cube = x * x * x;

                    return Math.FusedMultiplyAdd(-cube, Sixth, x);
                }

                return x;
            }

            if (magnitude <= CosineTinyBits)
            {
                return x;
            }

            double square = x * x;

            return x + ((x * square) * SineSeries(square));
        }

        if (magnitude >= InfinityBits)
        {
            return NotFinite(bits);
        }

        (int quadrant, double high, double low) = Reduce(Math.Abs(x), magnitude, fused);
        double value = (quadrant & 1) == 0 ? SineKernel(high, low, fused) : CosineKernel(high, low, fused);
        bool flip = ((quadrant >> 1) & 1) != (bits < 0 ? 1 : 0);

        return flip ? Negate(value, fused) : value;
    }

    /// <summary><c>FUN_1800cce64</c>: <c>acos</c>, which has one path.</summary>
    /// <param name="x">The argument.</param>
    /// <returns>The image's bits.</returns>
    /// <remarks>
    /// <code>
    /// NaN: quieted;  |x| under 2^−56: π/2;  1: +0;  −1: π;  otherwise at or over one: the domain handler's default NaN
    /// z = x² under one half, else z = (1 − |x|)·0.5 and s = √z;   r = (p(z)·z) / q(z), `180102bf8..c48`
    /// under one half:  π/2 − (x − (π/2ˡ − r·x))
    /// negative:        π − 2·((r·s − π/2ˡ) + s)
    /// positive:        f = s with its low 32 bits cleared, c = (z − f²)/(f + s):  ((2s·r) + 2c) + f·2
    /// </code>
    /// </remarks>
    public static double Acos(double x)
    {
        long bits = BitConverter.DoubleToInt64Bits(x);
        long magnitude = bits & long.MaxValue;
        int exponent = (int)((bits >> 52) & 0x7ff);

        if (magnitude > InfinityBits)
        {
            return BitConverter.Int64BitsToDouble(bits | QuietBit);
        }

        if (exponent < 0x3c7)
        {
            return HalfPi;
        }

        if (exponent >= 0x3ff)
        {
            // UCOMISD against 1.0 and −1.0: for an argument this large, equal means the same bits.
            if (bits == 0x3ff0000000000000)
            {
                return 0d;
            }

            return bits == unchecked((long)0xbff0000000000000) ? Pi : BitConverter.Int64BitsToDouble(DefaultNaNBits);
        }

        double absolute = bits < 0 ? -x : x;
        double root = 0d;
        double z;

        if (exponent >= 0x3fe)
        {
            z = (1d - absolute) * 0.5d;
            root = Math.Sqrt(z);
            absolute = root;
        }
        else
        {
            z = absolute * absolute;
        }

        ReadOnlySpan<long> top = AcosNumeratorBits;
        ReadOnlySpan<long> bottom = AcosDenominatorBits;
        double numerator = ((((((((((z * Bits(top[0])) + Bits(top[1])) * z) - Bits(top[2])) * z) + Bits(top[3])) * z) - Bits(top[4])) * z) + Bits(top[5])) * z;
        double denominator = ((((((z * Bits(bottom[0])) - Bits(bottom[1])) * z) + Bits(bottom[2])) * z) - Bits(bottom[3])) * z + Bits(bottom[4]);
        double ratio = numerator / denominator;

        if (exponent < 0x3fe)
        {
            return HalfPi - (x - (AcosHalfPiLow - (ratio * x)));
        }

        if (bits < 0)
        {
            double twice = ((ratio * absolute) - AcosHalfPiLow) + root;

            return Pi - (twice + twice);
        }

        double truncated = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(root) & unchecked((long)0xffffffff00000000));
        double correction = (z - (truncated * truncated)) / (truncated + root);

        return (((root + root) * ratio) + (correction + correction)) + (truncated * 2d);
    }

    /// <summary><c>FUN_1800db050</c> and <c>FUN_1800db028</c>: an infinity's answer is the default NaN, a NaN's is itself quieted.</summary>
    private static double NotFinite(long bits) =>
        BitConverter.Int64BitsToDouble((bits & 0xfffffffffffff) == 0 ? DefaultNaNBits : bits | QuietBit);

    /// <summary>A reduced result's sign flipped: the plain paths subtract from zero, the fused flip the sign bit.</summary>
    private static double Negate(double value, bool fused) =>
        fused ? BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(value) ^ long.MinValue) : 0d - value;

    /// <summary>The sine's <c>A</c>: <c>((c₅z + c₄)z + c₃)·z³ + ((c₂z + c₁)z + c₀)</c>, as both plain routines add it.</summary>
    private static double SineSeries(double z)
    {
        double cube = z * z * z;
        double upper = (((SineCoefficient(5) * z) + SineCoefficient(4)) * z) + SineCoefficient(3);
        double lower = (((SineCoefficient(2) * z) + SineCoefficient(1)) * z) + SineCoefficient(0);

        return (upper * cube) + lower;
    }

    /// <summary>The sine of a reduced argument <c>y₀ + y₁</c>.</summary>
    private static double SineKernel(double high, double low, bool fused)
    {
        double z = high * high;

        if (fused)
        {
            double p = Math.FusedMultiplyAdd(z, SineCoefficient(5), SineCoefficient(4));
            p = Math.FusedMultiplyAdd(z, p, SineCoefficient(3));
            p = Math.FusedMultiplyAdd(z, p, SineCoefficient(2));
            p = Math.FusedMultiplyAdd(z, p, SineCoefficient(1));
            double t = high * z;
            double u = (low * 0.5d) - (t * p);
            u = (z * u) - low;
            u = Math.FusedMultiplyAdd(-t, SineCoefficient(0), u);

            return high - u;
        }

        double scaled = (high * z) * SineSeries(z);

        return (low + (scaled - ((z * 0.5d) * low))) + high;
    }

    /// <summary>The cosine of a reduced argument <c>y₀ + y₁</c>.</summary>
    private static double CosineKernel(double high, double low, bool fused)
    {
        double z = high * high;

        if (fused)
        {
            double half = z * 0.5d;
            double w = 1d - half;
            double e = Math.FusedMultiplyAdd(-high, low, (1d - w) - half);
            double squared = z * z;
            double p = Math.FusedMultiplyAdd(z, CosineFourteenth, CosineTwelfth);
            p = Math.FusedMultiplyAdd(z, p, CosineTenth);
            p = Math.FusedMultiplyAdd(z, p, CosineFortieth);
            p = Math.FusedMultiplyAdd(z, p, CosineSeventwentieth);
            p = Math.FusedMultiplyAdd(z, p, TwentyFourthInverse);

            return Math.FusedMultiplyAdd(squared, p, e) + w;
        }

        double product = low * high;
        double upper = (((CosineFourteenth * z) + CosineTwelfth) * z) + CosineTenth;
        double lower = (((CosineFortieth * z) + CosineSeventwentieth) * z) + TwentyFourthInverse;
        double cube = z * z * z;
        double halfZ = z * 0.5d;
        double carried = ((((0.5d * z) - 1d) + 1d) - halfZ) - product;
        double series = (lower + (upper * cube)) * (z * z);

        return (series + carried) - (halfZ - 1d);
    }

    /// <summary><c>|x|</c> reduced by π/2 on a path: its quadrant and the two halves of the remainder.</summary>
    private static (int Quadrant, double High, double Low) Reduce(double magnitude, long magnitudeBits, bool fused)
    {
        if (fused)
        {
            return magnitudeBits >= FusedReductionLimitBits ? ReduceLarge(magnitude, fused: true) : ReduceFused(magnitude);
        }

        return magnitudeBits >= PlainReductionLimitBits ? ReduceLarge(magnitude, fused: false) : ReducePlain(magnitude, magnitudeBits);
    }

    /// <summary>
    /// The plain routines' inline reduction under 500000: <c>n = (int)(|x|·2/π + 0.5)</c>, the remainder taken against a
    /// 33-bit <c>π/2</c> and its tail, and against the next 33 bits when the first leaves more than 15 bits of cancellation.
    /// </summary>
    private static (int Quadrant, double High, double Low) ReducePlain(double magnitude, long magnitudeBits)
    {
        int n = (int)((magnitude * TwoOverPi) + 0.5d);
        double count = n;
        double remainder = magnitude - (PiOverTwoFirst * count);
        double tail = PiOverTwoFirstTail * count;
        double high = remainder - tail;
        long gap = (magnitudeBits >>> 52) - (long)(((ulong)BitConverter.DoubleToInt64Bits(high) << 1) >> 53);

        if (gap > 15)
        {
            double previous = remainder;
            double second = PiOverTwoSecond * count;

            tail = PiOverTwoSecondTail * count;
            remainder = previous - second;
            tail -= (previous - remainder) - second;
            high = remainder - tail;
        }

        return (n, high, (remainder - high) - tail);
    }

    /// <summary><c>FUN_1800dafb0</c>: the fused reduction under 2·10⁷, <c>n</c> rounded by adding and taking away 2⁵².</summary>
    private static (int Quadrant, double High, double Low) ReduceFused(double magnitude)
    {
        double count = Math.FusedMultiplyAdd(magnitude, TwoOverPi, TwoToThe52) - TwoToThe52;
        int n = (int)count;
        double remainder = Math.FusedMultiplyAdd(-count, HalfPi, magnitude);
        double product = count * FusedHalfPiLow;
        double error = Math.FusedMultiplyAdd(FusedHalfPiLow, count, -product);
        double difference = remainder - product;
        double lost = (remainder - difference) - product;
        double high = Math.FusedMultiplyAdd(-count, FusedHalfPiLow, remainder);
        double rest = ((difference - high) + lost) - error;

        return (n & 3, high, Math.FusedMultiplyAdd(-count, FusedHalfPiLowest, rest));
    }

    /// <summary>
    /// <c>FUN_1800daa70</c> (plain) and <c>FUN_1800dadc0</c> (fused): the mantissa multiplied by 2/π's bits from
    /// <c>180106100</c>, the quadrant rounded out of the top, the fraction normalised into two doubles and multiplied back by π/2.
    /// </summary>
    private static (int Quadrant, double High, double Low) ReduceLarge(double magnitude, bool fused)
    {
        ulong bits = (ulong)BitConverter.DoubleToInt64Bits(magnitude);
        long exponent = (long)(bits >> 52) - 0x3ff;
        int at = (int)(0x86 - (exponent >> 3));
        ulong mantissa = ((bits << 12) >> 12) | (1UL << 52);
        ReadOnlySpan<byte> table = TwoOverPiBytes;

        ulong carryHigh = Math.BigMul(ReadUInt64(table, at), mantissa, out ulong lowest);
        ulong middleHigh = Math.BigMul(ReadUInt64(table, at + 8), mantissa, out ulong middleLow);
        ulong middle = middleLow + carryHigh;
        ulong top = middleHigh + (middle < carryHigh ? 1UL : 0UL) + (ReadUInt64(table, at + 16) * mantissa);

        int fraction = (int)(exponent & 7);
        int shift = 54 - fraction;
        bool roundUp = ((top >> (shift - 1)) & 1) != 0;
        ulong sign = 0;

        int quadrant = (int)(((top >> shift) + (roundUp ? 1UL : 0UL)) & 3);

        if (roundUp)
        {
            top = ~top;
            middle = ~middle;
            lowest = ~lowest;
            sign = 1UL << 63;
        }

        int keep = fraction + 10;
        top = (top << keep) >> keep;
        long scale = keep - 64;
        long leading = scale;

        if (top != 0)
        {
            leading = 63 - BitOperations.LeadingZeroCount(top);
        }
        else
        {
            top = middle;
            middle = lowest;
            lowest = 0;

            if (top != 0)
            {
                leading = 63 - BitOperations.LeadingZeroCount(top);
            }

            scale -= 64;
        }

        scale += leading;
        long move = leading - 52;

        if (move < 0)
        {
            int left = (int)-move;
            ulong carried = middle;

            top <<= left;
            middle <<= left;
            carried >>= 64 - left;
            top |= carried;
            lowest >>= 64 - left;
            middle |= lowest;
        }
        else if (move > 0)
        {
            int right = (int)move;
            ulong carried = top;

            top >>= right;
            middle >>= right;
            carried <<= 64 - right;
            middle |= carried;
        }

        scale += 0x3ff;

        ulong fractionBits = (top & ~(1UL << 52)) | sign | ((ulong)scale << 52);
        double first = BitConverter.Int64BitsToDouble((long)fractionBits);
        long nextLeading = middle != 0 ? 63 - BitOperations.LeadingZeroCount(middle) : 0;
        long nextShift = 64 - nextLeading;

        middle <<= (int)nextShift;
        middle >>= 12;
        nextShift += 52;

        ulong nextBits = middle | sign | ((ulong)(scale - nextShift) << 52);
        double second = BitConverter.Int64BitsToDouble((long)nextBits);
        double firstHigh = BitConverter.Int64BitsToDouble((long)((fractionBits >> 27) << 27));
        double firstLow = first - firstHigh;

        if (fused)
        {
            double product = first * HalfPi;
            double sum = (firstHigh * ReductionHalfPiHigh) - product;
            sum = Math.FusedMultiplyAdd(firstLow, ReductionHalfPiHigh, sum);
            sum = Math.FusedMultiplyAdd(firstHigh, ReductionHalfPiMiddle, sum);
            sum = Math.FusedMultiplyAdd(firstLow, ReductionHalfPiMiddle, sum);
            double tail = Math.FusedMultiplyAdd(first, ReductionHalfPiLow, second * HalfPi);
            sum += tail;
            double fusedHigh = product + sum;

            return (quadrant, fusedHigh, (product - fusedHigh) + sum);
        }

        double scaled = first * HalfPi;
        double scaledLow = first * ReductionHalfPiLow;
        double next = second * HalfPi;
        double total = (ReductionHalfPiHigh * firstHigh) - scaled;
        total += ReductionHalfPiHigh * firstLow;
        double carriedTail = scaledLow + next;
        total += ReductionHalfPiMiddle * firstHigh;
        total += ReductionHalfPiMiddle * firstLow;
        total += carriedTail;
        double high = scaled + total;

        return (quadrant, high, total + (scaled - high));
    }

    private static ulong ReadUInt64(ReadOnlySpan<byte> table, int at) => BitConverter.ToUInt64(table.Slice(at, 8));

    private static double Bits(long bits) => BitConverter.Int64BitsToDouble(bits);

    private static double SineCoefficient(int index) => Bits(SineCoefficientBits[index]);

    private const long InfinityBits = 0x7ff0000000000000;
    private const long QuietBit = 0x0008000000000000;
    private const long DefaultNaNBits = unchecked((long)0xfff8000000000000);
    private const long PlainReductionLimitBits = 0x411e848000000000;
    private const long FusedReductionLimitBits = 0x417312d000000000;
    private const double TwoToThe52 = 4503599627370496d;

    /// <summary><c>180102030</c>: <c>2/π</c>.</summary>
    private static readonly double TwoOverPi = BitConverter.Int64BitsToDouble(0x3fe45f306dc9c883);

    /// <summary><c>180101fa0</c>: <c>π/2</c>'s first 33 bits.</summary>
    private static readonly double PiOverTwoFirst = BitConverter.Int64BitsToDouble(0x3ff921fb54400000);

    /// <summary><c>180101fb0</c>: what is left of <c>π/2</c> after them.</summary>
    private static readonly double PiOverTwoFirstTail = BitConverter.Int64BitsToDouble(0x3dd0b4611a626331);

    /// <summary><c>180101fc0</c>: the next 33 bits.</summary>
    private static readonly double PiOverTwoSecond = BitConverter.Int64BitsToDouble(0x3dd0b4611a600000);

    /// <summary><c>180101fd0</c>: what is left after those.</summary>
    private static readonly double PiOverTwoSecondTail = BitConverter.Int64BitsToDouble(0x3ba3198a2e037073);

    /// <summary><c>180105fe8</c> and <c>1801061e0</c>: <c>π/2</c>'s low part.</summary>
    private static readonly double ReductionHalfPiLow = BitConverter.Int64BitsToDouble(0x3c91a62633145c06);

    /// <summary><c>180105ff0</c> and <c>1801061c0</c>: <c>π/2</c> to 30 bits.</summary>
    private static readonly double ReductionHalfPiHigh = BitConverter.Int64BitsToDouble(0x3ff921fb50000000);

    /// <summary><c>180106000</c> and <c>1801061d0</c>: the bits after those.</summary>
    private static readonly double ReductionHalfPiMiddle = BitConverter.Int64BitsToDouble(0x3e5110b460000000);

    /// <summary><c>1801062d0</c>: the fused reduction's low <c>π/2</c>.</summary>
    private static readonly double FusedHalfPiLow = BitConverter.Int64BitsToDouble(0x3c91a62633145c00);

    /// <summary><c>1801062e0</c>: and its lowest.</summary>
    private static readonly double FusedHalfPiLowest = BitConverter.Int64BitsToDouble(0x397b839a252049c0);

    /// <summary><c>1800ed7a0</c>: <c>π</c>.</summary>
    private static readonly double Pi = BitConverter.Int64BitsToDouble(0x400921fb54442d18);

    /// <summary><c>180101538</c>: <c>acos</c>'s low <c>π/2</c>, one above the reductions'.</summary>
    private static readonly double AcosHalfPiLow = BitConverter.Int64BitsToDouble(0x3c91a62633145c07);

    /// <summary><c>180105f80..fd0</c>: the sine series.</summary>
    private static ReadOnlySpan<long> SineCoefficientBits =>
        [unchecked((long)0xbfc5555555555555), 0x3f81111111110bb3, unchecked((long)0xbf2a01a019e83e5c), 0x3ec71de3796cde01, unchecked((long)0xbe5ae600b42fdfa7), 0x3de5e0b2f9a43bb8];

    /// <summary><c>180102bf8</c>, <c>c00</c>, <c>c08</c>, <c>c20</c>, <c>c28</c>, <c>c18</c>: <c>acos</c>'s numerator, in the order it takes them.</summary>
    private static ReadOnlySpan<long> AcosNumeratorBits =>
        [0x3f0951665d321061, 0x3f51e5f887a62135, 0x3fac28d390c29690, 0x3fd1a2bec1b7ef59, 0x3fdc7b297e269eac, 0x3fcd1e4180029834];

    /// <summary><c>180102c10</c>, <c>c30</c>, <c>c40</c>, <c>c48</c>, <c>c38</c>: its denominator.</summary>
    private static ReadOnlySpan<long> AcosDenominatorBits =>
        [0x3fbb1a422982ce76, 0x3fee324ab418f78d, 0x40062021571dccfc, 0x400a4646f903cdea, 0x3ff5d6b12001f228];

    /// <summary><c>180106100</c> (and its copy at <c>180107fd0</c>): 2/π's bits, read by byte offset.</summary>
    private static ReadOnlySpan<byte> TwoOverPiBytes =>
    [
        0xe0, 0xf1, 0x1b, 0xc1, 0x0c, 0x58, 0x21, 0x74, 0x35, 0x7e, 0xc4, 0x7e, 0xed, 0xaf, 0xa9, 0x4b,
        0x4a, 0x29, 0xde, 0xe7, 0x1c, 0xf4, 0xec, 0xc5, 0x97, 0xaf, 0x1f, 0xeb, 0x9e, 0xd4, 0xb5, 0xa8,
        0x7f, 0x79, 0x9a, 0xfd, 0x18, 0x3d, 0xdd, 0x26, 0x2c, 0x9f, 0x3c, 0xfb, 0xd9, 0xb4, 0x7d, 0xb4,
        0x29, 0x68, 0x2d, 0x46, 0xbc, 0xbc, 0x3f, 0x60, 0x16, 0x78, 0xff, 0x5f, 0xe2, 0x7f, 0xec, 0xa0,
        0xe4, 0xf7, 0x2e, 0x7e, 0x11, 0x72, 0xd2, 0xe7, 0x4c, 0x0d, 0xe6, 0x58, 0x47, 0xe6, 0x04, 0xf9,
        0x7d, 0xd1, 0x9a, 0xc0, 0x71, 0xa6, 0x13, 0x12, 0xed, 0xba, 0xd4, 0xd7, 0x08, 0xa2, 0xfb, 0x9c,
        0xa6, 0xc4, 0x72, 0xac, 0x77, 0xf8, 0x73, 0x48, 0x46, 0x27, 0xa8, 0xbb, 0x24, 0x19, 0x80, 0x4b,
        0x37, 0x09, 0xe9, 0xb8, 0x91, 0xdc, 0x86, 0x15, 0xef, 0x7a, 0xaf, 0x8e, 0x45, 0xf9, 0x07, 0x41,
        0x0e, 0xf1, 0x64, 0x56, 0x8a, 0x6d, 0x03, 0x77, 0xd3, 0xd4, 0x47, 0x5f, 0x9d, 0xf0, 0xa7, 0x54,
        0x10, 0x39, 0xb9, 0x0d, 0xe6, 0x8b, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x18, 0x2d, 0x44, 0x54, 0xfb, 0x21, 0xf9, 0x3f, 0x18, 0x2d, 0x44, 0x54, 0xfb, 0x21, 0xf9, 0x3f,
    ];

    /// <summary><c>((((z·c₀ + c₁)·z + c₂)·z + c₃)·z + c₄)</c>, each step a multiply then an add.</summary>
    private static double Nested(ReadOnlySpan<long> coefficients, double z)
    {
        double value = (z * BitConverter.Int64BitsToDouble(coefficients[0])) + BitConverter.Int64BitsToDouble(coefficients[1]);

        for (int index = 2; index < coefficients.Length; index++)
        {
            value = (value * z) + BitConverter.Int64BitsToDouble(coefficients[index]);
        }

        return value;
    }

    /// <summary><c>2^m</c> as the routines build it: the biased exponent written into a double's bits.</summary>
    private static double PowerOfTwo(int exponent) =>
        BitConverter.Int64BitsToDouble(unchecked((long)(((ulong)(uint)exponent + 0x3ffUL) << 52)));
}
