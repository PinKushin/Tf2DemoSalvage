using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// The runtime-library math <c>vphysics.dll</c> carries in its own image, pinned to the bits Valve's binary answers (B369).
/// </summary>
/// <remarks>
/// **Every expectation below is differential evidence, not a reading**: the `vphysics-math` probe loaded the game's x64
/// `vphysics.dll` on 2026-09-13, called `FUN_1800d40f0`, `FUN_1800d3cf0`, `FUN_1800d4f9c` and `FUN_1800d4398` at their
/// addresses — on both of `exp`'s paths, the path flag written in the loaded image — and printed each answer's bits. Its
/// sweep then compared <see cref="IvpMath"/> with the binary over 1.43 million `expf` and `asinf` arguments, three million
/// `exp` arguments on each path and two million `atan` arguments, and found no difference.
///
/// **`exp`'s two paths are told apart by arguments the sweep found**: the binary's paths disagree on 3,641 of its three
/// million, `−2.997306` among them. **`expf`'s never disagreed**, over 1.43 million arguments, so no test here can tell its
/// paths apart; both are run, and they must agree.
///
/// **`cos`, `sin` and `acos` followed on 2026-09-14** — `FUN_1800d33b0`, `FUN_1800c8020` and `FUN_1800cce64` — over six
/// million `cos` and `sin` arguments and four million `acos` arguments on each path, none differing; the binary's own paths
/// disagree on 44,881 of the `cos` arguments and 44,510 of the `sin`.
/// </remarks>
public sealed class IvpMathConformanceTests
{
    /// <remarks>Arguments through the table, both overflows, both infinities and a tiny argument, on each path.</remarks>
    [TestCase(unchecked((int)0xbdcccccd), 0x3f67a36d, false)]
    [TestCase(unchecked((int)0xbdcccccd), 0x3f67a36d, true)]
    [TestCase(unchecked((int)0xbf000000), 0x3f1b4598, false)]
    [TestCase(unchecked((int)0xbf000000), 0x3f1b4598, true)]
    [TestCase(unchecked((int)0xbf800000), 0x3ebc5ab2, true)]
    [TestCase(unchecked((int)0xc03fd3dd), 0x3d4c7a5a, true)]
    [TestCase(unchecked((int)0xba83126f), 0x3f7fbe7f, false)]
    [TestCase(0x40a00000, 0x431469c5, false)]
    [TestCase(0x40a00000, 0x431469c5, true)]
    [TestCase(0x447a0000, 0x7f800000, true)]
    [TestCase(unchecked((int)0xc47a0000), 0x00000000, true)]
    [TestCase(0x7f800000, 0x7f800000, false)]
    [TestCase(unchecked((int)0xff800000), 0x00000000, false)]
    [TestCase(0x0da24260, 0x3f800000, true)]
    public void Expf_AnArgumentTheBinaryWasAsked_AnswersItsBits(int argument, int expected, bool fused) =>
        Bits(IvpMath.Expf(System.BitConverter.Int32BitsToSingle(argument), fused)).ShouldBe(expected);

    /// <remarks>
    /// **`−2.997306` is where the two paths part**: the plain path answers `…eb17`, the fused one `…eb16`. And between
    /// `−745.13` and `−744.03` the plain path answers the least denormal itself while the fused one hands the argument to
    /// its underflow handler and answers zero — a difference the sweep found in the port before it was fixed.
    /// </remarks>
    [TestCase(unchecked((long)0xc007fa7b9170d62c), 0x3fa98f4b66ceeb17, false)]
    [TestCase(unchecked((long)0xc007fa7b9170d62c), 0x3fa98f4b66ceeb16, true)]
    [TestCase(unchecked((long)0xc08742d78d5b76dc), 0x0000000000000001, false)]
    [TestCase(unchecked((long)0xc08742d78d5b76dc), 0x0000000000000000, true)]
    [TestCase(unchecked((long)0xc0874c0000000000), 0x0000000000000000, false)]
    [TestCase(0x4086280000000000, 0x7fdd422d2be5dc9b, false)]
    [TestCase(0x4086280000000000, 0x7fdd422d2be5dc9b, true)]
    [TestCase(0x4086300000000000, 0x7ff0000000000000, true)]
    [TestCase(unchecked((long)0xbfe8000000000000), 0x3fde3b40ebefcd7e, false)]
    [TestCase(unchecked((long)0xbfe8000000000000), 0x3fde3b40ebefcd7e, true)]
    [TestCase(unchecked((long)0xbf50624dd2f1a9fc), 0x3feff7cfe56f1a9e, true)]
    [TestCase(unchecked((long)0xc085e00000000000), 0x00d14f2b0fb9307f, false)]
    [TestCase(0x39b4484bfeebc2a0, 0x3ff0000000000000, true)]
    [TestCase(unchecked((long)0xfff0000000000000), 0x0000000000000000, true)]
    public void Exp_AnArgumentTheBinaryWasAsked_AnswersItsBitsOnThatPath(long argument, long expected, bool fused) =>
        Bits(IvpMath.Exp(System.BitConverter.Int64BitsToDouble(argument), fused)).ShouldBe(expected);

