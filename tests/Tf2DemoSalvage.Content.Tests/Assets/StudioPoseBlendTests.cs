using System;

using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// <c>StudioPoseBlend.Blend</c>, the interpolated blend every main sequence goes through.
/// </summary>
/// <remarks>
/// **Found by the mutation report: `StudioBlendGrid` is the largest survivor block in Content (122),
/// and its biggest function had no direct test at all.** `StudioPoseBlendLayerTests` covers `Layer`
/// — the additive path written for the gesture work — and nothing covered `Blend`, which is the one
/// every animated player's pose actually passes through.
///
/// **Two properties do the discriminating, and neither is the arithmetic.** The linear mix is easy
/// to get right and easy to test; what an implementation gets WRONG is the quaternion alignment and
/// the expansion to full arrays:
///
/// - **Align.** Two quaternions can name the same rotation with opposite signs. Blending them
///   without flipping one first interpolates the long way round — through the antipode — and a
///   limb takes a visible detour instead of the short arc. Every value stays finite and normalised,
///   so nothing reports it.
/// - **Expand.** An animation lists only the bones it touches; the rest fall back to the REST pose.
///   Blending the lists instead of full arrays mixes a moved bone against nothing.
///
/// Predictions are computed from the formula rather than read back from the implementation.
/// </remarks>
public sealed class StudioPoseBlendTests
{
    private const float Root2Over2 = 0.70710678f;

    /// <summary>Two bones, the second offset so a rest-pose fallback is visible.</summary>
    private static IReadOnlyList<StudioBone> TwoBones =>
    [
        new StudioBone(
            Name: "root",
            Parent: -1,
            Position: (0f, 0f, 0f),
            Rotation: (0f, 0f, 0f, 1f),
            PoseToBone: default),
        new StudioBone(
            Name: "child",
            Parent: 0,
            Position: (10f, 20f, 30f),
            Rotation: (0f, 0f, 0f, 1f),
            PoseToBone: default),
    ];

    [Test]
    public void PoseBlend_AtZero_IsTheFirstPose()
    {
        IReadOnlyList<StudioBonePose> blended = StudioPoseBlend.Blend(
            TwoBones, [Pose(0, (1f, 2f, 3f))], [Pose(0, (100f, 200f, 300f))], 0f);

        AssertPosition(blended[0].Position, (1f, 2f, 3f));
    }

    [Test]
    public void PoseBlend_AtOne_IsTheSecondPose()
    {
        // The other end, because a weight applied to the wrong operand passes at zero.
        IReadOnlyList<StudioBonePose> blended = StudioPoseBlend.Blend(
            TwoBones, [Pose(0, (1f, 2f, 3f))], [Pose(0, (100f, 200f, 300f))], 1f);

        AssertPosition(blended[0].Position, (100f, 200f, 300f));
    }

    [Test]
    public void PoseBlend_AtAQuarter_IsThreeQuartersOfTheFirst()
    {
        // **Not a half**, deliberately: at 0.5 a transposed weight gives the same answer, so the
        // test could not tell `s` from `1 - s`. At 0.25 the two differ.
        //
        // 0 * 0.75 + 100 * 0.25 = 25.
        IReadOnlyList<StudioBonePose> blended = StudioPoseBlend.Blend(
            TwoBones, [Pose(0, (0f, 0f, 0f))], [Pose(0, (100f, 400f, -80f))], 0.25f);

        AssertPosition(blended[0].Position, (25f, 100f, -20f));
    }

    [Test]
    public void PoseBlend_ABoneNeitherPoseMoved_IsStillNamed()
    {
        // **The expansion, which is why the result can be blended again.** Neither pose mentions
        // bone 1, so it must come back at its REST position rather than being absent or zero -
        // blending the lists would mix bone 0 of one against bone 1 of the other.
        IReadOnlyList<StudioBonePose> blended = StudioPoseBlend.Blend(
            TwoBones, [Pose(0, (1f, 1f, 1f))], [Pose(0, (3f, 3f, 3f))], 0.5f);

        blended.Count.ShouldBe(2, "the result names every bone in the skeleton");

        AssertPosition(blended[0].Position, (2f, 2f, 2f));

        // The untouched bone keeps the skeleton's own rest position.
        AssertPosition(blended[1].Position, (10f, 20f, 30f));
    }

