using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests;

/// <summary>
/// What <c>m_lifeState</c> reads across a demo, per player.
/// </summary>
/// <remarks>
/// **Written because the owner says the reading is wrong about him** (B222). The 2013 badlands
/// specimen is his own point-of-view recording — he is the soldier, and spire is on badlands — and
/// entity 1 reported <c>lifeState 2</c> (dead) at the moment the UI suite samples, while he
/// remembers being alive and jumping.
///
/// **A dead reading is not obviously a defect and that is why this measures rather than asserts.**
/// A recording genuinely can open with its recorder dead, and this project has already generalised
/// once from a single death event and been wrong. What distinguishes the two is the SHAPE over the
/// whole demo: a real death is a span bounded by transitions, and a player who is dead for every
/// tick of a match is a decode fault.
///
/// It reports the transitions and the proportion, not a sample, because a sample of a state that
/// changes is how the last four instruments went blind.
///
/// Explicit: it reports rather than asserts.
/// </remarks>
[Explicit("Diagnostic: what m_lifeState reads across a demo.")]
public sealed class LifeStateCorpusDiagnostic
{
    /// <summary>Candidate opening ticks for the UI suite: alive, armed, and out on the map.</summary>
    /// <remarks>
    /// **`ViewerSession.OpeningTick` is 2500 and the recorder is DEAD there** — the dead span runs
    /// 2012 to 3208. That is why the first-person suite was anchored on a moment where this viewer
    /// and TF2 disagree, and it has to move before the POV-death divergence is closed or the test
    /// breaks for the right reason at the wrong time.
    ///
    /// **The tick cannot simply be changed, because 2500 was chosen against criteria.** Its comment
    /// records them: the recorder out on the map rather than parked at a spawn gate, above the
    /// ground, sky and buildings in frame, and a viewmodel actually drawing — a frame with many
    /// times the colour variety of the alternative. A replacement has to meet the same bar AND be
    /// alive, so this reports all of it per candidate rather than picking on liveness alone.
    /// </remarks>
    [TestCase("tf2-2013-build1729296-pov-cp_badlands")]
    public void ReportCandidateTicks(string demo)
    {
        DemoTimeline timeline = TimelineCache.For(Corpus.Demo(demo));

        TestContext.Out.WriteLine($"{demo}: ticks {timeline.FirstTick}..{timeline.LastTick}");

        // Every hundred ticks across the whole demo, so the choice is made from the field rather
        // than from the first candidate that happens to be alive.
        for (int tick = timeline.FirstTick; tick <= timeline.LastTick; tick += 100)
        {
            ScenePlayer? recorder = timeline.PlayersAt(tick)
                .Select(each => (ScenePlayer?)each)
                .FirstOrDefault(each => each!.Value.EntityIndex == 1);

            if (recorder is not { } who)
            {
                continue;
            }

            SceneViewmodel? held = timeline.ViewmodelAt(tick, who.EntityIndex);

            TestContext.Out.WriteLine(
                $"  tick {tick,5}: life {who.LifeState?.ToString(CultureInfo.InvariantCulture) ?? "unsent",6} " +
                $"at ({who.X,8:0}, {who.Y,8:0}, {who.Z,6:0}) " +
                // **`Drawn` is `EF_NODRAW` on the viewmodel's own table, and it decides whether the
                // POV divergence exists at all.** TF2's non-HLTV path reaches
                // `BaseClass::ShouldDraw()`, which consults exactly this — so if a recording clears
                // it while the recorder is dead, honouring it is the whole implementation and there
                // is nothing to write.
                $"drawn {(held?.Drawn.ToString() ?? "-"),5} " +
                $"viewmodel {(held is { } vm && vm.ModelPath.Length > 0 ? vm.ModelPath : "(none)")}");
        }
    }

    [TestCase("tf2-2013-build1729296-pov-cp_badlands")]
    [TestCase("cp_process_f12")]
    public void ReportLifeState(string demo)
    {
        DemoTimeline timeline = TimelineCache.For(Corpus.Demo(demo));

        Dictionary<int, List<(int Tick, int? Life)>> transitions = [];

        // Keyed by −1 for "the recording never sent it", which is a distinct answer from any life
        // state and has to stay distinguishable — conflating unsent with a value is the mistake
        // `docs/memory/sentinels-conflate-unknown-with-answer.md` is about, and here the counting
        // is the only place it appears.
        Dictionary<int, Dictionary<int, int>> seen = [];

        int sampled = 0;

        for (int tick = timeline.FirstTick; tick <= timeline.LastTick; tick += 4)
        {
            foreach (ScenePlayer player in timeline.PlayersAt(tick))
            {
                sampled++;

                if (!seen.TryGetValue(player.EntityIndex, out Dictionary<int, int>? counts))
                {
                    counts = [];
                    seen[player.EntityIndex] = counts;
                    transitions[player.EntityIndex] = [];
                }

                int key = player.LifeState ?? -1;

                counts[key] = counts.GetValueOrDefault(key) + 1;

                List<(int Tick, int? Life)> changes = transitions[player.EntityIndex];

                // **Transitions, not samples.** A span is what distinguishes a real death from a
                // constant, and only a change carries that.
                if (changes.Count == 0 || changes[^1].Life != player.LifeState)
                {
                    changes.Add((tick, player.LifeState));
                }
            }
        }

        TestContext.Out.WriteLine(
            $"{demo}: ticks {timeline.FirstTick}..{timeline.LastTick}, {sampled} player samples");

        foreach (int entity in seen.Keys.OrderBy(each => each))
        {
            string spread = string.Join(
                ", ",
                seen[entity].OrderBy(each => each.Key)
                    .Select(each =>
                        $"{(each.Key < 0 ? "unsent" : each.Key.ToString(CultureInfo.InvariantCulture))}" +
                        $"x{each.Value}"));

            TestContext.Out.WriteLine(
                $"  entity {entity,3}: {spread} — {transitions[entity].Count} transitions, " +
                $"first {string.Join(" -> ", transitions[entity].Take(6).Select(each => $"{each.Life?.ToString(CultureInfo.InvariantCulture) ?? "unsent"}@{each.Tick}"))}");
        }
    }
}
