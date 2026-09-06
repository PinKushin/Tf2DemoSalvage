using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// What a ragdoll joint's limits become between the <c>.phy</c> and the solver (B58, D142).
/// </summary>
/// <remarks>
/// **Predicted from `vphysics.dll`, not from the code under test.** `CPhysicsConstraint`'s
/// constructor reads `constraint_ragdollparams_t` and writes three per-axis records, and four
/// separate things happen to a limit on the way. Every one of them is invisible if guessed: the
/// joint still has limits of plausible size, so the corpse settles smoothly into the wrong shape
/// rather than failing.
///
/// **These are the numbers the whole ragdoll rests on**, so they are pinned before any solver
/// exists to be biased by. `docs/findings/51` carries the decompiled source of each claim.
/// </remarks>
public sealed class RagdollJointLimitConformanceTests
{
    /// <remarks>
    /// **The table at `18011f014` is `00 02 01 03`** — Y and Z exchanged, which is the Z-up/Y-up
    /// difference between Source and IVP. Axis 0 and axis 3 map to themselves, which is what makes
    /// the middle pair the whole content of the table: a reader that skipped the remap entirely
    /// would still be right about half of it.
    /// </remarks>
    [Test]
    public void Slot_ForEachSourceAxis_ExchangesYAndZ()
    {
        RagdollJointLimits.Slot(0).ShouldBe(0);
        RagdollJointLimits.Slot(1).ShouldBe(2);
        RagdollJointLimits.Slot(2).ShouldBe(1);
        RagdollJointLimits.Slot(3).ShouldBe(3);
    }

    /// <remarks>
    /// **`if (axis &lt; 4) { … } return 0;`** — the engine's test is one-sided, so 4 and above
    /// answer 0 and so does anything negative. Reproduced rather than replaced by a throw: an index
    /// out of range is a fact about a stranger's `.phy`, and the engine's answer to it is a slot
    /// rather than a refusal.
    /// </remarks>
    [Test]
    public void Slot_ForAnAxisOutsideTheTable_IsZero()
    {
        RagdollJointLimits.Slot(4).ShouldBe(0);
        RagdollJointLimits.Slot(-1).ShouldBe(0);
    }

    /// <remarks>
    /// **Axes 0 and 1 take the POSITIVE constant and keep their field order.** 30 degrees is
    /// 0.5236 radians, and the pair arrives as it was written.
    /// </remarks>
    [Test]
    public void Convert_ForAxisZero_IsDegreesToRadiansInOrder()
    {
        (int slot, IvpAxisLimit limit) = RagdollJointLimits.Convert(0, -15f, 30f);

        slot.ShouldBe(0);
        limit.Minimum.ShouldBe(-15f * RagdollJointLimits.DegreesToRadians, 1e-6);
        limit.Maximum.ShouldBe(30f * RagdollJointLimits.DegreesToRadians, 1e-6);
    }

    /// <remarks>
    /// **Axis 1 is the control for the remap**: same conversion, different slot. Without it, "the
    /// slot is right" and "the slot is always the axis" read identically for axis 0.
    /// </remarks>
    [Test]
    public void Convert_ForAxisOne_KeepsTheSignAndMovesToSlotTwo()
    {
        (int slot, IvpAxisLimit limit) = RagdollJointLimits.Convert(1, -15f, 30f);

        slot.ShouldBe(2);
        limit.Minimum.ShouldBe(-15f * RagdollJointLimits.DegreesToRadians, 1e-6);
        limit.Maximum.ShouldBe(30f * RagdollJointLimits.DegreesToRadians, 1e-6);
    }

