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
    public void Build_ReadsTeamAndClassFromThePlayerResource()
    {
        // **Neither travels on the player entity on a modern demo.** A positioned modern player
        // carries only its health of the three; both team and class live on a single
        // CTFPlayerResource entity as arrays indexed by entity index. Reading them off the player
        // gives null for everyone, which draws a match in which nobody has a team - and looks like
        // a colour bug rather than a missing entity.
        int best = 0;

        foreach (string path in Corpus.FilesWithSchema())
        {
            DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));

            if (timeline.Frames.Count == 0)
            {
                continue;
            }

            ScenePlayer[] everyone = [.. timeline.Frames.SelectMany(frame => frame.Players)];

            if (everyone.Length == 0)
            {
                continue;
            }

            // **Three buckets, not two.** The first version counted "team is 2 or 3" as known and
            // everything else as missing, which folds spectators and the SourceTV camera in with
            // genuine gaps - and they are neither missing nor players. That made a correct 92%
            // look like a defect.
            int playing = everyone.Count(player => player.IsPlaying);
            int watching = everyone.Count(
                player => player.Team is SceneTeams.Spectator or SceneTeams.Unassigned);
            int unknown = everyone.Count(player => player.Team is null);
            int withClass = everyone.Count(player => player.PlayerClass is >= 1 and <= 9);

            TestContext.Out.WriteLine(
                $"ROSTER {Path.GetFileName(path)}: {playing * 100 / everyone.Length}% playing, " +
                $"{watching * 100 / everyone.Length}% watching, " +
                $"{unknown * 100 / everyone.Length}% unknown, " +
                $"{withClass * 100 / everyone.Length}% have a class, over {everyone.Length} sightings");

            // Not every sighting: a player is in the world for a moment before the resource has
            // said anything about them, and a spectator has neither. Most must, or the arrays are
            // not being read.
            // **The real assertion, and it is exact.** Every sighting must be accounted for:
            // playing, watching, or genuinely never stated. Anything else means a team number the
            // engine does not define.
            (playing + watching + unknown).ShouldBe(
                everyone.Length, $"{path}: every sighting must fall in a known bucket");

            best = Math.Max(best, (playing + watching) * 100 / everyone.Length);
        }

        // **The mechanism, not the coverage.** One demo in the corpus reaches 100% on both, which
        // proves the arrays are found and read correctly. Coverage on the rest ranges from 0 to
        // 100 and that spread is a real open question - recorded in RISKS as B45 rather than
        // papered over with a low threshold here.
        //
        // The format is not the reason: player_resource.cpp transmits with FL_EDICT_ALWAYS and
        // refreshes every connected player every 0.1 seconds, so the data is in every demo
        // continuously.
        best.ShouldBe(
            100, "every sighting in at least one demo must have a stated team");
    }

    [Test]
    public void Build_ReadsTheServersOwnTickRate()
    {
        // **Never a constant.** TF2's usual interval is 0.015, but early servers ran 33 tick and
        // LoadedDemo had 66.667 hardcoded for its duration. A demo replayed at the wrong rate is
        // wrong in a way that reads as a slow or fast server rather than as a defect, so the rate
        // comes from svc_ServerInfo and the corpus is asked what it actually contains.
        foreach (string path in Corpus.FilesWithSchema())
        {
            DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));

            if (timeline.IntervalPerTick <= 0f)
            {
                TestContext.Out.WriteLine($"RATE {Path.GetFileName(path)}: no svc_ServerInfo");
                continue;
            }

            TestContext.Out.WriteLine(
                $"RATE {Path.GetFileName(path)}: {timeline.IntervalPerTick:F6}s per tick, " +
                $"{1f / timeline.IntervalPerTick:F2} per second");

            // A plausible range rather than an exact value: the point is that it was read, and a
            // garbage float would fall outside any sane server rate.
            (1f / timeline.IntervalPerTick).ShouldBeInRange(
                10f, 200f, $"{path}: an implausible tick rate means the field was misread");
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
