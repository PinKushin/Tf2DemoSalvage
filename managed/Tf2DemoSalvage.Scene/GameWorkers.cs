using System;
using System.Threading.Tasks;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// The one pool every within-frame fork runs on — the engine's `ParallelProcess` over its single job pool (finding 105 C).
/// </summary>
/// <remarks>
/// **One pool, never a second** (finding 105 C): `ThreadedBoneSetup` (c_baseanimating.cpp:2787) and the particle step
/// (particlemgr.cpp:1869-1897) both hand their lists to `ParallelProcess`, which queues `min( nItems - 1, NumThreads() )` jobs
/// on the one job pool, runs items on the calling thread too, and returns only when the list is drained
/// (`jobthread.h:859-946`). The forks never nest and never coexist — bones, then particles, in time — so they share the
/// cores rather than summing them. Each client keeps its own switch, size threshold and fork count in a
/// <see cref="WorkerLane"/> over this.
///
/// **<see cref="Degree"/> is ours**: the threads counting the caller, which the viewer sets to the physical cores less one,
/// leaving one for `mat_queue_mode 2`'s render thread (TF2's pool size is in the closed `vstdlib`, finding 99 §6).
/// </remarks>
public sealed class GameWorkers
{
    private readonly Action<int, Action<int>>? _schedule;

    private ParallelOptions _options = new() { MaxDegreeOfParallelism = 1 };

    /// <summary>Workers that run on the thread pool.</summary>
    public GameWorkers()
    {
    }

    /// <summary>Workers that hand every index to a schedule of the caller's — a test running them backwards.</summary>
    /// <param name="schedule">Runs the body once for each index below the count, in any order.</param>
    /// <remarks>A schedule forks whenever a lane is threaded, whatever the count, so a test always exercises it.</remarks>
    public GameWorkers(Action<int, Action<int>> schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        _schedule = schedule;
    }

    /// <summary>The most threads a fork uses, the calling thread included; 1 or less is serial.</summary>
    public int Degree
    {
        get => _options.MaxDegreeOfParallelism;
        set => _options = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, value) };
    }

    /// <summary>Runs <paramref name="body"/> once per index on the pool if it forks, and says whether it did; returns at the join.</summary>
    /// <param name="count">How many indices.</param>
    /// <param name="body">The work for one index; it must touch only that index's own state.</param>
    /// <param name="threshold">A list this long or shorter is not forked.</param>
    /// <returns>Whether the list was forked; false means nothing ran and the caller runs it serially.</returns>
    internal bool TryFork(int count, Action<int> body, int threshold)
    {
        if (_schedule is not null)
        {
            _schedule(count, body);
            return true;
        }

        if (Degree <= 1 || count <= Math.Max(1, threshold))
        {
            return false;
        }

        // **The serial path throws the body's own exception, so the fork does too.** `Parallel.For` wraps whatever a body
        // throws in an AggregateException, and a catch above this call would then match in one mode and not the other.
        try
        {
            Parallel.For(0, count, _options, body);
        }
        catch (AggregateException wrapped) when (wrapped.InnerExceptions.Count == 1)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(wrapped.InnerExceptions[0]).Throw();
            throw;
        }

        return true;
    }
}
