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
/// — the decision the frame draws, carried from it rather than recomputed from the item's state (B243). Two controls, each
/// of which stops the probe before any item is reported when it fails: a synthetic visible unusual through the same decision
/// and model set must answer one run, and the first live player's <c>head</c> must be found through that model set. The
/// demo's map is read (<see cref="LoadedMap.Read"/>) for its geometry, the only route to a <c>.mdl</c>; a map that is not
/// installed or will not parse is reported and the attachment control then fails. *Not the viewer's:* the stride steps the
/// decision over sampled ticks, and every particle system is taken as loaded (no manifest is asked).
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
        MapLocator locator = new(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder);
        string? folder = ProbeInstall.Folder();
        GameContent game = ProbeInstall.Game(output);
        byte[] bytes = File.ReadAllBytes(path);
        DemoTimeline timeline = ProbeInstall.Timeline(bytes, game);

        // **The map, for its geometry loader — the only route to a `.mdl`** (B69 review I): `EntityModelSet.Geometry` answers
        // nothing until a map sets it, and then every `LookupAttachment` is 0 and every effect "follows its abs origin".
        string mapName = Tf2DemoSalvage.Core.Container.DemoHeader.Parse(bytes).MapName;
        (MapAssets? assets, string mapState) = ReadMap(mapName, locator, game, timeline);

        output.WriteLine(
            $"{Path.GetFileName(path)}, recorder {timeline.RecorderEntityIndex?.ToString(CultureInfo.InvariantCulture) ?? "none"}, "
            + $"game {folder ?? "NOT FOUND"}, ticks {timeline.FirstTick}..{timeline.LastTick} by {stride}");
        output.WriteLine($"map {mapName}: {mapState}");

        // Per entity and serial: first tick seen, first tick visible, the systems, the owner, the item.
        Dictionary<(int Entity, int Item), Row> found = [];
        EntityModelSet models = new() { Geometry = assets is null ? EntityModelSet.NoGeometry : assets.Geometry };

        // **The control** (B243): a synthetic visible unusual must come back as one run through the same decision, over a set
        // of its own with the same geometry, or the instrument — not the demo — is what answers "never". Its own, so its entity
        // leaves nothing in the set the frames below pose (B69 fourth round, item 8).
        EntityModelSet controlModels = new() { Geometry = models.Geometry };
        int control = Decided(controlModels, [ControlItem], 0, landed: true).Count;
        output.WriteLine($"control: a synthetic visible unusual decides {control.ToString(CultureInfo.InvariantCulture)} run (expected 1)");

        if (control != 1)
        {
            output.WriteLine("CONTROL FAILED: the decision answers no run for a visible unusual; nothing below would mean anything.");
            return;
        }

        MomentScene scene = new(models, new ViewmodelScene(), NullLogger.Instance)
        {
            Weapons = game.Weapons,
            Appearance = DemoAppearance.Ensure(DemoAppearance.None, timeline, game, NullLogger.Instance),
        };
        BurstEffects bursts = Bursts(models);
        TimelineMoments source = new(timeline);
        List<ScenePlayer> players = [];
        List<SceneProp> props = [];
        bool attachmentControlled = false;

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

            // **The attachment control** (B69 review I): the first control has no model, so it cannot tell a missed attachment
            // from an item that has none. A player in this frame must find his model's `head` through THIS model set — the
            // one the decisions below ask — or every "follows its abs origin" answer is the instrument's.
            if (!attachmentControlled && players.FirstOrDefault(player => player.Team is 2 or 3 && player.IsAlive) is { EntityIndex: > 0 } playing)
            {
                attachmentControlled = true;
                EconParticleItem onHead = ControlItem with
                {
                    Entity = playing.EntityIndex,
                    Systems = [new EconParticle("superrare_burning1", 0, false, false, false, ["head"])],
                };
                IReadOnlyList<EconParticleRun> made = Decided(models, [onHead], tick, landed: true);
                string? attachment = made.Count > 0 ? made[0].Effect.Attachment : null;
                output.WriteLine(
                    $"control: player {playing.EntityIndex.ToString(CultureInfo.InvariantCulture)}'s 'head' at {tick.ToString(CultureInfo.InvariantCulture)} is "
                    + $"{(attachment is null ? "NOT FOUND" : "found")} (expected found)");

                if (attachment is null)
                {
                    output.WriteLine("CONTROL FAILED: the probe's models cannot answer an attachment, so every item would read as following its abs origin.");
                    return;
                }
            }

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

        if (!attachmentControlled)
        {
            output.WriteLine("CONTROL NOT RUN: no sampled tick had a live player to find a 'head' on; nothing is reported.");
            return;
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

    /// <summary>The demo's map as the viewer reads it, or null with why: not named, not installed, or failing to parse.</summary>
    private static (MapAssets? Assets, string State) ReadMap(string mapName, MapLocator locator, GameContent game, DemoTimeline timeline)
    {
        if (mapName.Length == 0 || locator.Find(mapName) is not { } mapPath)
        {
            return (null, "NOT LOADED - not installed, so no model can be read");
        }

        try
        {
            return (LoadedMap.Read(File.ReadAllBytes(mapPath), game, timeline, 0, NullLoggerFactory.Instance, mapPath).Assets, "loaded");
        }
        catch (InvalidDataException failure)
        {
            // Reported, not thrown: the controls then fail and say so, rather than the probe dying before it prints why.
            return (null, $"NOT LOADED - {mapPath} would not parse: {failure.Message}");
        }
    }

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
