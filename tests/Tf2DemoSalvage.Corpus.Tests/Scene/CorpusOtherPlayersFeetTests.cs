using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// A real match's players, their feet turned per frame through the production chain (B750).
/// </summary>
/// <remarks>
/// **The output-level assertion** (<c>docs/memory/output-level-assertion-or-it-is-not-done.md</c>): the bodies
/// <c>TimelineMoments</c> hands the scene, frame by frame. `ComputePoseParam_AimYaw` draws the body at the feet and twists
/// by <c>-(eye - feet)</c> (<c>multiplayer_animstate.cpp:1765-1772</c>), and a player standing while he looks about keeps
/// his feet planted, so on a real match some body stands apart from its eyes. Drawn at the pose's yaw, which is the eyes,
/// none ever did. A SourceTV match from lcor, because the era specimens are the owner's solo recordings and never turn on
/// the spot; it skips without it.
/// </remarks>
public sealed class CorpusOtherPlayersFeetTests
{
    private const string Match = "serveme-627619-stv-2026-08-07";

    [Test]
    public void PlayersAt_FramesOfARealMatch_DrawEachBodyAtItsFeetAndTwistFromThem()
    {
        DemoTimeline timeline = TimelineCache.For(Corpus.Demo(Match));
        TimelineMoments moments = new(timeline);
        int start = timeline.FirstTick + ((timeline.LastTick - timeline.FirstTick) / 2);
        List<ScenePlayer> players = [];
        int apart = 0;
        int drawn = 0;

        // Five seconds of frames a quarter of a tick apart.
        for (double moment = start; moment < start + 333; moment += 0.25)
        {
            moments.PlayersAt(moment, players);

            foreach (ScenePlayer player in players)
            {
                if (!player.IsAlive || player.EyeYaw is not { } eyes)
                {
                    continue;
                }

                drawn++;
                float twist = MathF.IEEERemainder(eyes - player.Yaw, 360f);

                player.AimYaw.ShouldNotBeNull().ShouldBe(-twist, 1e-3f, $"entity {player.EntityIndex} at {moment}");

                if (MathF.Abs(twist) > 1f)
                {
                    apart++;
                }
            }
        }

        drawn.ShouldBeGreaterThan(1000, "the control: living players were drawn");
        apart.ShouldBeGreaterThan(0, "a body standing apart from its eyes: planted feet under a turning look");
    }
}
