using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// A scaled model opts out of IK entirely, which is Valve's choice and not an approximation.
/// </summary>
/// <remarks>
/// **<c>C_BaseAnimating::SetupBones</c> deletes the IK context outright when the model is scaled**
/// (<c>c_baseanimating.cpp:2841</c>), under its own note:
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
/// and <c>IsModelScaled</c> is an epsilon test rather than an exact one:
/// <c>m_flModelScale &gt; 1.0f+FLT_EPSILON || m_flModelScale &lt; 1.0f-FLT_EPSILON</c>
/// (<c>c_baseanimating.h:780</c>).
///
/// **The reason is stated and it is about correctness, not cost**: IK marks bones as already
/// calculated, and a scaled skeleton's bones are not where the unscaled solve assumed. So this is
/// not an optimisation to be reinstated later.
///
/// **Reachability, measured and small**: every prop in `z1800` reports scale 1, so nothing in the
/// committed corpus exercises this. TF2 scales models for MvM giants and some Halloween bosses.
/// It is implemented because the engine does it, and the tests say so rather than a demo.
/// </remarks>
public sealed class ModelScaleIkTests
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

    [Test]
    public void Instances_ForAScaledModel_RunsNoIkAtAll()
    {
        EntityModelSet models = Posed(scale: 2f);

        models.IkWork.Chained.ShouldBe(0, "a scaled model has no IK context at all");
    }

    /// <remarks>
    /// **The epsilon, which an exact comparison would get wrong in the safe direction and a
    /// generous one in the unsafe direction.** Valve's test is against <c>FLT_EPSILON</c>, so a
    /// scale that differs by a float's last bit is NOT scaled — the case that matters because a
    /// scale arriving over the wire and one written as a literal need not be bit-identical.
    /// </remarks>
    [Test]
    public void Instances_ForAScaleAFloatEpsilonFromOne_StillReachesTheIkStage()
    {
        EntityModelSet models = Posed(scale: 1f + (float.Epsilon * 2f));

        models.IkWork.Chained.ShouldBe(1, "a scale indistinguishable from one is not scaled");
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
