using System.Collections.Generic;

namespace Tf2DemoSalvage.Core.Schema;

/// <summary>
/// Overlaying an entity's update onto the baseline it deltas against.
/// </summary>
/// <remarks>
/// **One place, because two callers need exactly the same answer and disagreeing would be silent.**
/// <see cref="EntityDecoder.EffectiveProperties"/> merges to report what an entity IS, and
/// <see cref="EntityBaselineSlots.Update"/> merges to store what it will be delta'd against next
/// time. A difference between the two would show up as an entity whose state drifts from its own
/// baseline over successive snapshots — which reads as decode noise, not as a merge bug.
/// </remarks>
internal static class BaselineMerge
{
    /// <summary>The baseline with the update laid over it, by property index.</summary>
    /// <param name="baseline">What the entity was.</param>
    /// <param name="update">What this snapshot said, which wins wherever it spoke.</param>
    /// <returns>The merged properties, in property-index order.</returns>
    /// <remarks>
    /// **Sorted by index rather than left in arrival order**, because a merged list is read back by
    /// consumers that expect the wire's ordering, and an update whose indices interleave with the
    /// baseline's would otherwise produce a list in neither order.
    /// </remarks>
    public static IReadOnlyList<DecodedProperty> Overlay(
        IReadOnlyList<DecodedProperty> baseline, IReadOnlyList<DecodedProperty> update)
    {
        SortedDictionary<int, DecodedProperty> merged = [];

        foreach (DecodedProperty property in baseline)
        {
            merged[property.Index] = property;
        }

        // Second, so the snapshot wins wherever it spoke. That direction is the whole mechanism.
        foreach (DecodedProperty property in update)
        {
            merged[property.Index] = property;
        }

        return [.. merged.Values];
    }
}
