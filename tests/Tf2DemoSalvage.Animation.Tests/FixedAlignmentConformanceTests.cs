using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// A bone flagged <c>BONE_FIXED_ALIGNMENT</c> blends without being re-aligned.
/// </summary>
/// <remarks>
/// **<c>SlerpBones</c> picks between two blends per bone** (<c>bone_setup.cpp:1492</c>):
///
/// <code>
///   if ( pStudioHdr->boneFlags(i) &amp; BONE_FIXED_ALIGNMENT )
///       QuaternionSlerpNoAlign( q2[i], q1[i], s1, q3 );
///   else
///       QuaternionSlerp( q2[i], q1[i], s1, q3 );
/// </code>
///
/// **They differ by exactly one step.** `QuaternionSlerp` is `QuaternionAlign` followed by
/// `QuaternionSlerpNoAlign` (<c>mathlib_base.cpp:1605</c>), and the align step negates the target
/// when it points the long way round — a quaternion and its negation being the same rotation.
///
/// **So the flag is the animator asserting the shorter arc is not always right.** `studio.h:434`:
/// *"bone can't spin 360 degrees, all interpolation is normalized around a fixed orientation"*. On a
/// constrained bone the negation flips it out of its authored range rather than saving it a long
/// way round.
///
/// **Choosing an input where the two genuinely differ is the whole difficulty here**, and it is the
/// wrong-condition trap from `docs/memory/instrument-bugs-outnumber-decoder-bugs.md`: for any pair
/// of quaternions with a POSITIVE dot product the align step does nothing, so aligned and unaligned
/// predict the same observation and a test built from a natural-looking rotation pair cannot fail.
/// Every case below uses a negated target on purpose.
/// </remarks>
public sealed class FixedAlignmentConformanceTests
{
    /// <summary>How close two floats must be to count as equal here.</summary>
    private const double Tolerance = 1e-4;

    /// <summary>Halfway, where the two blends are furthest apart.</summary>
    private const float Halfway = 0.5f;

    [Test]
    public void Slerp_TowardANegatedTarget_TakesTheShortWayRound()
    {
        // The control for the test below, and it is Valve's ordinary path. Identity blended toward
        // the NEGATION of a ninety-degree turn: aligning flips the target back, so the result is
        // halfway to the ninety-degree turn itself — a forty-five degree rotation with a positive
        // scalar part.
        (float X, float Y, float Z, float W) turned = AboutZ(90f);

        (float X, float Y, float Z, float W) blended =
            StudioBones.Slerp(Identity, Negated(turned), Halfway);

        blended.W.ShouldBeGreaterThan(
            0f, "aligning negates the target, so the blend stays on the short arc");

        blended.Z.ShouldBe(
            AboutZ(45f).Z, Tolerance, "halfway to ninety degrees, not halfway to its negation");
    }

    [Test]
    public void SlerpNoAlign_TowardANegatedTarget_TakesTheLongWayRound()
    {
        // The same inputs without the align step, predicted by arithmetic rather than by reading
        // the implementation. The dot product is −cos(45°), so omega is 135 degrees; at the
        // midpoint both of Valve's shares are sin(ω/2)/sin(ω), which the half-angle identity
        // reduces to 1/(2·cos(ω/2)) — a form derived independently of the code under test.
        //
        // **The first version of this predicted 1/(2·sin ω) and was simply wrong**, having assumed
        // the shares were each a half. The test was red against correct code, which is why a
        // prediction gets checked against the failure before the code is touched.
        (float X, float Y, float Z, float W) turned = AboutZ(90f);

        (float X, float Y, float Z, float W) blended =
            StudioBones.SlerpNoAlign(Identity, Negated(turned), Halfway);

        float share = 1f / (2f * MathF.Cos(67.5f * MathF.PI / 180f));

        blended.W.ShouldBe(share * (1f - turned.W), Tolerance);
        blended.Z.ShouldBe(share * -turned.Z, Tolerance);

        blended.Z.ShouldBeLessThan(
            0f, "without the align step the blend travels the long way, through the far side");
    }

    [Test]
    public void Build_WithAFixedAlignmentBone_UsesTheUnalignedBlend()
    {
        // **The whole claim, at the level that matters: what the SKELETON was built with.** The two
        // blends above are functions and could both be correct while `Accumulate` called neither
        // conditionally — which is the wiring gap that has shipped three no-ops in this repository.
        BoneAccessor aligned = Built(fixedAlignment: false);
        BoneAccessor unaligned = Built(fixedAlignment: true);

        // Ninety degrees about Z takes the bone's X axis onto Y, so the sign of that matrix entry
        // says which way round the blend went.
        float alignedY = aligned.BoneForWrite(1)[4];
        float unalignedY = unaligned.BoneForWrite(1)[4];

        alignedY.ShouldBeGreaterThan(0f, "the aligning blend turns the short way, onto +Y");
        unalignedY.ShouldBeLessThan(0f, "the unaligned blend turns the other way");
    }

