using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// Every animation state one kind of prop passes through, as a list of CHANGES.
/// </summary>
/// <remarks>
/// **Log the transition, never a sample** (`docs/memory/log-the-event-not-a-sample-of-it.md`). A
/// probe that printed the sequence every N ticks answers "what was it at these moments" when the
/// question is "when did it change, and to what" — and a cabinet that opens and never shuts looks
/// identical to one sampled only while open.
///
/// <code>
///   anim tf2-2026-pub-pov-clean resupply_locker
///   anim tf2-2026-pub-pov-clean resupply_locker 8
/// </code>
///
/// The optional third argument limits how many entities are followed, because a map places a dozen
/// of the same cabinet and one is usually enough to see the shape.
/// </remarks>
public sealed class AnimationProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "anim";

    /// <inheritdoc/>
    public string Summary => "a prop's animation changes over a demo: anim <demo> <model> [entities]";

    private const int DefaultEntities = 4;

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count < 2)
        {
            output.WriteLine("anim <demo> <model substring> [how many entities]");
            return;
        }

        string? path = DemoCorpus.Find(arguments[0], output);
        if (path is null)
        {
            output.WriteLine($"No demo named '{arguments[0]}'.");
            return;
        }

        string filter = arguments[1];
        int wanted = arguments.Count > 2
            ? int.Parse(arguments[2], CultureInfo.InvariantCulture)
            : DefaultEntities;

        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));

        List<ScenePropTrack> tracks =
        [
            .. timeline.Props
                .Where(track => track.ModelPath.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .Take(wanted),
        ];

        output.WriteLine(
            $"{Path.GetFileName(path)} ticks "
            + $"{timeline.FirstTick.ToString(CultureInfo.InvariantCulture)}-"
            + $"{timeline.LastTick.ToString(CultureInfo.InvariantCulture)}, "
            + $"{tracks.Count.ToString(CultureInfo.InvariantCulture)} tracks matching '{filter}'");

        foreach (ScenePropTrack track in tracks)
        {
            output.WriteLine(
                $"--- entity {track.EntityIndex.ToString(CultureInfo.InvariantCulture)} "
                + $"'{track.ModelPath}'");

            Follow(output, timeline, track);
        }
    }

    private static void Follow(TextWriter output, DemoTimeline timeline, ScenePropTrack track)
    {
        (int Sequence, float Rate, bool Hidden)? was = null;
        int changes = 0;

        for (int tick = timeline.FirstTick; tick <= timeline.LastTick; tick++)
        {
            if (track.At(tick) is not { } pose)
            {
                continue;
            }

            (int, float, bool) now = (pose.Sequence, pose.PlaybackRate, pose.Hidden);

            if (was is { } before && before == now)
            {
                continue;
            }

            was = now;

            // **A cap, because a broken animation can change every tick** and a probe that prints
            // thirty thousand lines is not readable, which is the same as not reporting.
            if (++changes > 40)
            {
                output.WriteLine("  ... more changes than are worth printing");
                return;
            }

            output.WriteLine(
                $"  tick {tick,7} seq {pose.Sequence,3} "
                + $"cycle {pose.Cycle,6:0.000} "
                + $"rate {pose.PlaybackRate,5:0.00} "
                + $"started {pose.AnimationStartSeconds,8:0.00}s "
                + $"hidden {pose.Hidden}");
        }

        if (changes == 0)
        {
            output.WriteLine("  never appeared, or never changed");
        }
    }
}
