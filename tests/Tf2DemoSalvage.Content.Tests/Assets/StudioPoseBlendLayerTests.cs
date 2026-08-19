using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// <c>StudioPoseBlend.Layer</c> against <c>SlerpBones</c>'s <c>STUDIO_DELTA</c> branch.
/// </summary>
/// <remarks>
/// Every prediction here is computed independently from Valve's own formulas — the Hamilton
/// product by hand, the identity cases from the algebra — rather than by reading back whatever
/// the port happens to produce, which would test the port against itself.
///
/// Two 90-degree rotations about DIFFERENT axes are used throughout, because that is the case
/// where composition order actually matters: two rotations about the same axis commute and would
/// pass whichever order the port used, hiding a transposed argument.
/// </remarks>
public sealed class StudioPoseBlendLayerTests
{
    private const float Root2Over2 = 0.70710678f;

    /// <summary>A single bone at the world origin, so position math is easy to predict.</summary>
    private static readonly IReadOnlyList<StudioBone> OneBone =
    [
        new StudioBone(
            Name: "root",
            Parent: -1,
            Position: (0f, 0f, 0f),
            Rotation: (0f, 0f, 0f, 1f),
            PoseToBone: default),
    ];

    [Test]
    public void PoseBlendLayer_ZeroWeight_LeavesTheBaseBoneUntouched()
    {
        // `if (s2 <= 0.0f) continue` — a bone with no weight in the gesture is not blended
        // toward anything, not even fractionally.
        IReadOnlyList<StudioBonePose> basePose = [new StudioBonePose(0, (1f, 2f, 3f), (0.1f, 0.2f, 0.3f, 0.9274f))];
        IReadOnlyList<StudioBonePose> deltaPose = [new StudioBonePose(0, (10f, 20f, 30f), (Root2Over2, 0f, 0f, Root2Over2))];

        IReadOnlyList<StudioBonePose> result = StudioPoseBlend.Layer(
            OneBone, basePose, deltaPose, boneWeights: [0f], layerWeight: 1f, post: true);

        AssertEqual(result[0].Position, basePose[0].Position);
        AssertEqual(result[0].Rotation, basePose[0].Rotation);
    }

    [Test]
    public void PoseBlendLayer_AnUnmentionedBone_DefaultsToIdentityNotRestPose()
    {
        // Confirmed at bone_setup.cpp:599: a STUDIO_DELTA animation's own track, for a bone it
        // does not reach, decodes as (0,0,0,1) and zero position — never the rest pose. Composing
        // that at full weight must leave the base pose exactly as it was.
        IReadOnlyList<StudioBonePose> basePose = [new StudioBonePose(0, (5f, 6f, 7f), (0.1f, 0f, 0f, 0.995f))];
        IReadOnlyList<StudioBonePose> emptyDelta = [];

        IReadOnlyList<StudioBonePose> result = StudioPoseBlend.Layer(
            OneBone, basePose, emptyDelta, boneWeights: [1f], layerWeight: 1f, post: true);

        AssertEqual(result[0].Position, basePose[0].Position);
        AssertEqual(result[0].Rotation, basePose[0].Rotation, 0.0001f);
    }

    [Test]
    public void PoseBlendLayer_APostLayer_CombinesBaseThenScaledDelta()
    {
        // **The discriminator.** Base is 90° about X, delta is 90° about Y. Composing base⊗delta
        // and delta⊗base give different results because quaternion multiplication does not
        // commute across different axes — computed here by hand from QuaternionMult's own
        // formula, not read back from the port.
        //
        // p=(0.7071,0,0,0.7071), q=(0,0.7071,0,0.7071):
        //   qt.x = p.x*q.w + p.y*q.z - p.z*q.y + p.w*q.x = 0.5
        //   qt.y = -p.x*q.z + p.y*q.w + p.z*q.x + p.w*q.y = 0.5
        //   qt.z = p.x*q.y - p.y*q.x + p.z*q.w + p.w*q.z = 0.5
        //   qt.w = -p.x*q.x - p.y*q.y - p.z*q.z + p.w*q.w = 0.5
        IReadOnlyList<StudioBonePose> basePose = [new StudioBonePose(0, (0f, 0f, 0f), (Root2Over2, 0f, 0f, Root2Over2))];
        IReadOnlyList<StudioBonePose> deltaPose = [new StudioBonePose(0, (0f, 0f, 0f), (0f, Root2Over2, 0f, Root2Over2))];

        IReadOnlyList<StudioBonePose> result = StudioPoseBlend.Layer(
            OneBone, basePose, deltaPose, boneWeights: [1f], layerWeight: 1f, post: true);

        (float X, float Y, float Z, float W) rotation = result[0].Rotation;

        rotation.X.ShouldBe(0.5f, 0.001f);
        rotation.Y.ShouldBe(0.5f, 0.001f);
        rotation.Z.ShouldBe(0.5f, 0.001f);
        rotation.W.ShouldBe(0.5f, 0.001f);
    }

