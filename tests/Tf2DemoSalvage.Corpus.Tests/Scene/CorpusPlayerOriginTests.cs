using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene;

// Namespaced away from `Tf2DemoSalvage.Corpus.Tests.*`, where `Corpus` binds to the namespace.
namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>Does any real demo put a player at exactly the world origin?</summary>
/// <remarks>
/// **This measures a rule rather than asserting one** (D98,
/// `docs/findings/38-which-players-can-be-shown.md`). Valve's base `CMapOverview::CanPlayerBeSeen`
/// refuses a player at `Vector(0,0,0)` — its own comment is `// Invalid guy` — and **TF2's override
/// drops that check entirely**. We implemented it, then justified it with a citation to the wrong
/// class.
///
/// The owner asked the question the citation could not answer:
///
/// > *"why are we doing an orgin check if valve doesnt?"*
///
/// and then: *"yes measure that because the reasoning is sound"*.
///
/// **So this is an experiment, not a guard.** If a player at the origin never occurs in any demo we
/// have, the rule protects against nothing and TF2's version is right for us too. If one does, the
/// rule earns its place on evidence and the flat-marker reimplementation must carry it.
///
/// **It asserts nothing about the count either way** — it reports. An assertion here would be a
/// claim about the corpus, and the corpus grows.
/// </remarks>
public sealed class CorpusPlayerOriginTests
{
    /// <summary>How many ticks to sample per demo.</summary>
    /// <remarks>
    /// Spread across the file rather than clustered at the start: the state most likely to produce
    /// an origin player — a slot that exists before it has spawned — is a beginning-of-round thing,
    /// and a demo has more than one round.
    /// </remarks>
    private const int Samples = 40;

    /// <summary>The demos this walks: one or two per era, real matches only.</summary>
    /// <remarks>
    /// **Not the whole corpus, and not the era specimens.** The owner, when the first draft walked
    /// everything:
    ///
    /// > *"dont use the full corpus, thats a huge amount, like over a gig easy, pick some random
    /// > ones from each year available, that dont include my era specimins, because those really
    /// > are not goign to help find rendering issues since they are brand new installs, no items,
    /// > and no real MP."*
    ///
    /// **The era specimens cannot answer this question even in principle.** They are solo
    /// recordings on period clients — one player, no worn items, no other team — so a rule about
    /// players sitting at the origin has almost nothing to act on. Walking them would inflate the
    /// denominator and measure nothing.
    ///
    /// **Both points of view are here on purpose.** A POV demo has a recorder and a SourceTV one
    /// does not, and the recorder is exactly the entity the "don't draw ourself" rule is about.
    /// </remarks>
    private static readonly string[] Sampled =
    [
        "auto-20101109-2141-cp_badlands",               // 2010, earliest real match held
        "20120909_1804_cp_gullywash_final1_red_fags",   // 2012
        "20130518_0313_cp_granary_blu_blu",             // 2013
        "20140607_2311_cp_badlands_wwred_blu",          // 2014
        "20171113_2240_cp_badlands_red_blu",            // 2017
        "etf2l-12025-pov-2020-07-21",                   // 2020, POV — has a recorder
        "tf2-2026-pub-pov-clean",                       // 2026, POV, public match
        "demostf-cp_process_f12-2026-08-07",            // 2026, STV — the f12 parity reference
    ];

    [Test]
    public void EveryDemo_ReportsHowManyPlayersSitAtTheWorldOrigin()
    {
        List<string> found = [];
        int demos = 0;
        int sampled = 0;

        IReadOnlyList<string> available = Corpus.FilesWithSchema();

        foreach (string fragment in Sampled)
        {
            // **Resolved here rather than through `Corpus.Demo`, which calls `Assert.Ignore`.** One
            // absent demo would abort the whole measurement and report it as a skip — and a skip is
            // neither a pass nor a failure, so a partial walk would look like no walk at all.
            string? path = available.FirstOrDefault(
                file => Path.GetFileName(file).Contains(fragment, System.StringComparison.Ordinal));

            if (path is null)
            {
                TestContext.Out.WriteLine($"absent: {fragment}");
                continue;
            }

            DemoTimeline? timeline = TryTimeline(path);

            if (timeline is null)
            {
                TestContext.Out.WriteLine($"unreadable: {fragment}");
                continue;
            }

            demos++;

            int first = timeline.FirstTick;
            int last = timeline.LastTick;
            int step = System.Math.Max(1, (last - first) / Samples);

            for (int tick = first; tick <= last; tick += step)
            {
                sampled++;

                foreach (ScenePlayer player in timeline.PlayersAt(tick))
                {
                    if (player is { X: 0f, Y: 0f, Z: 0f })
                    {
                        found.Add(
                            $"{Path.GetFileName(path)} tick {tick} entity {player.EntityIndex} " +
                            $"team {player.Team} drawn {player.Drawn} alive {player.IsAlive}");
                    }
                }
            }
        }

        TestContext.Out.WriteLine($"demos {demos}, ticks sampled {sampled}");
        TestContext.Out.WriteLine($"players at exactly (0,0,0): {found.Count}");

        foreach (string line in found.Take(40))
        {
            TestContext.Out.WriteLine("  " + line);
        }

        if (found.Count > 40)
        {
            TestContext.Out.WriteLine($"  ... and {found.Count - 40} more");
        }

        // **Absent corpus is a SKIP; an empty walk with the corpus present is a FAILURE.** These are
        // different outcomes and the first draft conflated them: under `TF2DEMOSALVAGE_GCOR_ONLY`
        // none of these demos exist, so the control below failed the gate rather than standing
        // aside. The sample is deliberately all lcor — era specimens cannot answer this question.
        if (demos == 0)
        {
            Assert.Ignore(
                "none of the sampled demos are present; this measurement needs the local corpus");
        }

        // **The control, and the only assertion here on purpose.** A count of zero is the
        // interesting outcome — it says the origin rule guards a state that does not occur — but
        // zero is also what a measurement that never ran reports. Asserting the denominator is what
        // tells those apart (`docs/memory/an-empty-search-needs-a-control.md`).
        sampled.ShouldBeGreaterThan(0, "no ticks were sampled, so the zero above means nothing");
    }

    private static DemoTimeline? TryTimeline(string path)
    {
        try
        {
            return TimelineCache.For(path);
        }
        catch (IOException)
        {
            // A demo that will not read is a different finding and has its own suites; it must not
            // silently reduce this measurement's denominator, which is why the count is printed.
            return null;
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }
}
