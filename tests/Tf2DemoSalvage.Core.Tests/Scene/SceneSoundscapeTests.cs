using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// <see cref="SceneSoundscape"/>'s slot test and its hand-written equality (B335).
/// </summary>
/// <remarks>
/// **Nothing in `Core.Tests` touched this type at all**, and the coverage floor is what said so:
/// 15 branches at a flat zero, in the assembly whose coverage CI measures. One `Corpus.Tests` file
/// names it, which is a test Stryker never mutates and CI never counts — D38 drifting the wrong way
/// (B335).
///
/// **Everything here is synthetic, and that is the stronger form rather than the compromise.** A
/// soundscape is four numbers and a list; the test can put the values in and predict them exactly,
/// where a corpus test would have to compare two readings of the same demo and could not tell a
/// wrong bit position from a demo that happens not to use that slot.
/// </remarks>
public sealed class SceneSoundscapeTests
{
    /// <remarks>
    /// **A slot is "used" only when `localBits` says so**, which is the distinction the field
    /// exists for: *"if bits 0,1,2,3 are set then position 0,1,2,3 are valid/used"*. A slot at the
    /// origin and a slot nobody set are the same three floats and different facts.
    /// </remarks>
    [Test]
    public void HasPosition_ASlotItsBitsMark_IsUsed()
    {
        // Bits 0 and 3, so slots 0 and 3 and no others — an alternating pattern rather than a run,
        // because a run is satisfied by an off-by-one in either direction.
        SceneSoundscape soundscape = At(0b1001);

        soundscape.HasPosition(0).ShouldBeTrue();
        soundscape.HasPosition(3).ShouldBeTrue();

        soundscape.HasPosition(1).ShouldBeFalse("bit 1 is clear");
        soundscape.HasPosition(2).ShouldBeFalse("bit 2 is clear");
        soundscape.HasPosition(7).ShouldBeFalse("bit 7 is clear");
    }

    /// <remarks>
    /// **Both ends, because the guard is a range and a range fails two ways.** Slot 7 is the last
    /// real one — `m_audio.localSound` is eight slots — so 8 must be refused and 7 must not be,
    /// which is the pair that catches a `&lt;=` written for a `&lt;`.
    /// </remarks>
    [Test]
    public void HasPosition_ASlotOutsideTheEight_IsRefusedRatherThanShifted()
    {
        // Every bit set, so a refusal below can only come from the bound and not from the mask.
        SceneSoundscape soundscape = At(0xFF);

        soundscape.HasPosition(7).ShouldBeTrue("seven is the last real slot");
        soundscape.HasPosition(8).ShouldBeFalse("eight is past the end");
        soundscape.HasPosition(-1).ShouldBeFalse("a negative slot would shift by a negative count");
        soundscape.HasPosition(32).ShouldBeFalse("and a shift of 32 wraps to bit 0 in C#");
    }

    /// <remarks>
    /// **The equality override is load-bearing and the type says why**: a record struct holding a
    /// list compares that list by REFERENCE, so the generated equality calls two identical samples
    /// different and the sampler — which stores only on change — records a keyframe every tick.
    ///
    /// So the test needs two DISTINCT lists holding equal values. Handing the same list to both
    /// would pass against reference equality and prove nothing.
    /// </remarks>
    [Test]
    public void Equals_TwoSamplesWithEqualButDistinctLists_AreEqual()
    {
        SceneSoundscape first = new(3, 0b101, Slots(), 42);
        SceneSoundscape second = new(3, 0b101, Slots(), 42);

        ReferenceEquals(first.Positions, second.Positions).ShouldBeFalse(
            "the two lists must be distinct objects or this test cannot fail");

        first.Equals(second).ShouldBeTrue();
        (first == second).ShouldBeTrue("the operator must agree with the method");
    }

    /// <remarks>
    /// **The control, one field at a time**, because an equality that returned true for everything
    /// would satisfy the test above. Each of the four members is varied on its own.
    /// </remarks>
    [Test]
    public void Equals_SamplesDifferingInAnyOneField_AreNotEqual()
    {
        SceneSoundscape baseline = new(3, 0b101, Slots(), 42);

        baseline.Equals(new SceneSoundscape(4, 0b101, Slots(), 42))
            .ShouldBeFalse("a different soundscape index");

        baseline.Equals(new SceneSoundscape(3, 0b111, Slots(), 42))
            .ShouldBeFalse("different local bits");

        baseline.Equals(new SceneSoundscape(3, 0b101, Slots(), 7))
            .ShouldBeFalse("a different env_soundscape entity");

        baseline.Equals(new SceneSoundscape(3, 0b101, Slots(moved: true), 42))
            .ShouldBeFalse("a moved position, which is the whole reason the list is compared");
    }

    /// <remarks>
    /// **A null slot is not the origin**, and the list is of nullable triples precisely so the two
    /// can differ. An equality that coalesced them would call an unset slot equal to a slot placed
    /// at (0,0,0), which is the same conflation `HasPosition` exists to prevent.
    /// </remarks>
    [Test]
    public void Equals_AnUnsetSlotAgainstOneAtTheOrigin_AreNotEqual()
    {
        List<(float X, float Y, float Z)?> unset = [null];
        List<(float X, float Y, float Z)?> origin = [(0f, 0f, 0f)];

        new SceneSoundscape(0, 0, unset, 0)
            .Equals(new SceneSoundscape(0, 0, origin, 0))
            .ShouldBeFalse();
    }

    /// <remarks>
    /// **Equal values must hash the same**, or a sample used as a dictionary key finds nothing.
    /// The hash deliberately omits the list — hashing it would walk eight slots on every lookup —
    /// which is legal precisely because unequal objects MAY share a hash.
    /// </remarks>
    [Test]
    public void GetHashCode_TwoEqualSamples_Agree()
    {
        new SceneSoundscape(3, 0b101, Slots(), 42).GetHashCode()
            .ShouldBe(new SceneSoundscape(3, 0b101, Slots(), 42).GetHashCode());
    }

    /// <summary>A soundscape with the given <c>localBits</c> and eight empty slots.</summary>
    private static SceneSoundscape At(int bits) =>
        new(0, bits, new (float X, float Y, float Z)?[8], 0);

    /// <summary>A fresh list each call, which is what makes the equality tests meaningful.</summary>
    private static List<(float X, float Y, float Z)?> Slots(bool moved = false) =>
        [(1f, 2f, 3f), null, moved ? (9f, 9f, 9f) : (4f, 5f, 6f)];
}
