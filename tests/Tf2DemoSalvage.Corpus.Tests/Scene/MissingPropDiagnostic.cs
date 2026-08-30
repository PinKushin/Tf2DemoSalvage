using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

// Namespaced away from `Tf2DemoSalvage.Corpus.Tests.*`, where `Corpus` binds to the namespace
// rather than to the helper class — the same reason `CorpusPlayerOriginTests` beside it does.
namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// Which entity models the demo names but the scene never produces — a MEASUREMENT.
/// </summary>
/// <remarks>
/// **Written to separate a PRE-EXISTING absence from one a change introduced**, which is the
/// distinction two reverts turned on tonight. The owner reported missing weapons and a missing
/// spawn health cabinet after a build of mine, and then that *"the opponent weapons and the health
/// cab are actually preexisting"*. Attributing a symptom to the wrong cause is what sent a working,
/// tested change into the bin, so this asks the question directly instead.
///
/// **The instrument is the gap between two lists.** `DemoTimeline.ModelPaths()` is every model the
/// recording names; walking `PropsAt` over the demo gives every model that actually reaches the
/// scene. A path in the first and not the second is a model the demo carries and this project never
/// draws — and it says so regardless of which build is being measured, so the same run answers
/// "was this already broken" on any commit.
///
/// Explicit, and it asserts nothing about the demo beyond the precondition that the walk ran (D38).
/// </remarks>
[Explicit("Diagnostic: reports models the demo names that never reach the scene.")]
public sealed class MissingPropDiagnostic
{
    /// <summary>The recording the owner was watching.</summary>
    private const string Recording = "tf2-2026-pub-pov-clean";

    /// <summary>Ticks to sample across the demo.</summary>
    private const int Samples = 400;

    /// <summary>Models worth calling out by name, because they were reported missing.</summary>
    private static readonly string[] Reported =
    [
        "medkit", "health", "cabinet", "resupply", "locker",
        "door_grate003", "weapon", "w_", "c_",
    ];

    [Test]
    public void Decode_TheDemosModels_ReportsWhichNeverReachTheScene()
    {
        string path = Corpus.Demo(Recording);

        DemoTimeline timeline = TimelineCache.For(path);

        int first = timeline.FirstTick;
        int last = timeline.LastTick;
        int step = Math.Max(1, (last - first) / Samples);

        HashSet<string> reached = new(StringComparer.OrdinalIgnoreCase);

        List<SceneProp> props = [];

        for (int tick = first; tick <= last; tick += step)
        {
            props.Clear();
            timeline.PropsAt(tick, props);

            foreach (SceneProp prop in props)
            {
                reached.Add(prop.ModelPath);
            }
        }

        List<string> named = [.. timeline.ModelPaths()];

        List<string> missing =
        [
            .. named.Where(model => !reached.Contains(model))
                .Order(StringComparer.OrdinalIgnoreCase),
        ];

        TestContext.Out.WriteLine(
            $"{named.Count} models named, {reached.Count} reached the scene, {missing.Count} did not");

        foreach (string model in missing)
        {
            TestContext.Out.WriteLine($"  MISSING {model}");
        }

        // **The reported ones called out separately**, because a list of ninety paths buries the
        // three somebody asked about. Reported whether present or absent — "it is there" is as much
        // of an answer as "it is not", and only one of them is visible in the list above.
        foreach (string fragment in Reported)
        {
            List<string> matches =
            [
                .. named.Where(model => model.Contains(fragment, StringComparison.OrdinalIgnoreCase)),
            ];

            int drawn = matches.Count(model => reached.Contains(model));

            TestContext.Out.WriteLine(
                $"'{fragment}': {matches.Count} named, {drawn} reached the scene");
        }

        // A precondition on the HARNESS, not a claim about the demo.
        named.Count.ShouldBeGreaterThan(0, "the demo named no models at all");
    }
}
