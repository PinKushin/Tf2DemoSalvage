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
/// Lip sync on a real voice line, through the production path to the flex stream (B513): the scene builds the scout's
/// instance with the recording's sounds and the install's sound caches, and the mouth follows the cached sentence.
/// </summary>
/// <remarks>
/// **The subject**: 20130519_0130_cp_granary (lcor), entity 9, whose <c>vo/scout_HeadRight03.wav</c> starts on
/// <c>CHAN_VOICE</c> at tick 58230 (<c>flex lipsync</c>) — one of the 51 sounds TF2's caches carry a sentence for, and the
/// only one played in the 60 lcor demos swept. **The control** is the same moment built with no sentences at all, so the
/// difference is the visemes and nothing else.
/// </remarks>
public sealed class FaceLipSyncDemoTests
{
    private const string Demo = "20130519_0130_cp_granary_red_-----.dem";
    private const string ScoutModel = "models/player/scout.mdl";
    private const int Scout = 9;
    private const int LineStart = 58230;

    [Test]
    public void Instance_AScoutSpeakingACachedLine_MovesItsMouthBeyondTheSameMomentWithoutTheSentence()
    {
        string demo = CommittedDemo.RequireLocal(Demo);
        string folder = GameInstall.Require();
        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(demo));
        GameContent content = GameContent.Open(folder, NullLoggerFactory.Instance);
        GameAppearance appearance = (GameAppearance)new PlayerAppearances(NullLogger.Instance) { Timeline = timeline, Game = content }
            .Ensure(NoAppearance.Instance);
        FaceSources speaking = appearance.Faces.ShouldNotBeNull();
        FaceSources silent = new(
            timeline.Scenes, appearance.TauntForScene, timeline.Sounds, timeline.IntervalPerTick,
            new Dictionary<string, Sentence>(), speaking.Expression);

        int tick = LineStart + 40;
        speaking.Voices(Scout, tick * timeline.IntervalPerTick).ShouldHaveSingleItem("the line is on the scout's mouth");

        (int movedSpeaking, float largestSpeaking, float[] withLine) = Face(timeline, content, appearance, tick);
        (int movedSilent, float largestSilent, float[] without) = Face(timeline, content, appearance with { Faces = silent }, tick);

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
        DemoTimeline timeline, GameContent content, IPlayerAppearance appearance, int tick)
    {
        TimelineMoments moments = new(timeline);
        MapAssets assets = MapCache.Load(entityModels: [ScoutModel]);
        EntityModelSet models = new() { Geometry = assets.Geometry, IntervalPerTick = timeline.IntervalPerTick };

        MomentScene scene = new(models, new ViewmodelScene(), NullLogger.Instance)
        {
            Weapons = content.Weapons,
            Appearance = appearance,
        };
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
