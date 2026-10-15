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

    private sealed class Viewer
    {
        public ReadOnlyMemory<byte> Slice { get; set; }

        public Array? Grid { get; set; }
    }

    /// <remarks>
    /// **The demo's bytes are input, not state**: a read-only slice is shared with what it views, so a snapshot does not
    /// copy the whole file the decoder's slices point into.
    /// </remarks>
    [Test]
    public void Of_ReadOnlyMemorySlice_SharesItsBackingArray()
    {
        byte[] file = [1, 2, 3, 4];
        Viewer copy = StateCopy.Of(new Viewer { Slice = file.AsMemory(1, 2) }, new HashSet<object>());

        System.Runtime.InteropServices.MemoryMarshal.TryGetArray(copy.Slice, out ArraySegment<byte> segment).ShouldBeTrue();
        segment.Array.ShouldBeSameAs(file);
    }

    /// <remarks>**No silent shallow copy**: a multi-dimensional array of references is refused rather than shared.</remarks>
    [Test]
    public void Of_TwoDimensionalArrayOfReferences_Throws()
    {
        Should.Throw<NotSupportedException>(() =>
            StateCopy.Of(new Viewer { Grid = Array.CreateInstance(typeof(object), 1, 1) }, new HashSet<object>()));
    }

    /// <remarks>
    /// **An immutable graph shared whole**: everything under a root the caller names as unchanging — the schema — is
    /// shared, including what is reached only through a struct, as a decoded property reaches its definition.
    /// </remarks>
    [Test]
    public void Of_ShareEverythingReachable_SharesObjectsUnderTheRoot()
    {
        List<int> inner = [5];
        Holder schema = new() { Pair = (1, inner) };
        Holder state = new() { Pair = (2, inner) };

        Holder copy = StateCopy.Of(state, StateCopy.Reachable(schema));

        copy.Pair.Tail.ShouldBeSameAs(inner);
        copy.First.ShouldNotBeSameAs(state.First);
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
