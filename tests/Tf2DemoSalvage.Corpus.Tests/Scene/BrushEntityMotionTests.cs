using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// Whether a brush entity — a door, a gate, a lift — actually moves in the corpus.
/// </summary>
/// <remarks>
/// **B71 said "doors never open", and by the time anyone checked, three of its four steps were
/// already done.** Submodel faces are held back from the static world (176 of them on cp_badlands),
/// each <c>*N</c> is built as its own geometry, and the entity path draws them — the render log lists
/// <c>*57</c>, <c>*61</c>, <c>*65</c> among its posed models, at positions like
/// <c>(1077, 4602, -8)</c> that no compiled submodel carries, because a submodel's own origin is
/// zero and that number can only have come from the entity.
///
/// **What none of that established is MOTION**, which is the actual claim in the risk. Every
/// available run had opened at a tick and stayed there, so the renderer's own
/// <c>brush … seconds</c> line — which fires when a brush entity's height changes — reported each
/// entity once, at second zero, and said nothing.
///
/// So this asks the timeline instead of the screen. No device, no window, no playback: a track's
/// pose at two ticks either differs or it does not.
///
/// **A demo where nothing moves is a real answer**, not a failure. Most of the era specimens are
/// short solo recordings in which nobody opens a door, and the assertion is therefore about the
/// corpus as a whole rather than about any one file.
/// </remarks>
public sealed class BrushEntityMotionTests
{
    [Test]
    public void BrushEntities_AcrossTheCorpus_ChangePositionOverTime()
    {
        List<string> moving = [];
        int demosWithBrushwork = 0;

        foreach (string path in Corpus.FilesWithSchema())
        {
            DemoTimeline timeline = TimelineCache.For(path);

            List<ScenePropTrack> brushwork =
                [.. timeline.Props.Where(track => track.Kind == SceneModelKind.Brush)];

            if (brushwork.Count == 0)
            {
                continue;
            }

            demosWithBrushwork++;

            foreach (ScenePropTrack track in brushwork)
            {
                if (Travelled(track) is not { } distance || distance <= 1d)
                {
                    continue;
                }

                moving.Add(
                    $"{Path.GetFileName(path)}: {track.ModelPath} #{track.EntityIndex} " +
                    $"moved {distance:0.#} units");
            }
        }

        foreach (string line in moving.Take(20))
        {
            TestContext.Out.WriteLine(line);
        }

        TestContext.Out.WriteLine(
            $"{moving.Count} brush entities move, across {demosWithBrushwork} demos with brushwork");

        demosWithBrushwork.ShouldBeGreaterThan(
            0, "brush entities reach the scene layer; if none do, the decode regressed");

        // **The claim B71 makes, stated so it can be false.** If no brush entity in any demo ever
        // changes position, then either nothing in the corpus opens a door — which would make this
        // whole feature unverifiable and is worth knowing — or the pose is not tracking the entity.
        moving.ShouldNotBeEmpty(
            "a door that opens is a brush entity whose networked origin changes; none changing " +
            "means either the corpus never opens one or the pose is frozen at its first value");
    }

    /// <summary>How far a track's pose moves between its first and last recorded tick.</summary>
    /// <remarks>
    /// The widest separation between any two keyframes, which is the quantity that answers "did it
    /// move" without assuming the motion ends where it started.
    /// </remarks>
    private static double? Travelled(ScenePropTrack track)
    {
        // **The keyframes themselves rather than a pose lookup.** A track stores what the demo
        // networked; `At` interpolates between those, so asking it at the ends would round-trip
        // through the interpolator to recover values already in hand. The widest separation between
        // any two keyframes is also a better measure than first-versus-last: a door that opens and
        // shuts inside one demo reads as stationary at the ends and is caught here.
        if (track.Keyframes.Count < 2)
        {
            return null;
        }

        double widest = 0;

        for (int first = 0; first < track.Keyframes.Count; first++)
        {
            for (int second = first + 1; second < track.Keyframes.Count; second++)
            {
                ScenePose one = track.Keyframes[first].Pose;
                ScenePose two = track.Keyframes[second].Pose;

                double dx = two.X - one.X;
                double dy = two.Y - one.Y;
                double dz = two.Z - one.Z;

                widest = Math.Max(widest, Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz)));
            }
        }

        return widest;
    }
}
