using System;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// The four-category draw report: what it says, and how often.
/// </summary>
/// <remarks>
/// **The counts are the easy half; the FREQUENCY is what has gone wrong here.** This line printed
/// 13,566 times in two minutes of playback (B163) because it was guarded on "have the counts
/// changed" and the counts oscillate — 280/272 one frame, 272/272 the next — so every frame was a
/// change and the guard never fired. A change guard against an oscillating value is not a guard.
///
/// So the tests below are mostly about how many lines come out, not what is in them.
/// </remarks>
public sealed class DrawTallyTests
{
    [Test]
    public void Report_TheFirstFrame_SaysWhatWasAskedForAndProduced()
    {
        RecordingLogger log = new();
        DrawTally tally = new(log);

        tally.Begin(14);
        tally.Drawn();
        tally.NoGeometry("models/props/crate.mdl");

        tally.Report();

        log.Count("asked for 14, produced 1").ShouldBe(1);
        log.Count("1 no-batches [1xcrate.mdl]").ShouldBe(1);
    }

    [Test]
    public void Report_TheSameCountsTwice_SaysItOnce()
    {
        RecordingLogger log = new();
        DrawTally tally = new(log);

        for (int frame = 0; frame < 2; frame++)
        {
            tally.Begin(3);
            tally.Drawn();
            tally.Report();
        }

        // A steady scene is silent after it has been described once. Without this the line is
        // per-frame, which is how the log reached 8.2 MB in under two minutes.
        log.Count("asked for 3").ShouldBe(1);
    }

    [Test]
    public void Report_CountsThatOscillate_AreRateLimitedRatherThanPrintedEveryFrame()
    {
        // **The case the change guard alone cannot handle, and the one that was measured.** Props
        // enter and leave view, so the counts alternate between two shapes and every frame differs
        // from the last. The guard fires every time; only the rate limit bounds it.
        RecordingLogger log = new();
        DrawTally tally = new(log);

        for (int frame = 0; frame < 60; frame++)
        {
            tally.Begin(10);

            for (int drawn = 0; drawn < (frame % 2 == 0 ? 9 : 10); drawn++)
            {
                tally.Drawn();
            }

            tally.Report();
        }

        // At most one a second, and sixty frames pass in far less than that.
        log.Count("asked for 10").ShouldBe(1);
    }

    [Test]
    public void NotDrawable_InlineSubmodels_CollapseToOneEntry()
    {
        // **A map's doors and moving brushes are *1, *2, … and cp_process names 141 of them.**
        // Listing each turns the line into a wall that hides the entry that matters: they are one
        // gap, not 141 findings.
        RecordingLogger log = new();
        DrawTally tally = new(log);

        tally.Begin(3);

        foreach (string path in new[] { "*1", "*2", "*3" })
        {
            tally.NotDrawable(Prop(path, SceneModelKind.Sprite));
        }

        tally.Report();

        log.Count("3x<inline submodel>#Sprite").ShouldBe(1);

        // The control: a named model is NOT collapsed, so the grouping is about the `*` form rather
        // than about everything.
        log.Count("<no model>").ShouldBe(0);
    }

    [Test]
    public void NotDrawable_AModelWithNoPath_IsNamedRatherThanBlank()
    {
        RecordingLogger log = new();
        DrawTally tally = new(log);

        tally.Begin(1);
        tally.NotDrawable(Prop(string.Empty, SceneModelKind.Unknown));
        tally.Report();

        log.Count("1x<no model>#Unknown").ShouldBe(1);
    }

    [Test]
    public void Begin_ASecondFrame_ForgetsTheFirstFramesRejections()
    {
        // Counters that accumulated across frames would report a number that only ever grows, which
        // reads as a worsening scene rather than as a per-frame tally.
        RecordingLogger log = new();
        DrawTally tally = new(log);

        tally.Begin(2);
        tally.NoGeometry("models/props/crate.mdl");
        tally.Report();

        log.Clear();

        tally.Begin(2);
        tally.Drawn();
        tally.Drawn();
        tally.Report();

        // **Asserted on the ABSENCE of the stale rejection, not on a new line appearing.** Whether
        // this frame prints at all depends on the rate limit, and a test that waited for the limit
        // would be synchronising on a clock — which this project bans outright. What can be pinned
        // without a clock is that last frame's crate is not counted into this one.
        log.Count("1xcrate.mdl").ShouldBe(0);
    }

    private static SceneProp Prop(string model, SceneModelKind kind) =>
        new(1, model, kind, new ScenePose(), null);
}
