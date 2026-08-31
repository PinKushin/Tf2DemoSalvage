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
/// Every weapon a player carries, and whether anything names a model for it.
/// </summary>
/// <remarks>
/// **The question the owner keeps asking in different words:** *"there is one medigun still not
/// drawing, it has to be something like the kritz or quick"*, then *"you never did fix the
/// medigun"*. The medigun itself draws now — a weapon's model comes from its ITEM
/// (<c>CEconEntity::SetModel</c> → <c>GetPlayerDisplayModel</c>, <c>econ_entity.cpp:1167</c>) and
/// that is implemented — so what is left is whichever weapons the item lookup ALSO fails to name.
///
/// **It measures the production path, not a description of it.** <see cref="WeaponPropModels"/> is
/// the type the viewer runs, handed <see cref="WeaponModels.For(int?, string?, int?)"/> from a real
/// install. A probe that reimplemented "the item wins, the wire is the fallback" would agree with
/// whoever wrote the probe rather than with the viewer.
///
/// **A weapon with no model after this is a weapon that does not draw.** That is the whole report:
/// the distinct (class, item, owner class) it saw, and which of them ended with an empty path.
/// </remarks>
public sealed class WeaponModelProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "weapon-models";

    /// <inheritdoc/>
    public string Summary =>
        "weapons that resolve to no model: weapon-models [demo] [tick stride]";

    private const string DefaultDemo = "tf2-2026-pub-pov-clean";

    private const int DefaultStride = 100;

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        string fragment = arguments.Count > 0 ? arguments[0] : DefaultDemo;
        int stride = arguments.Count > 1
            ? int.Parse(arguments[1], CultureInfo.InvariantCulture)
            : DefaultStride;

        string? path = DemoCorpus.Find(fragment, output);
        if (path is null)
        {
            output.WriteLine($"No demo named '{fragment}'.");
            return;
        }

        string? folder = new MapLocator(
            MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder).FindGameFolder();

        output.WriteLine($"{Path.GetFileName(path)} stride {stride}");
        output.WriteLine($"game folder: {folder ?? "NOT FOUND — every lookup will answer null"}");

        GameContent game = GameContent.Open(folder, NullLoggerFactory.Instance);
        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));
        WeaponPropModels resolver = new();

        // Keyed on what the ANSWER depends on, which is Valve's own invalidation tuple: the item
        // and the owner's class. Reporting per tick would print the same weapon a thousand times.
        Dictionary<string, string> seen = new(StringComparer.Ordinal);

        List<ScenePlayer> players = [];
        List<SceneProp> props = [];

        for (int tick = timeline.FirstTick; tick <= timeline.LastTick; tick += stride)
        {
            players.Clear();
            timeline.PlayersAt(tick, players);

            props.Clear();
            timeline.PropsAt(tick, props);

            // Before, so the wire's own answer is visible beside the resolved one — the two
            // disagreeing is the interesting case and a report of only the outcome hides it.
            Dictionary<int, string> before = props
                .Where(prop => prop.ItemDefinitionIndex is not null)
                .GroupBy(prop => prop.EntityIndex)
                .ToDictionary(group => group.Key, group => group.First().ModelPath);

            resolver.Resolve(props, players, game.Weapons.For);

            foreach (SceneProp prop in props.Where(prop => prop.ItemDefinitionIndex is not null))
            {
                int? owner = prop.AttachedTo ?? prop.OwnedBy;
                // `ScenePlayer` is a struct, so `FirstOrDefault(...)?.PlayerClass` does not
                // compile — and would be wrong if it did, since a default struct is not "no
                // player". Selecting the nullable field says absent when there is none.
                int? ownerClass = players
                    .Where(player => player.EntityIndex == owner)
                    .Select(player => player.PlayerClass)
                    .FirstOrDefault();

                string key = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{prop.ClassName} item {prop.ItemDefinitionIndex} class {ownerClass}");

                string wire = before.TryGetValue(prop.EntityIndex, out string? was) ? was : "";

                seen[key] =
                    $"{(prop.ModelPath.Length == 0 ? "NO MODEL" : "ok      ")} {key} "
                    + $"wire '{wire}' resolved '{prop.ModelPath}'";
            }
        }

        foreach (string line in seen.Values.Order(StringComparer.Ordinal))
        {
            output.WriteLine(line);
        }

        int missing = seen.Values.Count(line => line.StartsWith("NO MODEL", StringComparison.Ordinal));

        output.WriteLine(
            $"WEAPONS {seen.Count.ToString(CultureInfo.InvariantCulture)} distinct, "
            + $"{missing.ToString(CultureInfo.InvariantCulture)} with no model");
    }
}
