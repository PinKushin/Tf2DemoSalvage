using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>Which tick to open the viewer at to see a watch.</summary>
/// <remarks>
/// **A diagnostic, not a check.** The corpus test next door asserts that the off hand is offered and
/// that everything offered is drawable; this one answers the different question of WHERE, so a
/// person can point the viewer at it and look. Nothing about the picture can be asserted from here —
/// that is the whole reason this exists.
/// </remarks>
public sealed class OffHandProbe
{
    /// <summary>One player's stretch of carrying a drawable off hand.</summary>
    private readonly record struct OffHandRun(int First, int Last, int Ticks, string Model);

    [Test]
    [Explicit("diagnostic")]
    public void OffHand_WhereItIsDrawn_IsReported()
    {
        DemoTimeline timeline = TimelineCache.For(Corpus.Demo("z1800"));

        Dictionary<int, OffHandRun> runs = [];

        for (int tick = timeline.FirstTick; tick <= timeline.LastTick; tick += 33)
        {
            foreach (ScenePlayer player in timeline.PlayersAt(tick))
            {
                if (timeline.OffHandViewmodelAt(tick, player.EntityIndex) is not { } offHand)
                {
                    continue;
                }

                runs[player.EntityIndex] =
                    runs.TryGetValue(player.EntityIndex, out OffHandRun seen)
                        ? seen with { Last = tick, Ticks = seen.Ticks + 1 }
                        : new OffHandRun(tick, tick, 1, offHand.ModelPath);
            }
        }

        foreach ((int entity, OffHandRun seen) in runs.OrderByDescending(pair => pair.Value.Ticks))
        {
            TestContext.Out.WriteLine(
                $"entity {entity}: {seen.Ticks} samples, ticks {seen.First}..{seen.Last}, {seen.Model}");
        }

        // **The longest unbroken run, which is the tick to actually open.** The survey above steps
        // 33 ticks, so its range is bracketed by gaps — a spy dies, respawns, switches class. A tick
        // picked from the middle of that range lands on nothing about three times in ten, and the
        // resulting empty screen reads as a defect in the drawing rather than in the choice of tick.
        if (runs.OrderByDescending(pair => pair.Value.Ticks).FirstOrDefault() is { Value.Ticks: > 0 } best)
        {
            (int from, int length) = LongestRun(timeline, best.Key, best.Value);

            TestContext.Out.WriteLine(
                $"OPEN AT: --spectate {best.Key} --tick {from + (length / 2)} " +
                $"(unbroken {from}..{from + length}, {best.Value.Model})");
        }

        // Which of them the viewer would show on its own, since it spectates the lowest entity index
        // on a playing team. A watch on any other player is on screen for nobody without
        // `--spectate`.
        TestContext.Out.WriteLine(string.Empty);

        foreach ((int entity, OffHandRun seen) in runs)
        {
            for (int tick = seen.First; tick <= seen.Last; tick += 33)
            {
                IReadOnlyList<ScenePlayer> at = timeline.PlayersAt(tick);

                if (SpectatorChoice(at) == entity &&
                    timeline.OffHandViewmodelAt(tick, entity) is not null)
                {
                    TestContext.Out.WriteLine($"VISIBLE: tick {tick}, entity {entity}, {seen.Model}");
                    break;
                }
            }
        }
    }

    /// <summary>The longest stretch of consecutive ticks where that player's off hand is drawn.</summary>
    /// <returns>Where it starts, and how many ticks it lasts.</returns>
    private static (int From, int Length) LongestRun(DemoTimeline timeline, int entity, OffHandRun seen)
    {
        int bestFrom = seen.First;
        int bestLength = 0;
        int from = seen.First;
        int length = 0;

        for (int tick = seen.First; tick <= seen.Last; tick++)
        {
            if (timeline.OffHandViewmodelAt(tick, entity) is not null)
            {
                if (length++ == 0)
                {
                    from = tick;
                }

                if (length > bestLength)
                {
                    (bestFrom, bestLength) = (from, length);
                }
            }
            else
            {
                length = 0;
            }
        }

        return (bestFrom, bestLength);
    }

    /// <summary>The viewer's own rule, restated: lowest entity index on a playing team.</summary>
    /// <remarks>
    /// Duplicated rather than referenced because <c>SpectatorTarget</c> lives in the viewer assembly
    /// and this project does not reference it. A copy in a diagnostic is acceptable where a copy in
    /// a check would not be — if the rule changes, this prints the wrong tick and a person notices
    /// immediately, because the watch is not there.
    /// </remarks>
    private static int? SpectatorChoice(IReadOnlyList<ScenePlayer> players)
    {
        ScenePlayer[] playing = [.. players.Where(player => player.Team is >= 2 and <= 3)];

        return playing.Length == 0 ? null : playing.MinBy(player => player.EntityIndex).EntityIndex;
    }
}
