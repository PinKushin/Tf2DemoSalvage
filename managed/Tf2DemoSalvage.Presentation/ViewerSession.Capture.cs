using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Logging;
using Tf2DemoSalvage.Render;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation;

public sealed partial class ViewerSession
{
    /// <summary>What the command line asked for.</summary>
    /// <remarks>
    /// **Six `_shot*` fields until 2026-08-25**, written one at a time by a parser in this form.
    /// Reading a command line is not window work and could not be tested where it was, so every
    /// option was covered only by whichever UI test happened to pass one (B188, D90).
    /// </remarks>
    private readonly LaunchOptions _launch;

    /// <summary>The countdown from opening to the capture, and what it wants each frame.</summary>
    /// <remarks>
    /// **Was `_shotPath`, `_shotDelay` and `_openingDone`** (B188, D90) — a small state machine in a
    /// window, reachable only by launching with `--shot` and looking for a file afterwards.
    ///
    /// The reason the path is consumed rather than read survives the move: it is not only a request,
    /// and taking the shot closes the window, so a second one is a race rather than a duplicate
    /// file. It stayed out of <see cref="LaunchOptions"/> for the same reason it still does — a
    /// record of what was ASKED for should not be edited to record what has been done.
    /// </remarks>
    private readonly OpeningSequence _opening;

    /// <summary>Frames to let the world settle before the opening state is applied.</summary>
    /// <remarks>
    /// **Counted in frames, not seconds**, so it measures settled frames rather than guessing at a
    /// machine. Restarted when a demo loads, because a demo opened from the playlist arrives after
    /// the window did — see the note in `Apply`.
    /// </remarks>
    private const int OpeningFrames = 45;

    /// <summary>Frames to let the world settle before the opening state is applied.</summary>
    /// <remarks>
    /// **Five, and it used to be the bare literal `40` compared against the countdown** (B208). That
    /// is `OpeningFrames - 5` written out, and the coupling was invisible: lower `OpeningFrames`
    /// below 40 and `_shotDelay` never equals it, so `ApplyOpeningState` silently never runs — no
    /// error, no log, just a viewer that ignores every `--camera`, `--first-person` and `--look-at`
    /// it was given.
    ///
    /// **Derived rather than repeated**, so the two numbers cannot disagree.
    /// </remarks>
    private const int SettleFrames = 5;

    /// <summary>The frame time the exposure walks by while a capture waits for it: one 66.67 Hz tick (B520).</summary>
    private const float CaptureExposureSeconds = 0.015f;

    /// <summary>Frames a capture run waits for a demo before giving up and writing nothing.</summary>
    /// <remarks>
    /// **A backstop, not a timing parameter** (B401). Ordering the capture after the opening state
    /// means waiting on a condition, and a condition that never becomes true has to end somewhere —
    /// a `--shot` whose demo argument never reached the window drew an empty viewport at 299 fps
    /// until an outer timeout killed it.
    ///
    /// **Frames are the wrong unit for it and it is still worth having.** What is being waited on is
    /// a disk read, not rendering work, so this is roughly 100 seconds at the ~300 fps an empty
    /// viewport draws at and several minutes at 60 — deliberately far past any real load. A wall
    /// clock would be the honest unit and would mean giving this class a clock; the number is large
    /// enough that the difference cannot decide a real run.
    /// </remarks>
    private const int PatienceFrames = 30_000;

