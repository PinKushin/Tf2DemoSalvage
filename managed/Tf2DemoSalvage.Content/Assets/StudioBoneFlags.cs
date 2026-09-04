namespace Tf2DemoSalvage.Content.Assets;

/// <summary>
/// What a bone is used for, and what rule computes it, as <c>studio.h</c> declares them.
/// </summary>
/// <remarks>
/// **These are the input to the engine's whole bone pipeline, not decoration.** Every entry point in
/// <c>C_BaseAnimating</c> takes a <c>boneMask</c>, and the mask is matched against the per-bone
/// <c>flags</c> field to decide which bones get built at all — <c>BuildTransformations</c> opens its
/// loop with <c>if ( !( hdr-&gt;boneFlags( i ) &amp; boneMask ) ) continue;</c>
/// (<c>c_baseanimating.cpp:1516</c>). It is what lets a shadow pass build eight bones where a render
/// pass builds ninety, and it is the input to the readable/writable accounting that makes
/// <c>SetupBones</c> idempotent within a frame.
///
/// **`BONE_USED_BY_BONE_MERGE` is the one with a visible cost.** `CBoneMergeCache::UpdateCache`
/// (<c>bone_merge_cache.cpp:95</c>) checks whether each parent bone it wants carries the bit, and if
/// any does not it widens the parent's setup mask to <c>BONE_USED_BY_ANYTHING</c> — with a
/// commented-out warning saying to mark the bone in the QC. So an unmarked bone does not break a
/// merge; it makes the wearer build its entire skeleton for every item worn on it.
///
/// **Separate from <see cref="StudioFlags"/> deliberately.** That holds the animation and sequence
/// storage bits, which say how bytes are laid out; these say what a bone is FOR. They share a header
/// and nothing else, and the two families have different values for overlapping bit positions.
///
/// Values are asserted against <c>public/studio.h</c> by <c>BonePipelineStructTests</c>, so none of
/// them is a remembered number.
/// </remarks>
public static class StudioBoneFlags
{
    /// <summary><c>BONE_USED_BY_HITBOX</c> — a hitbox hangs off this bone.</summary>
    public const int UsedByHitbox = 0x00000100;

    /// <summary><c>BONE_USED_BY_ATTACHMENT</c> — an attachment point hangs off this bone.</summary>
    /// <remarks>
    /// <c>SetupBones</c> runs <c>SetupBones_AttachmentHelper</c> only when this bit is newly
    /// requested (<c>c_baseanimating.cpp:3006</c>), which is what stops the attachment table being
    /// rebuilt for a caller that only wanted to draw.
    /// </remarks>
    public const int UsedByAttachment = 0x00000200;

    /// <summary><c>BONE_USED_BY_VERTEX_LOD0</c> — the highest-detail mesh skins to this bone.</summary>
    /// <remarks>
    /// There are eight of these, one per level of detail, at successive bits. Only LOD0 is named
    /// here because this project draws one level; <see cref="UsedByVertexMask"/> covers the family.
    /// </remarks>
    public const int UsedByVertexLod0 = 0x00000400;

    /// <summary><c>BONE_USED_BY_VERTEX_MASK</c> — any level of detail skins to this bone.</summary>
    public const int UsedByVertexMask = 0x0003FC00;

    /// <summary><c>BONE_USED_BY_BONE_MERGE</c> — something worn may merge onto this bone.</summary>
    public const int UsedByBoneMerge = 0x00040000;

    /// <summary><c>BONE_USED_BY_ANYTHING</c> — every "used by" bit at once.</summary>
    /// <remarks>
    /// The engine falls back to this whenever it cannot narrow the request: a merge onto an unmarked
    /// bone, <c>cl_SetupAllBones</c>, and tool recording all widen to it. It is the correct answer
    /// and the expensive one.
    /// </remarks>
    public const int UsedByAnything = 0x0007FF00;

