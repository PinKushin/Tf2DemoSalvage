using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
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
/// The walk and every window are built on workers; the comparison runs on the UI thread, in the frame after the window is
/// ready, because the whole timeline's readers are the UI thread's.
/// </remarks>
public sealed class WindowCheck
{
    private readonly ILogger _log;

    private readonly WindowSettings _settings;

    private readonly Task<(WindowCache? Cache, long Bytes, TimeSpan Took)> _walk;

    private (Task<DemoTimeline> Window, double Tick, long Began)? _pending;

    private int _seenSkips = -1;

    private WindowCheck(Task<(WindowCache?, long, TimeSpan)> walk, WindowSettings settings, ILogger log)
    {
        _walk = walk;
        _settings = settings;
        _log = log;
    }

    /// <summary>Starts walking a demo into checkpoints on a worker.</summary>
    /// <param name="path">The demo file.</param>
    /// <param name="settings">The settings windows are walked under.</param>
    /// <param name="log">Where the check reports.</param>
    /// <returns>The check, walking.</returns>
    public static WindowCheck Start(string path, WindowSettings settings, ILogger log)
    {
        ArgumentNullException.ThrowIfNull(log);

        log.LogInformation("{Message}", $"windows: walking {Path.GetFileName(path)} into checkpoints beside the whole timeline");

        return new WindowCheck(
            Task.Run(() =>
            {
                // The memory the checkpoints keep is the instrument's whole point, and only a full collection on both sides
                // measures what is LIVE rather than what has been allocated; this runs once per demo, on a worker.
#pragma warning disable S1215
                long before = GC.GetTotalMemory(forceFullCollection: true);
                long began = Stopwatch.GetTimestamp();
                WindowCache? cache = WindowCache.Walk(File.ReadAllBytes(path), settings);
                TimeSpan took = Stopwatch.GetElapsedTime(began);

                return (cache, GC.GetTotalMemory(forceFullCollection: true) - before, took);
#pragma warning restore S1215
            }),
            settings,
            log);
    }

    /// <summary>Called each shown moment: starts a window for a new seek, and checks one that has finished.</summary>
    /// <param name="skip">The frame presenter's last move.</param>
    /// <param name="whole">The whole timeline on screen.</param>
    public void Observe(FrameSkip? skip, DemoTimeline whole)
    {
        ArgumentNullException.ThrowIfNull(whole);

        if (!_walk.IsCompleted)
        {
            return;
        }

        (WindowCache? cache, long bytes, TimeSpan took) = _walk.Result;

        if (cache is null)
        {
            return;
        }

        if (_seenSkips < 0)
        {
            _log.LogInformation(
                "{Message}",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"windows: {cache.Count} checkpoints every {WindowCache.SpacingTicks(ScenePropTrack.Tf2TickInterval)} ticks in {took.TotalSeconds:0.0} s, keeping {bytes / 1048576d:0} MB"));
            _seenSkips = skip?.Skips ?? 0;
        }

        if (_pending is { } waiting && waiting.Window.IsCompleted)
        {
            _pending = null;
            Report(waiting.Window.Result, whole, waiting.Tick, Stopwatch.GetElapsedTime(waiting.Began));
        }

        // A skip since the last look, as every reader of the move asks (FrameSkip): the count, not the last move.
        if (skip is { } jumped && jumped.Skips != _seenSkips)
        {
            _seenSkips = jumped.Skips;
            _pending = (cache.At(jumped.Tick), jumped.Tick, Stopwatch.GetTimestamp());
        }
    }

    private void Report(DemoTimeline window, DemoTimeline whole, double tick, TimeSpan ready)
    {
        (System.Collections.Generic.IReadOnlyList<string> differing, long values) = WindowEquivalence.Compare(whole, window, tick, _settings);

        string verdict = differing.Count == 0 ? "IDENTICAL" : "DIFFERENT in " + string.Join(", ", differing);

        _log.LogInformation(
            "{Message}",
            string.Create(
                CultureInfo.InvariantCulture,
                $"windows: seek to {tick} ready in {ready.TotalMilliseconds:0} ms; {values} values compared, {verdict}"));
    }
}
