using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Tests.Assets;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>Each blend-grid corner is sampled at its OWN frame for the sequence's cycle (B742).</summary>
/// <remarks>
/// **<c>CalcPoseSingle</c> hands the cycle, not a frame, to each corner's <c>CalcAnimation</c>**
/// (<c>bone_setup.cpp:1993-2010</c>), and each computes its own <c>fFrame = cycle * (animdesc.numframes - 1)</c>
/// (<c>:913-917</c>). Measured with <c>grid-frames</c>: 205 of the 886 shipped grids have a corner whose frame count
/// differs from the sequence's own animation — run grids whose standing centre has 34 frames against 19.
///
/// **So the sequence's own animation does not choose any corner's frame.** The fixture's animation 0 has six frames and
/// animation 2 ten. A grid weighed wholly on one corner is compared with a grid over that corner twice whose sequence plays
/// the corner itself: the same blend on both sides, so the only difference a wrong frame can make is the frame. A
/// weight-zero blend is not the corner alone — it aligns and normalises — which is why the control is a grid too.
/// </remarks>
public sealed class GridCornerFrameConformanceTests
{
    private const float Cycle = 0.3f;

    /// <remarks>
    /// The corner at pose value zero, under a sequence whose own animation has the other frame count, both ways round:
    /// six-frame corner under a ten-frame sequence, and ten under six. At 0.3 the six-frame corner is at frame 1.5 and the
    /// ten-frame one at 2.7; one frame for every corner read the first at 2.7 of six, or the second at 1.5 of ten.
    /// </remarks>
    [TestCase(2, 0, 2, 0, TestName = "Locals_ASixFrameCornerUnderATenFrameSequence_IsSampledAtItsOwnFrame")]
    [TestCase(0, 2, 0, 2, TestName = "Locals_ATenFrameCornerUnderASixFrameSequence_IsSampledAtItsOwnFrame")]
    public void Locals_AGridCorner_IsSampledAtItsOwnFrame(int main, int corner, int other, int alone)
    {
        PropModels.SkinnedModel model = Model(
            Grid(main, corner, other),
            Grid(alone, corner, corner));

        Bits(model.Locals(0, Cycle, [0f])).ShouldBe(Bits(model.Locals(1, Cycle, [0f])));
    }

    /// <remarks>
    /// **The control**: the same grid at the cycle that puts the six-frame corner where the ten-frame sequence's frame
    /// would — 2.7 of six, cycle 0.54 — is a different pose, so the comparison above can tell the two readings apart.
    /// </remarks>
    [Test]
    public void Locals_TheCornerAtTheMainAnimationsFrame_IsADifferentPose()
    {
        PropModels.SkinnedModel model = Model(Grid(2, 0, 2), Grid(0, 0, 0));

        float wrong = (Cycle * 9f) / 5f;

        Bits(model.Locals(1, Cycle, [0f])).ShouldNotBe(Bits(model.Locals(1, wrong, [0f])));
    }

    /// <remarks>
    /// **The sampler applies the sequence's own range rule**, `CalcPoseSingle`'s (<c>bone_setup.cpp:1977-1988</c>): a
    /// looping sequence at cycle one is back at its start, a one-shot holds its last frame. The tests that predict a
    /// frame read it through <see cref="SampledFrame"/> with a flag they state, so this is what holds the flag production
    /// reads. Both sequences play animation 0 of the fixture, which moves between its first and last frame.
    /// </remarks>
    [TestCase(true, true, TestName = "Locals_ALoopAtCycleOne_IsAtItsStart")]
    [TestCase(false, false, TestName = "Locals_AOneShotAtCycleOne_HoldsItsLastFrame")]
    public void Locals_AtCycleOne_TakesTheSequencesRangeRule(bool loops, bool atStart)
    {
        // `STUDIO_LOOPING`, `studio.h:3078`.
        PropModels.SkinnedModel model = Model(
            new StudioSequence(Animation: 0, Flags: loops ? 0x0001 : 0, Label: "plain", Blend: null, Activity: "idle", ActivityWeight: 1));

        Bits(model.Locals(0, 1f, [0f])).SequenceEqual(Bits(model.Locals(0, 0f, [0f]))).ShouldBe(atStart);
    }

    /// <summary>Each pose by the bits of its floats, so the comparison is exact, each rotation up to its sign.</summary>
    /// <remarks>
    /// **q and −q are one rotation**, and the blend aligns a corner to the other one (<c>QuaternionAlign</c>), so the
    /// sign of a weight-zero result follows the corner that weighs nothing. The sign is fixed here by the largest
    /// component; a wrong frame is a different rotation, not a different sign.
    /// </remarks>
    private static List<string> Bits(IEnumerable<StudioBonePose> poses) =>
    [
        .. poses.OrderBy(pose => pose.Bone).Select(pose =>
        {
            (float x, float y, float z, float w) = Signed(pose.Rotation);

            return $"{pose.Bone} {Raw(x)} {Raw(y)} {Raw(z)} {Raw(w)} " +
                $"{Raw(pose.Position.X)} {Raw(pose.Position.Y)} {Raw(pose.Position.Z)}";
        }),
    ];

    private static (float X, float Y, float Z, float W) Signed((float X, float Y, float Z, float W) q)
    {
        float[] parts = [q.X, q.Y, q.Z, q.W];
        float largest = parts.MaxBy(System.MathF.Abs);

        // `+ 0f` turns a negated zero into a positive one, so zeros compare by value.
        return largest < 0f ? ((-q.X) + 0f, (-q.Y) + 0f, (-q.Z) + 0f, (-q.W) + 0f) : (q.X + 0f, q.Y + 0f, q.Z + 0f, q.W + 0f);
    }

    private static int Raw(float value) => System.BitConverter.SingleToInt32Bits(value);

    /// <summary>A sequence playing <paramref name="main"/> over a two-corner grid.</summary>
    private static StudioSequence Grid(int main, int first, int second) =>
        new(
            Animation: main,
            Flags: 0,
            Label: $"grid {main} {first} {second}",
            Blend: new StudioBlendGrid(
                groupX: 2,
                groupY: 1,
                animations: [first, second],
                parameterX: 0,
                parameterY: -1,
                startX: 0f,
                endX: 1f,
                startY: 0f,
                endY: 0f),
            Activity: "idle",
            ActivityWeight: 1);

    private static PropModels.SkinnedModel Model(params StudioSequence[] sequences)
    {
        List<(int Group, IReadOnlyList<StudioSequence> Sequences)> groups = [(0, sequences)];

        return new PropModels.SkinnedModel(
            Bones: SyntheticStudioAnimation.Skeleton(),
            Models: [SyntheticStudioAnimation.Synthetic()],
            Sequences: StudioSequenceTable.Merge(groups),
            Groups: groups,
            PoseParameters: [new StudioPoseParameter("move_x", 0f, 1f, 0f)],
            MasterPose: [[0]]);
    }
}
