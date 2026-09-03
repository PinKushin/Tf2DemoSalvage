namespace Tf2DemoSalvage.Content.Assets;

/// <summary>
/// The flag bits a studio model uses to say how its animation is stored and how it plays.
/// </summary>
/// <remarks>
/// **The animation bits are a format selector, not a hint.** Each one says which compressed type
/// follows in the track — <c>STUDIO_ANIM_RAWROT</c> means a six-byte <c>Quaternion48</c>,
/// <c>STUDIO_ANIM_RAWROT2</c> means an eight-byte <c>Quaternion64</c> — so reading the wrong bit
/// consumes the wrong number of bytes and every bone after it in that track is decoded from the
/// wrong offset. The pose that comes back is not obviously broken; it is a different pose.
///
/// **The sequence bits change playback rather than parsing**, which makes them quieter still. A
/// missed <c>STUDIO_LOOPING</c> is an animation that stops at its last frame instead of repeating,
/// which reads as a player freezing mid-stride rather than as an error.
///
/// Named from <c>public/studio.h</c> and asserted against it by <c>StudioFlagTests</c>.
/// </remarks>
internal static class StudioFlags
{
    /// <summary><c>STUDIO_ANIM_RAWPOS</c> — the track holds a <c>Vector48</c>.</summary>
    public const int AnimationRawPosition = 0x01;

    /// <summary><c>STUDIO_ANIM_RAWROT</c> — a <c>Quaternion48</c>, six bytes.</summary>
    public const int AnimationRawRotation = 0x02;

    /// <summary><c>STUDIO_ANIM_ANIMPOS</c> — a run-length encoded position track.</summary>
    public const int AnimationAnimatedPosition = 0x04;

    /// <summary><c>STUDIO_ANIM_ANIMROT</c> — a run-length encoded rotation track.</summary>
    public const int AnimationAnimatedRotation = 0x08;

    /// <summary><c>STUDIO_ANIM_DELTA</c> — the values add to the bone's rest pose.</summary>
    public const int AnimationDelta = 0x10;

    /// <summary><c>STUDIO_ANIM_RAWROT2</c> — a <c>Quaternion64</c>, eight bytes.</summary>
    /// <remarks>
    /// **Two bytes wider than <c>RAWROT</c>, and that is the whole hazard.** The two bits sit four
    /// apart and describe the same field at different precisions; treating one as the other
    /// misaligns the rest of the track by two bytes per bone.
    /// </remarks>
    public const int AnimationRawRotation64 = 0x20;

    /// <summary><c>STUDIO_LOOPING</c> — the last frame should match the first.</summary>
    public const int SequenceLooping = 0x0001;

    /// <summary><c>STUDIO_OVERRIDE</c> — a forward-declared sequence, with no animation of its own.</summary>
    /// <remarks>
    /// Named for what it means here rather than for the engine's word: a model declares the sequence
    /// so another can reference it, and the body arrives from an included model. Reading one as a
    /// real sequence gives an empty animation rather than an error.
    /// </remarks>
    public const int SequenceForwardDeclared = 0x0800;

    /// <summary><c>STUDIO_DELTA</c> — a difference from the rest pose, meant to be layered.</summary>
    /// <remarks>
    /// <c>studio.h:659</c>. The engine adds a delta sequence on top of an already-posed skeleton
    /// (<c>AccumulatePose</c>) rather than playing it; posing one on its own builds a skeleton out
    /// of differences with nothing underneath them.
    /// </remarks>
    public const int SequenceDelta = 0x0004;

    /// <summary><c>STUDIO_SNAP</c>, <c>studio.h:3079</c> — do not interpolate INTO this sequence.</summary>
    /// <remarks>
    /// **An authored cut.** `CSequenceTransitioner::CheckForSequenceChange` empties its whole queue
    /// when the sequence being entered carries this (`sequence_Transitioner.cpp:41`), so nothing
    /// fades out behind it. Cross-fading such a sequence in would add a blend the animator
    /// deliberately removed.
    /// </remarks>
    public const int SequenceSnap = 0x0002;

    /// <summary><c>STUDIO_AUTOPLAY</c>, <c>studio.h:3081</c> — always plays, driven by the clock.</summary>
    /// <remarks>
    /// **The mechanism by which a model animates part of itself with nothing driving it** — a flag
    /// in the wind, a chain, an idle machine. `CalcAutoplaySequences` (<c>bone_setup.cpp:4457</c>)
    /// accumulates every sequence carrying this on top of whatever the entity is already doing, at
    /// weight one, after the layers and before the bone controllers.
    ///
    /// **The autoplay list is COMPUTED, not stored.** `studiohdr_t::CountAutoplaySequences` and
    /// `CopyAutoplaySequences` (<c>studio.cpp:658</c>, <c>:672</c>) walk every sequence testing this
    /// bit, so there is no table to parse — the flag is the whole of the data.
    ///
    /// **Its cycle comes from REAL TIME rather than from the entity's** —
    /// <c>cycle = flRealTime * cps; cycle = cycle - (int)cycle;</c> — which is why it keeps running
    /// on an entity that is standing still, and why two copies of one model are always in step.
    ///
    /// Valve's own comment calls it "temporary", and it has outlived every engine that shipped it.
    /// </remarks>
    public const int SequenceAutoplay = 0x0008;

    /// <summary><c>STUDIO_TYPES</c>, <c>studio.h:3074</c> — the mask over a controller's type.</summary>
    /// <remarks>
    /// **<c>CalcBoneAdj</c> masks before it switches** (<c>bone_setup.cpp:2487</c>). The field
    /// carries more than the axis, so comparing it whole would miss a controller with upper bits
    /// set.
    /// </remarks>
    public const int ControllerTypes = 0x0003FFFF;

    /// <summary><c>STUDIO_X</c>, <c>studio.h:3058</c> — translate along X.</summary>
    public const int ControllerX = 0x0001;

    /// <summary><c>STUDIO_Y</c>, <c>studio.h:3059</c>.</summary>
    public const int ControllerY = 0x0002;

    /// <summary><c>STUDIO_Z</c>, <c>studio.h:3060</c>.</summary>
    public const int ControllerZ = 0x0004;

    /// <summary><c>STUDIO_XR</c>, <c>studio.h:3061</c> — rotate about X, in DEGREES.</summary>
    /// <remarks>
    /// **The rotation cases carry degrees and the translation cases carry units**, which
    /// <c>CalcBoneAdj</c> shows by converting only the former: <c>a0.Init( value * (M_PI / 180.0),
    /// 0, 0 )</c>. A reader that scaled both the same way would rotate a bone by a fraction of a
    /// degree or move it by fifty units.
    /// </remarks>
    public const int ControllerXRotation = 0x0008;

    /// <summary><c>STUDIO_YR</c>, <c>studio.h:3062</c>.</summary>
    public const int ControllerYRotation = 0x0010;

    /// <summary><c>STUDIO_ZR</c>, <c>studio.h:3063</c>.</summary>
    public const int ControllerZRotation = 0x0020;

    /// <summary><c>STUDIO_POST</c>, <c>studio.h:3082</c>.</summary>
    /// <remarks>
    /// **Only meaningful on a <see cref="SequenceDelta"/> sequence**, where it chooses which side
    /// the scaled delta is composed on: <c>QuaternionMA( q1, s2, q2, q1 )</c> with it,
    /// <c>QuaternionSM( s2, q2, q1, q1 )</c> without (<c>bone_setup.cpp:1441-1456</c>). Valve left
    /// the comment beside the define empty, and the branch above is the whole of its meaning.
    /// </remarks>
    public const int SequencePost = 0x0010;
}
