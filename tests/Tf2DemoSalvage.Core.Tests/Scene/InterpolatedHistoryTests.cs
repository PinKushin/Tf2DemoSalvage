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

        // The trim shifted the history by two (review 19, Codex), so the answers after it come from moved indices.
        (early, before, appended, history.Count, history.LatestChange(20, 0, -1), history.LatestChange(9, 0, -1))
            .ShouldBe((9, 10, 21, 19, 10, 9));
    }

    /// <summary>
    /// **The memos are keyed by variable and by schedule** (B860 review 18, Sonnet). Two components, the first changing
    /// every tick to 20 and the second fixed after 5: the first's latest change is 20, the second's 5. And one change
    /// asked under two schedules settles where each says, not where the first one asked found.
    /// </summary>
    [Test]
    public void LatestChangeAndSettleFrame_EachKeyPartVaried_AnswerTheirOwn()
    {
        InterpolatedHistory history = new(2);

        for (int tick = 1; tick <= 20; tick++)
        {
            history.Add(tick, tick, [tick, System.Math.Min(tick, 5)]);
        }

        (int?, int?) latest = (history.LatestChange(20, 0, 1), history.LatestChange(20, 1, 1));
        object schedule = new();

        // Each part of the key varied from the first call alone, each predicting another frame (review 19, Sonnet).
        int?[] settled =
        [
            history.SettleFrame(schedule, 0, 1, 10, 50, 30, static (frame, at) => frame == at),
            history.SettleFrame(new object(), 0, 1, 10, 50, 40, static (frame, at) => frame == at),
            history.SettleFrame(schedule, 1, 1, 10, 50, 41, static (frame, at) => frame == at),
            history.SettleFrame(schedule, 0, -1, 10, 50, 42, static (frame, at) => frame == at),
            history.SettleFrame(schedule, 0, 1, 11, 50, 43, static (frame, at) => frame == at),
        ];

        (latest, settled[0], settled[1], settled[2], settled[3], settled[4]).ShouldBe(((20, 5), 30, 40, 41, 42, 43));
    }

    /// <summary>
    /// **The memo stops at a width change and never reads the older generation** (B860 reviews 20 and 21, merge review 2):
    /// five one-wide entries, then six-wide from 10 with component 4 stepping to 1 at 13, asked for (3, 3): 10 at 11 (the
    /// reset) and 13 at 15. A memo that filled from the first entry sliced (3, 3) out of a one-wide entry and threw.
    /// </summary>
    [Test]
    public void LatestChange_AcrossAWidthChange_StopsAtTheReset()
    {
        InterpolatedHistory history = new(1);

        for (int tick = 1; tick <= 5; tick++)
        {
            history.Add(tick, tick, [tick]);
        }

        history.SetMaxCount(6, 10, [0f, 0f, 0f, 0f, 0f, 0f]);

        for (int tick = 11; tick <= 15; tick++)
        {
            history.Add(tick, tick, [0f, 0f, 0f, 0f, tick >= 13 ? 1f : 0f, 0f]);
        }

        (history.LatestChange(11, 3, 3), history.LatestChange(15, 3, 3)).ShouldBe((10, 13));

        // Asked inside the one-wide generation for a span it cannot hold: a caller asking the wrong history, which fails
        // loudly rather than reading as a change on every entry (merge review 2).
        Should.Throw<System.ArgumentOutOfRangeException>(() => history.LatestChange(5, 3, 3));
    }

    /// <summary>
    /// **Asked before what the memo has examined, it walks back and keeps what it met** (B860 merge review 2): values 1, 2,
    /// 2, 2, 5, 5, 7 at ticks 1 to 7. Asked at 7 first (the run starts at the change at 7), then at 3 (2, the walk passing
    /// the change at 5 on the way and keeping it), at 6 (5, from what the walk kept), at 4 (2) and at 7 again (7).
    /// </summary>
    [Test]
    public void LatestChange_AskedBackwardsAfterForwards_IsTheChangeAtOrBefore()
    {
        InterpolatedHistory history = new(1);
        float[] values = [1, 2, 2, 2, 5, 5, 7];

        for (int tick = 1; tick <= 7; tick++)
        {
            history.Add(tick, tick, [values[tick - 1]]);
        }

        int?[] answers =
        [
            history.LatestChange(7, 0, -1), history.LatestChange(3, 0, -1), history.LatestChange(6, 0, -1),
            history.LatestChange(4, 0, -1), history.LatestChange(7, 0, -1),
        ];

        answers.ShouldBe([7, 2, 5, 2, 7]);
    }

    /// <summary>
    /// **A backward walk passes a reset's generation start and does not stop there** (B860 review 22, Fable): values 1, 2, 2
    /// at ticks 1 to 3, a `Reset` at 4 to the same 2, then 2, 2, 3 at 5 to 7. Asked at 7 (7), then at 2 — the walk passes
    /// the generation start at 4, a change above 2, and ends at the change at 2 — then at 5 (4, the reset kept on the way).
    /// </summary>
    [Test]
    public void LatestChange_AskedBackAcrossAReset_FindsTheChangeBeforeIt()
    {
        InterpolatedHistory history = new(1);
        history.Add(1, 1, [1f]);
        history.Add(2, 2, [2f]);
        history.Add(3, 3, [2f]);
        history.Reset(4, [2f]);
        history.Add(5, 5, [2f]);
        history.Add(6, 6, [2f]);
        history.Add(7, 7, [3f]);

        (history.LatestChange(7, 0, -1), history.LatestChange(2, 0, -1), history.LatestChange(5, 0, -1)).ShouldBe((7, 2, 4));
    }

    /// <summary>
    /// **A rewind walks back in several steps, each appending what it met** (B860 merge review 4): values 1, 2, 3, 3, 5,
    /// 5, 7, 8 at ticks 1 to 8. Asked at 8 (8), then 5 (5), then 2 (2) — two walks back — then between them at 4 (3), 6 (5)
    /// and 3 (3): every answer the latest change at or before the tick.
    /// </summary>
    [Test]
    public void LatestChange_RewoundInSeveralSteps_IsTheChangeAtOrBefore()
    {
        InterpolatedHistory history = new(1);
        float[] values = [1, 2, 3, 3, 5, 5, 7, 8];

        for (int tick = 1; tick <= 8; tick++)
        {
            history.Add(tick, tick, [values[tick - 1]]);
        }

        int[] asked = [8, 5, 2, 4, 6, 3];

        System.Linq.Enumerable.Select(asked, tick => history.LatestChange(tick, 0, -1)).ShouldBe([8, 5, 2, 3, 5, 3]);
    }

    /// <summary>
    /// **A call far past what the memo examined answers the same** (B860 review 24, Fable): values change at ticks 1, 5 and
    /// 1,500 over 2,000 entries. Asked at 10 (5), then at 2,000 (1,500, a second stretch), then back at 1,000 (5, a walk
    /// from 1,000 down to the first stretch) and at 1,600 (1,500).
    /// </summary>
    [Test]
    public void LatestChange_FarPastTheRun_AnswersTheSame()
    {
        InterpolatedHistory history = new(1);

        for (int tick = 1; tick <= 2000; tick++)
        {
            float value = 1f;

            if (tick >= 1500)
            {
                value = 3f;
            }
            else if (tick >= 5)
            {
                value = 2f;
            }

            history.Add(tick, tick, [value]);
        }

        int[] asked = [10, 2000, 1000, 1600];

        System.Linq.Enumerable.Select(asked, tick => history.LatestChange(tick, 0, -1)).ShouldBe([5, 1500, 5, 1500]);
    }

    /// <summary>
    /// **Far past a stretch with nothing changed in the gap, the stretch reaches the new tick** (B860 merge review 5): a
    /// value that changes at 5 and then rests to 2,000. Asked at 10 (5), at 2,000 (5 — the walk back finds no change after
    /// 10 and extends the stretch), then at 1,000 and 7 (5 each), then at 3 (the change before, 1).
    /// </summary>
    [Test]
    public void LatestChange_FarPastARestingRun_KeepsTheRun()
    {
        InterpolatedHistory history = new(1);

        for (int tick = 1; tick <= 2000; tick++)
        {
            history.Add(tick, tick, [tick >= 5 ? 2f : 1f]);
        }

        // Then back near the start and forward once more: 3 is 1, 2,000 is 5. Two changes held — 1 and 5 — however often
        // asked, so the walk from 2,000 stopped at the stretch below rather than walking on to 5 again (merge review 11).
        int[] asked = [10, 2000, 1000, 7, 3, 2000];

        int?[] answers = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(asked, tick => history.LatestChange(tick, 0, -1)));

        answers.ShouldBe([5, 5, 5, 5, 1, 5]);
        history.ChangesHeld(0, -1).ShouldBe(2);

        // Entries (index = tick - 1) examined: 9..4 for 10 (6), 1999..10 for 2,000, stopped by the stretch ending at 9
        // (1,990), 2..0 for 3 (3); the rest answer from the memo. 1,999 — a walk past the floor would make it 2,005.
        history.EntriesExamined.ShouldBe(1999L);
    }

    /// <summary>
    /// The engine compares a latch with the head by <c>memcmp</c> (<c>interpolatedvar.h:636</c>, and
    /// <c>COMPARE_HISTORY</c> at <c>:22-23</c>), so −0 after +0 is a change, a NaN after the same NaN is not, and a NaN
    /// after a NaN of other bits is — where float equality would answer the other way on the first and the last.
    /// </summary>
    [Test]
    public void LatestChange_NegativeZeroAfterZeroAndNaNAfterNaN_ComparesBits()
    {
        InterpolatedHistory history = new(1);
        history.Add(1, 1, [0f]);
        history.Add(2, 2, [-0f]);
        history.Add(3, 3, [float.NaN]);
        history.Add(4, 4, [float.NaN]);
        history.Add(5, 5, [System.BitConverter.Int32BitsToSingle(0x7FC00001)]);

        (history.LatestChange(2, 0, -1), history.LatestChange(4, 0, -1), history.LatestChange(5, 0, -1)).ShouldBe((2, 3, 5));
    }

    /// <summary>
    /// Stepping forward or back one entry at a time past a stretch costs no allocation: a step forward appends to the
    /// stretch below and a step back prepends to the one above (merge reviews 3-5 and review 24; B860 merge review 12). The
    /// list's own growth allocates now and then, so the least over a hundred steps is what must be zero.
    /// </summary>
    [Test]
    public void LatestChange_SteppedForwardThenBack_AllocatesNothingPerStep()
    {
        InterpolatedHistory history = new(1);

        for (int tick = 1; tick <= 4000; tick++)
        {
            history.Add(tick, tick, [tick]);
        }

        history.LatestChange(2000, 0, -1);
        long forward = LeastAllocated(history, 2001, 1);
        long back = LeastAllocated(history, 1999, -1);

        (forward, back, history.LatestChange(1950, 0, -1), history.LatestChange(2050, 0, -1)).ShouldBe((0L, 0L, 1950, 2050));
    }

    /// <summary>
    /// A stretch filling the gap below a larger one joins into it from beneath, and the answers inside both keep naming
    /// the latest change. Values change every tenth tick. 100 to 200 stepped forward holds 11 changes; 75 to 85 holds 70
    /// and 80; 99 finds 90 and prepends it to the stretch above; 89 fills the gap, and the two-change stretch joins the
    /// larger one from beneath, newest first. 72 is 70, 82 is 80, 95 and 99 are 90, 105 is 100; 14 changes held.
    /// </summary>
    [Test]
    public void LatestChange_GapFilledBelowALargerStretch_JoinsAndAnswersBoth()
    {
        InterpolatedHistory history = new(1);

        for (int tick = 1; tick <= 200; tick++)
        {
            history.Add(tick, tick, [tick / 10]);
        }

        int[] asked = [.. System.Linq.Enumerable.Range(100, 101), .. System.Linq.Enumerable.Range(75, 11), 99, 89];

        foreach (int tick in asked)
        {
            history.LatestChange(tick, 0, -1);
        }

        int?[] answers = [.. System.Linq.Enumerable.Select(new[] { 72, 82, 95, 99, 105 }, tick => history.LatestChange(tick, 0, -1))];

        answers.ShouldBe([70, 80, 90, 90, 100]);
        history.ChangesHeld(0, -1).ShouldBe(14);
    }

    private static long LeastAllocated(InterpolatedHistory history, int from, int step)
    {
        long least = long.MaxValue;

        for (int i = 0, tick = from; i < 100; i++, tick += step)
        {
            long before = System.GC.GetAllocatedBytesForCurrentThread();
            history.LatestChange(tick, 0, -1);
            least = System.Math.Min(least, System.GC.GetAllocatedBytesForCurrentThread() - before);
        }

        return least;
    }

    /// <summary>
    /// **Far before a stretch, a second stretch serves it and the first is kept** (B860 review 26, merge reviews 8-10):
    /// values change at 1, 300, 1,500 and 1,900 over 2,000 entries. Asked at 2,000 (1,900; one stretch, one change), then at
    /// 800 (300; a second stretch, two changes in all), then at 1,600 (1,500) and 2,000 (1,900) again.
    /// </summary>
    [Test]
    public void LatestChange_FarBeforeAStretch_KeepsASecondStretch()
    {
        InterpolatedHistory history = new(1);

        for (int tick = 1; tick <= 2000; tick++)
        {
            float value = 1f;

            if (tick >= 1900)
            {
                value = 4f;
            }
            else if (tick >= 1500)
            {
                value = 3f;
            }
            else if (tick >= 300)
            {
                value = 2f;
            }

            history.Add(tick, tick, [value]);
        }

        int? atEnd = history.LatestChange(2000, 0, -1);
        int? farBack = history.LatestChange(800, 0, -1);
        int held = history.ChangesHeld(0, -1);
        int?[] after = [history.LatestChange(1600, 0, -1), history.LatestChange(2000, 0, -1)];

        (atEnd, farBack, held, after[0], after[1]).ShouldBe((1900, 300, 2, 1500, 1900));
    }

    /// <summary>
    /// **A throw leaves the memo as it was** (B860 merge reviews 7, 8 and 11). Six-wide entries at ticks 1 to 4 with (3, 3)
    /// changing at 1 and 3, then `SetMaxCount(1)` at 10 and one-wide entries after. Asked for (3, 3) at 4 the memo holds the
    /// change at 3 (one). Asked at 11, the walk back from 11 throws on its first entry — which cannot hold (3, 3) — twice;
    /// the memo still holds one.
    /// </summary>
    [Test]
    public void LatestChange_RetriedAfterAThrow_LeavesTheMemoAsItWas()
    {
        InterpolatedHistory history = new(6);
        history.Add(1, 1, [0f, 0f, 0f, 1f, 0f, 0f]);
        history.Add(2, 2, [0f, 0f, 0f, 1f, 0f, 0f]);
        history.Add(3, 3, [0f, 0f, 0f, 2f, 0f, 0f]);
        history.Add(4, 4, [0f, 0f, 0f, 2f, 0f, 0f]);
        history.SetMaxCount(1, 10, [0f]);
        history.Add(11, 11, [0f]);

        int? seeded = history.LatestChange(4, 3, 3);
        int heldBefore = history.ChangesHeld(3, 3);

        Should.Throw<System.ArgumentOutOfRangeException>(() => history.LatestChange(11, 3, 3));
        Should.Throw<System.ArgumentOutOfRangeException>(() => history.LatestChange(11, 3, 3));

        (seeded, heldBefore, history.ChangesHeld(3, 3)).ShouldBe((3, 1, 1));
    }

    /// <summary>
    /// **Far-apart asks keep a stretch each, and asking again examines nothing new** (B860 merge reviews 10 and 11): values
    /// change at 1, 1,000, 2,000, 3,000, 4,000 and 5,000 over 5,000 entries. Asked at 4,500 (4,000), 2,500 (2,000) and 500 (1):
    /// three stretches, one change each, 501 + 501 + 500 = 1,502 entries examined. Then at 4,500 again (4,000), still three
    /// and still 1,502.
    /// </summary>
    [Test]
    public void LatestChange_ThreeFarApartAsks_KeepAStretchEach()
    {
        InterpolatedHistory history = new(1);

        for (int tick = 1; tick <= 5000; tick++)
        {
            history.Add(tick, tick, [tick / 1000]);
        }

        int?[] answers = [history.LatestChange(4500, 0, -1), history.LatestChange(2500, 0, -1), history.LatestChange(500, 0, -1)];
        int heldAfterThree = history.ChangesHeld(0, -1);
        long examinedAfterThree = history.EntriesExamined;
        int? again = history.LatestChange(4500, 0, -1);

        (answers[0], answers[1], answers[2], heldAfterThree, examinedAfterThree, again, history.ChangesHeld(0, -1), history.EntriesExamined)
            .ShouldBe((4000, 2000, 1, 3, 1502L, 4000, 3, 1502L));
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
