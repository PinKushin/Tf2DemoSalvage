using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>`GetFOV`'s inputs per player — `m_iFOV`, `m_iFOVStart`, `m_flFOVTime`, `m_flFOVRate`, `m_iDefaultFOV` — each time one changes.</summary>
/// <remarks>
/// The control is `m_iDefaultFOV`: the server sets it for every player at connect, clamped to 75..90
/// (`CTFGameRules::ClientSettingsChanged`), so a player without it means the read is broken, not the demo.
/// </remarks>
public sealed class FovProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "fov";

    /// <inheritdoc/>
    public string Summary => "each player's field-of-view fields (zoom, default, lerp) as they change, with the view's GetFOV: fov <demo> [player]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0 || DemoCorpus.Find(arguments[0], output) is not { } path)
        {
            output.WriteLine("fov <demo> [player]");
            return;
        }

        int? only = arguments.Count > 1 ? int.Parse(arguments[1], CultureInfo.InvariantCulture) : null;
        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));
        Dictionary<int, (int?, int?, float?, float?, int?)> last = [];
        HashSet<int> seen = [];
        HashSet<int> withDefault = [];

        output.WriteLine($"recorder {timeline.RecorderEntityIndex?.ToString(CultureInfo.InvariantCulture) ?? "none"}");

        for (int tick = timeline.FirstTick; tick <= timeline.LastTick; tick++)
        {
            IReadOnlyList<ScenePlayer> players = timeline.PlayersAt(tick);

            foreach (ScenePlayer player in players)
            {
                seen.Add(player.EntityIndex);

                if (player.DefaultFov is not null)
                {
                    withDefault.Add(player.EntityIndex);
                }

                (int?, int?, float?, float?, int?) now = (player.Fov, player.FovStart, player.FovTime, player.FovRate, player.DefaultFov);

                if ((only is { } wanted && player.EntityIndex != wanted) || (last.TryGetValue(player.EntityIndex, out (int?, int?, float?, float?, int?) was) && was == now))
                {
                    continue;
                }

                last[player.EntityIndex] = now;
                bool isLocal = player.EntityIndex == timeline.RecorderEntityIndex;
                float curTime = (timeline.ServerTickAt(tick) ?? tick) * timeline.IntervalPerTick;
                float fov = PlayerFov.Get(player, index => Find(players, index), isLocal, 0f, curTime);

                output.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"  tick {tick,7} curtime {curTime:0.###} entity {player.EntityIndex,3}: m_iFOV {player.Fov} start {player.FovStart} time {player.FovTime} rate {player.FovRate} default {player.DefaultFov} -> GetFOV {fov}"));
            }
        }

        output.WriteLine($"control: {withDefault.Count} of {seen.Count} players carried m_iDefaultFOV");
    }

    private static ScenePlayer? Find(IReadOnlyList<ScenePlayer> players, int index)
    {
        foreach (ScenePlayer player in players)
        {
            if (player.EntityIndex == index)
            {
                return player;
            }
        }

        return null;
    }
}
