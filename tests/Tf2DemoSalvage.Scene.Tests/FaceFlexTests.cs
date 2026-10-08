using System;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// A player's face from a scene's expressions, on hand-built tables with known answers (D38, B513).
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
    public void Controllers_NoScene_IsEachControllersMinimum() =>
        FaceFlex.Controllers(Model, null, 0f).ShouldBe([0f, -1f]);

    [Test]
    public void Controllers_AnActiveExpression_BlendsItsWeightByInfluenceTimesIntensity()
    {
        // No ramp: full intensity. Influence 0.5 pulls smile half way from 0 toward 0.8; lid fully to 0.25.
        float[] src = FaceFlex.Controllers(Model, Scene(Expression(1f, 3f, ("smile", 0.8f, 0.5f), ("LID", 0.25f, 1f))), 2f);

        src[0].ShouldBe(0.4f, 1e-6f);
        src[1].ShouldBe(0.25f, "the name is matched without case, as the global list does");
    }

    [TestCase(0.99f)]
    [TestCase(3.01f)]
    public void Controllers_OutsideTheEvent_LeavesTheRestingValues(float sceneSeconds) =>
        FaceFlex.Controllers(Model, Scene(Expression(1f, 3f, ("smile", 1f, 1f))), sceneSeconds).ShouldBe([0f, -1f]);

    [Test]
    public void Controllers_TwoExpressions_BlendInOrderEachOverTheLast()
    {
        // 0 → 1 (influence 1), then half way toward 0: 0.5. The second blends over the first, as g_flexweight does.
        float[] src = FaceFlex.Controllers(
            Model, Scene(Expression(0f, 4f, ("smile", 1f, 1f)), Expression(1f, 4f, ("smile", 0f, 0.5f))), 2f);

        src[0].ShouldBe(0.5f);
    }

    [Test]
    public void Controllers_AnUnknownController_IsIgnored() =>
        FaceFlex.Controllers(Model, Scene(Expression(0f, 4f, ("inner_raiser", 1f, 1f))), 2f).ShouldBe([0f, -1f]);

    [Test]
    public void Controllers_ARampAtHalf_HalvesTheInfluence()
    {
        // A linear-looking ramp sampled at its own sample: one sample (1, 0.5) is read exactly there.
        SceneExpression ramped = new(0f, 2f, [new SceneCurveSample(1f, 0.5f)], [new SceneExpressionWeight("smile", 1f, 1f)]);

        FaceFlex.Controllers(Model, Scene(ramped), 1f)[0].ShouldBe(0.5f);
    }

    [Test]
    public void Deltas_ASmile_MovesItsVertexByTheWeight()
    {
        (float[] positions, float[] normals) = FaceFlex.Deltas(Model, [0.5f, -1f], vertexCount: 2).ShouldNotBeNull();

        positions.ShouldBe([0f, 0f, 0f, 1f, 0f, 0f]);
        normals.ShouldBe([0f, 0f, 0f, 0f, 0f, 0.5f]);
    }

    [Test]
    public void Deltas_NoWeight_IsNull() => FaceFlex.Deltas(Model, [0f, -1f], vertexCount: 2).ShouldBeNull();

    [Test]
    public void InBufferOrder_EachBufferVertex_TakesItsCornersVertexsDeltas()
    {
        // Corners 0, 1, 2 are .vvd vertices 1, 0, 1; the buffer holds corners 2, 0, 1.
        float[] stream = FaceFlex.InBufferOrder(([0f, 0f, 0f, 1f, 2f, 3f], [0f, 0f, 0f, 4f, 5f, 6f]), [1, 0, 1], [2, 0, 1]);

        stream.ShouldBe([1f, 2f, 3f, 4f, 5f, 6f, 1f, 2f, 3f, 4f, 5f, 6f, 0f, 0f, 0f, 0f, 0f, 0f]);
    }

    private static SceneTaunt Scene(params SceneExpression[] expressions) =>
        new([], -1f, -1f) { Expressions = expressions, Duration = 5f };

    private static SceneExpression Expression(float start, float end, params (string Name, float Weight, float Influence)[] weights) =>
        new(start, end, [], Array.ConvertAll(weights, w => new SceneExpressionWeight(w.Name, w.Weight, w.Influence)));
}
