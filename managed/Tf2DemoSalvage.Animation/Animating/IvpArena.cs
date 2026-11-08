using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// The scratch a heap solve takes its arrays from and hands back whole — the arena <c>FUN_1800aa5c0</c> is passed (B708).
/// </summary>
/// <remarks>
/// **Every array is handed out zeroed and exactly as long as asked**, as a new one would be, so nothing a solve reads can tell
/// the difference. <see cref="Release"/> takes back everything handed out since a <see cref="Mark"/>, so a solve marks on entry
/// and releases on exit, and two arrays taken inside one mark are never the same array. New arrays for every solve were 0.39 GB
/// per 20 s of the corpse physics' garbage on z1800 (docs/measurements/2026-11-08-landing-allocation.md). One per thread.
/// </remarks>
internal sealed class IvpArena
{
    /// <summary>This thread's arena.</summary>
    [ThreadStatic]
    private static IvpArena? _thread;

    private readonly Dictionary<int, Stack<double[]>> _free = [];
    private readonly List<double[]> _taken = [];

    /// <summary>The calling thread's arena.</summary>
    internal static IvpArena ForThisThread => _thread ??= new IvpArena();

    /// <summary>Where a later <see cref="Release"/> takes the arena back to.</summary>
    internal int Mark => _taken.Count;

    /// <summary>An array of zeros: <paramref name="arena"/>'s when there is one, else new.</summary>
    /// <param name="arena">The arena, or null for a new array.</param>
    /// <param name="length">Its length.</param>
    /// <returns>The array.</returns>
    internal static double[] Zeros(IvpArena? arena, int length) => arena?.Doubles(length) ?? new double[length];

    /// <summary>An array of zeros, an old one when one that long was handed back.</summary>
    /// <param name="length">Its length.</param>
    /// <returns>The array, held until the release past it.</returns>
    internal double[] Doubles(int length)
    {
        if (_free.TryGetValue(length, out Stack<double[]>? free) && free.TryPop(out double[]? array))
        {
            Array.Clear(array);
        }
        else
        {
            array = new double[length];
        }

        _taken.Add(array);
        return array;
    }

    /// <summary>Every array handed out since <paramref name="mark"/> taken back.</summary>
    /// <param name="mark">A <see cref="Mark"/> read before them.</param>
    internal void Release(int mark)
    {
        for (int index = _taken.Count - 1; index >= mark; index--)
        {
            double[] array = _taken[index];

            if (!_free.TryGetValue(array.Length, out Stack<double[]>? free))
            {
                free = new Stack<double[]>();
                _free.Add(array.Length, free);
            }

            free.Push(array);
        }

        _taken.RemoveRange(mark, _taken.Count - mark);
    }
}
