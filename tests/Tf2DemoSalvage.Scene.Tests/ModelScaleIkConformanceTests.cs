using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// A scaled model loses its IK RULE context and keeps its sequence lock brackets, as the engine's does (B623).
/// </summary>
/// <remarks>
/// **<c>C_BaseAnimating::SetupBones</c> deletes the entity's <c>m_pIk</c> when the model is scaled**
/// (<c>c_baseanimating.cpp:2946-2963</c>), under its own note:
///
/// <code>
///   // NOTE: For model scaling, we need to opt out of IK because it will mark the bones as
///   // already being calculated
///   if ( !IsModelScaled() )
///   {
///       if ( !m_pIk &amp;&amp; hdr-&gt;numikchains() &gt; 0 &amp;&amp; !(m_EntClientFlags &amp; ENTCLIENTFLAG_DONTUSEIK) )
///           m_pIk = new CIKContext;
///   }
///   else
///   {
///       if ( m_pIk ) { delete m_pIk; m_pIk = NULL; }
///   }
/// </code>
///
/// That context runs the IK rules. **The sequence locks are not in it**: <c>AccumulatePose</c> builds its own local
/// <c>seq_ik</c> off the model's chains whatever the scale (<c>bone_setup.cpp:2424-2429</c>), so a scaled model keeps them.
///
/// <c>IsModelScaled</c> is an epsilon test: <c>m_flModelScale &gt; 1.0f+FLT_EPSILON || m_flModelScale &lt;
/// 1.0f-FLT_EPSILON</c> (<c>c_baseanimating.h:780</c>), with C's <c>FLT_EPSILON</c>, 2⁻²³.
///
/// **Reachability, measured and small**: every prop in `z1800` reports scale 1. TF2 scales models for MvM giants and
/// some Halloween bosses.
/// </remarks>
public sealed class ModelScaleIkConformanceTests
{
    /// <summary>The entity this fixture draws.</summary>
    private const int Entity = 7;

    [Test]
    public void Instances_ForAnUnscaledModelWithAChain_ReachesTheIkStage()
    {
        // **The control, and it is the half that makes the test below mean anything.** Without it,
        // "a scaled model runs no IK" and "this fixture never had a chain to begin with" are the
        // same observation — which is exactly how the first IK probe reported zero on a demo full
        // of chains.
        EntityModelSet models = Posed(scale: 1f);

        models.IkWork.Chained.ShouldBe(1, "the model declares one three-link chain");
    }

    /// <remarks>
    /// **The chains stay for the lock brackets** (B623): <c>AccumulatePose</c>'s <c>seq_ik</c> is a local built off the
    /// model's own chains (`bone_setup.cpp:2424-2429`), which no scale reaches. Only the entity's rule context goes.
    /// </remarks>
    [Test]
    public void Instances_ForAScaledModel_KeepsItsChainsForTheLockBrackets()
    {
        EntityModelSet models = Posed(scale: 2f);

        models.IkWork.Chained.ShouldBe(1, "the sequence locks solve on the model's chains, scaled or not");
    }

    /// <remarks>
    /// **The epsilon is C's <c>FLT_EPSILON</c>, 2⁻²³** (`c_baseanimating.h:780`): above one, where a float step is 2⁻²³,
    /// one step is not scaled and two are; below one the step is 2⁻²⁴, so two steps are not and three are. .NET's
    /// <c>float.Epsilon</c> is 2⁻¹⁴⁹, and against it one step either way was scaled.
    /// </remarks>
    [TestCase(1f, false, TestName = "IsModelScaled_AtOne_IsFalse")]
    [TestCase(1.00000012f, false, TestName = "IsModelScaled_OneFloatStepAboveOne_IsFalse")]
    [TestCase(1.00000024f, true, TestName = "IsModelScaled_TwoFloatStepsAboveOne_IsTrue")]
    [TestCase(0.99999994f, false, TestName = "IsModelScaled_OneFloatStepBelowOne_IsFalse")]
    [TestCase(0.99999988f, false, TestName = "IsModelScaled_TwoFloatStepsBelowOne_IsFalse")]
    [TestCase(0.99999982f, true, TestName = "IsModelScaled_ThreeFloatStepsBelowOne_IsTrue")]
    [TestCase(2f, true, TestName = "IsModelScaled_AtTwo_IsTrue")]
    public void IsModelScaled_AroundOne_IsTheEnginesEpsilonTest(float scale, bool scaled) =>
        EntityModelSet.IsModelScaled(scale).ShouldBe(scaled);

    /// <remarks>
    /// **Read after a viewmodel pass, as a probe reads it** (finding 102): that pass advances the bone clock, and the world
    /// entity built in the world pass must still be counted (B243).
    /// </remarks>
    [Test]
    public void IkWork_AfterAViewmodelPass_StillCountsTheWorldEntity()
    {
        EntityModelSet models = Posed(scale: 1f);

        models.Instances([], [], seconds: 0d, pass: EntityModelSet.ViewmodelPass);

        models.IkWork.Chained.ShouldBe(1);
    }

    /// <summary>Poses one prop at the given model scale and hands back the set.</summary>
    private static EntityModelSet Posed(float scale)
    {
        PropModels.ModelFrames frames = new(
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
            Skinned: SyntheticSkinnedModel.WithIkChain("thigh", "knee", "foot"));

        EntityModelSet models = new() { Geometry = _ => frames };

        List<SceneProp> drawn =
        [
            new(
                Entity,
                "models/player/scout.mdl",
                ScenePropTrack.Classify("models/player/scout.mdl"),
                new ScenePose { Sequence = 0, Cycle = 0f, Scale = scale },
                null,
                ClientSideAnimated: true),
        ];

        models.Add(drawn, _ => frames);
        models.Instances(drawn, [], seconds: 0d);

        return models;
    }
}
