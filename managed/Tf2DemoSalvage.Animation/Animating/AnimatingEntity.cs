using System;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// One entity that has a skeleton: it owns its bones and decides when to build them.
/// </summary>
/// <remarks>
/// **This is <c>C_BaseAnimating</c>'s bone half, and owning the bones is the architectural point**
/// (D88). The arrangement it replaces had one set posing every entity inside a single loop, which is
/// why the ordering had to be solved with a depth sort and why nothing could answer "has this
/// entity already been posed this frame".
///
/// **The engine has no ordering step at all.** A merged entity asks its parent for bones where it
/// stands — <c>m_pFollow-&gt;SetupBones(...)</c>, <c>bone_merge_cache.cpp:130</c> — and
/// <see cref="SetupBones(int, double)"/> being idempotent within a frame is what makes that safe rather than
/// quadratic. So there is no list, no pass, no sort: an entity that needs a parent asks for it, to
/// whatever depth the chain runs.
///
/// Two guards make it idempotent, both from <c>c_baseanimating.cpp</c>:
///
/// <list type="number">
/// <item>the frame check at <c>:2874</c>, which notices this is the first request this frame;</item>
/// <item>the readable-bones early-out at <c>:2911</c>, which returns immediately when everything the
/// caller asked for is already built.</item>
/// </list>
///
/// The second is the one that pays: a player worn by six items is posed once and the other five
/// merges are an integer comparison.
/// </remarks>
public sealed class AnimatingEntity
{
    private readonly IBonePose _pose;
    private readonly BoneFrameCounter _clock;
    private readonly BoneAccessor _accessor;
    private readonly BoneBitList _written;
    private readonly ILogger _log;

    /// <summary>Creates an entity over a model's pose source.</summary>
    /// <param name="pose">What knows how to blend and transform this model's bones.</param>
    /// <param name="clock">Which frame the caches belong to; shared across one scene.</param>
    /// <param name="loggers">Where it reports, or null for nowhere.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public AnimatingEntity(IBonePose pose, BoneFrameCounter clock, ILoggerFactory? loggers = null)
    {
        ArgumentNullException.ThrowIfNull(pose);
        ArgumentNullException.ThrowIfNull(clock);

        _pose = pose;
        _clock = clock;
        _accessor = new BoneAccessor(pose.BoneCount);
        _written = new BoneBitList(pose.BoneCount);
        _log = (loggers ?? NullLoggerFactory.Instance).CreateLogger("props");
    }

    /// <summary>The entity this one is bone-merged onto, or null when it stands on its own.</summary>
    /// <remarks>
    /// **Valve's <c>GetMoveParent</c> / <c>FindFollowedEntity</c>.** Settable because the wire says
    /// so per tick: a weapon is picked up and dropped, and <c>CBoneMergeCache::UpdateCache</c>
    /// re-pairs when the followed entity changes.
    /// </remarks>
    public AnimatingEntity? Follows { get; set; }

    /// <summary>This entity's bone matrices.</summary>
    public BoneAccessor Bones => _accessor;

    /// <summary>How deep a follow chain may run before it is treated as malformed.</summary>
    /// <remarks>
    /// **Valve has no such bound and this needs one.** The engine's parent links are built by the
    /// engine; a demo this project exists to open may carry anything, and a cycle would recurse
    /// until the stack ends. The depth sort it replaces guarded the same way, by stopping at the
    /// entity count.
    ///
    /// Sixteen because the deepest real chain is three — an attachment on a weapon on a player —
    /// and a bound has to be well clear of legitimate use to be a corruption check rather than a
    /// feature limit.
    /// </remarks>
    public const int MaximumFollowDepth = 16;

