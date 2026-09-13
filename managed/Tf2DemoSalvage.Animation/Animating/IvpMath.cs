using System;
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
    private static readonly double CosineTwelfth = BitConverter.Int64BitsToDouble(0x3e21eeb69037ec2e);
    private static readonly double CosineFourteenth = BitConverter.Int64BitsToDouble(unchecked((long)0xbda907db47258aa7));

    /// <summary><c>FUN_1800d33b0</c>: <c>cos</c>, on the path the runtime chose.</summary>
    /// <param name="x">The argument, under π/4 in magnitude.</param>
    /// <returns>The image's bits.</returns>
    public static double Cos(double x) => Cos(x, FusedPath);

    /// <summary><c>FUN_1800d33b0</c> on a given path — the series `180105f20..f70` in `z = x²`, under π/4.</summary>
    /// <param name="x">The argument.</param>
    /// <param name="fused">Whether to take the fused-multiply-add path.</param>
    /// <returns>The image's bits.</returns>
    /// <exception cref="NotSupportedException">|x| is π/4 or more, or NaN: the reduction through <c>FUN_1800daa70</c> is not ported.</exception>
    /// <remarks>Plain π/4 itself goes to the reduction (<c>JC</c>); fused π/4 stays on the series (<c>JG</c>).</remarks>
    public static double Cos(double x, bool fused)
    {
        long magnitude = BitConverter.DoubleToInt64Bits(x) & long.MaxValue;
        bool reduced = fused ? magnitude > QuarterPiBits : magnitude >= QuarterPiBits;

        if (reduced)
        {
            throw new NotSupportedException("IvpMath.Cos ports FUN_1800d33b0 under pi/4 only; its reduction, FUN_1800daa70, is not ported.");
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
