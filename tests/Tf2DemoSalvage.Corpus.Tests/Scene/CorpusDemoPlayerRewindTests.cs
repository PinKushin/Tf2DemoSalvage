using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>The bounded rewind on real bytes: etf2l-12025, whose signons are ticked past every packet (batch-31 review 2).</summary>
/// <remarks>
/// Only real bytes carry a writer's signon ticks and a match's packet cadence; the synthetic cases in
/// `DemoPlayerConformanceTests` hold the rule. A rewound player must answer as a fresh read does, bit for bit, through the
/// twenty ticks after each of sixty landings, and re-read only the skip's tail.
/// </remarks>
public sealed class CorpusDemoPlayerRewindTests
{
    [Test]
    public void InterpolateViewpoint_RewoundOnAPovMatch_IsAFreshReadsAnswerAndReadsOnlyTheTail()
    {
        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(Corpus.Demo("etf2l-12025-pov-2020-07-21")));
        DemoPlayer played = new(timeline);
        played.IsSimple.ShouldBeTrue("the control: a real POV opening is the shape the bound is for, or the tail check below measures the full re-read");
        int span = timeline.LastTick - timeline.FirstTick;
        int compared = 0;
        int views = 0;

        foreach (int landing in Enumerable.Range(0, 60).Select(at => timeline.FirstTick + (int)((long)span * ((at * 7919L) % 60) / 60)))
        {
            played.InterpolateViewpoint(timeline.LastTick - 0.5d);
            long before = played.CommandsRead;
            DemoPlayer fresh = new(timeline);

            for (int step = 0; step < 40; step++)
            {
                double tick = landing + 0.25d + (step * 0.5d);
                RecordedView? view = played.InterpolateViewpoint(tick);

                if (step == 0)
                {
                    (played.CommandsRead - before).ShouldBeLessThan(100, $"landing {landing}: a rewind re-reads the skip's tail");
                }

                (view == fresh.InterpolateViewpoint(tick)).ShouldBeTrue($"landing {landing}, tick {tick}");
                played.QueuedPackets.SequenceEqual(fresh.QueuedPackets).ShouldBeTrue($"landing {landing}, tick {tick}: the parse-ahead list");
                played.InterpolatesView.ShouldBe(fresh.InterpolatesView, $"landing {landing}, tick {tick}");
                views += view is null ? 0 : 1;
                compared++;
            }
        }

        compared.ShouldBe(60 * 40);
        views.ShouldBeGreaterThan(compared / 2, "the control: the landings are on the recorder's view, not before it");
    }
}