    /// <summary>Takes the automatic capture once the world has settled, then closes.</summary>
    /// <remarks>
    /// **The claim that used to be here was false, and it cost B401**: *"the map, its textures and
    /// the entity models all load before the first frame is drawn, so a frame count after that is a
    /// count of settled frames"*. They do not. Frames are drawn from the moment the window opens,
    /// at ~300 fps with nothing in them, while the demo and its map take about twenty seconds — so
    /// the whole 45-frame budget was spent before there was anything to photograph.
    ///
    /// **The count now runs from the seek, not from the window**, and the capture waits on the
    /// opening state having been applied rather than sharing its clock. See `OpeningSequence`.
    /// </remarks>
    private void ReportCaptureWaiting(string gate, string state)
    {
        // **Said once a second, with the values the gate read** (the CI hang of 2026-10-11): a capture held by a gate
        // drew frames for three minutes and logged nothing, so the log could not say which gate or why.
        if (Stopwatch.GetElapsedTime(_captureWaitingReported).TotalSeconds < 1d)
        {
            return;
        }

        _captureWaitingReported = Stopwatch.GetTimestamp();
        _log.LogInformation("{Message}", $"capture waiting on {gate}: {state}");
    }

    /// <summary>When <see cref="ReportCaptureWaiting"/> last wrote its line.</summary>
    private long _captureWaitingReported;

    private void TakeAutomaticShot()
    {
        // **The opening state is not the capture, and tying them together made half the switches
        // unusable.** `--tick`, `--first-person`, `--spectate` and `--colours` all say where to
        // START; `--shot` says to photograph it and quit. Gating the first on the second meant the
        // only way to be put at a tick was to be handed a PNG, which is no way to LOOK at
        // something — and looking is the only instrument for anything about a picture.
        // **The countdown is `OpeningSequence`'s; the acting is this window's** (B188, D90) — which
        // is `FramePacer`'s split. Seeking, capturing and closing are things a window does; when to
        // do them is arithmetic that needed no window and could not be tested inside one.
        //
        // The coupling B208 found lives there now too: the settle point is `openingFrames -
        // settleFrames` rather than a literal, so lowering the wait cannot silently make it
        // unreachable and drop every launch option.
        // **A load in flight is not a demo that never came.** The patience is a frame count, and a
        // window that draws at 300 fps while an 80-second decode runs would give up two thirds of
        // the way through it.
        if (_presenter.LoadsInFlight > 0)
        {
            return;
        }

        // **Nothing left to do, so nothing asked of the device** (B581): the settle test below reads the device, which in
        // `mat_queue_mode 2` waits for the frame in flight, and it ran every frame for the rest of a session whose
        // opening was long over — the sequence answers `Nothing` from here on regardless.
        if (_opening.Finished)
        {
            return;
        }

        // **A capture waits for the post-processing to settle** (D192, B514): the bloom amount walks to the map's
        // value at 5% a frame from 1, so the opening's 45 frames would photograph it 10% high.
        if (_opening.Applied && _device is { PostProcessingSettled: false } unsettled)
        {
            ReportCaptureWaiting("post-processing", unsettled.PostProcessingReport);

            return;
        }

        // **And for the glows to arrive** (B378, B521): a lamp's halo fades in over a sixteenth of a second from the
        // first frame its occlusion query answers, so a capture taken sooner photographs it part way.
        if (_opening.Applied && !_effects.GlowsSettled)
        {
            ReportCaptureWaiting("glow occlusion", _effects.GlowsUnsettled());

            return;
        }

        switch (_opening.Advance())
        {
            case OpeningStep.ApplyOpeningState:
                ApplyOpeningState();
                break;

            case OpeningStep.Capture when _opening.TakeShotPath() is { } path:
                Pick();
                CaptureViewport(path);
                WriteSidecar(path);
                _window.Close(posted: true);
                break;

            // **Nothing is written, on purpose** (B401). An empty PNG is what the three silent
            // breaks of `--shot` all produced, and it reads as "the viewer drew nothing" rather
            // than "the viewer was never given anything". A line and a non-capture say which.
            case OpeningStep.GiveUp:
                _log.LogWarning(
                    "{Message}",
                    $"no demo loaded after {PatienceFrames} frames, so --shot wrote nothing");
                _opening.TakeShotPath();
                _window.Close(posted: true);
                break;

            default:
                break;
        }
    }

