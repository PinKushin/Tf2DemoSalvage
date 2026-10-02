using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// A real point-of-view demo's recorder, his feet turned per frame through the production chain (B450).
/// </summary>
/// <remarks>
/// **The output-level assertion** (<c>docs/memory/output-level-assertion-or-it-is-not-done.md</c>): the body
/// <c>TimelineMoments</c> hands the scene, over a <see cref="DemoPlayer"/>, at two frames inside ONE tick. The
/// engine converges the feet by <c>gpGlobals-&gt;frametime</c> from <c>EyeAngles()</c>
/// (<c>multiplayer_animstate.cpp:1759</c>, <c>c_tf_player.cpp:4279-4284</c>), so the drawn yaw moves between them;
/// the per-tick route read the same timeline frame twice and could not. The twist is measured against the drawn feet.
/// gcor's 2013 POV: the one era specimen whose recorder runs about while turning.
/// </remarks>
public sealed class CorpusRecorderFeetTests
{
    private const string PovDemo = "tf2-2013-build1729296-pov-cp_badlands";

    [Test]
    public void PlayersAt_TwoFramesInsideATickWhileTurning_TurnsTheDrawnFeetBetweenThem()
    {
        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(Corpus.Demo(PovDemo)));
        int recorder = timeline.RecorderEntityIndex.ShouldNotBeNull();

        // The first tick where he is alive, moving (so the goal is his eye, :1716-1720) and his recorded local yaw
        // turns by more than two degrees into the next — a frame half way through it has a goal to turn toward.
        int tick = Enumerable.Range(timeline.FirstTick, timeline.LastTick - timeline.FirstTick).First(at =>
            Turning(timeline, at, recorder));

        TimelineMoments moments = new(timeline) { Player = new DemoPlayer(timeline) };

        ScenePlayer start = Recorder(moments, tick + 0.25, recorder);
        ScenePlayer later = Recorder(moments, tick + 0.75, recorder);

        // The control: one timeline frame stands under both, so the per-tick route draws one yaw for the two.
        timeline.PlayersAt(tick).Single(player => player.EntityIndex == recorder).Yaw.ShouldBe(
            timeline.PlayersAt((int)Math.Floor(tick + 0.75)).Single(player => player.EntityIndex == recorder).Yaw);

        later.Yaw.ShouldNotBe(start.Yaw);
        later.AimYaw.ShouldNotBeNull().ShouldBe(
            -Normalize(later.EyeYaw.ShouldNotBeNull() - later.Yaw), 1e-3f);
    }

    private static bool Turning(DemoTimeline timeline, int tick, int recorder)
    {
        if (timeline.RecordedViewAt(tick) is not { } now || timeline.RecordedViewAt(tick + 1) is not { } next ||
            timeline.PlayersAt(tick).FirstOrDefault(player => player.EntityIndex == recorder) is not
                { IsAlive: true, Velocity: { } velocity })
        {
            return false;
        }

        return MathF.Sqrt((velocity.X * velocity.X) + (velocity.Y * velocity.Y)) > 1f &&
            MathF.Abs(Normalize(next.LocalAngles.Yaw - now.LocalAngles.Yaw)) > 2f;
    }

    private static ScenePlayer Recorder(TimelineMoments moments, double tick, int recorder)
    {
        List<ScenePlayer> players = [];
        moments.PlayersAt(tick, players);

        return players.Single(player => player.EntityIndex == recorder);
    }

    private static float Normalize(float degrees) => MathF.IEEERemainder(degrees, 360f);
}
