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
/// What each player's equipment does to their body — the parts a hat removes (B352).
/// </summary>
/// <remarks>
/// **Written because a body number is invisible until something asks.** A player whose hat sits on
/// top of their hair looks like a player wearing a hat, and the suite that covers
/// <c>PlayerProps.Add</c> passes with a stub wardrobe that hides nothing. Only a real demo, a real
/// install and a real <c>.mdl</c> can say whether the wiring reaches the drawn prop.
///
/// **It reports the delegate calls the production code made, not a second computation.** The
/// resolver handed to <c>PlayerProps.Add</c> is the model set's own <c>WithBodygroup</c> with a
/// recording wrapper around it, so every line below is a value the scene actually used — the rule
/// B243 exists for. A probe that re-derived the number from the schema would agree with the schema
/// and say nothing about the scene.
///
/// **Two passes, because a model must be loaded before its parts can be named.** `MomentScene`
/// packs models AFTER the player props are built (`MomentScene.cs:478`), so on the first frame a
/// player is seen <c>WithBodygroup</c> has no <c>.mdl</c> and answers the body unchanged. The first
/// pass here exists only to give the model set the player models to load; the second is the frame
/// whose answer is reported, which is what the viewer draws from frame two onward.
///
/// <code>
///   bodygroups tf2-2026-pub-pov-clean 14000
///   bodygroups tf2-2026-pub-pov-clean 14000 5
/// </code>
///
/// The optional third argument is a TF2 class number.
/// </remarks>
public sealed class BodygroupProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "bodygroups";

    /// <inheritdoc/>
    public string Summary =>
        "what each player's items hide on them: bodygroups <demo> <tick> [class]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count < 2)
        {
            output.WriteLine("bodygroups <demo> <tick> [class]");
            return;
        }

        if (DemoCorpus.Find(arguments[0], output) is not { } path)
        {
            output.WriteLine($"No demo named '{arguments[0]}'.");
            return;
        }

        int tick = int.Parse(arguments[1], CultureInfo.InvariantCulture);

        int? only = arguments.Count > 2
            ? int.Parse(arguments[2], CultureInfo.InvariantCulture)
            : null;

        // **Said out loud, because without an install every answer below is zero for a reason that
        // has nothing to do with the code under test.**
        MapLocator locator = new(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder);

        if (locator.FindGameFolder() is not { } folder)
        {
            output.WriteLine(
                "No TF2 install found. Every bodygroup lookup would answer zero, which says "
                + "nothing about the wiring — so this refuses rather than reporting it.");

            return;
        }

        byte[] bytes = File.ReadAllBytes(path);

        GameContent game = GameContent.Open(folder, NullLoggerFactory.Instance);
        DemoTimeline timeline = DemoTimeline.Build(bytes);

        // **The map is loaded for its GEOMETRY LOADER, which is the only route to a `.mdl`.**
        // `EntityModelSet.Geometry` answers nothing until a map sets it, so a set built without one
        // packs no models and every `WithBodygroup` returns the body unchanged — reported as "no
        // such part on this model" for parts that certainly exist. This probe printed exactly that
        // for all 24 requests on its first run, which was a fact about the probe.
        string mapName = Tf2DemoSalvage.Core.Container.DemoHeader.Parse(bytes).MapName;

        if (mapName.Length == 0 || locator.Find(mapName) is not { } mapPath)
        {
            output.WriteLine($"Map '{mapName}' is not installed, so no model can be read.");
            return;
        }

        LoadedMap map = LoadedMap.Read(
            File.ReadAllBytes(mapPath), game, timeline, 0, NullLoggerFactory.Instance);

        if (map.Assets is not { } assets)
        {
            output.WriteLine($"The assets for '{mapName}' did not load.");
            return;
        }

        IPlayerAppearance appearance = DemoAppearance.Ensure(
            DemoAppearance.None, timeline, game, NullLogger.Instance);

        List<ScenePlayer> players = [];
        timeline.PlayersAt(tick, players);

        List<SceneProp> props = [];
        timeline.PropsAt(tick, props);

        new WeaponPropModels().Resolve(props, players, game.Weapons.For);

        int equipment = props.Count;

        EntityModelSet models = new() { Geometry = assets.Geometry };

        // The load pass — see the remarks. Its answers are discarded; only the models it packs
        // matter, and the resolver is the identity so nothing here can be mistaken for a result.
        PlayerProps.Add(players, props, appearance, (_, _, _, body) => body);
        _ = models.Add(props);

        // **The control, and it is not optional** (`docs/memory/an-empty-search-needs-a-control.md`).
        // Every player model shipped with TF2 carries a `hat` part, so a run that reports no
        // changes AND fails this line is measuring a model set that loaded nothing.
        output.WriteLine(
            "control: setting 'hat' to 1 on models/player/scout.mdl gives " +
            models.WithBodygroup("models/player/scout.mdl", "hat", 1, 0)
                .ToString(CultureInfo.InvariantCulture) +
            " (0 means the model is not loaded, so nothing below can be believed)");

        List<(int Player, string Group, int Value, int Before, int After)> asked = [];

        List<SceneProp> drawn = [.. props.Take(equipment)];
        int wearer = 0;

        foreach (ScenePlayer player in players)
        {
            wearer = player.EntityIndex;

            PlayerProps.Add(
                [player],
                drawn,
                appearance,
                (model, group, value, body) =>
                {
                    int after = models.WithBodygroup(model, group, value, body);

                    asked.Add((wearer, group, value, body, after));

                    return after;
                });
        }

        output.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"{Path.GetFileName(path)} tick {tick}, game {folder}, "
                + $"{equipment} props and {players.Count} players"));

        int dressed = 0;

        foreach (SceneProp prop in drawn
            .Skip(equipment)
            .OrderBy(prop => prop.EntityIndex))
        {
            ScenePlayer player = players.First(each => each.EntityIndex == prop.EntityIndex);

            if (only is { } wanted && player.PlayerClass != wanted)
            {
                continue;
            }

            List<SceneProp> worn =
            [
                .. props.Take(equipment).Where(each =>
                    each.ItemDefinitionIndex is not null
                    && (each.OwnedBy ?? each.AttachedTo) == prop.EntityIndex),
            ];

            if (prop.Pose.Body != 0)
            {
                dressed++;
            }

            output.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"player {prop.EntityIndex,4}  class "
                    + $"{player.PlayerClass?.ToString(CultureInfo.InvariantCulture) ?? "?",-2}  "
                    + $"body {prop.Pose.Body,4}  wears {worn.Count} items  "
                    + $"{Path.GetFileName(prop.ModelPath)}"));

            foreach ((int _, string group, int value, int before, int after) in asked
                .Where(call => call.Player == prop.EntityIndex))
            {
                string moved = before == after
                    ? "   (NO CHANGE — no such part on this model)"
                    : string.Empty;

                output.WriteLine(
                    $"                set '{group}' to " +
                    value.ToString(CultureInfo.InvariantCulture) + ": " +
                    before.ToString(CultureInfo.InvariantCulture) + " -> " +
                    after.ToString(CultureInfo.InvariantCulture) + moved);
            }

            foreach ((SceneProp item, ItemBodygroups groups) in worn
                .Select(item => (item, appearance.BodygroupsOf(item.ItemDefinitionIndex ?? -1)))
                .Where(pair => pair.Item2.Named is { Count: > 0 }))
            {
                output.WriteLine(
                    "                item " +
                    (item.ItemDefinitionIndex?.ToString(CultureInfo.InvariantCulture) ?? "?") +
                    " declares " +
                    string.Join(", ", groups.Named.Select(pair => $"{pair.Key}={pair.Value}")) +
                    (groups.DeployedOnly ? " (deployed only)" : string.Empty));
            }
        }

        // **The denominator, because "12 players carry a body number" means nothing without it.**
        output.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"{dressed} of {drawn.Count - equipment} drawn players carry a non-zero body "
                + $"number, from {asked.Count} bodygroup requests"));
    }
}
