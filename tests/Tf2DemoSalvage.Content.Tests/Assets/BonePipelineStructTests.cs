using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// The structures Valve's bone pipeline reads, derived from <c>studio.h</c> rather than remembered.
/// </summary>
/// <remarks>
/// **D88 commits this project to matching the engine's bone ARCHITECTURE, and every stage of it is
/// blocked here.** B182's denominator found bone controllers, IK, procedural and jiggle bones and
/// local hierarchy all absent — and they are absent at the bottom of the stack rather than at the
/// top: the <c>.mdl</c> reader does not read the structures that carry their data, so the stages
/// have nothing to run on.
///
/// **Derived, not compared.** <see cref="CStruct"/> reads each declaration out of the SDK and this
/// asserts the offsets the readers use against the positions the compiler would give them. The
/// values were produced by <c>BonePipelineStructProbe</c>, not by hand: <c>mstudiojigglebone_t</c>
/// is thirty-five consecutive floats, and counting a run that long has no error signal.
///
/// **Why offsets and not just strides**, restating what <c>StudioStructTests</c> already learned:
/// a stride can be right while the members inside it are read from the wrong places, because the
/// total is the same either way. Both are asserted.
/// </remarks>
public sealed class BonePipelineStructTests
{
    /// <summary>Where the engine declares the studio model structures.</summary>
    private const string StudioFile = "src/public/studio.h";

