using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// A <c>FLEXANIMATION</c> scene played frame by frame through the production path (B513): the medic's
/// <c>scenes/Player/Medic/low/687.vcd</c>, whose one event is twenty viseme tracks.
/// </summary>
/// <remarks>
/// **The moment is written down, not read from a recording** (D213): medic 12 of demostf-koth_product_final-2026-08-07
/// as `moment-dump … 61145 12` printed him on 2026-10-11 — where he stands, what he holds, the idle scene he has been in
/// since tick 60872, the scene that starts at 61085 and stops at 61353, and the two voice lines that start with it. The
/// scene itself is the install's, resolved from <c>scenes.image</c> by <see cref="PlayerAppearances"/>; it is one of
/// three TF2 player scenes in the archive with a flex animation (<c>flex flexanim</c>). The face is stepped every tick
/// from before the scene's start, so the decay and <c>m_iv_flexWeight</c>'s latched history run as they would. **The
/// control** is the same playback with the scene's flex animations removed.
/// </remarks>
public sealed class FaceFlexAnimationDemoTests
{
    private const string MedicModel = "models/player/medic.mdl";
    private const string FlexScene = "scenes/Player/Medic/low/687.vcd";
    private const int Medic = 12;
    private const int SceneStart = 61085;

    [Test]
    [Category(Tf2DemoSalvage.SdkReference.TestCategories.MutationHeavy)]
    public void Instance_AMedicPlayingAFlexAnimation_MovesVerticesTheControlDoesNot()
    {
        ScenePlayer medic = new(
            EntityIndex: Medic,
            X: -1423f,
            Y: 1020.625f,
            Z: 160f,
            Team: 2,
            Health: 150,
            PlayerClass: 5,
            Yaw: -15.131946f,
            LifeState: 0,
            Flags: 131329,
            EyePitch: 2.4705963f,
            EyeYaw: -15.131958f,
            ActiveWeapon: 501,
            WeaponClass: "CWeaponMedigun",
            WeaponItem: 411,
            ClientSideAnimated: true);

        DemoTimeline timeline = SyntheticMatch.Timeline(
            [medic],
            scenes:
            [
                new SceneChoreography(60872, 516, "scenes/player/medic/low/idleloop01.vcd", [Medic]) { StoppedTick = 61874 },
                new SceneChoreography(SceneStart, 586, FlexScene, [Medic]) { StoppedTick = 61353 },
            ],
            sounds:
            [
                Voice("vo/medic_ThanksForTheTeleporter02.mp3", 3788),
                Voice("vo/medic_ThanksForTheTeleporter03.mp3", 3789),
            ]);

        GameAppearance appearance = SyntheticMatch.Appearance(timeline);
        FaceSources production = appearance.Faces.ShouldNotBeNull();

        appearance.TauntForScene(FlexScene).ShouldNotBeNull().FlexAnimations.Count
            .ShouldBe(1, "the instrument: the scene resolves and carries its flex animation");

        FaceSources without = new(
            timeline.Scenes,
            scene => appearance.TauntForScene(scene) is { } plan ? plan with { FlexAnimations = [] } : null,
            timeline.Sounds,
            timeline.IntervalPerTick,
            new Dictionary<string, Sentence>(),
            production.Expression) { InterpolationSeconds = production.InterpolationSeconds };

        float[] animated = Played(timeline, medic, appearance, SceneStart + 60).ShouldNotBeNull();
        float[]? control = Played(timeline, medic, appearance with { Faces = without }, SceneStart + 60);

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

    /// <summary>One of the two lines the recording starts on the medic's voice channel with the scene.</summary>
    private static SceneSound Voice(string name, int number) =>
        new(SceneStart, name, number, Medic, Channel: 2, Volume: 0.81889766f, SoundLevel: 95, Pitch: 100, DelaySeconds: 0f,
            OriginX: -1464f, OriginY: 856f, OriginZ: 208f);

    /// <summary>The medic's flex stream after playing every tick from before the scene's start up to <paramref name="until"/>.</summary>
    private static float[]? Played(DemoTimeline timeline, ScenePlayer medic, IPlayerAppearance appearance, int until)
    {
        MapAssets assets = MapCache.Load(entityModels: [MedicModel]);
        EntityModelSet models = new() { Geometry = assets.Geometry, IntervalPerTick = timeline.IntervalPerTick };
        MomentScene scene = SyntheticMatch.Scene(models, appearance);
        float[]? face = null;

        for (int tick = SceneStart - 5; tick <= until; tick++)
        {
            MomentInfo info = SyntheticMatch.Info(timeline, tick);

            scene.Build([medic], [], info);
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
