using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>The run-length read, branch by branch, on hand-built streams (B620).</summary>
/// <remarks>
/// **Valve's `ExtractAnimValue`** (<c>bone_setup.cpp:267-336</c> for two keys, <c>:339-369</c> for one), each slot two
/// bytes: a header slot is <c>valid</c> then <c>total</c>, a value slot one <c>short</c>. Every expected pair below is read
/// off the SDK's branches by hand, so a stream names which branch it reaches.
/// </remarks>
public sealed class ExtractAnimValueConformanceTests
{
    /// <remarks>
    /// <list type="bullet">
    /// <item>Valid past the next frame: both read from the block (<c>:302-305</c>).</item>
    /// <item>The last valid frame with the block running on: "data repeats, no blend" (<c>:309-312</c>).</item>
    /// <item>The last valid frame ending the block: "pull blend from first data block in next list" (<c>:316-317</c>).</item>
    /// <item>Past the valid frames, the block running on: the last valid, twice (<c>:324-329</c>).</item>
    /// <item>Past the valid frames, ending the block: the next list's first value (<c>:332-333</c>).</item>
    /// <item>A first block of one valid frame in one: the crash guard, both the same (<c>:277-281</c>), where the branches
    /// would pull 99 from the next list.</item>
    /// <item>A frame in a later block: the walk (<c>:286-296</c>).</item>
    /// <item>A frame whose walk meets an ending block: both zero (<c>:290-294</c>).</item>
    /// </list>
    /// </remarks>
    [TestCase(new[] { 3, 3, 10, 20, 30 }, 0, 10f, 20f, TestName = "ValuePair_ValidPastTheNextFrame_ReadsBothFromTheBlock")]
    [TestCase(new[] { 2, 4, 10, 20 }, 1, 20f, 20f, TestName = "ValuePair_TheLastValidFrameWithTheBlockRunningOn_Repeats")]
    [TestCase(new[] { 2, 2, 10, 20, 1, 2, 50 }, 1, 20f, 50f, TestName = "ValuePair_TheLastValidFrameEndingTheBlock_PullsTheNextList")]
    [TestCase(new[] { 1, 4, 10 }, 2, 10f, 10f, TestName = "ValuePair_PastTheValidFramesWithTheBlockRunningOn_RepeatsTheLast")]
    [TestCase(new[] { 1, 3, 10, 1, 1, 70 }, 2, 10f, 70f, TestName = "ValuePair_PastTheValidFramesEndingTheBlock_PullsTheNextList")]
    [TestCase(new[] { 1, 1, 33, 1, 1, 99 }, 0, 33f, 33f, TestName = "ValuePair_AOneFrameFirstBlock_TakesTheCrashGuard")]
    [TestCase(new[] { 2, 2, 1, 2, 3, 3, 5, 6, 7 }, 3, 6f, 7f, TestName = "ValuePair_AFrameInALaterBlock_WalksToIt")]
    [TestCase(new[] { 2, 2, 1, 2, 0, 0 }, 2, 0f, 0f, TestName = "ValuePair_AWalkMeetingAnEndingBlock_ReadsZero")]
    public void ValuePair_AStream_ReadsTheEnginesBranch(int[] stream, int frame, float first, float second)
    {
        ArgumentNullException.ThrowIfNull(stream);

        (float v1, float v2) = StudioAnimation.ValuePair(Track(stream), 0, 0, frame, 1f);

        (v1, v2).ShouldBe((first, second));
    }

    /// <remarks>The scale multiplies each value as it is read: <c>panimvalue[k+1].value * scale</c> (<c>:300</c>).</remarks>
    [Test]
    public void ValuePair_WithAScale_ScalesEachValue() =>
        StudioAnimation.ValuePair(Track([3, 3, 10, 20, 30]), 0, 0, 0, 0.5f).ShouldBe((5f, 10f));

    /// <remarks>
    /// **The one-key walk tests an ending block only after a step** (<c>:349-359</c>): a first block whose <c>total</c> is
    /// zero is stepped over, and the frame read from the next. Tested first, as the reader did, it read zero.
    /// </remarks>
    [Test]
    public void Value_AFirstBlockOfTotalZero_IsSteppedOver() =>
        StudioAnimation.Value(Track([0, 0, 2, 2, 8, 9]), 0, 0, 0).ShouldBe(8f);

    /// <remarks>
    /// **An absent channel is seeded by the TRACK's delta flag** (<c>CalcBoneQuaternion</c> <c>:395</c>,
    /// <c>CalcBonePosition</c> <c>:511</c>), not the animation's. The synthetic model's first animation is not a delta, and
    /// its bone 3 track is a delta with no position: the position is zero, not the rest. Its second animation is a delta,
    /// and its bone 6 track is not, with no rotation: the rotation is the rest, not the identity.
    /// </remarks>
    [Test]
    public void Pose_AnAbsentChannel_IsSeededByItsTracksDeltaFlag()
    {
        byte[] model = SyntheticStudioAnimation.Synthetic();
        List<StudioBone> bones = SyntheticStudioAnimation.Skeleton();

        StudioBonePose deltaTrack = StudioAnimation.Pose(model, bones, 0, 0).Single(pose => pose.Bone == 3);
        StudioBonePose plainTrack = StudioAnimation.Pose(model, bones, 1, 0).Single(pose => pose.Bone == 6);

        bones[3].Position.ShouldNotBe((0f, 0f, 0f), "the control: the rest is not zero");
        deltaTrack.Position.ShouldBe((0f, 0f, 0f));
        plainTrack.Rotation.ShouldBe(bones[6].Rotation);
    }

    /// <summary>
    /// A track whose first channel's stream is <paramref name="stream"/>: an offset table of three shorts, then the stream
    /// as two-byte slots, header bytes in pairs and values as shorts.
    /// </summary>
    private static byte[] Track(int[] stream)
    {
        List<byte> slots = [];

        for (int at = 0; at < stream.Length;)
        {
            int valid = stream[at];
            int total = stream[at + 1];

            slots.Add((byte)valid);
            slots.Add((byte)total);
            at += 2;

            for (int value = 0; value < valid && at < stream.Length; value++, at++)
            {
                slots.AddRange(BitConverter.GetBytes((short)stream[at]));
            }
        }

        return [6, 0, 0, 0, 0, 0, .. slots];
    }
}
