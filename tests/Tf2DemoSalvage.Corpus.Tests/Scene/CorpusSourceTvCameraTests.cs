using System.Collections.Generic;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// A real SourceTV demo's in-eye camera sits in the body the moment draws, playing and paused, through the production
/// chain (B747).
/// </summary>
/// <remarks>
/// **The output-level assertion** (<c>docs/memory/output-level-assertion-or-it-is-not-done.md</c>): the synthetic tests
/// hold the camera's own rule, and this holds that <see cref="TimelineEyes"/> hands the timeline the same
/// <c>interpolating</c> flag the moment samples with. `CalcInEyeCamView` places the eye at `pPlayer->GetAbsOrigin()`
/// (hltvcamera.cpp:315), so the eye's X and Y are the drawn body's exactly.
/// </remarks>
public sealed class CorpusSourceTvCameraTests
{
    [TestCase(true, TestName = "Eye_OnASourceTvDemoBetweenTicksPlaying_IsInTheDrawnBody")]
    [TestCase(false, TestName = "Eye_OnASourceTvDemoBetweenTicksPaused_IsInTheHeldBody")]
    public void Eye_OnASourceTvDemoBetweenTicks_IsInTheBodyTheMomentDraws(bool playing)
    {
        DemoTimeline timeline = TimelineCache.For(Corpus.Demo("tf2-2013-build1729296-spec-stv-cp_granary"));
        SpectatorView view = new(NullLogger.Instance)
        {
            Eyes = new TimelineEyes(timeline, new DemoPlayer(timeline)),
            Interpolating = () => playing,
        };

        double moment = MovingMoment(timeline, view);
        int target = view.Target((int)System.Math.Floor(moment)).ShouldNotBeNull().EntityIndex;
        ScenePlayer drawn = Sampled(timeline, moment, target, playing);

        FreeCamera eye = view.Eye(moment, 1f).ShouldNotBeNull();

        eye.Origin.X.ShouldBe(drawn.X, 0.001f);
        eye.Origin.Y.ShouldBe(drawn.Y, 0.001f);
    }

    /// <summary>The first moment past the middle where the followed player's playing and paused samples are apart.</summary>
    /// <remarks>The control: there, "drawn" and "held" are different answers, so each case can only pass its own way.</remarks>
    private static double MovingMoment(DemoTimeline timeline, SpectatorView view)
    {
        for (int frame = timeline.Frames.Count / 2; frame < timeline.Frames.Count; frame++)
        {
            double moment = timeline.Frames[frame].Tick + 0.5d;

            if (view.Target(timeline.Frames[frame].Tick) is { IsAlive: true } followed)
            {
                ScenePlayer interpolated = Sampled(timeline, moment, followed.EntityIndex, interpolating: true);
                ScenePlayer held = Sampled(timeline, moment, followed.EntityIndex, interpolating: false);

                if (System.MathF.Abs(interpolated.X - held.X) + System.MathF.Abs(interpolated.Y - held.Y) > 1f)
                {
                    return moment;
                }
            }
        }

        Assert.Fail("no moment in the second half where the followed player's drawn and held samples are apart");
        return 0d;
    }

    private static ScenePlayer Sampled(DemoTimeline timeline, double moment, int entity, bool interpolating)
    {
        List<ScenePlayer> players = [];
        timeline.PlayersAt(moment, players, interpolating);

        return players.Find(player => player.EntityIndex == entity);
    }
}
