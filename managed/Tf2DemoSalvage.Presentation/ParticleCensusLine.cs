using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Tf2DemoSalvage.Presentation;

/// <summary>The `--measure` line that says which effects are alive: a leak is a count that climbs, or never drains.</summary>
public static class ParticleCensusLine
{
    /// <summary>How many effect names the line lists, largest first.</summary>
    public const int Shown = 6;

    /// <summary>`census at tick T: bursts L (U unoffered); name L/U, …` — the largest <see cref="Shown"/> names.</summary>
    /// <param name="tick">The tick on screen.</param>
    /// <param name="census">Each effect name with how many run and how many are no longer offered.</param>
    /// <returns>The line.</returns>
    public static string Format(double tick, IReadOnlyList<(string Name, int Live, int Unoffered)> census)
    {
        StringBuilder line = new();
        int live = census.Sum(row => row.Live);
        int unoffered = census.Sum(row => row.Unoffered);

        line.Append(CultureInfo.InvariantCulture, $"census at tick {tick:0}: bursts {live} ({unoffered} unoffered)");

        foreach ((string name, int count, int orphans) in census
                     .OrderByDescending(row => row.Live)
                     .ThenBy(row => row.Name, System.StringComparer.Ordinal)
                     .Take(Shown))
        {
            line.Append(CultureInfo.InvariantCulture, $"; {name} {count}/{orphans}");
        }

        return line.ToString();
    }
}
