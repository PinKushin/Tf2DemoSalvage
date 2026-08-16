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
}
