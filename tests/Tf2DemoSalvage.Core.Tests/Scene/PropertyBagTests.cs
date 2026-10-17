using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>An entity's property storage (D217 step 3): the old dictionary's order and answers, in chunks a checkpoint shares.</summary>
public sealed class PropertyBagTests
{
    /// <remarks>
    /// **Insertion order, an overwrite keeping its place** — the dictionary's, which readers that take the first matching
    /// key depend on.
    /// </remarks>
    [Test]
    public void Enumerate_AfterAnOverwrite_IsInsertionOrder()
    {
        PropertyBag bag = new();
        bag.Set("b", PropertyValue.FromInt(1), 1);
        bag.Set("a", PropertyValue.FromInt(2), 2);
        bag.Set("b", PropertyValue.FromInt(3), 3);

        bag.Select(entry => (entry.Key, entry.Value.AsInt)).ShouldBe([("b", 3L), ("a", 2L)]);
        (bag.Sequence("b"), bag.Sequence("a"), bag.Sequence("c")).ShouldBe((3L, 2L, -1L));
    }

    /// <remarks>**Clear restarts the order**, as `Dictionary.Clear` does, and forgets every sequence.</remarks>
    [Test]
    public void Enumerate_AfterClear_RestartsFromTheNextWrite()
    {
        PropertyBag bag = new();
        bag.Set("a", PropertyValue.FromInt(1), 1);
        bag.Set("b", PropertyValue.FromInt(2), 2);
        bag.Clear();
        bag.Set("b", PropertyValue.FromInt(5), 3);
        bag.Set("a", PropertyValue.FromInt(6), 4);

        bag.Keys.ShouldBe(["b", "a"]);
        bag.TryGetValue("z", out _).ShouldBeFalse();
    }

    /// <remarks>
    /// **What it is for**: a second copy after one write shares the key map and every chunk but the written one — one
    /// 32-slot chunk created for 100 properties.
    /// </remarks>
    [Test]
    public void StateCopy_AfterOneWrite_CreatesOneChunk()
    {
        PropertyBag bag = new();

        for (int key = 0; key < 100; key++)
        {
            bag.Set($"k{key}", PropertyValue.FromInt(key), key);
        }

        StateCopy.Of(bag, new HashSet<object>(), prior: null, out Dictionary<object, object> map);
        bag.Set("k50", PropertyValue.FromInt(-1), 101);
        Dictionary<Type, (long Objects, long Elements)> created = [];
        PropertyBag later = StateCopy.Of(bag, new HashSet<object>(), map, out _, created);

        // The chunk; the list holding the chunks is re-created too, since one of its references changed.
        created[typeof((PropertyValue, long)[])].ShouldBe((1, 32));
        created.ContainsKey(typeof(Dictionary<string, int>)).ShouldBeFalse();
        later["k50"].AsInt.ShouldBe(-1);
    }

    /// <summary>40 keys — two chunks — of every kind, the floats chosen to differ only in their bits.</summary>
    private static PropertyBag Filled()
    {
        PropertyBag bag = new();

        for (int key = 0; key < 40; key++)
        {
            bag.Set($"k{key}", PropertyValue.FromInt(key * 3), 100 + key);
        }

        bag.Set("negative zero", PropertyValue.FromFloat(-0f), 200);
        bag.Set("nan", PropertyValue.FromFloat(BitConverter.Int32BitsToSingle(0x7FC0_1234)), 201);
        bag.Set("vector", PropertyValue.FromVector(1.5f, -0f, 3), 202);
        bag.Set("name", PropertyValue.FromString("scout"), 203);
        bag.Set("array", PropertyValue.FromArray([PropertyValue.FromInt(7)]), 204);
        bag.Set("k3", PropertyValue.FromInt(-3), 205);
        return bag;
    }

    private static string Describe(PropertyBag bag) => string.Join(
        ";",
        bag.Select(entry => $"{entry.Key}={entry.Value.Kind}:{Bits(entry.Value)}@{bag.Sequence(entry.Key)}"));

    private static string Bits(PropertyValue value) => value.Kind switch
    {
        PropertyValueKind.Float => $"{BitConverter.SingleToInt32Bits(value.AsFloat):X8}",
        PropertyValueKind.Vector => $"{BitConverter.SingleToInt32Bits(value.AsVector.X):X8},{BitConverter.SingleToInt32Bits(value.AsVector.Y):X8},{BitConverter.SingleToInt32Bits(value.AsVector.Z):X8}",
        PropertyValueKind.Array => $"[{string.Join(",", value.AsArray.Select(item => item.AsInt))}]",
        _ => value.ToString(),
    };