    [SetUp]
    public void RequireTheSdk()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
        }
    }

    [Test]
    public void Bone_TheThreeFieldsTheReaderNeverRead_AreWhereTheLayoutSaysTheyAre()
    {
        CLayout bone = Layout("mstudiobone_t");

        // The mask the whole engine pipeline is gated on, and the procedural rule beside it.
        bone.Offset("flags").ShouldBe(StudioLayout.BoneFlagsOffset);
        bone.Offset("proctype").ShouldBe(StudioLayout.BoneProcedureTypeOffset);
        bone.Offset("procindex").ShouldBe(StudioLayout.BoneProcedureIndexOffset);

        bone.Offset("bonecontroller").ShouldBe(StudioLayout.BoneControllerListOffset);

        // **`qAlignment`, which `CalcBoneQuaternion` aligns an animated rotation to** for a bone
        // carrying `BONE_FIXED_ALIGNMENT` (`bone_setup.cpp:470`). It sits in the gap between
        // `poseToBone` and `flags` and was unread until B308 — a gap nothing announces, since a
        // reader that skipped it still got every field on either side right.
        bone.Offset("qAlignment").ShouldBe(StudioLayout.BoneAlignmentOffset);

        // **The lock a sequence puts on an IK chain** (B311). Half its 32 bytes are an unused run,
        // so a stride computed from the four fields alone would be 16 and would read every lock
        // after the first from the wrong place.
        CLayout iklock = Layout("mstudioiklock_t");

        iklock.Size.ShouldBe(StudioLayout.IkLockStride);
        iklock.Offset("flPosWeight").ShouldBe(StudioLayout.IkLockPositionWeightOffset);
        iklock.Offset("flLocalQWeight").ShouldBe(StudioLayout.IkLockRotationWeightOffset);
        iklock.Offset("flags").ShouldBe(StudioLayout.IkLockFlagsOffset);

        // **The count matters as much as the offset**, because the six slots are read as a run: one
        // too few silently drops the ZR axis, one too many reads `pos.x` as a controller index and
        // then indexes the controller table with a float's bit pattern.
        bone.Members
            .First(member => string.Equals(member.Name, "bonecontroller", StringComparison.Ordinal))
            .Elements
            .ShouldBe(StudioLayout.BoneControllerSlots);

        // The control: a field the reader already gets right, so a layout that returned zeros
        // everywhere would fail here rather than passing the three assertions above.
        bone.Offset("poseToBone").ShouldBe(StudioLayout.BonePoseToBoneOffset);
        bone.Size.ShouldBe(StudioLayout.BoneStride);
    }

    [Test]
    public void Header_TheTwoTablesTheBonePipelineNeeds_AreWhereTheLayoutSaysTheyAre()
    {
        CLayout header = Layout("studiohdr_t");

        header.Offset("numbonecontrollers").ShouldBe(StudioLayout.HeaderBoneControllerCountOffset);
        header.Offset("bonecontrollerindex").ShouldBe(StudioLayout.HeaderBoneControllerIndexOffset);
        header.Offset("numikchains").ShouldBe(StudioLayout.HeaderIkChainCountOffset);
        header.Offset("ikchainindex").ShouldBe(StudioLayout.HeaderIkChainIndexOffset);

        // The control, and it earns its place here more than most: an MDL header is fifty integers
        // in a row where every count is followed by an index, so being four bytes out lands on the
        // other half of a pair rather than on nonsense.
        header.Offset("numbones").ShouldBe(StudioLayout.HeaderBoneCountOffset);
    }

    [Test]
    public void BoneController_TheStructThatGivesAWireValueMeaning_MatchesTheLayout()
    {
        CLayout controller = Layout("mstudiobonecontroller_t");

        controller.Size.ShouldBe(StudioLayout.BoneControllerStride);
        controller.Offset("bone").ShouldBe(StudioLayout.BoneControllerBoneOffset);
        controller.Offset("type").ShouldBe(StudioLayout.BoneControllerTypeOffset);
        controller.Offset("start").ShouldBe(StudioLayout.BoneControllerStartOffset);
        controller.Offset("end").ShouldBe(StudioLayout.BoneControllerEndOffset);
    }

    [Test]
    public void IkChain_ItsLinkTable_MatchesTheLayout()
    {
        CLayout chain = Layout("mstudioikchain_t");

        // Sixteen bytes with NO unused tail, which is unusual in this header: its neighbours all
        // end in int unused[8], and this one carries a Valve to-do note saying those entries still
        // need adding. Asserted rather than assumed, because assuming the usual shape would add 32
        // bytes and read every chain after the first from the wrong place.
        chain.Size.ShouldBe(StudioLayout.IkChainStride);
        chain.Offset("sznameindex").ShouldBe(StudioLayout.IkChainNameOffset);
        chain.Offset("linktype").ShouldBe(StudioLayout.IkChainLinkTypeOffset);
        chain.Offset("numlinks").ShouldBe(StudioLayout.IkChainLinkCountOffset);
        chain.Offset("linkindex").ShouldBe(StudioLayout.IkChainLinkIndexOffset);
    }

    [Test]
    public void IkLink_TheJointItself_MatchesTheLayout()
    {
        CLayout link = Layout("mstudioiklink_t");

        link.Size.ShouldBe(StudioLayout.IkLinkStride);
        link.Offset("bone").ShouldBe(StudioLayout.IkLinkBoneOffset);
        link.Offset("kneeDir").ShouldBe(StudioLayout.IkLinkKneeDirectionOffset);
    }

    [Test]
    public void JiggleBone_TheStrideNobodyCanCountByHand_MatchesTheLayout()
    {
        CLayout jiggle = Layout("mstudiojigglebone_t");

        jiggle.Size.ShouldBe(StudioLayout.JiggleBoneStride);
        jiggle.Offset("flags").ShouldBe(StudioLayout.JiggleFlagsOffset);

        // **Thirty-five members, asserted as a count.** Every one after `flags` is a float at
        // 4 × index, so the reader consumes them sequentially rather than by named offset — which
        // means the thing that can go wrong is the NUMBER of them, not any individual position.
        // This is the assertion that catches a field added or removed between SDK generations.
        //
        // It caught its author first: this said 36, which is the count you get from reading the
        // declaration's comment groups rather than its members. 140 bytes over 4 is 35, and the
        // stride assertion above and this one now agree — which is the point of asserting both.
        jiggle.Members.Count.ShouldBe(35);
        (jiggle.Members.Count * sizeof(float)).ShouldBe(StudioLayout.JiggleBoneStride);
    }

    [Test]
    public void LocalHierarchy_TheReparentingRecord_MatchesTheLayout()
    {
        CLayout hierarchy = Layout("mstudiolocalhierarchy_t");

        hierarchy.Size.ShouldBe(StudioLayout.LocalHierarchyStride);
        hierarchy.Offset("iBone").ShouldBe(StudioLayout.LocalHierarchyBoneOffset);
        hierarchy.Offset("iNewParent").ShouldBe(StudioLayout.LocalHierarchyNewParentOffset);
    }

    [Test]
    public void BoneFlags_EveryMaskTheEngineGatesOn_MatchesItsDeclaration()
    {
        IReadOnlyDictionary<string, int> constants = SourceSdk.Constants(StudioFile);

        constants["BONE_USED_BY_HITBOX"].ShouldBe(StudioBoneFlags.UsedByHitbox);
        constants["BONE_USED_BY_ATTACHMENT"].ShouldBe(StudioBoneFlags.UsedByAttachment);
        constants["BONE_USED_BY_VERTEX_LOD0"].ShouldBe(StudioBoneFlags.UsedByVertexLod0);
        constants["BONE_USED_BY_VERTEX_MASK"].ShouldBe(StudioBoneFlags.UsedByVertexMask);
        constants["BONE_USED_BY_BONE_MERGE"].ShouldBe(StudioBoneFlags.UsedByBoneMerge);
        constants["BONE_USED_BY_ANYTHING"].ShouldBe(StudioBoneFlags.UsedByAnything);
        constants["BONE_ALWAYS_PROCEDURAL"].ShouldBe(StudioBoneFlags.AlwaysProcedural);
        constants["BONE_PHYSICALLY_SIMULATED"].ShouldBe(StudioBoneFlags.PhysicallySimulated);
        constants["BONE_PHYSICS_PROCEDURAL"].ShouldBe(StudioBoneFlags.PhysicsProcedural);
        constants["BONE_FIXED_ALIGNMENT"].ShouldBe(StudioBoneFlags.FixedAlignment);
    }

    [Test]
    public void BoneFlags_UsedByAnything_IsExactlyTheUnionOfTheUsedByBits()
    {
        // **A relationship rather than a value, because the value alone cannot catch the mistake
        // that matters.** BONE_USED_BY_ANYTHING is what the engine falls back to whenever it cannot
        // narrow a request, so a mask missing one bit does not error — it silently stops building
        // that class of bone in exactly the case the engine chose to be safe. Deriving the union
        // and comparing is what makes the constant answerable.
        IReadOnlyDictionary<string, int> constants = SourceSdk.Constants(StudioFile);

        int union =
            constants["BONE_USED_BY_HITBOX"] |
            constants["BONE_USED_BY_ATTACHMENT"] |
            constants["BONE_USED_BY_VERTEX_MASK"] |
            constants["BONE_USED_BY_BONE_MERGE"];

        union.ShouldBe(StudioBoneFlags.UsedByAnything);
    }

    [Test]
    public void ProcedureType_EveryRuleAModelCanDeclare_MatchesItsDeclaration()
    {
        IReadOnlyDictionary<string, int> constants = SourceSdk.Constants(StudioFile);

        constants["STUDIO_PROC_AXISINTERP"].ShouldBe(StudioProcedureType.AxisInterpolate);
        constants["STUDIO_PROC_QUATINTERP"].ShouldBe(StudioProcedureType.QuaternionInterpolate);
        constants["STUDIO_PROC_AIMATBONE"].ShouldBe(StudioProcedureType.AimAtBone);
        constants["STUDIO_PROC_AIMATATTACH"].ShouldBe(StudioProcedureType.AimAtAttachment);
        constants["STUDIO_PROC_JIGGLE"].ShouldBe(StudioProcedureType.Jiggle);
    }

    /// <summary>Derives one structure's layout the way <c>StudioStructTests</c> does.</summary>
    private static CLayout Layout(string name)
    {
        string text = SourceSdk.Text(StudioFile)
            ?? throw new InvalidOperationException($"{StudioFile} is missing from the SDK checkout");

        // Composite sizes and the four-byte pointer are facts about the FILE rather than about this
        // process: studiomdl is a 32-bit tool and writes the structure it compiled.
        Dictionary<string, CTypeSize> composites = new(StringComparer.Ordinal)
        {
            ["Vector"] = new(12, 4),
            ["QAngle"] = new(12, 4),
            ["RadianEuler"] = new(12, 4),
            ["Vector2D"] = new(8, 4),
            ["Vector4D"] = new(16, 4),
            ["Quaternion"] = new(16, 4),
            ["Quaternion48"] = new(6, 2),
            ["matrix3x4_t"] = new(48, 4),
        };

        CLayoutAttempt attempt = CStruct.Attempt(
            text, name, SourceSdk.Constants(StudioFile), composites, pointerBytes: 4);

        return attempt.Layout
            ?? throw new InvalidOperationException(
                $"the layout of {name} could not be derived from {StudioFile}, so its stride is " +
                $"unchecked rather than correct. Stopped at: {attempt.Refused}");
    }
}
