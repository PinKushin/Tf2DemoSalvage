using System;
using System.Collections.Generic;
using System.Numerics;

namespace Tf2DemoSalvage.Scene;

/// <summary>The engine's model light cache: which entry, of a fixed 200, lights a point this frame.</summary>
/// <typeparam name="T">What an entry holds once built.</typeparam>
/// <remarks>
/// **`LightcacheGet`, `engine.dll` `0x1801b9cd0`.** An entry is keyed on the cell `((int)c + 0x8000) >> 5` (`>> 7` in z)
/// and the point's leaf. A hit moves it to the front (`0x1801baeb0`). A miss takes the entry at the back
/// (`DAT_180773316`), moves it to the front and builds it — but only `lightcache_maxmiss` times a frame (default 2,
/// `0x18000fde0`) once 60 frames have run; past that, a caller that allows it gets the nearest entry, scanned front to
/// back by largest cell difference with a different leaf counting 2, stopping at the first under 2, and not moved.
/// A fresh pool (`0x1801bb640`) is 200 zeroed entries: key 0 and unbuilt, ordered 199 at the front down to 0 at the back.
/// </remarks>
public sealed class LightCache<T>
{
    /// <summary>The pool's size, and the scan's end marker.</summary>
    public const int Capacity = 200;

    /// <summary>Frames that are never throttled (`DAT_1804716a0` under `0x3c`).</summary>
    public const int UnthrottledFrames = 60;

    private readonly Entry[] _entries = new Entry[Capacity];
    private readonly int[] _next = new int[Capacity];
    private readonly int[] _previous = new int[Capacity];
    private readonly Dictionary<(int X, int Y, int Z, int Leaf), int> _byKey = [];
    private int _front;
    private int _back;
    private int _missFrame = -1;
    private int _misses;

    /// <summary>Creates an empty pool.</summary>
    /// <param name="unbuilt">What a zeroed entry holds, for a fallback that reaches one.</param>
    public LightCache(T unbuilt)
    {
        for (int index = 0; index < Capacity; index++)
        {
            _entries[index] = new Entry(0, 0, 0, 0, false, unbuilt);

            // Toward the back is the lower index; the front is the highest.
            _next[index] = index - 1;
            _previous[index] = index + 1 < Capacity ? index + 1 : -1;
        }

        _front = Capacity - 1;
        _back = 0;
    }

    /// <summary>`lightcache_maxmiss`: entries built per frame once the first 60 have run.</summary>
    public int MaxMiss { get; init; } = 2;

    /// <summary>The entry lighting a point.</summary>
    /// <param name="point">The model's lighting origin.</param>
    /// <param name="leaf">The leaf the point is in.</param>
    /// <param name="frame">The rendered frame's number (`r_framecount`).</param>
    /// <param name="allowFast">Flag 8: past the budget, take the nearest entry rather than build.</param>
    /// <param name="build">Lights a new entry for this point.</param>
    /// <returns>What the entry holds.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="build"/> is null.</exception>
    public T Get(Vector3 point, int leaf, int frame, bool allowFast, Func<T> build)
    {
        ArgumentNullException.ThrowIfNull(build);

        (int X, int Y, int Z, int Leaf) key = (
            ((int)point.X + 0x8000) >> 5,
            ((int)point.Y + 0x8000) >> 5,
            ((int)point.Z + 0x8000) >> 7,
            leaf);

        if (_byKey.TryGetValue(key, out int hit))
        {
            ToFront(hit);
            return _entries[hit].Value;
        }

        if (frame < UnthrottledFrames || frame != _missFrame)
        {
            _missFrame = frame;
            _misses = 0;
        }

        if (_misses < MaxMiss)
        {
            _misses++;
        }
        else if (allowFast)
        {
            return _entries[Nearest(key)].Value;
        }

        int victim = _back;
        Entry old = _entries[victim];

        if (old.Built)
        {
            _byKey.Remove((old.X, old.Y, old.Z, old.Leaf));
        }

        ToFront(victim);

        T value = build();

        _entries[victim] = new Entry(key.X, key.Y, key.Z, key.Leaf, true, value);
        _byKey[key] = victim;

        return value;
    }

    private int Nearest((int X, int Y, int Z, int Leaf) key)
    {
        int best = -1;
        int bestDistance = int.MaxValue;

        for (int index = _front; index >= 0 && index < Capacity; index = _next[index])
        {
            Entry entry = _entries[index];
            int distance = entry.Leaf == key.Leaf ? 0 : 2;

            distance = Math.Max(distance, Math.Abs(entry.X - key.X));
            distance = Math.Max(distance, Math.Abs(entry.Y - key.Y));
            distance = Math.Max(distance, Math.Abs(entry.Z - key.Z));

            if (distance < bestDistance)
            {
                best = index;
                bestDistance = distance;

                if (distance < 2)
                {
                    break;
                }
            }
        }

        return best;
    }

    private void ToFront(int index)
    {
        if (index == _front)
        {
            return;
        }

        int before = _previous[index];
        int after = _next[index];

        _next[before] = after;

        if (after >= 0)
        {
            _previous[after] = before;
        }
        else
        {
            _back = before;
        }

        _next[index] = _front;
        _previous[index] = -1;
        _previous[_front] = index;
        _front = index;
    }

    /// <summary>One pool slot: its key, and what it holds when built.</summary>
    private readonly record struct Entry(int X, int Y, int Z, int Leaf, bool Built, T Value);
}