    [Test]
    public void PoseBlendLayer_ANonPostLayer_CombinesScaledDeltaThenBase()
    {
        // The same two rotations, opposite composition order — `QuaternionSM(s2, q2, q1, q1)`
        // multiplies delta first. By hand, p=(0,0.7071,0,0.7071) [delta], q=(0.7071,0,0,0.7071)
        // [base]:
        //   qt.x = 0*0.7071 + 0.7071*0 - 0*0 + 0.7071*0.7071 = 0.5
        //   qt.y = -0*0 + 0.7071*0.7071 + 0*0.7071 + 0.7071*0 = 0.5
        //   qt.z = 0*0 - 0.7071*0.7071 + 0*0.7071 + 0.7071*0 = -0.5
        //   qt.w = -0*0.7071 - 0.7071*0 - 0*0 + 0.7071*0.7071 = 0.5
        //
        // The Z component flips sign against the POST case above — that is the whole test.
        IReadOnlyList<StudioBonePose> basePose = [new StudioBonePose(0, (0f, 0f, 0f), (Root2Over2, 0f, 0f, Root2Over2))];
        IReadOnlyList<StudioBonePose> deltaPose = [new StudioBonePose(0, (0f, 0f, 0f), (0f, Root2Over2, 0f, Root2Over2))];

        IReadOnlyList<StudioBonePose> result = StudioPoseBlend.Layer(
            OneBone, basePose, deltaPose, boneWeights: [1f], layerWeight: 1f, post: false);

        (float X, float Y, float Z, float W) rotation = result[0].Rotation;

        rotation.X.ShouldBe(0.5f, 0.001f);
        rotation.Y.ShouldBe(0.5f, 0.001f);
        rotation.Z.ShouldBe(-0.5f, 0.001f);
        rotation.W.ShouldBe(0.5f, 0.001f);
    }

    [Test]
    public void PoseBlendLayer_Position_AddsLinearlyEitherWay()
    {
        // `pos1[i] += pos2[i] * s2` is identical in both branches — no quaternion involved, and
        // no reason for POST to change it. Weight 0.5 halves the delta's contribution.
        IReadOnlyList<StudioBonePose> basePose = [new StudioBonePose(0, (10f, 0f, 0f), (0f, 0f, 0f, 1f))];
        IReadOnlyList<StudioBonePose> deltaPose = [new StudioBonePose(0, (0f, 20f, 0f), (0f, 0f, 0f, 1f))];

        IReadOnlyList<StudioBonePose> post = StudioPoseBlend.Layer(
            OneBone, basePose, deltaPose, boneWeights: [1f], layerWeight: 0.5f, post: true);

        IReadOnlyList<StudioBonePose> nonPost = StudioPoseBlend.Layer(
            OneBone, basePose, deltaPose, boneWeights: [1f], layerWeight: 0.5f, post: false);

        AssertEqual(post[0].Position, (10f, 10f, 0f));
        AssertEqual(nonPost[0].Position, (10f, 10f, 0f));
    }

    private static void AssertEqual(
        (float X, float Y, float Z) actual, (float X, float Y, float Z) expected, float tolerance = 0f)
    {
        actual.X.ShouldBe(expected.X, tolerance);
        actual.Y.ShouldBe(expected.Y, tolerance);
        actual.Z.ShouldBe(expected.Z, tolerance);
    }

    private static void AssertEqual(
        (float X, float Y, float Z, float W) actual,
        (float X, float Y, float Z, float W) expected,
        float tolerance = 0f)
    {
        actual.X.ShouldBe(expected.X, tolerance);
        actual.Y.ShouldBe(expected.Y, tolerance);
        actual.Z.ShouldBe(expected.Z, tolerance);
        actual.W.ShouldBe(expected.W, tolerance);
    }

    [Test]
    public void PoseBlendLayer_BoneAndLayerWeights_BothScaleTheStrength()
    {
        // strength = layerWeight * boneWeights[i]. A bone weighted 0.5 in the list at a layer
        // weight of 0.5 gets a quarter of the delta's positional push, not half.
        IReadOnlyList<StudioBonePose> basePose = [new StudioBonePose(0, (0f, 0f, 0f), (0f, 0f, 0f, 1f))];
        IReadOnlyList<StudioBonePose> deltaPose = [new StudioBonePose(0, (100f, 0f, 0f), (0f, 0f, 0f, 1f))];

        IReadOnlyList<StudioBonePose> result = StudioPoseBlend.Layer(
            OneBone, basePose, deltaPose, boneWeights: [0.5f], layerWeight: 0.5f, post: true);

        result[0].Position.X.ShouldBe(25f, 0.01f);
    }
}