    /// <summary>Puts the viewer where the command line said to start, once there is a demo.</summary>
    /// <remarks>
    /// **`--tick`, `--first-person`, `--spectate` and `--colours` say where to START**, and this is
    /// the one place that obeys them.
    ///
    /// **It used to be reachable only at one frame, and that stopped working the day a demo could
    /// arrive late.** The block sat inside the capture countdown, guarded by
    /// `_shotDelay == 40 &amp;&amp; _timeline is not null` — a single frame, about forty frames after the
    /// window opened. That was safe while a demo named on the command line was loaded inside the
    /// constructor, so the timeline always existed by the first frame. It is not safe now: opening
    /// several files lists them without loading any, so the playlist supplies the demo *after* that
    /// frame has gone, the guard fails once, and the opening state is lost for the rest of the run.
    ///
    /// The symptom was a viewer sitting at tick zero with `--tick 2500` on its command line, and a
    /// capture test that failed with "the viewmodel never reached the screen" — true, because at
    /// tick zero nobody is holding anything. It passed intermittently beforehand only when some
    /// earlier test happened to move the transport first.
    ///
    /// So it is called from both ends now: from the countdown, as before, and from
    /// the load's <c>Apply</c> when a demo finishes loading. <c>OpeningSequence.Applied</c> makes the
    /// second of those a no-op.
    /// </remarks>
    private void ApplyOpeningState()
    {
        if (_opening.Applied || !_presenter.HasTimeline)
        {
            return;
        }

        // **This window says so, because applying can fail.** With no demo open there is nothing to
        // seek to, and a sequence that marked itself applied would count the refusal above as a
        // success and never offer again.
        _opening.MarkApplied();

        // **The clock too, not just the transport.** Moving the camera marks the world stale, and
        // the reprojection that follows re-reads the moment from the clock - so a capture that only
        // told the transport photographed tick zero while every log line said otherwise.
        foreach (int through in OpeningSequence.SeekThrough(_host.Environment("TF2VIEW_SEEK_THROUGH")))
        {
            _playback.Seek(through);
            _transport.ShowTick(through);
            ShowMoment(through);
            _log.LogInformation("{Message}", $"opening state sought through tick {through}");
        }

        _playback.Seek(_launch.ShotTick);
        _transport.ShowTick(_launch.ShotTick);
        ShowMoment(_launch.ShotTick);

        _log.LogInformation("{Message}", $"opening state applied at tick {_launch.ShotTick}");
        _reportFirstDrawAfterOpening = true;

        if (_launch.SurfaceColours)
        {
            _window.ShowSurfaceColours(true);
        }

        // **After the seek, because entering the first-person view reads the moment.** The camera
        // is placed from the recorded view or from the followed player at the CURRENT tick, so
        // switching before the clock moves photographs the right mode at the wrong instant — and
        // the picture looks like a camera bug rather than an ordering one.
        //
        // Skipped when the mode is already first person, because on a point-of-view recording the
        // D128 lock entered it at load — and `ToggleFirstPerson` TOGGLES, so calling it again
        // would be an attempt to leave, refused with a warning on every `--first-person` run.
        if (_launch.FirstPerson && !_camera.AskedFirstPerson)
        {
            _ = _camera.ToggleFirstPerson();
        }

        // **Entered directly rather than by cycling** (D134). `CycleCameraMode` goes Free → first
        // person → third person, so reaching this by toggling would pass THROUGH first person and
        // photograph whichever one the count happened to land on. `EnterCamera` takes the mode it
        // is given, and it is the same call the key ends in — so the launch route and the key
        // cannot drift apart.
        if (_launch.ThirdPerson && !_camera.AskedThirdPerson)
        {
            _ = _camera.EnterThirdPerson();
        }

        // **`--look-at` and `--zoom` applied here until 2026-08-26** (D98). They centred and
        // magnified the orthographic map, and neither has a meaning for a camera that is placed in
        // the world rather than fitted to it. `LaunchOptions` still carries both fields; what they
        // should mean for a free camera — fly to a point, at what distance — is a question for
        // whoever reimplements them, and inventing an answer here would be the guess this project
        // keeps paying for.
    }

