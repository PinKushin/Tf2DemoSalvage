using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>Chunk storage for the builder's growing lists (D217 step 3): a list's answers, and copies that share untouched chunks.</summary>
public sealed class ChunkedListTests
{
    private static ChunkedList<int> Upto(int count)
    {
        ChunkedList<int> list = [];

        for (int value = 0; value < count; value++)
        {
            list.Add(value);
        }

        return list;
    }

    /// <remarks>
    /// **A write behind the tail re-creates only its chunk** — what a heal beam's end and a scene's stop do: of three
    /// chunks, a write into the first leaves the other two shared with the earlier copy.
    /// </remarks>
    [Test]
    public void StateCopy_AfterAWriteBehindTheTail_CreatesOnlyThatChunk()
    {
        ChunkedList<int> list = Upto(3000);
        StateCopy.Of(list, new HashSet<object>(), prior: null, out Dictionary<object, object> map);

        list[5] = -5;
        Dictionary<Type, (long Objects, long Elements)> created = [];
        ChunkedList<int> later = StateCopy.Of(list, new HashSet<object>(), map, out _, created);

        created[typeof(int[])].ShouldBe((1, 1024));
        later[5].ShouldBe(-5);
        later.Count.ShouldBe(3000);
    }

    /// <remarks>**MoveOut hands over every element in order and leaves the list empty**, across a dropped front.</remarks>
    [Test]
    public void MoveOut_AfterADroppedFront_ReturnsTheRestInOrder()
    {
        ChunkedList<int> list = Upto(2100);
        list.DropFront(1030);

        List<int> moved = list.MoveOut();

        moved.ShouldBe(Enumerable.Range(1030, 1070));
        list.Count.ShouldBe(0);
    }

    /// <remarks>**Out of range throws**, rather than reading a slot a front drop cleared.</remarks>
    [Test]
    public void Index_AfterADroppedFront_ThrowsPastTheEnd()
    {
        ChunkedList<int> list = Upto(10);
        list.DropFront(4);

        Should.Throw<ArgumentOutOfRangeException>(() => list[6]);
        list[0].ShouldBe(4);
    }
}
