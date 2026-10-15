using System;
using System.Collections.Generic;

using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Render;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>A window with no controls: what a session read is settable, and what it wrote is kept in order.</summary>
internal sealed class FakeViewerWindow : IViewerWindow
{
    /// <summary>Every write, in order: <c>Status: text</c>, <c>Redraw</c>, <c>Close posted</c>, <c>ShowFullbright 1</c>.</summary>
    public List<string> Told { get; } = [];

    /// <summary>How many of the writes begin with a word.</summary>
    /// <param name="start">The word.</param>
    /// <returns>The count.</returns>
    public int Count(string start) => Told.FindAll(told => told.StartsWith(start, StringComparison.Ordinal)).Count;

    private string? _status = "No demo loaded.";

    /// <inheritdoc/>
    public string? Status
    {
        get => _status;
        set
        {
            _status = value;
            Told.Add("Status: " + value);
        }
    }

    /// <inheritdoc/>
    public (int Width, int Height) Viewport { get; set; } = (1280, 720);

    /// <inheritdoc/>
    public bool HasFocus { get; set; } = true;

    /// <inheritdoc/>
    public bool IsActiveApp { get; set; } = true;

    /// <inheritdoc/>
    public bool IsFullScreen { get; set; }

    /// <summary>What ticking the surface-colours item does: a real menu reports the change back to the session.</summary>
    public Action<bool>? SurfaceColoursTicked { get; set; }

    /// <inheritdoc/>
    public T OnUi<T>(Func<T> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        return work();
    }

    /// <inheritdoc/>
    public void ReportLoading(string stage, double? fraction) => Told.Add("ReportLoading " + stage);

    /// <inheritdoc/>
    public void HideLoading() => Told.Add(nameof(HideLoading));

    /// <inheritdoc/>
    public void Redraw() => Told.Add(nameof(Redraw));

    /// <inheritdoc/>
    public void Close(bool posted) => Told.Add(posted ? "Close posted" : "Close now");

    /// <inheritdoc/>
    public void LeaveFullScreen()
    {
        IsFullScreen = false;
        Told.Add(nameof(LeaveFullScreen));
    }

    /// <inheritdoc/>
    public void ShowSurfaceColours(bool ticked)
    {
        Told.Add($"ShowSurfaceColours {ticked}");
        SurfaceColoursTicked?.Invoke(ticked);
    }

    /// <inheritdoc/>
    public void ShowSpecular(bool ticked) => Told.Add($"ShowSpecular {ticked}");

    /// <inheritdoc/>
    public void ShowFullbright(int mode) => Told.Add($"ShowFullbright {mode}");

    /// <inheritdoc/>
    public void ShowFullScreenExclusive(bool exclusive) => Told.Add($"ShowFullScreenExclusive {exclusive}");

    /// <inheritdoc/>
    public void ShowTextureQuality(string quality) => Told.Add("ShowTextureQuality " + quality);

    /// <inheritdoc/>
    public void ShowPlaylist(int rows) => Told.Add($"ShowPlaylist {rows}");

    /// <inheritdoc/>
    public void ShowBindings(KeyBindings bindings) => Told.Add(nameof(ShowBindings));
}

/// <summary>A message queue with a script: what waits at each asking, and what the loop did to the thread.</summary>
internal sealed class FakeIdleHost : IIdleHost
{
    /// <summary>What <see cref="Waiting"/> answers, one per asking; null once it runs out would spin, so the last repeats.</summary>
    public Queue<uint?> Queue { get; } = new();

    /// <summary>Whether a message arrives within the traffic wait.</summary>
    public bool Arrives { get; set; }

    /// <summary>Every traffic wait asked for, in seconds.</summary>
    public List<double> Waits { get; } = [];

    /// <summary>Every sleep, in milliseconds; a yield is -1.</summary>
    public List<int> Sleeps { get; } = [];

    /// <summary>How many times the queue was looked at.</summary>
    public int Looks { get; private set; }

    /// <inheritdoc/>
    public bool ArrivesWithin(double seconds)
    {
        Waits.Add(seconds);

        return Arrives;
    }

