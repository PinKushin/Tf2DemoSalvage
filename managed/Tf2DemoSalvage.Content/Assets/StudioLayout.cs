namespace Tf2DemoSalvage.Content.Assets;

/// <summary>
/// How big each studio model structure is, and where its fields sit inside one.
/// </summary>
/// <remarks>
/// **An MDL is read almost entirely by offset, and the file gives no help.** Every count-and-index
/// pair in <c>studiohdr_t</c> is two integers among fifty, so reading <c>numbodyparts</c> four bytes
/// early returns <c>numskinfamilies</c> — a small positive number that produces a model with the
/// wrong number of body parts and no error anywhere. That is the entire failure mode of this format.
///
/// **These were spread across eight readers**, each with its own private copy of the header offsets
/// it happened to need, so nothing connected <c>BoneCountOffset = 156</c> in one file to
/// <c>SequenceCountOffset = 188</c> in another as members of one structure. One place, and
/// <c>StudioStructTests</c> derives every value here from the declarations in
/// <c>public/studio.h</c>: sizes are computed from the members Valve declares, offsets are the
/// positions those members land on.
///
/// **Version 48 is what these describe** (<c>STUDIO_VERSION</c>). Earlier versions moved fields, and
/// a demo-era model that predates one of these offsets would need its own table rather than a guess.
/// </remarks>
internal static class StudioLayout
{
    /// <summary>Byte offset of <c>name</c> in <c>studiohdr_t</c>: 64 bytes of model name.</summary>
    public const int HeaderNameOffset = 12;

    /// <summary>Byte offset of <c>numbones</c>.</summary>
    public const int HeaderBoneCountOffset = 156;

    /// <summary>Byte offset of <c>boneindex</c>.</summary>
    public const int HeaderBoneIndexOffset = 160;

    /// <summary>Byte offset of <c>numlocalanim</c>.</summary>
    public const int HeaderAnimationCountOffset = 180;

    /// <summary>Byte offset of <c>localanimindex</c>.</summary>
    public const int HeaderAnimationIndexOffset = 184;

    /// <summary>Byte offset of <c>numlocalseq</c>.</summary>
    public const int HeaderSequenceCountOffset = 188;

    /// <summary>Byte offset of <c>localseqindex</c>.</summary>
    public const int HeaderSequenceIndexOffset = 192;

    /// <summary>Byte offset of <c>numtextures</c>.</summary>
    public const int HeaderTextureCountOffset = 204;

    /// <summary>Byte offset of <c>textureindex</c>.</summary>
    public const int HeaderTextureIndexOffset = 208;

    /// <summary>Byte offset of <c>numcdtextures</c>: the material search paths.</summary>
    public const int HeaderFolderCountOffset = 212;

    /// <summary>Byte offset of <c>cdtextureindex</c>.</summary>
    public const int HeaderFolderIndexOffset = 216;

    /// <summary>Byte offset of <c>numskinref</c>: materials one skin family replaces.</summary>
    public const int HeaderSkinReferenceCountOffset = 220;

    /// <summary>Byte offset of <c>numskinfamilies</c>.</summary>
    public const int HeaderSkinFamilyCountOffset = 224;

    /// <summary>Byte offset of <c>skinindex</c>: the start of the family table.</summary>
    public const int HeaderSkinTableOffset = 228;

    /// <summary>Byte offset of <c>numbodyparts</c>.</summary>
    public const int HeaderBodyPartCountOffset = 232;

    /// <summary>Byte offset of <c>bodypartindex</c>.</summary>
    public const int HeaderBodyPartIndexOffset = 236;

    /// <summary>Byte offset of <c>numlocalposeparameters</c>.</summary>
    public const int HeaderPoseParameterCountOffset = 300;

    /// <summary>Byte offset of <c>localposeparamindex</c>.</summary>
    public const int HeaderPoseParameterIndexOffset = 304;

    /// <summary>Byte offset of <c>numincludemodels</c>: the shared animation MDLs.</summary>
    public const int HeaderIncludeCountOffset = 336;

