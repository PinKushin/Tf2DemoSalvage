using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// Which econ items in a demo carry attached particle systems — unusual effects, a weapon's own — and when the scene first
/// decides one visible (B69 g): the questions an output check needs answered before it can pick a player.
/// </summary>
/// <remarks>
/// **Through the production path**: each sampled tick is a third-person <see cref="MomentScene.Build"/>, whose
/// <see cref="MomentScene.EconItems"/> is what the frame hands the effects, so a state here is the viewer's.
///
/// <code>
///   econ-effects serveme-627619-stv-2026-08-07
///   econ-effects serveme-627619-stv-2026-08-07 66
/// </code>
///
/// The optional second argument is the stride in ticks, 660 (ten seconds) by default.
/// </remarks>
public sealed class EconEffectsProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "econ-effects";

    /// <inheritdoc/>
    public string Summary => "econ items with attached particle systems and when they first show: econ-effects <demo> [stride]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count < 1)
        {
            output.WriteLine("econ-effects <demo> [stride]");
            return;
        }

        if (DemoCorpus.Find(arguments[0], output) is not { } path)
        {
            output.WriteLine($"No demo named '{arguments[0]}'.");
            return;
        }

        int stride = arguments.Count > 1 ? int.Parse(arguments[1], CultureInfo.InvariantCulture) : 660;
        string? folder = new MapLocator(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder).FindGameFolder();
        GameContent game = GameContent.Open(folder, NullLoggerFactory.Instance);
        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));

        output.WriteLine(
            $"{Path.GetFileName(path)}, recorder {timeline.RecorderEntityIndex?.ToString(CultureInfo.InvariantCulture) ?? "none"}, "
            + $"game {folder ?? "NOT FOUND"}, ticks {timeline.FirstTick}..{timeline.LastTick} by {stride}");

        // Per entity and serial: first tick seen, first tick visible, the systems, the owner, the item.
        Dictionary<(int Entity, int Item), Row> found = [];
        MomentScene scene = new(new EntityModelSet(), new ViewmodelScene(), NullLogger.Instance) { Weapons = game.Weapons };
        List<ScenePlayer> players = [];
        List<SceneProp> props = [];

        for (int tick = timeline.FirstTick; tick <= timeline.LastTick; tick += stride)
        {
            players.Clear();
            props.Clear();
            timeline.PlayersAt(tick, players);
            timeline.PropsAt(tick, props);

            scene.Build(
                players,
                [.. props],
                new MomentInfo(
                    Tick: tick,
                    CurrentTick: tick,
                    FirstPerson: false,
                    Followed: null,
                    EyeCamera: null,
                    IntervalPerTick: timeline.IntervalPerTick,
                    ViewmodelFieldOfView: 54f) { Recorder = timeline.RecorderEntityIndex });

            foreach (EconParticleItem item in scene.EconItems)
            {
                SceneProp prop = props.First(prop => prop.EntityIndex == item.Entity);
                bool shown = item.State != EconParticleState.NotVisible;
                (int Entity, int Item) key = (item.Entity, prop.ItemDefinitionIndex ?? -1);

                if (!found.TryGetValue(key, out Row? row))
                {
                    row = new Row(tick, prop.OwnedBy ?? -1, string.Join(",", item.Systems.Select(system => system.System)), item.Wearable);
                    found[key] = row;
                }

                row.Samples++;

                if (shown)
                {
                    row.Visible ??= tick;
                    row.Shown++;
                }
            }
        }

        output.WriteLine($"{found.Count.ToString(CultureInfo.InvariantCulture)} econ items with systems");

        foreach (((int entity, int item), Row row) in found.OrderBy(pair => pair.Value.Seen))
        {
            output.WriteLine(
                $"  entity {entity,4} item {item,6} '{game.Weapons.Items?.ItemDefinitionName(item) ?? "?"}' "
                + $"owner {row.Owner,3} {(row.Wearable ? "wearable" : "weapon  ")} seen {row.Seen,6} "
                + $"first shown {row.Visible?.ToString(CultureInfo.InvariantCulture) ?? "never",6} "
                + $"shown {row.Shown}/{row.Samples} systems {row.Systems}");
        }
    }

    /// <summary>One item's line: first seen, first shown, owner, systems, and how many samples showed it.</summary>
    private sealed class Row(int seen, int owner, string systems, bool wearable)
    {
        public int Seen { get; } = seen;

        public int Owner { get; } = owner;

        public string Systems { get; } = systems;

        public bool Wearable { get; } = wearable;

        public int? Visible { get; set; }

        public int Samples { get; set; }

        public int Shown { get; set; }
    }
}
