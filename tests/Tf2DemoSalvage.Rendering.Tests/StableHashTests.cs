using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// The frame-hash and queue-verifier instrument's hash (B581, finding 98 §9): the same content must give the same value,
/// in any call order, or the verifier reports a frame that changed when nothing did.
/// </summary>
/// <remarks>
/// **Found by an elementwise-equal copy that hashed to two values.** On f12 the verifier's one "changed" frame per run
/// came from a list whose snapshot copy compared equal element by element, yet whose hash differed between the first
/// call and every later one, on both threads. The batches were hashed as a <c>ValueTuple</c>'s raw bytes; the fix writes
/// each field on its own, so these tests predict the exact FNV-1a value over the fields' bytes.
/// </remarks>
public sealed class StableHashTests
{
    private const ulong Offset = 14695981039346656037UL;
    private const ulong Prime = 1099511628211UL;

    [Test]
    public void Batches_TwoRuns_HashToTheFnvOfTheirCountAndFieldBytes()
    {
        IReadOnlyList<WorldBatch> batches = [new(7, 100, 30), new(9, 130, 6)];

        long expected = Fnv(2, 7, 100, 30, 9, 130, 6);

        StableHash.OfBatches(batches).ShouldBe(expected);
    }

    [Test]
    public void Batches_Null_HashToTheFnvOfMinusOne() =>
        StableHash.OfBatches(null).ShouldBe(Fnv(-1));

    [Test]
    public void Batches_HashedRepeatedly_GiveOneValue()
    {
        WorldBatch[] batches = [new(7, 100, 30), new(9, 130, 6), new(3, 0, 99)];

        long first = StableHash.OfBatches(batches);

        for (int at = 0; at < 50; at++)
        {
            StableHash.OfBatches([.. batches]).ShouldBe(first);
        }
    }

    /// <summary>FNV-1a over the little-endian bytes of each int, in order.</summary>
    private static long Fnv(params int[] values)
    {
        ulong hash = Offset;

        foreach (int value in values)
        {
            foreach (byte b in BitConverter.GetBytes(value))
            {
                hash = (hash ^ b) * Prime;
            }
        }

        return (long)hash;
    }
}
