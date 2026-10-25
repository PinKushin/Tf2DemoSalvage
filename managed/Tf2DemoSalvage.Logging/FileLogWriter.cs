using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Channels;

namespace Tf2DemoSalvage.Logging;

/// <summary>
/// The one open file every logger in a process writes into.
/// </summary>
/// <remarks>
/// **This is `ViewerLog`'s file handling, moved rather than rewritten (D83).** Every behaviour here
/// was paid for by a defect, so the conversion to `ILogger` preserves them exactly:
///
/// - **One stamped file per run, with the process id.** More than one viewer can be alive — a UI
///   suite launches one per fixture — so a reader asking for "the newest log" otherwise gets
///   whichever instance wrote last. The stamp alone cannot separate two started in the same second.
/// - **Pruned AFTER the new file is written, never before.** Pruning first meant every process in a
///   suite computed its deletions from a snapshot its siblings had not written into yet: each
///   trimmed to the limit, then each added one. Measured 2026-08-19 — 207 files against a limit of
///   50, and 929 MB.
/// - **A held stream, not open-append-close per line.** One five-minute run wrote
///   450,157 lines into a 37 MB file, every one a separate open and close. The owner was blunt about
///   it: *"THATS A BAD AI CHOICE, that should be using the async version, non blocking"* — and a
///   held handle beats an asynchronous open, because the expensive part IS the open.
/// - **The caller enqueues; one background thread writes and flushes (D230).** The per-line flush
///   was the main thread's last 100 ms stalls (B595 part 3: 6 of 7 frames over 100 ms). The writer
///   flushes after every drained batch, so durability is unchanged except for lines still queued at
///   a hard kill; `Flush` and `Dispose` drain synchronously and the viewer's crash handlers call it.
///   A frozen main thread no longer stops the log either, since the writer keeps draining.
/// - **Failure to log never fails the caller.** A locked or unwritable file costs its lines and
///   nothing else. A viewer that refuses to open a demo because it cannot write a log has the
///   priority backwards.
/// - **Mirrored to <see cref="Debug"/>**, so the IDE shows it while developing.
/// </remarks>
public sealed class FileLogWriter : IDisposable
{
    /// <summary>Lines the queue holds before the oldest is dropped: minutes of the busiest log measured (~13 lines/s, B595).</summary>
    private const int DefaultCapacity = 1 << 16;

    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(2);

    /// <summary>Held by whoever drains: the writer thread or a synchronous <see cref="Flush"/>. Never by <see cref="Write"/>.</summary>
    private readonly Lock _gate = new();
    private readonly Lock _timingGate = new();
    private readonly string _folder = string.Empty;
    private readonly int _kept;
    private readonly Channel<string> _queue;
    private readonly Thread? _thread;

    [ThreadStatic]
    private static LogWriteTiming _threadTiming;

    private TextWriter? _writer;
    private volatile bool _failed;
    private LogWriteTiming _timing;
    private long _dropped;
    private long _droppedUnreported;
    private int _pending;
    private int _highWater;

    /// <summary>Opens a log for this run.</summary>
    /// <param name="folder">Where logs are written.</param>
    /// <param name="prefix">The file name's leading part, such as <c>viewer</c>.</param>
    /// <param name="banner">A first line naming what is running, or <c>null</c> for none.</param>
    /// <param name="kept">How many runs' logs to keep.</param>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    public FileLogWriter(string folder, string prefix, string? banner = null, int kept = 50)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(prefix);

        _folder = folder;
        _kept = kept;

        Path = System.IO.Path.Combine(
            folder,
            string.Create(
                CultureInfo.InvariantCulture,
                $"{prefix}-{DateTime.Now:yyyyMMdd-HHmmss}-{Environment.ProcessId}.log"));

