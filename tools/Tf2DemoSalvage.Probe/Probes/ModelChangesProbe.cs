using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>Every prop that changed model while staying itself, with the tick it changed at (B548).</summary>
/// <remarks>
/// **Read through <see cref="ScenePropTrack.FactsAt"/>, the call `PropsAt` makes**, at every keyframe tick — so a change
/// is reported at the first keyframe that shows it, which is where the drawn answer moves. **The control is the count of
/// tracks walked**: zero changes beside zero tracks measures nothing.
/// </remarks>
public sealed class ModelChangesProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "model-changes";

    /// <inheritdoc/>
    public string Summary => "props whose model changed mid-life, and the tick: model-changes <demo>";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0 || DemoCorpus.Find(arguments[0], output) is not { } path)
        {
            output.WriteLine("model-changes <demo>");
            return;
        }

        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));
        int changed = 0;

        foreach (ScenePropTrack track in timeline.Props)
        {
            string? before = null;

            foreach (int tick in track.Keyframes.Select(keyframe => keyframe.Tick))
            {
                string now = track.FactsAt(tick).ModelPath;

                if (before is not null && now != before)
                {
                    changed++;
                    output.WriteLine($"  entity {track.EntityIndex} serial {track.SerialNumber} tick {tick} ({track.FirstTick}..{track.EndTick}): '{before}' -> '{now}'");
                }

                before = now;
            }
        }

        output.WriteLine($"{timeline.Props.Count} prop tracks walked, {changed} model changes");
    }
}
