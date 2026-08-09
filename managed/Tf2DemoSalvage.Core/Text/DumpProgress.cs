using System;

namespace Tf2DemoSalvage.Core.Text;

/// <summary>
/// How far a dump has got, for callers that want to draw a progress bar.
/// </summary>
/// <param name="Stage">What the dump is doing, for a caller that wants to label the bar.</param>
/// <param name="Completed">Units finished so far.</param>
/// <param name="Total">Units in this stage.</param>
/// <remarks>
/// Reported rather than printed. The core writes no console output of its own — a library that
/// draws to <c>Console</c> cannot be used from a test, a GUI, or a redirected pipe without
/// fighting it. The caller decides whether progress becomes a bar, a log line, or nothing.
///
/// Counted in <em>commands</em> rather than bytes or ticks, because that is the unit the scan
/// actually iterates. A bar driven by playback ticks would stall wherever a demo spends many
/// ticks in few packets, which is exactly what happens during a pause or a long freeze-time.
/// </remarks>
public readonly record struct DumpProgress(string Stage, int Completed, int Total)
{
    /// <summary>How far through this stage, from 0 to 1.</summary>
    /// <remarks>
    /// A stage with no work reports 1 rather than dividing by zero: nothing to do is done.
    /// </remarks>
    public double Fraction => Total <= 0 ? 1d : (double)Completed / Total;

    /// <summary>Renders a fixed-width bar, for callers that just want one drawn.</summary>
    /// <param name="width">Character width of the bar itself, excluding the label.</param>
    /// <returns>A line such as <c>Scanning packets [######----]  60%</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="width"/> is not positive.</exception>
    public string ToBar(int width = 30)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);

        int filled = (int)(Fraction * width);
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{Stage} [{new string('#', filled)}{new string('-', width - filled)}] {Fraction * 100,3:F0}%");
    }
}
