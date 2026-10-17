using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Presentation;

/// <summary>
/// `--windows`: the window cache running beside the whole timeline, checked against it on every seek (D217 step 4,
/// piece 1). It changes nothing drawn; it logs what a seek through windows costs and whether it answers the same.
/// </summary>
/// <remarks>
/// **Beside, not instead, until it is proven** — the owner's goal is no whole timeline at all, which is pieces 2 to 4.
/// The walk and every window are built on workers; the comparison runs on the UI thread, in a frame after the window is
/// ready, because the whole timeline's readers are the UI thread's.
///
/// **A failure parks the check, said once**: a walk or a window that faulted is logged with its exception and the check
/// stops, rather than throwing out of every frame. A seek made while the walk is still running is checked once it ends.
/// </remarks>
public sealed class WindowCheck : IDisposable
{
    private readonly ILogger _log;

    private readonly WindowSettings _settings;

    private readonly CancellationTokenSource _closing = new();

    private readonly Task<(WindowCache? Cache, long KeptBytes, TimeSpan Took)> _walk;

    private (Task<DemoTimeline> Window, double Tick, long Began)? _pending;

    /// <summary>The skip count last acted on, or null before the walk has finished.</summary>
    private int? _seenSkips;

    /// <summary>The last seek made while the walk ran, to check once it ends.</summary>
    private double? _seekDuringWalk;

    private bool _parked;

    private bool _ready;

    /// <summary>A check over any walk: the real one in production, any for a test.</summary>
    /// <param name="walk">Walks the demo, honouring the token between windows; answers the cache and what it keeps.</param>
    /// <param name="settings">The settings windows are walked under.</param>
    /// <param name="log">Where the check reports.</param>
    internal WindowCheck(Func<CancellationToken, (WindowCache?, long, TimeSpan)> walk, WindowSettings settings, ILogger log)
    {
        _settings = settings;
        _log = log;
        CancellationToken closing = _closing.Token;
        _walk = Task.Run(() => walk(closing), closing);
    }

    /// <summary>The walk, for a test to wait on.</summary>
    internal Task Walking => _walk;

    /// <summary>Starts the check for a decoded demo, from the bytes and the interval the load already has.</summary>
    /// <param name="decoded">The demo, read with its bytes kept.</param>
    /// <param name="settings">The settings windows are walked under.</param>
    /// <param name="log">Where the check reports.</param>
    /// <returns>The check, or null when the demo has no timeline or its bytes were not kept.</returns>
    public static WindowCheck? For(Scene.DecodedDemo decoded, WindowSettings settings, ILogger log)
    {
        ArgumentNullException.ThrowIfNull(decoded);

        if (decoded.Timeline is not { } whole || decoded.Bytes.IsEmpty)
        {
            return null;
        }

        // The demo's own interval, from its server info; the engine's default only when it never said.
        double interval = whole.IntervalPerTick > 0f ? whole.IntervalPerTick : PlaybackClock.DefaultIntervalPerTick;

        return Start(decoded.Bytes, interval, System.IO.Path.GetFileName(decoded.Demo.Path), settings, log);
    }

    /// <summary>Starts walking a demo into checkpoints on a worker.</summary>
    /// <param name="file">The demo's bytes, as the load already read them.</param>
    /// <param name="secondsPerTick">The demo's own tick interval.</param>
    /// <param name="name">The demo, for the log.</param>
    /// <param name="settings">The settings windows are walked under.</param>
    /// <param name="log">Where the check reports.</param>
    /// <returns>The check, walking.</returns>
    public static WindowCheck Start(ReadOnlyMemory<byte> file, double secondsPerTick, string name, WindowSettings settings, ILogger log)
    {
        ArgumentNullException.ThrowIfNull(log);

        log.LogInformation("{Message}", $"windows: walking {name} into checkpoints beside the whole timeline");

        return new WindowCheck(
            _ =>
            {
                long began = Stopwatch.GetTimestamp();
                WindowCache? cache = WindowCache.Walk(file, secondsPerTick, settings, out TimelineWindows? walk);
                TimeSpan took = Stopwatch.GetElapsedTime(began);

                // What the checkpoints keep, carried from the instrument that counts them (B243), not a GC delta.
                return (cache, walk?.KeptBytes() ?? 0, took);
            },
            settings,
            log);
    }

