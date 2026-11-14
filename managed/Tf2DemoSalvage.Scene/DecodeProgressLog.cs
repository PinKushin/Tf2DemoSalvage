using System;
using System.Globalization;

namespace Tf2DemoSalvage.Scene;

/// <summary>Which of a decode's progress reports become a log line: the first past each tenth, short of the end (B613).</summary>
/// <remarks>
/// **The decode's fractions reached the overlay and nothing else**, so the log said nothing between the demo's header line
/// and the timeline's total: 252 s on f12 in Debug (`docs/measurements/2026-11-13-b613-load-phases.md`), long enough that
/// a reader of the log could not tell a slow load from a hung one. TF2's own loading screen is a fraction and a status
/// text (`IGameUI::UpdateProgressBar( float progress, const char *statusText )`, `common/GameUI/IGameUI.h:70`); the
/// overlay is that, and this is the same report written down, naming the demo so two loads cannot be confused.
///
/// **No line at the end:** <see cref="DecodedDemo.Read"/> already times the build and prints the total, and a second
/// figure for the same moment from a second clock would disagree with it (B243). The percentage is the fraction as the
/// decode reported it, not the tenth it crossed.
/// </remarks>
/// <param name="demo">The demo's file name, which every line names.</param>
public sealed class DecodeProgressLog(string demo)
{
    /// <summary>What the decode is called, in the log here and on the loading overlay.</summary>
    public const string Stage = "Decoding the demo";

    private int _tenths;

    /// <summary>The line for a report, or null when it says nothing new.</summary>
    /// <param name="fraction">How far the decode is, as it reported it.</param>
    /// <param name="seconds">Seconds since the build began.</param>
    /// <returns>The line, or null.</returns>
    public string? Report(double fraction, double seconds)
    {
        int tenths = (int)Math.Floor(fraction * 10d);

        if (tenths <= _tenths || tenths >= 10)
        {
            return null;
        }

        _tenths = tenths;

        return string.Create(CultureInfo.InvariantCulture, $"{Stage} {demo}: {fraction * 100d:0}% after {seconds:0.0} s");
    }
}