    /// <summary>The command line as given, for the sidecar's <c>args</c>.</summary>
    private readonly IReadOnlyList<string> _arguments;

    /// <summary>Writes the D226 sidecar beside a <c>--shot</c>: what this capture depended on, read from where it was used.</summary>
    /// <param name="shot">The PNG's path.</param>
    /// <remarks>
    /// **Every value is carried, not recomputed** (B243): the camera is the one the last frame handed the device, the
    /// tick the one last shown, the viewport the window's, the ConVars the settings in force. The PNG itself is written
    /// on the next present, which changes none of those on a `--shot` run: the camera is placed and the clock paused. A
    /// capture taken while PLAYING is not paused, so its sidecar is written a frame later (<see cref="PlayedCaptures.SidecarDue"/>).
    /// </remarks>
    private void WriteSidecar(string shot)
    {
        System.Reflection.Assembly built = typeof(ViewerSession).Assembly;
        string? version = System.Reflection.CustomAttributeExtensions
            .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(built)?.InformationalVersion;
        int plus = version?.IndexOf('+', StringComparison.Ordinal) ?? -1;
        string? demo = _presenter.DemoPath;

        // **Hashed once, and that one value is both recorded and compared** (B243).
        string? demoSha = demo is null ? null : ShotSidecar.Sha256(demo);
        string notes = string.Empty;
        ShotLikeRef? likeRef = null;

        if (_launch.Like is { } like)
        {
            string likeText = File.ReadAllText(like);
            likeRef = new ShotLikeRef(Path.GetFileName(like), ShotSidecar.Sha256(like));

            // **A different demo warns and is noted, and the shot is still taken** (owner, on D226: "probably").
            if (ShotSidecar.Read(likeText).DemoSha256 is { } theirs && !string.Equals(theirs, demoSha, StringComparison.OrdinalIgnoreCase))
            {
                notes = $"demo differs from the TF2 side: demoSha256 {theirs} in {likeRef.File}, {demoSha} here";
                _log.LogWarning("{Message}", $"the demo opened ({demoSha}) is not the demo {likeRef.File} was taken of ({theirs})");
            }
        }

        Dictionary<string, string> convars = new(StringComparer.Ordinal);

        // **Read back through the one route the frame reads them by** (`ClientConVar`), and only those that differ from
        // their declared default (owner: "only the ones that differ from defaults").
        foreach (Core.Net.EngineConVar declared in Core.Net.EngineConVars.All)
        {
            if (!declared.Replicated && ClientConVar(declared.Name) is { } value && ShotSidecar.DiffersFromDefault(value, declared.Default))
            {
                convars[declared.Name] = ShotSidecar.Private(value);
            }
        }

        Dictionary<string, string> env = new(StringComparer.Ordinal);

        foreach (string name in _host.VariableNames())
        {
            if (name.StartsWith("TF2VIEW_", StringComparison.Ordinal) && _host.Environment(name) is { } value)
            {
                env[name] = ShotSidecar.Private(value);
            }
        }

        FreeCamera? eye = _presenter.LastViewing;

        ShotRecord record = new(
            DateTimeOffset.UtcNow,
            demo is null ? null : Path.GetFileName(demo),
            demoSha,
            _presenter.MapName,
            (int)Math.Round(_shownTick),
            eye is { } e ? (e.Origin.X, e.Origin.Y, e.Origin.Z, e.Angles.Pitch, e.Angles.Yaw) : null,
            _window.Viewport,
            convars,
            // **The tree is asked once, here, and the answer carried** (owner: "-dirty" when uncommitted).
            ShotSidecar.GitSha(plus < 0 ? null : version![(plus + 1)..], _host.TreeDirty()),
            _host.Build,
            [.. _arguments.Select(ShotSidecar.Private)],
            env,
            likeRef)
        {
            Notes = notes,
        };

        string path = ShotSidecar.PathFor(shot);
        File.WriteAllText(path, ShotSidecar.Json(record));
        _log.LogInformation("{Message}", $"sidecar written beside the shot: {Path.GetFileName(path)}");
    }

