using System;
using System.Diagnostics;
using System.Globalization;

namespace Tf2DemoSalvage.Presentation;

/// <summary>The particle phase's direct counters at one moment, running totals (finding 82).</summary>
/// <param name="Searches">Frames that searched the timeline for bursts to offer.</param>
/// <param name="SearchTicks">Stopwatch ticks in that search; it contains the attachment queries the muzzle flashes make.</param>
/// <param name="BurstTicks">Stopwatch ticks starting and stepping the bursts found.</param>
/// <param name="AttachmentQueries">Attachment queries from outside the pass that reached a bone setup.</param>
/// <param name="AttachmentBuilds">Skeletons those queries built.</param>
/// <param name="AttachmentRebuilds">Of those, queries that rebuilt a skeleton already built this frame.</param>
/// <param name="AttachmentTicks">Stopwatch ticks in those queries' bone setups.</param>
/// <param name="AttachmentRootBuilds">Of the builds, roots' (finding 105 A6, by caller: the attachment query).</param>
/// <param name="AttachmentEligibleBuilds">Of those, roots a threaded pre-pass would take.</param>
/// <param name="AttachmentEligibleTicks">Stopwatch ticks in the eligible roots' own calls.</param>
public readonly record struct ParticleCost(
    long Searches,
    long SearchTicks,
    long BurstTicks,
    long AttachmentQueries,
    long AttachmentBuilds,
    long AttachmentRebuilds,
    long AttachmentTicks,
    long AttachmentRootBuilds = 0,
    long AttachmentEligibleBuilds = 0,
    long AttachmentEligibleTicks = 0)
{
    /// <summary>The cost between an earlier reading and this one, per frame.</summary>
    /// <param name="start">The earlier reading.</param>
    /// <param name="frames">Frames between the two; positive.</param>
    /// <returns>One line.</returns>
    public string Since(ParticleCost start, int frames)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frames);

        double Ms(long ticks) => ticks * 1000d / Stopwatch.Frequency / frames;
        double Each(long count) => count / (double)frames;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"particle cost a frame: search {Ms(SearchTicks - start.SearchTicks):0.00} ms over {Each(Searches - start.Searches):0.##} searches, " +
            $"bursts {Ms(BurstTicks - start.BurstTicks):0.00} ms; attachments {Each(AttachmentQueries - start.AttachmentQueries):0.#} queries, " +
            $"{Each(AttachmentBuilds - start.AttachmentBuilds):0.#} skeletons built, {Each(AttachmentRebuilds - start.AttachmentRebuilds):0.#} rebuilt after this frame's first, " +
            $"{Ms(AttachmentTicks - start.AttachmentTicks):0.00} ms; roots built {Each(AttachmentRootBuilds - start.AttachmentRootBuilds):0.#}, " +
            $"eligible {Each(AttachmentEligibleBuilds - start.AttachmentEligibleBuilds):0.#} in {Ms(AttachmentEligibleTicks - start.AttachmentEligibleTicks):0.000} ms");
    }
}