    /// <remarks>
    /// **A frozen bag answers exactly as before it froze** (D217 step 4): every key, the order, every sequence, floats bit
    /// for bit (a negative zero, a NaN's payload), strings and arrays.
    /// </remarks>
    [Test]
    public void Freeze_EveryKind_AnswersAsBefore()
    {
        PropertyBag bag = Filled();
        string before = Describe(bag);

        bag.Freeze();

        Describe(bag).ShouldBe(before);
        (bag.Count, bag.ContainsKey("k39"), bag["k3"].AsInt, bag.Sequence("absent")).ShouldBe((45, true, -3L, -1L));
        bag.TryGetValue("name", out PropertyValue name).ShouldBeTrue();
        name.AsString.ShouldBe("scout");
        bag.TryGetValue("absent", out _).ShouldBeFalse();
    }

    /// <remarks>
    /// **A write after freezing thaws a copy and never touches the frozen form**, which a checkpoint's copy still reads:
    /// an overwrite in each chunk and a new key, then the copy answers as at the freeze and the bag as after the writes.
    /// </remarks>
    [Test]
    public void Set_AfterFreeze_LeavesTheFrozenFormUnchanged()
    {
        PropertyBag bag = Filled();
        bag.Freeze();
        string frozen = Describe(bag);
        PropertyBag checkpoint = StateCopy.Of(bag, new HashSet<object>());

        bag.Set("k0", PropertyValue.FromInt(1000), 300);
        bag.Set("k35", PropertyValue.FromInt(2000), 301);
        bag.Set("new", PropertyValue.FromInt(3000), 302);

        Describe(checkpoint).ShouldBe(frozen);
        (bag["k0"].AsInt, bag["k35"].AsInt, bag.Sequence("new"), bag.Keys.Last(), bag.Count).ShouldBe((1000L, 2000L, 302L, "new", 46));
        bag["k1"].AsInt.ShouldBe(3);
    }

    /// <remarks>
    /// **A second freeze packs what was written and reuses what was not** (D217 step 4, part 3): the written chunk reads
    /// the new value, the untouched one the old, and the earlier checkpoint's copy still reads as at its freeze.
    /// </remarks>
    [Test]
    public void Freeze_AgainAfterOneWrite_KeepsTheWriteAndEverythingElse()
    {
        PropertyBag bag = Filled();
        bag.Freeze();
        string frozen = Describe(bag);
        PropertyBag checkpoint = StateCopy.Of(bag, new HashSet<object>());
        bag.Set("k35", PropertyValue.FromInt(2000), 301);

        bag.Freeze();

        (bag["k35"].AsInt, bag.Sequence("k35"), bag["k0"].AsInt, bag.Sequence("k0")).ShouldBe((2000L, 301L, 0L, 100L));
        Describe(checkpoint).ShouldBe(frozen);
    }

    /// <remarks>
    /// **A clear then the same keys in the same order re-uses the map** (D217 step 4, part 3): the second checkpoint of a
    /// re-entered entity holds the same key map as before, and reads as freshly written.
    /// </remarks>
    [Test]
    public void Clear_ThenTheSameKeysInOrder_KeepsTheMap()
    {
        PropertyBag bag = Filled();
        bag.Freeze();
        object before = bag.SlotMap;

        bag.Clear();
        bag.Set("k0", PropertyValue.FromInt(10), 400);
        bag.Set("k1", PropertyValue.FromInt(11), 401);
        bag.Freeze();
        PropertyBag later = StateCopy.Of(bag, new HashSet<object>());

        bag.SlotMap.ShouldBeSameAs(before);
        Describe(later).ShouldBe("k0=Int:10@400;k1=Int:11@401");
        (later.Count, later.ContainsKey("k2"), later.Sequence("k2"), later.TryGetValue("name", out _)).ShouldBe((2, false, -1L, false));
    }

    /// <remarks>
    /// **Out of the old order, the bag gets a map of its own** holding only its visible keys, in the new order — and the
    /// map the earlier freeze shares is unchanged.
    /// </remarks>
    [Test]
    public void Clear_ThenKeysOutOfTheOldOrder_EnumeratesTheNewOrder()
    {
        PropertyBag bag = Filled();
        bag.Freeze();
        string frozen = Describe(bag);
        PropertyBag checkpoint = StateCopy.Of(bag, new HashSet<object>());

        bag.Clear();
        bag.Set("k0", PropertyValue.FromInt(10), 400);
        bag.Set("k5", PropertyValue.FromInt(15), 401);
        bag.Set("fresh", PropertyValue.FromInt(16), 402);

        bag.Keys.ShouldBe(["k0", "k5", "fresh"]);
        (bag.ContainsKey("k1"), bag["k5"].AsInt).ShouldBe((false, 15L));
        Describe(checkpoint).ShouldBe(frozen);
    }

