using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// Which of a prop keyframe's properties change from one stored keyframe to the next, counted over a whole demo.
/// </summary>
/// <remarks>
/// **Written for D217's second step**, the prop keyframes, by the same reasoning as `player-churn`: a member that changes
/// on most keyframes stays inline in `ScenePose`, and the rest move to a half shared between keyframes. Every public
/// property of each keyframe is compared with the same track's keyframe before it, lists element by element.
///
/// **Its control** is that a track stores a keyframe only when the pose differs, so every comparison must count at
/// least one change: the probe prints how many comparisons found none, which must be zero.
///
/// It asserts nothing — a measurement is not a test (D38).
/// </remarks>
public sealed class PropChurnProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "prop-churn";

    /// <inheritdoc/>
    public string Summary => "how often each ScenePose property changes keyframe to keyframe: prop-churn <demo>";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0 || DemoCorpus.Find(arguments[0], output) is not { } path)
        {
            output.WriteLine("prop-churn <demo>");
            return;
        }

        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));
        PropertyInfo[] properties = typeof(ScenePose).GetProperties(BindingFlags.Instance | BindingFlags.Public);
        Dictionary<string, long> changes = properties.ToDictionary(property => property.Name, _ => 0L);
        long compared = 0;
        long unchanged = 0;

        IEnumerable<(ScenePose Before, ScenePose Now)> pairs = timeline.Props.Concat(timeline.PlayerTracks).Distinct()
            .SelectMany(track => track.Keyframes.Zip(track.Keyframes.Skip(1), (before, now) => (before.Pose, now.Pose)));

        foreach ((ScenePose before, ScenePose now) in pairs)
        {
            compared++;
            bool any = false;

            foreach (PropertyInfo property in properties.Where(property =>
                         !Same(property.GetValue(before), property.GetValue(now))))
            {
                changes[property.Name]++;
                any = true;
            }

            unchanged += any ? 0 : 1;
        }

        output.WriteLine($"{Path.GetFileName(path)}: {compared} keyframe comparisons, {unchanged} with no change (control: 0)");

        foreach ((string name, long count) in changes.OrderByDescending(change => change.Value))
        {
            output.WriteLine($"  {name,-28} {count,10} ({100d * count / Math.Max(1, compared):0.0}%)");
        }
    }

    private static bool Same(object? left, object? right) =>
        left is IEnumerable first and not string && right is IEnumerable second and not string
            ? first.Cast<object?>().SequenceEqual(second.Cast<object?>())
            : Equals(left, right);
}
