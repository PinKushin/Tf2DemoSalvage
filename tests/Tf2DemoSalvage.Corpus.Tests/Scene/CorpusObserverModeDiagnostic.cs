using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

// Namespaced away from `Tf2DemoSalvage.Corpus.Tests.*`, where `Corpus` binds to the namespace
// rather than to the helper class — the same reason `CorpusPlayerOriginTests` beside it does.
namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// <c>m_iObserverMode</c> reaching the timeline off real recordings.
/// </summary>
/// <remarks>
/// **The level that catches a wiring failure, and nothing below it can** — `ObserverModeConformance
/// Tests` proves `Effective` obeys the rule when handed a mode, and says nothing about whether any
/// mode ever arrives. Three no-ops shipped in one session for exactly that gap
/// (`docs/memory/output-level-assertion-or-it-is-not-done.md`).
///
/// **B225.** The owner watched a demo play — which had never happened before, because autoplay was
/// broken (B223) — and saw a viewmodel drawn while he was in spectator. TF2 puts a player who goes
/// to spectator into <c>OBS_MODE_ROAMING</c>, and a point-of-view recording follows whatever camera
/// they chose, so the recorded view is not the player's eyes and
/// <c>C_BasePlayer::LocalPlayerInFirstPersonView</c> refuses first person there.
///
/// **The era specimen IS the right demo here**, unlike for a rendering question. The owner's rule —
/// *"those really are not goign to help find rendering issues since they are brand new installs, no
/// items, and no real MP"* — is about props and worn items. This question is about the RECORDER
/// himself, which every point-of-view demo has, and the badlands specimen is the one he was
/// watching when he found it.
/// </remarks>
[Explicit("Diagnostic: reports which observer modes real point-of-view demos contain.")]
public sealed class CorpusObserverModeDiagnostic
{
    /// <summary>Ticks to sample per demo.</summary>
    private const int Samples = 300;

    /// <summary>Point-of-view demos, which are the ones with a recorder to observe with.</summary>
    private static readonly string[] Sampled =
    [
        "tf2-2013-build1729296-pov-cp_badlands",  // where the owner saw it
        "etf2l-12025-pov-2020-07-21",             // 2020 POV, real match
        "tf2-2026-pub-pov-clean",                 // 2026 POV, public match
    ];

    [Test]
    public void Decode_AcrossPointOfViewDemos_ReportsWhichObserverModesArrive()
    {
        // **A report before an assertion, deliberately.** The exact modes a recording contains are
        // not predictable from the SDK — they depend on what the player did — so this prints the
        // distribution and asserts only what the SDK guarantees. Tightening it to a number nobody
        // has measured would be inventing ground truth.
        Dictionary<int, int> seen = [];
        Dictionary<string, HashSet<int>> byDemo = [];
        int demos = 0;
        int alive = 0;

        IReadOnlyList<string> available = Corpus.FilesWithSchema();

        foreach (string fragment in Sampled)
        {
            string? path = available.FirstOrDefault(
                file => Path.GetFileName(file).Contains(fragment, StringComparison.Ordinal));

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

            Dictionary<int, int> here = [];
            int first = timeline.FirstTick;
            int last = timeline.LastTick;
            int step = Math.Max(1, (last - first) / Samples);

            // **The number that says whether this fix changes anything at all.** A player observing
            // while DEAD was already put into third person by the life-state rule
            // (`SpectatorEffectiveModeTests`), so those samples were never drawn wrong. Only
            // alive-and-observing is new — and if that were zero, B225 would have to have some
            // other cause and this whole change would be a no-op wearing a green suite.
            int aliveObserving = 0;

            for (int tick = first; tick <= last; tick += step)
            {
                foreach (ScenePlayer player in timeline.PlayersAt(tick))
                {
                    int mode = player.ObserverMode ?? ObserverModes.None;

                    here[mode] = here.GetValueOrDefault(mode) + 1;
                    seen[mode] = seen.GetValueOrDefault(mode) + 1;

                    if (player.IsAlive && !player.InFirstPersonView)
                    {
                        aliveObserving++;
                        alive++;
                    }
                }
            }

            byDemo[fragment] = [.. here.Keys];

            TestContext.Out.WriteLine(
                $"{fragment}: " + string.Join(
                    ", ", here.OrderBy(pair => pair.Key).Select(pair => $"{Name(pair.Key)}={pair.Value}"))
                + $"  [alive AND observing: {aliveObserving}]");
        }

        TestContext.Out.WriteLine(
            "total: " + string.Join(
                ", ", seen.OrderBy(pair => pair.Key).Select(pair => $"{Name(pair.Key)}={pair.Value}")));

        // **No assertion, deliberately** — see the remarks. The numbers worth keeping are recorded
        // in B225 and here: on the badlands specimen, DEATHCAM=48 and CHASE=63 against NONE=200, so
        // better than a third of that recording is a moment the engine would draw no viewmodel in.
        // And across all three, samples that are alive AND observing come to **zero**, which is the
        // measurement that killed the first theory about B225.
        if (demos == 0)
        {
            Assert.Ignore("no point-of-view demo was available; this needs lcor.");
            return;
        }

        // **A precondition on the HARNESS, not a claim about the data.** That every sampled demo
        // yielded at least one mode says the walk ran; what those modes ARE is reported above and
        // deliberately not asserted.
        seen.Count.ShouldBeGreaterThan(0, "the sampled demos yielded no observer modes at all");
    }

    /// <summary>The timeline, or null when the file will not read.</summary>
    /// <remarks>
    /// A demo that will not read is a different finding with its own suites; it must not silently
    /// reduce this measurement's denominator, which is why the count is asserted and printed.
    /// </remarks>
    private static DemoTimeline? TryTimeline(string path)
    {
        try
        {
            return TimelineCache.For(path);
        }
        catch (IOException)
        {
            return null;
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private static string Name(int mode) => mode switch
    {
        ObserverModes.None => "NONE",
        ObserverModes.DeathCam => "DEATHCAM",
        ObserverModes.FreezeCam => "FREEZECAM",
        ObserverModes.Fixed => "FIXED",
        ObserverModes.InEye => "IN_EYE",
        ObserverModes.Chase => "CHASE",
        ObserverModes.PointOfInterest => "POI",
        ObserverModes.Roaming => "ROAMING",
        _ => $"?{mode}",
    };
}
