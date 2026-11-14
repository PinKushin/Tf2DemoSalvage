using System;
using System.Globalization;

namespace Tf2DemoSalvage.Presentation;

/// <summary>Which of a decode's progress reports become a log line: the first at or past each tenth (B613).</summary>
/// <remarks>
/// **The decode's fractions reached the overlay and nothing else**, so the log said nothing between the demo's header line
/// and the timeline's total: about four minutes on f12 in Debug (`docs/measurements/2026-11-13-b613-load-phases.md`), long
/// enough that a reader of the log could not tell a slow load from a hung one. TF2's own loading screen is a fraction and
/// a status text (`IGameUI::UpdateProgressBar( float progress, const char *statusText )`, `common/GameUI/IGameUI.h:70`);
/// the overlay is that, and this is the same report written down. A tenth keeps it to ten lines whatever the demo's
/// length. The percentage is the fraction as the decode reported it, not the tenth it crossed.
/// </remarks>
public sealed class DecodeProgressLog
{
    private int _tenths;

    /// <summary>The line for a report, or null when it says nothing new.</summary>
    /// <param name="fraction">How far the decode is, as it reported it.</param>
    /// <param name="seconds">Seconds since the decode began.</param>
    /// <returns>The line, or null.</returns>
    public string? Report(double fraction, double seconds)
    {
        int tenths = (int)Math.Floor(Math.Clamp(fraction, 0d, 1d) * 10d);

        if (tenths <= _tenths)
        {
            return null;
        }

        _tenths = tenths;

        return string.Create(CultureInfo.InvariantCulture, $"decoding the demo: {fraction * 100d:0}% after {seconds:0.0} s");
    }
}
