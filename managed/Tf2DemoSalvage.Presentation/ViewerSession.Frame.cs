using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Logging;
using Tf2DemoSalvage.Render;

namespace Tf2DemoSalvage.Presentation;

public sealed partial class ViewerSession
{
    /// <summary>Times a full screen transition from the keystroke to the first frame drawn.</summary>
    /// <remarks>
    /// **The number a user actually feels**, and the one that would have caught this project's
    /// worst performance defect on its own. Full screen took roughly a frame a second because
    /// entering it rebuilt the world seventeen times; every individual step looked fine and only
    /// the end-to-end time was absurd. Timing the parts would not have found it either — the cost
    /// was in how MANY times a fast thing ran.
    ///
    /// Stopped at the first PRESENTED frame rather than when the window finishes resizing, because
    /// a window that has changed size while still showing the old picture is not yet full screen
    /// as far as anyone looking at it is concerned.
    /// </remarks>
    private Stopwatch? _fullScreenClock;

    /// <summary>Demo time the last drawn frame covered: zero while paused.</summary>
    private double _demoFrameSeconds;

    /// <summary>Draws the whole world at a moment: players and every model-bearing entity.</summary>
    /// <param name="tick">The moment to show, which may fall between ticks.</param>
    /// <remarks>
    /// **All that is left here is what the window knows**, gathered into a <see cref="MomentView"/>
    /// and handed on: the camera mode, the transport's tick, the followed entity, the eye that needs
    /// this viewport's aspect ratio, and a setting. The sampling, the timing, the scene build and the
    /// stall report all left on 2026-08-26 (B188, D90).
    ///
    /// Takes a fractional tick rather than a whole one so the interpolation actually reaches the
    /// picture. Truncating here would leave every pose snapped to the last packet and make the
    /// whole interpolation layer a no-op that still passed its own tests.
    /// </remarks>
    public void ShowMoment(double tick)
    {
        // Whether there is a timeline, and what a seek rebuilds on the way, are the presenter's (D214); it comes back
        // here through `ShowOnly` to show each tick it needs.
        _presenter.ShowMoment(tick);
    }

    /// <summary>Builds the scene at a moment, with no replay before it.</summary>
    private void ShowOnly(double tick)
    {
        _shownTick = tick;

        _presenter.Show(tick, _transport.Playing);
    }

    /// <summary>What this window knows of the frame it is drawing; the presenter adds the demo, the map and the settings.</summary>
    private WindowFrame WindowFrameNow =>
        new(
            _transport.CurrentTick,
            _shownTick,
            _transport.Playing,
            _window.Viewport.Width,
            _window.Viewport.Height,
            (float)_clock.LastFrameSeconds,
            _demoFrameSeconds);

    /// <summary>What this window knows that a camera needs: the transport's tick, the tick last shown, its shape.</summary>
    private CameraWindow CameraWindowNow =>
        new(
            _transport.CurrentTick,
            _shownTick,
            _window.Viewport.Width,
            _window.Viewport.Height,
            _window.IsFullScreen && _settingsOwner.FullScreenExclusive);

    /// <summary>The colour behind everything: a dark blue, and deliberately not black.</summary>
    /// <remarks>
    /// **A diagnostic choice more than a cosmetic one.** This started at 0.06/0.07/0.09, which is
    /// near enough to black that a surface drawn black and a surface not drawn at all look
    /// identical — so a hole in the map reads as background and nobody investigates it. The owner
    /// found a black box on cp_process's last points only after suspecting the background was
    /// hiding it, and could not tell whether it was geometry or a gap.
    ///
    /// It also still does the job the near-black one was written for: a viewport that stays the
    /// form's grey looks the same whether the device failed or simply drew nothing, so the clear
    /// colour is the evidence that the swap chain is bound to this panel and presenting.
    ///
    /// Blue rather than any other hue because nothing in a TF2 map is this colour: the team blues
    /// are far brighter, and the world is browns, greys and greens.
    /// </remarks>
    private const float BackgroundRed = 0.07f;

    /// <inheritdoc cref="BackgroundRed"/>
    private const float BackgroundGreen = 0.10f;

    /// <inheritdoc cref="BackgroundRed"/>
    private const float BackgroundBlue = 0.20f;

    /// <summary>The frame clocks: when a frame may begin, and how long the last one took.</summary>
    /// <remarks>
    /// **Was `_flyWatch` and `_lastFrameAt`, two fields whose docs never mentioned each other**
    /// (B188, D90). They are still two clocks — Valve keeps at least four and names each by what it
    /// obeys, and the demo free camera flies by `absoluteframetime` while a limiter cannot pace
    /// itself by the duration of the frame it is deciding to allow. `FrameClock` states the
    /// relationship; `FrameTimingConformanceTests` carries the citations.
    /// </remarks>
    private readonly FrameClock _clock;

    /// <summary>The frame-rate log's meter, independent of whether the on-screen one is shown.</summary>
    private readonly FpsMeter _loggedRate = new() { Mode = FpsMeter.Smoothed };

    /// <summary>Seconds since the window opened, for VGUI's 250 ms ticks.</summary>
    private readonly Stopwatch _vguiClock = Stopwatch.StartNew();

