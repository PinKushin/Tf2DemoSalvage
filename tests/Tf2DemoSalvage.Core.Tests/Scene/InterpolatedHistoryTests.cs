using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>A history trimmed for a checkpoint (D217 step 3) must answer every question at or after its cut as before.</summary>
public sealed class InterpolatedHistoryTests
{
    /// <summary>Twenty entries, each changed and received at its own tick, valued at its tick.</summary>
    private static InterpolatedHistory Twenty()
    {
        InterpolatedHistory history = new(1);

        for (int tick = 1; tick <= 20; tick++)
        {
            history.Add(tick, tick, [tick]);
        }

        return history;
    }

    private static (float Older, float Newer) Pair(InterpolatedHistory history, double target, int arrivedBy)
    {
        Bracketing pair = history.Bracket(target, arrivedBy).ShouldNotBeNull();

        return (history.ValuesAt(pair.Older)[0], history.ValuesAt(pair.Newer)[0]);
    }

    /// <remarks>
    /// **A cut at 15 with a tail of 2** drops the twelve entries before 13 (entries 1..14 precede the cut; 14 − 2 = 12),
    /// and the pair bracketing 16.5 is the same two values.
    /// </remarks>
    [Test]
    public void DropReceivedBefore_CutAt15Tail2_KeepsTwoBeforeTheCutAndTheSameAnswers()
    {
        InterpolatedHistory history = Twenty();
        (float, float) before = Pair(history, 16.5, 17);

        history.DropReceivedBefore(15, 2);

        (history.Count, history.ChangeTimeAt(0)).ShouldBe((8, 13));
        Pair(history, 16.5, 17).ShouldBe(before);
    }

    /// <remarks>**The last entry is never dropped**, however late the cut: the value in force must stay.</remarks>
    [Test]
    public void DropReceivedBefore_CutPastTheEnd_KeepsTheLast()
    {
        InterpolatedHistory history = Twenty();

        history.DropReceivedBefore(100, 0);

        (history.Count, history.ChangeTimeAt(0), history.ValuesAt(0)[0]).ShouldBe((1, 20, 20f));
    }

    /// <remarks>
    /// **A trim gives back the capacity** (D217 step 4): a copy copies a list's whole backing array, so a history grown to
    /// 1,000 and cut to its last 2 must copy five int lists of 2 and one float list of 2, not of 1,024.
    /// </remarks>
    [Test]
    public void DropReceivedBefore_AfterAThousand_CopiesOnlyWhatIsKept()
    {
        InterpolatedHistory history = new(1);

        for (int tick = 1; tick <= 1000; tick++)
        {
            history.Add(tick, tick, [tick]);
        }

        history.DropReceivedBefore(999, 0);
        System.Collections.Generic.Dictionary<System.Type, (long Objects, long Elements)> created = [];
        StateCopy.Of(history, new System.Collections.Generic.HashSet<object>(), prior: null, out _, created);

        (history.Count, created[typeof(int[])], created[typeof(float[])]).ShouldBe((2, (5L, 10L), (1L, 2L)));
    }
}