    /// <remarks>
    /// **Axis 2 is the negated one, and it exchanges its pair.** Negating `[a, b]` gives
    /// `[−b, −a]`, so `[-15, 30]` degrees becomes `[-30, 15]` degrees' worth of radians — still a
    /// range with the minimum below the maximum. **This is the assertion that catches a
    /// transcription which negates without exchanging**: that one produces `[0.2618, -0.5236]`, an
    /// inverted and empty range, and a joint that locks solid or flails depending which side the
    /// solver clamps first.
    /// </remarks>
    [Test]
    public void Convert_ForAxisTwo_NegatesAndExchangesThePair()
    {
        (int slot, IvpAxisLimit limit) = RagdollJointLimits.Convert(2, -15f, 30f);

        slot.ShouldBe(1);
        limit.Minimum.ShouldBe(-30f * RagdollJointLimits.DegreesToRadians, 1e-6);
        limit.Maximum.ShouldBe(15f * RagdollJointLimits.DegreesToRadians, 1e-6);

        limit.Minimum.ShouldBeLessThan(limit.Maximum, "a limit whose range inverted is not a limit");
    }

    /// <remarks>
    /// **A symmetric range is the input that CANNOT distinguish the negation**, which is why it is
    /// asserted separately rather than used as the fixture everywhere: `[-30, 30]` comes back
    /// `[-30, 30]` under both the correct reading and the one that forgets to exchange. Stated so
    /// nobody later "simplifies" the fixtures above into this one.
    /// </remarks>
    [Test]
    public void Convert_ForASymmetricRangeOnAxisTwo_CannotTellTheReadingsApart()
    {
        (int _, IvpAxisLimit limit) = RagdollJointLimits.Convert(2, -30f, 30f);

        limit.Minimum.ShouldBe(-30f * RagdollJointLimits.DegreesToRadians, 1e-6);
        limit.Maximum.ShouldBe(30f * RagdollJointLimits.DegreesToRadians, 1e-6);
    }

    /// <remarks>
    /// **`useClockwiseRotations` negates every limit AND exchanges every pair**, at
    /// `constraint_ragdollparams_t` offset `0xB2`. The SDK explains it only as *"HACKHACK: Did this
    /// wrong in version one. Fix in the future."*, so the binary is the only statement of what it
    /// does. Applied on top of the axis's own sign, which is why axis 0 is used here: it isolates
    /// the flag from the axis-2 negation.
    /// </remarks>
    [Test]
    public void Convert_WithClockwiseRotations_NegatesAndExchangesAgain()
    {
        (int _, IvpAxisLimit limit) = RagdollJointLimits.Convert(0, -15f, 30f, clockwise: true);

        limit.Minimum.ShouldBe(-30f * RagdollJointLimits.DegreesToRadians, 1e-6);
        limit.Maximum.ShouldBe(15f * RagdollJointLimits.DegreesToRadians, 1e-6);
    }

    /// <remarks>
    /// **Two flags cancel.** Axis 2 negates, the clockwise flag negates again, and the range comes
    /// back as it went in — which is the arithmetic and also the check that neither is applied
    /// twice.
    /// </remarks>
    [Test]
    public void Convert_ForAxisTwoWithClockwiseRotations_ReturnsToTheOriginalOrder()
    {
        (int _, IvpAxisLimit limit) = RagdollJointLimits.Convert(2, -15f, 30f, clockwise: true);

        limit.Minimum.ShouldBe(-15f * RagdollJointLimits.DegreesToRadians, 1e-6);
        limit.Maximum.ShouldBe(30f * RagdollJointLimits.DegreesToRadians, 1e-6);
    }

    /// <remarks>
    /// **Zero and 1e12 are two spellings of "no limit"** — the constraint counts as breakable only
    /// when a limit is non-zero and below 1e12. `constraint_breakableparams_t::Defaults()` uses the
    /// zero one, so a reader handling only the 1e12 spelling would look correct on every stock
    /// ragdoll and be wrong on a map that set a real one.
    /// </remarks>
    [Test]
    public void Unlimited_ForZeroAndForTheThreshold_AreBothUnlimited()
    {
        RagdollJointLimits.Unlimited(0f).ShouldBeTrue();
        RagdollJointLimits.Unlimited(RagdollJointLimits.UnlimitedThreshold).ShouldBeTrue();
        RagdollJointLimits.Unlimited(2e12f).ShouldBeTrue();

        RagdollJointLimits.Unlimited(1f).ShouldBeFalse("a real limit is below the threshold");
        RagdollJointLimits.Unlimited(9.9e11f).ShouldBeFalse();
    }
}
