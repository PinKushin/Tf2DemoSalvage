using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// A <c>FLEXANIMATION</c> scene on a real recording, played frame by frame through the production path (B513): the
/// medic's <c>scenes/Player/Medic/low/687.vcd</c>, whose one event is twenty viseme tracks.
/// </summary>
/// <remarks>
/// **The subject**: demostf-koth_product_final-2026-08-07 (lcor), medic 12, scene from tick 61085 to 61353 (<c>flex
/// played</c>) — one of three TF2 player scenes in the archive with a flex animation (<c>flex flexanim</c>). The face is
/// stepped every tick from the scene's start, so the decay and <c>m_iv_flexWeight</c>'s latched history run as they
/// would. **The control** is the same playback with the scene's flex animations removed.
/// </remarks>
public sealed class FaceFlexAnimationDemoTests
{
    private const string Demo = "demostf-koth_product_final-2026-08-07.dem";
    private const string MedicModel = "models/player/medic.mdl";
    private const int Medic = 12;
    private const int SceneStart = 61085;

    [Test]
    public void Instance_AMedicPlayingAFlexAnimation_MovesVerticesTheControlDoesNot()
    {
        string demo = CommittedDemo.RequireLocal(Demo);
        GameContent content = GameContent.Open(GameInstall.Require(), NullLoggerFactory.Instance);
        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(demo));
        GameAppearance appearance = (GameAppearance)new PlayerAppearances(NullLogger.Instance) { Timeline = timeline, Game = content }
            .Ensure(NoAppearance.Instance);
        FaceSources production = appearance.Faces.ShouldNotBeNull();

        appearance.TauntForScene("scenes/Player/Medic/low/687.vcd").ShouldNotBeNull().FlexAnimations.Count
            .ShouldBe(1, "the instrument: the scene resolves and carries its flex animation");

        FaceSources without = new(
            timeline.Scenes,
            scene => appearance.TauntForScene(scene) is { } plan ? plan with { FlexAnimations = [] } : null,
            timeline.Sounds,
            timeline.IntervalPerTick,
            new Dictionary<string, Sentence>(),
            production.Expression) { InterpolationSeconds = production.InterpolationSeconds };

        float[] animated = Played(timeline, content, appearance, SceneStart + 60).ShouldNotBeNull();
        float[]? control = Played(timeline, content, appearance with { Faces = without }, SceneStart + 60);

        int differ = 0;

        for (int at = 0; at < animated.Length; at += 6)
        {
            float dx = animated[at] - (control is null ? 0f : control[at]);
            float dy = animated[at + 1] - (control is null ? 0f : control[at + 1]);
            float dz = animated[at + 2] - (control is null ? 0f : control[at + 2]);
            differ += MathF.Sqrt((dx * dx) + (dy * dy) + (dz * dz)) > 0.05f ? 1 : 0;
        }

        TestContext.Out.WriteLine($"medic at {SceneStart + 60}: {differ} of {animated.Length / 6} buffer vertices differ from the control");
        differ.ShouldBeGreaterThan(100, "the visemes the tracks write move the mouth");
    }

    /// <summary>The medic's flex stream after playing every tick from the scene's start up to <paramref name="until"/>.</summary>
    private static float[]? Played(DemoTimeline timeline, GameContent content, IPlayerAppearance appearance, int until)
    {
        TimelineMoments moments = new(timeline);
        MapAssets assets = MapCache.Load(entityModels: [MedicModel]);
        EntityModelSet models = new() { Geometry = assets.Geometry, IntervalPerTick = timeline.IntervalPerTick };
        MomentScene scene = new(models, new ViewmodelScene(), NullLogger.Instance)
        {
            Weapons = content.Weapons,
            Appearance = appearance,
        };
        float[]? face = null;

        for (int tick = SceneStart - 5; tick <= until; tick++)
        {
            List<ScenePlayer> players = [];
            List<SceneProp> props = [];
            timeline.PlayersAt(tick, players);
            timeline.PropsAt(tick, props);

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
            scene.Pose(info);

            foreach (ModelInstance instance in scene.Instances.Where(instance => instance.EntityIndex == Medic &&
                string.Equals(instance.ModelPath, MedicModel, StringComparison.OrdinalIgnoreCase)))
            {
                face = instance.Flex;
            }
        }

        return face;
    }
}
