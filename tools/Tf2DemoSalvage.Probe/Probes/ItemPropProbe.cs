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

/// <summary>Which props carry the named items, the model the wire gave each, and the model production draws.</summary>
/// <remarks>
/// **The census for a question about the item schema** — first B105's 18 `"model_player" ""` declarations: does any
/// recording carry one, and what is drawn for it. Each prop track whose `m_iItemDefinitionIndex` is named is sampled at
/// its first tick through `WeaponPropModels.Resolve` handed `WeaponModels.For`, the step `MomentScene.Build` runs, so the
/// drawn model is the one production CARRIED out of that step rather than a second reading of the rule (B243).
///
/// **The control comes first**: the tracks carrying ANY item index. It is zero before the 2009 build, which networks no
/// index, and an empty census beside an empty control measures nothing.
/// <code>
///   item-props 5,195,241 z1800 C:/Users/pinku/source/repos/PinKushin/Tf2DemoSalvage/tools/corpus/local/demostf-cp_process_f12-2026-08-07.dem
/// </code>
/// </remarks>
public sealed class ItemPropProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "item-props";

    /// <inheritdoc/>
    public string Summary =>
        "props carrying the named items, the wire's model and the drawn one: item-props <item,item...> <demo> [demo...]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count < 2)
        {
            output.WriteLine("item-props <item,item...> <demo> [demo...]");
            return;
        }

        HashSet<int> wanted =
        [
            .. arguments[0]
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(item => int.Parse(item, CultureInfo.InvariantCulture)),
        ];

        string? folder = new MapLocator(
            MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder).FindGameFolder();

        // Opened once for every demo named: the schema is eight megabytes and the census walks a corpus.
        GameContent game = GameContent.Open(folder, NullLoggerFactory.Instance);
        output.WriteLine($"game {folder ?? "NOT FOUND - every schema lookup answers null"}");

        foreach (string demo in arguments.Skip(1))
        {
            if (DemoCorpus.Find(demo, output) is not { } path)
            {
                output.WriteLine($"No demo named '{demo}'.");
                continue;
            }

            try
            {
                Census(output, path, wanted, game);
            }
            catch (InvalidDataException undecodable)
            {
                // A writer-truncated schema decodes no entities at all; said, and the census goes on.
                output.WriteLine($"{Path.GetFileName(path)}: no entities — {undecodable.Message}");
            }
        }
    }

    /// <summary>One demo's census.</summary>
    private static void Census(TextWriter output, string path, HashSet<int> wanted, GameContent game)
    {
        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));

        List<ScenePropTrack> withItem = [.. timeline.Props.Where(track => track.ItemDefinitionIndex is not null)];
        List<ScenePropTrack> named = [.. withItem.Where(track => wanted.Contains(track.ItemDefinitionIndex ?? -1))];

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{Path.GetFileName(path)}: {withItem.Count} prop tracks carry an item index (the control), {named.Count} a named one"));

        foreach (ScenePropTrack track in named)
        {
            int end = Math.Min(track.EndTick, timeline.LastTick + 1);
            int sampled = 0;
            int present = 0;
            string first = "never present at a sample";

            // **Sampled across the whole life, because a holstered weapon is not a prop at that moment** — the census's
            // own control run found the stock sticky launcher absent at its first tick. Half a second apart.
            for (int tick = track.FirstTick; tick < end; tick += SampleTicks)
            {
                sampled++;

                if (Sample(timeline, track, tick, game) is not { } seen)
                {
                    continue;
                }

                if (present++ == 0)
                {
                    first = seen;
                }
            }

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  item {track.ItemDefinitionIndex} {track.ClassName} entity {track.EntityIndex} ticks {track.FirstTick}+{end - track.FirstTick} "
                + $"present {present}/{sampled} samples; first: {first}"));
        }
    }

    /// <summary>Half a second at 66 ticks a second.</summary>
    private const int SampleTicks = 33;

    /// <summary>The track's prop at one tick, before and after production's resolution, or null when it is not a prop then.</summary>
    private static string? Sample(DemoTimeline timeline, ScenePropTrack track, int tick, GameContent game)
    {
        List<SceneProp> props = [];
        List<ScenePlayer> players = [];
        timeline.PropsAt(tick, props);
        timeline.PlayersAt(tick, players);

        int at = props.FindIndex(prop => prop.EntityIndex == track.EntityIndex);

        if (at < 0)
        {
            return null;
        }

        SceneProp wire = props[at];

        // Resolve replaces an entry in place when the item wins, so the same slot holds what is drawn.
        new WeaponPropModels().Resolve(props, players, game.Weapons.For);

        SceneProp drawn = props[at];
        int? owner = drawn.AttachedTo ?? drawn.OwnedBy;
        int? ownerClass = players.Where(player => player.EntityIndex == owner).Select(player => player.PlayerClass).FirstOrDefault();

        return string.Create(
            CultureInfo.InvariantCulture,
            $"tick {tick} owner {owner} class {ownerClass} wire '{wire.ModelPath}' drawn '{drawn.ModelPath}' can draw {EntityModelSet.CanDraw(drawn)}");
    }
}
