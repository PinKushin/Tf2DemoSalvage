using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// Server impacts on players whose blood would stay: how long each victim goes before `C_TFPlayer` wipes his decals.
/// </summary>
/// <remarks>
/// **Written to choose a moment to look at in real TF2.** `C_TFPlayer::OnDataChanged` removes every decal when health
/// rises to full (`c_tf_player.cpp:4474`), and in a match a medic does that within ticks, so most hits leave nothing to
/// compare. The wipe condition is the same one `MainForm.ClearOnChange` applies, read from the same `PlayersAt`.
/// </remarks>
public sealed class LastingBloodProbe : IProbe
{
    /// <summary>How far past a hit to look, in ticks: 10 s at 66 ticks per second.</summary>
    private const int Horizon = 660;

    /// <inheritdoc/>
    public string Name => "lasting-blood";

    /// <inheritdoc/>
    public string Summary => "impacts on players, longest-lasting blood first (ticks until a full heal, death or uber wipes it): lasting-blood <demo> [n]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count < 1 || DemoCorpus.Find(arguments[0], output) is not { } path)
        {
            output.WriteLine("Usage: lasting-blood <demo> [n]");
            return;
        }

        int shown = arguments.Count > 1 ? int.Parse(arguments[1], CultureInfo.InvariantCulture) : 10;
        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));
        IReadOnlyList<(int Index, SceneEffectDispatch Dispatch)> impacts =
            ServerImpacts.OnEntities(timeline.Dispatches.All, timeline.Dispatches.Names.Name);

        List<(SceneEffectDispatch Hit, int Lasts, string Ended)> lasting = [];

        foreach ((int _, SceneEffectDispatch hit) in impacts)
        {
            if (Player(timeline.PlayersAt(hit.Tick), hit.Entity) is not { IsAlive: true } victim)
            {
                continue;
            }

            (int lasts, string ended) = Survives(timeline, hit, victim.Health);
            lasting.Add((hit, lasts, ended));
        }

        output.WriteLine($"{impacts.Count} impacts on entities, {lasting.Count} on living players");

        foreach ((SceneEffectDispatch hit, int lasts, string ended) in lasting.OrderByDescending(one => one.Lasts).Take(shown))
        {
            string who = timeline.Roster.Values.FirstOrDefault(player => player.EntityIndex == hit.Entity).Name ?? "?";
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"tick {hit.Tick} {who} (entity {hit.Entity}) hitbox {hit.HitBox}: blood lasts {lasts} ticks, then {ended}"));
        }
    }

    /// <summary>Ticks until the first wipe after the hit, and what caused it.</summary>
    private static (int Ticks, string Ended) Survives(DemoTimeline timeline, SceneEffectDispatch hit, int? health)
    {
        int? was = health;

        for (int tick = hit.Tick + 1; tick <= hit.Tick + Horizon; tick++)
        {
            if (Player(timeline.PlayersAt(tick), hit.Entity) is not { IsAlive: true } now)
            {
                return (tick - hit.Tick, "death");
            }

            if (now.Health > was && now.Health >= now.MaxHealth)
            {
                return (tick - hit.Tick, "full heal");
            }

            if (now.Conditions.IsInvulnerable)
            {
                return (tick - hit.Tick, "uber");
            }

            was = now.Health;
        }

        return (Horizon, $"still there after {Horizon}");
    }

    private static ScenePlayer? Player(IReadOnlyList<ScenePlayer> players, int entity) =>
        players.FirstOrDefault(player => player.EntityIndex == entity);
}
