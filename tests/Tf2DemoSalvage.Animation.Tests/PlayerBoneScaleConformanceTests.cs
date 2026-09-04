using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// TF2's three per-bone scales — head, torso and hands (B312).
/// </summary>
/// <remarks>
/// **`C_TFPlayer::BuildTransformations` ends by running all three, unconditionally**
/// (`c_tf_player.cpp:8815`):
///
/// <code>
///   float flHeadScale = m_Shared.InCond( TF_COND_HALLOWEEN_GHOST_MODE ) ? 1.5 : m_flHeadScale;
///   BuildBigHeadTransformations( …, flHeadScale );
///   BuildTorsoScaleTransformations( …, m_flTorsoScale, GetPlayerClass()->GetClassIndex() );
///   BuildHandScaleTransformations( …, m_flHandScale );
/// </code>
///
/// **The call is unconditional and the VALUE is what makes it a no-op**, which is why its absence
/// was invisible: all three fields default to 1, and a scale of 1 draws an identical picture. Each
/// function's own first line is `if ( !pObject || flScale == 1.f ) return;`.
///
/// **They act on the finished model-space matrices**, after the pose is composed — not on the local
/// pose — so this is a post-pass over bone matrices rather than anything the blender does.
///
/// **`MatrixScaleBy` scales the 3×3 basis and leaves the translation alone** (`mathlib_base.cpp:430`).
/// That distinction is the whole of the head case: the head is made bigger where it is, and the hat
/// is then moved outward by hand, because scaling its basis alone would leave it inside the skull.
/// </remarks>
public sealed class PlayerBoneScaleConformanceTests
{
    private const double Tolerance = 1e-4;

    [Test]
    public void Head_AtScaleOne_ChangesNothing()
    {
        // `if ( !pObject || flScale == 1.f ) return;` — the guard every one of the three opens with,
        // and the reason a field nobody read was invisible for the life of the project.
        BoneAccessor bones = Skeleton();

        PlayerBoneScales.Head(bones, Bones, 1f);

        Basis(bones, Head).ShouldBe(1f, Tolerance, "untouched at the default");
    }

    [Test]
    public void Head_AtDoubleScale_GrowsTheHeadWithoutMovingIt()
    {
        BoneAccessor bones = Skeleton();

        PlayerBoneScales.Head(bones, Bones, 2f);

        Basis(bones, Head).ShouldBe(2f, Tolerance, "MatrixScaleBy multiplies the 3x3");
        Position(bones, Head).ShouldBe((0f, 0f, 70f), "and leaves the translation alone");
    }

    /// <remarks>
    /// **The hat is moved as well as scaled, and that is the part a reimplementation drops.**
    /// `MatrixSetTranslation( ( offset * flScale ) + head_position, … )` pushes it out along its own
    /// offset from the head, so it still sits on top of a head twice the size. Scaling its basis
    /// alone would leave it buried inside the skull — a defect that looks like a missing hat.
    /// </remarks>
    [Test]
    public void Head_AtDoubleScale_PushesTheHatOutAlongItsOffset()
    {
        BoneAccessor bones = Skeleton();

        PlayerBoneScales.Head(bones, Bones, 2f);

        // The hat sits 4 above the head at 70, so its offset is 4 and it lands at 70 + 8.
        Position(bones, Hat).ShouldBe((0f, 0f, 78f), "(74 - 70) * 2 + 70");
        Basis(bones, Hat).ShouldBe(2f, Tolerance, "and it is scaled too");
    }

    /// <remarks>
    /// **The torso COMPRESSES rather than scaling**: each spine bone is pulled toward the one below
    /// it, `vTargetBonePos + flScale * ( vMoveBonePos - vTargetBonePos )`, with the pelvis as the
    /// first target and each moved bone becoming the next. Nothing's basis is touched at all.
    /// </remarks>
    [Test]
    public void Torso_AtHalfScale_PullsEachSpineBoneTowardTheOneBelow()
    {
        BoneAccessor bones = Skeleton();

        PlayerBoneScales.Torso(bones, Bones, 0.5f);

        // Pelvis 0, spine_0 at 10: 0 + 0.5 * 10 = 5.
        Position(bones, Spine0).Z.ShouldBe(5f, Tolerance);

        // **spine_1 lands on 10, and predicting 12.5 was the mistake worth recording.** It is a
        // DESCENDANT of spine_0, so the child carry has already pulled it from 20 down to 15 before
        // its own step runs — and its step then measures from the moved spine_0 at 5:
        // 5 + 0.5 * (15 - 5) = 10. Reading the two halves of the loop separately gives 12.5; they
        // interact, which is why Valve's comment says the order is load-bearing.
        Position(bones, Spine1).Z.ShouldBe(
            10f, Tolerance, "already carried to 15 by spine_0's offset, then compressed from there");

        Basis(bones, Spine0).ShouldBe(1f, Tolerance, "a compression, not a scale");
    }

