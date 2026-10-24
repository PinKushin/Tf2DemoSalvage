using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Presentation;

/// <summary>The <c>[first, end)</c> of a tick-ordered list's items in <c>(after, through]</c>, found by search.</summary>
/// <remarks>
/// The engine hands an event to its listeners once, as it arrives; walking the whole match each frame to find this
/// frame's is a cost it never pays (finding 93, finding 97). A list that is not in tick order is walked whole, which
/// visits the same items in the same order, so callers keep their own <c>(after, through]</c> guard. Unlike
/// <see cref="Tf2DemoSalvage.Core.Scene.TickWindow"/>, it does not assume order, which neither list here is promised.
/// </remarks>
internal static class LandedWindow
{
    /// <summary>The <c>[first, end)</c> of a list's items in <c>(after, through]</c>, or all of it when it is out of tick order.</summary>
    /// <remarks>Whether a list is in order is found once per list, the first time it is asked for.</remarks>
    public static (int First, int End) Landed<T>(
        IReadOnlyList<T> items, Func<T, int> tickOf, ref object? orderOf, ref bool inOrder, int after, int through)
    {
        if (!ReferenceEquals(orderOf, items))
        {
            orderOf = items;
            inOrder = true;

            for (int at = 1; at < items.Count && inOrder; at++)
            {
                inOrder = tickOf(items[at - 1]) <= tickOf(items[at]);
            }
        }

        return inOrder ? (FirstAfter(items, tickOf, after), FirstAfter(items, tickOf, through)) : (0, items.Count);
    }

    /// <summary>The index of the first item of a tick-ordered list whose tick is after this one.</summary>
    private static int FirstAfter<T>(IReadOnlyList<T> items, Func<T, int> tickOf, int tick)
    {
        int low = 0;
        int high = items.Count;

        while (low < high)
        {
            int middle = (low + high) >>> 1;

            if (tickOf(items[middle]) <= tick)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }
}
