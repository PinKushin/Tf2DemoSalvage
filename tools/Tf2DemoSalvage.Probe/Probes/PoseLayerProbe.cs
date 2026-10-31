using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// The layers each player's pose build actually accumulates, and which of them declare autolayers (B624).
/// </summary>
/// <remarks>
/// **Read off the object that did the work** (B243): `LayersOf` is the list `SkeletonPose` accumulates, so this cannot
/// disagree with the pose about what was blended. One `EntityModelSet` walks the whole range, because a transition needs
/// the frame before it.
///
/// **What it answers.** Valve's `AccumulatePose` runs `AddSequenceLayers` for every sequence it accumulates
/// (`bone_setup.cpp:2449`), so a gesture's or a fading sequence's autolayers are accumulated beneath it. A layer here
/// whose sequence declares non-local autolayers with none of them following it inside its bracket is the B624 shape.
///
/// **The control:** the main sequence's own autolayers (`stand_PRIMARY` → `PRIMARY_aimmatrix_idle`) were accumulated
/// before B624 was touched, so a player standing in a primary stance must show them. If it does not, the instrument is
/// broken, not the subject.
///
/// <code>
///   pose-layers f12 30000 30400            every tick, the parents found, then the last tick's lists
///   pose-layers f12 30000 30400 7          only entity 7
/// </code>
/// </remarks>
public sealed class PoseLayerProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "pose-layers";

    /// <inheritdoc/>
    public string Summary =>
        "each player's accumulated pose layers and which declare autolayers (B624): pose-layers <demo> <from> <to> [entity]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count < 3)
        {
            output.WriteLine("pose-layers <demo> <from> <to> [entity]");
            return;
        }

        string? path = DemoCorpus.Find(arguments[0], output);

        if (path is null)
        {
            output.WriteLine($"No demo named '{arguments[0]}'.");
            return;
        }

        int from = int.Parse(arguments[1], CultureInfo.InvariantCulture);
        int to = int.Parse(arguments[2], CultureInfo.InvariantCulture);
        int? only = arguments.Count > 3 ? int.Parse(arguments[3], CultureInfo.InvariantCulture) : null;

        byte[] bytes = File.ReadAllBytes(path);
        DemoTimeline timeline = DemoTimeline.Build(bytes);
        string mapName = Tf2DemoSalvage.Core.Container.DemoHeader.Parse(bytes).MapName;
        MapLocator locator = new(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder);

        if (mapName.Length == 0 || locator.Find(mapName) is not { } mapPath || locator.FindGameFolder() is not { } folder)
        {
            output.WriteLine("The demo's map or the game could not be found.");
            return;
        }

        GameContent game = GameContent.Open(folder, NullLoggerFactory.Instance);
        LoadedMap map = LoadedMap.Read(File.ReadAllBytes(mapPath), game, timeline, 0, NullLoggerFactory.Instance, mapPath);

        if (map.Assets is not { } assets)
        {
            output.WriteLine("The map loaded with no assets.");
            return;
        }

        EntityModelSet models = new() { Geometry = assets.Geometry };
        List<SceneProp> drawn = [];
        List<ScenePlayer> players = [];
        List<ModelInstance> instances = [];

        int playerTicks = 0;
        int layersSeen = 0;
        int brackets = 0;
        int parents = 0;
        int nestedUnder = 0;
        Dictionary<string, (int Count, int FirstTick, int Entity)> byParent = new(StringComparer.Ordinal);

        for (int tick = from; tick <= to; tick++)
        {
            timeline.PlayersAt(tick, players);
            timeline.PropsAt(tick, drawn);
            PlayerProps.Add(players, drawn, new GameAppearance(game.Classes, null), NoBodygroups.Instance);

            if (players.Count > 0)
            {
                models.ViewOrigin = (players[0].X, players[0].Y, players[0].Z);
            }

            models.Add(drawn, assets.Geometry);
            models.UpdateClientSideAnimations(drawn);
            models.Instances(drawn, instances, seconds: tick * timeline.IntervalPerTick);

            foreach (int entity in players.Select(player => player.EntityIndex))
            {
                if ((only is { } wanted && entity != wanted) ||
                    drawn.FirstOrDefault(prop => prop.EntityIndex == entity) is not { } prop ||
                    assets.Geometry(prop.ModelPath)?.Skinned is not { } skinned ||
                    models.LayersOf(entity) is not { } layers)
                {
                    continue;
                }

                playerTicks++;

                for (int index = 0; index < layers.Count; index++)
                {
                    PoseLayer layer = layers[index];
                    layersSeen++;
                    brackets += layer.Locks is { Count: > 0 } ? 1 : 0;

                    if (!skinned.AutoLayersOf(layer.Sequence).Any(entry => !entry.IsLocal))
                    {
                        continue;
                    }

                    parents++;
                    nestedUnder += layer.Nested;
                    string label = skinned.LabelOf(layer.Sequence);

                    byParent[label] = byParent.TryGetValue(label, out (int Count, int FirstTick, int Entity) was)
                        ? (was.Count + 1, was.FirstTick, was.Entity)
                        : (1, tick, entity);
                }

                if (tick == to)
                {
                    Describe(output, models, skinned, prop, layers);
                }
            }
        }

        output.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"{Path.GetFileName(path)} ticks {from}..{to}: {playerTicks} player-ticks, {layersSeen} layers, " +
                $"{brackets} with locks; {parents} layers declare non-local autolayers, {nestedUnder} layers nested under them"));

        foreach ((string label, (int count, int first, int entity)) in byParent.OrderByDescending(entry => entry.Value.Count))
        {
            output.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"  parent '{label}' x{count}, first at tick {first} on entity {entity}"));
        }
    }

    private static void Describe(
        TextWriter output,
        EntityModelSet models,
        PropModels.SkinnedModel skinned,
        SceneProp prop,
        IReadOnlyList<PoseLayer> layers)
    {
        int main = models.FrameOf(prop.EntityIndex)?.Sequence ?? -1;

        output.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"entity {prop.EntityIndex} at ({prop.Pose.X:0} {prop.Pose.Y:0} {prop.Pose.Z:0}) yaw {prop.Pose.Yaw:0} " +
                $"main '{skinned.LabelOf(main)}' locks {skinned.LocksOf(main).Count}, {layers.Count} layers:"));

        foreach (PoseLayer layer in layers)
        {
            string declared = string.Join(
                ", ",
                skinned.AutoLayersOf(layer.Sequence)
                    .Where(entry => !entry.IsLocal)
                    .Select(entry => string.Create(
                        CultureInfo.InvariantCulture,
                        $"{skinned.LabelOf(skinned.RelativeSequence(layer.Sequence, entry.Sequence))} " +
                        $"[{entry.Start:0.##} {entry.Peak:0.##} {entry.Tail:0.##} {entry.End:0.##}] 0x{entry.Flags:X}")));

            output.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"  '{skinned.LabelOf(layer.Sequence)}' w {layer.Weight:0.###} frame {layer.Frame}+{layer.FrameFraction:0.##} " +
                    $"locks {layer.Locks?.Count ?? 0} nested {layer.Nested} autolayers: {declared}"));
        }
    }
}
