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
    public void DoPlayersCarryASequenceAndCycle()
    {
        // **The measurement that decides whether an animation state has to be emulated at all.**
        // TF2 computes a player's animation client-side in CTFPlayerAnimState, which is why the
        // assumption has been that a demo cannot say what a player is doing. But m_nSequence and
        // m_flCycle live on DT_BaseAnimating and a player is a CBaseAnimating, so the question is
        // what the wire actually carries - not what the client would compute if nothing did.
        foreach (string path in Corpus.FilesWithSchema())
        {
            DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));

            if (timeline.Frames.Count == 0)
            {
                continue;
            }

            HashSet<int> sequences = [];
            HashSet<int> cycles = [];
            int sampled = 0;

            foreach (TimelineFrame frame in timeline.Frames)
            {
                List<ScenePlayer> players = [];
                timeline.PlayersAt(frame.Tick, players);

                foreach (ScenePlayer player in players.Where(player => player.IsPlaying))
                {
                    if (timeline.TrackFor(player.EntityIndex)?.At(frame.Tick) is not { } pose)
                    {
                        continue;
                    }

                    sampled++;
                    sequences.Add(pose.Sequence);
                    cycles.Add((int)MathF.Round(pose.Cycle * 100f));
                }
            }

            TestContext.Out.WriteLine(
                $"PSEQ {Path.GetFileName(path)}: {sampled} samples, " +
                $"{sequences.Count} distinct sequences {string.Join(",", sequences.Order().Take(8))}, " +
                $"{cycles.Count} distinct cycles");
        }

        Assert.Pass();
    }

    [Test]
    public void WhatDoesADemoGiveAnAnimationStateToWorkFrom()
    {
        // **CTFPlayerAnimState needs three things**, and this asks the corpus for each. Horizontal
        // speed decides standing against running (GetOuterXYSpeed against MOVING_MINIMUM_SPEED),
        // FL_DUCKING decides crouching, and the eye yaw aims the upper body.
        //
        // Speed is not networked as such but positions are, so it can be differenced. Whether that
        // produces sane numbers - a TF2 scout runs at 400 units a second, a heavy at 230 - is the
        // measurement, because a derived quantity that comes out wrong is worse than one missing.
        foreach (string path in Corpus.FilesWithSchema().Take(4))
        {
            DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));

            Dictionary<int, (float X, float Y, int Tick)> last = [];
            float fastest = 0f;
            int moving = 0;
            int still = 0;

            foreach (TimelineFrame frame in timeline.Frames)
            {
                List<ScenePlayer> players = [];
                timeline.PlayersAt(frame.Tick, players);

                foreach (ScenePlayer player in players.Where(player => player.IsPlaying))
                {
                    if (last.TryGetValue(player.EntityIndex, out (float X, float Y, int Tick) was) &&
                        frame.Tick > was.Tick)
                    {
                        float seconds = (frame.Tick - was.Tick) * timeline.IntervalPerTick;

                        if (seconds > 0f)
                        {
                            float speed = MathF.Sqrt(
                                ((player.X - was.X) * (player.X - was.X)) +
                                ((player.Y - was.Y) * (player.Y - was.Y))) / seconds;

                            // Ignore teleports: a respawn moves a player across the map in a tick.
                            if (speed < 2000f)
                            {
                                fastest = MathF.Max(fastest, speed);
                                _ = speed > 10f ? moving++ : still++;
                            }
                        }
                    }

                    last[player.EntityIndex] = (player.X, player.Y, frame.Tick);
                }
            }

            // **The fastest figure is an artefact and is printed as one.** It lands at 1985 to
            // 1996 on every demo, immediately under the 2000 cutoff above - the sign of a clamped
            // distribution rather than a measured maximum, since a respawn moves a player across
            // the map in one tick and the filter is what those readings hit. The usable result is
            // the split: speed separates moving from still, which is the input HandleMoving needs.
            TestContext.Out.WriteLine(
                $"PANIM {Path.GetFileName(path)}: {moving} moving samples, {still} still, " +
                $"fastest under the teleport cutoff {fastest:0} (clamped, not a real maximum)");
        }

        Assert.Pass();
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