    /// <summary>Byte offset of <c>includemodelindex</c>.</summary>
    public const int HeaderIncludeIndexOffset = 340;

    /// <summary>Byte offset of <c>numlocalattachments</c>.</summary>
    /// <remarks>
    /// **These five are declared ahead of the reader that will use them, deliberately.** B82 is
    /// open: items parented to an attachment are not implemented, so a halo or a canteen sits at the
    /// wearer's feet. The standing order of work here is the conformance test first, then the
    /// ordinary tests, then the implementation — so the layout is pinned against
    /// <c>public/studio.h</c> now, and whatever reads it later starts from numbers that were checked
    /// before anything depended on them.
    ///
    /// That is the opposite of how the BSP lump constants began, which sat unread long enough that
    /// their test guarded a file nothing used. The difference is that this is one step of a sequence
    /// with the next step named, rather than a table someone forgot to wire up.
    /// </remarks>
    public const int HeaderAttachmentCountOffset = 240;

    /// <summary>Byte offset of <c>localattachmentindex</c>.</summary>
    public const int HeaderAttachmentIndexOffset = 244;

    /// <summary>Bytes per <c>mstudioattachment_t</c>.</summary>
    public const int AttachmentStride = 92;

    /// <summary>Byte offset of <c>sznameindex</c>: the attachment's name, such as <c>head</c>.</summary>
    /// <remarks>
    /// **Attachments are matched by NAME, not by index**, which is why this field is the one that
    /// matters. A cosmetic asks for <c>partyhat</c> or <c>eyeglow_L</c>; the index it happens to
    /// occupy differs between models.
    /// </remarks>
    public const int AttachmentNameOffset = 0;

    /// <summary>Byte offset of <c>flags</c>.</summary>
    public const int AttachmentFlagsOffset = 4;

    /// <summary>Byte offset of <c>localbone</c>: which bone the point hangs off.</summary>
    public const int AttachmentBoneOffset = 8;

    /// <summary>Byte offset of <c>local</c>: a 3×4 matrix, the offset from that bone.</summary>
    /// <remarks>
    /// **The half that makes an attachment different from a bone.** Taking the bone's transform
    /// alone puts the item at the bone rather than at the point — close for a hat, visibly wrong for
    /// anything offset from the head, and identical to the bone-merge path this project already has.
    /// </remarks>
    public const int AttachmentMatrixOffset = 12;

    /// <summary>Bytes per <c>mstudiobone_t</c>.</summary>
    public const int BoneStride = 216;

    /// <summary>Byte offset of <c>sznameindex</c> in a bone.</summary>
    public const int BoneNameOffset = 0;

    /// <summary>Byte offset of <c>parent</c>, −1 on a root bone.</summary>
    public const int BoneParentOffset = 4;

    /// <summary>Byte offset of <c>pos</c>: the bone's default position.</summary>
    public const int BonePositionOffset = 32;

    /// <summary>Byte offset of <c>quat</c>: the default rotation, as a quaternion.</summary>
    public const int BoneRotationOffset = 44;

    /// <summary>Byte offset of <c>rot</c>: the same rotation as Euler angles, in radians.</summary>
    /// <remarks>
    /// **Both are stored, and they are not interchangeable in animation.** Delta animation values
    /// are applied to the Euler form and then converted, so reading the quaternion where the engine
    /// reads <c>rot</c> gives a pose that is right at rest and wrong the moment anything moves.
    /// </remarks>
    public const int BoneEulerOffset = 60;

    /// <summary>Byte offset of <c>posscale</c>: what compressed position deltas are multiplied by.</summary>
    public const int BonePositionScaleOffset = 72;

    /// <summary>Byte offset of <c>rotscale</c>: the same for rotation deltas.</summary>
    public const int BoneRotationScaleOffset = 84;

    /// <summary>Byte offset of <c>poseToBone</c>: a 3×4 matrix, the skinning bind pose.</summary>
    public const int BonePoseToBoneOffset = 96;

    /// <summary>Bytes per <c>mstudioseqdesc_t</c>.</summary>
    public const int SequenceStride = 212;

