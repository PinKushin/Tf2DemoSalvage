using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// A bone controller bends the bone it names — <c>CalcBoneAdj</c>.
/// </summary>
/// <remarks>
/// **<c>bone_setup.cpp:2462</c>**, and the whole of it:
///
/// <code>
///   i = pbonecontroller->inputfield;
///   value = controllers[i];
///   if (value &lt; 0) value = 0;
///   if (value &gt; 1.0) value = 1.0;
///   value = (1.0 - value) * pbonecontroller->start + value * pbonecontroller->end;
///   switch(pbonecontroller->type &amp; STUDIO_TYPES)
///   {
///   case STUDIO_XR: a0.Init( value * (M_PI / 180.0), 0, 0 ); AngleQuaternion( a0, q0 );
///                   QuaternionSM( 1.0, q0, q[k], q[k] ); break;
///   case STUDIO_X:  pos[k].x += value; break;
///   }
/// </code>
///
/// **`m_flEncodedController` IS networked** — eleven bits each over nought to one
/// (`baseanimating.cpp:248`) — which makes this one of the few animation inputs genuinely
/// recoverable from a demo. It was decoded, and the model's controllers were read, and neither was
/// applied to anything (B288).
/// </remarks>
public sealed class BoneControllerConformanceTests
{
    /// <summary>How close two floats must be to count as equal here.</summary>
    private const double Tolerance = 1e-4;

    [Test]
    public void Build_WithATranslationController_MovesTheBoneItNames()
    {
        BoneAccessor into = new(2);

        SkeletonPose pose = Posed(
            new StudioBoneController(
                Bone: 1, Type: StudioBoneController.TranslateX, Start: 0f, End: 10f),
            value: 1f);

        pose.Build(boneMask: ~0, currentTime: 0d, into, new BoneBitList(2));

        XOf(into, 1).ShouldBe(10f, Tolerance, "pos[k].x += value, at the controller's own End");
    }

    /// <remarks>
    /// **The control: the bone it does NOT name must not move.** With one bone in the fixture a
    /// controller that adjusted everything would be indistinguishable from one that adjusted the
    /// right thing.
    /// </remarks>
    [Test]
    public void Build_WithATranslationController_LeavesOtherBonesAlone()
    {
        BoneAccessor into = new(2);

        SkeletonPose pose = Posed(
            new StudioBoneController(
                Bone: 1, Type: StudioBoneController.TranslateX, Start: 0f, End: 10f),
            value: 1f);

        pose.Build(boneMask: ~0, currentTime: 0d, into, new BoneBitList(2));

        XOf(into, 0).ShouldBe(0f, Tolerance, "bone 0 is named by no controller");
    }

    /// <remarks>
    /// **The value is a LERP between the controller's own endpoints**, not the raw input:
    /// <c>value = (1.0 - value) * start + value * end</c>. A reader that used the normalised number
    /// directly would move every bone by at most one unit.
    ///
    /// **The endpoints are asymmetric ON PURPOSE, and they were −30 to 30 until a sabotage said
    /// why that was wrong.** Halfway between −30 and 30 is 0, which is also where the bone rests —
    /// so "the lerp is right" and "the controller never ran at all" predicted the SAME observation,
    /// and the test stayed green while a deliberately broken guard skipped the bone entirely. That
    /// is the wrong-CONDITION fault: an input for which correct and broken agree, fixed by changing
    /// the input rather than by strengthening the assertion.
    ///
    /// **10 to 30 at halfway separates all three readings**: 20 if the lerp is right, 0.5 if the raw
    /// input is used, 0 if nothing is applied.
    /// </remarks>
    [Test]
    public void Build_WithAHalfwayInput_LandsBetweenTheControllersEndpoints()
    {
        BoneAccessor into = new(2);

        SkeletonPose pose = Posed(
            new StudioBoneController(
                Bone: 1, Type: StudioBoneController.TranslateX, Start: 10f, End: 30f),
            value: 0.5f);

        pose.Build(boneMask: ~0, currentTime: 0d, into, new BoneBitList(2));

        XOf(into, 1).ShouldBe(20f, Tolerance, "halfway between 10 and 30, and not the rest position");
    }

    /// <remarks>
    /// **The clamp comes BEFORE the lerp**, which is what stops an out-of-range input extrapolating
    /// past the authored limit — for a rotation controller that would spin the bone somewhere the
    /// animator never allowed.
    /// </remarks>
    [Test]
    public void Build_WithAnOutOfRangeInput_ClampsToTheEndpoint()
    {
        BoneAccessor into = new(2);

        SkeletonPose pose = Posed(
            new StudioBoneController(
                Bone: 1, Type: StudioBoneController.TranslateX, Start: 0f, End: 10f),
            value: 5f);

        pose.Build(boneMask: ~0, currentTime: 0d, into, new BoneBitList(2));

        XOf(into, 1).ShouldBe(10f, Tolerance, "clamped to one, not extrapolated to fifty");
    }

