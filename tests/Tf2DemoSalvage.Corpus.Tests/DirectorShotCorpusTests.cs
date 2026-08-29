using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests;

/// <summary>
/// The director's <c>hltv_chase</c> shots reach the timeline from a real SourceTV recording.
/// </summary>
/// <remarks>
/// **A unit test cannot fail if the event never arrives**, and that is the risk with every field
/// decoded from a demo: a wrong name, a wrong type, or a message this reader skips all read as "the
/// director said nothing" and leave the camera on its defaults for ever. The same failure has
/// shipped three times in this project with a green suite.
///
/// **A point-of-view demo genuinely has no director**, so absence is only evidence on a SourceTV
/// recording. That is why this asserts on a SourceTV demo and reports across the corpus rather than
/// demanding shots everywhere.
/// </remarks>
public sealed class DirectorShotCorpusTests
{
    [Test]
    public void DirectorAt_OnEveryCorpusDemo_IsAbsentBecauseNoneIsSourceTv()
    {
        // **This asserts the ABSENCE, and it is a claim about the corpus rather than about the
        // reader.** Measured before it was written: `hltv_chase` and `hltv_status` appear zero times
        // in the bytes of either demo, while `player_death` appears in both — so the events are not
        // being missed, they were never recorded. Every demo within reach is a point-of-view
        // recording, and only a SourceTV broadcast has a director choosing shots.
        //
        // **Kept as a test rather than deleted, because it is the thing that will change.** The day
        // a SourceTV demo joins the corpus this goes red, and that is exactly the moment somebody
        // should come back and point `DirectorShotTests`' authored specimen at real bytes. An
        // absence nobody records is an absence nobody revisits.
        foreach (string name in new[] { "cp_process_f12", "tf2-2013-build1729296-pov-cp_badlands" })
        {
            TimelineCache.For(Corpus.Demo(name)).HasDirector.ShouldBeFalse(
                $"{name} is a point-of-view recording and carries no hltv_chase; if this is now " +
                $"true the corpus has gained a SourceTV demo and the director path can finally be " +
                $"measured against real bytes instead of an authored specimen");
        }
    }

    // **The control for the absence above was run outside the suite and is recorded here rather
    // than kept as code.** `DemoScan.Result` is internal to Core, so a Corpus test cannot use it;
    // the check was a byte-level search of the demo files for the event NAMES, which the game event
    // list carries as strings. `player_death` appears in both files and `hltv_chase` and
    // `hltv_status` in neither — so other events do arrive and these were never recorded, which is
    // what makes the assertion above a statement about the corpus rather than about this reader.

    [Test]
    [Explicit("Diagnostic: what the director asks for, per demo.")]
    public void ReportDirectorShots()
    {
        foreach (string name in new[] { "cp_process_f12", "tf2-2013-build1729296-pov-cp_badlands" })
        {
            DemoTimeline timeline = TimelineCache.For(Corpus.Demo(name));

            List<string> seen = [];

            for (int tick = timeline.FirstTick; tick <= timeline.LastTick; tick += 8)
            {
                if (timeline.DirectorAt(tick) is { } shot &&
                    (seen.Count == 0 || seen[^1] != Describe(shot)))
                {
                    seen.Add(Describe(shot));
                }
            }

            TestContext.Out.WriteLine(
                $"{name}: director {(timeline.HasDirector ? "present" : "ABSENT")}, " +
                $"{seen.Count} distinct shots");

            foreach (string shot in seen.Take(8))
            {
                TestContext.Out.WriteLine($"    {shot}");
            }
        }
    }

    private static string Describe(DirectorShot shot) => string.Create(
        CultureInfo.InvariantCulture,
        $"ineye {shot.InEye,-5} target {shot.Target,2} second {shot.SecondTarget,2} " +
        $"distance {shot.Distance,6:0.#} offset {shot.Offset,5:0.#} " +
        $"theta {shot.Theta,6:0.#} phi {shot.Phi,6:0.#}");
}