    /// <summary>Sends the current view to the device, without rebuilding anything.</summary>
    /// <remarks>
    /// **Sixty-four bytes, where telling the device the camera moved used to cost a projection of
    /// the whole map** (B98). The `SetCamera` call lived inside <see cref="ProjectMap"/>, so the
    /// only way to update the view was to re-project every map segment and every surface triangle
    /// into screen space for the top-down overlay — which the free view does not draw.
    ///
    /// Affordable while the camera moved once per keystroke; the frame budget once it flew. The
    /// tell was that flight stayed smooth while the demo was paused and stuttered while it played,
    /// because playback's per-frame scene rebuild was competing for the same milliseconds.
    ///
    /// **Safe to use instead of a full rebuild in the FREE view only**, and each half of that was
    /// checked rather than assumed. The world's vertices are in map coordinates and only the view
    /// changes (D35). The 3D models are world-space too, placed by their own matrices. And the
    /// screen-space scene points are a map-view fallback drawn only for players with no model, so
    /// they are empty in any modern demo and are projected through the top-down camera anyway. The
    /// map view still rebuilds, because there everything IS projected to screen space.
    /// </remarks>
    private void UploadCamera(long flyTicks = 0)
    {
        // The view's setup beside the camera, in the order it ran here, is the presenter's (`ViewerPresenter.UploadCamera`).
        // The exposure's time is this window's to say: it walks by game frame time (B514), and
        // **a pending capture is a paused frame, and gets a tick of time anyway** (B520): TF2 arrives at a tick by playing
        // to it, so its exposure has had seconds; a still that held the reset value of 1 was 1/0.70 too bright outdoors.
        _presenter.UploadCamera(
            flyTicks,
            _launch.ShotPath is not null && !_opening.Finished
                ? CaptureExposureSeconds
                : (float)_demoFrameSeconds,
            _surfaceColours);
    }

    /// <summary>The moment <see cref="ShowMoment"/> last showed, fraction included — what the players are posed at.</summary>
    private double _shownTick;

    /// <summary>This frame's VGUI: the client's HUD, then the tools panel and `CFPSPanel` on it above.</summary>
    /// <returns>The draw list.</returns>
    /// <remarks>What is built, and in which order, is the presenter's (D214); this is what a window knows of it.</remarks>
    public FrameOverlay BuildOverlay() => _presenter.BuildOverlay();

    /// <summary>Writes the frame rate to the log once a second.</summary>
    private readonly FrameRateLog _frameRateLog = new();

    /// <summary>
    /// Seconds of drawn frames, accumulated from the clock rather than from a wall clock.
    /// </summary>
    /// <remarks>
    /// Summing the frame durations rather than reading a stopwatch keeps the interval measured in
    /// the same quantity the reading describes: a run paused at a breakpoint, or one whose window is
    /// hidden and drawing nothing, does not silently accrue an interval it never rendered.
    /// </remarks>
    private double _frameSeconds;

    /// <summary>Every per-second line this run produced, for <c>--measure</c> to print.</summary>
    private readonly MeasureLog _measured = new();

    /// <summary>Whether the previous frame had a loaded demo on screen — the first frame after a load is not counted.</summary>
    private bool _wasOnScreen;

    /// <summary>The collector's counters when counting began, for the measurement's GC line (finding 82).</summary>
    private GcReading _gcAtStart;

    /// <summary>The JIT's counters when counting began, for the measurement's JIT line (finding 84).</summary>
    private JitReading _jitAtStart;

    /// <summary>The particle phase's counters when counting began (finding 82).</summary>
    private ParticleCost _particlesAtStart;

    /// <summary>The particle phase's work when counting began (finding 84).</summary>
    private ParticleWork _particleWorkAtStart;

    /// <summary>Particles born by system when counting began (finding 82, B557).</summary>
    private Dictionary<string, long> _birthsAtStart = new(StringComparer.Ordinal);

    /// <summary>Prints what <c>--measure</c> gathered and closes.</summary>
    /// <remarks>
    /// **To stdout, because the log is buffered.** Reading the log file while the viewer is still
    /// running shows only what has been flushed — 52 lines of asset loading, and nothing about
    /// frames — which was misread twice in one session, once as the viewer having exited on its own.
    /// A measurement that has to be read out of a file nobody can read yet is not a measurement.
    ///
    /// **The first line is dropped.** It covers the second in which the map finished loading and
    /// playback began, so it averages a handful of frames against a rebuild that included model
    /// uploads; keeping it drags the mean toward a cost no steady frame pays.
    /// </remarks>
    private void FinishMeasuring()
    {
        // Stryker disable once all : a mutated interpolated string cannot bind to its handler (CS1620) and Safe Mode drops the method - B410
        PrintMeasured(
            _seekedForMeasure
                ? string.Create(CultureInfo.InvariantCulture, $"measured {_frameSeconds:0.#} seconds paused at {_launch.ThenSeek} after the seek, {_measured.Samples} samples ({_measured.Rebuilds} rebuild reports)")
                : MeasuredPlaybackHeading());

        _heartbeat?.Dispose();
        _heartbeat = null;

        _window.Close(posted: false);
    }

    /// <summary>A measurement's stalls and heartbeat gaps (finding 88); made when its first frame is counted.</summary>
    private PauseLedger? _pauses;

    /// <summary>When the last frame's sequence ended, in Stopwatch ticks; zero before the first.</summary>
    private long _lastSequenceEnded;

    /// <summary>The running counters at the last frame boundary, for the ledger's slow-frame detail (B595).</summary>
    private FrameCounters _lastCounters;

    /// <summary>The same at the measurement's start.</summary>
    private FrameCounters _countersAtStart;

    /// <summary>Every thread's log writes at the measurement's start (B595).</summary>
    private Logging.LogWriteTiming _logAtStart;

    /// <summary>Each main-thread archive read since the measurement began: path, ms and the first callers outside the archive (B595 part 6).</summary>
    private readonly ReadsInPlay _readsInPlay = new();

    /// <summary>Whether <see cref="_lastCounters"/> holds a boundary of this measurement.</summary>
    private bool _counted;