    /// <summary>Called each drawn frame: starts a window for a new seek, and checks one that has finished.</summary>
    /// <param name="skip">The frame presenter's last move.</param>
    /// <param name="whole">The whole timeline on screen.</param>
    public void Observe(FrameSkip? skip, DemoTimeline whole)
    {
        ArgumentNullException.ThrowIfNull(whole);

        if (_parked)
        {
            return;
        }

        if (!_walk.IsCompleted)
        {
            // Remembered, not dropped: the walk's end checks the last seek made meanwhile.
            if (skip is { } during && during.Skips != (_seenSkips ?? -1))
            {
                _seenSkips = during.Skips;
                _seekDuringWalk = during.Tick;
            }

            return;
        }

        if (!_walk.IsCompletedSuccessfully)
        {
            Park("the walk", _walk.Exception);
            return;
        }

        (WindowCache? cache, long kept, TimeSpan took) = _walk.Result;

        if (cache is null)
        {
            Park("the walk found no data tables", failure: null);
            return;
        }

        if (!_ready)
        {
            _ready = true;
            _log.LogInformation(
                "{Message}",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"windows: {cache.Count} checkpoints in {took.TotalSeconds:0.0} s, keeping {kept / 1048576d:0} MB"));

            if (_seekDuringWalk is { } waited)
            {
                _log.LogInformation("{Message}", string.Create(CultureInfo.InvariantCulture, $"windows: checking the seek to {waited} made during the walk"));
                _pending = (cache.At(waited), waited, Stopwatch.GetTimestamp());
            }

            _seenSkips = skip?.Skips ?? 0;
        }

        if (_pending is { } waiting && waiting.Window.IsCompleted)
        {
            _pending = null;

            if (!waiting.Window.IsCompletedSuccessfully)
            {
                Park($"the window for {waiting.Tick.ToString(CultureInfo.InvariantCulture)}", waiting.Window.Exception);
                return;
            }

            // The window's answers on a worker — the window is this check's alone — and the whole timeline's one question
            // per frame below, on this thread, which owns it: one comparison of a real match was a second on the UI thread.
            DemoTimeline window = waiting.Window.Result;
            double tick = waiting.Tick;
            _comparing = new Comparing(
                Task.Run(() => WindowEquivalence.Start(whole, window, tick, _settings), _closing.Token), tick, Stopwatch.GetElapsedTime(waiting.Began));
        }

        StepComparison();

        // A skip since the last look, as every reader of the move asks (FrameSkip): the count, not the last move.
        if (skip is { } jumped && jumped.Skips != _seenSkips)
        {
            _seenSkips = jumped.Skips;
            _comparing = null;
            _pending = (cache.At(jumped.Tick), jumped.Tick, Stopwatch.GetTimestamp());
        }
    }

    /// <summary>A comparison being made a question per frame, and what it has cost the frames so far.</summary>
    private sealed class Comparing(Task<WindowEquivalence> prepared, double tick, TimeSpan ready)
    {
        public Task<WindowEquivalence> Prepared { get; } = prepared;

        public double Tick { get; } = tick;

        public TimeSpan Ready { get; } = ready;

        public TimeSpan Longest { get; set; }

        public TimeSpan Total { get; set; }
    }

    private Comparing? _comparing;

    private void StepComparison()
    {
        if (_comparing is not { Prepared.IsCompleted: true } comparing)
        {
            return;
        }

        if (!comparing.Prepared.IsCompletedSuccessfully)
        {
            _comparing = null;
            Park($"the comparison for {comparing.Tick.ToString(CultureInfo.InvariantCulture)}", comparing.Prepared.Exception);
            return;
        }

        long began = Stopwatch.GetTimestamp();
        WindowEquivalence equivalence = comparing.Prepared.Result;
        bool done = equivalence.Step();
        TimeSpan step = Stopwatch.GetElapsedTime(began);
        comparing.Total += step;
        comparing.Longest = step > comparing.Longest ? step : comparing.Longest;

        if (done)
        {
            _comparing = null;
            Report(equivalence, comparing);
        }
    }

    /// <inheritdoc/>
    /// <remarks>On close or a new demo: the walk is told to stop and every window still building is cancelled.</remarks>
    public void Dispose()
    {
        _closing.Cancel();

        if (_walk.IsCompletedSuccessfully)
        {
            _walk.Result.Cache?.Dispose();
        }

        _closing.Dispose();
    }

    private void Park(string what, Exception? failure)
    {
        _parked = true;
        _log.LogWarning(failure, "{Message}", $"windows: {what} failed; the check is off for this demo");
    }

    private void Report(WindowEquivalence equivalence, Comparing comparing)
    {
        IReadOnlyList<string> differing = equivalence.Differing;
        string verdict = differing.Count == 0 ? "IDENTICAL" : "DIFFERENT in " + string.Join(", ", differing);

        _log.LogInformation(
            "{Message}",
            string.Create(
                CultureInfo.InvariantCulture,
                $"windows: seek to {comparing.Tick} ready in {comparing.Ready.TotalMilliseconds:0} ms; compared {equivalence.Values} values, " +
                $"{comparing.Total.TotalMilliseconds:0} ms of UI frames, the longest frame's share {comparing.Longest.TotalMilliseconds:0} ms, {verdict}"));
    }
}
