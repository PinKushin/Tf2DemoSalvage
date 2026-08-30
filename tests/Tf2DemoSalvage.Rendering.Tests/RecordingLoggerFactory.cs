using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Microsoft.Extensions.Logging;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// Keeps every line a load writes, with the area that wrote it.
/// </summary>
/// <remarks>
/// **This exists because a log nobody asserts on is not an instrument** — and the omission it was
/// written to catch is the exact shape `docs/memory/a-null-object-default-hides-a-missed-wiring.md`
/// records. `PropModels.Load` takes <c>ILogger? props = null</c>; `MapAssets` never passed one; so
/// every warning the static-prop path produced went to a `NullLogger` and was discarded. Two of
/// those warnings name the model whose mesh will draw in the missing-material chequer, and the hunt
/// for B229 spent four hypotheses reading a log that could not contain the answer.
///
/// **The AREA is recorded, not just the text.** D83 splits the log by subsystem, and the defect was
/// precisely that one area produced nothing at all — which a flat list of strings cannot express,
/// because "no line said X" and "that area never spoke" look identical in it.
///
/// Thread-safe because <see cref="MapCache"/> loads under a <c>Lazy</c> that several parallel tests
/// may await, and the load itself writes from whichever thread got there first.
/// </remarks>
internal sealed class RecordingLoggerFactory : ILoggerFactory
{
    private readonly ConcurrentQueue<(string Area, LogLevel Level, string Message)> _lines = new();

    /// <summary>Every line written so far, oldest first.</summary>
    public IReadOnlyList<(string Area, LogLevel Level, string Message)> Lines => [.. _lines];

    /// <summary>Every line one area wrote.</summary>
    /// <param name="area">The subsystem name, as <c>CreateLogger</c> was given it.</param>
    public IReadOnlyList<string> From(string area) =>
    [
        .. _lines
            .Where(line => string.Equals(line.Area, area, StringComparison.Ordinal))
            .Select(line => line.Message),
    ];

    public ILogger CreateLogger(string categoryName) => new Recorder(categoryName, _lines);

    /// <summary>Not supported; this factory is its own sink.</summary>
    /// <param name="provider">Ignored.</param>
    /// <exception cref="NotSupportedException">Always.</exception>
    /// <remarks>
    /// **Throws rather than ignoring**, because a test that added a provider and got silence would
    /// be reading an empty log and calling it a measurement — which is the failure this whole file
    /// exists to make impossible.
    /// </remarks>
    public void AddProvider(ILoggerProvider provider) =>
        throw new NotSupportedException("RecordingLoggerFactory is its own sink.");

    public void Dispose()
    {
        // Nothing to release: the queue is managed and the factory holds no handles.
    }

    private sealed class Recorder(
        string area, ConcurrentQueue<(string Area, LogLevel Level, string Message)> lines)
        : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        // Debug included: the prop path's "mesh N carries material -1" line is at Debug, and a
        // recorder that filtered it out would reproduce the blindness this replaces.
        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            string message = formatter(state, exception);

            if (exception is not null)
            {
                message = string.Create(
                    CultureInfo.InvariantCulture, $"{message} :: {exception.Message}");
            }

            lines.Enqueue((area, logLevel, message));
        }
    }
}
