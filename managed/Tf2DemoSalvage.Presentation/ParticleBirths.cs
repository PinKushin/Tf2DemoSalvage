using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Tf2DemoSalvage.Presentation;

/// <summary>Particles born per second of playback, by system, between two readings of the birth tally (finding 82, B557).</summary>
public static class ParticleBirths
{
    /// <summary>The busiest systems' births a second.</summary>
    /// <param name="start">The tally when counting began.</param>
    /// <param name="now">The tally now.</param>
    /// <param name="seconds">Seconds of playback between the two; positive.</param>
    /// <param name="top">How many systems to name.</param>
    /// <returns>One line, busiest first, and the total over every system.</returns>
    public static string Since(IReadOnlyDictionary<string, long> start, IReadOnlyDictionary<string, long> now, double seconds, int top)
    {
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(now);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(seconds);

        List<(string Name, long Born)> born = [.. now
            .Select(pair => (pair.Key, pair.Value - start.GetValueOrDefault(pair.Key)))
            .Where(static pair => pair.Item2 > 0)
            .OrderByDescending(static pair => pair.Item2)
            .ThenBy(static pair => pair.Item1, StringComparer.Ordinal)];

        string busiest = string.Join(
            ", ",
            born.Take(top).Select(pair => string.Create(CultureInfo.InvariantCulture, $"{pair.Name} {pair.Born / seconds:0.#}")));

        return string.Create(
            CultureInfo.InvariantCulture,
            $"particles born a second: {born.Sum(static pair => pair.Born) / seconds:0.#} in all; {busiest}");
    }
}