    /// <summary>`TF2VIEW_PICK="x y"`: logs what lies under that pixel of the shot — <see cref="PickReport"/>.</summary>
    private void Pick()
    {
        string? report = _presenter.Pick(_host.Environment("TF2VIEW_PICK"));

        if (report is not null)
        {
            _renderLog.LogInformation("{Message}", report);
        }
    }

    /// <summary>Writes the next drawn frame to a PNG.</summary>
    /// <param name="path">Where to write it.</param>
    /// <exception cref="ArgumentException"><paramref name="path"/> is null or blank.</exception>
    /// <remarks>
    /// **The picture comes from the renderer the user is looking at**, which is the only kind that
    /// is evidence about it. The offscreen target draws the same scene through a parallel path, and
    /// a parallel path agrees only until one side gains an argument the other does not — decals
    /// were added to this one and not to that one, and its pictures went on being read as though
    /// they showed the viewer.
    ///
    /// Bound to F12 as well, because a person looking at something wrong wants to send a picture of
    /// it more often than a test does.
    /// </remarks>
    public void CaptureViewport(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        _device?.CaptureNextFrame(path);
        _window.Redraw();
    }

    /// <summary>Environment variable naming where captures are written.</summary>
    /// <remarks>
    /// **This exists because the UI suite deleted the owner's screenshots.** Captures were written
    /// beside the log and pruned to the newest twenty, and the capture tests press F12 — so every
    /// run of the suite wrote captures and evicted older ones. A day of chasing B146 and B148 spent
    /// the entire development history of hand-taken shots, and the loss was only noticed when a
    /// "before" image was wanted and there was none.
    ///
    /// > *"the test runs and the manual SS's should not be on the same thing captures kept though,
    /// > test SS's can literally be deleted immedietly, i dont look at them, they are worthless,
    /// > because we are not comparing them to a golden image. i need the manual SS's"*
    ///
    /// **It solves a second problem at the same time.** Raising the limit was the obvious answer and
    /// is the wrong one here: the owner's C: drive is nearly full, and captures were already found
    /// occupying 203 MB once. Pointing this at another drive keeps as many as somebody wants without
    /// spending the disk that is short.
    /// </remarks>
    /// <summary>Where captures are written; beside the log unless told otherwise.</summary>
    /// <remarks>
    /// **A setting, not an environment variable** — <c>cl_screenshot_folder</c> in the config, or
    /// <c>+cl_screenshot_folder &lt;path&gt;</c> at startup, which is Source's own convention for
    /// setting a cvar from the command line. The variable this replaced had to be exported in the
    /// shell that launched the viewer, so it was lost on every terminal restart and absent whenever
    /// a demo was opened by double-clicking it — which is the ordinary way to open one.
    ///
    /// Falls back rather than failing when the named folder cannot be made: a screenshot is not
    /// worth refusing to run over, and the log says where it actually went.
    /// </remarks>
    // **The fallback policy moved to `Captures.Folder`** (B208). What this window still supplies is
    // the fallback itself: the log's folder, which is also where captures go — one directory, one
    // retention policy (D83). `FileLogWriter` owns that path so both writers agree on it.
    public string CaptureFolder =>
        Captures.Folder(_settingsOwner.ScreenshotFolder, FileLogWriter.DefaultFolder, _renderLog);

