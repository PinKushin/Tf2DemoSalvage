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

    [Test]
    [Explicit("diagnostic")]
    public void MainHandViewmodel_OnTheUiSuitesDemo_IsReported()
    {
        // **Which tick the UI capture should open at.** That test jumps to the END of this demo, for
        // a reason about the installed game rather than the picture: at tick 0 the recording names
        // `v_scattergun_scout.mdl`, which TF2 no longer ships, so nothing draws. Nobody chose the
        // framing, and at the end of a solo recording the player is parked facing a wall — so the
        // capture is of planks, with no viewmodel in it.
        //
        // This reports where the recorder both HAS a viewmodel and is alive, so a tick can be picked
        // on evidence instead of on the demo's last frame.
        DemoTimeline timeline = TimelineCache.For(Corpus.Demo("tf2-2013-build1729296-pov-cp_badlands"));

        // Nullable, because a SourceTV recording has no local player at all. This demo is a POV
        // recording so it has one, and saying so out loud is cheaper than a cast that would throw.
        if (timeline.RecorderEntityIndex is not { } recorder)
        {
            Assert.Ignore("this demo names no recorder, so it is not the point-of-view case");
            return;
        }

        Dictionary<string, (int First, int Last, int Samples)> byModel = [];

        for (int tick = timeline.FirstTick; tick <= timeline.LastTick; tick += 33)
        {
            if (timeline.ViewmodelAt(tick, recorder) is not { } weapon)
            {
                continue;
            }

            byModel[weapon.ModelPath] =
                byModel.TryGetValue(weapon.ModelPath, out (int First, int Last, int Samples) seen)
                    ? (seen.First, tick, seen.Samples + 1)
                    : (tick, tick, 1);
        }

        foreach ((string model, (int first, int last, int samples)) in
            byModel.OrderByDescending(pair => pair.Value.Samples))
        {
            TestContext.Out.WriteLine(
                $"  {model}: {samples} samples, ticks {first}..{last}");
        }

        TestContext.Out.WriteLine($"  recorder is entity {recorder}, ticks {timeline.FirstTick}..{timeline.LastTick}");

        // **Where the recorder actually IS, because a viewmodel is not enough.** Tick 3400 has a
        // drawn pyro viewmodel and is still a bad capture: the player is pressed against a spawn
        // gate, and brush entities are drawn at their COMPILED position (B71), so that gate is shut
        // for the whole demo whatever the demo says. A frame worth looking at needs the player out
        // in the map as well as holding something.
        TestContext.Out.WriteLine(string.Empty);

        foreach (int tick in new[] { 300, 600, 900, 1200, 1500, 2000, 2500, 3000, 5000, 6000, 7000 })
        {
            if (timeline.RecordedViewAt(tick) is not { } view)
            {
                continue;
            }

            string model = timeline.ViewmodelAt(tick, recorder) is { } held
                ? System.IO.Path.GetFileNameWithoutExtension(held.ModelPath)
                : "none";

            TestContext.Out.WriteLine(
                $"  tick {tick}: at ({view.Origin.X:0},{view.Origin.Y:0},{view.Origin.Z:0}) " +
                $"facing yaw {view.Angles.Yaw:0} holding {model}");
        }

        byModel.ShouldNotBeEmpty("the recorder never carries a viewmodel, so no tick can be chosen");
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
