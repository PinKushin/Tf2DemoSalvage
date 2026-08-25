using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Extensions.Logging;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// A logger that keeps what it was told, so a diagnostic can be asserted rather than assumed.
/// </summary>
/// <remarks>
/// **Written because a log line disappeared and nothing noticed.** The bone-merge report lived
/// inside <c>EntityModelSet.Merge</c>; D88 deleted the method and took the line with it, and the
/// next viewer run could not say whether weapons had paired with the players holding them — the
/// diagnostic for the thing that broke was removed by the change that broke it.
///
/// **A log this project relies on is behaviour, not decoration.** The rules here say so from
/// several directions: a failure-only log reads clean while everything falls back; a log must name
/// what it measured; log what you will need before you need it. All of that is untestable while
/// nothing records what was written.
///
/// **What the assertions are actually about is FREQUENCY**, which is why the recorder keeps every
/// line rather than a set. Each of these lines is deduped on something different — once per model,
/// once per entity, on a change of more than a unit, at most one a second — and getting that wrong
/// has cost this project twice: a per-frame line that printed 1,280 times a second (B163), and a
/// once-per-model line that let a bright control point silence a dark one for ever.
/// </remarks>
public sealed class RecordingLogger : ILogger
{
    private readonly List<(LogLevel Level, string Message)> _lines = [];

    /// <summary>Everything written, in order.</summary>
    public IReadOnlyList<(LogLevel Level, string Message)> Lines => _lines;

    /// <summary>Every message written, in order.</summary>
    public IEnumerable<string> Messages => _lines.Select(line => line.Message);

    /// <summary>How many lines contain a fragment.</summary>
    /// <param name="fragment">What to look for.</param>
    /// <returns>The count.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="fragment"/> is null.</exception>
    public int Count(string fragment)
    {
        ArgumentNullException.ThrowIfNull(fragment);

        return _lines.Count(line => line.Message.Contains(fragment, StringComparison.Ordinal));
    }

    /// <summary>Forgets everything, for a test measuring a second phase separately.</summary>
    public void Clear() => _lines.Clear();

    /// <inheritdoc/>
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    /// <inheritdoc/>
    /// <remarks>
    /// **Always enabled, deliberately.** A recorder that respected a level would make a test's
    /// result depend on configuration rather than on the code under test — and the question here is
    /// always whether the line was WRITTEN, not whether a sink would have kept it.
    /// </remarks>
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <inheritdoc/>
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);

        _lines.Add((logLevel, formatter(state, exception)));
    }
}

/// <summary>Hands the same recorder to every category, so one assertion sees them all.</summary>
/// <remarks>
/// The scene layer writes to two categories on purpose — <c>props</c> and <c>render</c> (D83) —
/// and a test asserting that a line was written does not usually care which. One that does can read
/// <see cref="RecordingLogger.Lines"/> and check the text.
/// </remarks>
public sealed class RecordingLoggerFactory : ILoggerFactory
{
    /// <summary>What every category writes to.</summary>
    public RecordingLogger Recorder { get; } = new();

    /// <inheritdoc/>
    public ILogger CreateLogger(string categoryName) => Recorder;

    /// <inheritdoc/>
    public void AddProvider(ILoggerProvider provider)
    {
        // Nothing to add to: this factory IS the sink.
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        // Nothing held.
    }
}
