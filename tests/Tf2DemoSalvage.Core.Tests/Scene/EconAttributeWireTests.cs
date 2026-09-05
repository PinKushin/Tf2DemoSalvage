using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// <see cref="EconAttributeWire"/>'s hand-written equality, which the dedup depends on (B335).
/// </summary>
/// <remarks>
/// **Twenty branches at a flat zero**, found by the coverage floor rather than by anyone reading:
/// `EconAttributes.Resolve` beside it in the same file is thoroughly tested, and that is exactly
/// what made the gap invisible — the file looks covered.
///
/// **What is at stake is not equality in the abstract.** The type's own remarks say it: the record
/// default compares the LISTS by reference, and `RecordViewmodels` keeps a sample only when
/// `before == weapon` reports no change. Fresh lists every tick under reference equality make every
/// tick "changed", and the sampler records the entire demo as viewmodel samples. So a broken
/// equality here is a size and a performance defect that still draws the right picture — the kind
/// nothing on screen reports.
///
/// **Every list here is built fresh per call for that reason.** Handing the same list instance to
/// both sides would pass against the reference-equality bug this override exists to prevent.
/// </remarks>
public sealed class EconAttributeWireTests
{
    [Test]
    public void Equals_TwoWiresWithEqualButDistinctLists_AreEqual()
    {
        EconAttributeWire first = new(Local(), Networked(), HasValidItemId: true);
        EconAttributeWire second = new(Local(), Networked(), HasValidItemId: true);

        ReferenceEquals(first.Local, second.Local).ShouldBeFalse(
            "the lists must be distinct objects or this test cannot fail");

        first.Equals(second).ShouldBeTrue();
        (first == second).ShouldBeTrue("the operator must agree with the method");
    }

    /// <remarks>
    /// **Each of the three members varied on its own**, because an equality returning true for
    /// everything satisfies the test above. The two lists are varied separately: comparing only one
    /// of them is the plausible half-implementation, and it would pass a test that varied both.
    /// </remarks>
    [Test]
    public void Equals_WiresDifferingInAnyOneMember_AreNotEqual()
    {
        EconAttributeWire baseline = new(Local(), Networked(), HasValidItemId: true);

        baseline.Equals(new EconAttributeWire(Local(), Networked(), HasValidItemId: false))
            .ShouldBeFalse("the item-id guard routes to a different branch of IterateAttributes");

        baseline.Equals(new EconAttributeWire(Local(paint: 0xFF0000), Networked(), true))
            .ShouldBeFalse("a different LOCAL value");

        baseline.Equals(new EconAttributeWire(Local(), Networked(extra: true), true))
            .ShouldBeFalse("a different NETWORKED list");
    }

    /// <remarks>
    /// **A shorter list against a longer one**, which is `SameValues`' first guard and the one an
    /// index-only comparison misses: walking `left` alone would call `[a]` equal to `[a, b]`.
    /// Asserted both ways round, since a length check written on one side only is asymmetric.
    /// </remarks>
    [Test]
    public void Equals_ListsOfDifferentLengths_AreNotEqual()
    {
        EconAttributeWire shorter = new([Attribute(1, 2f)], [], HasValidItemId: true);
        EconAttributeWire longer = new([Attribute(1, 2f), Attribute(2, 3f)], [], true);

        shorter.Equals(longer).ShouldBeFalse();
        longer.Equals(shorter).ShouldBeFalse("and the same the other way round");
    }

    /// <remarks>
    /// **Same values, different ORDER.** `IterateAttributes` is first-writer-wins per definition
    /// index, so the order of the list decides which value survives — two orderings are two
    /// different resolutions and must not compare equal.
    /// </remarks>
    [Test]
    public void Equals_TheSameAttributesInADifferentOrder_AreNotEqual()
    {
        new EconAttributeWire([Attribute(1, 2f), Attribute(2, 3f)], [], true)
            .Equals(new EconAttributeWire([Attribute(2, 3f), Attribute(1, 2f)], [], true))
            .ShouldBeFalse("order decides which definition index wins");
    }

    /// <remarks>
    /// The null comparand, which `other is not null` guards and which a dictionary reaches.
    /// </remarks>
    [Test]
    public void Equals_ANullComparand_IsNotEqual()
    {
        new EconAttributeWire(Local(), Networked(), true).Equals(null).ShouldBeFalse();
    }

    /// <remarks>
    /// **Equal wires must hash the same or the dedup finds nothing** — and unlike
    /// <c>SceneSoundscape</c>, this hash DOES walk both lists, so it can disagree with the equality
    /// in a way a reference hash never would.
    /// </remarks>
    [Test]
    public void GetHashCode_TwoEqualWires_Agree()
    {
        new EconAttributeWire(Local(), Networked(), true).GetHashCode()
            .ShouldBe(new EconAttributeWire(Local(), Networked(), true).GetHashCode());
    }

    /// <remarks>
    /// **A raw-bit value is a FLOAT reinterpreted, not converted**, which is the trap the type
    /// exists to keep straight: the wire carries 32 bits and reading them as an integer gives a
    /// number in the millions where the attribute means a small multiplier.
    /// </remarks>
    [Test]
    public void Value_ARawBitPattern_IsReinterpretedRatherThanConverted()
    {
        EconAttributeValue attribute = Attribute(134, 2.5f);

        attribute.Value.ShouldBe(2.5f);

        attribute.AsInteger.ShouldBe(
            BitConverter.SingleToInt32Bits(2.5f),
            "the same bits read as an integer, which is 1075838976 rather than 2");
    }

    /// <summary>An attribute carrying a float, stored the way the wire stores it.</summary>
    private static EconAttributeValue Attribute(int definition, float value) =>
        new(definition, BitConverter.SingleToInt32Bits(value));

    /// <summary>Branch 1's list, fresh each call.</summary>
    private static List<EconAttributeValue> Local(int paint = 0x2D2D24) =>
        [Attribute(142, paint), Attribute(134, 1.5f)];

    /// <summary>Branch 3's list, fresh each call.</summary>
    private static List<EconAttributeValue> Networked(bool extra = false) =>
        extra ? [Attribute(214, 3f), Attribute(215, 4f)] : [Attribute(214, 3f)];
}
