using System.Collections.Generic;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>Which of an entity's networked overlay layers reach the skeleton, and at what frame.</summary>
/// <remarks>
/// **`C_BaseAnimatingOverlay::AccumulateLayers`** (`c_baseanimatingoverlay.cpp:336-364`): a layer whose sequence the model
/// does not have is skipped (`m_nSequence >= nSequences`), one at zero weight contributes nothing (`if (fWeight > 0)`), and
/// the rest are sampled at <c>ClampCycle( m_flCycle, looping )</c>. The wire sends the weight in eight bits over [0, 1]
/// (`baseanimatingoverlay.cpp:68`), so the engine's <c>if (fWeight &gt; 1) fWeight = 1</c> has nothing to clamp here.
/// </remarks>
public sealed class WireLayerTests
{
    /// <summary>The fixture has three sequences.</summary>
    private const int SequenceCount = 3;

    [Test]
    public void LayersFor_TheFirstSequenceAtAPositiveWeight_IsSampledAtItsCycle()
    {
        IReadOnlyList<PoseLayer> layers = Posed(new SceneAnimationLayer(Order: 0, Sequence: 0, Cycle: 0.25f, Weight: 0.5f));

        layers.Count.ShouldBe(1, "sequence 0 is a real sequence, not a sentinel");
        layers[0].Sequence.ShouldBe(0);

        // 0.25 of 30 distinct frames is 7.5.
        layers[0].Frame.ShouldBe(7);
        layers[0].FrameFraction.ShouldBe(0.5f, 1e-4f);
        layers[0].Weight.ShouldBe(0.5f);
    }

    [Test]
    public void LayersFor_TheLastSequence_IsKept()
    {
        Posed(new SceneAnimationLayer(Order: 0, Sequence: SequenceCount - 1, Cycle: 0f, Weight: 1f))
            .ShouldHaveSingleItem().Sequence.ShouldBe(SequenceCount - 1);
    }

    [Test]
    public void LayersFor_ASequenceOnePastTheModels_IsSkipped()
    {
        Posed(new SceneAnimationLayer(Order: 0, Sequence: SequenceCount, Cycle: 0f, Weight: 1f))
            .ShouldBeEmpty("m_nSequence >= nSequences continues past the layer");
    }

    [Test]
    public void LayersFor_AZeroWeight_IsSkipped()
    {
        Posed(new SceneAnimationLayer(Order: 0, Sequence: 1, Cycle: 0f, Weight: 0f))
            .ShouldBeEmpty("only fWeight > 0 is accumulated");
    }

    /// <summary>Poses a server-animated cabinet carrying one wire layer and returns the layers it was handed.</summary>
    private static IReadOnlyList<PoseLayer> Posed(SceneAnimationLayer layer)
    {
        EntityModelSet models = new();

        SceneProp[] props =
        [
            new(
                9,
                "models/props_gameplay/resupply_locker.mdl",
                ScenePropTrack.Classify("models/props_gameplay/resupply_locker.mdl"),
                new ScenePose { Sequence = 0, Layers = [layer] },
                null),
        ];

        models.Add(props, _ => Model());
        models.Instances(props, [], seconds: 1d);

        return models.LayersOf(9).ShouldNotBeNull();
    }

    /// <summary>Three looping one-cycle-a-second sequences over 31 frames, with no autolayers or transitions.</summary>
    private static PropModels.ModelFrames Model()
    {
        PropModels.SkinnedModel model = SyntheticSkinnedModel.WithFlags(("a", 0x0001), ("b", 0x0001), ("c", 0x0001));

        return new PropModels.ModelFrames(
            [
                new PropVertex[]
                {
                    new(1f, 0f, 0f, 0f, 0f, MaterialIndex: 3),
                    new(0f, 1f, 0f, 1f, 0f, MaterialIndex: 3),
                    new(0f, 0f, 1f, 0f, 1f, MaterialIndex: 3),
                },
            ],
            new Dictionary<int, (int Start, int Frames, float CyclesPerSecond)> { [0] = (0, 1, 0f) },
            [0],
            [true],
            Skinned: model with { Models = [AnimatedStudioBytes.OneSecondLoop(animations: 3)] });
    }
}
