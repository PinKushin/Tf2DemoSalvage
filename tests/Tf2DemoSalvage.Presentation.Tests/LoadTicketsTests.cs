namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>Deciding which of several loads in flight is still wanted.</summary>
/// <remarks>
/// **This was `MainForm._loadsRequested` and four bare `ticket != _loadsRequested` comparisons**
/// (B188, D90). The policy is "a newer request wins": double-clicking two demos starts two decodes,
/// and the slower one must not overwrite the faster.
///
/// **It was tested only through a `MainForm`**, by racing two real `LoadDemoAsync` calls against a
/// real 24-minute demo. That integration test is still worth having and stays — but it can only
/// exercise the orderings the machine happens to produce, and it cannot reach the boundaries at all.
/// </remarks>
public sealed class LoadTicketsTests
{
    [Test]
    public void IsCurrent_TheOnlyTicket_IsTrue()
    {
        LoadTickets tickets = new();

        int only = tickets.Take();

        tickets.IsCurrent(only).ShouldBeTrue();
    }

    [Test]
    public void IsCurrent_AnOvertakenTicket_IsFalse()
    {
        // The whole policy in one case: opening a big demo and changing your mind must not leave you
        // looking at the big one when it finally finishes.
        LoadTickets tickets = new();

        int slower = tickets.Take();
        tickets.Take();

        tickets.IsCurrent(slower).ShouldBeFalse();
    }

    [Test]
    public void IsCurrent_TheNewestOfSeveral_IsTrue()
    {
        // **The control for the case above.** A `IsCurrent` that answered false for everything would
        // pass that test perfectly, and would discard every load ever started.
        LoadTickets tickets = new();

        tickets.Take();
        tickets.Take();
        int newest = tickets.Take();

        tickets.IsCurrent(newest).ShouldBeTrue();
    }

    [Test]
    public void Take_TheFirstTicket_IsNeverZero()
    {
        // **Zero is what an uninitialised `int` holds, and this is what keeps it from being a valid
        // ticket** — the structural version of a guard rather than the guard.
        //
        // The first draft of this file asserted `IsCurrent(0)` is false, which it is not: the
        // counter starts at zero, so zero compares equal to it. Making that true would mean adding
        // `ticket != 0 &&` to `IsCurrent` — a branch no caller can reach, since every holder got its
        // ticket from `Take`. That is dead code, and dead code with a condition on it is also a
        // permanent mutation survivor: nothing can distinguish the guard from its absence.
        //
        // So the invariant is moved to where it can actually hold. `Take` counting from one is
        // falsifiable — a post-increment would hand out zero first — and it is the whole reason no
        // guard is needed downstream.
        LoadTickets tickets = new();

        tickets.Take().ShouldBe(1);
    }

    [Test]
    public void Take_WithoutKeepingTheTicket_StillOvertakesWhatWasRunning()
    {
        // **`LoadDemo` does exactly this**, and it is the reason `Take` is not called `TakeTicket`
        // and quietly assumed to be paired with an `IsCurrent`. A synchronous load starting while an
        // async one decodes must supersede it: they both end by assigning the same fields.
        LoadTickets tickets = new();

        int decoding = tickets.Take();

        tickets.Take();

        tickets.IsCurrent(decoding).ShouldBeFalse();
    }

    [Test]
    public void IsCurrent_AskedTwiceForTheSameTicket_AnswersTheSameBothTimes()
    {
        // **Asking must not consume**, because the async path asks up to three times for one ticket
        // — after the decode, after the map read, and again in the failure handler. A check that
        // advanced anything would make the second question answer differently from the first, and
        // the load would abandon itself halfway.
        LoadTickets tickets = new();

        int ticket = tickets.Take();

        tickets.IsCurrent(ticket).ShouldBeTrue();
        tickets.IsCurrent(ticket).ShouldBeTrue();
    }
}
