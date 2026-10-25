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
    /// <summary>Where maps come from: the disk first, then the network.</summary>
    /// <remarks>
    /// **Replaced a `MapDownloader?` created on first need** (B188, D90). The lazy construction is
    /// still there and is still right — a viewer that never meets a missing map should never open an
    /// `HttpClient` — but it belongs to the thing that owns the download, not to the window that
    /// happens to notice a map is absent.
    /// </remarks>
    private readonly MapProvider _maps;

    /// <summary>Cancelled when the window shuts down, so background work stops (B402).</summary>
    /// <remarks>
    /// **A fetch outlived the window and killed the process on exit.** `DownloadMapAsync` is
    /// fire-and-forget by design — a 40 MB download must not freeze the window — and it resumed on
    /// the UI thread to set `_status.Text` and build Direct3D geometry. A `--shot` run closes about
    /// a second in, long before a download finishes, so the continuation ran against a disposed
    /// form and a released device: exit code `0xC000041D`, a fatal callback exception, with an
    /// empty standard error because the crash is inside the message loop.
    ///
    /// **CI had been red on it for four runs** and the capture test reported it exactly right —
    /// "the viewer did not exit cleanly" with a written PNG and a clean shutdown line in the log
    /// above it. Nobody read the annotations.
    /// </remarks>
    private readonly CancellationTokenSource _shutdown = new();

    /// <summary>Samples the demo for a moment and hands it to the moment scene, which the composite presenter holds.</summary>
    /// <remarks>
    /// **The last non-view work in <c>ShowMoment</c>** (B188, D90). The owner's question is what
    /// found it — *"does the view need to hold them to pass them on?"* — and it did not: the two
    /// scene buffers were fields of this form purely because the sampling happened here.
    /// </remarks>
    private readonly MomentPresenter _moments;

    /// <summary>Owns playback: what the transport controls mean, and where the demo has got to.</summary>
    /// <remarks>
    /// **Playback rides the idle loop rather than a timer of its own.** A first version used a
    /// 15 ms timer that invalidated the viewport on every firing - and the viewer already redraws
    /// on every idle, so paint messages never drained and the mouse went sluggish over the very
    /// buttons that had just been added. The comment beside Application.Idle warned about exactly
    /// this: a timer would keep presenting underneath it.
    ///
    /// Advancing in the idle loop is also closer to what an engine does: a frame takes however long
    /// it takes, and the clock is told how long that was. That is why the presenter exposes
    /// <see cref="PlaybackPresenter.Advance"/> for the host to call rather than running a clock of
    /// its own — the host owns the frame, and the presenter owns what a frame MEANS.
    ///
    /// **The rules it carries used to be four methods and a stopwatch in this file (D62)** and could
    /// not be tested here at all: reaching them needed a form and a message pump. They now have
    /// sixteen tests that run in milliseconds with no window.
    /// </remarks>
    private readonly PlaybackPresenter _playback;

    /// <summary>The ticks a script asked captured while playing, or null; see <see cref="PlayedCaptures"/>.</summary>
    private readonly PlayedCaptures? _playedCaptures;

    /// <summary>Where a second of frames went, counted and reported once a second.</summary>
    /// <remarks>
    /// **Was nine fields and two methods in this window** (B188, D90): the frame count, the longest
    /// frame, three phase totals and the idle-burst count went to <see cref="FrameLedger"/> on
    /// 2026-08-25; the one-second clock, the collection tuple, `CountFrame` and `GarbageThisSecond`
    /// followed on 2026-08-26. Counters that must all be cleared together are chances to forget one,
    /// and a counter that survives its report makes the NEXT second read as worse than it was.
    ///
    /// **The window holds the reporter and not the ledger**, which is deliberate. `Yielded` and
    /// `Drawing` are reported from two other places in the frame, so a window holding the ledger
    /// directly would be a second holder of the same accumulator — the arrangement that let two
    /// assignments sit unnoticed for a day (B196). The ledger is a constructor local now.
    ///
    /// **Not <see cref="FpsMeter"/>, which is `cl_showfps`** — a smoothed average drawn on screen
    /// for a person watching. This is a diagnostic account written to the log, and its value is the
    /// breakdown rather than the number: B191 was found by reading which column stayed fat as the
    /// others were measured away.
    /// </remarks>
    private readonly FrameReporter _frames;

    /// <summary>Which camera the viewport is drawn through, and every decision about changing it (D214).</summary>
    private readonly CameraPresenter _camera;

    /// <summary>Raises or lowers what the log accepts, wired by the composition root.</summary>
    /// <remarks>
    /// **A delegate rather than the provider itself**, because the form has no business holding a
    /// sink: it knows the setting, `Program` knows the provider, and this is the seam between them.
    /// Null in every test, where there is no file to write to and nothing to turn down.
    ///
    /// Hidden from the designer (WFO1000): a public property on a Form is otherwise assumed to be
    /// something the designer should serialise, and a delegate is not.
    /// </remarks>
    public Action<LogLevel>? SetLogVerbosity { get; set; }

    /// <summary>Reads the file log's own write timing, every thread, for <c>--measure</c> (B595); <c>null</c> when nothing logs to a file.</summary>
    public Func<Tf2DemoSalvage.Logging.LogWriteTiming>? LogTiming { get; set; }

    /// <summary>The file log's queue — lines dropped and high water — as the writer words it, for <c>--measure</c> (D230).</summary>
    public Func<string>? LogQueue { get; set; }

    /// <summary>Applies <c>developer</c> to the log, if anything is listening.</summary>
    /// <remarks>
    /// Called at startup and whenever the setting changes. 0 is the ordinary log, 1 admits the
    /// per-frame detail, 2 admits everything.
    /// </remarks>
    public void ApplyLogVerbosity()
    {
        // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
        if (SetLogVerbosity is not { } set)
        {
            return;
        }

        // **`ViewerSettings.Verbosity` rather than a switch here** (B208). The mapping was stated in
        // `Developer`'s own documentation over in `Scene` and implemented here — the same rule
        // written twice, in two projects, with only one of them running.
        LogLevel level = _settingsOwner.Verbosity;

        set(level);

        // Stryker disable once all : a mutated interpolated string cannot bind to its handler (CS1620) and Safe Mode drops the method - B410
        _log.LogInformation(
            "{Message}",
            string.Create(
                CultureInfo.InvariantCulture,
                $"developer {_settingsOwner.Developer}; the log accepts {level} and above"));
    }

    /// <summary>The settings' owner; this window talks to it in plain values (D214).</summary>
    private readonly SettingsPresenter _settingsOwner;

    /// <summary>Environment variable that starts playback as soon as a demo is loaded.</summary>
    /// <remarks>
    /// For measurement runs. A demo's first tick is before the match starts, so a viewer launched
    /// and logged without this one draws a scene with no capture points, no holograms and no
    /// carried weapons in it — and every question about those models comes back "never drawn".
    ///
    /// **Prefer `--autoplay`, which does the same thing and can be tested** (D118). This variable
    /// had exactly one reference in the repository — this declaration — so nothing set it and
    /// nothing asserted it, and its ordering requirement broke three times unnoticed (B223). It is
    /// kept because a shell somewhere may already export it, and dropping it would be a regression
    /// with no visible symptom, which is the same class of fault.
    /// </remarks>
    public const string AutoPlayVariable = "TF2VIEW_AUTOPLAY";

    /// <summary>The load in flight, kept so it is observed rather than discarded.</summary>
    /// <remarks>
    /// **The alternative was `async void` on the event handler, and the owner ruled it out** —
    /// *"we dont async void, we do pass back, at least just pass a sucess or fail message"*. He is
    /// right, and the reason bites here specifically: an `async void` load throws on a thread with
    /// no handler, so a demo that fails to open takes the process down instead of writing a line in
    /// the status bar.
    ///
    /// So the task is held. It is what the UI tests await instead of watching the log, and it is
    /// what a later "open the next demo in the playlist" would chain from.
    /// </remarks>
    public Task<DemoLoadResult> Loading { get; private set; } = NothingSelected;

    /// <summary>The result for a request that never named a demo.</summary>
    private static readonly Task<DemoLoadResult> NothingSelected =
        Task.FromResult(new DemoLoadResult(DemoLoadOutcome.Superseded, "No demo is selected."));

    /// <summary>Everything a frame makes around its models: lights, decals, sounds, particles, the target ID (D92, D214).</summary>
    private readonly FramePresenter _effects;

    /// <summary>Says how long the program took to put this window on screen, from the process starting (D182).</summary>
    /// <param name="elapsed">Process start to this window's first <c>Shown</c>.</param>
    /// <remarks>
    /// **The owner's reason for a splash was really this number**: *"I just want to see how long our boot time is, and be able to
    /// notice if it becomes crazy long."* Always logged; shown in the status bar too unless a demo is already opening, whose own
    /// status matters more.
    /// </remarks>
    public void ReportStartup(TimeSpan elapsed)
    {
        // Stryker disable once all : a mutated interpolated string cannot bind to its handler (CS1620) and Safe Mode drops the method - B410
        string started = string.Create(CultureInfo.InvariantCulture, $"Started in {elapsed.TotalSeconds:F2} s.");

        _log.LogInformation("{Message}", $"startup: window shown {elapsed.TotalMilliseconds:F0} ms after the process started");

        if (_presenter.LoadsInFlight == 0 && !_presenter.HasDemo)
        {
            _window.Status = $"{_window.Status} {started}";
        }
    }

    /// <inheritdoc/>
    void ICameraView.Say(string? status) => _window.Status = status;

    /// <inheritdoc/>
    void ICameraView.CameraChanged()
    {
        _presenter.InvalidateWorld();
        _window.Redraw();
    }

    /// <inheritdoc/>
    void ICameraView.ReleaseHeldKeys() => ReleaseHeldKeys();

    /// <inheritdoc/>
    void ICameraView.FailHeadless(string reason) => FailHeadless(reason);

    /// <inheritdoc/>
    T ILoadView.OnUi<T>(Func<T> work) => _window.OnUi(work);

    /// <inheritdoc/>
    string ILoadView.Status
    {
        get => _window.Status ?? string.Empty;
        set => _window.Status = value;
    }

    /// <inheritdoc/>
    void ILoadView.ReportLoading(string stage, double? fraction) => _window.ReportLoading(stage, fraction);

    /// <inheritdoc/>
    void ILoadView.HideLoading() => _window.HideLoading();

    /// <inheritdoc/>
    (bool Play, string Reason) ILoadView.AutoPlay =>
        (_launch.AutoPlay || _host.Environment(AutoPlayVariable) is { Length: > 0 },
            _launch.AutoPlay ? "--autoplay" : $"{AutoPlayVariable} is set");

    /// <inheritdoc/>
    void ILoadView.ClearWorld()
    {
        _device?.ClearWorld();
    }

    /// <inheritdoc/>
    void ILoadView.MapRead() => ProjectMap();

    /// <summary>Projects the map: whether it needs uploading and what follows an upload are the load owner's; this window's part is its device, the surface-colour toggle and the viewport's size.</summary>
    private void ProjectMap() =>
        _presenter.ProjectMap(_surfaceColours, _window.Viewport);

    /// <inheritdoc/>
    void ILoadView.DemoOpened()
    {
        if (_launch.PlaybackSpeed is { } speed)
        {
            _transport.SetSpeed(speed);
        }
    }

    /// <inheritdoc/>
    void ILoadView.DemoShown()
    {
        // **Restart the settling countdown, rather than applying the opening state here.** A demo
        // opened from the playlist arrives long after the frame the countdown used to fire on, so
        // the state was being lost — but applying it at this instant is worse than useless: the
        // world has not settled, the textures are uploaded on a later frame, and the seek lands in a
        // scene that is not ready and then latches itself done.
        //
        // Restarting keeps the original reasoning intact — the countdown exists so the map, its
        // textures and the entity models are all in place first — and simply measures it from the
        // demo rather than from the window.
        if (!_opening.Applied)
        {
            _opening.Restart();
        }
    }

    /// <summary>
    /// A client ConVar's value: this viewer's settings file or a <c>+name value</c> launch option, which run last as
    /// Source's command-line cvars do, else the watcher's TF2 configs; null for the SDK default.
    /// </summary>
    private string? ClientConVar(string name) =>
        _settingsOwner.ConVar(name) ?? _console.ConVar(name);

    /// <summary>Where the picker looks for HUDs: this program's OWN <c>custom/</c> folder, never `tf/custom` (D193).</summary>
    private static string ProgramCustomFolder { get; } =
        System.IO.Path.Combine(AppContext.BaseDirectory, "custom");
}
