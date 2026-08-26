using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Extensions.Logging;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>
/// A logger that keeps what it was told, so a diagnostic can be asserted rather than assumed.
/// </summary>
/// <remarks>
/// **A log this project relies on is behaviour, not decoration**, and several of its rules are about
/// exactly that: a failure-only log reads clean while everything falls back; a log must name what it
/// measured; log what you will need before you need it. None of that is testable while nothing
/// records what was written.
///
/// **This is the third copy — Scene.Tests and Corpus.Tests have their own — and that is a deliberate
/// trade rather than an oversight.** A test project referencing another test project pulls its
/// fixtures into this assembly's discovery, which breaks the exact per-project counts
/// `build/assert-test-count.sh` depends on.
///
/// **The clean fix is a `TestSupport` project holding helpers and no `[Test]` methods**, which would
/// not affect discovery and would end the triplication. It has not been done because the copies are
/// small and stable, and because adding a project mid-refactor is a change with its own audit. Worth
/// doing when a fourth copy is wanted.
/// </remarks>
public sealed class RecordingLogger : ILogger
{
    private readonly List<(LogLevel Level, string Message)> _lines = [];

    /// <summary>Everything written, in order.</summary>
    public IReadOnlyList<(LogLevel Level, string Message)> Lines => _lines;

    /// <summary>How many lines contain a fragment.</summary>
    /// <param name="fragment">What to look for.</param>
    /// <returns>The count.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="fragment"/> is null.</exception>
    public int Count(string fragment)
    {
        ArgumentNullException.ThrowIfNull(fragment);

        return _lines.Count(line => line.Message.Contains(fragment, StringComparison.Ordinal));
    }

    /// <inheritdoc/>
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    /// <inheritdoc/>
    /// <remarks>
    /// **Always enabled, deliberately.** A recorder that respected a level would make a test's
    /// result depend on configuration rather than on the code under test — and the question is
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
