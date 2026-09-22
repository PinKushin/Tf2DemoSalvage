using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>Which of its weapon's sounds an item replaces, through the viewer's own <see cref="ItemSchema"/> (B415).</summary>
/// <remarks>
/// **The check that the shipped schema says what the synthetic tests assume** — that the Black Box replaces
/// `special1` and The Original does not, which decides the explosion sound for a real match's blasts.
///
/// <code>
///   weapon-sounds 228        — every WeaponSound_t the item replaces, heard as a spectator
///   weapon-sounds 228 2      — the same, heard from team 2 (red)
/// </code>
/// </remarks>
public sealed class WeaponSoundProbe : IProbe
{
    /// <summary>`WeaponSound_t`'s sixteen values, so the output names them.</summary>
    private static readonly string[] Categories =
    [
        "empty", "single_shot", "single_shot_npc", "double_shot", "double_shot_npc", "burst", "reload",
        "reload_npc", "melee_miss", "melee_hit", "melee_hit_world", "special1", "special2", "special3", "taunt",
        "deploy",
    ];

    /// <inheritdoc/>
    public string Name => "weapon-sounds";

    /// <inheritdoc/>
    public string Summary => "which weapon sounds an item replaces: weapon-sounds <item index> [team]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0 ||
            !int.TryParse(arguments[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int item))
        {
            output.WriteLine("Usage: weapon-sounds <item index> [team]");
            return;
        }

        int team = arguments.Count > 1
            ? int.Parse(arguments[1], CultureInfo.InvariantCulture)
            : 1;

        if (new MapLocator(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder)
            .FindGameFolder() is not { } folder)
        {
            output.WriteLine("The game folder could not be found, so there is no schema to read.");
            return;
        }

        string path = Path.Combine(folder, "scripts", "items", "items_game.txt");

        if (!File.Exists(path))
        {
            output.WriteLine($"No items_game.txt at {path}.");
            return;
        }

        ItemSchema schema = ItemSchema.Read(File.ReadAllBytes(path));

        int replaced = 0;

        for (int sound = 0; sound < Categories.Length; sound++)
        {
            if (schema.WeaponSoundReplacement(item, team, sound) is not { } name)
            {
                continue;
            }

            replaced++;

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  {sound,2} {Categories[sound],-16} {name}"));
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"item {item}, team {team}: {replaced} of {Categories.Length} weapon sounds replaced"));
    }
}