    /// <summary>Builds this entity's bones, and its parent's first if it merges onto one.</summary>
    /// <param name="boneMask">Which bones are wanted, as a <c>BONE_USED_BY_*</c> mask.</param>
    /// <param name="currentTime">Demo time.</param>
    /// <returns>Whether the bones are now readable for that mask.</returns>
    /// <remarks>
    /// **Returns false rather than throwing when the chain cannot be built**, which is the engine's
    /// own contract: <c>MergeMatchingBones</c> checks the result and, when it is false, shrinks
    /// every merged bone to zero size rather than drawing an item at the map origin
    /// (<c>bone_merge_cache.cpp:134</c>). A caller that ignores it draws the hat in the wrong place;
    /// a caller that treats it as an exception refuses to draw the scene.
    /// </remarks>
    public bool SetupBones(int boneMask, double currentTime) =>
        SetupBones(boneMask, currentTime, MaximumFollowDepth);

    /// <summary>Which frame this entity's cached bones belong to.</summary>
    /// <remarks><c>m_iMostRecentModelBoneCounter</c>. Zero is never a valid frame, so a fresh
    /// entity always misses.</remarks>
    private long _builtOn;

    /// <summary>What was asked for over the whole of the previous frame.</summary>
    /// <remarks>
    /// **<c>m_iPrevBoneMask</c>, and it is what makes the threaded pre-pass possible.** Valve poses
    /// last frame's expensive roots speculatively with <c>boneMask = -1</c>, which resolves to this
    /// (<c>c_baseanimating.cpp:2827</c>): whatever was needed last frame is the best guess at what
    /// will be needed next.
    /// </remarks>
    private int _previousMask;

    /// <summary>Everything asked for so far this frame.</summary>
    private int _accumulatedMask;

    private bool SetupBones(int boneMask, double currentTime, int budget)
    {
        if (_pose.BoneCount == 0)
        {
            return false;
        }

        if (budget <= 0)
        {
            // A cycle, or a chain deeper than anything the game builds. Reported rather than
            // swallowed: a silently truncated chain is a hat in the wrong place with no trace.
            _log.LogWarning(
                "{Message}",
                $"a follow chain ran past {MaximumFollowDepth} links, so it is being treated as a " +
                $"cycle; the deepest legitimate chain is three");

            return false;
        }

        // **Guard one: is this the first request this frame?** c_baseanimating.cpp:2874. Everything
        // cached belongs to a previous frame, so nothing is readable until it is rebuilt — and what
        // was asked for last frame is remembered, because the threaded pre-pass poses from it.
        if (_builtOn != _clock.Frame)
        {
            _builtOn = _clock.Frame;
            _previousMask = _accumulatedMask;
            _accumulatedMask = 0;

            _accessor.ReadableBones = 0;
            _accessor.WritableBones = 0;
        }

        _accumulatedMask |= boneMask;

        // **Guard two, and it is the one that pays.** c_baseanimating.cpp:2911. A player worn by
        // six items is posed once; the other five merges are this comparison. Note it must be a
        // SUBSET test rather than equality — a narrower request than one already satisfied is free,
        // and a request for a bit not yet built has to rebuild even though the entity has been
        // posed this frame.
        if ((_accessor.ReadableBones & boneMask) == boneMask)
        {
            return true;
        }

        // **The parent is asked FIRST, and asked rather than scheduled.** This is the whole of
        // Valve's ordering: bone_merge_cache.cpp:130 calls SetupBones on the followed entity where
        // it stands, and the guards above make a repeat free.
        if (Follows is { } parent && !parent.SetupBones(boneMask, currentTime, budget - 1))
        {
            return false;
        }

        _written.Clear();

        // **Widened to include what was already built**, per c_baseanimating.cpp:2920. A second
        // request for a different mask rebuilds the union rather than only the new bits, because a
        // bone's parent may be outside the new mask and its transform is still needed.
        int wanted = boneMask | _accessor.ReadableBones | _previousMask;

        _accessor.WritableBones = wanted;
        _accessor.ReadableBones = wanted;

        _pose.Build(wanted, currentTime, _accessor, _written);

        return true;
    }
}
