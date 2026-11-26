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

    /// <summary>
    /// **A settle scan carries on from where the last one stopped** (B860 merge review): a variable that never settles was
    /// scanned from its last change on every call. Frames 10 to 50 asked (41), then 10 to 60 settling at 55: the second
    /// asks only 51 to 55 (5).
    /// A scan that found its frame answers again without asking anything, and a trim starts over.
    /// </summary>
    [Test]
    public void SettleFrame_AskedAgainFurther_ScansOnlyTheNewFrames()
    {
        InterpolatedHistory history = Twenty();
        object schedule = new();
        System.Collections.Generic.List<int> asked = [];

        history.SettleFrame(schedule, 0, -1, 10, 50, asked, static (frame, list) => { list.Add(frame); return false; }).ShouldBeNull();
        int firstScan = asked.Count;
        history.SettleFrame(schedule, 0, -1, 10, 60, asked, static (frame, list) => { list.Add(frame); return frame == 55; }).ShouldBe(55);
        int secondScan = asked.Count - firstScan;
        history.SettleFrame(schedule, 0, -1, 10, 70, asked, static (frame, list) => { list.Add(frame); return true; }).ShouldBe(55);
        int thirdScan = asked.Count - firstScan - secondScan;
        history.SettleFrame(schedule, 0, -1, 10, 54, asked, static (frame, list) => { list.Add(frame); return true; }).ShouldBeNull();

        history.DropReceivedBefore(5, tail: 2);
        history.SettleFrame(schedule, 0, -1, 10, 12, asked, static (frame, list) => { list.Add(frame); return false; }).ShouldBeNull();

        (firstScan, secondScan, thirdScan, asked[^1]).ShouldBe((41, 5, 0, 12));
    }

    /// <summary>
    /// **The latest change is memoized and a trim does not corrupt it** (B860 merge review): twenty entries where 11 to 20
    /// restate 10, asked at 20 (10), then trimmed and asked again (still 10, now at a shifted index).
    /// </summary>
    [Test]
    public void LatestChange_AfterARunOfRestatementsAndATrim_IsTheLastRealChange()
    {
        InterpolatedHistory history = new(1);

        for (int tick = 1; tick <= 20; tick++)
        {
            history.Add(tick, tick, [System.Math.Min(tick, 10)]);
        }

        // Asked early first, so the memo is extended forward later, and again after an append that changes (review 18, Fable).
        int? early = history.LatestChange(9, 0, -1);
        int? before = history.LatestChange(20, 0, -1);
        history.Add(21, 21, [21]);
        int? appended = history.LatestChange(21, 0, -1);
        history.DropReceivedBefore(5, tail: 2);

        (early, before, appended, history.LatestChange(20, 0, -1), history.LatestChange(9, 0, -1))
            .ShouldBe((9, 10, 21, 10, 9));
    }

    /// <summary>
    /// **The memos are keyed by variable and by schedule** (B860 review 18, Sonnet). Two components, the first changing
    /// every tick to 20 and the second fixed after 5: the first's latest change is 20, the second's 5. And one change
    /// asked under two schedules settles where each says, not where the first one asked found.
    /// </summary>
    [Test]
    public void Memos_TwoVariablesAndTwoSchedules_AnswerEachOwn()
    {
        InterpolatedHistory history = new(2);

        for (int tick = 1; tick <= 20; tick++)
        {
            history.Add(tick, tick, [tick, System.Math.Min(tick, 5)]);
        }

        (int?, int?) latest = (history.LatestChange(20, 0, 1), history.LatestChange(20, 1, 1));
        (int?, int?) settled = (
            history.SettleFrame(new object(), 0, 1, 10, 50, 30, static (frame, at) => frame == at),
            history.SettleFrame(new object(), 0, 1, 10, 50, 40, static (frame, at) => frame == at));

        (latest, settled).ShouldBe(((20, 5), (30, 40)));
    }

    private static (float Older, float Newer) Pair(InterpolatedHistory history, double target, int arrivedBy)
    {
        Bracketing pair = history.Bracket(target, arrivedBy).ShouldNotBeNull();

        return (history.ValuesAt(pair.Older)[0], history.ValuesAt(pair.Newer)[0]);
    }

    /// <remarks>
    /// **A latch is compared with the head before it flushes anything** (B860 review 6, Sonnet): `NoteChanged` `memcmp`s the
    /// new value with `m_VarHistory[0]` and only then `AddToHead` drops the newer entries (`interpolatedvar.h:633-649`).
    /// 100 at changetime 10, then 200 stamped 12 arriving at 11, then 200 stamped 11 arriving at 12, which flushes the entry
    /// stamped 12 — but met it as the head, identical: no change. The latest change arrived at 11.
    /// </remarks>
    [Test]
    public void LatestChange_ALatchThatFlushesItsHead_ComparesWithItFirst()
    {
        InterpolatedHistory history = new(1);

        history.Add(10, 10, [100f]);
        history.Add(12, 11, [200f]);
        history.Add(11, 12, [200f]);

        history.LatestChange(12, 0, 1).ShouldBe(11);
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
