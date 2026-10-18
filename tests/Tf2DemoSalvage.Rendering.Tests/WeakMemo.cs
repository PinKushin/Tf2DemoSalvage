using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// One load per key while anyone holds the result, and none once nobody does — the release half of <see cref="MapCache"/>.
/// </summary>
/// <typeparam name="T">What is loaded.</typeparam>
/// <remarks>
/// **Weak, because a strong cache kept every map the run ever asked for** — 26 distinct requests, each a whole map, held
/// until the process ended: the test host measured 18.7 GB at peak, 2026-10-17, enough to starve a UI suite beside it
/// (D213). Tests that run together still share one load, because each holds the result while it runs; once the last one
/// finishes, the collector may take it, and a later ask loads it again.
///
/// **Two asks at once still load once**: the load in flight is a <see cref="Lazy{T}"/> both wait on. A failed load is not
/// remembered, so the next ask tries again.
/// </remarks>
internal sealed class WeakMemo<T>
    where T : class
{
    private readonly Lock _gate = new();

    private readonly Dictionary<string, WeakReference<T>> _held = new(StringComparer.Ordinal);

    private readonly Dictionary<string, Lazy<T>> _loading = new(StringComparer.Ordinal);

    /// <summary>How many loads have run — for a test of the memo itself.</summary>
    public int Loads => Volatile.Read(ref _loads);

    private int _loads;

    /// <summary>How many asks are waiting on a load in flight — for a test that must know the second has arrived.</summary>
    public int Waiting => Volatile.Read(ref _waiting);

    private int _waiting;

    /// <summary>How many keys the memo still tracks — for a test that dead entries are pruned.</summary>
    public int Tracked
    {
        get
        {
            lock (_gate)
            {
                return _held.Count;
            }
        }
    }

    /// <summary>The value for a key: the one still alive, the one being loaded, or a new load.</summary>
    /// <param name="key">Everything that changes the result.</param>
    /// <param name="load">Loads it.</param>
    /// <returns>The value, shared with every other holder of the same key.</returns>
    public T Get(string key, Func<T> load)
    {
        Lazy<T>? loading;

        lock (_gate)
        {
            if (_held.TryGetValue(key, out WeakReference<T>? weak) && weak.TryGetTarget(out T? alive))
            {
                return alive;
            }

            // A collected entry is pruned, so the table holds only what is alive.
            foreach (string dead in _held.Where(entry => !entry.Value.TryGetTarget(out _)).Select(entry => entry.Key).ToList())
            {
                _held.Remove(dead);
            }

            if (!_loading.TryGetValue(key, out loading))
            {
                loading = new Lazy<T>(
                    () =>
                    {
                        Interlocked.Increment(ref _loads);
                        return load();
                    },
                    LazyThreadSafetyMode.ExecutionAndPublication);
                _loading[key] = loading;
            }
        }

        T? value = null;
        Interlocked.Increment(ref _waiting);

        try
        {
            value = loading.Value;
            return value;
        }
        finally
        {
            Interlocked.Decrement(ref _waiting);

            lock (_gate)
            {
                if (value is not null)
                {
                    _held[key] = new WeakReference<T>(value);
                }

                if (_loading.TryGetValue(key, out Lazy<T>? current) && ReferenceEquals(current, loading))
                {
                    _loading.Remove(key);
                }
            }
        }
    }
}
