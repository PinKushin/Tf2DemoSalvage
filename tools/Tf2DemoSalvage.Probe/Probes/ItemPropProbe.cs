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

/// <summary>Which props carry the named items, the model the wire gave each, the one production resolves, and whether it is shown.</summary>
/// <remarks>
/// **The census for a question about the item schema** — first B105's 18 `"model_player" ""` declarations: does any
/// recording carry one, and what does production do with it; then the four `model_world` items. Each prop track whose
/// `m_iItemDefinitionIndex` is named is sampled through `WeaponPropModels.Resolve` handed `WeaponModels.For` and
/// `WorldDisplayModel`, the step `MomentScene.Build` runs, so the resolved model is the one production CARRIED out of that
/// step rather than a second reading of the rule (B243).
///
/// **Resolved is not shown, and the first version said "drawn" for both.** A holstered weapon is resolved like any prop and
/// then dropped by `WeaponVisibility` — `C_BaseCombatWeapon::ShouldDraw`, `c_basecombatweapon.cpp:399` — so the one change
/// this census found, a spellbook resolved to its carrier's hands, turned out never to reach the screen. Each sample is now
/// also asked whether that rule keeps it, and only a kept prop with a model counts as shown with one.
///
/// **The control comes first**: the tracks carrying ANY item index. It is zero before the 2009 build, which networks no
/// index, and an empty census beside an empty control measures nothing. Samples are half a second apart unless a stride
/// is given — a holstered weapon is no prop at all at some ticks, so a track is walked over its whole life.
/// <code>
///   item-props 5,195,241 z1800 C:/Users/pinku/source/repos/PinKushin/Tf2DemoSalvage/tools/corpus/local/demostf-cp_process_f12-2026-08-08-2207.dem
///   item-props 1070 1 C:/Users/pinku/source/repos/PinKushin/Tf2DemoSalvage/tools/corpus/local/20150119_2240_cp_process_final_(ovo)_blu.dem
/// </code>
/// </remarks>
public sealed class ItemPropProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "item-props";

    /// <inheritdoc/>
    public string Summary =>
        "props carrying the named items, the wire's model, the resolved one and whether it is shown: item-props <item,item...> [stride] <demo> [demo...]";

    /// <summary>Half a second at 66 ticks a second, when no stride is given.</summary>
    private const int DefaultStride = 33;

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count < 2)
        {
            output.WriteLine("item-props <item,item...> [stride] <demo> [demo...]");
            return;
        }

        HashSet<int> wanted =
        [
            .. arguments[0]
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(item => int.Parse(item, CultureInfo.InvariantCulture)),
        ];

        // A bare number second is the stride; a demo's name never is one.
        bool strided = int.TryParse(arguments[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int stride) && stride > 0;
        stride = strided ? stride : DefaultStride;

        string? folder = new MapLocator(
            MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder).FindGameFolder();

        // Opened once for every demo named: the schema is eight megabytes and the census walks a corpus.
        GameContent game = GameContent.Open(folder, NullLoggerFactory.Instance);
        output.WriteLine($"game {folder ?? "NOT FOUND - every schema lookup answers null"}, every {stride.ToString(CultureInfo.InvariantCulture)} ticks");

        foreach (string demo in arguments.Skip(strided ? 2 : 1))
        {
            if (DemoCorpus.Find(demo, output) is not { } path)
            {
                output.WriteLine($"No demo named '{demo}'.");
                continue;
            }

            try
            {
                Census(output, path, wanted, stride, game);
            }
            catch (InvalidDataException undecodable)
            {
                // A writer-truncated schema decodes no entities at all; said, and the census goes on.
                output.WriteLine($"{Path.GetFileName(path)}: no entities — {undecodable.Message}");
            }
        }
    }

    /// <summary>One demo's census.</summary>
    private static void Census(TextWriter output, string path, HashSet<int> wanted, int stride, GameContent game)
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
            int shown = 0;
            int shownWithModel = 0;
            string first = "never present at a sample";

            for (int tick = track.FirstTick; tick < end; tick += stride)
            {
                sampled++;

                if (Sample(timeline, track, tick, game) is not { } seen)
                {
                    continue;
                }

                if (present++ == 0)
                {
                    first = seen.Text;
                }

                if (seen.Shown)
                {
                    shown++;
                }

                // The sample that matters is the first one a viewer would SEE, so it replaces the first-present one.
                if (seen.Shown && seen.Model && shownWithModel++ == 0)
                {
                    first = seen.Text;
                }
            }

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  item {track.ItemDefinitionIndex} {track.ClassName} entity {track.EntityIndex} ticks {track.FirstTick}+{end - track.FirstTick} "
                + $"present {present}/{sampled} samples, shown {shown}, shown with a model {shownWithModel}; first: {first}"));
        }
    }

    /// <summary>The track's prop at one tick, before and after production's resolution, or null when it is not a prop then.</summary>
    private static (string Text, bool Shown, bool Model)? Sample(DemoTimeline timeline, ScenePropTrack track, int tick, GameContent game)
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

        // Resolve replaces an entry in place when the item wins, so the same slot holds what it resolved to.
        new WeaponPropModels().Resolve(props, players, game.Weapons.For, game.Weapons.WorldDisplayModel);

        SceneProp resolved = props[at];
        bool shown = WeaponVisibility.Visible(props).Any(prop => prop.EntityIndex == track.EntityIndex);
        bool model = EntityModelSet.CanDraw(resolved);
        int? owner = resolved.AttachedTo ?? resolved.OwnedBy;
        int? ownerClass = players.Where(player => player.EntityIndex == owner).Select(player => player.PlayerClass).FirstOrDefault();

        return (string.Create(
            CultureInfo.InvariantCulture,
            $"tick {tick} owner {owner} class {ownerClass} state {resolved.WeaponState?.ToString(CultureInfo.InvariantCulture) ?? "-"} "
            + $"wire '{wire.ModelPath}' resolved '{resolved.ModelPath}' model {model} shown {shown}"), shown, model);
    }
}
