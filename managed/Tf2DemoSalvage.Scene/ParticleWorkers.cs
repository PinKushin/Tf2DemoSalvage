using System;
using System.Threading.Tasks;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// The fork-join the particle step runs on — `r_threaded_particles` (particlemgr.cpp:1543, default 1; finding 103).
/// </summary>
/// <remarks>
/// **The engine's shape**: `CParticleMgr::UpdateNewEffects` hands its sim list to `ParallelProcess( …, ProcessPSystem )`
/// (particlemgr.cpp:1869-1897), which queues `min( nItems - 1, NumThreads() )` jobs, runs items on the calling thread too,
/// and returns only when the list is drained (`jobthread.h:859-946`). <see cref="For"/> is that: `Parallel.For` runs on
/// the calling thread as well and returns at the join.
///
/// **A body writes only its own index's state.** The caller merges in index order afterwards, so the output is the
/// serial output whatever order the indices finish in — which a test proves by handing a schedule of its own.
///
/// **Two things here are ours, not the engine's.** <see cref="Degree"/> is the threads counting the caller (TF2's pool
/// size is in the closed `vstdlib`, finding 99 §6); the viewer sets it to the physical cores less one, leaving one for
/// `mat_queue_mode 2`'s render thread. And <see cref="Threshold"/>: the engine forks any non-empty list, but a .NET fork
/// costs more than a handful of small effects, so a list at or below it runs serially.
/// </remarks>
public sealed class ParticleWorkers
{
    /// <summary>The default for <see cref="Threshold"/>.</summary>
    /// <remarks>0, 4 and 16 measured the same on f12 (docs/measurements/2026-10-26-threaded-particles-and-sun.md); kept as a guard for tiny lists.</remarks>
    public const int DefaultThreshold = 4;

    private readonly Action<int, Action<int>>? _schedule;

    private ParallelOptions _options = new() { MaxDegreeOfParallelism = 1 };

    /// <summary>Workers that run on the thread pool.</summary>
    public ParticleWorkers()
    {
    }

    /// <summary>Workers that hand every index to a schedule of the caller's — a test running them backwards.</summary>
    /// <param name="schedule">Runs the body once for each index below the count, in any order.</param>
    /// <remarks>A schedule forks whenever <see cref="Threaded"/> is on, whatever the count, so a test always exercises it.</remarks>
    public ParticleWorkers(Action<int, Action<int>> schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        _schedule = schedule;
    }

    /// <summary>`r_threaded_particles`: off runs every step in order on the calling thread.</summary>
    public bool Threaded { get; set; } = true;

    /// <summary>The most threads a fork uses, the calling thread included; 1 or less is serial.</summary>
    public int Degree
    {
        get => _options.MaxDegreeOfParallelism;
        set => _options = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, value) };
    }

    /// <summary>A list this long or shorter runs serially: our addition, see the remarks.</summary>
    public int Threshold { get; set; } = DefaultThreshold;

    /// <summary>How many lists were forked, ever — counted where the fork is taken, on the calling thread (B243).</summary>
    public long Forks { get; private set; }

    /// <summary>Runs <paramref name="body"/> once for each index below <paramref name="count"/>, and returns at the join.</summary>
    /// <param name="count">How many indices.</param>
    /// <param name="body">The work for one index; it must touch only that index's own state.</param>
    public void For(int count, Action<int> body)
    {
        ArgumentNullException.ThrowIfNull(body);

        if (Threaded && _schedule is not null)
        {
            Forks++;
            _schedule(count, body);
            return;
        }

        if (!Threaded || Degree <= 1 || count <= Math.Max(1, Threshold))
        {
            for (int index = 0; index < count; index++)
            {
                body(index);
            }

            return;
        }

        Forks++;
        Parallel.For(0, count, _options, body);
    }
}
