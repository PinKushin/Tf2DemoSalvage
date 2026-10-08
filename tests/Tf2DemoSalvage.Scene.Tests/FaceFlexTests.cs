using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// A player's face — <c>SetupGlobalWeights</c> as it runs for a TF player — on hand-built tables with known answers
/// (D38, B513).
/// </summary>
/// <remarks>
/// Two controllers — <c>smile</c> 0..1 and <c>lid</c> −1..1 — one rule copying <c>smile</c> into descriptor 0, and one
/// flex moving vertex 1 by (2, 0, 0) with a normal delta of (0, 0, 1).
/// </remarks>
public sealed class FaceFlexTests
{
    private static readonly StudioFlexData Model = new(
        ["smile"],
        [new("default", "smile", 0f, 1f), new("default", "lid", -1f, 1f)],
        [new StudioFlexRule(0, [new StudioFlexOp(2, 0, 0f)])],
        [new StudioMeshFlex(0, 0, 0f, 1f, 10f, 11f, [new StudioVertAnim(1, 255, 0, (2f, 0f, 0f), (0f, 0f, 1f))])]);

    [Test]
    public void ActorFace_New_RestsEachControllerAtZeroInItsOwnRange()
    {
        // ResetFlexWeights: SetFlexWeight( i, 0 ) — normalised, so the −1..1 lid stores 0.5 and reads 0.
        ActorFace face = new(Model, "models/player/x.mdl", null);
        face.Step(0d, 1, [], []);

        face.FlexWeight(1).ShouldBe(0.5f);
        face.Global["smile"].ShouldBe(0f);
        face.Global["lid"].ShouldBe(0f);
    }

    [Test]
    public void Step_AnEventStartingNow_WaitsAFrame()
    {
        // EventThink tests the PREVIOUS frame's scene time: at 1.0 the last frame was 0.98, so it starts at 1.02.
        ActorFace face = new(Model, "m.mdl", null);
        List<FaceScene> scenes = [new(Scene(Animation(1f, 4f, "smile")), 0d, null)];

        face.Step(0.98d, 1, scenes, []);
        face.Step(1.0d, 1, scenes, []);
        face.Global["smile"].ShouldBe(0f);

        face.Step(1.02d, 1, scenes, []);
        face.Global["smile"].ShouldBe(1f);
    }

    [Test]
    public void Step_AHalfWeightFlexAnimation_BlendsOverTheDecayedValue()
    {
        // Weight 0.5 toward 1: frame one 0 → 0.5; frame two decays first, 0.475, then 0.475 × 0.5 + 0.5 = 0.7375
        // (0.75 without the decay, c_baseflex.cpp:1703).
        ActorFace face = new(Model, "m.mdl", null);
        List<FaceScene> scenes = [new(Scene(Animation(0f, 4f, "smile", 0.5f)), 0d, null)];

        face.Step(1d, 1, scenes, []);
        face.Global["smile"].ShouldBe(0.5f, 1e-6f);

        face.Step(2d, 1, scenes, []);
        face.Global["smile"].ShouldBe(0.7375f, 1e-6f);
    }

    [Test]
    public void Step_ASecondDrawerInOneFrame_DecaysAndBlendsAgain()
    {
        // A worn item calls the wearer's SetupGlobalWeights too (econ_entity.cpp:1377), and each call decays; the
        // same drawer twice in a frame is one call.
        ActorFace face = new(Model, "m.mdl", null);
        List<FaceScene> scenes = [new(Scene(Animation(0f, 4f, "smile", 0.5f)), 0d, null)];

        face.Step(1d, 1, scenes, []);
        face.Step(1d, 1, scenes, []);
        face.Global["smile"].ShouldBe(0.5f, 1e-6f);

        face.Step(1d, 2, scenes, []);
        face.Global["smile"].ShouldBe(0.7375f, 1e-6f);
    }

    [Test]
    public void Step_ATrackNamingAControllerTheModelLacks_WritesControllerZero()
    {
        // MAX( FindFlexController( name ), 0 ), c_baseflex.cpp:1987.
        ActorFace face = new(Model, "m.mdl", null);
        List<FaceScene> scenes = [new(Scene(Animation(0f, 4f, "jaw_drop")), 0d, null)];

        face.Step(0.5d, 1, scenes, []);

        face.Global["smile"].ShouldBe(1f);
    }

    [Test]
    public void Step_TwoScenesAtOnce_BothReachTheFace()
    {
        ActorFace face = new(Model, "m.mdl", null);
        List<FaceScene> scenes =
        [
            new(Scene(Expression(0f, 4f, ("smile", 0.8f, 1f))), 0d, null),
            new(Scene(Expression(0f, 4f, ("lid", 0.25f, 1f))), 1d, null),
        ];

        face.Step(2d, 1, scenes, []);

        face.Global["smile"].ShouldBe(0.8f);
        face.Global["lid"].ShouldBe(0.25f);
    }

    [Test]
    public void Step_ASentenceBeingSpoken_AddsItsViseme()
    {
        // Phoneme 0 over the whole second, neutral emphasis: the normal class at amount 2 × 0.5 = 1, fully covering
        // the 0.08 s filter window, so the viseme's smile 0.6 is added once.
        FaceSources sources = new([], _ => null, [], 0.015, new Dictionary<string, Sentence>(), name => name == "phonemes" ? Phonemes() : null);
        ActorFace face = new(Model, "models/player/x.mdl", sources);
        Sentence sentence = new([new SentencePhoneme(0, 0f, 1f)], [], 22050, 22050);

        face.Step(0.5d, 1, [], [new FaceVoice(sentence, 0d, IgnorePhonemes: false)]);

        face.Global["smile"].ShouldBe(0.6f, 1e-6f);
    }

