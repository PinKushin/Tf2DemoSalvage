using System;
using System.Collections.Generic;
using System.IO;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>What `DemoTimeline.Build` spends, phase by phase, with the GC's share beside it.</summary>
/// <remarks>The viewer's own log line, without launching the viewer. `rest` is the loop outside the timed phases.</remarks>
public sealed class TimelineCostProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "timeline-cost";

    /// <inheritdoc/>
    public string Summary => "DemoTimeline.Build phases plus GC pause and gen2 count: timeline-cost <demo> [runs]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        byte[] bytes = File.ReadAllBytes(arguments[0]);
        int runs = arguments.Count > 1 ? int.Parse(arguments[1], System.Globalization.CultureInfo.InvariantCulture) : 1;

        for (int run = 0; run < runs; run++)
        {
            TimeSpan gcBefore = GC.GetTotalPauseDuration();
            int gen2Before = GC.CollectionCount(2);
            TimelinePhases p = DemoTimeline.Build(bytes).Phases;
            double rest = p.Total - p.Commands - p.Schema - p.Messages - p.Entities - p.Sampling - p.Frames;

            output.WriteLine(
                $"total {p.Total:0} ms: entities {p.Entities:0}, sampling {p.Sampling:0}, messages {p.Messages:0}, frames {p.Frames:0}, " +
                $"rest {rest:0}; gc paused {(GC.GetTotalPauseDuration() - gcBefore).TotalMilliseconds:0} ms, " +
                $"{GC.CollectionCount(2) - gen2Before} gen2, heap {GC.GetTotalMemory(false) / (1 << 20)} MB");
        }
    }
}
