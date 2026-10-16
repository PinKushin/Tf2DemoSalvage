using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>A value as held at each tick, change-only (B548).</summary>
public sealed class HeldValuesTests
{
    /// <remarks>
    /// **A trim keeps the value in force at the cut** (D217 step 3): of A at 5, B at 10, C at 20, a cut at 15 drops A and
    /// keeps B, so 15 still answers B and 20 answers C.
    /// </remarks>
    [Test]
    public void DropBefore_ACutBetweenChanges_KeepsTheValueInForce()
    {
        HeldValues<string> held = new();
        held.Set(5, "A");
        held.Set(10, "B");
        held.Set(20, "C");

        held.DropBefore(15);

        held.Ticks.ShouldBe([10, 20]);
        held.TryAt(15, out string at).ShouldBeTrue();
        at.ShouldBe("B");
    }

    [Test]
    public void TryAt_BeforeTheFirst_IsFalse()
    {
        HeldValues<string> held = new();
        held.Set(5, "A");

        held.TryAt(4, out _).ShouldBeFalse();
        held.TryAt(5, out string at).ShouldBeTrue();
        at.ShouldBe("A");
    }

    [Test]
    public void TryAt_BetweenChanges_IsTheLatestAtOrBefore()
    {
        HeldValues<string> held = new();
        held.Set(5, "A");
        held.Set(10, "B");

        held.TryAt(9, out string before).ShouldBeTrue();
        before.ShouldBe("A");
        held.TryAt(10, out string after).ShouldBeTrue();
        after.ShouldBe("B");
    }

    [Test]
    public void Set_ARepeat_IsNotKept()
    {
        HeldValues<string> held = new();
        held.Set(5, "A");
        held.Set(7, "A");

        held.Count.ShouldBe(1);
        held.ChangesAfter(5).ShouldBe(double.PositiveInfinity);
    }

    [Test]
    public void Set_ASecondValueAtOneTick_ReplacesTheFirst()
    {
        HeldValues<string> held = new();
        held.Set(5, "A");
        held.Set(10, "B");
        held.Set(10, "C");

        held.Ticks.ShouldBe([5, 10]);
        held.Values.ShouldBe(["A", "C"]);
    }

    [Test]
    public void Set_ARevertAtOneTick_LeavesNoChange()
    {
        HeldValues<string> held = new();
        held.Set(5, "A");
        held.Set(10, "B");
        held.Set(10, "A");

        held.Ticks.ShouldBe([5]);
        held.ChangesAfter(5).ShouldBe(double.PositiveInfinity);
    }

    [Test]
    public void Set_AnEarlierTick_IsTakenAtTheLast()
    {
        HeldValues<string> held = new();
        held.Set(5, "A");
        held.Set(10, "B");
        held.Set(9, "C");

        held.Ticks.ShouldBe([5, 10]);
        held.Values.ShouldBe(["A", "C"]);
        held.Clamped.ShouldBe(1);
    }

    [Test]
    public void Set_AnEarlierTickRevertingToTheValueBefore_LeavesNoChange()
    {
        HeldValues<string> held = new();
        held.Set(5, "A");
        held.Set(10, "B");
        held.Set(9, "A");

        held.Ticks.ShouldBe([5]);
        held.Values.ShouldBe(["A"]);
        held.Clamped.ShouldBe(1);
    }

    [Test]
    public void Set_AnEarlierTickWithOneEntry_ReplacesItAtItsTick()
    {
        HeldValues<string> held = new();
        held.Set(10, "A");
        held.Set(9, "B");

        held.Ticks.ShouldBe([10]);
        held.Values.ShouldBe(["B"]);
        held.Clamped.ShouldBe(1);
    }

    [Test]
    public void Set_FourValuesAtOneTickEndingWhereTheyBegan_KeepsOne()
    {
        HeldValues<string> held = new();
        held.Set(5, "A");
        held.Set(5, "B");
        held.Set(5, "C");
        held.Set(5, "A");

        held.Ticks.ShouldBe([5]);
        held.Values.ShouldBe(["A"]);
    }

    [Test]
    public void Set_ARevertAtOneTickToTheValueBefore_DropsTheEntry()
    {
        HeldValues<string> held = new();
        held.Set(1, "X");
        held.Set(5, "A");
        held.Set(5, "B");
        held.Set(5, "X");

        held.Ticks.ShouldBe([1]);
        held.Values.ShouldBe(["X"]);
    }

    [Test]
    public void Set_ASecondValueAtTheOnlyTick_ReplacesIt()
    {
        HeldValues<string> held = new();
        held.Set(5, "A");
        held.Set(5, "B");

        held.Values.ShouldBe(["B"]);
    }

    [Test]
    public void ChangesAfter_BeforeTheFirst_IsTheFirst()
    {
        HeldValues<string> held = new();
        held.Set(5, "A");
        held.Set(10, "B");

        held.ChangesAfter(0).ShouldBe(5d);
        held.ChangesAfter(5).ShouldBe(10d);
    }
}
