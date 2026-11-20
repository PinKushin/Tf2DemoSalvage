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
/// **Through the production path**: each sampled tick is a third-person <see cref="MomentScene.Build"/> with the dormancy
/// the viewer hands it, and "shown" is a running run of the item's own out of <see cref="BurstEffects.DecideEconParticles"/>
/// — the decision the frame draws, carried from it rather than recomputed from the item's state (B243). It prints a control
/// first: a synthetic visible unusual through the same decision must answer one run. *Not the viewer's:* the stride steps
/// the decision over sampled ticks, and every system is taken as loaded (no map is read).
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

        // **The control** (B243): a synthetic visible unusual must come back as one run through the same decision, or the
        // instrument — not the demo — is what answers "never".
        int control = Decided(new EntityModelSet(), [ControlItem], 0, landed: true).Count;
        output.WriteLine($"control: a synthetic visible unusual decides {control.ToString(CultureInfo.InvariantCulture)} run (expected 1)");

        // Per entity and serial: first tick seen, first tick visible, the systems, the owner, the item.
        Dictionary<(int Entity, int Item), Row> found = [];
        EntityModelSet models = new();
        MomentScene scene = new(models, new ViewmodelScene(), NullLogger.Instance) { Weapons = game.Weapons };
        BurstEffects bursts = Bursts(models);
        TimelineMoments source = new(timeline);
        List<ScenePlayer> players = [];
        List<SceneProp> props = [];

        for (int tick = timeline.FirstTick; tick <= timeline.LastTick; tick += stride)
        {
            players.Clear();
            props.Clear();
            timeline.PlayersAt(tick, players);
            timeline.PropsAt(tick, props);

            // The dormancy the viewer hands the moment (`MomentPresenter.Show`): the players' and any entity's own.
            int at = tick;
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
                    ViewmodelFieldOfView: 54f)
                {
                    Recorder = timeline.RecorderEntityIndex,
                    DormantPlayers = source.DormantPlayersAt(tick),
                    EntityDormant = entity => source.IsDormantAt(entity, at),
                });

            // **What the viewer decides, carried from the decision itself** (B243): `BurstEffects.DecideEconParticles`, as
            // the pose runs it, stepped over the strided ticks; an item is shown when a run of its own is running.
            bursts.DecideEconParticles(scene.EconItems, tick, landed: tick == timeline.FirstTick, static _ => true);

            foreach (EconParticleItem item in scene.EconItems)
            {
                SceneProp prop = props.First(prop => prop.EntityIndex == item.Entity);
                bool shown = bursts.EconDecided.Any(run => run.Entity == item.Entity && !run.Released && !run.Effect.StoppedAsMade);
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

    /// <summary>The control's item: a worn hat whose one system is decided visible.</summary>
    private static readonly EconParticleItem ControlItem = new(
        40,
        Wearable: true,
        Received: null,
        Dormant: false,
        EconParticleState.Visible,
        [new EconParticle("superrare_burning1", 0, false, false, false, [null])],
        new EconParticleHolder(true, true, false, false, false, true, false, false, null));

    /// <summary>The viewer's own effects presenter over a model set, as the frame makes it.</summary>
    private static BurstEffects Bursts(EntityModelSet models) =>
        new(new ParticleEffects(), models, new BulletStrikes(models, NullLogger.Instance), NullLogger.Instance);

    /// <summary>What one decision answers for some items.</summary>
    private static IReadOnlyList<EconParticleRun> Decided(EntityModelSet models, IReadOnlyList<EconParticleItem> items, int tick, bool landed)
    {
        BurstEffects bursts = Bursts(models);
        bursts.DecideEconParticles(items, tick, landed, static _ => true);

        return bursts.EconDecided;
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
