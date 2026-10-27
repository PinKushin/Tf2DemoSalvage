namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>What one scene's <c>SetupBones</c> calls cost, split by whether the entity is a root (finding 105 A6).</summary>
/// <remarks>
/// **Measured at the call, exclusive of the parent it asks for**, so a child that builds its wearer first does not carry the
/// wearer's cost: a root is an entity that merges onto nothing, and a child is what stays on the draw
/// (<c>bone_merge_cache.cpp:130</c>). Running totals, read as deltas either side of a call (B243: the value carried from
/// where it was timed).
///
/// **Eligible is the subset a threaded pre-pass would take**, by the engine's own test — <c>nBoneCount &gt;= 16</c> on an
/// entity with no move parent (<c>c_baseanimating.cpp:2897</c>) — less one exclusion of ours, a ragdoll-driven body, whose
/// physics is not shown safe off the game thread (finding 105 A3.4). A parent that is not a merge is invisible here; the
/// caller, which has the prop, counts those.
///
/// **One per scene, beside the bone clock**, for the reason the clock is not static (D86). Single-threaded: a fork of
/// the bone setup must give each worker its own and sum them at the join (finding 105 A3.2).
/// </remarks>
public sealed class BoneSetupMeter
{
    /// <summary>The fewest bones the engine pre-poses on a thread — <c>nBoneCount &gt;= 16</c>, <c>c_baseanimating.cpp:2897</c>.</summary>
    public const int ThreadedMinimumBones = 16;

    /// <summary>Stopwatch ticks in roots' calls, ever — the guards, the blend inputs and the build.</summary>
    public long RootTicks { get; private set; }

    /// <summary>Of <see cref="RootTicks"/>: the eligible roots'.</summary>
    public long EligibleTicks { get; private set; }

    /// <summary>Stopwatch ticks in children's calls, ever, less the parent each asked for.</summary>
    public long ChildTicks { get; private set; }

    /// <summary>Roots that built bones, ever.</summary>
    public int RootBuilds { get; private set; }

    /// <summary>Of <see cref="RootBuilds"/>: the eligible roots'.</summary>
    public int EligibleBuilds { get; private set; }

    /// <summary>Children that built bones, ever.</summary>
    public int ChildBuilds { get; private set; }

    /// <summary>Records one call.</summary>
    /// <param name="root">Whether the entity follows nothing.</param>
    /// <param name="eligible">Whether a threaded pre-pass would take it; meaningful only for a root.</param>
    /// <param name="built">Whether the call built bones rather than returning the cached ones.</param>
    /// <param name="ticks">Its own ticks, without its parent's.</param>
    public void Record(bool root, bool eligible, bool built, long ticks)
    {
        int builds = built ? 1 : 0;

        if (!root)
        {
            ChildTicks += ticks;
            ChildBuilds += builds;
            return;
        }

        RootTicks += ticks;
        RootBuilds += builds;

        if (eligible)
        {
            EligibleTicks += ticks;
            EligibleBuilds += builds;
        }
    }
}