    /// <remarks>
    /// Both branches of the rational function — under one half, and over it through the truncated root — the tiny
    /// pass-through, the exact ends, and the domain error's `0xffc00000`.
    /// </remarks>
    [TestCase(0x3dcccccd, 0x3dcd2494)]
    [TestCase(0x3efff2e5, 0x3f060301)]
    [TestCase(0x3f000000, 0x3f060a92)]
    [TestCase(0x3f7fbe77, 0x3fc35650)]
    [TestCase(unchecked((int)0xbecccccd), unchecked((int)0xbed2b256))]
    [TestCase(unchecked((int)0xbf400000), unchecked((int)0xbf591a99))]
    [TestCase(0x0da24260, 0x0da24260)]
    [TestCase(0x3f800000, 0x3fc90fdb)]
    [TestCase(0x3fc00000, unchecked((int)0xffc00000))]
    [TestCase(0x7f800000, unchecked((int)0xffc00000))]
    public void Asinf_AnArgumentTheBinaryWasAsked_AnswersItsBits(int argument, int expected) =>
        Bits(IvpMath.Asinf(System.BitConverter.Int32BitsToSingle(argument))).ShouldBe(expected);

    /// <remarks>
    /// Every breakpoint's interval — under `7/16`, to `11/16`, to `19/16`, to `39/16` and beyond — a negative argument,
    /// the infinities and a tiny argument.
    /// </remarks>
    [TestCase(0x3f50624dd2f1a9fc, 0x3f50624d77516e16)]
    [TestCase(0x3fd3333333333333, 0x3fd2a73a661eaf06)]
    [TestCase(0x3fdc000000000000, 0x3fda64eec3cc23fd)]
    [TestCase(0x3fe999999999999a, 0x3fe5977a5103ea93)]
    [TestCase(0x3ff3000000000000, 0x3febde70ed439fe7)]
    [TestCase(0x4003800000000000, 0x3ff2e75728833a54)]
    [TestCase(0x4008000000000000, 0x3ff3fc176b7a8560)]
    [TestCase(0x4024000000000000, 0x3ff789bd2c160054)]
    [TestCase(0x408f400000000000, 0x3ff91de2c0e658bd)]
    [TestCase(unchecked((long)0xbfe6666666666666), unchecked((long)0xbfe38b112d7bd4ad))]
    [TestCase(0x7ff0000000000000, 0x3ff921fb54442d18)]
    [TestCase(unchecked((long)0xfff0000000000000), unchecked((long)0xbff921fb54442d18))]
    [TestCase(0x39b4484bfeebc2a0, 0x39b4484bfeebc2a0)]
    public void Atan_AnArgumentTheBinaryWasAsked_AnswersItsBits(long argument, long expected) =>
        Bits(IvpMath.Atan(System.BitConverter.Int64BitsToDouble(argument))).ShouldBe(expected);

    /// <remarks>
    /// FUN_1800d33b0 below `π/4` on both of its paths: the series, under `2^-13`, under `2^-27`, and a negative
    /// argument. Every value was answered by vphysics.dll called in process.
    /// </remarks>
    [TestCase(0x3fd3333333333333, 0x3fee921dd42f09ba, false)]
    [TestCase(0x3fd3333333333333, 0x3fee921dd42f09ba, true)]
    [TestCase(0x3f264840e1719f80, 0x3feffffff83e01d3, false)]
    [TestCase(0x3f264840e1719f80, 0x3feffffff83e01d3, true)]
    [TestCase(0x3e112e0be826d695, 0x3ff0000000000000, false)]
    [TestCase(0x3e112e0be826d695, 0x3ff0000000000000, true)]
    [TestCase(0x3fe9212d77318fc5, 0x3fe6a12ff5f42bd9, false)]
    [TestCase(0x3fe9212d77318fc5, 0x3fe6a12ff5f42bd9, true)]
    [TestCase(unchecked((long)0xbfe0000000000000), 0x3fec1528065b7d50, false)]
    [TestCase(unchecked((long)0xbfe0000000000000), 0x3fec1528065b7d50, true)]

