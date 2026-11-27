using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>Every change of a point-of-view recorder's observer mode and target, with his death time (B745, B738).</summary>
/// <remarks>
/// **Through the production path**: the <c>ScenePlayer</c> the timeline hands the camera. **The control** is the first
/// line, the recorder's mode at the first frame: a SourceTV demo has no recorder and says so.
/// <code>
///   observer &lt;demo&gt;
/// </code>
/// </remarks>
public sealed class ObserverModeProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "observer";

    /// <inheritdoc/>
    public string Summary => "a POV recorder's observer mode and target changes, with his death time: observer <demo>";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0 || DemoCorpus.Find(arguments[0], output) is not { } path)
        {
            output.WriteLine("Give a demo: observer <demo>");
            return;
        }

        DemoTimeline timeline = ProbeInstall.Timeline(File.ReadAllBytes(path), output);

        if (timeline.RecorderEntityIndex is not { } recorder)
        {
            output.WriteLine($"{Path.GetFileName(path)}: no recorder (SourceTV)");
            return;
        }

        output.WriteLine($"{Path.GetFileName(path)}: recorder {recorder.ToString(CultureInfo.InvariantCulture)}");

        (int? Mode, int? Target)? last = null;

        foreach (TimelineFrame frame in timeline.Frames)
        {
            foreach (ScenePlayer player in frame.Players)
            {
                if (player.EntityIndex != recorder)
                {
                    continue;
                }

                (int? Mode, int? Target) now = (player.ObserverMode, player.ObserverTarget);

                if (last != now)
                {
                    output.WriteLine(string.Create(
                        CultureInfo.InvariantCulture,
                        $"tick {frame.Tick}: mode {now.Mode?.ToString(CultureInfo.InvariantCulture) ?? "-"} target {now.Target?.ToString(CultureInfo.InvariantCulture) ?? "-"} alive {player.IsAlive} death time {player.DeathTime:0.###} ({player.DeathTime / timeline.IntervalPerTick:0} ticks)"));
                    last = now;
                }
            }
        }
    }
}