    /// <summary>Every running counter a slow frame is differenced over, read now on this — the main — thread.</summary>
    private FrameCounters ReadCounters() => new(
        System.Runtime.JitInfo.GetCompiledMethodCount(currentThread: true),
        System.Runtime.JitInfo.GetCompilationTime(currentThread: true),
        System.Runtime.JitInfo.GetCompilationTime(),
        GC.GetTotalPauseDuration(),
        _presenter.CameraSpent,
        _presenter.SoundsHeld.Misses,
        Tf2DemoSalvage.Content.Assets.ParticleEffect.SystemsBorn,
        new ModelFirstUses(
            _presenter.Models.ModelsPacked,
            _presenter.Models.PackTicks,
            _presenter.Models.BakedBuilds,
            _presenter.Models.BakedBuildTicks,
            _presenter.Map?.Lighting.StaticStateBuilds ?? 0,
            _presenter.Map?.Lighting.StaticStateBuildTicks ?? 0),
        Logging.FileLogWriter.ThreadTiming,
        Tf2DemoSalvage.Content.Assets.GameArchives.ThreadReads,
        Tf2DemoSalvage.Content.Assets.ArchiveReadTiming.ProcessPageFaults());

    /// <summary>A slow frame's whole phase split and its counters' deltas, built only for a slow frame.</summary>
    // Stryker disable once all : a mutated interpolated string cannot bind to its handler (CS1620) and Safe Mode drops the method - B410
    private string SlowFrameDetail(in FramePhases phases, long between, in FrameCounters counters)
    {
        double ms(long ticks) => ticks * 1000d / Stopwatch.Frequency;

        // The weapon-script precache, when it ended inside this frame (B595 part 6).
        (long precacheTicks, long precachedAt) = _effects.WeaponScriptPrecache;
        string precache = precachedAt > _lastSequenceEnded
            ? string.Create(CultureInfo.InvariantCulture, $", weapon precache {ms(precacheTicks):0.0}")
            : string.Empty;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"between {ms(between):0.0}, sound {ms(phases.Sound):0.0}, camera {ms(phases.Camera):0.0}, project {ms(phases.Project):0.0}" +
            $", advance {ms(phases.Advance):0.0}, hud {ms(phases.Hud):0.0}, draw {ms(phases.Draw):0.0}" +
            $", unaccounted {ms(phases.Unaccounted):0.0}{precache}; {counters.Since(_lastCounters, Stopwatch.Frequency)}");
    }

    /// <summary>When <see cref="_pauses"/> began, in Stopwatch ticks.</summary>
    private long _pausesFrom;

    /// <summary>The thread beating into <see cref="_pauses"/>, stopped when the measurement prints.</summary>
    private Heartbeat? _heartbeat;

    /// <summary>
    /// The heading <c>PlaybackUiTests</c> parses: seconds played, rate reports, rebuild reports, and the tick
    /// the demo was showing when measuring ended.
    /// </summary>
    /// <remarks>
    /// **The tick is the proof the demo played**, carried from <see cref="_shownTick"/>, the value the frame drew. The
    /// rebuild count cannot be: its line prints once per hundred rebuilds, and a backgrounded viewer (always, in the gate)
    /// can finish twenty seconds without reaching a hundred (2026-10-01, the cut ESEA demo).
    /// </remarks>
    // Stryker disable once all : a mutated interpolated string cannot bind to its handler (CS1620) and Safe Mode drops the method - B410
    private string MeasuredPlaybackHeading() =>
        string.Create(CultureInfo.InvariantCulture, $"measured {_frameSeconds:0.#} seconds of playback, {_measured.Samples} samples ({_measured.Rebuilds} rebuild reports), ending at tick {_shownTick:0}");

    /// <summary>Frames counted into <see cref="_frameSeconds"/> since the last printed measurement.</summary>
    private int _framesMeasured;

    /// <summary>The runtime's large-allocation ticks over a measurement; made when the first measured frame is counted.</summary>
    private AllocationTickListener? _allocations;

    /// <summary>The measured frames' phases, summed, for a paused measurement that writes no per-second line.</summary>
    private FramePhases _phasesMeasured;

    /// <summary>The render thread's drawing time over the measured frames, in stopwatch ticks (B581).</summary>
    private long _renderMeasured;

    /// <summary>Whether <c>--then-seek</c> has already paused and seeked, so the second measurement is running.</summary>
    private bool _seekedForMeasure;

    /// <summary>Prints one measurement's heading and its per-second lines, the first dropped.</summary>
    private void PrintMeasured(string heading)
    {
        // The frame count beside the seconds: a paused frame writes no per-second line (the rate meter samples only
        // while the scene rebuilds), so for B420's paused measurement this mean IS the measurement.
        // Stryker disable once all : a mutated interpolated string cannot bind to its handler (CS1620) and Safe Mode drops the method - B410
        _host.Out.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{heading}; {_framesMeasured} frames, mean {(_framesMeasured > 0 ? _frameSeconds * 1000d / _framesMeasured : 0d):0.00} ms"));

        if (_framesMeasured > 0)
        {
            // The same phases the per-second line names, as means over every measured frame. `between` is the mean frame
            // the clock used less the mean of `FrameSequence`'s own total: everything outside the sequence — the idle
            // loop, the message pump, this method's own reporting (finding 82). Valid as a mean only, since the clock
            // is stamped mid-sequence.
            double each(double total) => total * 1000d / Stopwatch.Frequency / _framesMeasured;
            FramePhases sum = _phasesMeasured;
            (int trails, int bursts, int quads) = _effects.ParticleCounts;

            // Stryker disable once all : a mutated interpolated string cannot bind to its handler (CS1620) and Safe Mode drops the method - B410
            _host.Out.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  phases, mean ms: sound {each(sum.Sound):0.00}, camera {each(sum.Camera):0.00}, project {each(sum.Project):0.00}, " +
                $"advance {each(sum.Advance):0.00}, capture {each(sum.Capture):0.00}, hud {each(sum.Hud):0.00}, draw {each(sum.Draw):0.00}, " +
                $"between {(_frameSeconds * 1000d / _framesMeasured) - each(sum.Total):0.00}; " +
                $"render thread draw {each(_renderMeasured):0.00} (mode 2 only, frame-weighted like the phases); " +
                $"particles now: {trails} trails, {bursts} bursts, {quads} quads last frame"));
        }

        // What the collector cost over the counted frames, from its own counters (finding 82).
        if (_framesMeasured > 0)
        {
            _host.Out.WriteLine("  " + GcReading.Now().Since(_gcAtStart, _framesMeasured));

            if (_ownedDevice?.TakePassReport() is { Length: > 0 } passes)
            {
                _host.Out.WriteLine("  " + passes);
            }

            if (_pauses is { } pauses)
            {
                _host.Out.WriteLine("  " + pauses.Report(_pausesFrom));
            }
            _host.Out.WriteLine("  " + JitReading.Now().Since(_jitAtStart));

            // The share of that compiled on THIS thread, which is the share that lands inside frames (B595).
            FrameCounters jitNow = ReadCounters();
            // Stryker disable once all : a mutated interpolated string cannot bind to its handler (CS1620) and Safe Mode drops the method - B410
            _host.Out.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  main thread jit: {jitNow.MainJitMethods - _countersAtStart.MainJitMethods} methods, {(jitNow.MainJit - _countersAtStart.MainJit).TotalMilliseconds:0.0} ms compiling"));
            // Stryker disable once all : a mutated interpolated string cannot bind to its handler (CS1620) and Safe Mode drops the method - B410
            _host.Out.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  first uses in play: models packed {jitNow.Models.Packed - _countersAtStart.Models.Packed}, {(jitNow.Models.PackTicks - _countersAtStart.Models.PackTicks) * 1000d / Stopwatch.Frequency:0.0} ms" +
                $"; baked built {jitNow.Models.Baked - _countersAtStart.Models.Baked}, {(jitNow.Models.BakedTicks - _countersAtStart.Models.BakedTicks) * 1000d / Stopwatch.Frequency:0.0} ms" +
                $"; static states {jitNow.Models.StaticStates - _countersAtStart.Models.StaticStates}, {(jitNow.Models.StaticStateTicks - _countersAtStart.Models.StaticStateTicks) * 1000d / Stopwatch.Frequency:0.0} ms" +
                $"; before play: models packed {_countersAtStart.Models.Packed}, {_countersAtStart.Models.PackTicks * 1000d / Stopwatch.Frequency:0.0} ms" +
                $"; baked built {_countersAtStart.Models.Baked}, {_countersAtStart.Models.BakedTicks * 1000d / Stopwatch.Frequency:0.0} ms" +
                $"; static states {_countersAtStart.Models.StaticStates}, {_countersAtStart.Models.StaticStateTicks * 1000d / Stopwatch.Frequency:0.0} ms"));
            _host.Out.WriteLine(
                "  main thread archive reads: " + (jitNow.Reads - _countersAtStart.Reads) +
                "; process page faults " + (jitNow.PageFaults - _countersAtStart.PageFaults).ToString(CultureInfo.InvariantCulture));
            _readsInPlay.Report(_host.Out.WriteLine);

            // Where the first-frame weapon-script precache landed against the window (B595 part 6): negative is before it.
            (long precacheTicks, long precachedAt) = _effects.WeaponScriptPrecache;
            _host.Out.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  weapon scripts precached in {precacheTicks * 1000d / Stopwatch.Frequency:0.0} ms, ending {(precachedAt - _pausesFrom) * 1000d / Stopwatch.Frequency:0.0} ms from the window's start"));

            // The writer's own timing of the window (B595, D230): the main thread's enqueues ("wait"), then the
            // writer thread's batches, then its queue over the whole run.
            _host.Out.WriteLine(
                "  log writes: main thread enqueue " + (jitNow.Log - _countersAtStart.Log) +
                (LogTiming is { } logTiming ? "; writer thread " + (logTiming() - _logAtStart) : string.Empty) +
                (LogQueue is { } logQueue ? "; " + logQueue() : string.Empty));
            _host.Out.WriteLine("  " + _effects.ParticleCost.Since(_particlesAtStart, _framesMeasured));
            _host.Out.WriteLine("  " + _effects.ParticleWork.Since(_particleWorkAtStart, _framesMeasured));
            _host.Out.WriteLine("  " + ParticleBirths.Since(_birthsAtStart, Tf2DemoSalvage.Content.Assets.ParticleEffect.BirthsBySystem, _frameSeconds, 12));
        }

        // The GC's own account beside the process's, read where the measurement ends (finding 80).
        // Stryker disable once all : a mutated interpolated string cannot bind to its handler (CS1620) and Safe Mode drops the method - B410
        _host.Out.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  {GcMemoryLine.Now()}; entity model vertices on the CPU {_presenter.Models.Vertices.Count:N0} in {_presenter.Models.VertexBytes / 1048576d:F0} MB"));

        if (_allocations is { } allocations)
        {
            _host.Out.WriteLine("  " + allocations.Tally.Report(8));
        }

        // Everything this process allocated since it started, every heap: the control for a whole-process trace (finding 80).
        // Stryker disable once all : a mutated interpolated string cannot bind to its handler (CS1620) and Safe Mode drops the method - B410
        _host.Out.WriteLine(string.Create(
            CultureInfo.InvariantCulture, $"  allocated since start: {GC.GetTotalAllocatedBytes() / 1048576d:0} MB"));

        // Stryker disable once all : a mutated interpolated string cannot bind to its handler (CS1620) and Safe Mode drops the method - B410
        Audio.SoundCacheHeld sounds = _presenter.SoundsHeld;
        _host.Out.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  sound cache: {sounds.Sounds} sounds, WAV {sounds.WaveBytes / 1048576d:0} MB, MP3 {sounds.Mp3Bytes / 1048576d:0} MB held; {sounds.Misses} decodes in play"));

        _framesMeasured = 0;
        _phasesMeasured = default;
        _renderMeasured = 0;

        foreach (string line in _measured.Lines)
        {
            _host.Out.WriteLine(line);
        }
    }

    /// <summary>Flies the camera by however long the last frame took.</summary>
    /// <remarks>
    /// **Here rather than in the key handler, which is the whole of B97.** A message-driven camera
    /// moves at whatever rate Windows repeats a held key; a frame-driven one moves at a speed. Its
    /// own stopwatch, because the playback clock only runs while the demo is playing and the camera
    /// has to fly while paused.
    /// </remarks>
    private void FlyCamera()
    {
        // **`absoluteframetime`, which is what Valve's own demo camera flies by** — see
        // `CalcDemoViewOverride` (`view.cpp:153`) and `FrameTimingConformanceTests`.
        double seconds = _clock.Drew();

        // **The demo frame time, kept beside the absolute one because the two are not the same
        // clock and the chase camera needs this one.** Valve flies the demo camera on
        // absoluteframetime so it works while paused — the comment above — while the chase
        // recovery is `m_flLastDistance += gpGlobals->frametime * 32.0f` (`hltvcamera.cpp:227`),
        // and frametime stops when the game does. A paused demo therefore keeps flying and stops
        // easing, and both halves of that are the engine's rather than a choice made here.
        _demoFrameSeconds = _transport.Playing ? seconds : 0d;

        // Every frame's duration passes through here, so this is where the worst one is noticed —
        // and where the meter takes its reading, rather than starting a second clock (B174).
        //
        // **Recorded UNCLAMPED, and the clamp used to be applied here, which hid the very defect it
        // was meant to help find.** The reading was `Math.Min(seconds, MaximumFrameSeconds)`, so the
        // worst frame could never be reported as worse than 100 ms — the ceiling. The owner's report
        // was "everything freezes for a half a second to maybe a second", and the log for those
        // exact seconds said `longest 100 ms`: not a coincidence, not a measurement, just the clamp
        // showing through. A saturating instrument is worse than a missing one.
        //
        // The clamp lives with FLIGHT now, in `FreeCameraController`, which is what it was always
        // for; applying it to the record of what happened was the mistake.
        // The longest frame is the LEDGER's business now — `FrameReporter.Drew` is handed this
        // duration from the idle loop and the ledger keeps the maximum itself, so there is one place
        // that knows what "worst frame this second" means rather than a field here and a reset there.
        //
        // `FrameClock.Drew` recorded it as `LastFrameSeconds` above; the assignment that used to be
        // here is gone with the field (B188, D90).

        // Which mode flies, and that `Intent` is read exactly once a frame and only then, are the camera's
        // (`CameraPresenter.Fly`); it is handed the reader and not the reading.
        if (_camera.Fly(_console.Intent, seconds))
        {
            // The view, and nothing else. Flight only happens in the free camera, where the map's
            // screen-space projection is not what is being drawn (B98). Not drained here (finding 98 §9 step 5): the upload
            // waits at its own pose, and the cull before it lands on the half not last handed, whatever drew.
            UploadCamera();
        }
    }

    /// <summary>Steps the sound for this frame, with the ears wherever the camera is.</summary>
    /// <remarks>
    /// **What is left here is the LISTENER and nothing else** (B188, D214): which camera the ears are at and whose eyes
    /// it is, asked for by <see cref="ViewerPresenter.PlaySounds(int, bool)"/> only once it knows there is a device to play through.
    /// </remarks>
    private void PlaySounds() => _presenter.PlaySounds(_transport.CurrentTick, _window.IsActiveApp);

    /// <returns>Whether a frame was actually drawn.</returns>
    /// <remarks>
    /// **The return value exists because the frame count was counting frames this method
    /// declined to draw.** The idle loop ran `RenderFrame(); CountFrame();` unconditionally, so
    /// during a map read — when this returns immediately — the loop still counted a frame per
    /// iteration. The per-second report then read
    ///
    /// <code>186.5 frames a second, longest 0 ms, drawing 0 ms</code>
    ///
    /// which is not a frame rate at all: it is how fast an empty loop spins. Every number in that
    /// line was consistent with itself and none of it measured rendering, which is the failure
    /// `docs/memory/logs-are-the-debugger.md#a-log-must-name-what-it-measured` is about. It survived because a high frame
    /// rate is the answer nobody investigates.
    /// </remarks>
    public bool RenderFrame()
    {
        // **Nothing is drawn while a map is being read on another thread (B146).** That read
        // replaces a dozen fields one at a time — the outline, the surfaces, the assets, the
        // terrain, the overlays — and drawing halfway through it would be drawing a world that does
        // not exist yet.
        //
        // The window keeps pumping, which is the whole point: it stays responsive, the menus work,
        // and Windows stops calling it hung. What it shows meanwhile is the last frame drawn, which
        // is what a frozen window showed anyway.
        if (_presenter.ReadingMap)
        {
            return false;
        }

        // **A ledger over the whole frame, because four rounds of guessing found three causes and
        // missed the fourth.** Each hypothesis so far — the model upload, the model read, the sound
        // decode — was instrumented one at a time, and the sound one recorded nothing. Timing every
        // phase and printing the breakdown whenever the frame is slow answers the question once
        // instead of proposing a suspect at a time.
        //
        // The residual is the important column: it is everything between these timers, so a large
        // one says the cost is somewhere nobody has thought to measure yet, which is precisely the
        // state this whole hunt started in.
        //
        // **The ORDER of those phases moved to `FrameSequence`** (B188, B203, D90). It is the
        // engine's frame order, it was wrong here for months, and it was wrong because a window
        // cannot be asked what order it does things in.
        GarbageReading frameBegan = GarbageReading.FromRuntime();

        // Read BEFORE the sequence, because its first step is the one that applies the opening (finding 82).
        bool openedBefore = _opening.Applied;

        long sequenceBegan = Stopwatch.GetTimestamp();
        FramePhases phases = FrameSequence.Run(this);
        long sequenceEnded = Stopwatch.GetTimestamp();

        // The render thread's drawing time since the last frame, carried from the queue (B581); zero in mode 0, whose draw
        // is timed as the `draw` phase itself.
        long rendered = _ownedDevice?.Queue.TakeRenderTicks() ?? 0;

        _frames.Drawing(phases.Draw);

        StallReport.Frame(phases, _renderLog, frameBegan, GarbageReading.FromRuntime());

        // **Every frame, averaged over the second — not sampled once a second.** `StallReport.Frame`
        // above fires only past 30 ms, so at the 90 fps this actually runs at it never fires and
        // nothing says where the 11 ms goes. Fed here rather than in `BuildOverlay` because this is
        // where the whole frame's phases exist; the overlay only knows its own.
        //
        // **Only once a demo is on screen** (D182). The window draws from the moment it opens now, behind the loading overlay, and
        // counting those frames spent `--measure 20` — and gate phase 3's whole run — on a demo still decoding. **And not the first
        // frame after**: the render loop is held off while the map is read, so that frame's duration is the whole wait, and counted it
        // was the entire measurement in one sample.
        //
        // **Nor the opening** (finding 82): the frame that seeks to `--tick` and the one after it, whose interval holds
        // that seek's tail, are the opening state, not playback. Counted, they were about 8 of `--measure 20`'s seconds
        // on f12 — three frames of about two seconds each — and every mean before 2026-10-19 carried them.
        bool onScreen = _presenter.HasDemo && _presenter.LoadsInFlight == 0 && openedBefore;

        if (onScreen && _wasOnScreen)
        {
            _frameSeconds += _clock.LastFrameSeconds;

            // The large-allocation tally counts the measured frames only, not the load before them (finding 80).
            if (_framesMeasured == 0 && _launch.MeasureSeconds is not null)
            {
                _allocations ??= new AllocationTickListener();
                _allocations.Tally.Clear();

                // And the per-second lines written before counting began, which described the opening (finding 82).
                _measured.Clear();
                _gcAtStart = GcReading.Now();
                _jitAtStart = JitReading.Now();
                _particlesAtStart = _effects.ParticleCost;
                _particleWorkAtStart = _effects.ParticleWork;
                _birthsAtStart = new(Tf2DemoSalvage.Content.Assets.ParticleEffect.BirthsBySystem, StringComparer.Ordinal);

                // Each stall beside whether a do-nothing thread stopped too (finding 88): `--measure` only.
                _pauses = new PauseLedger(Stopwatch.Frequency);
                _counted = false;
                _countersAtStart = ReadCounters();
                _ownedDevice?.TakePassReport();
                _readsInPlay.Arm();
                _logAtStart = LogTiming?.Invoke() ?? default;
                _pausesFrom = Stopwatch.GetTimestamp();
                _heartbeat?.Dispose();
                _heartbeat = new Heartbeat(_pauses);
            }

            if (_pauses is { } pauses)
            {
                // The counters at this boundary, over the SAME window as the frame's length (B595): read at entry, the
                // pause excluded `between`, so a stall mostly between could not have shown a collection.
                FrameCounters counters = ReadCounters();

                if (_lastSequenceEnded != 0 && _counted)
                {
                    // From the last frame's end to this one's, by this method's own stamps: this frame's phases, and the
                    // time since the last frame ended as `between`. The frame clock is stamped mid-sequence, so its interval
                    // straddles two frames' phases and named the wrong one.
                    long frameTicks = sequenceEnded - _lastSequenceEnded;
                    long between = sequenceBegan - _lastSequenceEnded;

                    pauses.Frame(
                        sequenceEnded,
                        frameTicks,
                        PauseLedger.LargestPhase(phases, between + phases.Total),
                        (counters.GcPaused - _lastCounters.GcPaused).TotalMilliseconds,
                        pauses.IsSlow(frameTicks) ? SlowFrameDetail(phases, between, counters) : string.Empty);
                }

                _lastCounters = counters;
                _counted = true;
            }

            _framesMeasured++;
            _phasesMeasured = new FramePhases(
                _phasesMeasured.Sound + phases.Sound,
                _phasesMeasured.Camera + phases.Camera,
                _phasesMeasured.Project + phases.Project,
                _phasesMeasured.Advance + phases.Advance,
                _phasesMeasured.Capture + phases.Capture,
                _phasesMeasured.Hud + phases.Hud,
                _phasesMeasured.Draw + phases.Draw,
                _phasesMeasured.Total + phases.Total);
            _renderMeasured += rendered;
        }

        _wasOnScreen = onScreen;
        _lastSequenceEnded = sequenceEnded;

        // **Captures while playing** (TF2VIEW_PLAY_SHOTS), at the first frame at or past a listed tick. The device writes the
        // PNG on the next present, so its sidecar is written here on that next frame, from the state of the frame it shows.
        if (_playedCaptures?.SidecarDue() is { } written)
        {
            WriteSidecar(written);
        }

        if (onScreen && _transport.Playing && _playedCaptures?.Due(_shownTick) is { } played)
        {
            CaptureViewport(played);
        }

        // **The log's own meter, always smoothed**, fed the frame time the on-screen one gets. The on-screen meter answers
        // nothing while `cl_showfps` is 0 — Valve's rule for drawing it — and with no TF2 install it is never built, so a
        // log that borrowed its reading went silent on a fresh profile and `--measure` counted 0 samples on CI.
        // **Timed only for `--measure`**: the parts cost a clock read each, and a log line nobody asked for.
        if (_launch.MeasureSeconds is not null)
        {
            _presenter.FrameParts ??= new FrameParts();

            // The draw's passes, CPU and GPU, per frame (B597): the device times them only when asked.
            _ownedDevice?.ProfilePasses = true;

            // Every sun query timed where it runs, whoever asked (B594 follow-up): two clock reads a call, so measuring only.
            _presenter.Map?.Lighting.TimeSun = true;
        }

        _presenter.FrameParts?.Frame();

        if (_presenter.FrameParts is { } sunParts)
        {
            _sunMeter.Frame(_presenter.Map?.Lighting, sunParts);
        }

        // **The render thread's own time and every drain that waited** (B581), carried from the queue where each was
        // timed; in mode 0 the queue never waits and draws nothing of its own, so neither part appears.
        if (_ownedDevice?.Queue is { } queue)
        {
            foreach ((string caller, long ticks) in queue.TakeWaits())
            {
                if (!_queueWaitParts.TryGetValue(caller, out string? part))
                {
                    part = "queue wait " + caller;
                    _queueWaitParts[caller] = part;
                }

                _presenter.FrameParts?.Add(part, ticks);
            }

            if (rendered > 0)
            {
                _presenter.FrameParts?.Add("queue render thread", rendered);
            }
        }

        if (_frameRateLog.Report(_loggedRate.Sample(_clock.LastFrameSeconds), phases, _frameSeconds) is { } logged)
        {
            // The parts of `camera` and `hud`, over the same frames as the line they follow. Nested: `hud frame` holds
            // the `hud think`…`hud paint` parts, `hud paint` the viewport's children, `specgui` its cards, a card its `card.*`.
            string rate = _presenter.FrameParts is { } parts ? logged + "; parts: " + parts.Report() : logged;

            _renderLog.LogInformation("{Message}", rate);

            // **`--measure`, and the line it prints is the one just logged.** Reported from here
            // rather than gathered separately so the two cannot differ: what stdout shows is
            // literally what the log shows, which is the rule that stops a summary from being a
            // second, disagreeing measurement.
            _measured.AddRate(rate);

            // The rebuild breakdown beside the rate, since one without the other says where the
            // frame went but not what it was doing.
            if (_moments.LastCost is { } cost)
            {
                _measured.AddCost(cost);
                _moments.LastCost = null;
            }

            // **What is alive, once a second, for a leak to show as a count that climbs** (lifetime audit, 2026-10-17).
            if (_launch.MeasureSeconds is not null)
            {
                (int trails, int modelDecals) = _effects.KeptCounts;
                (long replayTicks, long replaySteps) = _effects.ParticleReplay;
                (long Checked, long Asked, long Merged, long Ticks) merges =_presenter.Map?.Lighting.DynamicMerges ?? default;
                _measured.AddNote(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{ParticleCensusLine.Format(_shownTick, _effects.ParticleCensus())}; trails {trails}; model decals {modelDecals}" +
                    $"; replays so far {replayTicks * 1000d / Stopwatch.Frequency:0.#} ms over {replaySteps} steps" +
                    $"; dynamic light draws so far {merges.Asked} of {merges.Checked}, re-ranked {merges.Merged}, {merges.Ticks * 1000d / Stopwatch.Frequency:0.##} ms" +
                    $"; particle forks so far {_effects.ParticleForks}; bone forks so far {_presenter.BoneForks}; {_sunMeter.Report()}"));
            }
        }

        // **Counted in seconds of PLAYBACK.** A wall clock would spend the first twenty seconds on
        // archives and the map, so `--measure 40` would be about two seconds of frames — which is
        // exactly the mistake this replaces.
        if (_launch.MeasureSeconds is not null && !_transport.Playing)
        {
            // A paused viewer repaints only when asked; a measurement of the paused frame asks every frame, as a user
            // moving the free camera does (B420 was measured that way).
            _window.Redraw();
        }

        if (_launch.MeasureSeconds is { } run && _frameSeconds >= run)
        {
            if (_launch.ThenSeek is { } back && !_seekedForMeasure)
            {
                // B420's scenario: print what playback measured, then pause, seek and measure the paused frame as long.
                _seekedForMeasure = true;
                PrintMeasured(MeasuredPlaybackHeading());
                _transport.Playing = false;
                _playback.Seek(back);
                _transport.ShowTick(back);
                ShowMoment(back);
                _frameSeconds = 0d;
                _measured.Clear();
            }
            else
            {
                FinishMeasuring();
            }
        }

        // **NOT cleared here, and that was a real bug.** `Instances` clears the list it fills, so
        // it is emptied and refilled by the pose step exactly like the world's own list — and the
        // pose step does not run on a paused frame. Clearing after the draw meant the viewmodel
        // survived exactly one frame and every capture, which is taken while paused, got nothing.
        //
        // The pass is fed empty when first person is off because AddViewmodel drops the camera
        // then, which is the state that actually means "draw none".

        if (_fullScreenClock is { } clock)
        {
            _fullScreenClock = null;

            // Stryker disable once all : a mutated interpolated string cannot bind to its handler (CS1620) and Safe Mode drops the method - B410
            _renderLog.LogDebug(
                "{Message}",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"full screen {(_window.IsFullScreen ? "on" : "off")} took " +
                    $"{clock.Elapsed.TotalMilliseconds:F0} ms to the first frame at " +
                    $"{_window.Viewport.Width}x{_window.Viewport.Height}"));
        }

        return true;
    }

    /// <summary>Advance the world to the moment this frame shows.</summary>
    /// <remarks>
    /// **This ran AFTER the camera until 2026-08-26** (B203), so every frame drew tick T+1's
    /// entities through tick T's eye. Valve simulates in `CHLClient::HudUpdate`, before the view is
    /// built at all (`cdll_client_int.cpp:1308`).
    /// </remarks>
    public void Simulate() => _playback.Advance();

    /// <summary>`--measure`'s sun line, over the whole frame.</summary>
    private readonly SunMeter _sunMeter = new();

    /// <summary>`queue wait {caller}` part names, made once per caller rather than per frame.</summary>
    private readonly System.Collections.Generic.Dictionary<string, string> _queueWaitParts = new(StringComparer.Ordinal);

    /// <summary>Work out where the eye is and hand the camera to the GPU.</summary>
    /// <remarks>
    /// **Uploaded every frame, because the view can change without anything here being told.** This
    /// used to be sent only from FlyCamera, and only when the free camera had actually moved — so in
    /// the first-person view the recorded camera advanced every tick, produced a correct matrix, and
    /// never reached the GPU. The owner: "pov isnt actually updating cam position at the tick rate
    /// either, the only way to get the cam in pov to move is by clicking and moving the mouse",
    /// which is mouse-look going through the flight path that does upload.
    ///
    /// Same shape as the debug views not appearing until the camera moved, and the same lesson: a
    /// value computed correctly and not sent is indistinguishable from one computed wrongly.
    /// Uploading unconditionally costs one 112-byte constant write per frame and removes the whole
    /// class — a spectator switch, a scrub, a demo change and playback itself all move the view
    /// without touching FlyCamera.
    /// </remarks>
    public void PlaceCamera()
    {
        long flyAt = Stopwatch.GetTimestamp();

        FlyCamera();

        UploadCamera(Stopwatch.GetTimestamp() - flyAt);

        _cameraPlacedAtTick = _shownTick;
    }

    /// <summary>The tick shown when this frame's camera was placed, carried to the draw (B431).</summary>
    private double _cameraPlacedAtTick;

    /// <summary>Whether the next draw is the first after the opening state, and should say what it drew.</summary>
    private bool _reportFirstDrawAfterOpening;

    /// <summary>Put the ears where the eye is, and play what is due.</summary>
    /// <remarks>
    /// **This ran FIRST until 2026-08-26** (B203), so the listener sat at the previous frame's eye.
    /// Valve sets the audio state from the same `viewEye` it just built the camera from, four
    /// statements later (`view.cpp:779-796`).
    /// </remarks>
    public void UpdateListener() => PlaySounds();

    /// <summary>Rebuild the projected world, if anything invalidated it.</summary>
    /// <remarks>
    /// **Reprojected here rather than in the resize handler**, which is what coalesces a burst of
    /// resizes into one rebuild. Idle runs when the message queue empties, so every layout step of a
    /// full-screen transition — or every pixel of a window drag — is collapsed into the single size
    /// that was current when the pump went quiet.
    ///
    /// **The scene is projected too, so a camera change invalidates it as well.** Points are stored
    /// in screen space while the world's vertices are not, so rebuilding one and not the other left
    /// every dot at the pixel it had before the zoom while the map moved underneath it. Playback hid
    /// this by rebuilding the scene every frame regardless; it only showed while paused.
    ///
    /// Done here rather than beside each camera change: five places already set this flag, and the
    /// next one added would have had the same bug again.
    /// </remarks>
    public void ProjectWorld()
    {
        // **The pose, here and not in `Simulate`** (B255). The engine's order is `SetUpView`, then
        // `BuildWorldLists`, then `BuildRenderablesList` — which is where `SetupBones` becomes
        // reachable — so posing belongs after the view exists and beside the world lists, not before
        // the camera has been placed. Posing in `Simulate` meant the newest frustum available was
        // the previous frame's, and culling against a stale view pops entities in at the screen edge.
        //
        // **The frustum is the device's own**, the same one the world cull and the draw use, rather
        // than a second one built from the camera here
        // (`docs/memory/instrument-bugs-outnumber-decoder-bugs.md#one-camera-or-the-cull-lies`).
        //
        // **Before the early return, because the two are unrelated.** `NeedsProjecting` asks whether
        // the WORLD's screen-space projection is stale, which a camera move invalidates and a demo
        // tick does not; the pose is due on every frame that built a moment.
        // A span cannot be null-coalesced, so the device is tested once and both halves come from
        // the same one — which is also what stops a frustum and a visible set from different frames
        // reaching the cull together.
        _presenter.PoseNow();

        if (!_presenter.WorldNeedsProjecting)
        {
            return;
        }

        _presenter.WorldProjected();
        ProjectMap();
        ReprojectScene();
    }

    /// <summary>Take a screenshot if one was asked for.</summary>
    /// <remarks>
    /// **Not named `Capture`**, which is `Control.Capture` — WinForms' mouse capture. See
    /// <see cref="IFrameSteps.TakeShot"/>.
    /// </remarks>
    public void TakeShot() => TakeAutomaticShot();

    /// <summary>Draw the frame.</summary>
    /// <param name="overlay">The VGUI built for this frame.</param>
    public void Draw(FrameOverlay overlay)
    {
        // **B431's output-level check.** The tick CARRIED from the camera placement, never re-read:
        // the UI suite asserts this names the opening tick.
        if (_reportFirstDrawAfterOpening)
        {
            _reportFirstDrawAfterOpening = false;
            _log.LogInformation(
                "{Message}",
                $"first frame after the opening state draws the camera placed at tick {_cameraPlacedAtTick:0}");
        }

        // The tick this frame shows, for TF2VIEW_FRAME_HASH's lines (B581): carried with the frame, never re-read.
        if (_ownedDevice is { } device)
        {
            device.FrameLabel = _shownTick;
        }

        DrawScene(overlay);
    }

    /// <summary>Hands this frame's scene to the device: what is handed, and in which order, is the presenter's (D214).</summary>
    private void DrawScene(FrameOverlay overlay) =>
        _presenter.DrawScene(BackgroundRed, BackgroundGreen, BackgroundBlue, _debug.LeafVis, overlay);

    /// <summary>Rebuilds the projected scene after the camera has moved.</summary>
    /// <remarks>
    /// Uses the clock's position when there is one, so a paused viewer reprojects the moment it is
    /// actually showing rather than jumping to the scrub bar's whole tick.
    /// </remarks>
    private void ReprojectScene()
    {
        // **The `_timeline is null` guard here was redundant and is gone** (2026-08-26).
        // `ShowMoment` opens with the same test, so this one could not change what happened — and a
        // condition nothing can distinguish from its absence is dead code that also survives every
        // mutation of itself. The same argument retired a `ticket != 0` guard in `LoadTickets` an
        // hour earlier.
        //
        // The guard at `MomentChanged` is NOT redundant and stays: it also skips a
        // `_viewport.Invalidate()`, so removing it would repaint for a demo that is not there.
        ShowMoment(_playback.Position ?? _transport.CurrentTick);
    }
}
