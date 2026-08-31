using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// Everything drawn on a disguised spy over a window of ticks.
/// </summary>
/// <remarks>
/// **Written because the owner watched the demo and named the ticks:** *"tick 870 is where i can
/// see him, till 903 ... it turns out its a soldier not a demo drawing inside the spy"*. What it
/// found, at those exact ticks:
///
/// <code>
///   SPY 2 class 8 team 3 enemy False as 3/2
///   PROP-AT-SPY 1031 '.../items/soldier/hwn2023_warlocks_warcloak.mdl' attached 2
///   PROP-AT-SPY 1015 '.../c_rocketlauncher.mdl'                        attached 2
/// </code>
///
/// A BLU spy disguised as a RED soldier, seen by a BLU recorder — a TEAMMATE — wearing the
/// disguise's hats and carrying its rocket launcher. `DisguiseVisibility` removes all six now.
///
/// **The window is an argument, and that is the point of moving this out of the suite.** As a test
/// the ticks were constants, so a second window meant an edit, a rebuild of an NUnit assembly and a
/// VSTest host. The owner names a tick; this runs against it.
///
/// **Two things this must not do, both of which it did in earlier forms:**
///
/// - **Sample.** Earlier sweeps stepped about seventy ticks at a time and landed on 840 and 910,
///   stepping straight over a 33-tick window. A stride coarser than the event cannot find the
///   event, however many ticks it covers in total.
/// - **Look for a merged prop by POSITION.** A bone-merged prop's pose is (0,0,0), so a proximity
///   test can never match one. Placed props are found by position, merged ones by ownership.
/// </remarks>
// Public, not internal: the only construction is `Activator.CreateInstance` in `Program`, and
// CA1812 cannot see that — an internal probe reads as dead code to the analyser and fails the
// build. Making it public is the honest answer rather than a suppression, because a probe IS the
// assembly's outward surface.
public sealed class SpyDrawProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "spy-draw";

    /// <inheritdoc/>
    public string Summary => "everything drawn on a disguised spy: spy-draw [demo] [from] [to]";

    /// <summary>The recording the owner was watching when this was written.</summary>
    private const string DefaultDemo = "tf2-2026-pub-pov-clean";

    private const int DefaultFrom = 860;

    private const int DefaultTo = 935;

    /// <summary>How close counts as "inside him".</summary>
    private const float Reach = 72f;

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        string fragment = arguments.Count > 0 ? arguments[0] : DefaultDemo;
        int from = arguments.Count > 1 ? int.Parse(arguments[1], CultureInfo.InvariantCulture) : DefaultFrom;
        int to = arguments.Count > 2 ? int.Parse(arguments[2], CultureInfo.InvariantCulture) : DefaultTo;

        string? path = DemoCorpus.Find(fragment, output);
        if (path is null)
        {
            output.WriteLine($"No demo named '{fragment}'.");
            return;
        }

        output.WriteLine($"{Path.GetFileName(path)} ticks {from}-{to}");

        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));

        List<ScenePlayer> players = [];
        List<SceneProp> props = [];
        HashSet<string> found = new(StringComparer.Ordinal);

        for (int tick = from; tick <= to; tick++)
        {
            players.Clear();
            timeline.PlayersAt(tick, players);

            props.Clear();
            timeline.PropsAt(tick, props);

            foreach (ScenePlayer spy in players
                .Where(player => player.Conditions.Has(PlayerConditions.Disguised)))
            {
                Report(found, spy, players, props);
            }
        }

        foreach (string line in found.Order(StringComparer.Ordinal))
        {
            output.WriteLine(line);
        }

        output.WriteLine(
            $"WINDOW {found.Count.ToString(CultureInfo.InvariantCulture)} distinct observations");
    }

    private static void Report(
        HashSet<string> found,
        ScenePlayer spy,
        IReadOnlyList<ScenePlayer> players,
        IReadOnlyList<SceneProp> props)
    {
        found.Add(
            $"SPY {spy.EntityIndex.ToString(CultureInfo.InvariantCulture)} "
            + $"class {spy.PlayerClass?.ToString(CultureInfo.InvariantCulture) ?? "?"} "
            + $"team {spy.Team?.ToString(CultureInfo.InvariantCulture) ?? "?"} "
            + $"enemy {spy.IsEnemy} "
            + $"as {spy.DisguiseClass?.ToString(CultureInfo.InvariantCulture) ?? "none"}"
            + $"/{spy.DisguiseTeam?.ToString(CultureInfo.InvariantCulture) ?? "none"}");

        // Another PLAYER standing in him, which is the only thing that can supply a class and a
        // team of its own — the owner's "a red player drawing inside his actual player model".
        foreach (ScenePlayer other in players.Where(other =>
            other.EntityIndex != spy.EntityIndex && Near(other.X, other.Y, other.Z, spy)))
        {
            found.Add(
                $"PLAYER-IN-SPY {other.EntityIndex.ToString(CultureInfo.InvariantCulture)} "
                + $"class {other.PlayerClass?.ToString(CultureInfo.InvariantCulture) ?? "?"} "
                + $"team {other.Team?.ToString(CultureInfo.InvariantCulture) ?? "?"} "
                + $"drawn {other.Drawn}");
        }

        // **Through the visibility rule, as the scene applies it.** Reporting the raw props would
        // say what the timeline holds; what matters is what survives `DisguiseVisibility`, which is
        // what a screen shows.
        foreach (SceneProp prop in DisguiseVisibility.Visible(props, players).Where(prop =>
            Near(prop.Pose.X, prop.Pose.Y, prop.Pose.Z, spy)
            || prop.AttachedTo == spy.EntityIndex
            || prop.OwnedBy == spy.EntityIndex))
        {
            found.Add(
                $"PROP-AT-SPY {prop.EntityIndex.ToString(CultureInfo.InvariantCulture)} "
                + $"'{prop.ModelPath}' "
                + $"skin {prop.Pose.Skin.ToString(CultureInfo.InvariantCulture)} "
                + $"disguise {prop.OfDisguise} "
                + $"merged {prop.BoneMerged}");
        }
    }

    private static bool Near(float x, float y, float z, ScenePlayer spy) =>
        Math.Abs(x - spy.X) < Reach
        && Math.Abs(y - spy.Y) < Reach
        && Math.Abs(z - spy.Z) < Reach + 32f;
}
