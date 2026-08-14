using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// Which way a player is facing.
/// </summary>
/// <remarks>
/// **Needed the moment a player stops being a dot.** A dot has no orientation, so the yaw the
/// timeline already decoded was thrown away in <c>PlayersAt</c>, which kept only the position out
/// of a pose that carries all six numbers. Drawing a class model with that missing puts every
/// player on the map facing the same direction.
///
/// The angle comes from the entity's own <c>m_angRotation</c>, interpolated by the same track as
/// its position — not from a second path. TF2 adds <c>m_angEyeAngles</c> on top of that
/// (<c>c_tf_player.cpp:3874</c>) for where the head is pointed, which is a different question from
/// where the body is.
/// </remarks>
public sealed class PlayerFacingTests
{
    [Test]
    public void PlayersAt_CarriesTheYawTheTrackHolds()
    {
        int checkedDemos = 0;

        foreach (string path in Corpus.FilesWithSchema())
        {
            DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));

            if (timeline.Frames.Count == 0)
            {
                continue;
            }

            // A tick with someone playing on it, taken from the middle so the demo has settled.
            int tick = timeline.Frames[timeline.Frames.Count / 2].Tick;

            List<ScenePlayer> players = [];
            timeline.PlayersAt(tick, players);

            ScenePlayer[] playing = [.. players.Where(player => player.IsPlaying)];

            if (playing.Length == 0)
            {
                continue;
            }

            checkedDemos++;

            // **The prediction is the track's own number**, so this measures the plumbing rather
            // than the decode: whatever the pose says, the player must report.
            foreach (ScenePlayer player in playing)
            {
                ScenePropTrack? track = timeline.TrackFor(player.EntityIndex);

                if (track?.At(tick) is not { } pose)
                {
                    continue;
                }

                player.Yaw.ShouldBe(
                    pose.Yaw,
                    0.001f,
                    $"{Path.GetFileName(path)} entity {player.EntityIndex}");
            }

            // **The control, and without it the test passes on all-zero yaw.** A build that never
            // read an angle would report 0 for every player and agree with a pose that was also 0.
            // Real players in a real match do not all face north.
            TestContext.Out.WriteLine(
                $"FACING {Path.GetFileName(path)}: {playing.Length} playing, " +
                $"{playing.Select(player => MathF.Round(player.Yaw)).Distinct().Count()} distinct yaws");
        }

        checkedDemos.ShouldBeGreaterThan(0, "no demo in the corpus produced a playing player");
    }

    [Test]
    public void PlayersInAMatch_DoNotAllFaceTheSameWay()
    {
        // The control for the test above, stated as its own experiment rather than a note. Yaw
        // reported as a constant is indistinguishable from yaw plumbed correctly, if every player
        // happens to be looking the same way - so this demands the spread across a whole demo.
        int best = 0;
        string where = "none";

        foreach (string path in Corpus.FilesWithSchema())
        {
            DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));
            HashSet<int> yaws = [];

            foreach (TimelineFrame frame in timeline.Frames)
            {
                List<ScenePlayer> players = [];
                timeline.PlayersAt(frame.Tick, players);

                foreach (ScenePlayer player in players.Where(player => player.IsPlaying))
                {
                    yaws.Add((int)MathF.Round(player.Yaw));
                }
            }

            if (yaws.Count > best)
            {
                best = yaws.Count;
                where = Path.GetFileName(path);
            }
        }

        TestContext.Out.WriteLine($"FACING best spread {best} distinct yaws in {where}");

        best.ShouldBeGreaterThan(
            8, "players in a real match look in many directions, so a handful of values is a bug");
    }
}
