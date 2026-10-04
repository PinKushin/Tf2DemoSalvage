using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// Every <c>CSpriteTrail</c> a demo carries, with how long it lived and how far its head travelled, as the trail pass
/// samples it.
/// </summary>
/// <remarks>
/// **Over the whole demo, because a trail lives as long as its projectile** — a second or less — so a tick sample
/// cannot say whether a demo has one (the same reason `projectiles` walks tracks). The head is placed the way the
/// viewer's trail pass places it when the attachment is zero: the trail through its move parents
/// (<see cref="ParentChain"/>), from <c>PropsAt</c> at each tick, so the distance printed is the one the ring samples.
///
/// <code>
///   trails z1800
/// </code>
/// </remarks>
public sealed class SpriteTrailProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "trails";

    /// <inheritdoc/>
    public string Summary =>
        "every sprite trail over the whole demo, its life in ticks and its head's travel: trails <demo> [n]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0)
        {
            output.WriteLine("trails <demo> [n]");
            return;
        }

        if (DemoCorpus.Find(arguments[0], output) is not { } path)
        {
            output.WriteLine($"No demo named '{arguments[0]}'.");
            return;
        }

        int shown = arguments.Count > 1 ? int.Parse(arguments[1], CultureInfo.InvariantCulture) : 20;
        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));

        List<ScenePropTrack> trails =
            [.. timeline.Props.Where(track => string.Equals(track.ClassName, "CSpriteTrail", StringComparison.Ordinal))];

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{Path.GetFileName(path)}: {trails.Count} trail tracks of {timeline.Props.Count} prop tracks"));

        List<SceneProp> props = [];

        foreach (ScenePropTrack track in trails.Take(shown))
        {
            int first = track.Keyframes[0].Tick;
            int last = track.Keyframes[^1].Tick;
            Vector3? start = null;
            Vector3 end = Vector3.Zero;
            int sampled = 0;
            int orphaned = 0;

            for (int tick = first; tick <= last; tick++)
            {
                timeline.PropsAt(tick, props);

                if (props.FirstOrDefault(prop => prop.EntityIndex == track.EntityIndex && prop.Pose.SpriteTrail is not null)
                    is not { } trail)
                {
                    continue;
                }

                sampled++;

                // The viewer holds the head still while the parent is missing (`MainForm.TrailOrigin`), so the travel
                // printed stops there too; the count says how long the trail outlived it.
                if (trail.AttachedTo is { } parent && !props.Exists(prop => prop.EntityIndex == parent))
                {
                    orphaned++;
                    continue;
                }

                ScenePose placed = ParentChain.Absolute(
                    trail, entity => props.FirstOrDefault(prop => prop.EntityIndex == entity));

                end = new Vector3(placed.X, placed.Y, placed.Z);
                start ??= end;
            }

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  entity {track.EntityIndex} '{track.ModelPath}' ticks {first}-{last}, drawn at {sampled}, parent " +
                $"{track.AttachedTo?.ToString(CultureInfo.InvariantCulture) ?? "none"} missing at {orphaned}, head " +
                $"({start?.X:0},{start?.Y:0},{start?.Z:0}) -> ({end.X:0},{end.Y:0},{end.Z:0}), " +
                $"{(start is { } from ? Vector3.Distance(from, end) : 0f):0} units"));
        }
    }
}
