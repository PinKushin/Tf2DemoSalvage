using System;
using System.Collections.Generic;
using System.Threading;

namespace Tf2DemoSalvage.SdkReference;

/// <summary>
/// Keeps the values a factory built for the keys asked most recently, and no more than
/// <c>capacity</c> of them.
/// </summary>
/// <typeparam name="TKey">What a value is built from, such as a demo's path.</typeparam>
/// <typeparam name="TValue">What is built, such as that demo's timeline.</typeparam>
/// <remarks>
/// **Written for the corpus suite's timelines** (B439), which run to 4.7 GB apiece, so the cache has
/// to let go of them where an unbounded one reached 39 GB and was stopped.
///
/// Four rules, each pinned by <c>LruCacheTests</c> in Core.Tests:
///
/// - **A key is built once while the cache has it**, and callers arriving during that build wait for
///   it rather than starting another.
/// - **At most <c>capacity</c> BUILT values are kept.** A value still building is never evicted and
///   does not count, since its callers hold it anyway; nor does a build that threw, whose exception
///   is kept and rethrown to every later caller, as the unbounded cache did.
/// - **A released value somebody still holds comes back** instead of being built a second time — two
///   copies of a timeline is the cost the bound exists to avoid. The cache keeps only a weak
///   reference to it, so a value nobody holds is collected.
/// - **Builds of different keys run at the same time**; the lock guards the bookkeeping, never a build.
/// </remarks>
public sealed class LruCache<TKey, TValue>
    where TKey : notnull
    where TValue : class
{
    private readonly int _capacity;
    private readonly Func<TKey, TValue> _build;
    private readonly Lock _gate = new();

    /// <summary>What the cache holds, most recently used first.</summary>
    private readonly LinkedList<(TKey Key, Lazy<TValue> Value)> _recency = new();
    private readonly Dictionary<TKey, LinkedListNode<(TKey Key, Lazy<TValue> Value)>> _kept;

    /// <summary>What the cache let go of, in case somebody still holds it.</summary>
    private readonly Dictionary<TKey, WeakReference<TValue>> _released;

    /// <summary>Creates an empty cache.</summary>
    /// <param name="capacity">How many built values to keep; at least one.</param>
    /// <param name="build">Builds the value for a key.</param>
    /// <param name="comparer">How keys compare, or <c>null</c> for the default.</param>
    public LruCache(int capacity, Func<TKey, TValue> build, IEqualityComparer<TKey>? comparer = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        ArgumentNullException.ThrowIfNull(build);

        _capacity = capacity;
        _build = build;
        _kept = new(comparer);
        _released = new(comparer);
    }

    /// <summary>The value for a key, built only if the cache has not got it.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The value.</returns>
    public TValue Get(TKey key)
    {
        Lazy<TValue> value;

        lock (_gate)
        {
            if (_kept.TryGetValue(key, out LinkedListNode<(TKey Key, Lazy<TValue> Value)>? node))
            {
                _recency.Remove(node);
                _recency.AddFirst(node);
            }
            else
            {
                node = _recency.AddFirst((key, Revived(key) ?? new Lazy<TValue>(
                    () => _build(key), LazyThreadSafetyMode.ExecutionAndPublication)));
                _kept.Add(key, node);
            }

            value = node.Value.Value;
        }

        try
        {
            return value.Value;
        }
        finally
        {
            // After the value is published, so a value released here is released with a weak
            // reference a later caller can still find.
            lock (_gate)
            {
                Trim();
            }
        }
    }

    /// <summary>
    /// The keys in the order that shares the most: at every step, one the cache already has — kept,
    /// building, or released but still held — before one it would have to build.
    /// </summary>
    /// <param name="keys">Every key a sweep must visit, in the order it would otherwise take.</param>
    /// <returns>The same keys, each once, decided one step at a time.</returns>
    /// <remarks>
    /// **Why it exists** (B439): the corpus suite runs a dozen sweeps over every demo at once. With a
    /// small capacity, sweeps that start minutes apart would each build all 59 timelines; asked in
    /// this order, a late sweep joins whatever the others are holding, so a timeline is built once
    /// for all of them. Decided lazily, after the caller has used the previous key.
    /// </remarks>
    public IEnumerable<TKey> WarmFirst(IEnumerable<TKey> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);

        return Sweep([.. keys]);

        IEnumerable<TKey> Sweep(List<TKey> remaining)
        {
            while (remaining.Count > 0)
            {
                int next;

                lock (_gate)
                {
                    next = Math.Max(0, remaining.FindIndex(IsWarm));
                }

                TKey key = remaining[next];
                remaining.RemoveAt(next);

                yield return key;
            }
        }
    }

    private bool IsWarm(TKey key) =>
        _kept.ContainsKey(key) ||
        (_released.TryGetValue(key, out WeakReference<TValue>? weak) && weak.TryGetTarget(out _));

    private Lazy<TValue>? Revived(TKey key) =>
        _released.Remove(key, out WeakReference<TValue>? weak) && weak.TryGetTarget(out TValue? alive)
            ? new Lazy<TValue>(alive)
            : null;

    /// <summary>Releases built values past the capacity, least recently used first.</summary>
    private void Trim()
    {
        int built = 0;

        for (LinkedListNode<(TKey Key, Lazy<TValue> Value)>? node = _recency.First; node is not null;)
        {
            LinkedListNode<(TKey Key, Lazy<TValue> Value)>? next = node.Next;
            (TKey key, Lazy<TValue> value) = node.Value;

            if (value.IsValueCreated && ++built > _capacity)
            {
                _recency.Remove(node);
                _kept.Remove(key);
                _released[key] = new WeakReference<TValue>(value.Value);
            }

            node = next;
        }
    }
}