    [Test]
    public void PoseBlend_ABoneOnlyOnePoseMoves_BlendsFromItsRest()
    {
        // The asymmetric case: bone 1 is moved by one pose and not the other, so its result is the
        // rest position blended toward the moved one. Half way from (10,20,30) to (20,20,30) is
        // (15,20,30) -- a value neither input holds, which is what makes this a blend rather than
        // a selection.
        IReadOnlyList<StudioBonePose> blended = StudioPoseBlend.Blend(
            TwoBones, [Pose(0, (0f, 0f, 0f))], [Pose(1, (20f, 20f, 30f))], 0.5f);

        AssertPosition(blended[1].Position, (15f, 20f, 30f));
    }

    [Test]
    public void PoseBlend_TheResult_IsAlwaysANormalisedRotation()
    {
        // A linear mix of two unit quaternions is NOT unit, so the normalise at the end is
        // load-bearing: an un-normalised rotation scales the bone it drives.
        IReadOnlyList<StudioBonePose> blended = StudioPoseBlend.Blend(
            TwoBones,
            [Rotated(0, (Root2Over2, 0f, 0f, Root2Over2))],
            [Rotated(0, (0f, Root2Over2, 0f, Root2Over2))],
            0.5f);

        (float x, float y, float z, float w) = blended[0].Rotation;

        ((x * x) + (y * y) + (z * z) + (w * w)).ShouldBe(1f, 0.0001f);
    }

    [Test]
    public void PoseBlend_AnOppositelySignedRotation_IsAlignedFirst()
    {
        // **The discriminator, and the one an implementation actually gets wrong.** q and -q name
        // the SAME rotation. Blending toward -q without flipping it first travels the long way
        // round: at the half-way point the naive mix of q and -q is the zero quaternion, which
        // normalises to something arbitrary, and in general a limb swings the wrong side.
        //
        // Here both poses are the same rotation, one written negated. Aligned, the blend is that
        // rotation unchanged at any weight. Unaligned, it collapses.
        (float X, float Y, float Z, float W) rotation = (Root2Over2, 0f, 0f, Root2Over2);
        (float X, float Y, float Z, float W) negated = (-rotation.X, -rotation.Y, -rotation.Z, -rotation.W);

        IReadOnlyList<StudioBonePose> blended = StudioPoseBlend.Blend(
            TwoBones, [Rotated(0, rotation)], [Rotated(0, negated)], 0.5f);

        (float x, float y, float z, float w) = blended[0].Rotation;

        // Still a unit quaternion...
        ((x * x) + (y * y) + (z * z) + (w * w)).ShouldBe(1f, 0.0001f);

        // ...and still the SAME rotation, up to sign. Compared by absolute value because either
        // representative is correct; what would be wrong is a different rotation entirely.
        MathF.Abs(x).ShouldBe(MathF.Abs(rotation.X), 0.001f);
        MathF.Abs(w).ShouldBe(MathF.Abs(rotation.W), 0.001f);
    }

    private static StudioBonePose Pose(int bone, (float X, float Y, float Z) position) =>
        new(bone, position, (0f, 0f, 0f, 1f));

    private static StudioBonePose Rotated(int bone, (float X, float Y, float Z, float W) rotation) =>
        new(bone, (0f, 0f, 0f), rotation);

    private static void AssertPosition(
        (float X, float Y, float Z) actual, (float X, float Y, float Z) expected)
    {
        actual.X.ShouldBe(expected.X, 0.001f);
        actual.Y.ShouldBe(expected.Y, 0.001f);
        actual.Z.ShouldBe(expected.Z, 0.001f);
    }
}