    // Found by search: arguments whose last bit moves when the plain path adds its rounding back in another order, or
    // the fused path's second-to-last step multiplies and adds in two.
    [TestCase(0x3fe4e68f72713f8d, 0x3fe969e57de70333, false)]
    [TestCase(unchecked((long)0xbfe5e986b39a2205), 0x3fe8c94d55538a65, true)]
    public void Cos_AnArgumentTheBinaryWasAsked_AnswersItsBitsOnThatPath(long argument, long expected, bool fused) =>
        Bits(IvpMath.Cos(System.BitConverter.Int64BitsToDouble(argument), fused)).ShouldBe(expected);

    /// <remarks>
    /// FUN_1800d33b0 past `π/4`: the inline reduction on each path, the plain path's large reduction from 500000 and the fused
    /// path's from 2·10^7, a huge argument, an infinity's default NaN and a NaN passed through. **`3.9219045125656624` is the
    /// plain kernel's twelfth coefficient**: a port carrying `…69037ec2e` for the binary's `…690382eec` answers `…b6b9` there,
    /// one ulp from the binary, and agreed with every replica written from the same wrong literal. The pairs that differ by
    /// path were found by the sweep.
    /// </remarks>
    [TestCase(0x3ff0000000000000, 0x3fe14a280fb5068b, false)]
    [TestCase(0x3ff0000000000000, 0x3fe14a280fb5068c, true)]
    [TestCase(unchecked((long)0xc004000000000000), unchecked((long)0xbfe9a2f7ef858b7d), false)]
    [TestCase(unchecked((long)0xc004000000000000), unchecked((long)0xbfe9a2f7ef858b7d), true)]
    [TestCase(0x400f600f791c08e4, unchecked((long)0xbfe6be01b845b6ba), false)]
    [TestCase(0x400f600f791c08e4, unchecked((long)0xbfe6be01b845b6b9), true)]
    [TestCase(0x401be6b5eb61fda4, 0x3fe8a2fd19644ac4, false)]
    [TestCase(0x401be6b5eb61fda4, 0x3fe8a2fd19644ac3, true)]
    [TestCase(unchecked((long)0xbfe2d19ecdb328b0), 0x3fea9fc41abd3874, false)]
    [TestCase(unchecked((long)0xbfe2d19ecdb328b0), 0x3fea9fc41abd3875, true)]
    [TestCase(0x412e848100000000, 0x3fefad0a779be88d, false)]
    [TestCase(0x412e848100000000, 0x3fefad0a779be88d, true)]
    [TestCase(0x41349280c43fce3c, unchecked((long)0xbfe502a17c3d41e2), false)]
    [TestCase(0x41349280c43fce3c, unchecked((long)0xbfe502a17c3d41e3), true)]
    [TestCase(0x417c9c3804000000, unchecked((long)0xbfdfb9abf312eecc), false)]
    [TestCase(0x417c9c3804000000, unchecked((long)0xbfdfb9abf312eecc), true)]
    [TestCase(0x7e37e43c8800759c, unchecked((long)0xbfe2699022adc4c1), false)]
    [TestCase(0x7e37e43c8800759c, unchecked((long)0xbfe2699022adc4c1), true)]
    [TestCase(0x7ff0000000000000, unchecked((long)0xfff8000000000000), false)]
    [TestCase(0x7ff0000000000000, unchecked((long)0xfff8000000000000), true)]
    [TestCase(unchecked((long)0xfff8000000000000), unchecked((long)0xfff8000000000000), false)]
    [TestCase(unchecked((long)0xfff8000000000000), unchecked((long)0xfff8000000000000), true)]
    public void Cos_AnArgumentPastQuarterPi_AnswersItsBitsOnThatPath(long argument, long expected, bool fused) =>
        Bits(IvpMath.Cos(System.BitConverter.Int64BitsToDouble(argument), fused)).ShouldBe(expected);

