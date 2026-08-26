namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>The tick readout, and reading it back.</summary>
/// <remarks>
/// **The format was a literal inside `TransportBar` and its parser was a private helper inside a UI
/// test** (B188, D90). Two places knowing one format is two places to change, and the test's own
/// comment admitted it: *"the format is the bar's own"*.
/// </remarks>
public sealed class DemoPositionTests
{
    [Test]
    public void Label_ForAPosition_ReadsAsTickOfLast()
    {
        new DemoPosition(2500, 8065).Label().ShouldBe("tick 2500 / 8065");
    }

    [Test]
    public void AtEnd_AtTheLastTick_IsTrue()
    {
        new DemoPosition(8065, 8065).AtEnd.ShouldBeTrue();
    }

    [Test]
    public void AtEnd_BeforeTheLastTick_IsFalse()
    {
        new DemoPosition(2500, 8065).AtEnd.ShouldBeFalse();
    }

    [Test]
    public void AtEnd_WithNoDemoOpen_IsFalseRatherThanTrueByArithmetic()
    {
        // **Zero is not the end of nothing.** `Tick >= LastTick` alone is true for a fresh window,
        // so a viewer showing no demo would report that playback had finished — and a test asking
        // "did End reach the end" would pass before pressing anything.
        DemoPosition.None.AtEnd.ShouldBeFalse();
    }

    [Test]
    public void AtEnd_PastTheLastTick_IsStillTrue()
    {
        // Playback can be told a tick beyond the end by a seek that rounds up; the question is
        // "has it got there", not "is it exactly equal".
        new DemoPosition(9000, 8065).AtEnd.ShouldBeTrue();
    }

    [Test]
    public void Read_ItsOwnLabel_RoundTrips()
    {
        // **The property that makes the parser worth having**: whatever `Label` writes, `Read`
        // understands. A test owning its own copy of the format cannot make that claim.
        DemoPosition position = new(2500, 8065);

        DemoPosition.Read(position.Label()).ShouldBe(position);
    }

    [Test]
    public void Read_SomethingElse_IsNullRatherThanAGuess()
    {
        // **Null, not a fabricated position**, so a caller can say "not that shape" instead of
        // asserting against tick zero. A sentinel would conflate an unexpected readout with the
        // start of a demo — and ticks do not start at zero anyway.
        DemoPosition.Read("no demo open").ShouldBeNull();
    }

    [Test]
    public void Read_ALabelWithNonNumericHalves_IsNull()
    {
        // The shape can be right while the contents are not — "tick x / y" splits into two halves
        // and parses as neither.
        DemoPosition.Read("tick x / y").ShouldBeNull();
    }
}
