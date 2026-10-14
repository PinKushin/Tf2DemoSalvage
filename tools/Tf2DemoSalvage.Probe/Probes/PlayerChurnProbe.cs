using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// Which of a player's properties change between one frame and the next, counted over a whole demo.
/// </summary>
/// <remarks>
/// **Written for D217**, whose first cut shared a player's rarely-changing half between frames and found almost nothing
/// shared: the question is which member breaks the sharing. Every public property of each `ScenePlayer` is compared with
/// the same player's value in the frame before — lists element by element — and each property's count of changes is
/// printed against the number of comparisons.
///
/// **Its control** is `EntityIndex`, which must change zero times (it is how a player is matched), and `X`, which must
/// change often in any match.
///
/// It asserts nothing — a measurement is not a test (D38).
/// </remarks>
public sealed class PlayerChurnProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "player-churn";

    /// <inheritdoc/>
    public string Summary => "how often each ScenePlayer property changes frame to frame: player-churn <demo>";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0 || DemoCorpus.Find(arguments[0], output) is not { } path)
        {
            output.WriteLine("player-churn <demo>");
            return;
        }

        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));
        PropertyInfo[] properties = typeof(ScenePlayer).GetProperties(BindingFlags.Instance | BindingFlags.Public);
        Dictionary<string, long> changes = properties.ToDictionary(property => property.Name, _ => 0L);
        Dictionary<int, ScenePlayer> last = [];
        long compared = 0;

        foreach (TimelineFrame frame in timeline.Frames)
        {
            foreach (ScenePlayer player in frame.Players)
            {
                if (last.TryGetValue(player.EntityIndex, out ScenePlayer before))
                {
                    compared++;

                    foreach (PropertyInfo property in properties.Where(property =>
                                 !Same(property.GetValue(before), property.GetValue(player))))
                    {
                        changes[property.Name]++;
                    }
                }

                last[player.EntityIndex] = player;
            }
        }

        output.WriteLine($"{Path.GetFileName(path)}: {compared} player-frame comparisons");

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
