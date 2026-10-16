using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>Every track that changed model while staying itself, with the tick it changed at (B548).</summary>
/// <remarks>
/// **Walks the identity history itself (`FactsHistory`), the record `PropsAt` reads through `FactsAt`** — so a change is
/// reported at the tick it took effect, whether or not a keyframe marks it. Props and player tracks both.
///
/// **Two controls, because an empty answer measures nothing on its own.** The count of tracks walked says the walk
/// reached something; the identity entries counted say the history is populated (one per track at least — a history
/// the builder never stamped would still answer one, so the total counts entries BEYOND the first as well).
/// </remarks>
public sealed class ModelChangesProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "model-changes";

    /// <inheritdoc/>
    public string Summary => "tracks whose model changed mid-life, and the tick: model-changes <demo>";

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
        List<ScenePropTrack> tracks = [.. timeline.Props, .. timeline.PlayerTracks];
        int changed = 0;
        int entries = 0;
        int animationOnly = 0;

        foreach (ScenePropTrack track in tracks)
        {
            string? before = null;
            ScenePropFacts? previous = null;

            foreach ((int tick, ScenePropFacts facts) in track.FactsHistory)
            {
                entries++;

                // The cost of carrying m_bClientSideAnimation in the facts: entries (and wakes) it alone caused.
                if (previous is { } last && last with { ClientSideAnimated = facts.ClientSideAnimated } == facts)
                {
                    animationOnly++;
                }

                previous = facts;

                if (before is not null && facts.ModelPath != before)
                {
                    changed++;
                    output.WriteLine($"  entity {track.EntityIndex} serial {track.SerialNumber} tick {tick} ({track.FirstTick}..{track.EndTick}): '{before}' -> '{facts.ModelPath}'");
                }

                before = facts.ModelPath;
            }
        }

        output.WriteLine($"{timeline.Props.Count} prop and {timeline.PlayerTracks.Count} player tracks walked, {entries} identity entries (control: more than {tracks.Count} means the builder stamped changes), {changed} model changes, {animationOnly} entries from client-side animation alone, {tracks.Sum(track => track.FactsClamped)} stamps that arrived with a regressed tick");
    }
}