    /// <remarks>
    /// FUN_1800c8020: the pass-through under `2^−27`, the fused path's cube under `2^−13`, the series, `π/4` itself, a negative
    /// argument, the reductions as for the cosine, and the two ways a non-finite argument ends. The pairs that differ by path
    /// were found by the sweep.
    /// </remarks>
    [TestCase(0x3bc79ca10c924223, 0x3bc79ca10c924223, false)]
    [TestCase(0x3bc79ca10c924223, 0x3bc79ca10c924223, true)]
    [TestCase(0x3e112e0be826d695, 0x3e112e0be826d695, false)]
    [TestCase(0x3e112e0be826d695, 0x3e112e0be826d695, true)]
    [TestCase(0x3ee4f8b588e368f1, 0x3ee4f8b588e1e8a2, false)]
    [TestCase(0x3ee4f8b588e368f1, 0x3ee4f8b588e1e8a2, true)]
    [TestCase(0x3fd3333333333333, 0x3fd2e9cd95baba33, false)]
    [TestCase(0x3fd3333333333333, 0x3fd2e9cd95baba33, true)]
    [TestCase(0x3fe0b012ca010180, 0x3fdfe21b9b92aa74, false)]
    [TestCase(0x3fe0b012ca010180, 0x3fdfe21b9b92aa75, true)]
    [TestCase(0x3fe921fb54442d18, 0x3fe6a09e667f3bcd, false)]
    [TestCase(0x3fe921fb54442d18, 0x3fe6a09e667f3bcd, true)]
    [TestCase(unchecked((long)0xbfe6666666666666), unchecked((long)0xbfe49d6e694619b8), false)]
    [TestCase(unchecked((long)0xbfe6666666666666), unchecked((long)0xbfe49d6e694619b8), true)]
    [TestCase(unchecked((long)0xc004000000000000), unchecked((long)0xbfe326af0dcfcab1), false)]
    [TestCase(unchecked((long)0xc004000000000000), unchecked((long)0xbfe326af0dcfcab0), true)]
    [TestCase(0x411de9547f6e5d71, unchecked((long)0xbfdd8d341675be40), false)]
    [TestCase(0x411de9547f6e5d71, unchecked((long)0xbfdd8d341675be3f), true)]
    [TestCase(0x4159fa8425c3604f, unchecked((long)0xbfe713d4e239a0d0), false)]
    [TestCase(0x4159fa8425c3604f, unchecked((long)0xbfe713d4e239a0cf), true)]
    [TestCase(unchecked((long)0xc19d6f34547df3b6), unchecked((long)0xbfeffeb69ed066b7), false)]
    [TestCase(unchecked((long)0xc19d6f34547df3b6), unchecked((long)0xbfeffeb69ed066b7), true)]
    [TestCase(0x7e37e43c8800759c, unchecked((long)0xbfea2c16b010e385), false)]
    [TestCase(0x7e37e43c8800759c, unchecked((long)0xbfea2c16b010e385), true)]
    [TestCase(0x7ff0000000000000, unchecked((long)0xfff8000000000000), false)]
    [TestCase(unchecked((long)0xfff8000000000000), unchecked((long)0xfff8000000000000), true)]
    public void Sin_AnArgumentTheBinaryWasAsked_AnswersItsBitsOnThatPath(long argument, long expected, bool fused) =>
        Bits(IvpMath.Sin(System.BitConverter.Int64BitsToDouble(argument), fused)).ShouldBe(expected);

    /// <remarks>
    /// FUN_1800cce64 on each of its branches: a tiny argument's `π/2`, under one half, both signs over it, ±1 exactly, and the
    /// domain's default NaN beyond one and for an infinity.
    /// </remarks>
    [TestCase(0x3bc79ca10c924223, 0x3ff921fb54442d18)]
    [TestCase(0x3e112e0be826d695, 0x3ff921fb53ff74e9)]
    [TestCase(0x3fd3333333333333, 0x3ff441f5ecbeef59)]
    [TestCase(0x3fe0000000000000, 0x3ff0c152382d7366)]
    [TestCase(unchecked((long)0xbfe0000000000000), 0x4000c152382d7366)]
    [TestCase(0x3fe6666666666666, 0x3fe973e83f5d5c9b)]
    [TestCase(unchecked((long)0xbfe6666666666666), 0x4002c501446cd5f2)]
    [TestCase(0x3fe921fb54442d18, 0x3fe55bcf3c4a4694)]
    [TestCase(0x3ff0000000000000, 0x0000000000000000)]
    [TestCase(unchecked((long)0xbff0000000000000), 0x400921fb54442d18)]
    [TestCase(0x3ff8000000000000, unchecked((long)0xfff8000000000000))]
    [TestCase(0x7ff0000000000000, unchecked((long)0xfff8000000000000))]
    [TestCase(unchecked((long)0xfff8000000000000), unchecked((long)0xfff8000000000000))]
    public void Acos_AnArgumentTheBinaryWasAsked_AnswersItsBits(long argument, long expected) =>
        Bits(IvpMath.Acos(System.BitConverter.Int64BitsToDouble(argument))).ShouldBe(expected);

    private static int Bits(float value) => System.BitConverter.SingleToInt32Bits(value);

    private static long Bits(double value) => System.BitConverter.DoubleToInt64Bits(value);
}
