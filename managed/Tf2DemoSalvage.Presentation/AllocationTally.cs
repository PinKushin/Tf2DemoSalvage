using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Globalization;
using System.Linq;

namespace Tf2DemoSalvage.Presentation;

/// <summary>Large-object-heap allocations by type, from the runtime's own sampled ticks, for `--measure` (finding 80).</summary>
/// <remarks>
/// **Why it exists:** at the end of a measured f12 run the GC reported a 6.5 GB large-object heap against a live heap a
/// third that size. Something allocates large arrays and drops them. A heap dump cannot name it, because by then the
/// arrays are dead. The runtime's <c>GCAllocationTick</c> event carries the type, the heap and the bytes allocated
/// since the previous tick, so summing it per type names the allocator, as the runtime sampled it.
/// </remarks>
public sealed class AllocationTally
{
    private readonly ConcurrentDictionary<string, long> _large = new(StringComparer.Ordinal);

    /// <summary>Adds one tick.</summary>
    /// <param name="type">The sampled object's type name.</param>
    /// <param name="large">Whether it went to the large-object heap.</param>
    /// <param name="bytes">Bytes allocated since the previous tick.</param>
    public void Add(string type, bool large, long bytes)
    {
        if (large)
        {
            _large.AddOrUpdate(type, bytes, (_, sum) => sum + bytes);
        }
    }

    /// <summary>The line: the total, then the largest types.</summary>
    /// <param name="top">How many types to name.</param>
    /// <returns>The line.</returns>
    public string Report(int top)
    {
        static string Mb(long bytes) => (bytes / 1048576d).ToString("0", CultureInfo.InvariantCulture);
        KeyValuePair<string, long>[] all = [.. _large];

        return $"large allocations: {Mb(all.Sum(pair => pair.Value))} MB; " +
            string.Join(", ", all.OrderByDescending(pair => pair.Value).Take(top).Select(pair => $"{pair.Key} {Mb(pair.Value)} MB"));
    }

    /// <summary>Clears the tally, so a measurement counts only its own interval.</summary>
    public void Clear() => _large.Clear();
}

/// <summary>Listens to this process's runtime and feeds an <see cref="AllocationTally"/>.</summary>
internal sealed class AllocationTickListener : EventListener
{
        private const EventKeywords GcKeyword = (EventKeywords)0x1;

        /// <summary>The tally this feeds.</summary>
        public AllocationTally Tally { get; } = new();

        /// <inheritdoc/>
        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            ArgumentNullException.ThrowIfNull(eventSource);

            if (eventSource.Name == "Microsoft-Windows-DotNETRuntime")
            {
                EnableEvents(eventSource, EventLevel.Verbose, GcKeyword);
            }
        }

        /// <inheritdoc/>
        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            ArgumentNullException.ThrowIfNull(eventData);

            // GCAllocationTick_V4: AllocationAmount, AllocationKind (0 small, 1 large, 2 pinned), …, TypeName, …, AllocationAmount64.
            if (eventData.EventName?.StartsWith("GCAllocationTick", StringComparison.Ordinal) != true || eventData.PayloadNames is not { } names)
            {
                return;
            }

            object? Value(string name) => names.IndexOf(name) is >= 0 and var at ? eventData.Payload?[at] : null;

            Tally.Add(
                Value("TypeName") as string ?? "?",
                Convert.ToInt32(Value("AllocationKind") ?? 0, CultureInfo.InvariantCulture) == 1,
                Convert.ToInt64(Value("AllocationAmount64") ?? Value("AllocationAmount") ?? 0L, CultureInfo.InvariantCulture));
        }
}
