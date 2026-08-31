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
/// Every player at a tick, what they carry, and which of it survives to be drawn.
/// </summary>
/// <remarks>
/// **Written because "player X is missing their weapon" kept being answered by squinting at a
/// screenshot.** The medic's medigun was chased through four instruments in one session and each
/// one answered a different question than the one asked:
///
/// - `props` grouped by model path, so every weapon whose model comes from the ITEM — 47 of them at
///   one tick, all with an empty path — collapsed into a single line labelled `CTFWearable`. The
///   medigun was in that group and the report said it was not.
/// - `instance` never runs `WeaponPropModels.Resolve`, so a weapon named only by its item cannot
///   appear in it at all. Its "zero mediguns" was a fact about the probe.
/// - `weapon-models` walks with a stride and reports DISTINCT (class, item, owner class), so it
///   says a medigun resolves somewhere without saying whether this player has one now.
/// - The viewer log reports per MODEL, deduplicated, so one medigun drawing and two not looks
///   exactly like three drawing.
///
/// **So this reports per PLAYER and per ENTITY, at one tick, with no deduplication anywhere.** The
/// question it answers is the one that gets asked: this player, right now, holds what — and if the
/// screen disagrees, which rule dropped it.
///
/// **It runs the production filters in the production order** — `WeaponPropModels.Resolve`, then
/// `WeaponVisibility` (`C_BaseCombatWeapon::ShouldDraw`), then `DisguiseVisibility`
/// (`CTFWearable::ShouldDraw` / `CTFWeaponBase::ShouldDraw`) — and names the one that removed each
/// prop. A probe that reimplemented those would agree with itself rather than with the viewer.
///
/// <code>
///   carried tf2-2026-pub-pov-clean 14000
///   carried tf2-2026-pub-pov-clean 14000 5
/// </code>
///
/// The optional third argument is a TF2 class number, so `5` is every medic and nobody else.
/// </remarks>
public sealed class CarriedProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "carried";

    /// <inheritdoc/>
    public string Summary =>
        "what each player holds and which rule drops the rest: carried <demo> <tick> [class]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count < 2)
        {
            output.WriteLine("carried <demo> <tick> [class]");
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

        string? folder = new MapLocator(
            MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder).FindGameFolder();

        // **Said out loud rather than assumed**, because every model path below comes from the
        // item schema in this folder — with no install, every weapon reads as "no model" and the
        // report would blame the decoder for a missing game.
        output.WriteLine(
            $"{Path.GetFileName(path)} tick {tick.ToString(CultureInfo.InvariantCulture)}, "
            + $"game {folder ?? "NOT FOUND — every model lookup will answer null"}");

        GameContent game = GameContent.Open(folder, NullLoggerFactory.Instance);
        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));

        List<ScenePlayer> players = [];
        timeline.PlayersAt(tick, players);

        List<SceneProp> props = [];
        timeline.PropsAt(tick, props);

        new WeaponPropModels().Resolve(props, players, game.Weapons.For);

        // Each filter's SURVIVORS, so a prop's absence can be attributed to the rule that removed
        // it rather than reported as a bare absence.
        HashSet<int> heldOut =
            [.. WeaponVisibility.Visible(props).Select(prop => prop.EntityIndex)];

        HashSet<int> notDisguise =
            [.. DisguiseVisibility.Visible(props, players).Select(prop => prop.EntityIndex)];

        foreach (ScenePlayer player in players
            .Where(player => only is not { } wanted || player.PlayerClass == wanted)
            .OrderBy(player => player.EntityIndex))
        {
            output.WriteLine(
                $"player {player.EntityIndex,4}  class {player.PlayerClass?.ToString(CultureInfo.InvariantCulture) ?? "?",-2}  "
                + $"team {player.Team?.ToString(CultureInfo.InvariantCulture) ?? "?"}  "
                + $"{(player.IsAlive ? "alive" : "DEAD ")}  "
                + $"at ({player.X:0} {player.Y:0} {player.Z:0})  "
                + $"holds {player.ActiveWeapon?.ToString(CultureInfo.InvariantCulture) ?? "nothing"} "
                + $"({player.WeaponClass ?? "?"} item {player.WeaponItem?.ToString(CultureInfo.InvariantCulture) ?? "?"})");

            List<SceneProp> mine =
            [
                .. props.Where(prop =>
                    prop.AttachedTo == player.EntityIndex || prop.OwnedBy == player.EntityIndex),
            ];

            if (mine.Count == 0)
            {
                output.WriteLine("                carries nothing at all");
                continue;
            }

            foreach (SceneProp prop in mine.OrderBy(prop => prop.EntityIndex))
            {
                // **Named after the rule, not after the outcome.** "Missing" is what a screenshot
                // says; which engine rule removed it is what tells you whether it was right to.
                string verdict = Verdict(prop, heldOut, notDisguise);

                output.WriteLine(
                    $"   {prop.EntityIndex,4}  {verdict}  {prop.ClassName ?? "?",-26} "
                    + $"item {prop.ItemDefinitionIndex?.ToString(CultureInfo.InvariantCulture) ?? "-",-6} "
                    + $"state {prop.WeaponState?.ToString(CultureInfo.InvariantCulture) ?? "-"}  "
                    + $"merged {(prop.BoneMerged ? "y" : "n")}  "
                    + $"'{prop.ModelPath}'");
            }
        }
    }

    /// <summary>Which rule, if any, took this prop out of the scene.</summary>
    /// <remarks>
    /// Ordered as the scene orders them, so a prop dropped by two rules is reported under the first
    /// one that would have removed it — which is the one to go and read.
    /// </remarks>
    private static string Verdict(SceneProp prop, HashSet<int> held, HashSet<int> notDisguise)
    {
        if (!held.Contains(prop.EntityIndex))
        {
            return "HOLSTERED (ShouldDraw)";
        }

        if (!notDisguise.Contains(prop.EntityIndex))
        {
            return "DISGUISE  (ShouldDraw)";
        }

        return prop.ModelPath.Length == 0 ? "NO MODEL              " : "drawn                 ";
    }
}
