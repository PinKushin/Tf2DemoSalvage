using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// A corpse's face (B513): <c>C_TFRagdoll::SetupWeights</c> draws the dead player's face while the player is dead, and
/// the corpse's own never-set face once the player is alive again (<c>c_tf_player.cpp:630-637</c>).
/// </summary>
/// <remarks>
/// **The moment is written down, not read from a recording** (D213): the first corpse in tf2-2026-pub-pov-clean still
/// drawn after its player respawned — spy 2, whose ragdoll (entity 978) lies from tick 22138 to 25093 while he is alive
/// again and walking at 22364 — as `moment-dump tf2-2026-pub-pov-clean 22364 2` printed both on 2026-10-11. **The
/// control** is the same moment with the player reported dead, which hands the corpse the player's face instead.
/// </remarks>
public sealed class FaceCorpseDemoTests
{
    private const string SpyModel = "models/player/spy.mdl";
    private const int Spy = 2;
    private const int Tick = 22364;

    /// <summary><c>RagdollProps.FirstCorpseEntityIndex</c>: a corpse is drawn under this plus its place in the list.</summary>
    private const int FirstCorpse = 2048;

    [Test]
    [Category(Tf2DemoSalvage.SdkReference.TestCategories.MutationHeavy)]
    public void Instance_ACorpseWhosePlayerRespawned_DrawsItsOwnUnsetFaceNotThePlayers()
    {
        ScenePlayer spy = new(
            EntityIndex: Spy,
            X: 2264.125f,
            Y: 2422.625f,
            Z: 0f,
            Team: 2,
            Health: 125,
            PlayerClass: 8,
            Yaw: 175.24927f,
            LifeState: 0,
            Flags: 131329,
            ClientSideAnimated: true);

        SceneRagdoll corpse = new(
            EntityIndex: 978,
            Serial: 119,
            PlayerClass: 8,
            Team: 2,
            X: 600f,
            Y: 882.96875f,
            Z: 0.03125f,
            Gib: false,
            Burning: false,
            FeignDeath: false,
            WasDisguised: false,
            FirstTick: 22138,
            LastTick: 25093,
            Yaw: 92.19941f,
            DamageCustom: 6,
            PlayerIndex: Spy,
            OnGround: true,
            Force: (0f, 0f, 0f),
            Velocity: (249.78052f, 28.253418f, 0.25f),
            ForceBone: 0);

        DemoTimeline timeline = SyntheticMatch.Timeline(
            [spy],
            scenes: [new SceneChoreography(22138, 1010, "scenes/player/spy/low/idleloop01.vcd", [Spy]) { StoppedTick = 23140 }],
            corpses: [corpse]);
        GameAppearance appearance = SyntheticMatch.Appearance(timeline);

        spy.IsAlive.ShouldBeTrue("the instrument: the player is alive again while his corpse still lies");
        appearance.ModelOf(8).ShouldBe(SpyModel, StringCompareShould.IgnoreCase);

        // A C_BaseFlex nothing set holds each controller at its minimum, which on a class model moves no vertex: the
        // corpse draws the bind face. The player's face rests at zero in range, which half-closes the lids.
        float[]? own = Face(timeline, spy, appearance, forceDead: false);
        float[] players = Face(timeline, spy, appearance, forceDead: true)
            .ShouldNotBeNull("the control: reported dead, the corpse wears the player's face");

        own.ShouldBeNull("the corpse's own face, every controller at its minimum");
        players.Any(delta => delta != 0f).ShouldBeTrue();
    }

    private static float[]? Face(DemoTimeline timeline, ScenePlayer spy, IPlayerAppearance appearance, bool forceDead)
    {
        // The corpses are the moment source's, not the timeline's prop walk (RagdollProps.Fill).
        TimelineMoments moments = new(timeline) { ClassModels = () => appearance.ModelOf };
        MapAssets assets = MapCache.Load(entityModels: [SpyModel]);
        EntityModelSet models = new() { Geometry = assets.Geometry, IntervalPerTick = timeline.IntervalPerTick };
        MomentScene scene = SyntheticMatch.Scene(models, appearance);
        List<SceneProp> props = [];

        moments.PropsAt(Tick, props);

        // The recording is a point-of-view one, made by player 9.
        MomentInfo info = SyntheticMatch.Info(timeline, Tick, recorder: 9);

        scene.Build([spy], props, info);

        if (forceDead)
        {
            Func<int, bool?> production = models.PlayerAlive.ShouldNotBeNull();
            models.PlayerAlive = index => production(index) is null ? null : false;
        }

        scene.Pose(info);

        return scene.Instances.Single(instance => instance.EntityIndex == FirstCorpse).Flex;
    }
}
