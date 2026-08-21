using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

// Namespaced away from `Tf2DemoSalvage.Corpus.Tests.*`, where `Corpus` binds to the namespace.
namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// What the off-hand lookup actually offers, on a real match.
/// </summary>
/// <remarks>
/// **The unit tests prove the rules; only this proves the rules ever fire.** A synthetic fixture
/// says an off hand flagged <c>EF_NODRAW</c> is not offered and one carrying a model is — written by
/// the same person who wrote the reader, from the same reading of the SDK. It cannot say whether a
/// real demo contains either case, and a feature that is correct on inputs the corpus never holds is
/// a feature nobody has run.
///
/// That gap has shipped three no-ops in this project with a green suite
/// (<c>docs/memory/output-level-assertion-or-it-is-not-done.md</c>), so the claim here is about
/// counts on real bytes rather than about behaviour on chosen ones.
///
/// **What z1800 holds, measured before this suite was written**, over its first 40,000 entity
/// snapshots: every player carries a slot-1 viewmodel for their whole life, 22 of them sending model
/// index 0 — no model — and 23 samples across 7 owners carrying a real one. So both branches of the
/// rule occur, and the common case by far is the empty one.
///
/// **And what this suite then measured: 190 of 9,165 player-ticks, three distinct models, every one
/// a spy watch** — <c>v_watch_spy</c>, <c>v_watch_leather_spy</c> and <c>v_watch_pocket_spy</c>, the
/// stock Invis Watch, the Enthusiast's Timepiece and the Quäckenbirdt. That is the whole of slot 1
/// on a nine-versus-nine match, which is the corpus confirming what the SDK says: only
/// <c>CTFWeaponInvis</c> claims the off hand.
///
/// **It also settles what to draw there.** Every path is a complete watch model, so the off hand
/// needs no client-built weapon merged onto it the way a modern main-hand viewmodel does — those
/// are arms. Valve's own comment gives the reason ("Watch uses the player model as its viewmodel,
/// because it's never seen being carried by the player"); the corpus gives the fact.
/// </remarks>
public sealed class CorpusOffHandTests
{
    /// <summary>How many ticks to sample across the demo.</summary>
    /// <remarks>
    /// Sampled rather than walked, because asking every player on every tick of a 24-minute match
    /// is a quadratic walk of the viewmodel list for no extra information — a watch is deployed for
    /// seconds at a time, not for single ticks.
    /// </remarks>
    private const int Samples = 400;

    [Test]
    public void OffHandViewmodelAt_AcrossARealMatch_OffersOnlyModelsThatAreOnScreen()
    {
        DemoTimeline timeline = TimelineCache.For(Corpus.Demo("z1800"));

        HashSet<string> paths = [];
        int offered = 0;
        int asked = 0;

        int first = timeline.FirstTick;
        int step = ((timeline.LastTick - first) / Samples) + 1;

        for (int tick = first; tick <= timeline.LastTick; tick += step)
        {
            foreach (ScenePlayer player in timeline.PlayersAt(tick))
            {
                asked++;

                if (timeline.OffHandViewmodelAt(tick, player.EntityIndex) is not { } offHand)
                {
                    continue;
                }

                offered++;
                paths.Add(offHand.ModelPath);

                // **The rule, asserted on real data rather than on a fixture.** An offered off hand
                // must be one the engine would draw: a model that exists, and no EF_NODRAW.
                offHand.ModelPath.ShouldNotBeEmpty(
                    $"entity {player.EntityIndex} was offered an off hand with no model at {tick}");

                offHand.Drawn.ShouldBeTrue(
                    $"entity {player.EntityIndex} was offered a hidden off hand at {tick}");
            }
        }

        // Written out before the assertions that could hide them.
        TestContext.Out.WriteLine(
            $"z1800: {asked} player-ticks sampled, {offered} offered an off hand, " +
            $"{paths.Count} distinct models");

        foreach (string path in paths.Order())
        {
            TestContext.Out.WriteLine($"  {path}");
        }

        // **The positive control, and it is the assertion this suite exists for.** Everything above
        // is satisfied by a lookup that never answers — which is exactly what the off hand did
        // before EF_NODRAW was read from the right table, and exactly what it would do again if a
        // future change filtered too hard. Five absence claims in this project have been facts about
        // the search rather than about the data.
        asked.ShouldBeGreaterThan(0, "no players were sampled, so nothing was measured");

        offered.ShouldBeGreaterThan(
            0,
            "no player in a Highlander match was ever offered an off hand, which the raw " +
            "snapshots contradict: 23 slot-1 viewmodels carry a model");
    }
}
