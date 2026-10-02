using System.Collections.Generic;

namespace Tf2DemoSalvage.Presentation;

/// <summary>
/// What a <c>--measure</c> run printed: the frame-rate reports and the rebuild-cost lines under them, counted separately.
/// </summary>
/// <remarks>
/// **Written because "N samples" counted both.** The playback check holds N to one rate report per
/// <see cref="FrameRateLog.IntervalSeconds"/> of playback, but a rebuild-cost line is printed under only some of the reports,
/// so N swung from 14 to 26 on the same twenty seconds depending on how many rebuilds fired. The counts are carried from where
/// each line is added, never recovered by parsing the text again.
/// </remarks>
public sealed class MeasureLog
{
    private readonly List<(bool IsRate, string Text)> _entries = [];

    /// <summary>Frame-rate reports — the samples.</summary>
    public int Samples { get; private set; }

    /// <summary>Rebuild-cost lines; non-zero only while a demo is playing.</summary>
    public int Rebuilds { get; private set; }

    /// <summary>Records one frame-rate report.</summary>
    public void AddRate(string line)
    {
        _entries.Add((true, line));
        Samples++;
    }

    /// <summary>Records one rebuild-cost line.</summary>
    public void AddCost(string line)
    {
        _entries.Add((false, "    " + line));
        Rebuilds++;
    }

    /// <summary>Empties the log for the next measurement.</summary>
    public void Clear()
    {
        _entries.Clear();
        Samples = 0;
        Rebuilds = 0;
    }

    /// <summary>
    /// The lines to print, indented, without the first rate report and the cost beside it. That report covers the second in
    /// which the map finished loading, so it averages a handful of frames against a rebuild that included model uploads.
    /// </summary>
    public IEnumerable<string> Lines
    {
        get
        {
            int from = _entries.Count > 1 ? 1 : 0;

            if (from == 1 && !_entries[1].IsRate)
            {
                from = 2;
            }

            for (int at = from; at < _entries.Count; at++)
            {
                yield return "  " + _entries[at].Text;
            }
        }
    }
}