    /// <summary><c>BONE_ALWAYS_PROCEDURAL</c> — a rule computes this bone, never an animation.</summary>
    /// <remarks>
    /// Paired with <c>proctype</c> on the bone. <c>BuildTransformations</c> tests this bit together
    /// with <c>STUDIO_PROC_JIGGLE</c> to decide whether to run the spring simulation
    /// (<c>c_baseanimating.cpp:1545</c>), so a jiggle bone is identified by the PAIR rather than by
    /// either alone.
    /// </remarks>
    public const int AlwaysProcedural = 0x00000004;

    /// <summary><c>BONE_PHYSICALLY_SIMULATED</c> — a ragdoll drives this bone.</summary>
    public const int PhysicallySimulated = 0x00000001;

    /// <summary><c>BONE_PHYSICS_PROCEDURAL</c> — physics computes it, but not as a ragdoll body.</summary>
    public const int PhysicsProcedural = 0x00000002;

    /// <summary><c>BONE_FIXED_ALIGNMENT</c> — interpolate this bone without re-aligning it.</summary>
    /// <remarks>
    /// **<c>studio.h:434</c>**, whose own comment is the whole explanation: *"bone can't spin 360
    /// degrees, all interpolation is normalized around a fixed orientation"*.
    ///
    /// **What it changes is which of two blends runs, and the pair differ by one step.**
    /// `QuaternionSlerp` is `QuaternionAlign` followed by `QuaternionSlerpNoAlign`
    /// (<c>mathlib_base.cpp:1605</c>); the align step negates the target when it points the long way
    /// round, because a quaternion and its negation are the same rotation. `SlerpBones` skips that
    /// step for a bone carrying this bit (<c>bone_setup.cpp:1492</c>), and `BlendBones` skips the
    /// same step in `QuaternionBlend` (<c>:1608</c>).
    ///
    /// **So the flag is an assertion by the ANIMATOR that the shorter arc is not always the right
    /// one.** Aligning is normally the safe choice — without it a limb can swing 300 degrees
    /// through the body to reach a pose 60 degrees away — but on a bone whose rotation is
    /// constrained, the negation flips it out of its authored range instead.
    /// </remarks>
    public const int FixedAlignment = 0x00100000;
}

/// <summary>
/// Which rule computes a procedural bone, from <c>proctype</c>.
/// </summary>
/// <remarks>
/// **Five kinds. `JIGGLE` is implemented (B58's jiggle half) and `QUATINTERP` is (B317); the other
/// three are not, and no model measured here declares one** — see B182. They are not exotic:
/// TF2's cosmetics lean on jiggle bones heavily, and the seven unmatched bones on a
/// <c>ghostly_gibus</c> that this repository already documents as *"stayed at the model origin"* are
/// exactly this family. The engine does not merge them onto a wearer either; it SIMULATES them
/// (<c>m_pJiggleBones-&gt;BuildJiggleTransformations</c>, <c>c_baseanimating.cpp:1586</c>).
///
/// Zero is not a member: <c>mstudiobone_t.procindex</c> is documented as "0 == none", and
/// <c>pProcedure()</c> returns null for it.
/// </remarks>
public static class StudioProcedureType
{
    /// <summary><c>STUDIO_PROC_AXISINTERP</c> — interpolate along an axis.</summary>
    public const int AxisInterpolate = 1;

    /// <summary><c>STUDIO_PROC_QUATINTERP</c> — interpolate between authored quaternions.</summary>
    /// <remarks>The common one: a helper bone driven by another bone's rotation.</remarks>
    public const int QuaternionInterpolate = 2;

    /// <summary><c>STUDIO_PROC_AIMATBONE</c> — point this bone at another bone.</summary>
    public const int AimAtBone = 3;

    /// <summary><c>STUDIO_PROC_AIMATATTACH</c> — point it at an attachment point.</summary>
    public const int AimAtAttachment = 4;

    /// <summary><c>STUDIO_PROC_JIGGLE</c> — a spring simulation, carried by <c>mstudiojigglebone_t</c>.</summary>
    public const int Jiggle = 5;
}
