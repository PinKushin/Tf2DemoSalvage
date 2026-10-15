using System;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>The builder's output lists accept only edits at their tail — the invariant snapshot marks stand on (D217).</summary>
public sealed class TailListTests
{
    private static TailList<int> Three() => [1, 2, 3];

    /// <remarks>**The edits the builder makes**: append, replace the last, drop the last.</remarks>
    [Test]
    public void Edit_AtTheTail_IsAllowed()
    {
        TailList<int> list = Three();

        list[^1] = 30;
        list.RemoveAt(list.Count - 1);
        list.Add(4);

        list.ShouldBe([1, 2, 4]);
    }

    /// <remarks>**What `choreography` did**: a write behind the last element.</remarks>
    [Test]
    public void Replace_BeforeTheLast_Throws()
    {
        TailList<int> list = Three();

        Should.Throw<InvalidOperationException>(() => list[0] = 10);
    }

    /// <remarks>**A removal behind the last element**, which would shift every mark after it.</remarks>
    [Test]
    public void RemoveAt_BeforeTheLast_Throws()
    {
        TailList<int> list = Three();

        Should.Throw<InvalidOperationException>(() => list.RemoveAt(1));
    }

    /// <remarks>
    /// **A tick cut keeps the value in force** (D217 step 3): of (tick 10, 20, 30, 40), a cut at 25 keeps 20 — what holds at
    /// 25 — and everything after.
    /// </remarks>
    [Test]
    public void DropBefore_CutBetweenTwo_KeepsTheOneInForceAndAfter()
    {
        TailList<int> list = [10, 20, 30, 40];

        list.DropBefore(tick => tick, 25);

        list.ShouldBe([20, 30, 40]);
    }

    /// <remarks>
    /// **By stream, each stream's own value in force** (f12's viewmodels: two players' records interleaved, written only on
    /// change): stream A last wrote at 10, B at 20; a cut at 25 must keep both, where a tick cut keeps only B's.
    /// </remarks>
    [Test]
    public void DropBefore_ByStreamWithAQuietStream_KeepsEachStreamsLastBeforeTheCut()
    {
        TailList<(int Tick, char Stream)> list = [(5, 'A'), (10, 'A'), (15, 'B'), (20, 'B'), (30, 'A')];

        list.DropBefore(entry => entry.Tick, entry => entry.Stream, 25);

        list.ShouldBe([(10, 'A'), (20, 'B'), (30, 'A')]);
    }

    /// <remarks>**The last element is never dropped**: the builder may still replace it, whatever its tick.</remarks>
    [Test]
    public void DropBefore_EverythingBeforeTheCut_KeepsTheLast()
    {
        TailList<int> list = [10, 20, 30];

        list.DropBefore(tick => tick, 100);

        list.ShouldBe([30]);
    }
}
