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

    /// <summary>Byte offset of <c>hull_min</c>: the movement hull's lower corner.</summary>
    /// <remarks>
    /// **The header carries TWO boxes and they are not interchangeable.** Valve names them in
    /// `studio.h`: `hull_min`/`hull_max` is the *"ideal movement hull size"* and
    /// `view_bbmin`/`view_bbmax` the *"clipping bounding box"*.
    /// <c>C_BaseAnimating::GetRenderBounds</c> prefers the clipping box when the modeller authored
    /// one and falls back to the hull, so a reader that takes either alone is right on some models
    /// and wrong on others.
    ///
    /// After `id`, `version`, `checksum`, `name[64]`, `length`, `eyeposition` and `illumposition`:
    /// 12 + 64 + 4 + 12 + 12 = 104. Cross-checked against <see cref="HeaderBoneCountOffset"/>,
    /// which has decoded correctly for months and sits four Vectors and an `int flags` later.
    /// </remarks>
    public const int HeaderHullMinOffset = 104;

    /// <summary>Byte offset of <c>hull_max</c>.</summary>
    public const int HeaderHullMaxOffset = 116;

    /// <summary>Byte offset of <c>view_bbmin</c>: the clipping box, when one was authored.</summary>
    public const int HeaderViewBoundsMinOffset = 128;

    /// <summary>Byte offset of <c>view_bbmax</c>.</summary>
    public const int HeaderViewBoundsMaxOffset = 140;

    /// <summary>Byte offset of <c>flags</c>: the <c>STUDIOHDR_FLAGS_*</c> word.</summary>
    /// <remarks>
    /// **Described here in prose for months before anything read it.** The note on
    /// <see cref="HeaderBoneCountOffset"/> below already said "flags sits between view_bbmax and
    /// numbones", which is how a field can be simultaneously documented and unimplemented — see
    /// `docs/memory/write-can-destroy-what-you-did-not-read.md`.
    ///
    /// Bracketed by two numbers that were established independently: <c>view_bbmax</c> at 140 is
    /// load-bearing in the render-bounds path, and <c>numbones</c> at 156 has decoded correctly
    /// since this reader was written. See <see cref="StudioModelFlags"/> for what the bits mean.
    /// </remarks>
    public const int HeaderFlagsOffset = 152;

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

    /// <summary>Byte offset of <c>bonecontroller[6]</c>: which controller drives each axis.</summary>
    /// <remarks>
    /// Six signed integers, one per degree of freedom (X, Y, Z, XR, YR, ZR), each the index of a
    /// <c>mstudiobonecontroller_t</c> or −1. This is the lookup <c>CalcBoneAdj</c> walks.
    /// </remarks>
    public const int BoneControllerListOffset = 8;

    /// <summary>How many controller slots a bone carries.</summary>
    public const int BoneControllerSlots = 6;

    /// <summary>Byte offset of <c>flags</c>: the <c>BONE_USED_BY_*</c> mask (B182).</summary>
    /// <remarks>
    /// **The field the whole engine pipeline is gated on**, and the reader has never read it. Every
    /// <c>C_BaseAnimating</c> entry point takes a <c>boneMask</c> and matches it against this;
    /// <c>BuildTransformations</c> skips a bone outright when the two do not intersect. See
    /// <see cref="StudioBoneFlags"/>.
    /// </remarks>
    public const int BoneFlagsOffset = 160;

    /// <summary>Byte offset of <c>proctype</c>: which rule computes this bone, or 0 for none.</summary>
    public const int BoneProcedureTypeOffset = 164;

    /// <summary>Byte offset of <c>procindex</c>: where that rule's data sits, relative to the bone.</summary>
    /// <remarks>
    /// **Relative to the BONE, not to the file.** <c>pProcedure()</c> is
    /// <c>((byte *)this) + procindex</c>, and it returns null when <c>procindex</c> is zero — so
    /// zero means "no rule" rather than "at the start of the file", which is the reading that would
    /// decode the header as a jiggle bone.
    /// </remarks>
    public const int BoneProcedureIndexOffset = 168;

    /// <summary>Byte offset of <c>numbonecontrollers</c> in the header.</summary>
    public const int HeaderBoneControllerCountOffset = 164;

    /// <summary>Byte offset of <c>bonecontrollerindex</c>.</summary>
    public const int HeaderBoneControllerIndexOffset = 168;

    /// <summary>Byte offset of <c>numikchains</c> in the header.</summary>
    public const int HeaderIkChainCountOffset = 284;

    /// <summary>Byte offset of <c>ikchainindex</c>.</summary>
    public const int HeaderIkChainIndexOffset = 288;

    /// <summary>Bytes per <c>mstudiobonecontroller_t</c>.</summary>
    public const int BoneControllerStride = 56;

    /// <summary>Byte offset of <c>bone</c>: which bone this controller drives.</summary>
    public const int BoneControllerBoneOffset = 0;

    /// <summary>Byte offset of <c>type</c>: which axis, plus the wrapping bit.</summary>
    public const int BoneControllerTypeOffset = 4;

    /// <summary>Byte offset of <c>start</c>: the value the encoded 0 maps to.</summary>
    public const int BoneControllerStartOffset = 8;

    /// <summary>Byte offset of <c>end</c>: the value the encoded 1 maps to.</summary>
    /// <remarks>
    /// **The pair is what makes the wire value meaningful.** <c>m_flEncodedController</c> is sent
    /// as eleven bits over 0..1 (<c>baseanimating.cpp:248</c>), so the demo carries a fraction and
    /// the model carries what that fraction means. Neither is usable alone.
    /// </remarks>
    public const int BoneControllerEndOffset = 12;

    /// <summary>Byte offset of a bone controller's <c>inputfield</c>.</summary>
    /// <remarks>
    /// **Which of the entity's controller values drives this bone**, and the reason the whole
    /// mechanism needs it: <c>CalcBoneAdj</c> is <c>i = pbonecontroller-&gt;inputfield; value =
    /// controllers[i];</c> (<c>bone_setup.cpp:2482</c>). Two controllers can share an input, and a
    /// model's controllers are not in input order — assuming index equals input would drive the
    /// wrong bone from the wrong value on any model where they differ.
    ///
    /// <c>mstudiobonecontroller_t</c>: <c>bone</c> 0, <c>type</c> 4, <c>start</c> 8, <c>end</c> 12,
    /// <c>rest</c> 16, <c>inputfield</c> 20 (<c>studio.h:443</c>).
    /// </remarks>
    public const int BoneControllerInputOffset = 20;

    /// <summary>Bytes per <c>mstudioikchain_t</c>.</summary>
    /// <remarks>
    /// Sixteen, with no trailing padding. Where every neighbouring structure ends in an
    /// <c>int unused[]</c> tail, this one carries a Valve to-do note saying those entries still need
    /// adding — so the usual shape is exactly what it does NOT have. Assuming it would add 32 bytes
    /// and read every chain after the first from the wrong place.
    /// </remarks>
    public const int IkChainStride = 16;

    /// <summary>Byte offset of <c>sznameindex</c>: the chain's name, such as <c>rfoot</c>.</summary>
    public const int IkChainNameOffset = 0;

    /// <summary>Byte offset of <c>linktype</c>.</summary>
    public const int IkChainLinkTypeOffset = 4;

    /// <summary>Byte offset of <c>numlinks</c>: usually three — hip, knee, foot.</summary>
    public const int IkChainLinkCountOffset = 8;

    /// <summary>Byte offset of <c>linkindex</c>, relative to the chain.</summary>
    public const int IkChainLinkIndexOffset = 12;

    /// <summary>Bytes per <c>mstudioiklink_t</c>.</summary>
    public const int IkLinkStride = 28;

    /// <summary>Byte offset of <c>bone</c> in a link.</summary>
    public const int IkLinkBoneOffset = 0;

    /// <summary>Byte offset of <c>kneeDir</c>: which way the joint bends.</summary>
    /// <remarks>
    /// Zero on most links and meaningful on the middle one, which is what stops a two-bone solve
    /// from choosing a knee that bends backwards.
    /// </remarks>
    public const int IkLinkKneeDirectionOffset = 4;

    /// <summary>Bytes per <c>mstudiojigglebone_t</c>.</summary>
    /// <remarks>
    /// **140, derived rather than counted.** It is thirty-five consecutive floats after the flags
    /// word, and hand-counting a run that long has no error signal — a stride one field short reads
    /// every subsequent jiggle bone from the wrong place and produces springy nonsense rather than
    /// an exception. <c>BonePipelineStructProbe</c> measured it out of <c>studio.h</c> and
    /// <c>BonePipelineStructTests</c> holds it there.
    /// </remarks>
    public const int JiggleBoneStride = 140;

    /// <summary>Byte offset of the jiggle bone's <c>flags</c>: the <c>JIGGLE_*</c> bits.</summary>
    public const int JiggleFlagsOffset = 0;

    /// <summary>Bytes per <c>mstudiolocalhierarchy_t</c>.</summary>
    public const int LocalHierarchyStride = 48;

    /// <summary>Byte offset of <c>iBone</c>: the bone being reparented.</summary>
    public const int LocalHierarchyBoneOffset = 0;

    /// <summary>Byte offset of <c>iNewParent</c>: what it is reparented to.</summary>
    public const int LocalHierarchyNewParentOffset = 4;

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

    /// <summary>Byte offset of <c>numevents</c> within a sequence: how many events it fires.</summary>
    /// <remarks>
    /// **The events live on the SEQUENCE, not on the animation** (`studio.h:817`). That distinction
    /// cost a wrong entry in `docs/PARITY-AUDIT.md`, which named `mstudioanimdesc_t` — a struct
    /// with no event members at all — so anyone implementing from it would have searched the wrong
    /// place. `mstudioseqdesc_t` carries `numevents`, `eventindex` and `pEvent(i)`, and
    /// `C_BaseAnimating::DoAnimationEvents` reads exactly those.
    ///
    /// Six ints in, immediately after <see cref="SequenceActivityWeightOffset"/> and immediately
    /// before <see cref="SequenceBoundsMinOffset"/> — which is the same arithmetic that already
    /// justifies the 32 below, read from the other end.
    /// </remarks>
    public const int SequenceEventCountOffset = 24;

    /// <summary>Byte offset of <c>eventindex</c>: where this sequence's events begin.</summary>
    /// <remarks>
    /// Relative to the START OF THE SEQUENCE, as every index in this format is relative to the
    /// structure holding it: <c>pEvent</c> is <c>((byte *)this) + eventindex</c> and then indexed
    /// by element, so a sequence's events are contiguous from there.
    /// </remarks>
    public const int SequenceEventIndexOffset = 28;

    /// <summary>Bytes in one <c>mstudioevent_t</c> (<c>studio.h:495</c>).</summary>
    /// <remarks>
    /// `float cycle` + `int event` + `int type` + `char options[64]` + `int szeventindex` — four,
    /// four, four, sixty-four and four. The options string is INSIDE the structure rather than
    /// pointed at, which is why the stride is eighty and not a pointer's width.
    /// </remarks>
    public const int EventStride = 80;

    /// <summary>Byte offset of <c>cycle</c> within an event: when in the sequence it fires.</summary>
    public const int EventCycleOffset = 0;

    /// <summary>Byte offset of <c>event</c>: the resolved event id.</summary>
    /// <remarks>
    /// **Resolved at load time for the new system, and literal for the old one.** An event carrying
    /// <c>AE_TYPE_NEWEVENTSYSTEM</c> is named by <c>szeventindex</c> and its id is filled in by
    /// <c>SetEventIndexForSequence</c> (`animation.cpp:60`) from the shared registry; an older one
    /// states its number here outright, which is why <c>DoAnimationEvents</c> falls back to
    /// <c>event &lt; 5000</c> for those.
    /// </remarks>
    public const int EventIdOffset = 4;

    /// <summary>Byte offset of <c>type</c>: the <c>AE_TYPE_*</c> flags.</summary>
    public const int EventTypeOffset = 8;

    /// <summary>Byte offset of <c>options</c>: sixty-four bytes of event argument, in place.</summary>
    public const int EventOptionsOffset = 12;

    /// <summary>Bytes in an event's <c>options</c> field.</summary>
    public const int EventOptionsLength = 64;

    /// <summary>Byte offset of <c>szeventindex</c>: the event's NAME, for the new system.</summary>
    public const int EventNameIndexOffset = 76;

    /// <summary>Byte offset of <c>bbmin</c> within a sequence: its own bounding box.</summary>
    /// <remarks>
    /// **The per-sequence box is what makes render bounds change as a model animates.**
    /// `GetRenderBounds` unions it into the header's box — `VectorMin( seqdesc.bbmin, theMins,
    /// theMins )` — so a running player is bounded differently from a crouched one, and a single
    /// number cached per model cannot say so.
    ///
    /// Eight ints in — `baseptr`, `szlabelindex`, `szactivitynameindex`, `flags`, `activity`,
    /// `actweight`, `numevents`, `eventindex` — which puts it at 32, immediately before
    /// `numblends`.
    /// </remarks>
    public const int SequenceBoundsMinOffset = 32;

    /// <summary>Byte offset of <c>bbmax</c> within a sequence.</summary>
    public const int SequenceBoundsMaxOffset = 44;

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

    /// <summary>
    /// Byte offset of <c>weightlistindex</c>: a float per bone, the sequence's own contribution
    /// weight for each one when it is layered as a gesture.
    /// </summary>
    /// <remarks>
    /// Counted forward from <c>animindexindex</c> at 60 through <c>movementindex</c> (64),
    /// <c>groupsize[2]</c> (68), <c>paramindex[2]</c> (76), <c>paramstart[2]</c> (84),
    /// <c>paramend[2]</c> (92), <c>paramparent</c> (100), <c>fadeintime</c>/<c>fadeouttime</c>
    /// (104, 108), <c>localentrynode</c>/<c>localexitnode</c>/<c>nodeflags</c> (112, 116, 120),
    /// <c>entryphase</c>/<c>exitphase</c> (124, 128), <c>lastframe</c> (132),
    /// <c>nextseq</c>/<c>pose</c> (136, 140), <c>numikrules</c> (144),
    /// <c>numautolayers</c>/<c>autolayerindex</c> (148, 152) — landing here at 156. Every offset up
    /// to <c>paramend</c> is already load-bearing elsewhere in this project and agrees with this
    /// count, which is what makes carrying it forward trustworthy rather than a fresh guess.
    ///
    /// <c>SlerpBones</c> (<c>bone_setup.cpp:1373</c>) reads it as
    /// <c>pS2[i] = s * seqdesc.weight( i )</c> — the layer's own blend-in weight times this
    /// PER-BONE value — for both the ordinary and the <c>STUDIO_DELTA</c> branch. A gesture cannot
    /// be composited correctly without it: applying the layer weight alone moves bones the
    /// sequence was authored to leave untouched.
    /// </remarks>
    public const int SequenceWeightListIndexOffset = 156;

    /// <summary>Byte offset of <c>numautolayers</c>: how many sequences this one layers on itself.</summary>
    /// <remarks>
    /// **A sequence can automatically play OTHER sequences over itself** — `AccumulatePose` calls
    /// `AddSequenceLayers` right after the main blend (<c>bone_setup.cpp:2125</c>), and each
    /// `mstudioautolayer_t` carries a cycle window (start, peak, tail, end) plus its own
    /// `STUDIO_AL_*` flags for splining, cross-fading and local layering.
    ///
    /// **Read to MEASURE rather than to implement**, and that order is deliberate: the same
    /// question asked of procedural bones turned five unimplemented rules into one, and asking it
    /// here is one number against a whole mechanism.
    ///
    /// The offset is the count in the pair the comment above already walks to — 148, immediately
    /// after `numikrules` at 144 and immediately before `autolayerindex` at 152.
    /// </remarks>
    public const int SequenceAutoLayerCountOffset = 148;

    /// <summary>Byte offset of <c>autolayerindex</c>: where this sequence's autolayers begin.</summary>
    /// <remarks>
    /// Relative to the START OF THE SEQUENCE, as every index in this format is:
    /// <c>pAutolayer(i)</c> is <c>((byte *)this) + autolayerindex</c> then indexed by element
    /// (<c>studio.h:873</c>), so the entries are contiguous from there.
    /// </remarks>
    public const int SequenceAutoLayerIndexOffset = 152;

    /// <summary>Byte offset of <c>fadeintime</c>: how long this sequence takes to blend IN.</summary>
    /// <remarks>
    /// **The cross-fade either side of a sequence change** (<c>studio.h:854</c>, *"ideal cross fate
    /// in time (0.2 default)"* — Valve's typo). `CSequenceTransitioner::CheckForSequenceChange`
    /// keeps the outgoing sequence alive for
    /// <c>MIN( prevseqdesc.fadeouttime, seqdesc.fadeintime )</c>
    /// (<c>sequence_Transitioner.cpp:46</c>), so both halves are needed and neither alone is the
    /// answer.
    ///
    /// **Arithmetic check, and it holds:** counting fields from `baseptr` puts `fadeintime` at 104
    /// and `weightlistindex` at 156, which is the offset already measured against a real model
    /// above. Two independent landings on 156 is what makes 104 believable.
    /// </remarks>
    public const int SequenceFadeInOffset = 104;

    /// <summary>Byte offset of <c>fadeouttime</c>: how long this sequence takes to blend OUT.</summary>
    public const int SequenceFadeOutOffset = 108;

    /// <summary>Bytes per <c>mstudioposeparamdesc_t</c>.</summary>
    public const int PoseParameterStride = 20;

    /// <summary>Bytes per <c>mstudioanimdesc_t</c>.</summary>
    /// <summary>Byte offset of <c>flags</c> in an animation description.</summary>
    /// <remarks>
    /// <c>mstudioanimdesc_t</c>: <c>baseptr</c> 0, <c>sznameindex</c> 4, <c>fps</c> 8,
    /// <c>flags</c> 12, <c>numframes</c> 16 (<c>studio.h:726-735</c>). This is the ANIMATION's
    /// flag word, which is not the sequence's and does not mean the same thing — see
    /// <c>StudioAnimation.Flags</c>.
    /// </remarks>
    public const int AnimationFlagsOffset = 12;

    public const int AnimationStride = 100;

    /// <summary>Byte offset of <c>fps</c>.</summary>
    public const int AnimationFramesPerSecondOffset = 8;

    /// <summary>Byte offset of <c>numframes</c>.</summary>
    public const int AnimationFrameCountOffset = 16;

    /// <summary>Byte offset of <c>numlocalhierarchy</c>.</summary>
    /// <remarks>
    /// **An animation can reparent a bone while it plays**, which is what
    /// <c>mstudiolocalhierarchy_t</c> carries and what <c>CalcLocalHierarchyAnimation</c> applies
    /// (<c>bone_setup.cpp:1003</c>). Nothing here implements it; this offset exists so a model can
    /// be ASKED whether it needs it before anyone decides whether to.
    ///
    /// Counted from the struct: <c>baseptr</c>, <c>sznameindex</c>, <c>fps</c>, <c>flags</c>,
    /// <c>numframes</c>, <c>nummovements</c>, <c>movementindex</c>, <c>unused1[6]</c>,
    /// <c>animblock</c>, <c>animindex</c>, <c>numikrules</c>, <c>ikruleindex</c>,
    /// <c>animblockikruleindex</c>, then this — 4+4+4+4+4+4+4+24+4+4+4+4+4 = 72.
    /// </remarks>
    public const int AnimationLocalHierarchyCountOffset = 72;

    /// <summary>Byte offset of <c>zeroframecount</c>, a short.</summary>
    /// <remarks>
    /// <c>CalcZeroframeData</c> fills bones an animation does not otherwise mention from a
    /// compressed span table, and it runs BEFORE the local hierarchy pass. Also unimplemented here,
    /// and also worth being able to ask about.
    /// </remarks>
    public const int AnimationZeroFrameCountOffset = 90;

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
    /// <remarks>
    /// **Valve's comment on the field is "non-zero when anim data isn't in sections"**, so this
    /// alone does not locate the data — see <see cref="AnimationSectionFramesOffset"/>.
    /// </remarks>
    public const int AnimationDataOffset = 56;

    /// <summary>Byte offset of <c>sectionindex</c>: the per-section table of animation data.</summary>
    /// <remarks>
    /// Entries are <c>mstudioanimsections_t</c> — <c>animblock</c> then <c>animindex</c>, eight
    /// bytes — indexed by section.
    /// </remarks>
    public const int AnimationSectionIndexOffset = 80;

    /// <summary>Byte offset of <c>sectionframes</c>: frames per section, or zero when unsectioned.</summary>
    /// <remarks>
    /// **A long animation is split into sections and each one restarts its frame numbering**
    /// (<c>mstudioanimdesc_t::pAnim</c>, <c>studio.cpp</c>). Ignoring it does not fail loudly: the
    /// run-length walk simply runs off the end of section zero and keeps reading, which repeats a
    /// stale value for most frames and lands on stray bytes for a few. That reads as an animation
    /// with occasional one-frame spikes, and it is what B222 turned out to be.
    /// </remarks>
    public const int AnimationSectionFramesOffset = 84;

    /// <summary>Bytes per <c>mstudioanimsections_t</c>: <c>animblock</c> and <c>animindex</c>.</summary>
    public const int AnimationSectionStride = 8;

    /// <summary>Byte offset of <c>animindex</c> within an <c>mstudioanimsections_t</c>.</summary>
    public const int AnimationSectionDataOffset = 4;

    /// <summary>Bytes per <c>mstudiotexture_t</c>: a material reference, not pixels.</summary>
    public const int TextureStride = 64;

    /// <summary>Bytes per <c>mstudiobodyparts_t</c>.</summary>
    public const int BodyPartStride = 16;

    /// <summary>Byte offset of <c>sznameindex</c>: the part's name, relative to the part.</summary>
    /// <remarks>
    /// **A body part is addressed by NAME by the code that matters.** `FindBodygroupByName`
    /// (<c>shared/animation.cpp:927</c>) walks the parts comparing this string, and TF2 uses it for
    /// the one bodygroup a demo viewer has to reproduce: <c>m_iSpyMaskBodygroup =
    /// FindBodygroupByName( "spyMask" )</c> (<c>c_tf_player.cpp:5371</c>). Without the name there
    /// is no way to say which part that is — the index differs per model.
    /// </remarks>
    public const int BodyPartNameOffset = 0;

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
