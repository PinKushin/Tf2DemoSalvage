using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// <c>SkinnedModel.BlendedAnimationsInto</c> on a sequence that really blends: the grid path B589 moved onto the stack.
/// </summary>
/// <remarks>
/// **The oracle is the grid's own array answer, compacted the way the code before B589 did**: `ThreeWay` gives three corners
/// and their weights, and a corner of weight zero is dropped while the order of the rest is kept. The allocation tests in
/// <see cref="IkRuleDependencyTests"/> pass no pose values, which takes the one-corner early return, so this is the only
/// test that reaches the stack spans, the compaction and the corner order.
/// </remarks>
public sealed class BlendedAnimationsIntoTests
{
    /// <summary>A three-by-three grid on two parameters spanning −1 to 1, as a player's movement blend is.</summary>
    private static readonly StudioBlendGrid Grid = new(3, 3, [0, 1, 2, 3, 4, 5, 6, 7, 8], 0, 1, -1f, 1f, -1f, 1f);

    private static readonly StudioPoseParameter[] Parameters =
        [new("move_x", -1f, 1f, 0f), new("move_y", -1f, 1f, 0f)];

    private static PropModels.SkinnedModel Model()
    {
        List<(int Group, IReadOnlyList<StudioSequence> Sequences)> groups =
        [
            (0, [new StudioSequence(
                Animation: 0, Flags: 0, Label: "run", Blend: Grid,
                Activity: "run", ActivityWeight: 1)]),
        ];

        return new PropModels.SkinnedModel(
            Bones: [],
            Models: [[]],
            Sequences: StudioSequenceTable.Merge(groups),
            Groups: groups,
            PoseParameters: Parameters,
            MasterPose: [[0, 1]]);
    }

    /// <summary>What the code before B589 returned: the grid's three corners with the weightless ones dropped, in order.</summary>
    private static List<(int Animation, float Weight)> Oracle(IReadOnlyList<float> poseValues)
    {
        int[] identity = [0, 1];
        (int x, float settingX) = Grid.Locate(0, Parameters, poseValues, identity);
        (int y, float settingY) = Grid.Locate(1, Parameters, poseValues, identity);
        (int[] animations, float[] weights) = Grid.ThreeWay(x, y, settingX, settingY);

        List<(int Animation, float Weight)> blended = [];

        for (int corner = 0; corner < animations.Length; corner++)
        {
            if (weights[corner] > 0f)
            {
                blended.Add((animations[corner], weights[corner]));
            }
        }

        return blended;
    }

    /// <remarks>
    /// Settings inside a cell, where all three corners weigh; on grid lines, where one corner weighs nothing and the
    /// compaction has to close the gap; and on a node, a single corner. The control is that the oracle itself returns
    /// three, two and one corners there, so each branch of the compaction is reached. **Which corner drops depends on the
    /// line**: at (0.25, 0) and (0, −0.4) it is the last, so nothing moves; at (−0.25, 0) and (0, 0.4) it is an earlier one.
    /// Measured by sabotage: writing each kept corner at its own index instead of the compacted one reddened those two and
    /// the node, and reversing the corner order reddened all.
    /// </remarks>
    [TestCase(0.3f, -0.6f, 3)]
    [TestCase(0.25f, 0f, 2)]
    [TestCase(-0.25f, 0f, 2)]
    [TestCase(0f, 0.4f, 2)]
    [TestCase(0f, -0.4f, 2)]
    [TestCase(0f, 0f, 1)]
    public void BlendedAnimationsInto_OnABlendGrid_MatchesTheGridsCompactedCorners(float moveX, float moveY, int corners)
    {
        float[] poseValues =
        [
            StudioBlendGrid.Normalize(Parameters[0], moveX),
            StudioBlendGrid.Normalize(Parameters[1], moveY),
        ];

        List<(int Animation, float Weight)> expected = Oracle(poseValues);
        expected.Count.ShouldBe(corners, "the control: this setting reaches the branch it is chosen for");

        Span<int> animations = stackalloc int[PropModels.SkinnedModel.BlendCorners];
        Span<float> weights = stackalloc float[PropModels.SkinnedModel.BlendCorners];
        (int group, int count) = Model().BlendedAnimationsInto(0, poseValues, animations, weights);

        group.ShouldBe(0);
        count.ShouldBe(expected.Count);

        for (int corner = 0; corner < count; corner++)
        {
            animations[corner].ShouldBe(expected[corner].Animation, $"corner {corner}'s animation");
            weights[corner].ShouldBe(expected[corner].Weight, 1e-6f, $"corner {corner}'s weight");
        }
    }
}
