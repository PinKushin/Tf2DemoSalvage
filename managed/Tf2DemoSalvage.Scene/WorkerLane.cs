using System;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// One client's fork over the shared <see cref="GameWorkers"/> — `r_threaded_particles` (particlemgr.cpp:1543, default 1;
/// finding 103) or `cl_threaded_bone_setup` (c_baseanimating.cpp:2743, default 0; finding 105).
/// </summary>
/// <remarks>
/// **A body writes only its own index's state.** The caller merges in index order afterwards, so the output is the serial
/// output whatever order the indices finish in — which a test proves by handing the pool a schedule of its own.
///
/// **<see cref="Threshold"/> is ours, not the engine's**: the engine forks any list of two or more (`nCount &gt; 1`,
/// c_baseanimating.cpp:2783), but a .NET fork costs more than a handful of small items, so a list at or below it runs
/// serially. The default is the engine's 1.
/// </remarks>
public sealed class WorkerLane
{
    /// <summary>A lane over a pool of its own of one thread — serial, for a caller that never forks (tests).</summary>
    public WorkerLane()
        : this(new GameWorkers())
    {
    }

    /// <summary>A lane over the shared pool.</summary>
    /// <param name="pool">The pool every fork runs on.</param>
    public WorkerLane(GameWorkers pool)
    {
        ArgumentNullException.ThrowIfNull(pool);
        Pool = pool;
    }

    /// <summary>The pool the lane forks onto.</summary>
    public GameWorkers Pool { get; }

    /// <summary>The cvar: off runs every item in order on the calling thread.</summary>
    public bool Threaded { get; set; } = true;

    /// <summary>A list this long or shorter runs serially: our addition, see the remarks.</summary>
    public int Threshold { get; set; } = 1;

    /// <summary>How many lists were forked, ever — counted where the fork is taken, on the calling thread (B243).</summary>
    public long Forks { get; private set; }

    /// <summary>Runs <paramref name="body"/> once for each index below <paramref name="count"/>, and returns at the join.</summary>
    /// <param name="count">How many indices.</param>
    /// <param name="body">The work for one index; it must touch only that index's own state.</param>
    public void For(int count, Action<int> body)
    {
        ArgumentNullException.ThrowIfNull(body);

        if (Threaded && Pool.TryFork(count, body, Threshold))
        {
            Forks++;
            return;
        }

        for (int index = 0; index < count; index++)
        {
            body(index);
        }
    }
}
