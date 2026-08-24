using System;
using System.Collections.Concurrent;
using System.Globalization;

using Microsoft.Extensions.Logging;

namespace Tf2DemoSalvage.Logging;

/// <summary>
/// An <see cref="ILoggerProvider"/> that writes to one file per run.
/// </summary>
/// <remarks>
/// **The piece .NET does not ship (D83).** Console, Debug, EventSource and EventLog are the
/// built-ins; there is no file provider, which is why this solution had a hand-rolled static logger
/// on one side and `ILogger` on the other. With this, both sides use the same abstraction and the
/// same sink.
///
/// **The category IS the area.** `ViewerLog` took an area string per call — `"assets"`, `"render"`,
/// `"audio"` — and wrote it in brackets. A logger category is exactly that concept, so a logger
/// created for `"assets"` produces byte-identical lines to the old `ViewerLog.Write("assets", …)`.
/// Keeping the format identical is not nostalgia: `ViewerSession` in the UI suite counts lines by
/// matching on them, and several of this project's diagnostics are greps.
/// </remarks>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new(StringComparer.Ordinal);
    private readonly FileLogWriter _writer;
    private readonly bool _owned;

    /// <summary>Creates a provider writing to a new file.</summary>
    /// <param name="folder">Where logs are written.</param>
    /// <param name="prefix">The file name's leading part, such as <c>viewer</c>.</param>
    /// <param name="banner">A first line naming what is running, or <c>null</c>.</param>
    /// <param name="kept">How many runs' logs to keep.</param>
    public FileLoggerProvider(string folder, string prefix, string? banner = null, int kept = 50)
        : this(new FileLogWriter(folder, prefix, banner, kept), owned: true)
    {
    }

    /// <summary>Creates a provider over a writer somebody else owns.</summary>
    /// <param name="writer">The open log.</param>
    /// <param name="owned">Whether disposing this provider should close the writer.</param>
    /// <exception cref="ArgumentNullException"><paramref name="writer"/> is null.</exception>
    /// <remarks>
    /// **Two processes-worth of loggers can share one file**, which is what the viewer needs: the
    /// form, the scene and the renderer all log into the run's single log rather than one file each.
    /// </remarks>
    public FileLoggerProvider(FileLogWriter writer, bool owned = false)
    {
        ArgumentNullException.ThrowIfNull(writer);

        _writer = writer;
        _owned = owned;
    }

    /// <summary>Where this run's log is written.</summary>
    public string Path => _writer.Path;

    /// <summary>Whether writing has been abandoned after an IO failure.</summary>
    public bool Failed => _writer.Failed;

    /// <inheritdoc/>
    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName ?? string.Empty, name => new FileLogger(name, _writer));

    /// <inheritdoc/>
    public void Dispose()
    {
        _loggers.Clear();

        if (_owned)
        {
            _writer.Dispose();
        }
    }
}

/// <summary>One category's view of the run's log file.</summary>
/// <remarks>
/// **The level column is fixed width and matches what `ViewerLog` wrote**, because things read this
/// file by pattern. Information is five spaces and Warning is <c>"WARN "</c>, exactly as before;
/// Error and Critical get their own words rather than being folded into Warning, which the old
/// logger could not express at all.
/// </remarks>
internal sealed class FileLogger(string category, FileLogWriter writer) : ILogger
{
    /// <inheritdoc/>
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    /// <inheritdoc/>
    /// <remarks>
    /// **Everything at Information and above, and nothing below.** The viewer's log is read by
    /// people diagnosing a run, and `Debug`/`Trace` in a per-frame renderer is how a 37 MB log
    /// happens — which it did, once, at 450,157 lines. A caller that wants those can filter them in
    /// rather than out.
    /// </remarks>
    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

    /// <inheritdoc/>
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        ArgumentNullException.ThrowIfNull(formatter);

        string message = formatter(state, exception);

        if (exception is not null)
        {
            // **Type and message, not the stack.** `ViewerLog.Warn(area, what, failure)` wrote
            // exactly this, and the reason holds: these are exceptions that were CAUGHT and
            // handled, so what matters is which one and what happened instead. A stack per handled
            // fallback would bury the log it is meant to make readable.
            message = $"{message}: {exception.GetType().Name}: {exception.Message}";
        }

        writer.Write(string.Create(
            CultureInfo.InvariantCulture,
            $"{DateTime.Now:HH:mm:ss.fff} {Level(logLevel)}[{category}] {message}"));
    }

    private static string Level(LogLevel level) => level switch
    {
        LogLevel.Warning => "WARN ",
        LogLevel.Error => "ERROR",
        LogLevel.Critical => "CRIT ",
        _ => "     ",
    };
}
