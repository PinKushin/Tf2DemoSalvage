using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests;

/// <summary>The <c>env_tonemap_controller</c> globals reach the timeline from a real demo's wire (B514).</summary>
/// <remarks>
/// **The synthetic suite proves the bookkeeping; only a real file proves the production decode reaches it** —
/// <c>docs/memory/output-level-assertion-or-it-is-not-done.md</c>. cp_process_f12's <c>logic_auto</c> sets the controller to
/// 0.5 / 0.7 / bloom 0.5 at tick 1, and every round restart deletes it and creates a fresh one that the
/// <c>logic_auto</c> configures thirteen ticks later; this recording restarts at 334 (`autoexposure` probe).
/// </remarks>
public sealed class TonemapCorpusTests
{
    [Test]
    public void TonemapAt_F12AcrossARoundRestart_IsTheMapsValuesThenTheCvarsThenTheMapsAgain()
    {
        DemoTimeline timeline = TimelineCache.For(Corpus.Demo("demostf-cp_process_f12-2026-08-08-2207"));
        SceneTonemap configured = new(true, 0.5f, true, 0.7f, true, 0.5f);

        timeline.Tonemap.At(100).ShouldBe(configured);
        timeline.Tonemap.At(340).ShouldBe(default(SceneTonemap), "a fresh controller sends zeros until configured");
        timeline.Tonemap.At(20000).ShouldBe(configured);
    }
}
