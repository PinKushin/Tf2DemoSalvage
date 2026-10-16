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

    /// <remarks>
    /// **Checkpoints share what did not change** (D217 step 3): of two lists, the one left alone is the earlier copy's own
    /// object, the one changed is a new copy holding the new values.
    /// </remarks>
    [Test]
    public void Of_WithPrior_SharesTheUnchangedListAndCopiesTheChangedOne()
    {
        Holder original = new() { First = [1], Second = [2] };
        Holder earlier = StateCopy.Of(original, new HashSet<object>(), prior: null, out Dictionary<object, object> map);

        original.Second.Add(3);
        Holder later = StateCopy.Of(original, new HashSet<object>(), map, out _);

        later.First.ShouldBeSameAs(earlier.First);
        later.Second.ShouldNotBeSameAs(earlier.Second);
        later.Second.ShouldBe([2, 3]);
        earlier.Second.ShouldBe([2]);
    }

    private sealed class Signed
    {
        public List<float> Values { get; } = [0f];
    }

    /// <remarks>**Bit for bit**: +0 and −0 compare equal as floats, and an earlier copy holding the other is not the same state.</remarks>
    [Test]
    public void Of_WithPriorAndAZeroThatChangedSign_CopiesAgain()
    {
        Signed original = new();
        Signed earlier = StateCopy.Of(original, new HashSet<object>(), prior: null, out Dictionary<object, object> map);

        original.Values[0] = -0f;
        Signed later = StateCopy.Of(original, new HashSet<object>(), map, out _);

        later.Values.ShouldNotBeSameAs(earlier.Values);
        BitConverter.SingleToInt32Bits(later.Values[0]).ShouldBe(BitConverter.SingleToInt32Bits(-0f));
    }

    /// <remarks>
    /// **Three copies, not two**: an object inside a reused one was never visited by the second copy, so it has to be put
    /// in that copy's map — or the third copy cannot recognise it, and copies the whole unchanged list again.
    /// </remarks>
    [Test]
    public void Of_ThirdCopyOfAnUnchangedNestedList_StillSharesIt()
    {
        Holder original = new();
        original.BySlot[1] = [4];
        Holder first = StateCopy.Of(original, new HashSet<object>(), prior: null, out Dictionary<object, object> map);
        StateCopy.Of(original, new HashSet<object>(), map, out map);

        original.First.Add(9);
        Holder third = StateCopy.Of(original, new HashSet<object>(), map, out _);

        third.BySlot.ShouldBeSameAs(first.BySlot);
        third.First.ShouldNotBeSameAs(first.First);
    }

    /// <remarks>
    /// **A primitive array is compared bit for bit**: one element of a float array going from 0 to −0 is a change, the
    /// engine's comparisons can tell them apart; an unchanged int array is reused.
    /// </remarks>
    [Test]
    public void Of_WithPriorAFloatArrayWhoseZeroChangedSign_CopiesItAndReusesTheUnchangedOne()
    {
        float[] floats = [1f, 0f, 2f];
        object[] root = [floats, new[] { 1, 2, 3 }];
        object[] first = StateCopy.Of(root, new HashSet<object>(), prior: null, out Dictionary<object, object> map);

        floats[1] = -0f;
        object[] second = StateCopy.Of(root, new HashSet<object>(), map, out _);

        second[0].ShouldNotBeSameAs(first[0]);
        BitConverter.SingleToInt32Bits(((float[])second[0])[1]).ShouldBe(BitConverter.SingleToInt32Bits(-0f));
        second[1].ShouldBeSameAs(first[1]);
    }

    /// <remarks>
    /// **An enum array is compared byte for byte too**: one element changing is a change and copies the array again; the
    /// other, unchanged, is reused.
    /// </remarks>
    [Test]
    public void Of_WithPriorAnEnumArrayWithOneElementChanged_CopiesItAndReusesTheUnchangedOne()
    {
        DayOfWeek[] changed = [DayOfWeek.Monday, DayOfWeek.Tuesday];
        object[] root = [changed, new[] { DayOfWeek.Friday }];
        object[] first = StateCopy.Of(root, new HashSet<object>(), prior: null, out Dictionary<object, object> map);

        changed[1] = DayOfWeek.Sunday;
        object[] second = StateCopy.Of(root, new HashSet<object>(), map, out _);

        second[0].ShouldNotBeSameAs(first[0]);
        ((DayOfWeek[])second[0]).ShouldBe([DayOfWeek.Monday, DayOfWeek.Sunday]);
        second[1].ShouldBeSameAs(first[1]);
    }

    /// <summary>A node of a cycle: the reference is declared first, so a comparison follows it before reading the value.</summary>
    private sealed class Node
    {
        public Node? Other { get; set; }

        public int Value { get; set; }
    }

    /// <remarks>
    /// **A cycle must not reuse what was judged against an assumption that failed**: A and B name each other; A's value
    /// changes. Comparing A assumes it equal while it follows the cycle, so B is judged unchanged — and once A is found
    /// changed, the earlier B, which names the earlier A, must not be reused, or the copy's cycle closes on stale A.
    /// </remarks>
    [Test]
    public void Of_WithPriorACycleWhoseOtherEndChanged_ClosesTheCycleOnTheNewCopy()
    {
        Node a = new() { Value = 1 };
        Node b = new() { Other = a, Value = 2 };
        a.Other = b;
        StateCopy.Of(a, new HashSet<object>(), prior: null, out Dictionary<object, object> map);

        a.Value = 10;
        Node copy = StateCopy.Of(a, new HashSet<object>(), map, out _);

        copy.Value.ShouldBe(10);
        copy.Other!.Other.ShouldBeSameAs(copy);
    }

    private static PropertyBag Bag()
    {
        PropertyBag bag = new();

        for (int key = 0; key < 100; key++)
        {
            bag.Set($"k{key}", Core.Schema.PropertyValue.FromInt(key), key);
        }

        return bag;
    }

    /// <remarks>
    /// **A frozen bag is shared, not copied** (D217 step 4): copying it with no prior creates the bag's shell and nothing
    /// under it — no key map, no chunk list, no chunk.
    /// </remarks>
    [Test]
    public void Of_FrozenBag_CreatesOnlyItsShell()
    {
        PropertyBag bag = Bag();
        bag.Freeze();
        Dictionary<Type, (long Objects, long Elements)> created = [];

        PropertyBag copy = StateCopy.Of(bag, new HashSet<object>(), prior: null, out _, created);

        created.Keys.ShouldBe([typeof(PropertyBag)]);
        copy["k99"].AsInt.ShouldBe(99);
    }

    /// <remarks>**A thawed bag is copied** like any other: its four chunks of 32 slots, with no prior to share.</remarks>
    [Test]
    public void Of_ThawedBag_CopiesItsChunks()
    {
        PropertyBag bag = Bag();
        bag.Freeze();
        bag.Set("k0", Core.Schema.PropertyValue.FromInt(-1), 101);
        Dictionary<Type, (long Objects, long Elements)> created = [];

        PropertyBag copy = StateCopy.Of(bag, new HashSet<object>(), prior: null, out _, created);

        created[typeof((Core.Schema.PropertyValue, long)[])].ShouldBe((4, 128));
        copy["k0"].AsInt.ShouldBe(-1);
    }

    /// <remarks>
    /// **A copy owns nothing it did not make**: copy 2 of a thawed bag reuses copy 1's chunk 0 (unchanged), and a write
    /// to copy 2 must copy that chunk first. An inherited "owned" would write straight into copy 1.
    /// </remarks>
    [Test]
    public void Set_OnACopyReusingAPriorChunk_LeavesThePriorCopyUnchanged()
    {
        PropertyBag bag = Bag();
        bag.Freeze();
        bag.Set("k0", Core.Schema.PropertyValue.FromInt(-1), 101);
        PropertyBag first = StateCopy.Of(bag, new HashSet<object>(), prior: null, out Dictionary<object, object> map);
        bag.Set("k40", Core.Schema.PropertyValue.FromInt(-40), 102);
        PropertyBag second = StateCopy.Of(bag, new HashSet<object>(), map, out _);

        second.Set("k1", Core.Schema.PropertyValue.FromInt(-2), 103);

        (first["k1"].AsInt, second["k1"].AsInt).ShouldBe((1L, -2L));
    }

    private sealed class BagHolder
    {
        public PropertyBag Properties { get; } = Bag();
    }

    /// <remarks>
    /// **The freeze site skips a bag under a parent reused unchanged, and that bag is still frozen** — the second
    /// checkpoint shares the first one's bag; a bag written between them is frozen again by the second.
    /// </remarks>
    [Test]
    public void Of_FreezingAgainOverAnUnchangedParent_LeavesItsBagFrozen()
    {
        BagHolder still = new();
        BagHolder written = new();
        List<BagHolder> both = [still, written];
        List<BagHolder> first = StateCopy.Of(both, new HashSet<object>(), prior: null, out Dictionary<object, object> map, freeze: true);
        written.Properties.Set("k0", Core.Schema.PropertyValue.FromInt(-1), 101);

        List<BagHolder> second = StateCopy.Of(both, new HashSet<object>(), map, out _, freeze: true);

        (still.Properties.IsFrozen, written.Properties.IsFrozen).ShouldBe((true, true));
        second[0].ShouldBeSameAs(first[0]);
        second[1].Properties["k0"].AsInt.ShouldBe(-1);
    }
}
