using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

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
/// - **A held stream with `AutoFlush`, not open-append-close per line.** One five-minute run wrote
///   450,157 lines into a 37 MB file, every one a separate open and close. The owner was blunt about
///   it: *"THATS A BAD AI CHOICE, that should be using the async version, non blocking"* — and a
///   held handle beats an asynchronous open, because the expensive part IS the open. `AutoFlush`
///   stays on because this project debugs by log and a buffered tail lost in a crash is exactly the
///   part worth having.
/// - **Failure to log never fails the caller.** A locked or unwritable file costs its lines and
///   nothing else. A viewer that refuses to open a demo because it cannot write a log has the
///   priority backwards.
/// - **Mirrored to <see cref="Debug"/>**, so the IDE shows it while developing.
/// </remarks>
public sealed class FileLogWriter : IDisposable
{
    private readonly Lock _gate = new();
    private readonly string _folder;
    private readonly int _kept;

    private StreamWriter? _writer;
    private bool _failed;
    private bool _disposed;

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

        Begin(banner);
    }

    /// <summary>Where this run's log is written.</summary>
    public string Path { get; }

    /// <summary>Whether writing has been abandoned after an IO failure.</summary>
    /// <remarks>
    /// Exposed so a caller can say so once rather than discovering it from an empty file. Nothing
    /// depends on it to decide whether to log — that is this type's business.
    /// </remarks>
    public bool Failed
    {
        get
        {
            lock (_gate)
            {
                return _failed;
            }
        }
    }

    /// <summary>Writes one already-formatted line.</summary>
    /// <param name="line">The line, without a trailing newline.</param>
    /// <remarks>
    /// **Mirrored to <see cref="Debug"/> before the file**, so a line still reaches the IDE after
    /// the file has been abandoned. That ordering is deliberate: the two sinks fail independently.
    /// </remarks>
    public void Write(string line)
    {
        Debug.WriteLine(line);

        lock (_gate)
        {
            if (_failed || _disposed || _writer is not { } writer)
            {
                return;
            }

            try
            {
                writer.WriteLine(line);
            }
            catch (Exception failure) when (
                failure is IOException or UnauthorizedAccessException or ArgumentException
                    or ObjectDisposedException)
            {
                // Stop trying rather than throwing on every line afterwards.
                _failed = true;
                _writer = null;
            }
        }
    }

    /// <summary>Closes the file.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _writer?.Dispose();
            _writer = null;
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
                AutoFlush = true,
            };

            if (banner is not null)
            {
                _writer.WriteLine(banner);
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
