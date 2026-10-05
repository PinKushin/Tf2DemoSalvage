namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>The instrument two builds' skips are compared with: it must tell order and splits apart, and be stable.</summary>
public sealed class SequenceHashTests
{
    [Test]
    public void Add_TwoOrdersAndTwoSplits_HashApartAndRepeatExactly()
    {
        ulong ab = SequenceHash.Add(SequenceHash.Add(SequenceHash.Empty, "a"), "bc");
        ulong ba = SequenceHash.Add(SequenceHash.Add(SequenceHash.Empty, "bc"), "a");
        ulong split = SequenceHash.Add(SequenceHash.Add(SequenceHash.Empty, "ab"), "c");

        (ab == ba, ab == split, ab == SequenceHash.Add(SequenceHash.Add(SequenceHash.Empty, "a"), "bc")).ShouldBe((false, false, true));
    }
}
