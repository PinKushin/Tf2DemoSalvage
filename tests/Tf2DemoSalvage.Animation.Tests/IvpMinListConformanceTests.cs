using System;
using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// IVP's event queue — the min-list the time manager keeps at <c>+0x10</c>, <c>FUN_1800aaed0</c> and <c>FUN_1800ab1b0</c>
/// (B369).
/// </summary>
/// <remarks>
/// **Read from the disassembly** (`docs/findings/51`, *The event queue, and where the far branch hands a pair off*). An add
/// at or under the minimum becomes the head; any other goes in before the first entry its value is not over; a remove
/// unlinks in place and resets the minimum to the new head's value, or `1e10f`; a freed slot is reused last-freed-first.
/// </remarks>
public sealed class IvpMinListConformanceTests
{
    /// <remarks>
    /// Three values added out of order come off in order, **the minimum following the head**: removing the head makes the
    /// next entry's value the minimum.
    /// </remarks>
    [Test]
    public void Add_ValuesOutOfOrder_ComeOffInOrderWithTheMinimumAtTheHead()
    {
        IvpMinList<string> queue = new();
        queue.Add("three", 3f);
        queue.Add("one", 1f);
        queue.Add("two", 2f);

        queue.Minimum.ShouldBe(1f);
        queue.TryFirst(out string? head, out int slot).ShouldBeTrue();
        head.ShouldBe("one");
        queue.Remove(slot);
        queue.Minimum.ShouldBe(2f);

        Drain(queue).ShouldBe(["two", "three"]);
    }

    /// <remarks>
    /// **A new event goes before every queued event of equal time** — at the head, where `COMISS`/`JA` takes a tie as
    /// not over the minimum, and in the middle, where the walk stops at the first entry the value is not over.
    /// </remarks>
    [Test]
    public void Add_AValueEqualToQueuedOnes_GoesBeforeThem()
    {
        IvpMinList<string> queue = new();
        queue.Add("early", 0f);
        queue.Add("first two", 2f);
        queue.Add("second two", 2f);
        queue.Add("first zero", 0f);

        Drain(queue).ShouldBe(["first zero", "early", "second two", "first two"]);
    }

    /// <remarks>**A NaN is not over the minimum, so it becomes the head**, and the minimum is NaN until it is removed.</remarks>
    [Test]
    public void Add_ANaNValue_BecomesTheHead()
    {
        IvpMinList<string> queue = new();
        queue.Add("one", 1f);
        queue.Add("nan", float.NaN);

        float.IsNaN(queue.Minimum).ShouldBeTrue();
        Drain(queue).ShouldBe(["nan", "one"]);
    }

    /// <remarks>**Removing the last event resets the minimum to `0x501502f9`**, `1e10f`, not to anything the queue held.</remarks>
    [Test]
    public void Remove_TheLastEvent_ResetsTheMinimumToTenBillion()
    {
        IvpMinList<string> queue = new();
        int slot = queue.Add("only", 0.5f);

        queue.Remove(slot);

        queue.Minimum.ShouldBe(1e10f);
        queue.Count.ShouldBe(0);
    }

    /// <remarks>Removing an event behind the head leaves the head and the minimum alone.</remarks>
    [Test]
    public void Remove_AnEventBehindTheHead_KeepsTheHeadAndMinimum()
    {
        IvpMinList<string> queue = new();
        queue.Add("one", 1f);
        int middle = queue.Add("two", 2f);
        queue.Add("three", 3f);

        queue.Remove(middle);

        queue.Minimum.ShouldBe(1f);
        Drain(queue).ShouldBe(["one", "three"]);
    }

    /// <remarks>
    /// **A freed slot is reused last-freed-first**, and with none free the next slot is the next index: slots 0 and 2 freed
    /// in that order are handed back 2, then 0, then 3.
    /// </remarks>
    [Test]
    public void Add_AfterTwoRemovals_ReusesTheLastFreedSlotFirst()
    {
        IvpMinList<string> queue = new();
        int a = queue.Add("a", 1f);
        queue.Add("b", 2f);
        int c = queue.Add("c", 3f);

        queue.Remove(a);
        queue.Remove(c);

        queue.Add("d", 4f).ShouldBe(c);
        queue.Add("e", 5f).ShouldBe(a);
        queue.Add("f", 6f).ShouldBe(3);
    }

    /// <remarks>
    /// **The engine would read past its entries**: a value over `1e10f` added to an empty queue is over the empty minimum and
    /// walks from index `0xffff`. Refused.
    /// </remarks>
    [Test]
    public void Add_AValueOverTenBillionToAnEmptyQueue_IsRefused() =>
        Should.Throw<InvalidOperationException>(() => new IvpMinList<string>().Add("late", 2e10f));

    private static string[] Drain(IvpMinList<string> queue)
    {
        string[] drained = new string[queue.Count];

        for (int index = 0; index < drained.Length; index++)
        {
            queue.TryFirst(out string? element, out int slot).ShouldBeTrue();
            drained[index] = element;
            queue.Remove(slot);
        }

        return drained;
    }
}
