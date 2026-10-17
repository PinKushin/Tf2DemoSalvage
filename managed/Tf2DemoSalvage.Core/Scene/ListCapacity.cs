using System.Collections.Generic;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>Giving back a list's spare capacity after a trim — the one rule every trimmed history follows.</summary>
internal static class ListCapacity
{
    /// <summary>Shrinks the backing array to the count, only when more than half of it is spare.</summary>
    /// <typeparam name="T">The element.</typeparam>
    /// <param name="list">The list, just cut at its front.</param>
    /// <remarks>
    /// **Why at all** (D217 step 4): a checkpoint copy copies a list's whole backing array, and a trim before every
    /// checkpoint cut the front and kept the capacity. **Why only past half**: a list that is then appended to again
    /// would regrow straight after an unconditional shrink, reallocating every checkpoint for nothing.
    /// </remarks>
    public static void TrimIfSparse<T>(this List<T> list)
    {
        if (list.Capacity > 2 * list.Count)
        {
            list.Capacity = list.Count;
        }
    }
}
