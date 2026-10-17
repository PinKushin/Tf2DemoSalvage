using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Tf2DemoSalvage.Presentation;

/// <summary>The parts of a frame phase, summed per frame and printed as means beside the rate line.</summary>
/// <remarks>
/// **For the phases whose own columns are one number over several pieces of work** — `camera` is view, device, pose and
/// particles; `hud` is events, state, the HUD's frame and the tools. Each part is timed where it runs and carried here, so
/// the printed mean is the value the code measured (B243), and the window is the rate line's own: <see cref="Frame"/> is
/// called once per drawn frame and <see cref="Report"/> when that line is written.
/// </remarks>
public sealed class FrameParts
{
    private readonly List<(string Name, long Ticks)> _parts = [];
    private int _frames;

    /// <summary>Adds time to a part, creating it in first-seen order.</summary>
    /// <param name="name">The part, as printed.</param>
    /// <param name="ticks">Stopwatch ticks.</param>
    public void Add(string name, long ticks)
    {
        for (int at = 0; at < _parts.Count; at++)
        {
            if (_parts[at].Name == name)
            {
                _parts[at] = (name, _parts[at].Ticks + ticks);
                return;
            }
        }

        _parts.Add((name, ticks));
    }

    /// <summary>Counts one drawn frame.</summary>
    public void Frame() => _frames++;

    /// <summary>The means since the last report, as `name ms, …`, and starts a new window; empty with no frame.</summary>
    /// <returns>The text, or empty.</returns>
    public string Report()
    {
        StringBuilder text = new();

        if (_frames > 0)
        {
            foreach ((string name, long ticks) in _parts)
            {
                text.Append(text.Length == 0 ? string.Empty : ", ")
                    .Append(name)
                    .Append(' ')
                    .Append((ticks / (double)Stopwatch.Frequency * 1000d / _frames).ToString("0.##", CultureInfo.InvariantCulture));
            }
        }

        _parts.Clear();
        _frames = 0;

        return text.ToString();
    }
}
