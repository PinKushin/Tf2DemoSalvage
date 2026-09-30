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
/// Which held weapons carry an item whose <c>anim_slot</c> overrides the script, and the role each player was drawn with.
/// </summary>
/// <remarks>
/// **The census B105's residual asked for.** `CTFWeaponBase::GetActivityWeaponRole` (`tf_weaponbase.cpp:4185`)
/// replaces the script's `WeaponType` with the item's `anim_slot` whenever that is non-negative, so the question is
/// how many player-ticks a real recording spends holding such an item — and what production drew them with.
///
/// **Both halves are CARRIED values, never recomputed** (B243): the item's slot is `ItemSchema.AnimSlot` on the index
/// the timeline decoded, the script's role is `WeaponRoles.Suffix` on the weapon's own class, and the drawn role is the
/// <c>Slot</c> that `PlayerProps.Add` — the draw path itself — put on the player's pose. A row whose drawn role is the
/// script's while its item names another is the defect, read off the output rather than predicted.
///
/// <code>
///   anim-slot tf2-2026-pub-pov-clean
///   anim-slot C:/Users/pinku/source/repos/PinKushin/Tf2DemoSalvage/tools/corpus/local/demostf-cp_process_f12-2026-08-07.dem
/// </code>
/// </remarks>
public sealed class AnimSlotProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "anim-slot";

    /// <inheritdoc/>
    public string Summary =>
        "held items whose anim_slot overrides the script, and the role production drew: anim-slot <demo> [demo...]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count < 1)
        {
            output.WriteLine("anim-slot <demo> [demo...]");
            return;
        }

        string? folder = new MapLocator(
            MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder).FindGameFolder();

        // Opened once for every demo named: the schema is eight megabytes and the census walks a corpus.
        GameContent game = GameContent.Open(folder, NullLoggerFactory.Instance);

        foreach (string demo in arguments)
        {
            if (DemoCorpus.Find(demo, output) is not { } path)
            {
                output.WriteLine($"No demo named '{demo}'.");
                continue;
            }

            try
            {
                Census(output, path, folder, game);
            }
            catch (InvalidDataException undecodable)
            {
                // A writer-truncated schema decodes no entities at all (B-era recordings); said, and the census goes on.
                output.WriteLine($"{Path.GetFileName(path)}: no entities — {undecodable.Message}");
            }
        }
    }

    /// <summary>One demo's census.</summary>
    private static void Census(TextWriter output, string path, string? folder, GameContent game)
    {
        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));

        // Built exactly as the viewer builds it, so the roles and the schema are the ones production reads.
        if (DemoAppearance.Ensure(DemoAppearance.None, timeline, game, NullLogger.Instance) is not GameAppearance
            {
                Roles: { } roles, Items: { } items,
            } appearance)
        {
            output.WriteLine($"{Path.GetFileName(path)}: no install at '{folder ?? "(none)"}', nothing to resolve");
            return;
        }

        Dictionary<(string Weapon, int? Item, int? Class, int AnimSlot, string Script, string? Drawn), int> rows = [];
        HashSet<(int Player, int Item)> overriddenHolders = [];
        int drawnTicks = 0;
        int indexedTicks = 0;
        int overriddenTicks = 0;
        int changedTicks = 0;
        List<SceneProp> props = [];

        foreach (IReadOnlyList<ScenePlayer> players in timeline.Frames.Select(frame => frame.Players))
        {
            props.Clear();
            PlayerProps.Add(players, props, appearance, NoBodygroups.Instance);

            foreach (SceneProp prop in props)
            {
                if (players.FirstOrDefault(player => player.EntityIndex == prop.EntityIndex) is not
                    { WeaponClass: { } weapon } holder)
                {
                    continue;
                }

                drawnTicks++;

                int slot = holder.WeaponItem is { } item ? items.AnimSlot(item) : -1;
                string script = roles.Suffix(weapon, holder.PlayerClass);

                if (holder.WeaponItem is not null)
                {
                    indexedTicks++;
                }

                if (slot >= 0)
                {
                    overriddenTicks++;
                    overriddenHolders.Add((holder.EntityIndex, holder.WeaponItem ?? -1));

                    if (!string.Equals(prop.Pose.Slot, script, StringComparison.Ordinal))
                    {
                        changedTicks++;
                    }
                }

                (string, int?, int?, int, string, string?) key =
                    (weapon, holder.WeaponItem, holder.PlayerClass, slot, script, prop.Pose.Slot);
                rows[key] = rows.GetValueOrDefault(key) + 1;
            }
        }

        output.WriteLine(
            $"{Path.GetFileName(path)}: {drawnTicks.ToString(CultureInfo.InvariantCulture)} drawn player-ticks holding a weapon, "
            + $"{indexedTicks.ToString(CultureInfo.InvariantCulture)} with an item index, "
            + $"{overriddenTicks.ToString(CultureInfo.InvariantCulture)} holding an item whose anim_slot >= 0 "
            + $"({overriddenHolders.Count.ToString(CultureInfo.InvariantCulture)} distinct player+item), "
            + $"{changedTicks.ToString(CultureInfo.InvariantCulture)} of those drawn with a role other than the script's");

        foreach (((string weapon, int? item, int? playerClass, int slot, string script, string? drawn), int count) in rows
            .Where(row => row.Key.AnimSlot >= 0)
            .OrderByDescending(row => row.Value))
        {
            string name = slot < ItemSchema.WeaponTypeSubstrings.Count ? ItemSchema.WeaponTypeSubstrings[slot] : "?";

            output.WriteLine(
                $"  {count,8}  {weapon,-28} item {item?.ToString(CultureInfo.InvariantCulture) ?? "-",-6} "
                + $"class {playerClass?.ToString(CultureInfo.InvariantCulture) ?? "?",-2} "
                + $"anim_slot {slot.ToString(CultureInfo.InvariantCulture),2} {name,-15} "
                + $"script {script,-10} drawn {drawn ?? "(null)"}");
        }
    }
}