    [Test]
    public void Build_WithAFixedAlignmentBone_LeavesUnflaggedBonesAligned()
    {
        // **The control, and without it "honoured the flag" and "stopped aligning everything" are
        // the same observation.** Bone 0 carries no flag in either fixture and takes the layer at
        // the same weight, so its blend must be identical across the two.
        float aligned = Built(fixedAlignment: false).BoneForWrite(0)[4];
        float unaligned = Built(fixedAlignment: true).BoneForWrite(0)[4];

        unaligned.ShouldBe(
            aligned, Tolerance, "bone 0 carries no flag, so nothing about it should change");

        aligned.ShouldBeGreaterThan(0f, "and it took the aligning blend, onto +Y");
    }

    /// <summary>The skeleton after one layer over a base, with bone 1 flagged or not.</summary>
    /// <remarks>
    /// **The base and the layer must DIFFER, and the first version of this fixture returned one
    /// rotation for every sequence.** Blending a pose with itself gives that pose back whichever
    /// blend runs, so both fixtures produced the same full ninety-degree turn and the first
    /// assertion passed on it — an experiment insensitive to its own manipulation.
    ///
    /// **The layer's rotation is NEGATED on purpose**, which is the other half. For any pair with a
    /// positive dot product the align step does nothing, so aligned and unaligned predict the same
    /// observation and a fixture built from a plain rotation could not fail either.
    /// </remarks>
    private static BoneAccessor Built(bool fixedAlignment)
    {
        BoneAccessor into = new(2);

        SkeletonPose pose = new(Bones(fixedAlignment), Sampler())
        {
            Sequence = BaseSequence,
            Layers =
            [
                new PoseLayer(
                    Sequence: LayerSequence,
                    Frame: 0,
                    FrameFraction: 0f,
                    Weight: Halfway,
                    BoneWeights: [1f, 1f],
                    Delta: false,
                    Post: false),
            ],
        };

        pose.Build(boneMask: ~0, currentTime: 0d, into, new BoneBitList(2));

        return into;
    }

    /// <summary>The sequence the body holds: no rotation at all.</summary>
    private const int BaseSequence = 0;

    /// <summary>The sequence the layer plays: a negated ninety-degree turn.</summary>
    private const int LayerSequence = 1;

    /// <summary>A sampler answering by SEQUENCE, so the base and the layer are different poses.</summary>
    private static Func<int, int, float, IReadOnlyList<float>, IReadOnlyList<StudioBonePose>>
        Sampler() =>
        (sequence, _, _, _) =>
        {
            (float X, float Y, float Z, float W) rotation =
                sequence == LayerSequence ? Negated(AboutZ(90f)) : Identity;

            return
            [
                new StudioBonePose(0, (0f, 0f, 0f), rotation),
                new StudioBonePose(1, (0f, 0f, 0f), rotation),
            ];
        };

    /// <summary>Two parentless bones, the second optionally flagged.</summary>
    private static IReadOnlyList<StudioBone> Bones(bool fixedAlignment) =>
    [
        new StudioBone("plain", -1, (0f, 0f, 0f), Identity, default, Flags: ~0 & ~Fixed),
        new StudioBone(
            "constrained",
            -1,
            (0f, 0f, 0f),
            Identity,
            default,
            Flags: fixedAlignment ? ~0 : ~0 & ~Fixed),
    ];

    /// <summary><c>BONE_FIXED_ALIGNMENT</c>.</summary>
    private const int Fixed = StudioBoneFlags.FixedAlignment;

    /// <summary>No rotation at all.</summary>
    private static (float X, float Y, float Z, float W) Identity => (0f, 0f, 0f, 1f);

    /// <summary>A rotation about the Z axis, in degrees.</summary>
    private static (float X, float Y, float Z, float W) AboutZ(float degrees)
    {
        float half = degrees * MathF.PI / 360f;

        return (0f, 0f, MathF.Sin(half), MathF.Cos(half));
    }

    /// <summary>The same rotation written the other way round.</summary>
    private static (float X, float Y, float Z, float W) Negated(
        (float X, float Y, float Z, float W) rotation) =>
        (-rotation.X, -rotation.Y, -rotation.Z, -rotation.W);
}