    /// <remarks>
    /// **The input is chosen by <c>inputfield</c>, not by the controller's position in the list.**
    /// Two controllers can share an input and a model's list is not in input order, so this fixture
    /// deliberately puts the controller at index zero and points it at input two.
    /// </remarks>
    [Test]
    public void Build_WithAControllerNamingALaterInput_ReadsThatInput()
    {
        BoneAccessor into = new(2);

        SkeletonPose pose = new(Bones(), (_, _, _, _) => [])
        {
            Controllers =
            [
                new StudioBoneController(
                    Bone: 1,
                    Type: StudioBoneController.TranslateX,
                    Start: 0f,
                    End: 10f,
                    InputField: 2),
            ],
            BoneControllers = [0f, 0f, 1f],
        };

        pose.Build(boneMask: ~0, currentTime: 0d, into, new BoneBitList(2));

        XOf(into, 1).ShouldBe(
            10f, Tolerance, "input two is one, and the controller names input two");
    }

    /// <remarks>
    /// **A rotation is in DEGREES**, which the engine shows by converting only those cases:
    /// <c>value * (M_PI / 180.0)</c>. Ninety degrees about Z turns the bone's own X axis onto Y, so
    /// a translation-free bone's matrix reports it — and a reader that fed degrees straight into a
    /// quaternion would turn it by about a thousandth of that.
    /// </remarks>
    [Test]
    public void Build_WithARotationController_TurnsTheBoneInDegrees()
    {
        BoneAccessor into = new(2);

        SkeletonPose pose = Posed(
            new StudioBoneController(
                Bone: 1, Type: StudioBoneController.RotateZ, Start: 0f, End: 90f),
            value: 1f);

        pose.Build(boneMask: ~0, currentTime: 0d, into, new BoneBitList(2));

        float[] matrix = into.BoneForWrite(1);

        // A row-major 3x4: the first column is where the bone's own X axis points.
        matrix[0].ShouldBe(0f, 0.001d, "ninety degrees about Z takes X off X");
        matrix[4].ShouldBe(1f, 0.001d, "and onto Y");
    }

    /// <summary>A pose carrying one controller and one input value.</summary>
    /// <remarks>
    /// **The engine's own guard, and the only branch of <c>CalcBoneAdj</c> we did not have**
    /// (<c>bone_setup.cpp:2480</c>):
    ///
    /// <code>
    ///   k = pbonecontroller->bone;
    ///   if (pStudioHdr->boneFlags( k ) &amp; boneMask)
    /// </code>
    ///
    /// **A bone outside the mask is skipped entirely** — the lerp is not even computed. Production
    /// asks for <c>BONE_USED_BY_ANYTHING</c> (`0x0007FF00`, `EntityModels.cs:839` and `:3663`), so
    /// the test reduces to "the bone is used by a hitbox, an attachment or a vertex at some LOD",
    /// and a bone flagged only <c>BONE_ALWAYS_PROCEDURAL</c> (`0x04`) fails it.
    ///
    /// **The symptom of getting this wrong is nothing visible, and that is worth stating plainly.**
    /// Every <c>BONE_USED_BY_*</c> flag reads *"bone (or child) is used by"*, so a bone with none of
    /// them has no descendant used by anything either — bending it moves nothing that is drawn,
    /// hit-tested or hung from. The guard is Valve's economy, not Valve's correctness. It is here
    /// because an optimisation of the engine's is not a departure this project gets to skip
    /// (`docs/memory/an-optimisation-is-not-a-skippable-departure.md`), and because a mask narrower
    /// than <c>UsedByAnything</c> would make it correctness immediately.
    /// </remarks>
    [Test]
    public void Build_WithAControllerOnABoneOutsideTheMask_LeavesItAlone()
    {
        BoneAccessor into = new(2);

        SkeletonPose pose = new(
            [
                new StudioBone("root", -1, (0f, 0f, 0f), (0f, 0f, 0f, 1f), default, Flags: ~0),

                // Procedural, and used by nothing: the exact bone the engine's guard rejects.
                new StudioBone(
                    "helper", -1, (0f, 0f, 0f), (0f, 0f, 0f, 1f), default,
                    Flags: StudioBoneFlags.AlwaysProcedural),
            ],
            (_, _, _, _) => [])
        {
            Controllers =
            [
                new StudioBoneController(
                    Bone: 1, Type: StudioBoneController.TranslateX, Start: 0f, End: 10f),
            ],
            BoneControllers = [1f],
        };

        pose.Build(boneMask: ~0, currentTime: 0d, into, new BoneBitList(2));

        XOf(into, 1).ShouldBe(
            0f, Tolerance, "boneFlags(k) & BONE_USED_BY_ANYTHING is zero, so the engine skips it");
    }

    private static SkeletonPose Posed(StudioBoneController controller, float value) =>
        new(Bones(), (_, _, _, _) => [])
        {
            Controllers = [controller],
            BoneControllers = [value],
        };

    /// <summary>Two parentless bones at the origin, so a matrix reports one bone alone.</summary>
    private static IReadOnlyList<StudioBone> Bones() =>
    [
        new StudioBone("root", -1, (0f, 0f, 0f), (0f, 0f, 0f, 1f), default, Flags: ~0),
        new StudioBone("driven", -1, (0f, 0f, 0f), (0f, 0f, 0f, 1f), default, Flags: ~0),
    ];

    /// <summary>The X translation of a built bone matrix.</summary>
    private static float XOf(BoneAccessor bones, int bone) => bones.BoneForWrite(bone)[3];
}
