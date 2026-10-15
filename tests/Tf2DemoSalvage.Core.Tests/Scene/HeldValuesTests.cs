using System;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>A value as held at each tick, change-only (B548).</summary>
public sealed class HeldValuesTests
{
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
    public void Set_AnEarlierTick_Throws()
    {
        HeldValues<string> held = new();
        held.Set(10, "A");

        Should.Throw<ArgumentOutOfRangeException>(() => held.Set(9, "B"));
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
