using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// A corpse's face on a real recording (B513): <c>C_TFRagdoll::SetupWeights</c> draws the dead player's face while the
/// player is dead, and the corpse's own never-set face once the player is alive again (<c>c_tf_player.cpp:630-637</c>).
/// </summary>
/// <remarks>
/// **The subject** is the first corpse in tf2-2026-pub-pov-clean (lcor) still drawn after its player respawned. **The
/// control** is the same moment with the player reported dead, which hands the corpse the player's face instead.
/// </remarks>
public sealed class FaceCorpseDemoTests
{
    private const string Demo = "tf2-2026-pub-pov-clean.dem";

    /// <summary><c>RagdollProps.FirstCorpseEntityIndex</c>: a corpse is drawn under this plus its place in the list.</summary>
    private const int FirstCorpse = 2048;

    [Test]
    public void Instance_ACorpseWhosePlayerRespawned_DrawsItsOwnUnsetFaceNotThePlayers()
    {
        string demo = CommittedDemo.RequireLocal(Demo);
        GameContent content = GameContent.Open(GameInstall.Require(), NullLoggerFactory.Instance);
        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(demo));
        IPlayerAppearance appearance = new PlayerAppearances(NullLogger.Instance) { Timeline = timeline, Game = content }
            .Ensure(NoAppearance.Instance);

        (int tick, int at, string model) = Respawned(timeline, appearance);
        TestContext.Out.WriteLine($"corpse {at} ({model}) of player {timeline.Corpses[at].PlayerIndex} at tick {tick}");

        // A C_BaseFlex nothing set holds each controller at its minimum, which on a class model moves no vertex: the
        // corpse draws the bind face. The player's face rests at zero in range, which half-closes the lids.
        float[]? own = Face(timeline, content, appearance, tick, at, model, forceDead: false);
        float[] players = Face(timeline, content, appearance, tick, at, model, forceDead: true)
            .ShouldNotBeNull("the control: reported dead, the corpse wears the player's face");

        own.ShouldBeNull("the corpse's own face, every controller at its minimum");
        players.Any(delta => delta != 0f).ShouldBeTrue();
    }

    /// <summary>A tick where a class-model corpse is drawn and its player is alive, and the corpse's place.</summary>
    private static (int Tick, int At, string Model) Respawned(DemoTimeline timeline, IPlayerAppearance appearance)
    {
        List<ScenePlayer> players = [];

        for (int at = 0; at < timeline.Corpses.Count; at++)
        {
            SceneRagdoll corpse = timeline.Corpses[at];

            if (corpse.PlayerIndex is not { } player)
            {
                continue;
            }

            for (int tick = corpse.FirstTick + 1; tick < Math.Min(corpse.LastTick, corpse.FirstTick + 600); tick += 15)
            {
                players.Clear();
                timeline.PlayersAt(tick, players);

                if (players.FirstOrDefault(p => p.EntityIndex == player) is { IsAlive: true, PlayerClass: { } cls } &&
                    appearance.ModelOf(cls) is { } model)
                {
                    return (tick + 15, at, model);
                }
            }
        }

        Assert.Inconclusive("no corpse outlives its player's respawn in this recording");
        return default;
    }

    private static float[]? Face(
        DemoTimeline timeline, GameContent content, IPlayerAppearance appearance, int tick, int at, string model, bool forceDead)
    {
        // The corpses are the moment source's, not the timeline's prop walk (RagdollProps.Fill).
        TimelineMoments moments = new(timeline) { ClassModels = () => appearance.ModelOf };
        MapAssets assets = MapCache.Load(entityModels: [model]);
        EntityModelSet models = new() { Geometry = assets.Geometry, IntervalPerTick = timeline.IntervalPerTick };

        MomentScene scene = new(models, new ViewmodelScene(), NullLogger.Instance)
        {
            Weapons = content.Weapons,
            Appearance = appearance,
        };
        List<ScenePlayer> players = [];
        List<SceneProp> props = [];

        timeline.PlayersAt(tick, players);
        moments.PropsAt(tick, props);

        MomentInfo info = new(
            Tick: tick,
            CurrentTick: tick,
            FirstPerson: false,
            Followed: -1,
            EyeCamera: null,
            IntervalPerTick: timeline.IntervalPerTick,
            ViewmodelFieldOfView: 54f,
            Recorder: timeline.RecorderEntityIndex)
        {
            ServerTime = moments.ServerTimeAt(tick),
        };

        scene.Build(players, props, info);

        if (forceDead)
        {
            Func<int, bool?> production = models.PlayerAlive.ShouldNotBeNull();
            models.PlayerAlive = index => production(index) is null ? null : false;
        }

        scene.Pose(info);

        return scene.Instances.Single(instance => instance.EntityIndex == FirstCorpse + at).Flex;
    }
}