    /// <remarks>
    /// **An owned map re-filled in its own order takes a new key in place** (D217 step 4, part 3): a bag never frozen,
    /// cleared, sent every key again in order, then one more — the same map, all keys in order, the new one last.
    /// </remarks>
    [Test]
    public void Set_ANewKeyAfterTheOldOnesInOrder_AddsToTheOwnedMapInPlace()
    {
        PropertyBag bag = new();
        bag.Set("a", PropertyValue.FromInt(1), 1);
        bag.Set("b", PropertyValue.FromInt(2), 2);
        object map = bag.SlotMap;

        bag.Clear();
        bag.Set("a", PropertyValue.FromInt(3), 3);
        bag.Set("b", PropertyValue.FromInt(4), 4);
        bag.Set("c", PropertyValue.FromInt(5), 5);

        bag.SlotMap.ShouldBeSameAs(map);
        Describe(bag).ShouldBe("a=Int:3@3;b=Int:4@4;c=Int:5@5");
    }

    /// <remarks>
    /// **A read fills the unpack cache, and a write after it neither reads stale nor reaches the checkpoint**: k0 read
    /// from the frozen form, then overwritten, reads the write; k1 in the same chunk keeps its value; the copy taken at
    /// the freeze still reads as then.
    /// </remarks>
    [Test]
    public void Set_AfterAReadOfTheFrozenChunk_ReadsTheWriteAndLeavesTheCheckpoint()
    {
        PropertyBag bag = Filled();
        bag.Freeze();
        string frozen = Describe(bag);
        PropertyBag checkpoint = StateCopy.Of(bag, new HashSet<object>());
        bag["k0"].AsInt.ShouldBe(0, "the read that fills the cache");

        bag.Set("k0", PropertyValue.FromInt(77), 500);

        (bag["k0"].AsInt, bag.Sequence("k0"), bag["k1"].AsInt).ShouldBe((77L, 500L, 3L));
        Describe(checkpoint).ShouldBe(frozen);
    }

    /// <remarks>
    /// **Hidden keys answer nothing, frozen or not**: after a clear and one key back, every reader — count, keys, values,
    /// enumeration, lookups, sequences — sees one key, though the map still holds the others.
    /// </remarks>
    [Test]
    public void Clear_ThenOneKeyBack_EveryReaderSeesOnlyThatKey()
    {
        PropertyBag bag = Filled();
        bag.Clear();
        bag.Set("k0", PropertyValue.FromInt(9), 600);

        foreach (bool frozen in new[] { false, true })
        {
            if (frozen)
            {
                bag.Freeze();
            }

            (bag.Count, string.Join(",", bag.Keys), bag.Values.Count(), bag.Select(entry => entry.Key).Count(),bag.ContainsKey("k1"), bag.TryGetValue("k1", out _), bag.Sequence("k1"))
                .ShouldBe((1, "k0", 1, 1, false, false, -1L), $"frozen {frozen}");
            Should.Throw<KeyNotFoundException>(() => bag["k39"]);
        }
    }

    /// <remarks>
    /// **Read from many threads at once, a frozen bag answers the same every time** (D217 step 4): the unpack cache is
    /// written by the first read of each chunk, so sixteen threads racing to fill it must each see every value as frozen.
    /// </remarks>
    [Test]
    public void Read_SixteenThreadsAtOnceOnAFrozenBag_EveryReadIsTheFrozenValue()
    {
        PropertyBag bag = Filled();
        bag.Freeze();
        PropertyBag reference = StateCopy.Of(bag, new HashSet<object>());
        string expected = Describe(reference);

        string[] seen = new string[16];
        System.Threading.Tasks.Parallel.For(0, seen.Length, thread => seen[thread] = Describe(bag));

        seen.ShouldAllBe(described => described == expected);
    }

    /// <remarks>**Clear after a freeze restarts the order on the bag alone**; the frozen form keeps every key.</remarks>
    [Test]
    public void Clear_AfterFreeze_ThenSet_RestartsOnlyTheBag()
    {
        PropertyBag bag = Filled();
        bag.Freeze();
        string frozen = Describe(bag);
        PropertyBag checkpoint = StateCopy.Of(bag, new HashSet<object>());

        bag.Clear();
        bag.Set("b", PropertyValue.FromInt(5), 1);
        bag.Set("k0", PropertyValue.FromInt(6), 2);

        bag.Keys.ShouldBe(["b", "k0"]);
        bag.Sequence("k1").ShouldBe(-1);
        Describe(checkpoint).ShouldBe(frozen);
    }
}
