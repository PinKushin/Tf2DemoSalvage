using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>Every field of the props and players a tick holds under the given entity indices, as the timeline hands them over.</summary>
/// <remarks>
/// **For writing a specimen down** (D213): a unit test builds its entity from measured constants rather than opening the
/// recording, and this is where the constants come from. The records print themselves, so nothing is chosen or renamed
/// here. With no entity it lists every index the tick holds, which is also the control: a tick inside a match lists
/// players.
///
/// <code>
///   moment-dump z1800 17318 573
/// </code>
/// </remarks>
public sealed class MomentDumpProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "moment-dump";

    /// <inheritdoc/>
    public string Summary => "every field of a tick's props and players by entity index: moment-dump <demo> <tick> [entity...]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count < 2 || DemoCorpus.Find(arguments[0], output) is not { } path)
        {
            output.WriteLine(Summary);
            return;
        }

        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));
        double tick = double.Parse(arguments[1], CultureInfo.InvariantCulture);
        HashSet<int> wanted = [.. arguments.Skip(2).Select(text => int.Parse(text, CultureInfo.InvariantCulture))];

        List<SceneProp> props = [];
        List<ScenePlayer> players = [];
        timeline.PropsAt(tick, props);
        timeline.PlayersAt(tick, players);

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"tick {tick}: interval {timeline.IntervalPerTick}, recorder {timeline.RecorderEntityIndex}, " +
            $"{props.Count} props, {players.Count} players, server time " +
            $"{new Tf2DemoSalvage.Scene.TimelineMoments(timeline).ServerTimeAt((int)tick):R}"));

        if (wanted.Count == 0)
        {
            output.WriteLine("players: " + string.Join(' ', players.Select(player => player.EntityIndex)));
            output.WriteLine("props: " + string.Join(' ', props.Select(prop => $"{prop.EntityIndex}:{prop.ClassName}")));
            return;
        }

        foreach (ScenePlayer player in players.Where(player => wanted.Contains(player.EntityIndex)))
        {
            output.WriteLine(player);
            output.WriteLine("  items: " + string.Join(" | ", player.Items ?? []));
            output.WriteLine("  gestures: " + string.Join(" | ", player.Gestures ?? []));
        }

        // What drives a face: the scenes an entity acts in around this tick, and the sounds on it or spoken by it.
        foreach (SceneChoreography scene in timeline.Scenes.Where(scene =>
            scene.Actors.Any(wanted.Contains) && scene.Tick <= tick && (scene.StoppedTick ?? int.MaxValue) >= tick))
        {
            output.WriteLine($"{scene} actors [{string.Join(", ", scene.Actors)}]");
        }

        foreach (SceneSound sound in timeline.Sounds.Where(sound =>
            (wanted.Contains(sound.EntityIndex) || wanted.Contains(sound.SpeakerEntity)) && Math.Abs(sound.Tick - tick) <= 200))
        {
            output.WriteLine(sound);
        }

        for (int at = 0; at < timeline.Corpses.Count; at++)
        {
            if (timeline.Corpses[at] is { PlayerIndex: { } owner } corpse && wanted.Contains(owner) &&
                corpse.FirstTick <= tick && corpse.LastTick >= tick)
            {
                output.WriteLine($"corpse {at}: {corpse}");
            }
        }

        foreach (SceneProp prop in props.Where(prop => wanted.Contains(prop.EntityIndex)))
        {
            output.WriteLine(prop);

            if (prop.Pose.Beam is { } beam)
            {
                output.WriteLine("  beam ends: " + string.Join(' ', beam.Ends));
            }
        }
    }
}
