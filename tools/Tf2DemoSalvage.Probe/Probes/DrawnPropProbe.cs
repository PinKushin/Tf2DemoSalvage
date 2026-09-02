using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// What the timeline holds at a tick, filtered by model path.
/// </summary>
/// <remarks>
/// **The counterpart to `map-near`.** That one says what the MAP places; this says what the DEMO
/// produces, which is the other half of any "we are not drawing X" claim. A brush entity appears
/// here as its submodel path — <c>*109</c> — so a question about one specific piece of the map is
/// asked by filtering on that.
///
/// <code>
///   props tf2-2026-pub-pov-clean 900
///   props tf2-2026-pub-pov-clean 900 "*109"
///   props tf2-2026-pub-pov-clean 900 grate
/// </code>
///
/// Grouped by model, because a match places the same rocket forty times and the question is almost
/// never which one.
/// </remarks>
public sealed class DrawnPropProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "props";

    /// <inheritdoc/>
    public string Summary => "props the timeline holds: props <demo> [tick] [model substring]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0)
        {
            output.WriteLine("props <demo> [tick] [model substring]");
            return;
        }

        string? path = DemoCorpus.Find(arguments[0], output);
        if (path is null)
        {
            output.WriteLine($"No demo named '{arguments[0]}'.");
            return;
        }

        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));

        // **The demo's own ticks, not zero.** A recording starts wherever the server was, so a
        // hardcoded tick reports a plausible count of nothing at all.
        int tick = arguments.Count > 1
            ? int.Parse(arguments[1], CultureInfo.InvariantCulture)
            : timeline.FirstTick + ((timeline.LastTick - timeline.FirstTick) / 2);

        string filter = arguments.Count > 2 ? arguments[2] : string.Empty;

        List<SceneProp> props = [];
        timeline.PropsAt(tick, props);

        List<ScenePlayer> players = [];
        timeline.PlayersAt(tick, players);

        // **Through the scene's own visibility rules, so this reports what a screen shows.** The
        // timeline holds everything the demo mentions; `MomentScene` then applies the engine's
        // ShouldDraw rules, and a probe that stopped short of them would say a spawn wall is
        // "drawn" when the viewer removes it two lines later.
        HashSet<int> kept =
        [
            .. RespawnRoomVisibility
                .Visible(
                    [.. DisguiseVisibility.Visible(props, players)],
                    timeline.RoundStateAt(tick))
                .Select(prop => prop.EntityIndex),
        ];

        output.WriteLine(
            $"{Path.GetFileName(path)} tick {tick.ToString(CultureInfo.InvariantCulture)} "
            + $"of {timeline.FirstTick.ToString(CultureInfo.InvariantCulture)}"
            + $"-{timeline.LastTick.ToString(CultureInfo.InvariantCulture)}, "
            + $"{props.Count.ToString(CultureInfo.InvariantCulture)} props, filter '{filter}'");

        // **Grouped by model AND CLASS, and the pair is the whole point.** Grouping by model alone
        // collapsed every prop whose model is not on the wire — 47 of them at one tick — into a
        // single line labelled with whichever class happened to sort first, so a `CWeaponMedigun`
        // read as a `CTFWearable` and the answer to "is the medigun a prop" was NO when it was yes.
        //
        // The filter matches either half for the same reason: a weapon whose model the item schema
        // supplies has no path to search, and `medigun` is then only findable by its class.
        foreach (IGrouping<(string Model, string Class), SceneProp> group in props
            .Where(prop => filter.Length == 0
                || prop.ModelPath.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || (prop.ClassName?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false))
            .GroupBy(prop => (Model: prop.ModelPath, Class: prop.ClassName ?? string.Empty))
            .OrderBy(group => group.Key.Model, StringComparer.Ordinal)
            .ThenBy(group => group.Key.Class, StringComparer.Ordinal))
        {
            SceneProp first = group.First();

            string state;

            if (!EntityModelSet.CanDraw(first))
            {
                state = "NOMODEL";
            }
            else
            {
                state = group.All(prop => kept.Contains(prop.EntityIndex)) ? "DRAWN " : "HIDDEN";
            }

            output.WriteLine(
                // **Three states, because "DRAWN" used to mean two different things** (PARITY-AUDIT
                // finding 5). `kept` is the ShouldDraw visibility rules and nothing else, so a
                // prop with no model at all passed them and was reported DRAWN while the renderer
                // produced nothing from it. NOMODEL is asked through `EntityModelSet.CanDraw` —
                // production's own predicate — so the probe cannot drift from what the renderer
                // does.
                $"{state} "
                + $"{group.Count(),4}  '{group.Key.Model}' "
                + $"kind {first.Kind} "
                + $"class '{first.ClassName}' "
                + $"entities [{string.Join(
                    " ", group.Take(8).Select(prop =>
                        prop.EntityIndex.ToString(CultureInfo.InvariantCulture)))}] "
                + $"first at ({first.Pose.X:0} {first.Pose.Y:0} {first.Pose.Z:0}) "
                + $"angles ({first.Pose.Pitch:0.#} {first.Pose.Yaw:0.#} {first.Pose.Roll:0.#}) "
                + $"attached {first.AttachedTo?.ToString(CultureInfo.InvariantCulture) ?? "none"} "
                + $"merged {first.BoneMerged} "
                + $"mode {first.Pose.RenderMode.ToString(CultureInfo.InvariantCulture)}");
        }
    }
}
