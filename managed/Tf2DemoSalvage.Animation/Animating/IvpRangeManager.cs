using System;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>IVP's default range manager — the environment's <c>+0x38</c>, <c>FUN_1800a0420(rm, env, 1)</c> — which says how far
/// ahead the broad phase and a pair's watcher look (B369).</summary>
/// <remarks>
/// **Read from the disassembly** (`docs/findings/51`, *What vphysics hands the construction*): vphysics' template leaves
/// <c>+0x58</c> null, so every environment gets this one, with the constants its constructor writes. Every product and sum below
/// keeps the disassembly's destination, because a NaN speed or radius reaches a lane with whichever payload that operand holds.
/// Pinned by the `vphysics-range` probe (`IvpRangeConformanceTests`).
/// </remarks>
public static class IvpRangeManager
{
    /// <summary><c>DAT_1800f4f20</c>, added to each speed: <c>0x3bfd83c94fb6d2ac</c>, about <c>1e-20</c>, by its bits.</summary>
    private static readonly double SpeedFloor = BitConverter.Int64BitsToDouble(0x3bfd83c94fb6d2ac);

    /// <summary>The constructor's <c>+0x18</c>.</summary>
    private const double PairSpeedShare = 0.5d;

    /// <summary>The constructor's <c>+0x20</c>, <c>(double)0.9f</c> (<c>0x3fecccccc0000000</c>).</summary>
    private const double PairRadiusShare = 0.9f;

    /// <summary>The constructor's <c>+0x28</c>, <c>(double)0.8f</c> (<c>0x3fe99999a0000000</c>).</summary>
    private const double PairLeast = 0.8f;

    /// <summary>The constructor's <c>+0x30</c>.</summary>
    private const double PairMost = 10d;

    /// <summary>The constructor's <c>+0x38</c> and <c>+0x60</c>, <c>(double)0.06f</c> (<c>0x3faeb851e0000000</c>).</summary>
    private const double SpeedAhead = 0.06f;

    /// <summary>The constructor's <c>+0x40</c>.</summary>
    private const double ObjectSpeedShare = 1d;

    /// <summary>The constructor's <c>+0x48</c>.</summary>
    private const double ObjectRadiusShare = 5d;

    /// <summary>The constructor's <c>+0x50</c>.</summary>
    private const double ObjectLeast = 0.5d;

    /// <summary>The constructor's <c>+0x58</c>.</summary>
    private const double ObjectMost = 15d;

    /// <summary><c>DAT_1800f4f40</c>, <c>(double)0.2f</c> (<c>0x3fc99999a0000000</c>): the other side's share of a side's weight.</summary>
    private const double OtherShare = 0.2f;

    /// <summary><c>DAT_1800fe6c0</c>, <c>(double)0.18f</c> (<c>0x3fc70a3d80000000</c>): the first weight's share of the second.</summary>
    private const double FirstShare = 0.18f;

    /// <summary>How far past its surface the broad phase files an object — slot 2, <c>FUN_1800a04e0</c>.</summary>
    /// <param name="core">The object's core, <c>object+0xe8</c>.</param>
    /// <param name="step">The environment's <c>+0x108</c>, narrowed before use.</param>
    /// <returns>The range, in double.</returns>
    /// <remarks>
    /// <code>
    /// s = (double)(+0x254 + +0x1dc) + 1e-20;  r = (double)+0x4;  dt = (double)(float)step
    /// a = MINSD(s·1, r·5);  a = MAXSD(a, 0.5);  a = MINSD(a, 15);  a −= dt·s;  MAXSD(a, s·0.06f + r)
    /// </code>
    /// </remarks>
    public static double ObjectRange(IvpCoreBounds core, double step)
    {
        double speed = IvpMath.Addsd(IvpMath.Addss(core.SurfaceSpeedBound, core.LinearSpeed), SpeedFloor);
        double radius = core.Radius;
        double narrowed = (float)step;
        double range = Minsd(IvpMath.Mulsd(speed, ObjectSpeedShare), IvpMath.Mulsd(radius, ObjectRadiusShare));

        range = Minsd(Maxsd(range, ObjectLeast), ObjectMost);
        range -= IvpMath.Mulsd(narrowed, speed);

        return Maxsd(range, IvpMath.Addsd(IvpMath.Mulsd(speed, SpeedAhead), radius));
    }