    /// <remarks>
    /// **A moved spine bone drags its children with it**, by the offset it moved — which is what
    /// keeps the arms and head attached to a compressed torso instead of floating where the spine
    /// used to be.
    /// </remarks>
    [Test]
    public void Torso_AtHalfScale_CarriesTheChildrenOfEachMovedBone()
    {
        BoneAccessor bones = Skeleton();

        float before = Position(bones, Head).Z;

        PlayerBoneScales.Torso(bones, Bones, 0.5f);

        Position(bones, Head).Z.ShouldBeLessThan(
            before, "the head hangs off the neck and the neck moved down");
    }

    /// <remarks>
    /// **A missing torso bone stops the pass where it stands — and does NOT undo what it already
    /// did.** `if ( iMoveBone == -1 ) return;` sits INSIDE the loop, so every spine bone before the
    /// gap has already been moved and stays moved. The model is left half compressed.
    ///
    /// **This test first asserted the opposite**, on the strength of a comment reasoning that "half
    /// a compression is worse than none, so Valve must bail before touching anything". That is an
    /// argument from taste about what the engine ought to do, and the engine does the other thing.
    /// The rule this project keeps relearning: read the branch, do not reason about what would be
    /// sensible (`docs/memory/ask-valve-before-designing-not-after.md`).
    /// </remarks>
    [Test]
    public void Torso_WithASpineBoneMissing_KeepsWhatItAlreadyMoved()
    {
        IReadOnlyList<StudioBone> partial =
        [
            Named("bip_pelvis", -1),
            Named("bip_spine_0", 0),

            // No spine_1, so the run stops after spine_0 has already been moved.
        ];

        BoneAccessor bones = new(partial.Count);

        Place(bones, 0, 0f);
        Place(bones, 1, 10f);

        PlayerBoneScales.Torso(bones, partial, 0.5f);

        Position(bones, 1).Z.ShouldBe(
            5f, Tolerance, "spine_0 was compressed before the missing spine_1 ended the run");
    }

    /// <remarks>
    /// **The hands scale themselves AND every descendant**, because a finger is a child of the hand
    /// and would otherwise stay its original size on a giant palm.
    /// </remarks>
    [Test]
    public void Hands_AtDoubleScale_ScaleTheHandAndItsFingers()
    {
        BoneAccessor bones = Skeleton();

        PlayerBoneScales.Hands(bones, Bones, 2f);

        Basis(bones, HandLeft).ShouldBe(2f, Tolerance);
        Basis(bones, Finger).ShouldBe(2f, Tolerance, "a descendant, reached recursively");
        Basis(bones, Head).ShouldBe(1f, Tolerance, "the control: the head is not a hand's child");
    }

    private const int Pelvis = 0;
    private const int Spine0 = 1;
    private const int Spine1 = 2;
    private const int Spine2 = 3;
    private const int Spine3 = 4;
    private const int Neck = 5;
    private const int Head = 6;
    private const int Hat = 7;
    private const int HandLeft = 8;
    private const int Finger = 9;

    /// <summary>A skeleton with the bones all three passes look up, by TF2's own names.</summary>
    private static IReadOnlyList<StudioBone> Bones =>
    [
        Named("bip_pelvis", -1),
        Named("bip_spine_0", Pelvis),
        Named("bip_spine_1", Spine0),
        Named("bip_spine_2", Spine1),
        Named("bip_spine_3", Spine2),
        Named("bip_neck", Spine3),
        Named("bip_head", Neck),
        Named("prp_hat", Head),
        Named("bip_hand_L", Spine3),
        Named("bip_finger", HandLeft),
    ];

    private static StudioBone Named(string name, int parent) =>
        new(name, parent, (0f, 0f, 0f), (0f, 0f, 0f, 1f), default);

    /// <summary>Identity matrices stacked up the Z axis, so a move is readable as one number.</summary>
    private static BoneAccessor Skeleton()
    {
        BoneAccessor bones = new(10);

        Place(bones, Pelvis, 0f);
        Place(bones, Spine0, 10f);
        Place(bones, Spine1, 20f);
        Place(bones, Spine2, 30f);
        Place(bones, Spine3, 40f);
        Place(bones, Neck, 60f);
        Place(bones, Head, 70f);
        Place(bones, Hat, 74f);
        Place(bones, HandLeft, 45f);
        Place(bones, Finger, 47f);

        return bones;
    }

    private static void Place(BoneAccessor bones, int bone, float z)
    {
        float[] matrix = bones.BoneForWrite(bone);

        matrix[0] = 1f;
        matrix[5] = 1f;
        matrix[10] = 1f;
        matrix[11] = z;
    }

    /// <summary>The matrix's own scale, read off the first basis element.</summary>
    private static float Basis(BoneAccessor bones, int bone) => bones.Bone(bone)[0];

    private static (float X, float Y, float Z) Position(BoneAccessor bones, int bone)
    {
        ReadOnlySpan<float> matrix = bones.Bone(bone);

        return (matrix[3], matrix[7], matrix[11]);
    }
}
