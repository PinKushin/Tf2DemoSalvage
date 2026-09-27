using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>Every Engineer building, each time it changes.</summary>
/// <remarks>
/// The control: on every tick a building exists, its builder must resolve to a player entity that
/// tick's frame actually lists. A builder that never resolves means the read is broken, not the demo.
/// </remarks>
public sealed class BuildingProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "buildings";

    /// <inheritdoc/>
    public string Summary => "every Engineer building as it changes, with the builder resolved: buildings <demo>";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0 || DemoCorpus.Find(arguments[0], output) is not { } path)
        {
            output.WriteLine("buildings <demo>");
            return;
        }

        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));
        Dictionary<int, string> last = [];
        int withBuilder = 0;
        int resolved = 0;

        for (int tick = timeline.FirstTick; tick <= timeline.LastTick; tick++)
        {
            IReadOnlyList<SceneBuilding> buildings = timeline.BuildingsAt(tick);
            IReadOnlyList<ScenePlayer> players = timeline.PlayersAt(tick);

            foreach (SceneBuilding building in buildings)
            {
                if (building.BuilderEntityIndex is { } builder)
                {
                    withBuilder++;
                    resolved += players.Any(player => player.EntityIndex == builder) ? 1 : 0;
                }

                string state = string.Create(
                    CultureInfo.InvariantCulture,
                    $"type {building.ObjectType} mode {building.ObjectMode} team {building.Team} builder {building.BuilderEntityIndex} " +
                    $"health {building.Health}/{building.MaxHealth} level {building.UpgradeLevel} metal {building.UpgradeMetal}/{building.UpgradeMetalRequired} " +
                    $"building {building.Building} placing {building.Placing} carried {building.Carried} mini {building.MiniBuilding} " +
                    $"sapped {building.Sapped} disabled {building.Disabled} pct {building.PercentageConstructed:0.##} " +
                    $"shells {building.SentryAmmoShells} rockets {building.SentryAmmoRockets} metalammo {building.DispenserAmmoMetal} teleport {building.TeleporterState}");

                if (last.TryGetValue(building.EntityIndex, out string? previous) && previous == state)
                {
                    continue;
                }

                last[building.EntityIndex] = state;
                output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"tick {tick,7} building {building.EntityIndex}: {state}"));
            }
        }

        output.WriteLine($"control: the builder resolved to a listed player on {resolved} of {withBuilder} ticks with a builder");
    }
}
