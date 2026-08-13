using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// Turning a demo into player positions over time.
/// </summary>
/// <remarks>
/// **The step between decoding a demo and watching one.** Everything underneath already worked:
/// entities decode, the scene layer accumulates them, and the corpus tests confirm players carry
/// origins. What none of that produces is <em>where everyone was at tick 4,000</em>, which is the
/// only question a viewer asks.
///
/// Built once and kept, rather than decoded on demand. A demo is a forward-only stream of deltas —
/// there is no way to jump to a tick without replaying everything before it — so scrubbing
/// backwards would mean re-reading from the start each time.
/// </remarks>
public sealed class DemoTimelineTests
{
    [Test]
    public void Build_ProducesFramesThatAdvanceThroughTheDemo()
    {
        foreach (string path in Corpus.FilesWithSchema())
        {
            DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));

            TestContext.Out.WriteLine(
                $"TIMELINE {Path.GetFileName(path)}: {timeline.Frames.Count} frames, " +
                $"ticks {timeline.FirstTick} to {timeline.LastTick}, " +
                $"{timeline.Frames.Max(frame => frame.Players.Count)} players at once");

            timeline.Frames.Count.ShouldBeGreaterThan(0, path);

            // **Ticks must increase.** A frame list out of order silently plays the demo
            // backwards in places, which looks like players teleporting rather than like a bug.
            for (int index = 1; index < timeline.Frames.Count; index++)
            {
                timeline.Frames[index].Tick.ShouldBeGreaterThan(
                    timeline.Frames[index - 1].Tick, $"{path} frame {index}");
            }
        }
    }

    [Test]
    public void Build_FindsPlayersWhoActuallyMove()
    {
        // **The measurement that a static scene cannot pass.** A timeline whose frames all carry
        // the same positions would satisfy every structural assertion above while showing a map
        // full of statues. Players move; the question is whether the frames record it.
        foreach (string path in Corpus.FilesWithSchema())
        {
            DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));

            Dictionary<int, HashSet<(float, float)>> seen = [];

            foreach (TimelineFrame frame in timeline.Frames)
            {
                foreach (ScenePlayer player in frame.Players)
                {
                    if (!seen.TryGetValue(player.EntityIndex, out HashSet<(float, float)>? places))
                    {
                        places = [];
                        seen[player.EntityIndex] = places;
                    }

                    places.Add((player.X, player.Y));
                }
            }

            int moved = seen.Count(entry => entry.Value.Count > 1);

            TestContext.Out.WriteLine(
                $"TIMELINE {Path.GetFileName(path)}: {moved} of {seen.Count} players occupy more " +
                $"than one position");

            if (seen.Count > 0)
            {
                moved.ShouldBeGreaterThan(0, $"{path}: nobody moved, which is not a real demo");
            }
        }
    }

    [Test]
    public void PlayersAt_ReturnsTheLastFrameAtOrBeforeATick()
    {
        // Scrubbing lands on arbitrary ticks, and not every tick has a frame - positions arrive
        // with packets, not on a fixed cadence. The viewer must get the most recent known
        // positions rather than nothing at all, or the map blinks empty between updates.
        string path = Corpus.FilesWithSchema()[0];

        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));

        TimelineFrame middle = timeline.Frames[timeline.Frames.Count / 2];

        timeline.PlayersAt(middle.Tick).ShouldBe(middle.Players);

        // One tick past a frame still shows that frame, until the next one arrives.
        IReadOnlyList<ScenePlayer> justAfter = timeline.PlayersAt(middle.Tick + 1);

        justAfter.ShouldNotBeEmpty();

        // Before the first frame there is genuinely nothing to show, and an empty list says so
        // without the caller having to special-case it.
        timeline.PlayersAt(timeline.FirstTick - 1).ShouldBeEmpty();
    }
}