        _queue = NewQueue(DefaultCapacity);
        Begin(banner);
        _thread = StartWriter();
    }

    /// <summary>A writer over a sink the caller supplies, for tests: a stalled disk, a small queue, or no writer thread.</summary>
    /// <param name="sink">Where lines go.</param>
    /// <param name="capacity">Lines held before the oldest is dropped.</param>
    /// <param name="background">Whether a writer thread drains; without one only <see cref="Flush"/> does.</param>
    internal FileLogWriter(TextWriter sink, int capacity, bool background)
    {
        Path = string.Empty;
        _writer = sink;
        _queue = NewQueue(capacity);
        _thread = background ? StartWriter() : null;
    }

    /// <summary>Lines dropped because the queue was full, oldest first, over this writer's life.</summary>
    public long Dropped => Interlocked.Read(ref _dropped);

    /// <summary>The most lines the queue has held at once.</summary>
    public int HighWater => Volatile.Read(ref _highWater);

    /// <summary>Where this run's log is written.</summary>
    public string Path { get; }

    /// <summary>The folder this application's logs and captures live in.</summary>
    /// <remarks>
    /// **Here rather than in the viewer, because two writers share it and only one of them used to
    /// prune.** Logs and screenshots go to the same directory, and `FileRetention` beside this type
    /// keeps both — the measured failure was 233 screenshots at 203 MB with no retention code at
    /// all, alongside 207 logs against a stated limit of 50.
    /// </remarks>
    public static string DefaultFolder => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Tf2DemoSalvage");

    /// <summary>Whether writing has been abandoned after an IO failure.</summary>
    /// <remarks>
    /// Exposed so a caller can say so once rather than discovering it from an empty file. Nothing
    /// depends on it to decide whether to log — that is this type's business.
    /// </remarks>
    public bool Failed => _failed;

    /// <summary>The DRAINING side: lines written into this file and each batch's write-and-flush (B595, D230).</summary>
    /// <remarks>
    /// `WaitTicks` is always zero here; `SlowWrites` counts batches over the threshold. Under its own
    /// lock, not the drain's, because the drain can hold that for a whole slow flush.
    /// </remarks>
    public LogWriteTiming Timing
    {
        get
        {
            lock (_timingGate)
            {
                return _timing;
            }
        }
    }

    /// <summary>The CALLING side, for lines this thread wrote into any writer: `WaitTicks` is the enqueue (B595, D230).</summary>
    /// <remarks>
    /// Per thread so the viewer's main thread can difference it over one frame. `FlushTicks` is
    /// always zero, because the caller no longer touches the disk — a non-zero one would mean it does.
    /// </remarks>
    public static LogWriteTiming ThreadTiming => _threadTiming;

    /// <summary>Writes one already-formatted line.</summary>
    /// <param name="line">The line, without a trailing newline.</param>
    /// <remarks>
    /// **Mirrored to <see cref="Debug"/> before the file**, so a line still reaches the IDE after
    /// the file has been abandoned. That ordering is deliberate: the two sinks fail independently.
    /// </remarks>
    public void Write(string line)
    {
        // **Only when something is actually listening, because this is a MACHINE-WIDE lock.**
        // `Debug.WriteLine` is `OutputDebugString`, which serialises every caller on the system
        // through the global `DBWinMutex` and shared buffer — so its cost depends on what else is
        // running rather than on this process. `Debugger.IsAttached` keeps the IDE case this was
        // written for and drops the call when nothing is there to read it.
        //
        // **This was NOT the stall, and the honest record matters more than the tidy story.** It
        // was changed first on the theory that it was, and the stall continued unchanged: sink
        // still read 110-125 ms over a full run afterwards. The per-line flush was what blocked, and D230 moved it to the writer thread. Kept
        // because taking a system-wide mutex for an absent listener is worth not doing, not because
        // it fixed anything.
        if (Debugger.IsAttached)
        {
            Debug.WriteLine(line);
        }

        if (_failed)
        {
            return;
        }

        long asked = Stopwatch.GetTimestamp();

        // Never waits: the queue drops its OLDEST line when full (D230), and after Dispose this returns false.
        if (_queue.Writer.TryWrite(line))
        {
            int pending = Interlocked.Increment(ref _pending);
            int high = Volatile.Read(ref _highWater);

            while (pending > high)
            {
                int seen = Interlocked.CompareExchange(ref _highWater, pending, high);

                if (seen == high)
                {
                    break;
                }

                high = seen;
            }
        }

        // Measured here, where the enqueue happens, never recomputed by a caller (B243, B595).
        CountOnThisThread(LogWriteTiming.One(Stopwatch.GetTimestamp() - asked, 0));
    }

    private static void CountOnThisThread(LogWriteTiming one) => _threadTiming += one;

    /// <summary>Drains every queued line to disk on the calling thread, for crash and exit handlers.</summary>
    /// <param name="timeout">How long to wait for a drain already in progress.</param>
    /// <returns>Whether the queue was drained; false when the writer was stuck past the timeout.</returns>
    public bool Flush(TimeSpan timeout)
    {
        if (!_gate.TryEnter(timeout))
        {
            return false;
        }

        try
        {
            Drain();

            // True only when the lines reached a sink: not after a sink failure (lines left queued), and not after
            // Dispose or a file that never opened (nothing written at all).
            return !_failed && _writer is not null;
        }
        finally
        {
            _gate.Exit();
        }
    }

    /// <summary>Drains what is queued and closes the file.</summary>
    public void Dispose()
    {
        if (!_queue.Writer.TryComplete())
        {
            return;
        }

        bool joined = _thread?.Join(CloseTimeout) ?? true;

        if (!joined || !_gate.TryEnter(CloseTimeout))
        {
            // ponytail: a writer stuck on the disk past two seconds keeps the file; the process is ending anyway.
            return;
        }

        try
        {
            Drain();
            _writer?.Dispose();
            _writer = null;
        }
        finally
        {
            _gate.Exit();
        }
    }

    private Channel<string> NewQueue(int capacity) => Channel.CreateBounded<string>(
        new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = false,
            SingleWriter = false,
        },
        _ =>
        {
            Interlocked.Increment(ref _dropped);
            Interlocked.Increment(ref _droppedUnreported);
            Interlocked.Decrement(ref _pending);
        });

    private Thread StartWriter()
    {
        Thread thread = new(Run) { IsBackground = true, Name = "file log writer" };
        thread.Start();
        return thread;
    }

    /// <summary>The writer thread: wait for lines, drain them all, flush once, repeat until the queue completes.</summary>
    private void Run()
    {
        // Blocking is this thread's whole job, so waiting synchronously on the channel is right here.
        while (_queue.Reader.WaitToReadAsync().AsTask().GetAwaiter().GetResult())
        {
            lock (_gate)
            {
                Drain();
            }

            if (_failed)
            {
                return;
            }
        }
    }

    /// <summary>Writes every queued line in queue order and flushes once. Caller holds <see cref="_gate"/>.</summary>
    private void Drain()
    {
        int lines = 0;
        long started = Stopwatch.GetTimestamp();

        try
        {
            // Checked per line, so the note lands where the gap is: drops happen while a batch is mid-write.
            while (_queue.Reader.TryRead(out string? line))
            {
                Interlocked.Decrement(ref _pending);

                if (Interlocked.Exchange(ref _droppedUnreported, 0) is var dropped and > 0)
                {
                    _writer?.WriteLine(string.Create(
                        CultureInfo.InvariantCulture, $"({dropped} log lines dropped here: the queue was full)"));
                }

                _writer?.WriteLine(line);
                lines++;
            }

            if (lines == 0)
            {
                return;
            }

            _writer?.Flush();
        }
#pragma warning disable CA1031 // Any sink failure abandons the log: on the writer thread an escaped exception would end the process.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            // Every type, not only IO's: a NotSupportedException, an EncoderFallbackException or a custom sink's own
            // exception, unhandled here, would take the process — and failure to log never fails the caller. Recorded
            // in Failed; Write sees it and stops queueing, and the writer thread stops.
            Debug.WriteLine($"file log abandoned: {failure.GetType().Name}: {failure.Message}");
            _failed = true;
            _writer = null;
            return;
        }

        long spent = Stopwatch.GetTimestamp() - started;
        LogWriteTiming batch = LogWriteTiming.One(0, spent) with { Writes = lines };

        lock (_timingGate)
        {
            _timing += batch;
        }
    }

    private void Begin(string? banner)
    {
        try
        {
            Directory.CreateDirectory(_folder);

            // **`FileShare.ReadWrite | FileShare.Delete`, and this is not optional.** The log has to
            // be readable WHILE the process holding it is running: the UI suite counts lines in it
            // to decide whether the viewer did something, and diagnosing a live run means tailing
            // it. A plain `new StreamWriter(path, …)` takes an exclusive handle, and the first
            // version of this did — every test that read the file back failed with "the process
            // cannot access the file", which is precisely the symptom a person tailing a running
            // viewer would hit.
            //
            // `Delete` is in there so retention can remove an older log that some reader still has
            // open, rather than failing the prune.
            _writer = new StreamWriter(
                new FileStream(
                    Path,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.ReadWrite | FileShare.Delete),
                Encoding.UTF8)
            {
                // **Off since D230: the writer thread flushes after every drained batch instead.** It
                // was on per line (B191) because a buffered tail lost in a crash is the part worth
                // having, and that still holds — the batch flush keeps it, off the main thread.
                AutoFlush = false,
            };

            if (banner is not null)
            {
                _writer.WriteLine(banner);
                _writer.Flush();
            }

            // After the write, not before — see the type remarks. Pruning first races every sibling
            // process in a suite and converges on the wrong number.
            FileRetention.Keep(_folder, "*.log", _kept);
        }
        catch (Exception failure) when (
            failure is IOException or UnauthorizedAccessException or ArgumentException
                or NotSupportedException)
        {
            _failed = true;
            _writer = null;
        }
    }
}