    /// <summary>Captures the viewport to a stamped file beside the log.</summary>
    /// <remarks>
    /// The one place that decides where a screenshot goes, so the menu item and F12 cannot
    /// disagree about it. They were one copied expression apart, which is exactly how the viewer's
    /// two drawing paths drifted until one of them stopped showing decals.
    /// </remarks>
    public void CaptureViewportToFile()
    {
        string folder = CaptureFolder;

        string shot = Path.Combine(folder, Captures.Name(DateTime.Now));
        CaptureViewport(shot);

        // **The screenshot key's sidecar is a Debug build's only** (owner); `--shot` writes one in every build.
        if (_host.IsDebug)
        {
            WriteSidecar(shot);
        }

        // **Captures had no retention of any kind until 2026-08-19**, when 233 of them were found
        // occupying 203 MB — the single largest thing the viewer had written to the owner's disk,
        // and it had never reported a byte of it.
        //
        // The limit is lower than the logs' because a screenshot is two orders of magnitude larger:
        // fifty logs is a few megabytes and fifty captures is most of a gigabyte. It is not zero,
        // because a capture is taken deliberately — somebody pressed a key — and the recent ones
        // are usually the comparison being made.
        //
        // **The limit was never the thing that lost the history, though.** Captures went beside the
        // log and the UI suite presses F12, so a dozen test runs in a day evicted every hand-taken
        // shot the project had. That is fixed by TF2VIEW_CAPTURE_FOLDER — the tests write somewhere
        // else and can only delete each other — rather than by raising this, which would spend a
        // disk that is already short.
        //
        // After the write, matching the log path, and for the same reason: pruning first lets
        // concurrent writers each trim to the limit and then each add one.
        // **`Captures.Pattern` rather than a literal `"shot-*.png"`** (B208). The glob has to match
        // what `Captures.Name` produces, and it did so only because the same prefix was typed in
        // two places — a disagreement would prune nothing, silently, since deleting zero files looks
        // exactly like having nothing to delete.
        // **A shot's sidecar goes with it**, so pruning leaves no orphan `.png.json` (D226).
        FileRetention.Keep(folder, Captures.Pattern, Captures.Kept, ShotSidecar.PathFor(string.Empty));
    }

    /// <summary>The demo named on the command line, opened once the window is up; null when none was.</summary>
    private string? _openOnShow;

    /// <summary>Opens the demo named on the command line, the way a double-click in the playlist would.</summary>
    /// <returns>The load, or the nothing-selected result when the command line named no single demo.</returns>
    /// <remarks>
    /// **Held in <see cref="Loading"/> by the <c>Shown</c> handler, never <c>async void</c>** — the owner's rule, and the
    /// playlist's pattern: *"we dont async void, we do pass back"*.
    /// </remarks>
    public async Task<DemoLoadResult> OpenCommandLineDemo()
    {
        if (_openOnShow is null)
        {
            return await NothingSelected.ConfigureAwait(true);
        }

        DemoLoadResult result = await _presenter.LoadDemoAsync(_openOnShow).ConfigureAwait(true);

        if (result.Outcome == DemoLoadOutcome.Failed)
        {
            FailHeadless(result.Message);
        }

        return result;
    }

    /// <summary>Whether this run was asked for a capture or a measurement rather than for a person.</summary>
    private bool IsHeadless => _launch.ShotPath is not null || _launch.MeasureSeconds is not null;

    /// <summary>Ends a headless run whose command-line demo cannot be opened: stderr, exit 1, close.</summary>
    /// <param name="reason">What went wrong, naming the file.</param>
    /// <remarks>
    /// **The load failed in milliseconds and `--shot` then waited out its patience** — tens of thousands
    /// of empty frames, minutes at 60 fps — before exiting ZERO with nothing written, and `--measure`
    /// never ended at all, since no playback ever starts. Both wait on a demo; a load that has already
    /// failed is not one that might still arrive. A person's window just shows the status line.
    /// </remarks>
    private void FailHeadless(string reason)
    {
        if (!IsHeadless)
        {
            return;
        }

        _log.LogError("{Message}", $"{reason}; the headless run stops");
        _host.Error.WriteLine(reason);
        _host.SetExitCode(1);
        _opening.TakeShotPath();
        _window.Close(posted: true);
    }
}
