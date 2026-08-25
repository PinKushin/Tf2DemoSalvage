namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// Which frame the bone caches belong to.
/// </summary>
/// <remarks>
/// **This is <c>g_iModelBoneCounter</c>** (<c>c_baseanimating.cpp:653</c>), bumped once per frame by
/// <c>InvalidateBoneCaches</c> at <c>:3153</c>. An entity compares its own
/// <c>m_iMostRecentModelBoneCounter</c> against it and, on a mismatch, treats everything it cached
/// as belonging to a previous frame.
///
/// **A counter rather than a per-entity flag, and the difference is the whole design.** Invalidating
/// N entities costs one increment instead of N writes, and an entity nobody asks about this frame is
/// never touched at all — it simply notices next time it is asked. That matters here for the same
/// reason it matters in the engine: a demo can carry hundreds of animating entities and most frames
/// draw a fraction of them.
///
/// **An instance rather than a static**, which is a deliberate departure in FORM and not in
/// behaviour (D86). Valve's is a file-scope global because the client has exactly one world; this
/// project opens several demos in one process, and the offscreen render target in the test suite
/// runs a second scene concurrently with the viewer's. A static counter would make those two
/// invalidate each other — a cross-talk failure that appears as a stale pose in one scene when the
/// other advances, which is close to unfindable. The instance is per scene and everything else
/// about the mechanism is unchanged.
/// </remarks>
public sealed class BoneFrameCounter
{
    /// <summary>Which frame this is.</summary>
    /// <remarks>
    /// Starts at 1 so that an entity's default of 0 is always a mismatch. Zero-initialised state
    /// that happens to equal a valid counter value would make every entity think its empty cache
    /// was current for the first frame — bones of identity matrices, drawn once, and then correct
    /// forever after, which is exactly the kind of one-frame artefact nobody reproduces.
    /// </remarks>
    public long Frame { get; private set; } = 1;

    /// <summary>Ends the current frame, so every cached pose is stale.</summary>
    public void Advance() => Frame++;
}
