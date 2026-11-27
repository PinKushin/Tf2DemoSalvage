using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>Each player's `GetGroundEntity()` as the timeline resolves it, tick by tick (B768).</summary>
/// <remarks>
/// **The question B768 turns on.** `m_hGroundEntity` is in `DT_LocalPlayerExclusive` (player.cpp:8117-8138), sent to the
/// player himself, and `C_TFPlayer::BuildTransformations` dips a crouching player whose `GetGroundEntity()` is NULL
/// (c_tf_player.cpp:8764). The client holds what it received — the instance baseline, then any delta that carried the
/// field — resolved through the handle's serial. This reads `ScenePlayer.GroundEntity`, the value the duck-jump and the
/// dispenser gate USE, per (index, serial): ticks holding a ground entity, ticks holding none, and the distinct values.
///
/// <code>
///   ground-entity tf2-live-pov-cp_process_final-bots-2026-11-05
/// </code>
/// </remarks>
public sealed class GroundEntityProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "ground-entity";

    /// <inheritdoc/>
    public string Summary =>
        "per player, the resolved ground entity the timeline holds each tick, recorder marked: ground-entity <demo>";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0)
        {
            output.WriteLine("ground-entity <demo>");
            return;
        }

        if (DemoCorpus.Find(arguments[0], output) is not { } path)
        {
            output.WriteLine($"No demo named '{arguments[0]}'.");
            return;
        }

        DemoTimeline timeline = ProbeInstall.Timeline(File.ReadAllBytes(path), output);
        Dictionary<(int Index, int Serial), (int Held, int None, HashSet<int> Values)> players = [];

        for (int tick = timeline.FirstTick; tick <= timeline.LastTick; tick++)
        {
            foreach (ScenePlayer player in timeline.PlayersAt(tick))
            {
                (int, int) key = (player.EntityIndex, player.SerialNumber);
                (int Held, int None, HashSet<int> Values) seen = players.TryGetValue(key, out (int, int, HashSet<int>) was) ? was : (0, 0, []);

                if (player.GroundEntity is { } ground)
                {
                    seen.Held++;
                    seen.Values.Add(ground);
                }
                else
                {
                    seen.None++;
                }

                players[key] = seen;
            }
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{Path.GetFileName(path)}: recorder entity {timeline.RecorderEntityIndex?.ToString(CultureInfo.InvariantCulture) ?? "none"}"));

        foreach (((int index, int serial), (int held, int none, HashSet<int> values)) in players.OrderBy(pair => pair.Key))
        {
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  player {index,3}/{serial,-4}{(timeline.RecorderEntityIndex == index ? " RECORDER" : "         ")}  " +
                $"ground held {held,6} ticks, none {none,6}; values {string.Join(' ', values.Order().Take(8))}"));
        }
    }
}
