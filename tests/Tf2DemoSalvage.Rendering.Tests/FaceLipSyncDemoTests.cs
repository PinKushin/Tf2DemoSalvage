using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// Lip sync on a voice line, through the production path to the flex stream (B513): the scene builds the scout's
/// instance with the moment's sounds and the install's sound caches, and the mouth follows the cached sentence.
/// </summary>
/// <remarks>
/// **The moment is written down, not read from a recording** (D213): scout 9 of 20130519_0130_cp_granary, whose
/// <c>vo/scout_HeadRight03.wav</c> starts on <c>CHAN_VOICE</c> at tick 58230, with the four scenes he is in at tick
/// 58270, as `moment-dump … 58270 9` printed them on 2026-10-11. The sentence is the install's: the line is one of the 51
/// sounds TF2's caches carry one for (<c>flex lipsync</c>), and was the only one played in the 60 lcor demos swept.
/// **The control** is the same moment built with no sentences at all, so the difference is the visemes and nothing else.
/// </remarks>
public sealed class FaceLipSyncDemoTests
{
    private const string ScoutModel = "models/player/scout.mdl";
    private const int Scout = 9;
    private const int LineStart = 58230;

    [Test]
    [Category(Tf2DemoSalvage.SdkReference.TestCategories.MutationHeavy)]
    public void Instance_AScoutSpeakingACachedLine_MovesItsMouthBeyondTheSameMomentWithoutTheSentence()
    {
        ScenePlayer scout = new(
            EntityIndex: Scout,
            X: -1347.375f,
            Y: 2014.625f,
            Z: -369.625f,
            Team: 3,
            Health: 157,
            PlayerClass: 1,
            Yaw: -130.55719f,
            LifeState: 0,
            Flags: 256,
            ClientSideAnimated: true);

        DemoTimeline timeline = SyntheticMatch.Timeline(
            [scout],
            scenes:
            [
                new SceneChoreography(0, 773, "scenes/player/scout/low/idleloopHurt01.vcd", [Scout]),
                new SceneChoreography(56890, 619, "scenes/Player/Scout/low/2765.vcd", [Scout]),
                new SceneChoreography(58097, 504, "scenes/player/scout/low/idleloop01.vcd", [Scout]) { StoppedTick = 59207 },
                new SceneChoreography(LineStart, 494, "scenes/Player/Scout/low/403.vcd", [Scout]) { StoppedTick = 58302 },
            ],
            sounds:
            [
                new SceneSound(
                    LineStart, "vo/scout_HeadRight03.wav", 1555, Scout, Channel: 2, Volume: 0.81889766f, SoundLevel: 95,
                    Pitch: 100, DelaySeconds: 0f, OriginX: -1224f, OriginY: 2128f, OriginZ: -368f),
            ]);

        GameAppearance appearance = SyntheticMatch.Appearance(timeline);
        FaceSources speaking = appearance.Faces.ShouldNotBeNull();
        FaceSources silent = new(
            timeline.Scenes, appearance.TauntForScene, timeline.Sounds, timeline.IntervalPerTick,
            new Dictionary<string, Sentence>(), speaking.Expression);

        int tick = LineStart + 40;
        speaking.Voices(Scout, tick * timeline.IntervalPerTick).ShouldHaveSingleItem("the line is on the scout's mouth");

        (int movedSpeaking, float largestSpeaking, float[] withLine) = Face(timeline, scout, appearance, tick);
        (int movedSilent, float largestSilent, float[] without) = Face(timeline, scout, appearance with { Faces = silent }, tick);

        int changed = 0;

        // A face that moves nothing has no stream: read it as zeros.
        float At(int at) => at < without.Length ? without[at] : 0f;

        for (int at = 0; at < withLine.Length; at += 6)
        {
            float dx = withLine[at] - At(at), dy = withLine[at + 1] - At(at + 1), dz = withLine[at + 2] - At(at + 2);
            changed += MathF.Sqrt((dx * dx) + (dy * dy) + (dz * dz)) > 0.05f ? 1 : 0;
        }

        TestContext.Out.WriteLine(
            $"scout at {tick}: speaking {movedSpeaking} vertices, furthest {largestSpeaking:0.###}; " +
            $"without the sentence {movedSilent}, {largestSilent:0.###}; {changed} differ");

        changed.ShouldBeGreaterThan(100, "the visemes move the mouth");
    }

    private static (int Moved, float Largest, float[] Face) Face(
        DemoTimeline timeline, ScenePlayer scout, IPlayerAppearance appearance, int tick)
    {
        MapAssets assets = MapCache.Load(entityModels: [ScoutModel]);
        EntityModelSet models = new() { Geometry = assets.Geometry, IntervalPerTick = timeline.IntervalPerTick };
        MomentScene scene = SyntheticMatch.Scene(models, appearance);
        MomentInfo info = SyntheticMatch.Info(timeline, tick);

        scene.Build([scout], [], info);
        scene.Pose(info);

        float[] face = scene.Instances.Single(
            instance => instance.EntityIndex == Scout &&
                string.Equals(instance.ModelPath, ScoutModel, StringComparison.OrdinalIgnoreCase)).Flex ?? [];

        float largest = 0f;
        int moved = 0;

        for (int at = 0; at < face.Length; at += 6)
        {
            float length = MathF.Sqrt((face[at] * face[at]) + (face[at + 1] * face[at + 1]) + (face[at + 2] * face[at + 2]));
            largest = MathF.Max(largest, length);
            moved += length > 0.05f ? 1 : 0;
        }

        return (moved, largest, face);
    }
}