    /// <summary>Byte offset of <c>szlabelindex</c>: the sequence's name.</summary>
    public const int SequenceLabelOffset = 4;

    /// <summary>Byte offset of <c>flags</c>: <c>STUDIO_LOOPING</c> and the rest.</summary>
    public const int SequenceFlagsOffset = 12;

    /// <summary>
    /// Where <c>mstudioseqdesc_t.szactivitynameindex</c> sits: the activity's NAME.
    /// </summary>
    /// <remarks>
    /// **The name is the lookup, and the number beside it is useless in a file.** <c>studio.h</c>
    /// annotates the following <c>activity</c> field "initialized at loadtime to game DLL values",
    /// so what a model ships is this string — <c>ACT_MP_RUN</c> and the like — and the game resolves
    /// it to whatever its own enum says. A reader that wanted the number would be reading a slot the
    /// compiler left for the engine to fill.
    /// </remarks>
    public const int SequenceActivityNameOffset = 8;

    /// <summary>
    /// Where <c>mstudioseqdesc_t.actweight</c> sits: how strongly this sequence claims its activity.
    /// </summary>
    /// <remarks>
    /// Several sequences can share one activity, and <c>SelectWeightedSequence</c> picks between them
    /// in proportion to this. A weight of zero means the sequence is never chosen for the activity
    /// even though it names it.
    /// </remarks>
    public const int SequenceActivityWeightOffset = 20;

    /// <summary>Byte offset of <c>animindexindex</c>: the animation grid for this sequence.</summary>
    public const int SequenceAnimationIndexOffset = 60;

    /// <summary>Byte offset of <c>groupsize</c>: the grid's width and height.</summary>
    public const int SequenceGroupSizeOffset = 68;

    /// <summary>Byte offset of <c>paramindex</c>: which pose parameters drive the grid.</summary>
    public const int SequenceParameterIndexOffset = 76;

    /// <summary>Byte offset of <c>paramstart</c>: the value at the grid's first column.</summary>
    public const int SequenceParameterStartOffset = 84;

    /// <summary>Byte offset of <c>paramend</c>: the value at its last.</summary>
    public const int SequenceParameterEndOffset = 92;

    /// <summary>Bytes per <c>mstudioposeparamdesc_t</c>.</summary>
    public const int PoseParameterStride = 20;

    /// <summary>Bytes per <c>mstudioanimdesc_t</c>.</summary>
    public const int AnimationStride = 100;

    /// <summary>Byte offset of <c>fps</c>.</summary>
    public const int AnimationFramesPerSecondOffset = 8;

    /// <summary>Byte offset of <c>numframes</c>.</summary>
    public const int AnimationFrameCountOffset = 16;

    /// <summary>Byte offset of <c>nummovements</c>: how many piecewise motion blocks it has.</summary>
    /// <remarks>
    /// Straight after <c>numframes</c> in <c>mstudioanimdesc_t</c> — <c>baseptr</c>,
    /// <c>sznameindex</c>, <c>fps</c>, <c>flags</c>, <c>numframes</c>, then this pair, then
    /// <c>unused1[6]</c> and the <c>animblock</c>/<c>animindex</c> already read below.
    /// </remarks>
    public const int AnimationMovementCountOffset = 20;

    /// <summary>Byte offset of <c>movementindex</c>, relative to the animation description.</summary>
    public const int AnimationMovementIndexOffset = 24;

    /// <summary>
    /// Bytes per <c>mstudiomovement_t</c>: <c>endframe</c>, <c>motionflags</c>, <c>v0</c>,
    /// <c>v1</c>, <c>angle</c>, then two <c>Vector</c>s — <c>vector</c> and <c>position</c>.
    /// </summary>
    public const int MovementStride = 44;

    /// <summary>Byte offset of <c>endframe</c> within a movement block.</summary>
    public const int MovementEndFrameOffset = 0;

    /// <summary>Byte offset of <c>v0</c>: the velocity at the start of the block.</summary>
    public const int MovementStartVelocityOffset = 8;

    /// <summary>Byte offset of <c>v1</c>: the velocity at its end.</summary>
    public const int MovementEndVelocityOffset = 12;