    /// <inheritdoc/>
    public uint? Waiting()
    {
        Looks++;

        return Queue.Count > 1 ? Queue.Dequeue() : Queue.Peek();
    }

    /// <inheritdoc/>
    public void Sleep(int milliseconds) => Sleeps.Add(milliseconds);

    /// <inheritdoc/>
    public void Yield() => Sleeps.Add(-1);
}

/// <summary>A device that draws nothing and records what it was told.</summary>
internal sealed class FakeRenderDevice : IRenderDevice
{
    /// <inheritdoc/>
    public bool Wireframe { get; set; }

    /// <inheritdoc/>
    public bool Specular { get; set; } = true;

    /// <inheritdoc/>
    public bool Phong { get; set; } = true;

    /// <inheritdoc/>
    public Fullbright Fullbright { get; set; }

    /// <inheritdoc/>
    public bool DrawWorld { get; set; } = true;

    /// <inheritdoc/>
    public bool DrawEntities { get; set; } = true;

    /// <inheritdoc/>
    public DebugModes Debug { get; set; }

    /// <inheritdoc/>
    public bool PostProcessingSettled { get; set; } = true;

    /// <inheritdoc/>
    public string PostProcessingReport { get; set; } = "bloom 1.00 of 0.50";

    /// <summary>Every capture asked for.</summary>
    public List<string> Captured { get; } = [];

    /// <summary>Every size the buffers were given.</summary>
    public List<(int Width, int Height)> Sizes { get; } = [];

    /// <summary>Every mode asked for.</summary>
    public List<bool> Exclusive { get; } = [];

    /// <summary>Whether a mode change is granted.</summary>
    public bool GrantsExclusive { get; set; } = true;

    /// <summary>How many times the world was thrown away.</summary>
    public int Cleared { get; private set; }

    /// <inheritdoc/>
    public void CaptureNextFrame(string path) => Captured.Add(path);

    /// <inheritdoc/>
    public void ClearWorld() => Cleared++;

    /// <inheritdoc/>
    public bool SetExclusiveFullScreen(bool enabled)
    {
        Exclusive.Add(enabled);

        return GrantsExclusive;
    }

    /// <inheritdoc/>
    public void Resize(int width, int height) => Sizes.Add((width, height));
}

/// <summary>A logger factory that keeps every line with its category, as the log file prints them.</summary>
internal sealed class RecordingLoggers : ILoggerFactory
{
    private readonly List<string> _lines = [];

    /// <summary>Every line: <c>[category] message</c>.</summary>
    public IReadOnlyList<string> Lines
    {
        get
        {
            lock (_lines)
            {
                return [.. _lines];
            }
        }
    }

    /// <summary>How many lines contain a fragment.</summary>
    /// <param name="fragment">The fragment.</param>
    /// <returns>The count.</returns>
    public int Count(string fragment) => new List<string>(Lines).FindAll(line => line.Contains(fragment, StringComparison.Ordinal)).Count;

    /// <inheritdoc/>
    public void AddProvider(ILoggerProvider provider)
    {
        // Nothing to add to: every logger made here writes to the one list.
    }

    /// <inheritdoc/>
    public ILogger CreateLogger(string categoryName) => new Category(this, categoryName);

    /// <inheritdoc/>
    public void Dispose()
    {
        // Holds nothing to release.
    }

    private sealed class Category(RecordingLoggers owner, string name) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (owner._lines)
            {
                owner._lines.Add($"[{name}] {formatter(state, exception)}");
            }
        }
    }
}

/// <summary>A clock that always reads the same, however often it is restarted.</summary>
internal sealed class FixedTime(double seconds) : IElapsedTime
{
    /// <inheritdoc/>
    public double Seconds { get; set; } = seconds;

    /// <inheritdoc/>
    public void Restart()
    {
        // A fixed reading has nothing to restart.
    }

    /// <inheritdoc/>
    public void Reset()
    {
        // A fixed reading has nothing to reset.
    }
}
