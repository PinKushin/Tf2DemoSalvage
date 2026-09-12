using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// IVP's collision thresholds, which are all one tolerance of a quarter inch (B369).
/// </summary>
/// <remarks>
/// **Read from `vphysics.dll`, every multiplier dumped** — `docs/findings/51`, *The block is linear in
/// one collision tolerance `d`, plus gravity `g`*. The environment constructor `FUN_1800114f0` sets
/// `d = (DAT_18011f008 − DAT_1800eb144) × DAT_18011f000`, which dumps as `(0.25 − 1e-4) × 0.0254` metres;
/// the neighbouring `0.0254` and `39.37` are the pair `IvpTransform` already carries, so the addresses
/// were checked with a control before the values were believed. `CPhysicsEnvironment::SetGravity`
/// prints that tolerance as `"0.250 tolerance"`.
///
/// `FUN_180098fd0` then derives the pair scheduler's thresholds from it:
///
/// <code>
///   block[0]    = 0.1 · d                   // the recheck's epsilon
///   block[1]    = 0.1 · d + 0.9 · d = d     // the collision margin
///   block[0x44] = block[0x42] + d + 0.3 · d // 2.3 · d
///   block[0x45] = √(2 · (block[0x44] − block[1]) · g)  = √(2.6 · d · g)
/// </code>
///
/// **Why this is a test before anything uses it.** B369's corpses carry limbs under the ground
/// because this project's narrow phase lets a pair penetrate by `IvpContact.Slop` — 0.25 — and pushes
/// back. IVP holds a pair `d` APART and never lets it close further. The number is the same and the
/// question is opposite, so the constants are pinned here from the binary before the scheduler that
/// reads them is written, not reverse-engineered from whatever makes corpses rest.
/// </remarks>
public sealed class IvpCollisionToleranceConformanceTests
{
    private const float Tolerance = 1e-4f;

    /// <remarks>
    /// `(0.25 − 0.0001) × 0.0254` = `0.00634746` metres — both factors stored as floats, so the
    /// product is taken as the engine takes it.
    /// </remarks>
    [Test]
    public void Metres_TheEnvironmentsTolerance_IsAQuarterInchLessATenThousandth()
    {
        IvpCollisionTolerance.Metres.ShouldBe(0.00634746f, 1e-7f);
    }

    /// <remarks>
    /// **In inches, the units this project simulates in.** `0.00634746 × 39.37` = `0.2498995`: Valve
    /// stores `0.0254` and `39.37` as a pair rather than one and its reciprocal, so the round trip does
    /// not return exactly `0.2499`.
    /// </remarks>
    [Test]
    public void Margin_InSourceUnits_IsTheToleranceBackThroughThirtyNinePointThreeSeven()
    {
        IvpCollisionTolerance.Margin.ShouldBe(0.2498995f, Tolerance);
    }

    /// <remarks>
    /// `block[0] = 0.1 · d`, subtracted from a pair's distance before the recheck divides it by the
    /// speed bound — `(distance − ε) / speedBound` in `FUN_180099380`.
    /// </remarks>
    [Test]
    public void Epsilon_InSourceUnits_IsATenthOfTheMargin()
    {
        IvpCollisionTolerance.Epsilon.ShouldBe(0.02498995f, Tolerance);
    }

    /// <remarks>
    /// **`√(2.6 · d · g)` with `g` in metres, because `SetGravity` converts before the block sees it.**
    /// At `sv_gravity 800` that is `g = 20.32`, `√(2.6 × 0.00634746 × 20.32)` = `0.579093` m/s, and
    /// `22.7989` inches a second back through `39.37`. It is the speed a body reaches falling
    /// `1.3 · d`, and below it `FUN_180099380` leaves a pair alone.
    /// </remarks>
    [Test]
    public void ClosingSpeedThreshold_AtSvGravity800_IsTwentyTwoPointEightInchesASecond()
    {
        IvpCollisionTolerance.ClosingSpeedThreshold(PhysicsEnvironment.DefaultGravity)
            .ShouldBe(22.7989f, 1e-3f);
    }

    /// <remarks>
    /// **A control on the formula's shape, not only its value at one gravity.** The threshold goes as
    /// the square root of `g`, so quadrupling gravity must exactly double it. A linear or a constant
    /// implementation cannot satisfy both this and the test above.
    /// </remarks>
    [Test]
    public void ClosingSpeedThreshold_AtFourTimesTheGravity_IsExactlyDouble()
    {
        float at800 = IvpCollisionTolerance.ClosingSpeedThreshold(800f);
        float at3200 = IvpCollisionTolerance.ClosingSpeedThreshold(3200f);

        at3200.ShouldBe(at800 * 2f, 1e-3f);
    }
}
