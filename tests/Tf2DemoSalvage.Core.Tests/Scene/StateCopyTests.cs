using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>The builder's deep copy (D217): independent of the original, shaped like it, and sharing what it is told to.</summary>
public sealed class StateCopyTests
{
    private sealed class Holder
    {
        public List<int> First { get; set; } = [];

        public List<int> Second { get; set; } = [];

        public Dictionary<int, List<int>> BySlot { get; } = [];

        public (int Count, List<int>? Tail) Pair { get; set; }

        public string Name { get; set; } = "a";
    }

    /// <remarks>**Independence**: a change to the original after the copy does not reach the copy, through a list in a struct either.</remarks>
    [Test]
    public void Of_OriginalChangedAfterwards_CopyKeepsTheOldValues()
    {
        Holder original = new() { First = [1], Pair = (1, [7]) };
        Holder copy = StateCopy.Of(original, new HashSet<object>());

        original.First.Add(2);
        original.Pair.Tail.Add(8);

        (copy.First.Count, copy.Pair.Tail!.Count).ShouldBe((1, 1));
    }

    /// <remarks>**Shape**: one object reached twice is one copy, as a track in both the slot map and the track list is.</remarks>
    [Test]
    public void Of_OneListReachedTwice_IsOneCopy()
    {
        List<int> shared = [3];
        Holder original = new() { First = shared, Second = shared };
        original.BySlot[5] = shared;

        Holder copy = StateCopy.Of(original, new HashSet<object>());

        copy.First.ShouldBeSameAs(copy.Second);
        copy.BySlot[5].ShouldBeSameAs(copy.First);
        copy.First.ShouldNotBeSameAs(shared);
    }

    /// <remarks>**Sharing on request**: what the caller names is handed over, not copied — the builder's output lists.</remarks>
    [Test]
    public void Of_ListNamedAsShared_IsTheSameInstance()
    {
        Holder original = new() { First = [1] };

        Holder copy = StateCopy.Of(original, new HashSet<object>(ReferenceEqualityComparer.Instance) { original.First });

        copy.First.ShouldBeSameAs(original.First);
        copy.Second.ShouldNotBeSameAs(original.Second);
    }
}
