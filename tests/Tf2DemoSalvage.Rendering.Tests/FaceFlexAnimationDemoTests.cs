using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene.Tests;

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
    /// **The range the renderer sends holds every slot that moves** (B597): the renderer uploads only
    /// <see cref="ModelInstance.FlexRange"/> and keeps zeros on the device either side, so a non-zero float outside it
    /// would never be drawn. Every frame of the medic's flex animation, each moving slot must lie inside, and the range
    /// must be the model's first to last flexed slot (<see cref="EntityModelSet.FlexedSlotsOf"/>).
    /// </summary>
    [Test]
    [Category(Tf2DemoSalvage.SdkReference.TestCategories.MutationHeavy)]
    public void FlexRange_AMedicPlayingAFlexAnimation_HoldsEverySlotThatMoves()
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
        int faces = 0;
        int widest = 0;

        for (int tick = SceneStart - 5; tick <= SceneStart + 60; tick++)
        {
            MomentInfo info = SyntheticMatch.Info(timeline, tick);
            scene.Build([medic], [], info);
            scene.Pose(info);

            ModelInstance instance = scene.Instances.Single(instance => instance.EntityIndex == Medic &&
                string.Equals(instance.ModelPath, MedicModel, StringComparison.OrdinalIgnoreCase));

            if (instance.Flex is not { } face)
            {
                continue;
            }

            FlexSlots range = instance.FlexRange.ShouldNotBeNull("a scene face says which slots can move");
            (FlexedCorners corners, int slots) = models.FlexedSlotsOf(MedicModel).ShouldNotBeNull();

            range.ShouldBe(new FlexSlots(corners.At[0], corners.At[^1] - corners.At[0] + 1), "the first to the last flexed slot");
            (face.Length / 6).ShouldBe(slots);

            int first = -1;
            int last = -1;

            for (int slot = 0; slot < slots; slot++)
            {
                if (face.AsSpan(slot * 6, 6).IndexOfAnyExcept(0f) >= 0)
                {
                    first = first < 0 ? slot : first;
                    last = slot;
                }
            }

            if (first >= 0)
            {
                first.ShouldBeGreaterThanOrEqualTo(range.First, $"tick {tick}");
                last.ShouldBeLessThan(range.First + range.Count, $"tick {tick}");
                widest = Math.Max(widest, last - first + 1);
            }

            faces++;
        }

        TestContext.Out.WriteLine($"{faces} faces; the widest moving span {widest} slots of the range");
        faces.ShouldBeGreaterThan(60, "the control: the medic wears a face every frame");
        widest.ShouldBeGreaterThan(1000, "the control: the face moves across much of the range");
    }

    /// <summary>
    /// **The flex counters add up on the production path**: one medic, drawn every tick of a talking scene, is one own face
    /// a draw, each either a hit or a miss, and a moving face walks vertices. The instrument the flex-cost measurement reads
    /// (docs/measurements/2026-10-30-flex-for-cost.md).
    /// </summary>
    [Test]
    [Category(Tf2DemoSalvage.SdkReference.TestCategories.MutationHeavy)]
    public void FlexCounters_AMedicPlayingAFlexAnimation_CountEachDrawOnce()
    {
        ScenePlayer medic = new(
            EntityIndex: Medic, X: -1423f, Y: 1020.625f, Z: 160f, Team: 2, Health: 150, PlayerClass: 5,
            Yaw: -15.131946f, LifeState: 0, Flags: 131329, EyePitch: 2.4705963f, EyeYaw: -15.131958f,
            ActiveWeapon: 501, WeaponClass: "CWeaponMedigun", WeaponItem: 411, ClientSideAnimated: true);

        DemoTimeline timeline = SyntheticMatch.Timeline(
            [medic],
            scenes: [new SceneChoreography(SceneStart, 586, FlexScene, [Medic]) { StoppedTick = 61353 }],
            sounds: []);

        GameAppearance appearance = SyntheticMatch.Appearance(timeline);
        MapAssets assets = MapCache.Load(entityModels: [MedicModel]);
        EntityModelSet models = new() { Geometry = assets.Geometry, IntervalPerTick = timeline.IntervalPerTick };
        MomentScene scene = SyntheticMatch.Scene(models, appearance);
        FlexCounters before = models.Counters.Flex;
        int drawn = 0;

        for (int tick = SceneStart; tick < SceneStart + 40; tick++)
        {
            MomentInfo info = SyntheticMatch.Info(timeline, tick);
            scene.Build([medic], [], info);
            scene.Pose(info);
            drawn += scene.Instances.Count(instance => instance.EntityIndex == Medic &&
                string.Equals(instance.ModelPath, MedicModel, StringComparison.OrdinalIgnoreCase));
        }

        FlexCounters flex = models.Counters.Flex.Since(before);
        TestContext.Out.WriteLine(flex.ToString());

        drawn.ShouldBe(40, "the control: the medic is drawn every tick");
        flex.Own.ShouldBe(drawn, "one own face a draw");
        (flex.Hits + flex.Misses).ShouldBe(flex.Own + flex.Worn + flex.Corpse, "each driven face is a hit or a miss");
        flex.Faces.ShouldBe(flex.Own + flex.Worn + flex.Corpse + flex.RestingFaces, "each face is driven or resting");
        flex.Misses.ShouldBeGreaterThan(0, "a talking face changes");
        flex.Vertices.ShouldBeGreaterThan(0, "a changing face walks its flexed vertices");
    }

    /// <summary>
    /// **Through <c>FlexFor</c> itself, the face is byte-identical to the code before the flex-cost rewrite**
    /// (docs/measurements/2026-10-30-flex-for-cost-after.md): the medic plays his flex scene, turns into a scout mid-way
    /// (a model switch, the two sharing the set's one scratch), and plays on, posed on both halves. Each frame's stream is
    /// compared with <see cref="FaceFlexReference"/> run on the weights <c>FlexFor</c> used, carried out of the set.
    /// </summary>
    [Test]
    [Category(Tf2DemoSalvage.SdkReference.TestCategories.MutationHeavy)]
    public void Instance_AFaceAcrossAModelSwitchOnBothHalves_IsByteIdenticalToTheReference()
    {
        const string ScoutModel = "models/player/scout.mdl";
        const int Switch = SceneStart + 30;

        ScenePlayer medic = new(
            EntityIndex: Medic, X: -1423f, Y: 1020.625f, Z: 160f, Team: 2, Health: 150, PlayerClass: 5,
            Yaw: -15.131946f, LifeState: 0, Flags: 131329, EyePitch: 2.4705963f, EyeYaw: -15.131958f,
            ActiveWeapon: 501, WeaponClass: "CWeaponMedigun", WeaponItem: 411, ClientSideAnimated: true);
        ScenePlayer scout = medic with { PlayerClass = 1, Health = 125, WeaponClass = "CTFScatterGun", WeaponItem = 13 };

        DemoTimeline timeline = SyntheticMatch.Timeline(
            [medic],
            scenes:
            [
                new SceneChoreography(60872, 516, "scenes/player/medic/low/idleloop01.vcd", [Medic]) { StoppedTick = 61874 },
                new SceneChoreography(SceneStart, 586, FlexScene, [Medic]) { StoppedTick = 61353 },
                new SceneChoreography(Switch, 517, "scenes/player/scout/low/idleloop01.vcd", [Medic]) { StoppedTick = 61874 },
            ],
            sounds: []);

        GameAppearance appearance = SyntheticMatch.Appearance(timeline);
        MapAssets assets = MapCache.Load(entityModels: [MedicModel, ScoutModel]);
        EntityModelSet models = new() { Geometry = assets.Geometry, IntervalPerTick = timeline.IntervalPerTick };
        MomentScene scene = SyntheticMatch.Scene(models, appearance);

        // Every frame handed to the draw, as the viewer's device counts them, so the poses alternate halves.
        long handOffs = 0;
        scene.HandOffs = () => handOffs;

        float[] positions = [];
        float[] normals = [];
        Dictionary<(int Half, string Model), float[]> streams = [];
        HashSet<int> halves = [];
        HashSet<string> faced = new(StringComparer.OrdinalIgnoreCase);
        int compared = 0;
        int rechecked = 0;
        (float[] Face, int[] Bits, int Tick)? previous = null;

        for (int tick = SceneStart - 5; tick <= Switch + 40; tick++)
        {
            MomentInfo info = SyntheticMatch.Info(timeline, tick);
            scene.Build([tick < Switch ? medic : scout], [], info);
            scene.Pose(info);

            ModelInstance instance = scene.Instances.Single(instance => instance.EntityIndex == Medic &&
                instance.ModelPath.StartsWith("models/player/", StringComparison.OrdinalIgnoreCase) &&
                instance.ModelPath.IndexOf('/', "models/player/".Length) < 0);

            int half = models.PoseHalf;
            (float[] weights, float[] delayed) = models.LastFaceWeights(half, Medic).ShouldNotBeNull($"tick {tick}");
            PropModels.ModelFrames frames = models.Geometry(instance.ModelPath).ShouldNotBeNull();
            FlexedCorners corners = models.FlexedSlotsOf(instance.ModelPath).ShouldNotBeNull().Corners;

            // The reference's own scratch, grown only, as the set's is; its own stream per half and model, from zero.
            if (positions.Length < frames.VertexCount * 3)
            {
                (positions, normals) = (new float[frames.VertexCount * 3], new float[frames.VertexCount * 3]);
            }

            if (!streams.TryGetValue((half, instance.ModelPath), out float[]? stream))
            {
                stream = new float[(corners.MaxAt + 1) * 6];
                streams[(half, instance.ModelPath)] = stream;
            }

            int applied = FaceFlexReference.Face(frames.Flex, weights, delayed, positions, normals, corners, stream);

            // The previous frame's face, posed on the other half, must still hold what it held: the draw of that frame
            // reads it while this one is posed, so a stream shared between the halves would show here.
            if (previous is { } held)
            {
                MemoryMarshal.Cast<float, int>(held.Face).ToArray().ShouldBe(held.Bits, $"tick {held.Tick} after posing {tick}");
            }

            previous = null;

            if (applied > 0)
            {
                float[] face = instance.Flex.ShouldNotBeNull($"tick {tick}");
                MemoryMarshal.Cast<float, int>(face.AsSpan(0, stream.Length)).ToArray()
                    .ShouldBe(MemoryMarshal.Cast<float, int>(stream).ToArray(), $"tick {tick}");
                face.AsSpan(stream.Length).IndexOfAnyExcept(0f).ShouldBe(-1, $"tick {tick}: past the last flexed slot");
                faced.Add(instance.ModelPath);
                previous = (face, MemoryMarshal.Cast<float, int>(face).ToArray(), tick);
                rechecked++;
            }
            else
            {
                instance.Flex.ShouldBeNull($"tick {tick}");
            }

            halves.Add(half);
            compared++;
            handOffs++;
        }

        compared.ShouldBe(76, "the control: every frame was compared");
        rechecked.ShouldBeGreaterThan(60, "the control: most faces were checked again after the next pose");
        halves.Count.ShouldBe(2, "the control: both pose halves were posed");
        faced.ShouldBe([MedicModel, ScoutModel], ignoreOrder: true, "the control: both models wore a moving face");
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
