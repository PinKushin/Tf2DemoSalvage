using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// IVP's collision thresholds, which are all derived from one tolerance of a quarter inch (B369).
/// </summary>
/// <remarks>
/// **Read from `vphysics.dll`, every multiplier dumped** — `docs/findings/51`, *The block is linear in one collision
/// tolerance `d`, plus gravity `g`*. The environment constructor `FUN_1800114f0` passes `d = (0.25f − 1e-4f) × 0.0254f`
/// widened; `CPhysicsEnvironment::SetGravity` then runs `FUN_180098fd0` again on the block's own margin and the converted
/// gravity's length. The derivation, as the disassembly takes it:
///
/// <code>
///   block[0]    = (float)(d · 0.1f)
///   block[1]    = (float)(d · 0.9f + block[0])             // and block[0x42], the ramp's far end
///   block[0x43] = (float)(block[0x42] + d)
///   block[0x44] = (float)(d · 0.3f + block[0x43])
///   block[0x45] = (float)√((block[0x44] − block[1]) + (block[0x44] − block[1])) · g)
///   block[0x49] = block[1] · 0.1f
/// </code>
///
/// **Every expectation is a float's exact bits, evaluated independently** — that arithmetic run in F# over the dumped
/// constants' bits, not through this project's code — and converted to inches through the dword `0x421d7af6`.
///
/// **For this preset the engine's grouping does not show, and that is arithmetic rather than a gap in the fixtures.**
/// `2·d`, `d + d` and `(double)block[0x42] + d` agree to the bit, as do `2.3·d` and `d·0.3f + block[0x43]`, and the second
/// derivation returns the first's block unchanged. No other `d` reaches a running environment, so a regrouping of the
/// derivation cannot redden these tests; what they pin is the values.
/// </remarks>
public sealed class IvpCollisionToleranceConformanceTests
{
    /// <remarks>
    /// `(0.25f − 1e-4f) × 0.0254f`, both in float: `0x3bcffe5a`, `0.00634745974` metres.
    /// </remarks>
    [Test]
    public void Metres_TheConstructorsTolerance_IsTheFloatProductOfTheTrimmedPresetAndTheScale() =>
        Bits(IvpCollisionTolerance.Metres).ShouldBe(0x3bcffe5a);

    /// <remarks>
    /// **`block[1]` comes back as `d` itself**, `0x3bcffe5a`, and in inches `0x3e7fe5c9` — `0.249899998`. The value this
    /// project carried before the dword was dumped, `d × 39.37f`, is `0x3e7fe5a6`.
    /// </remarks>
    [Test]
    public void Margin_InSourceUnits_IsTheSettledMarginThroughTheReciprocalDword() =>
        Bits(IvpCollisionTolerance.Margin).ShouldBe(0x3e7fe5c9);

    /// <remarks>
    /// `block[0] = (float)(d · 0.1f)`, `0x3a266515`; in inches `0x3cccb7d4`, `0.0249899998`. Subtracted from a pair's
    /// distance before the recheck divides it by the speed bound — `(distance − ε) / speedBound` in `FUN_180099380`.
    /// </remarks>
    [Test]
    public void Epsilon_InSourceUnits_IsATenthOfTheToleranceInFloat() =>
        Bits(IvpCollisionTolerance.Epsilon).ShouldBe(0x3cccb7d4);

    /// <remarks>
    /// `block[0x49] = block[1] · 0.1f`, `DAT_18012d664`: the factor on the face core's `+0x54` in the edge search's target.
    /// The same bits as <see cref="IvpCollisionTolerance.Epsilon"/> from a different field and a different product.
    /// </remarks>
    [Test]
    public void EdgeTargetScale_InSourceUnits_IsATenthOfTheMargin() =>
        Bits(IvpCollisionTolerance.EdgeTargetScale).ShouldBe(0x3cccb7d4);

    /// <remarks>
    /// `block[0x43] = (float)(block[0x42] + d)`, `0x3c4ffe5a`; in inches `0x3effe5c9`, `0.499799997` — exactly twice the
    /// margin. It is `DAT_18012d64c`, the gap `FUN_180082ed0` starts every contact point with.
    /// </remarks>
    [Test]
    public void ContactGap_InSourceUnits_IsTheRampsEndPlusOneTolerance() =>
        Bits(IvpCollisionTolerance.ContactGap).ShouldBe(0x3effe5c9);

    /// <remarks>
    /// `block[0x44] = (float)(d · 0.3f + block[0x43])`, `0x3c6f314e`; in inches `0x3f132420`, `0.574769974`. It is
    /// `DAT_18012d650`, the gap the edge–edge measure gives edges with no crossing.
    /// </remarks>
    [Test]
    public void ParallelEdgeGap_InSourceUnits_IsThreeTenthsOfAToleranceBeyondTheContactGap() =>
        Bits(IvpCollisionTolerance.ParallelEdgeGap).ShouldBe(0x3f132420);

    /// <remarks>
    /// **`g` in metres, because `SetGravity` converts before the block sees it.** At `sv_gravity 800` the down lane is
    /// `800f × 0.0254f = 20.32` and its length the same; `(float)√(((0x3c6f314e − 0x3bcffe5a)·2) · 20.32)` is `0x3f143f75`,
    /// `0.579093277` m/s, and `0x41b6643f`, `22.7989483` inches a second. Below it `FUN_180099380` leaves a pair alone.
    /// </remarks>
    [Test]
    public void ClosingSpeedThreshold_AtSvGravity800_IsTheFallFromTheParallelEdgeGapToTheMargin() =>
        Bits(IvpCollisionTolerance.ClosingSpeedThreshold(PhysicsEnvironment.DefaultGravity)).ShouldBe(0x41b6643f);

    /// <remarks>
    /// **A control on the formula's shape, not only its value at one gravity.** The threshold goes as the square root of
    /// `g`, so quadrupling gravity must double it: `0x4236643f`, `45.5978966`, exactly twice the bits above. A linear or a
    /// constant implementation cannot satisfy both this and the test above.
    /// </remarks>
    [Test]
    public void ClosingSpeedThreshold_AtFourTimesTheGravity_IsDouble() =>
        Bits(IvpCollisionTolerance.ClosingSpeedThreshold(3200f)).ShouldBe(0x4236643f);

    /// <remarks>
    /// **The margin table `DAT_18012d548` is the block from `[2]`: 64 entries ramped from `block[1]` to `block[0x42]`**,
    /// and both ends are the margin — so every class on the ramp is the margin, to the bit. `FUN_1800a1b50` indexes it by
    /// the mindist's byte at bits 22–29.
    /// </remarks>
    [TestCase(0)]
    [TestCase(63)]
    public void MarginFor_AClassOnTheRamp_IsTheMargin(int marginClass) =>
        Bits(IvpCollisionTolerance.MarginFor(marginClass)).ShouldBe(Bits(IvpCollisionTolerance.Margin));

    /// <remarks>
    /// **Past the ramp the engine reads the block's later fields** — `block[0x42]` onward, one of them gravity-dependent —
    /// and below it reads before the table. Refused rather than guessed at, until what sets the byte is read.
    /// </remarks>
    [TestCase(-1)]
    [TestCase(64)]
    public void MarginFor_AClassOffTheRamp_IsRefused(int marginClass) =>
        Should.Throw<System.ArgumentOutOfRangeException>(() => IvpCollisionTolerance.MarginFor(marginClass));

    private static int Bits(float value) => System.BitConverter.SingleToInt32Bits(value);
}
