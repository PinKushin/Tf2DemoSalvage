using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// How many items in a demo are painted, and with what — TF2's `ItemTintColor` (B330).
/// </summary>
/// <remarks>
/// **The number that sets B330's priority, and priority is all it sets** — a divergence is owed
/// whatever the count says (D137). It is worth having because paint is the opposite case to the
/// gold ragdoll: that flag appears on 0 of 566 corpses and had to be authored, and this one is
/// expected to be everywhere in ordinary play. Expected is not measured.
///
/// **It reports through the production path.** `WeaponModels.PaintFor` resolves the item's
/// attributes with `AttributesFor`, which is the four-branch `IterateAttributes` every other
/// attribute question here goes through — so this cannot disagree with what a renderer would see.
/// A probe that walked `m_AttributeList` itself would agree with whoever wrote the probe.
///
/// **Colours are printed as they are packed, and named where TF2 names them.** A count of "12
/// painted items" says nothing about whether the values are colours or noise; `E7B53B` is
/// recognisable and `4B67B53B` is the same attribute read the wrong way.
///
/// <code>
///   paint tf2-2026-pub-pov-clean 14000
///   paint z1800 30000
/// </code>
/// </remarks>
public sealed class PaintProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "paint";

    /// <inheritdoc/>
    public string Summary => "which items are painted, and with what: paint <demo> <tick>";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count < 2)
        {
            output.WriteLine("paint <demo> <tick>");
            return;
        }

        if (DemoCorpus.Find(arguments[0], output) is not { } path)
        {
            output.WriteLine($"No demo named '{arguments[0]}'.");
            return;
        }

        int tick = int.Parse(arguments[1], CultureInfo.InvariantCulture);

        string? folder = new MapLocator(
            MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder).FindGameFolder();

        // Said out loud: the paint attribute is looked up by NAME in items_game.txt, so with no
        // install every item reads as unpainted and the report would blame the demo.
        output.WriteLine(
            $"{Path.GetFileName(path)} tick {tick.ToString(CultureInfo.InvariantCulture)}, "
            + $"game {folder ?? "NOT FOUND — every item will read as unpainted"}");

        GameContent game = GameContent.Open(folder, NullLoggerFactory.Instance);
        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));

        List<SceneProp> props = [];
        timeline.PropsAt(tick, props);

        int econ = 0;
        int painted = 0;

        foreach (SceneProp prop in props)
        {
            if (prop.ItemDefinitionIndex is null && prop.Econ is null)
            {
                continue;
            }

            econ++;

            // **Both teams asked, because a two-tone paint differs between them** and reporting one
            // would hide half of what the attribute says. The engine picks by the entity's team;
            // this prints the pair so the difference is visible where there is one.
            (float Red, float Green, float Blue)? red =
                game.Weapons.PaintFor(prop, SceneTeams.Red);

            (float Red, float Green, float Blue)? blu =
                game.Weapons.PaintFor(prop, SceneTeams.Blu);

            if (red is not { } primary)
            {
                continue;
            }

            painted++;

            string alternate = blu is { } second && second != primary
                ? $" / BLU {Hex(second)}"
                : string.Empty;

            output.WriteLine(
                $"  entity {prop.EntityIndex.ToString(CultureInfo.InvariantCulture),5}  "
                + $"item {prop.ItemDefinitionIndex?.ToString(CultureInfo.InvariantCulture) ?? "-",6}  "
                + $"{Hex(primary)}{alternate}  {prop.ModelPath}");
        }

        output.WriteLine();

        // **Three numbers, not one.** "12 painted" is uninterpretable without how many items could
        // have been, and how many props there were at all — the denominator is what says whether a
        // small count is a rare feature or a broken lookup
        // (`docs/memory/the-denominator-decides-what-can-be-lost.md`).
        output.WriteLine(
            $"{painted} painted of {econ} econ items, {props.Count} props at this tick");
    }

    /// <summary>A tint printed the way the attribute packs it, so it can be recognised.</summary>
    private static string Hex((float Red, float Green, float Blue) tint) =>
        $"#{(int)Math.Round(tint.Red * 255f):X2}{(int)Math.Round(tint.Green * 255f):X2}"
        + $"{(int)Math.Round(tint.Blue * 255f):X2}";
}