    /// <summary>How far each side of a pair looks — slot 1, <c>FUN_1800a0560</c>, what a watcher and a recursive mindist ask.</summary>
    /// <param name="first">The first object's core.</param>
    /// <param name="second">The second object's core.</param>
    /// <param name="step">The environment's <c>+0x108</c>, narrowed before use.</param>
    /// <returns>Each side's range.</returns>
    /// <remarks>
    /// <code>
    /// sA, sB as ObjectRange's s;  m = (double)MINSS(A+0x4, B+0x4);  sum = sB + sA
    /// g = MINSD(m·0.9f, sum·0.5);  g = MAXSD(g, 0.8f);  g = MINSD(g, 10);  g −= dt·sum;  g = MAXSD(g, sum·0.06f)
    /// wA = sA + sB·0.2f;  wB = sB + wA·0.18f;  i = 1 / (wB + wA);  (g·wA·i, g·wB·i)
    /// </code>
    /// </remarks>
    public static (double First, double Second) PairRange(IvpCoreBounds first, IvpCoreBounds second, double step)
    {
        double firstSpeed = IvpMath.Addsd(IvpMath.Addss(first.SurfaceSpeedBound, first.LinearSpeed), SpeedFloor);
        double secondSpeed = IvpMath.Addsd(IvpMath.Addss(second.SurfaceSpeedBound, second.LinearSpeed), SpeedFloor);
        double radius = Minss(first.Radius, second.Radius);
        double sum = IvpMath.Addsd(secondSpeed, firstSpeed);
        double narrowed = (float)step;
        double range = Minsd(IvpMath.Mulsd(radius, PairRadiusShare), IvpMath.Mulsd(sum, PairSpeedShare));

        range = Minsd(Maxsd(range, PairLeast), PairMost);
        range -= IvpMath.Mulsd(narrowed, sum);
        range = Maxsd(range, IvpMath.Mulsd(sum, SpeedAhead));

        double firstWeight = IvpMath.Addsd(firstSpeed, IvpMath.Mulsd(secondSpeed, OtherShare));
        double secondWeight = IvpMath.Addsd(secondSpeed, IvpMath.Mulsd(firstWeight, FirstShare));
        double inverse = 1d / IvpMath.Addsd(secondWeight, firstWeight);

        return (IvpMath.Mulsd(IvpMath.Mulsd(range, firstWeight), inverse), IvpMath.Mulsd(IvpMath.Mulsd(range, secondWeight), inverse));
    }

    /// <summary>What both slots read of a core: its radius <c>+0x4</c>, linear speed <c>+0x1dc</c> and surface speed bound <c>+0x254</c>.</summary>
    /// <param name="core">The core, an object's <c>+0xe8</c>.</param>
    /// <returns>The bounds, the fields no slot reads zero.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="core"/> is null.</exception>
    public static IvpCoreBounds Bounds(IvpRigidBody core)
    {
        ArgumentNullException.ThrowIfNull(core);

        return new IvpCoreBounds(core.Radius, 0f, 0f, core.LinearSpeed, core.SurfaceSpeedBound);
    }

    /// <summary><c>MINSD</c>: the first operand only when strictly less, so a NaN on either side answers the second.</summary>
    private static double Minsd(double x, double y) => x < y ? x : y;

    /// <summary><c>MAXSD</c>: the first operand only when strictly greater, so a NaN on either side answers the second.</summary>
    private static double Maxsd(double x, double y) => x > y ? x : y;

    /// <summary><c>MINSS</c>, widened.</summary>
    private static double Minss(float x, float y) => x < y ? x : y;
}