    /// <summary>Byte offset of <c>angle</c>: the yaw rotation at the end of the block.</summary>
    public const int MovementAngleOffset = 16;

    /// <summary>Byte offset of <c>vector</c>: the direction of travel for the block.</summary>
    public const int MovementVectorOffset = 20;

    /// <summary>Byte offset of <c>position</c>: displacement from the start of the animation.</summary>
    public const int MovementPositionOffset = 32;

    /// <summary>Byte offset of <c>animblock</c>: 0 when the data is in this file.</summary>
    public const int AnimationBlockOffset = 52;

    /// <summary>Byte offset of <c>animindex</c>: where the compressed tracks start.</summary>
    public const int AnimationDataOffset = 56;

    /// <summary>Bytes per <c>mstudiotexture_t</c>: a material reference, not pixels.</summary>
    public const int TextureStride = 64;

    /// <summary>Bytes per <c>mstudiobodyparts_t</c>.</summary>
    public const int BodyPartStride = 16;

    /// <summary>Byte offset of <c>nummodels</c>: how many alternatives this part offers.</summary>
    public const int BodyPartModelCountOffset = 4;

    /// <summary>Byte offset of <c>base</c>: the divisor in Valve's body-group arithmetic.</summary>
    /// <remarks>
    /// <c>(body / base) % nummodels</c>, from <c>shared/animation.cpp:876</c> — which is why this
    /// field cannot be skipped: every part after the first decodes to alternative zero without it.
    /// </remarks>
    public const int BodyPartBaseOffset = 8;

    /// <summary>Byte offset of <c>modelindex</c>.</summary>
    public const int BodyPartModelIndexOffset = 12;

    /// <summary>Bytes per <c>mstudiomodel_t</c>.</summary>
    public const int ModelStride = 148;

    /// <summary>Byte offset of <c>nummeshes</c>.</summary>
    public const int ModelMeshCountOffset = 72;

    /// <summary>Byte offset of <c>meshindex</c>.</summary>
    public const int ModelMeshIndexOffset = 76;

    /// <summary>Byte offset of <c>numvertices</c>.</summary>
    public const int ModelVertexCountOffset = 80;

    /// <summary>Byte offset of <c>vertexindex</c>: a BYTE offset into the VVD, not an index.</summary>
    public const int ModelVertexIndexOffset = 84;

    /// <summary>Bytes per <c>mstudiomesh_t</c>.</summary>
    public const int MeshStride = 116;

    /// <summary>Byte offset of <c>material</c>: an index into the model's own texture list.</summary>
    public const int MeshMaterialOffset = 0;

    /// <summary>Byte offset of <c>numvertices</c>.</summary>
    public const int MeshVertexCountOffset = 8;

    /// <summary>Byte offset of <c>vertexoffset</c>: this mesh's start within its model's vertices.</summary>
    public const int MeshVertexOffset = 12;

    /// <summary>Bytes per <c>mstudiovertex_t</c> in a VVD.</summary>
    public const int VertexStride = 48;

    /// <summary>Byte offset of <c>bone</c> inside a vertex's <c>mstudioboneweight_t</c>.</summary>
    /// <remarks>Three weights come first, then three bone indices as bytes, then the count.</remarks>
    public const int VertexBoneIndexOffset = 12;

    /// <summary>Byte offset of <c>m_vecPosition</c>.</summary>
    public const int VertexPositionOffset = 16;

    /// <summary>Byte offset of <c>m_vecNormal</c>.</summary>
    public const int VertexNormalOffset = 28;

    /// <summary>Byte offset of <c>m_vecTexCoord</c>.</summary>
    public const int VertexTexCoordOffset = 40;

    /// <summary>Bytes per <c>mstudiomodelgroup_t</c>: two string offsets.</summary>
    public const int ModelGroupStride = 8;

    /// <summary>Byte offset of <c>sznameindex</c>: the included MDL's path.</summary>
    /// <remarks>The label comes first and is not the filename; reading it finds the wrong string.</remarks>
    public const int ModelGroupNameOffset = 4;
}
