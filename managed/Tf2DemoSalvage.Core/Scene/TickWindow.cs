using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>The window search every one-shot feed answers: what fired between two ticks (B415).</summary>
/// <remarks>
/// **One copy for every feed of one-shots.** An explosion and a bullet are both events at a tick whose effect runs
/// on afterwards, so both are kept in fire order and asked for by window. The search was written for
/// <see cref="ExplosionFeed"/> and moved here when the second feed needed it.
/// </remarks>
public static class TickWindow
{
    /// <summary>Every item that fired in a window of ticks, both ends included, with its index.</summary>
    /// <typeparam name="T">The event type.</typeparam>
    /// <param name="items">Every event, in tick order.</param>
    /// <param name="tickOf">An event's tick.</param>
    /// <param name="fromTick">The first tick to include.</param>
    /// <param name="toTick">The last.</param>
    /// <param name="into">Cleared, then filled in tick order.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// A binary search for the start rather than a scan: `demostf-cp_process_f12` carries thousands of each kind over
    /// one match, and a viewer asks once a frame.
    /// </remarks>
    public static void Between<T>(
        IReadOnlyList<T> items, Func<T, int> tickOf, int fromTick, int toTick, ICollection<(int Index, T Item)> into)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(tickOf);
        ArgumentNullException.ThrowIfNull(into);

        into.Clear();

        int low = 0;
        int high = items.Count;

        while (low < high)
        {
            int middle = low + ((high - low) / 2);

            if (tickOf(items[middle]) < fromTick)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        for (int at = low; at < items.Count && tickOf(items[at]) <= toTick; at++)
        {
            into.Add((at, items[at]));
        }
    }
}
