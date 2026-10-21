using System;
using System.Diagnostics;
using System.Threading;

namespace Tf2DemoSalvage.Presentation;

/// <summary>A thread that wakes every millisecond and tells a <see cref="PauseLedger"/> each time it could not.</summary>
/// <remarks>
/// **`--measure` only** (finding 87). It does no work, so when it misses its wake by more than
/// <see cref="PauseLedger.GapMilliseconds"/> the whole process, or the machine, was stopped: a collector suspension, CPU
/// starvation, the scheduler, paging. A frame stall with no gap beside it was the render thread's own.
///
/// **The wait is on its own stop signal with a 1 ms timeout**, not a sleep: it is the condition that ends the thread,
/// and the timeout is the beat. The timer's resolution is whatever the process has set (15.6 ms by default), far below
/// the 50 ms that counts.
/// </remarks>
public sealed class Heartbeat : IDisposable
{
    private readonly ManualResetEvent _stop = new(false);
    private readonly Thread _thread;

    /// <summary>Starts beating into <paramref name="ledger"/>.</summary>
    /// <param name="ledger">Where gaps go.</param>
    /// <exception cref="ArgumentNullException"><paramref name="ledger"/> is null.</exception>
    public Heartbeat(PauseLedger ledger)
    {
        ArgumentNullException.ThrowIfNull(ledger);

        _thread = new Thread(() =>
        {
            long previous = Stopwatch.GetTimestamp();

            while (!_stop.WaitOne(1))
            {
                long now = Stopwatch.GetTimestamp();

                ledger.Beat(previous, now);
                previous = now;
            }
        })
        {
            IsBackground = true,
            Name = "measure heartbeat",
        };

        _thread.Start();
    }

    /// <summary>Stops the thread and waits for it.</summary>
    public void Dispose()
    {
        _stop.Set();
        _thread.Join();
        _stop.Dispose();
    }
}
