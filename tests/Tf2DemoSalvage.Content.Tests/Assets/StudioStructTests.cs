using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// Every studio model size and field offset, derived from the engine's own declaration of it.
/// </summary>
/// <remarks>
/// **An MDL header is fifty integers in a row and almost all of them are read by offset.** Every
/// count is followed by an index, so being four bytes out does not land on nonsense — it lands on the
/// other half of a pair, or on the next pair entirely. Reading <c>numbodyparts</c> at 236 returns
/// <c>bodypartindex</c>, a large positive number, and the model comes out with thousands of body
/// parts instead of two. Nothing throws; the file is perfectly valid.
///
/// **Derived, not compared**, exactly as the BSP structures are: <see cref="CStruct"/> reads
/// <c>studiohdr_t</c> out of <c>public/studio.h</c> and this asserts the position each member lands
/// on against the constant the readers use.
///
/// **studio.h is harder to parse than bspfile.h and that is the point of doing it.** Its structures
/// interleave members with inline method bodies, so a member sits between two braces rather than
/// after a semicolon. Anything the parser cannot resolve comes back as a refusal and fails here
/// rather than quietly skipping a structure.
/// </remarks>
public sealed class StudioStructTests
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
    public void TheHeaderCountsAndIndices_SitWhereTheEngineDeclaresThem()
    {
        CLayout header = Layout("studiohdr_t");

        header.Offset("name").ShouldBe(StudioLayout.HeaderNameOffset);

        // **Every one of these is a count immediately followed by its index**, which is what makes a
        // four-byte error produce a plausible model instead of an exception. Each is named so a
        // failure says which pair moved.
        header.Offset("numbones").ShouldBe(StudioLayout.HeaderBoneCountOffset);
        header.Offset("boneindex").ShouldBe(StudioLayout.HeaderBoneIndexOffset);
        header.Offset("numlocalanim").ShouldBe(StudioLayout.HeaderAnimationCountOffset);
        header.Offset("localanimindex").ShouldBe(StudioLayout.HeaderAnimationIndexOffset);
        header.Offset("numlocalseq").ShouldBe(StudioLayout.HeaderSequenceCountOffset);
        header.Offset("localseqindex").ShouldBe(StudioLayout.HeaderSequenceIndexOffset);
        header.Offset("numtextures").ShouldBe(StudioLayout.HeaderTextureCountOffset);
        header.Offset("textureindex").ShouldBe(StudioLayout.HeaderTextureIndexOffset);
        header.Offset("numcdtextures").ShouldBe(StudioLayout.HeaderFolderCountOffset);
        header.Offset("cdtextureindex").ShouldBe(StudioLayout.HeaderFolderIndexOffset);
        header.Offset("numbodyparts").ShouldBe(StudioLayout.HeaderBodyPartCountOffset);
        header.Offset("bodypartindex").ShouldBe(StudioLayout.HeaderBodyPartIndexOffset);
    }

    [Test]
    public void TheSkinTableFields_SitWhereTheEngineDeclaresThem()
    {
        // **Three fields, not two, and the odd one is the trap.** numskinref and numskinfamilies are
        // adjacent and both small; swapping them gives a table that indexes correctly for family
        // zero and walks off the end for any other, which is invisible on a model whose skin is
        // never changed and wrong on every team-coloured one.
        CLayout header = Layout("studiohdr_t");

        header.Offset("numskinref").ShouldBe(StudioLayout.HeaderSkinReferenceCountOffset);
        header.Offset("numskinfamilies").ShouldBe(StudioLayout.HeaderSkinFamilyCountOffset);
        header.Offset("skinindex").ShouldBe(StudioLayout.HeaderSkinTableOffset);
    }

    [Test]
    public void ThePoseParameterAndIncludeFields_SitWhereTheEngineDeclaresThem()
    {
        CLayout header = Layout("studiohdr_t");

        header.Offset("numlocalposeparameters")
            .ShouldBe(StudioLayout.HeaderPoseParameterCountOffset);
        header.Offset("localposeparamindex")
            .ShouldBe(StudioLayout.HeaderPoseParameterIndexOffset);
        header.Offset("numincludemodels").ShouldBe(StudioLayout.HeaderIncludeCountOffset);
        header.Offset("includemodelindex").ShouldBe(StudioLayout.HeaderIncludeIndexOffset);
    }

    [Test]
    public void TheAttachmentLayout_MatchesItsDeclaration()
    {
        // **Written before the reader exists, which is the point.** B82 is open — items parented to
        // an attachment sit at the wearer's feet because nothing reads these — and the order of work
        // here puts the conformance test first. When the reader arrives it starts from numbers that
        // were checked against studio.h before any code depended on them, rather than from numbers
        // that looked right while the picture looked wrong.
        CLayout header = Layout("studiohdr_t");

        header.Offset("numlocalattachments").ShouldBe(StudioLayout.HeaderAttachmentCountOffset);
        header.Offset("localattachmentindex").ShouldBe(StudioLayout.HeaderAttachmentIndexOffset);

        CLayout attachment = Layout("mstudioattachment_t");

        attachment.Size.ShouldBe(StudioLayout.AttachmentStride);
        attachment.Offset("sznameindex").ShouldBe(StudioLayout.AttachmentNameOffset);
        attachment.Offset("flags").ShouldBe(StudioLayout.AttachmentFlagsOffset);
        attachment.Offset("localbone").ShouldBe(StudioLayout.AttachmentBoneOffset);
        attachment.Offset("local").ShouldBe(StudioLayout.AttachmentMatrixOffset);
    }

    [Test]
    public void StudioStructs_AnAttachment_CarriesItsOwnTransform()
    {
        // **The fact B82's fix turns on, stated where it cannot be forgotten.** An attachment is not
        // just a bone reference: it carries a 3x4 matrix positioning the point relative to that
        // bone. A fix that read localbone and stopped would place every item AT the bone, which is
        // the bone-merge behaviour this project already has and is exactly the symptom being
        // investigated — close enough for a hat to look almost right and wrong for everything else.
        CLayout attachment = Layout("mstudioattachment_t");

        int matrix = attachment.Offset("local");
        int bone = attachment.Offset("localbone");

        (matrix - bone).ShouldBe(4, "the matrix follows the bone index immediately");

        // 48 bytes of matrix, not 12 of position: it carries rotation as well, so an item can be
        // turned by its attachment point rather than only moved.
        attachment.Members
            .First(member => member.Name == "local")
            .Size
            .ShouldBe(48);
    }

    [Test]
    public void TheBoneLayout_MatchesItsDeclaration()
    {
        CLayout bone = Layout("mstudiobone_t");

        bone.Size.ShouldBe(StudioLayout.BoneStride);

        bone.Offset("sznameindex").ShouldBe(StudioLayout.BoneNameOffset);
        bone.Offset("parent").ShouldBe(StudioLayout.BoneParentOffset);
        bone.Offset("pos").ShouldBe(StudioLayout.BonePositionOffset);
        bone.Offset("quat").ShouldBe(StudioLayout.BoneRotationOffset);
        bone.Offset("rot").ShouldBe(StudioLayout.BoneEulerOffset);
        bone.Offset("posscale").ShouldBe(StudioLayout.BonePositionScaleOffset);
        bone.Offset("rotscale").ShouldBe(StudioLayout.BoneRotationScaleOffset);
        bone.Offset("poseToBone").ShouldBe(StudioLayout.BonePoseToBoneOffset);
    }

    [Test]
    public void StudioStructs_QuaternionAndEulerRotations_AreSeparateFields()
    {
        // **Stated as its own claim because they are both rotations and both correct at rest.** A
        // reader that took quat where the engine takes rot poses a standing model perfectly and
        // animates it wrongly, which is the hardest kind of defect to attribute.
        CLayout bone = Layout("mstudiobone_t");

        bone.Offset("quat").ShouldNotBe(bone.Offset("rot"));
        (bone.Offset("rot") - bone.Offset("quat")).ShouldBe(16, "a Quaternion is four floats");
    }

    [Test]
    public void TheSequenceLayout_MatchesItsDeclaration()
    {
        CLayout sequence = Layout("mstudioseqdesc_t");

        sequence.Size.ShouldBe(StudioLayout.SequenceStride);

        sequence.Offset("szlabelindex").ShouldBe(StudioLayout.SequenceLabelOffset);
        sequence.Offset("flags").ShouldBe(StudioLayout.SequenceFlagsOffset);

        // **The activity pair, derived rather than counted by hand.** These sit between two fields
        // this test already pins, so a wrong offset here would read the label pointer or the flags
        // as an activity name — a plausible-looking string index that resolves to the wrong text,
        // or a number where a string was wanted. Neither would throw.
        sequence.Offset("szactivitynameindex")
            .ShouldBe(StudioLayout.SequenceActivityNameOffset);

        sequence.Offset("actweight").ShouldBe(StudioLayout.SequenceActivityWeightOffset);
        sequence.Offset("animindexindex").ShouldBe(StudioLayout.SequenceAnimationIndexOffset);
        sequence.Offset("groupsize").ShouldBe(StudioLayout.SequenceGroupSizeOffset);
        sequence.Offset("paramindex").ShouldBe(StudioLayout.SequenceParameterIndexOffset);
        sequence.Offset("paramstart").ShouldBe(StudioLayout.SequenceParameterStartOffset);
        sequence.Offset("paramend").ShouldBe(StudioLayout.SequenceParameterEndOffset);

        // **Added late, and it was the only offset in this structure that nothing checked.**
        // B112's gesture work needed the per-bone weight list, and its offset was derived by
        // counting forward from the verified members either side of it rather than read off a
        // declaration. That is exactly the kind of arithmetic this file exists to confirm: a
        // wrong offset here reads a neighbouring field as a pointer and returns weights that are
        // plausible floats, so nothing throws and every gesture is quietly mis-weighted.
        sequence.Offset("weightlistindex").ShouldBe(StudioLayout.SequenceWeightListIndexOffset);
    }

    [Test]
    public void TheAnimationDescriptionLayout_MatchesItsDeclaration()
    {
        CLayout animation = Layout("mstudioanimdesc_t");

        animation.Size.ShouldBe(StudioLayout.AnimationStride);

        animation.Offset("fps").ShouldBe(StudioLayout.AnimationFramesPerSecondOffset);
        animation.Offset("numframes").ShouldBe(StudioLayout.AnimationFrameCountOffset);
        animation.Offset("animblock").ShouldBe(StudioLayout.AnimationBlockOffset);
        animation.Offset("animindex").ShouldBe(StudioLayout.AnimationDataOffset);

        // The two that point at the movement table, checked here because the table itself is
        // checked below and a correct stride reached from a wrong index reads the wrong bytes.
        animation.Offset("nummovements").ShouldBe(StudioLayout.AnimationMovementCountOffset);
        animation.Offset("movementindex").ShouldBe(StudioLayout.AnimationMovementIndexOffset);
    }

    [Test]
    public void TheMovementLayout_MatchesItsDeclaration()
    {
        // **The last unverified group in StudioLayout**, found by comparing its 83 constants
        // against the ones this file names: everything else was covered and the whole
        // mstudiomovement_t block was not. It is the locomotion table -- how far and which way an
        // animation carries the player -- so a wrong offset here moves a running player by the
        // wrong distance or in the wrong direction, which reads as a physics problem rather than
        // as a parsing one.
        CLayout movement = Layout("mstudiomovement_t");

        movement.Size.ShouldBe(StudioLayout.MovementStride);

        movement.Offset("endframe").ShouldBe(StudioLayout.MovementEndFrameOffset);
        movement.Offset("v0").ShouldBe(StudioLayout.MovementStartVelocityOffset);
        movement.Offset("v1").ShouldBe(StudioLayout.MovementEndVelocityOffset);
        movement.Offset("angle").ShouldBe(StudioLayout.MovementAngleOffset);
        movement.Offset("vector").ShouldBe(StudioLayout.MovementVectorOffset);
        movement.Offset("position").ShouldBe(StudioLayout.MovementPositionOffset);
    }

    [Test]
    public void TheBodyPartLayout_MatchesItsDeclaration()
    {
        CLayout part = Layout("mstudiobodyparts_t");

        part.Size.ShouldBe(StudioLayout.BodyPartStride);

        part.Offset("nummodels").ShouldBe(StudioLayout.BodyPartModelCountOffset);
        part.Offset("base").ShouldBe(StudioLayout.BodyPartBaseOffset);
        part.Offset("modelindex").ShouldBe(StudioLayout.BodyPartModelIndexOffset);
    }

    [Test]
    public void TheModelAndMeshLayouts_MatchTheirDeclarations()
    {
        // mstudiomodel_t embeds mstudio_modelvertexdata_t, which is two runtime pointers the file
        // still reserves room for. Derived from its own declaration rather than stated, so a change
        // to it moves these offsets here instead of silently disagreeing.
        CLayout model = Layout(
            "mstudiomodel_t",
            new Dictionary<string, CTypeSize>(StringComparer.Ordinal)
            {
                ["mstudio_modelvertexdata_t"] = new(Layout("mstudio_modelvertexdata_t").Size, 4),
            });

        model.Size.ShouldBe(StudioLayout.ModelStride);
        model.Offset("nummeshes").ShouldBe(StudioLayout.ModelMeshCountOffset);
        model.Offset("meshindex").ShouldBe(StudioLayout.ModelMeshIndexOffset);
        model.Offset("numvertices").ShouldBe(StudioLayout.ModelVertexCountOffset);
        model.Offset("vertexindex").ShouldBe(StudioLayout.ModelVertexIndexOffset);

        CLayout mesh = Layout(
            "mstudiomesh_t",
            new Dictionary<string, CTypeSize>(StringComparer.Ordinal)
            {
                ["mstudio_meshvertexdata_t"] = new(Layout("mstudio_meshvertexdata_t").Size, 4),
            });

        mesh.Size.ShouldBe(StudioLayout.MeshStride);
        mesh.Offset("material").ShouldBe(StudioLayout.MeshMaterialOffset);
        mesh.Offset("numvertices").ShouldBe(StudioLayout.MeshVertexCountOffset);
        mesh.Offset("vertexoffset").ShouldBe(StudioLayout.MeshVertexOffset);
    }

    [Test]
    public void TheVertexLayout_MatchesItsDeclaration()
    {
        // mstudiovertex_t opens with a boneweight structure whose size decides every offset after
        // it, so that type is derived from its own declaration rather than stated here.
        CLayout vertex = Layout(
            "mstudiovertex_t",
            new Dictionary<string, CTypeSize>(StringComparer.Ordinal)
            {
                ["mstudioboneweight_t"] = new(Layout("mstudioboneweight_t").Size, 4),
            });

        vertex.Size.ShouldBe(StudioLayout.VertexStride);
        vertex.Offset("m_vecPosition").ShouldBe(StudioLayout.VertexPositionOffset);
        vertex.Offset("m_vecNormal").ShouldBe(StudioLayout.VertexNormalOffset);
        vertex.Offset("m_vecTexCoord").ShouldBe(StudioLayout.VertexTexCoordOffset);

        // The bone indices sit inside the weights, after the three floats.
        Layout("mstudioboneweight_t").Offset("bone").ShouldBe(StudioLayout.VertexBoneIndexOffset);
    }

    [Test]
    public void TheTextureAndPoseParameterAndGroupSizes_MatchTheirDeclarations()
    {
        Layout("mstudiotexture_t").Size.ShouldBe(StudioLayout.TextureStride);
        Layout("mstudioposeparamdesc_t").Size.ShouldBe(StudioLayout.PoseParameterStride);

        CLayout group = Layout("mstudiomodelgroup_t");

        group.Size.ShouldBe(StudioLayout.ModelGroupStride);
        group.Offset("sznameindex").ShouldBe(StudioLayout.ModelGroupNameOffset);
    }

    [Test]
    public void StudioStructs_TheseSizes_DescribeTheTargetedVersion()
    {
        // **A layout is only meaningful next to a version.** studio.h declares one, and if it ever
        // moves the constants here describe a file this project no longer reads.
        SourceSdk.Constants(StudioFile)["STUDIO_VERSION"].ShouldBe(48);
    }

    [Test]
    public void StudioStructs_TheParser_AgreesOnAnUndisputedStructure()
    {
        // The control. mstudiomodelgroup_t is two ints and nothing else, in a declaration short
        // enough to verify by reading: a parser dropping members would report fewer than two.
        CLayout group = Layout("mstudiomodelgroup_t");

        group.Members.Count.ShouldBe(2);
        group.Offset("szlabelindex").ShouldBe(0);
        group.Offset("sznameindex").ShouldBe(4);
        group.Size.ShouldBe(8);
    }

    /// <summary>Reads one studio structure's layout, failing rather than skipping when it cannot.</summary>
    private static CLayout Layout(
        string name, IReadOnlyDictionary<string, CTypeSize>? extra = null)
    {
        string text = SourceSdk.Text(StudioFile)
            ?? throw new InvalidOperationException($"{StudioFile} is missing from the SDK checkout");

        Dictionary<string, CTypeSize> composites = new(StringComparer.Ordinal)
        {
            // Sizes stated at the call site rather than inside the parser, because each is a fact
            // about a mathlib type rather than something this header declares.
            ["Vector"] = new(12, 4),
            ["QAngle"] = new(12, 4),
            ["RadianEuler"] = new(12, 4),
            ["Vector2D"] = new(8, 4),
            ["Vector4D"] = new(16, 4),
            ["Quaternion"] = new(16, 4),
            ["Quaternion48"] = new(6, 2),
            ["matrix3x4_t"] = new(48, 4),
        };

        if (extra is not null)
        {
            foreach ((string type, CTypeSize size) in extra)
            {
                composites[type] = size;
            }
        }

        // **Four bytes per pointer, and that is a fact about the FILE rather than about this
        // process.** studiomdl is a 32-bit tool and writes the structure it compiled, so an MDL on
        // disk reserves four bytes for each of studiohdr_t's runtime pointers no matter what reads
        // it. Every offset after `virtualModel` depends on this being right, which is why it is
        // stated here with its reason rather than defaulted inside the parser.
        CLayoutAttempt attempt = CStruct.Attempt(
            text, name, SourceSdk.Constants(StudioFile), composites, pointerBytes: 4);

        return attempt.Layout
            ?? throw new InvalidOperationException(
                $"the layout of {name} could not be derived from {StudioFile}, so its stride is " +
                $"unchecked rather than correct. Stopped at: {attempt.Refused}");
    }
}