    [Test]
    public void Step_ASoundFlaggedIgnorePhonemes_MovesNoMouth()
    {
        FaceSources sources = new([], _ => null, [], 0.015, new Dictionary<string, Sentence>(), name => name == "phonemes" ? Phonemes() : null);
        ActorFace face = new(Model, "models/player/x.mdl", sources);
        Sentence sentence = new([new SentencePhoneme(0, 0f, 1f)], [], 22050, 22050);

        face.Step(0.5d, 1, [], [new FaceVoice(sentence, 0d, IgnorePhonemes: true)]);

        face.Global["smile"].ShouldBe(0f);
    }

    [Test]
    public void Delay_TheFirstFrame_BlendsNothing()
    {
        float[] delayed = [0f];
        double time = 0d;

        FaceFlex.Delay([1f], delayed, ref time, 1d, 0.033d);

        delayed[0].ShouldBe(0f);
        time.ShouldBe(1d);
    }

    [Test]
    public void Delay_OneFrameOf33Milliseconds_MovesAFifthOfTheWay()
    {
        // ExponentialDecay( 0.8, 0.033, 0.033 ) = 0.8: delayed = 0 × 0.8 + 1 × 0.2.
        float[] delayed = [0f];
        double time = 1d;

        FaceFlex.Delay([1f], delayed, ref time, 1.033d, 0.033d);

        delayed[0].ShouldBe(0.2f, 1e-5f);
    }

    [Test]
    public void Local_AControllerTheActorNeverSet_ReadsZero() =>
        FaceFlex.Local(Model, new Dictionary<string, float> { ["smile"] = 0.3f }).ShouldBe([0.3f, 0f]);

    [Test]
    public void Deltas_ASmile_MovesItsVertexByTheWeight()
    {
        (float[] positions, float[] normals) = FaceFlex.Deltas(Model, [0.5f], [0.5f], vertexCount: 2).ShouldNotBeNull();

        positions.ShouldBe([0f, 0f, 0f, 1f, 0f, 0f]);
        normals.ShouldBe([0f, 0f, 0f, 0f, 0f, 0.5f]);
    }

    [Test]
    public void Deltas_NoWeight_IsNull() => FaceFlex.Deltas(Model, [0f], [0f], vertexCount: 2).ShouldBeNull();

    [Test]
    public void InBufferOrder_EachBufferVertex_TakesItsCornersVertexsDeltas()
    {
        // Corners 0, 1, 2 are .vvd vertices 1, 0, 1; the buffer holds corners 2, 0, 1.
        float[] stream = FaceFlex.InBufferOrder(([0f, 0f, 0f, 1f, 2f, 3f], [0f, 0f, 0f, 4f, 5f, 6f]), [1, 0, 1], [2, 0, 1]);

        stream.ShouldBe([1f, 2f, 3f, 4f, 5f, 6f, 1f, 2f, 3f, 4f, 5f, 6f, 0f, 0f, 0f, 0f, 0f, 0f]);
    }

    private static SceneTaunt Scene(SceneExpression expression) =>
        new([], -1f, -1f) { Expressions = [expression], Duration = 5f };

    private static SceneTaunt Scene(SceneFlexAnimation animation) =>
        new([], -1f, -1f) { FlexAnimations = [animation], Duration = 5f };

    /// <summary>A flex animation holding one controller at 1 throughout.</summary>
    private static SceneFlexAnimation Animation(float start, float end, string controller, float? ramp = null) =>
        new(start, end, ramp is { } r ? [new(0f, r), new(1f, r), new(2f, r), new(3f, r), new(4f, r)] : [], [new SceneFlexTrack(controller, true, false, 0f, 1f,
            [new SceneFlexSample(0f, 1f, 0), new SceneFlexSample((end - start) / 2f, 1f, 0), new SceneFlexSample(end - start, 1f, 0)],
            [])]);

    private static SceneExpression Expression(float start, float end, params (string Name, float Weight, float Influence)[] weights) =>
        new(start, end, [], Array.ConvertAll(weights, w => new SceneExpressionWeight(w.Name, w.Weight, w.Influence)));

    /// <summary>A phoneme file: one setting, smile 0.6, at phoneme code 0 through the index table.</summary>
    private static FlexSettings Phonemes()
    {
        byte[] file = new byte[1024];
        Span<byte> s = file;
        Encoding.ASCII.GetBytes("aa\0smile\0").CopyTo(s[500..]);

        Int(s, 76, 1);
        Int(s, 80, 200);
        Int(s, 88, 1);
        Int(s, 92, 600);
        Int(s, 600, 0);
        Int(s, 96, 1);
        Int(s, 100, 400);
        Int(s, 400, 503);
        Int(s, 200, 500 - 200);
        Int(s, 208, 1);
        Int(s, 220, 300 - 200);
        Int(s, 300, 0);
        BinaryPrimitives.WriteSingleLittleEndian(s[304..], 0.6f);
        BinaryPrimitives.WriteSingleLittleEndian(s[308..], 1f);

        return FlexSettings.Read(file);
    }

    private static void Int(Span<byte> s, int at, int value) => BinaryPrimitives.WriteInt32LittleEndian(s[at..], value);
}
