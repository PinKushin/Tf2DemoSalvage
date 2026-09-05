using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// Which animation events a demo would fire, walked the way the client walks them.
/// </summary>
/// <remarks>
/// **<c>C_BaseAnimating::DoAnimationEvents</c> against real cycles** (B275). The events have been
/// read off the sequences for a while and nothing fired them, so this is the first thing to ask
/// what a recording actually contains: how many, of which kind, on which models.
///
/// <code>
///   animevents tf2-2013-build1729296-stv-cp_foundry
///   animevents tf2-2013-build1729296-stv-cp_foundry 8000 9000
/// </code>
///
/// **It walks tick by tick, because the traversal is stateful.** Sampling every hundredth tick
/// would miss every event in between and report a plausible smaller number — the walk only sees
/// what the cycle crossed since it last ran.
///
/// **Event 5004 is <c>AE_CL_PLAYSOUND</c> and names a sound script outright**, which is the kind a
/// demo viewer can honour today. Event 7001 is TF2's footstep, and answering it needs the ground
/// surface under the foot (B172).
/// </remarks>
public sealed class AnimationEventProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "animevents";

    /// <inheritdoc/>
    public string Summary =>
        "animation events a demo fires: animevents <demo> [fromTick] [toTick]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0)
        {
            output.WriteLine("animevents <demo> [fromTick] [toTick]");
            return;
        }

        string? path = DemoCorpus.Find(arguments[0], output);
        if (path is null)
        {
            output.WriteLine($"No demo named '{arguments[0]}'.");
            return;
        }

        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));

        MapLocator locator = new(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder);

        string mapName = Tf2DemoSalvage.Core.Container.DemoHeader
            .Parse(File.ReadAllBytes(path)).MapName;

        if (mapName.Length == 0
            || locator.Find(mapName) is not { } mapPath
            || locator.FindGameFolder() is not { } folder)
        {
            output.WriteLine("The demo's map or the game could not be found.");
            return;
        }

        GameContent game = GameContent.Open(folder, NullLoggerFactory.Instance);

        // **Through the map's loaded assets, because the sequence list has to be the MERGED one.**
        // A TF2 player model declares no events of its own — they live in the included
        // `<class>_animations.mdl` — so reading the root model answers "no events" for every player
        // in every demo. The first run of this probe did exactly that and reported zero.
        LoadedMap map = LoadedMap.Read(
            File.ReadAllBytes(mapPath), game, timeline, 0, NullLoggerFactory.Instance);

        if (map.Assets is not { } assets)
        {
            output.WriteLine("The map loaded with no assets.");
            return;
        }

        int from = arguments.Count > 1
            ? int.Parse(arguments[1], CultureInfo.InvariantCulture)
            : timeline.FirstTick;

        int to = arguments.Count > 2
            ? int.Parse(arguments[2], CultureInfo.InvariantCulture)
            : Math.Min(timeline.LastTick, from + 2000);

        Dictionary<string, int> byOptions = [];
        Dictionary<int, int> byId = [];

        // **The production model set, walked tick by tick.** The first version of this probe ran
        // the traversal itself against `prop.Pose.Cycle` and reported zero events on a demo full of
        // them — because a player's cycle is NOT on the wire. The client advances it, in
        // `Simulate`, which is where the walk now lives, so a probe that does not call `Instances`
        // is asking about a number that is a constant for every player.
        EntityModelSet models = new() { Geometry = assets.Geometry };

        List<SceneProp> props = [];
        List<ModelInstance> instances = [];

        int walked = 0;
        int withEvents = 0;
        int clientAnimated = 0;
        Dictionary<string, int> availableBy = [];
        Dictionary<string, int> clientAvailable = [];
        float lowestCycle = float.MaxValue;
        float highestCycle = float.MinValue;

        List<ScenePlayer> players = [];

        for (int tick = from; tick <= to; tick++)
        {
            timeline.PropsAt(tick, props);

            // **Players are not props and this probe missed them twice.** `PropsAt` reports what
            // the demo describes as entities with models; a PLAYER becomes a prop only when
            // `MomentScene` puts it there, through `PlayerProps.Add` — which is also where its
            // model path comes from. Without this the walk covered map decorations and buildings
            // and no player at all, and reported "no client events" on a demo whose players are
            // the only things carrying any.
            timeline.PlayersAt(tick, players);
            PlayerProps.Add(
                players, props, new GameAppearance(game.Classes, null), (_, _, _, body) => body);

            models.Add(props, assets.Geometry);
            models.Instances(props, instances, seconds: tick * timeline.IntervalPerTick);

            walked += props.Count;

            // **The control, and it is three separate questions.** A run reporting no events could
            // be a demo with none, a sequence lookup that finds no event list, or a cycle that
            // never advances — and those look identical from the outside.
            foreach (SceneProp prop in props)
            {
                if (prop.ModelPath.Length == 0 ||
                    assets.Geometry(prop.ModelPath)?.Skinned is not { } skinned)
                {
                    continue;
                }

                IReadOnlyList<StudioEvent> available =
                    skinned.Events(Math.Max(0, prop.Pose.Sequence));

                if (available.Count > 0)
                {
                    withEvents++;

                    string owner = $"{prop.ClassName} {prop.ModelPath}";

                    availableBy[owner] = availableBy.GetValueOrDefault(owner) + 1;

                    if (available.Any(one => one.FiresOnTheClient()))
                    {
                        clientAvailable[owner] = clientAvailable.GetValueOrDefault(owner) + 1;
                    }
                }

                if (prop.ClientSideAnimated)
                {
                    clientAnimated++;
                }

                lowestCycle = Math.Min(lowestCycle, prop.Pose.Cycle);
                highestCycle = Math.Max(highestCycle, prop.Pose.Cycle);
            }

            foreach (StudioEvent one in models.FiredEvents.Select(fired => fired.Event))
            {
                byId[one.Id] = byId.GetValueOrDefault(one.Id) + 1;

                string key = $"{one.Id.ToString(CultureInfo.InvariantCulture)} '{one.Options}'";

                byOptions[key] = byOptions.GetValueOrDefault(key) + 1;
            }
        }

        output.WriteLine(
            $"{Path.GetFileName(path)} ticks {from.ToString(CultureInfo.InvariantCulture)}"
            + $"-{to.ToString(CultureInfo.InvariantCulture)}: "
            + $"{walked.ToString(CultureInfo.InvariantCulture)} entity walks, "
            + $"{withEvents.ToString(CultureInfo.InvariantCulture)} on a sequence that HAS events, "
            + $"{clientAnimated.ToString(CultureInfo.InvariantCulture)} client-animated, "
            + $"cycles {lowestCycle:0.###} to {highestCycle:0.###}");

        foreach ((string owner, int count) in availableBy
            .OrderByDescending(entry => entry.Value)
            .Take(8))
        {
            output.WriteLine(
                $"  available on {count.ToString(CultureInfo.InvariantCulture),6} walks "
                + $"({clientAvailable.GetValueOrDefault(owner).ToString(CultureInfo.InvariantCulture)} "
                + $"of them CLIENT events)  {owner}");
        }

        // **The control: how many entity-frames were walked at all.** A run reporting no events
        // could be a demo with none or a walk that never ran, and those need telling apart.
        if (byId.Count == 0)
        {
            output.WriteLine("no client events fired");
            return;
        }

        foreach ((int id, int count) in byId.OrderByDescending(entry => entry.Value))
        {
            output.WriteLine(
                $"EVENT {id.ToString(CultureInfo.InvariantCulture),5}: "
                + $"{count.ToString(CultureInfo.InvariantCulture),6} times"
                + (id == 5004 ? "  (AE_CL_PLAYSOUND — names a sound script)" : string.Empty)
                + (id == 7001 ? "  (TF2 footstep — needs the ground surface)" : string.Empty));
        }

        foreach ((string key, int count) in byOptions
            .OrderByDescending(entry => entry.Value)
            .Take(15))
        {
            output.WriteLine(
                $"  {count.ToString(CultureInfo.InvariantCulture),6}  {key}");
        }
    }

}
