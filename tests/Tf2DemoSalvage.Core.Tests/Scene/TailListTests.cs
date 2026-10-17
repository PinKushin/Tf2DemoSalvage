using System;
using System.Collections.Generic;
using System.Linq;

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

    /// <remarks>
    /// **A stream cut shares the chunks after the cut** with the copy before it: 3,000 records of two streams, a cut at 100
    /// that keeps one per stream, and the next copy creates only the front chunk the kept records were written into.
    /// </remarks>
    [Test]
    public void DropBefore_ByStream_LeavesLaterChunksShared()
    {
        TailList<(int Tick, int Stream)> list = [];

        for (int tick = 0; tick < 3000; tick++)
        {
            list.Add((tick, tick % 2));
        }

        StateCopy.Of(list, new HashSet<object>(), prior: null, out Dictionary<object, object> map);
        list.DropBefore(entry => entry.Tick, entry => entry.Stream, 100);
        Dictionary<Type, (long Objects, long Elements)> created = [];
        TailList<(int Tick, int Stream)> later = StateCopy.Of(list, new HashSet<object>(), map, out _, created);

        later.Take(3).ShouldBe([(98, 0), (99, 1), (100, 0)]);
        later.Count.ShouldBe(2902);
        created[typeof((int, int)[])].ShouldBe((1, ChunkedList<int>.ChunkLength));
    }

    /// <remarks>
    /// **The in-place splice never writes a checkpoint's chunks**: a checkpoint copies the live chunks (and a later one
    /// reuses only an EARLIER COPY's), so after two checkpoints, a by-stream trim of the live list leaves both reading
    /// every record they held.
    /// </remarks>
    [Test]
    public void DropBefore_ByStreamAfterCheckpoints_LeavesTheCheckpointsWhole()
    {
        TailList<(int Tick, int Stream)> list = [];

        for (int tick = 0; tick < 3000; tick++)
        {
            list.Add((tick, tick % 2));
        }

        TailList<(int Tick, int Stream)> first = StateCopy.Of(list, new HashSet<object>(), prior: null, out Dictionary<object, object> map);
        TailList<(int Tick, int Stream)> second = StateCopy.Of(list, new HashSet<object>(), map, out _);

        list.DropBefore(entry => entry.Tick, entry => entry.Stream, 100);

        list.Count.ShouldBe(2902, "the control: the trim ran");
        first.ShouldBe(Enumerable.Range(0, 3000).Select(tick => (tick, tick % 2)));
        second.ShouldBe(Enumerable.Range(0, 3000).Select(tick => (tick, tick % 2)));
    }

    /// <remarks>
    /// **Across chunk boundaries** (chunks of 1,024): 3,000 elements, a cut dropping 1,500 of them and the last removed,
    /// read back in order with the right count.
    /// </remarks>
    [Test]
    public void DropBefore_AcrossChunks_KeepsOrderAndCount()
    {
        TailList<int> list = [];

        for (int value = 0; value < 3000; value++)
        {
            list.Add(value);
        }

        list.DropBefore(tick => tick, 1501);
        list.RemoveAt(list.Count - 1);

        list.ShouldBe(Enumerable.Range(1500, 1499));
    }

    /// <remarks>
    /// **What the chunks are for**: a second checkpoint of a grown list shares every full chunk with the first (D217 step 3),
    /// so copying a long append-only list again costs one chunk, not the list.
    /// </remarks>
    [Test]
    public void StateCopy_TwoCopiesOfAGrownList_ShareTheFullChunks()
    {
        TailList<int> list = [];

        for (int value = 0; value < 2500; value++)
        {
            list.Add(value);
        }

        StateCopy.Of(list, new HashSet<object>(), prior: null, out Dictionary<object, object> map);
        list.Add(2500);
        Dictionary<Type, (long Objects, long Elements)> created = [];
        TailList<int> later = StateCopy.Of(list, new HashSet<object>(), map, out _, created);

        created[typeof(int[])].ShouldBe((1, ChunkedList<int>.ChunkLength));
        later.ShouldBe(Enumerable.Range(0, 2501));
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
