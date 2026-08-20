using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

// Namespaced away from `Tf2DemoSalvage.Corpus.Tests.*`, where `Corpus` binds to the namespace.
namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// Does the weapon we resolve belong to the class the demo says the player was?
/// </summary>
/// <remarks>
/// **A model path that looks like a real weapon is not evidence that it is the RIGHT one.** The
/// viewmodel lookup resolved <c>c_sniper_arms.mdl</c> for the owner's own 2013 badlands recording
/// and he said he never played sniper — which is exactly the class of defect this project keeps
/// meeting, a plausible answer that nothing flags.
///
/// Memory of a thirteen-year-old recording is not decisive either way, so this settles it without
/// relying on anyone's recollection. TF2 names its viewmodels after the class that carries them —
/// <c>c_sniper_arms</c>, <c>v_scattergun_scout</c>, <c>v_stickybomb_launcher_demo</c> — and the
/// player entity separately networks <c>m_iClass</c>. Those are two unrelated paths through the
/// decoder: a compressed string table resolved by index, and a delta-compressed integer on the
/// player. If they agree, the resolution is right whatever anybody remembers; if they disagree,
/// there is a real bug and the disagreement names it.
///
/// That is the <c>two-recordings-of-one-value</c> pattern, which has already settled a question in
/// this feature once — the recorded view origin against the recorder's networked origin.
/// </remarks>
public sealed class ViewmodelClassAgreementTests
{
    /// <summary>TF2's class indices, as <c>m_iClass</c> networks them.</summary>
    /// <remarks>
    /// <c>tf_shareddefs.h</c>: scout is 1 and the rest follow in the order the class menu shows
    /// them. Index 0 is <c>TF_CLASS_UNDEFINED</c>, which is a player who has not chosen yet.
    /// </remarks>
    private static readonly string[] ClassNames =
    [
        "undefined", "scout", "sniper", "soldier", "demo",
        "medic", "heavy", "pyro", "spy", "engineer",
    ];

    [Test]
    public void Viewmodel_TheModelPath_NamesTheClassTheDemoSaysThePlayerWas()
    {
        List<string> agreed = [];
        List<string> disagreed = [];

        // Which demos actually contributed a comparison. **Without this the verdict is
        // unfalsifiable in the dangerous direction**: a lookup that answered null everywhere would
        // empty the disagreement list too, and the fix for the two-viewmodel defect is exactly the
        // kind of change that could do it by filtering out one demo entirely.
        HashSet<string> compared = [];

        foreach (string path in Corpus.FilesWithSchema())
        {
            DemoTimeline timeline = TimelineCache.For(path);

            if (timeline.RecorderEntityIndex is not { } recorder)
            {
                continue;
            }

            // Sampled across the demo rather than at one tick, because a player changes class and
            // a single sample cannot tell a wrong resolution from a class switch.
            foreach (int tick in Ticks(timeline))
            {
                if (timeline.ViewmodelAt(tick, recorder) is not { } weapon)
                {
                    continue;
                }

                int? played = timeline.PlayersAt(tick)
                    .Where(player => player.EntityIndex == recorder)
                    .Select(player => player.PlayerClass)
                    .FirstOrDefault();

                if (played is not { } index || index <= 0 || index >= ClassNames.Length)
                {
                    continue;
                }

                string expected = ClassNames[index];
                string model = Path.GetFileNameWithoutExtension(weapon.ModelPath);

                // Deduplicated to demo/class/weapon rather than logged per tick: eleven samples of
                // one unchanged weapon is eleven lines that say the same thing, and the whole
                // point of the output is to be readable when it goes red.
                string line = $"{Path.GetFileName(path)}: class {index} ({expected}), {model}";

                // TF2 names viewmodels after the class that holds them. A weapon whose name
                // carries a DIFFERENT class than the player is the disagreement worth catching.
                bool names = model.Contains(expected, StringComparison.OrdinalIgnoreCase);
                (names ? agreed : disagreed).Add(line);
                compared.Add(Path.GetFileName(path));
            }
        }

        TestContext.Out.WriteLine("COMPARED: " + string.Join(", ", compared.Order()));
        TestContext.Out.WriteLine("AGREED:");
        TestContext.Out.WriteLine(string.Join(Environment.NewLine, agreed.Distinct().Order()));
        TestContext.Out.WriteLine("DISAGREED:");
        TestContext.Out.WriteLine(string.Join(Environment.NewLine, disagreed.Distinct().Order()));

        // A positive control before any verdict: an empty comparison agrees with everything.
        (agreed.Count + disagreed.Count).ShouldBeGreaterThan(
            0, "no tick had both a viewmodel and a known class, so nothing was compared");

        // **The demo the defect lived on, named rather than counted.** It is the only corpus
        // recording carrying two viewmodels, so a change that quietly stopped resolving one there
        // would leave every other number looking healthy.
        compared.ShouldContain(
            TwoViewmodelDemo,
            $"{TwoViewmodelDemo} contributed no comparison, so its weapon resolves to nothing");

        disagreed.ShouldBeEmpty(
            "a weapon was resolved for a class that does not carry it");
    }

    /// <summary>The corpus recording that describes a main hand and an off hand at once.</summary>
    /// <remarks>
    /// Measured 2026-08-20: every other demo carries one viewmodel entity, this one carries two.
    /// It is where the wrong weapon appeared — <c>v_watch_spy</c>, held steady while the
    /// recorder's networked class went from soldier to scout.
    /// </remarks>
    private const string TwoViewmodelDemo = "tf2-2009-build3862-pov-cp_badlands.dem";

    /// <summary>A spread of ticks across the demo, so a class change is visible.</summary>
    private static IEnumerable<int> Ticks(DemoTimeline timeline)
    {
        int first = timeline.FirstTick;
        int last = timeline.LastTick;

        if (last <= first)
        {
            yield return first;
            yield break;
        }

        for (int step = 0; step <= 10; step++)
        {
            yield return first + ((last - first) * step / 10);
        }
    }
}
