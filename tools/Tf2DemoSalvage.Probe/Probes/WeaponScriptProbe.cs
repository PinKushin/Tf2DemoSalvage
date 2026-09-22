using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>A weapon script as the game ships it, decrypted (B415).</summary>
/// <remarks>
/// **`m_iWeaponID` is an index into `g_aWeaponNames` and the alias IS the script's name.**
/// `WeaponIdToAlias` (`tf_shareddefs.cpp:1224`) answers `TF_WEAPON_ROCKETLAUNCHER` for 22, and
/// `ReadWeaponDataFromFileForSlot` (`weapon_parse.cpp:286`) opens `scripts/` plus that — the plain `.txt` first and
/// the ICE-encrypted `.ctx` after, which is why a `ls` of `tf/scripts/` shows no weapon scripts at all.
///
/// **The question this exists to answer** is what `TFExplosionCallback` reads out of one: `ExplosionEffect`,
/// `ExplosionPlayerEffect` and `ExplosionWaterEffect` pick the particle system for every explosion in a demo, and
/// `BulletsPerShot` decides how many seeds a hitscan shot runs through. Guessing any of them would put a plausible
/// effect in a wrong place.
///
/// **Through `GameArchives` and `WeaponScript`, which is what the viewer uses.** A probe that opened the VPKs itself
/// would agree with whoever wrote the probe; this one reads the search path out of `gameinfo.txt` and takes the
/// extension order from production, so it cannot disagree with `WeaponRoles` about which file a weapon has.
///
/// <code>
///   weapon-script                      — every alias with a script, and the keys the effects need
///   weapon-script &lt;alias or id&gt;        — one script in full
/// </code>
/// </remarks>
public sealed class WeaponScriptProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "weapon-script";

    /// <inheritdoc/>
    public string Summary =>
        "a weapon script as the game ships it, decrypted: weapon-script [alias or m_iWeaponID]";

    /// <summary>The keys the effect work reads, printed for every weapon in the summary table.</summary>
    private static readonly string[] Interesting =
    [
        "WeaponType", "BulletsPerShot", "Spread", "Range", "Damage",
        "ExplosionEffect", "ExplosionPlayerEffect", "ExplosionWaterEffect", "ExplosionSound",
    ];

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (new MapLocator(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder)
                .FindGameFolder() is not { } game)
        {
            output.WriteLine("No TF2 install found, so there is nothing to read.");
            return;
        }

        GameArchives archives = GameArchives.Open(game);

        if (archives.IsEmpty)
        {
            output.WriteLine($"No content sources under {game}.");
            return;
        }

        if (arguments.Count == 0)
        {
            Summarise(output, archives);
            return;
        }

        string alias = Resolve(arguments[0]);

        if (WeaponScript.Read(archives.Read, alias) is not { } script)
        {
            output.WriteLine($"No scripts/{alias}.txt or .ctx in the game's content.");

            // **The control an absence needs.** A miss here is far more likely to be this probe looking in the
            // wrong place than a weapon the game does not ship
            // (`docs/memory/instrument-bugs-outnumber-decoder-bugs.md#an-empty-search-needs-a-control`).
            output.WriteLine(
                WeaponScript.Read(archives.Read, "TF_WEAPON_ROCKETLAUNCHER") is null
                    ? "  and neither is the rocket launcher's, so the LOOKUP is broken, not the weapon."
                    : "  the rocket launcher's reads, so the lookup works and this alias has no script.");

            return;
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture, $"scripts/{alias}  ({script.Text.Length} bytes)"));
        output.WriteLine();
        output.WriteLine(Encoding.UTF8.GetString(script.Text.Span).TrimEnd('\0'));
    }

    /// <summary>Every alias that has a script, with the keys the effect work reads.</summary>
    private static void Summarise(TextWriter output, GameArchives archives)
    {
        int found = 0;

        for (int id = 0; id < TfWeaponAliases.All.Count; id++)
        {
            string alias = TfWeaponAliases.All[id];

            if (WeaponScript.Read(archives.Read, alias) is not { } script)
            {
                continue;
            }

            found++;

            List<string> said = [];

            foreach (string key in Interesting)
            {
                if (script.Value(key) is { Length: > 0 } value)
                {
                    said.Add($"{key}={value}");
                }
            }

            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {id,3}  {alias}"));
            output.WriteLine($"       {string.Join("  ", said)}");
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  {found} of {TfWeaponAliases.All.Count} aliases have a script."));
    }

    /// <summary>The alias for an argument that may be a name or a wire id.</summary>
    private static string Resolve(string argument) =>
        int.TryParse(argument, CultureInfo.InvariantCulture, out int id)
            ? TfWeaponAliases.Of(id) ?? argument
            : argument;
}
