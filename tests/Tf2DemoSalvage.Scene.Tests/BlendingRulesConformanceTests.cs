using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// The blend inputs — layers, autoplay sequences, IK rules — are built when the bones are, not for every entity a frame.
/// </summary>
/// <remarks>
/// **What the engine does, from published source** (`F:/src/source-sdk-2013`, read 2026-10-23).
/// `C_BaseAnimating::StandardBlendingRules` (<c>c_baseanimating.cpp:1953-2018</c>) is where the pose is accumulated:
/// <c>AccumulatePose</c> for the main sequence, <c>MaintainSequenceTransitions</c>, <c>AccumulateLayers</c>,
/// <c>CalcAutoplaySequences</c>, <c>CalcBoneAdj</c>. It is called only from <c>SetupBones</c> (<c>:2796</c>), which an
/// entity reaches when it is drawn or something asks for its bones. What runs for every client-animating entity every
/// frame is <c>UpdateClientSideAnimations</c> (<c>:6368</c>) — the cycle advance and the events — and nothing of the
/// blend. So an entity the view culls has no layers built this frame.
/// </remarks>
public sealed class BlendingRulesConformanceTests
{
    private const int Autoplaying = 2;

    [Test]
    public void Instances_AnEntityBehindTheCamera_BuildsNoAutoplayLayer()
    {
        SceneProp behind = Prop(x: -5000f);
        EntityModelSet models = Loaded(behind);

        models.Instances([behind], [], seconds: 0.5d, frustum: LookingAlongX());

        models.Posed.ShouldBe(0, "the control on the cull: nothing reached SetupBones");
        models.LayersOf(4).ShouldNotBeNull().ShouldBeEmpty(
            "StandardBlendingRules runs inside SetupBones, which a culled entity never reaches");
    }

    /// <remarks>
    /// The control: the same entity in view builds its autoplay layer, so the observation can tell the two apart. Green
    /// before the change too, as is the next test; only the first test here was red before it.
    /// </remarks>
    [Test]
    public void Instances_TheSameEntityInView_BuildsItsAutoplayLayer()
    {
        SceneProp ahead = Prop(x: 500f);
        EntityModelSet models = Loaded(ahead);

        models.Instances([ahead], [], seconds: 0.5d, frustum: LookingAlongX());

        models.Posed.ShouldBe(1);
        models.LayersOf(4).ShouldNotBeNull().Select(layer => layer.Sequence).ShouldBe([Autoplaying]);
    }

    /// <remarks>
    /// **Asked for out of view, built then** — `SetupBones` from an attachment query or a merge is still a bone setup, so
    /// the blend is built for it exactly as for a draw.
    /// </remarks>
    [Test]
    public void SetupBones_OnACulledEntity_BuildsItsAutoplayLayer()
    {
        SceneProp behind = Prop(x: -5000f);
        EntityModelSet models = Loaded(behind);

        models.Instances([behind], [], seconds: 0.5d, frustum: LookingAlongX());
        models.SkinningOf(4);

        models.LayersOf(4).ShouldNotBeNull().Select(layer => layer.Sequence).ShouldBe([Autoplaying]);
    }

    /// <remarks>
    /// **Culled at 0.5 s, then drawn at 1.5 s: the layer is the later frame's**, built from that frame's inputs and not the
    /// pending ones of the frame that never set up. The prediction is exact: a model set that only ever drew at 1.5.
    /// </remarks>
    [Test]
    public void Instances_CulledThenDrawn_BuildsTheDrawnFramesAutoplayLayer()
    {
        SceneProp behind = Prop(x: -5000f);
        SceneProp ahead = Prop(x: 500f);
        EntityModelSet models = Loaded(behind);
        EntityModelSet control = Loaded(ahead);

        models.Instances([behind], [], seconds: 0.5d, frustum: LookingAlongX());
        models.Instances([ahead], [], seconds: 1.75d, frustum: LookingAlongX());
        control.Instances([ahead], [], seconds: 1.75d, frustum: LookingAlongX());

        PoseLayer expected = control.LayersOf(4).ShouldNotBeNull().Single();
        PoseLayer layer = models.LayersOf(4).ShouldNotBeNull().Single();

        layer.Frame.ShouldBe(expected.Frame);
        layer.FrameFraction.ShouldBe(expected.FrameFraction);
    }

    /// <remarks>
    /// **A new model on the same index is a new animating entity, and the hook moves with it.** Without re-hooking, the
    /// new entity has no hook and builds no layers.
    /// </remarks>
    [Test]
    public void Instances_AModelChangeOnAnIndex_StillBuildsTheLayers()
    {
        SceneProp first = Prop(x: 500f);
        SceneProp second = Prop(x: 500f, model: "models/props_gameplay/resupply_locker_b.mdl");
        EntityModelSet models = new() { Geometry = _ => Frames() };

        models.Add([first, second], _ => Frames());
        models.Instances([first], [], seconds: 0.5d, frustum: LookingAlongX());
        models.Instances([second], [], seconds: 0.6d, frustum: LookingAlongX());

        models.LayersOf(4).ShouldNotBeNull().Select(layer => layer.Sequence).ShouldBe([Autoplaying]);
    }

    private static SceneProp Prop(float x, string model = "models/props_gameplay/resupply_locker.mdl") =>
        new(
            4,
            model,
            ScenePropTrack.Classify(model),
            new ScenePose { X = x, Sequence = 0, Cycle = 0f },
            null,
            ClientSideAnimated: true);

    private static EntityModelSet Loaded(SceneProp prop)
    {
        EntityModelSet models = new() { Geometry = _ => Frames() };

        models.Add([prop], _ => Frames());

        return models;
    }

    private static PropModels.ModelFrames Frames()
    {
        // `STUDIO_AUTOPLAY` is `0x0008` (`studio.h:3081`), as in `AutoplaySequenceTests`.
        PropModels.SkinnedModel model =
            SyntheticSkinnedModel.WithFlags(("idle", 0), ("open", 0), ("spin", 0x0008));

        return new PropModels.ModelFrames(
            [
                new PropVertex[]
                {
                    new(1f, 0f, 0f, 0f, 0f, MaterialIndex: 3),
                    new(0f, 1f, 0f, 1f, 0f, MaterialIndex: 3),
                    new(0f, 0f, 1f, 0f, 1f, MaterialIndex: 3),
                },
            ],
            new Dictionary<int, (int Start, int Frames, float CyclesPerSecond)>
            {
                [0] = (0, 1, 0f),
            },
            [0],
            [true],
            Skinned: model with
            {
                Models =
                [
                    AnimatedStudioBytes.OneSecondLoop(animations: 3, sequences: 3),
                ],
            })
        {
            HeaderBounds = new StudioBox(-16f, -16f, 0f, 16f, 16f, 64f),
        };
    }

    private static ViewFrustum LookingAlongX() =>
        ViewFrustum.Perspective(
            origin: (0f, 0f, 0f),
            forward: (1f, 0f, 0f),
            right: (0f, -1f, 0f),
            up: (0f, 0f, 1f),
            nearZ: 1f,
            farZ: 10000f,
            fovX: 90f,
            fovY: 60f);
}
