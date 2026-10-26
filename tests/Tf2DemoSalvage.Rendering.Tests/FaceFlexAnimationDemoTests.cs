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

    /// <summary>
    /// **The stamp the renderer skips an upload by** (B597): a face array whose <see cref="ModelInstance.FlexWritten"/> is
    /// unchanged since its last draw must hold the same floats, and one whose floats changed must carry a new stamp —
    /// the renderer keeps each array's last upload and re-sends it only on a new stamp, so a missed bump draws a stale face.
    /// </summary>
    /// <remarks>
    /// Every tick is posed twice, the second time at the same tick: a render frame faster than the simulation, which is
    /// the case the skip exists for. Compared frame to frame on the instance the renderer is handed.
    /// </remarks>
    [Test]
    [Category(Tf2DemoSalvage.SdkReference.TestCategories.MutationHeavy)]
    public void FlexWritten_AMedicPlayingAFlexAnimation_ChangesExactlyWhenItsFaceDoes()
    {
        ScenePlayer medic = new(
            EntityIndex: Medic, X: -1423f, Y: 1020.625f, Z: 160f, Team: 2, Health: 150, PlayerClass: 5,
            Yaw: -15.131946f, LifeState: 0, Flags: 131329, EyePitch: 2.4705963f, EyeYaw: -15.131958f,
            ActiveWeapon: 501, WeaponClass: "CWeaponMedigun", WeaponItem: 411, ClientSideAnimated: true);

        DemoTimeline timeline = SyntheticMatch.Timeline(
            [medic],
            scenes:
            [
                new SceneChoreography(60872, 516, "scenes/player/medic/low/idleloop01.vcd", [Medic]) { StoppedTick = 61874 },
                new SceneChoreography(SceneStart, 586, FlexScene, [Medic]) { StoppedTick = 61353 },
            ],
            sounds: []);

        GameAppearance appearance = SyntheticMatch.Appearance(timeline);
        MapAssets assets = MapCache.Load(entityModels: [MedicModel]);
        EntityModelSet models = new() { Geometry = assets.Geometry, IntervalPerTick = timeline.IntervalPerTick };
        MomentScene scene = SyntheticMatch.Scene(models, appearance);

        float[]? array = null;
        float[]? previous = null;
        long previousWritten = 0;
        int held = 0;
        int changed = 0;

        for (int tick = SceneStart - 5; tick <= SceneStart + 60; tick++)
        {
            MomentInfo info = SyntheticMatch.Info(timeline, tick);
            scene.Build([medic], [], info);

            for (int again = 0; again < 2; again++)
            {
                scene.Pose(info);

                ModelInstance instance = scene.Instances.Single(instance => instance.EntityIndex == Medic &&
                    string.Equals(instance.ModelPath, MedicModel, StringComparison.OrdinalIgnoreCase));

                if (instance.Flex is not { } face)
                {
                    continue;
                }

                instance.FlexWritten.ShouldNotBe(0L, "a face the scene wrote carries a stamp; 0 is the always-upload default");

                if (previous is not null && ReferenceEquals(face, array))
                {
                    bool same = face.AsSpan().SequenceEqual(previous);

                    if (instance.FlexWritten == previousWritten)
                    {
                        same.ShouldBeTrue($"tick {tick}: the stamp held, so the floats must have");
                        held++;
                    }
                    else if (!same)
                    {
                        changed++;
                    }
                }

                // A copy of the floats as they stood: the scene rewrites the array itself in place.
                previous = [.. face];
                array = face;
                previousWritten = instance.FlexWritten;
            }
        }

        TestContext.Out.WriteLine($"{held} frames held the stamp and their floats, {changed} changed both");
        held.ShouldBeGreaterThan(0, "the control: some frame holds its face, or the skip is never exercised");
        changed.ShouldBeGreaterThan(10, "the control: the flex animation moves the face");
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
