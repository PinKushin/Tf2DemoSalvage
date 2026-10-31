using System.Threading;

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
/// **One per scene, beside the bone clock**, for the reason the clock is not static (D86), and **added to atomically**,
/// because the pre-pass's roots record from the pool's threads (finding 105 A3.2): a delta read across the fork, after its
/// join, is the fork's exact sum.
/// </remarks>
public sealed class BoneSetupMeter
{
    /// <summary>The fewest bones the engine pre-poses on a thread — <c>nBoneCount &gt;= 16</c>, <c>c_baseanimating.cpp:2897</c>.</summary>
    public const int ThreadedMinimumBones = 16;

    private long _rootTicks;
    private long _eligibleTicks;
    private long _childTicks;
    private int _rootBuilds;
    private int _eligibleBuilds;
    private int _childBuilds;

    /// <summary>Stopwatch ticks in roots' calls, ever — the guards, the blend inputs and the build.</summary>
    public long RootTicks => Interlocked.Read(ref _rootTicks);

    /// <summary>Of <see cref="RootTicks"/>: the eligible roots'.</summary>
    public long EligibleTicks => Interlocked.Read(ref _eligibleTicks);

    /// <summary>Stopwatch ticks in children's calls, ever, less the parent each asked for.</summary>
    public long ChildTicks => Interlocked.Read(ref _childTicks);

    /// <summary>Roots that built bones, ever.</summary>
    public int RootBuilds => Volatile.Read(ref _rootBuilds);

    /// <summary>Of <see cref="RootBuilds"/>: the eligible roots'.</summary>
    public int EligibleBuilds => Volatile.Read(ref _eligibleBuilds);

    /// <summary>Children that built bones, ever.</summary>
    public int ChildBuilds => Volatile.Read(ref _childBuilds);

    /// <summary>Of <see cref="RootTicks"/>: the parts each root call carried, ever.</summary>
    public RootSetupParts RootParts => new(
        Interlocked.Read(ref _blend),
        Interlocked.Read(ref _base),
        Interlocked.Read(ref _accumulate),
        Interlocked.Read(ref _sampling),
        Interlocked.Read(ref _samples),
        Interlocked.Read(ref _adjust),
        Interlocked.Read(ref _loop),
        Interlocked.Read(ref _ik),
        Interlocked.Read(ref _finish),
        Interlocked.Read(ref _seed),
        Interlocked.Read(ref _locks),
        Interlocked.Read(ref _compose),
        Interlocked.Read(ref _enumerators));

    private long _blend;
    private long _base;
    private long _accumulate;
    private long _sampling;
    private long _samples;
    private long _adjust;
    private long _loop;
    private long _ik;
    private long _finish;
    private long _seed;
    private long _locks;
    private long _compose;
    private long _enumerators;

    /// <summary>Records one call.</summary>
    /// <param name="root">Whether the entity follows nothing.</param>
    /// <param name="eligible">Whether a threaded pre-pass would take it; meaningful only for a root.</param>
    /// <param name="built">Whether the call built bones rather than returning the cached ones.</param>
    /// <param name="ticks">Its own ticks, without its parent's.</param>
    /// <param name="parts">Of <paramref name="ticks"/>: the parts the call carried; added for a root only.</param>
    public void Record(bool root, bool eligible, bool built, long ticks, in RootSetupParts parts = default)
    {
        int builds = built ? 1 : 0;

        if (!root)
        {
            Interlocked.Add(ref _childTicks, ticks);
            Interlocked.Add(ref _childBuilds, builds);
            return;
        }

        Interlocked.Add(ref _rootTicks, ticks);
        Interlocked.Add(ref _rootBuilds, builds);

        if (eligible)
        {
            Interlocked.Add(ref _eligibleTicks, ticks);
            Interlocked.Add(ref _eligibleBuilds, builds);
        }

        // The parts of those ticks, beside them; a child's are not a root's (the 2026-10-31 setup-parts split). A cache hit
        // carries a blend and nothing else, so it pays one add rather than nine.
        Interlocked.Add(ref _blend, parts.Blend);

        if (built)
        {
            Interlocked.Add(ref _base, parts.Base);
            Interlocked.Add(ref _accumulate, parts.Accumulate);
            Interlocked.Add(ref _sampling, parts.Sampling);
            Interlocked.Add(ref _samples, parts.Samples);
            Interlocked.Add(ref _adjust, parts.Adjust);
            Interlocked.Add(ref _loop, parts.Loop);
            Interlocked.Add(ref _ik, parts.Ik);
            Interlocked.Add(ref _finish, parts.Finish);
            Interlocked.Add(ref _seed, parts.Seed);
            Interlocked.Add(ref _locks, parts.Locks);
            Interlocked.Add(ref _compose, parts.Compose);
            Interlocked.Add(ref _enumerators, parts.Enumerators);
        }
    }
}
