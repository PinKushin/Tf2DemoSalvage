using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>What every living player wears at a tick, and whether each item follows the player's bones (B406).</summary>
/// <remarks>
/// **Written for the owner's report**: *"living people, its offset i guess, its down near the feet"*. A worn item that is not
/// bone-merged is drawn at its own pose, and a wearable's own origin is its wearer's — the feet. So the first question is which
/// items the timeline says are NOT merged, and what they are attached to. **A control is printed first**: the count of merged
/// items, which must be most of them, or the flag itself is not being read.
/// <code>
///   worn &lt;demo&gt; &lt;tick&gt;
/// </code>
/// </remarks>
public sealed class WornProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "worn";

    /// <inheritdoc/>
    public string Summary => "what each living player wears and whether it follows their bones: worn <demo> <tick>";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count < 2 || DemoCorpus.Find(arguments[0], output) is not { } path)
        {
            output.WriteLine("worn <demo> <tick>");
            return;
        }

        int tick = int.Parse(arguments[1], CultureInfo.InvariantCulture);
        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));
        List<ScenePlayer> players = [];
        List<SceneProp> props = [];
        timeline.PlayersAt(tick, players);
        timeline.PropsAt(tick, props);

        HashSet<int> living = [.. players.Where(player => player.Drawn).Select(player => player.EntityIndex)];
        List<SceneProp> worn = [.. props.Where(prop => prop.AttachedTo is { } wearer && living.Contains(wearer))];

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{Path.GetFileName(path)} tick {tick}: {living.Count} living players, {worn.Count} items attached to them, " +
            $"{worn.Count(prop => prop.BoneMerged)} bone-merged (the control: most should be)"));

        foreach (SceneProp prop in worn.OrderBy(prop => prop.BoneMerged).ThenBy(prop => prop.AttachedTo))
        {
            ScenePlayer wearer = players.First(player => player.EntityIndex == prop.AttachedTo);

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  {(prop.BoneMerged ? "merged  " : "UNMERGED")} {prop.EntityIndex,5} on {prop.AttachedTo,3} (class {wearer.PlayerClass} " +
                $"at {wearer.X:0} {wearer.Y:0} {wearer.Z:0} yaw {wearer.Yaw:0}) " +
                $"{prop.ClassName} '{prop.ModelPath}' pose ({prop.Pose.X:0}, {prop.Pose.Y:0}, {prop.Pose.Z:0}) " +
                $"attachment {prop.AttachmentPoint?.ToString(CultureInfo.InvariantCulture) ?? "-"}"));
        }
    }
}
