using System.Collections.Generic;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene;

// Namespaced away from `Tf2DemoSalvage.Corpus.Tests.*`, where `Corpus` binds to the namespace.
namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>B425 step 2 on a real recording: the only Dragon's Fury in the measured corpus is someone else's.</summary>
/// <remarks>
/// **`tf2-2026-pub-pov-clean` carries 72 `CTFProjectile_BallOfFire`, all owned by entity 17, and the recorder is 9**
/// (`docs/findings/66-tf2-barely-uses-dynamic-lights.md`). `ClientThink` allocates only for the local player's own
/// fireball (`tf_projectile_dragons_fury.cpp:509`), so the engine walks no light into the world (`R_PushDlights`,
/// `engine.dll` `0x1800d48b0`) and no face is dlight-lit. The control is the same demo with the recorder taken to be 17:
/// the instrument must then find the lights, or its zero means nothing.
/// </remarks>
public sealed class CorpusWorldDynamicLightTests
{
    [Test]
    public void WorldLights_OnAPubWhereOnlyAnotherPlayerFiresTheDragonsFury_ReachNoFace()
    {
        if (Corpus.Demo("tf2-2026-pub-pov-clean") is not { } path)
        {
            Assert.Ignore("tf2-2026-pub-pov-clean.dem is not available");
            return;
        }

        DemoTimeline timeline = TimelineCache.For(path);
        int recorder = timeline.RecorderEntityIndex.ShouldNotBeNull();

        (int fireballs, int pushed) = Walk(timeline, recorder);
        (int _, int control) = Walk(timeline, 17);

        fireballs.ShouldBeGreaterThan(0, "the instrument must see the fireballs");
        control.ShouldBeGreaterThan(0, "with the owner as the recorder the lights must reach the world");
        pushed.ShouldBe(0);
    }

    /// <summary>Every fourth tick through a production <see cref="MomentScene"/>: fireballs seen, and lights the world walk would take.</summary>
    private static (int Fireballs, int Pushed) Walk(DemoTimeline timeline, int recorder)
    {
        MomentScene scene = new(new EntityModelSet(), new ViewmodelScene(), NullLogger.Instance);
        List<SceneProp> props = [];
        int fireballs = 0;
        int pushed = 0;

        // A fireball lives for a fraction of a second, so four ticks cannot step over one.
        for (int tick = timeline.FirstTick; tick <= timeline.LastTick; tick += 4)
        {
            timeline.PropsAt(tick, props);

            bool any = false;

            foreach (SceneProp prop in props)
            {
                any |= prop.ClassName == "CTFProjectile_BallOfFire";
            }

            if (!any)
            {
                continue;
            }

            fireballs++;

            MomentInfo info = new(
                tick, tick, false, null, null, timeline.IntervalPerTick, 54f, Recorder: recorder);

            scene.Build([], props, info);
            scene.Pose(info);

            foreach (DynamicLight light in scene.WorldLights.Dlights)
            {
                if (WorldDynamicLights.Pushes(light, scene.WorldLights.Time))
                {
                    pushed++;
                }
            }
        }

        return (fireballs, pushed);
    }
}
