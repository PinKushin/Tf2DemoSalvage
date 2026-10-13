using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Render;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Presentation;

/// <summary>
/// One viewer, without its window: what a frame, a key, a capture, a measurement and a startup mean (D214, slice 9).
/// </summary>
/// <remarks>
/// **Everything the window decided that was not about a control.** Slices 1 to 8 took every domain type out of the view
/// and left the instrument reading zero; the view still held the idle loop, the frame's steps, the opening sequence,
/// the capture gates, the measurement, the key dispatch and the render switches — written with plain values and
/// Presentation's own types, so the reach count could not see them. They are here, moved and not rewritten, and the
/// window is an <see cref="IViewerWindow"/> and an <see cref="IPlaybackView"/>.
///
/// It is the load's view and the camera's view as well (<see cref="ILoadView"/>, <see cref="ICameraView"/>): what
/// those two ask was half control work and half decision, and the halves are split here — the status line and the
/// loading display go on to the window, and what a demo opening means for the playback speed or the capture countdown
/// stays.
/// </remarks>
public sealed partial class ViewerSession : IFrameSteps, ILoadView, ICameraView, IDisposable
{
    /// <summary>The window: controls, and nothing decided.</summary>
    private readonly IViewerWindow _window;

    /// <summary>The playback controls.</summary>
    private readonly IPlaybackView _transport;

    /// <summary>The process: its streams, exit code, environment and clocks.</summary>
    private readonly ViewerHost _host;

    /// <summary>The factory, kept so the pieces this builds get their own categories (D83).</summary>
    private readonly ILoggerFactory _loggers;

    /// <summary>One logger per area; a logger's category is exactly the area string the log file shows.</summary>
    private readonly ILogger _log;

    private readonly ILogger _renderLog;

    private readonly ILogger _demoLog;

    private readonly ILogger _configLog;

    /// <summary>Owns what a load fills and the load itself; this session is its <see cref="ILoadView"/> (D214).</summary>
    private readonly ViewerPresenter _presenter;

    /// <summary>The composite presenter, for the window's forwarding and the tests that ask what a load left.</summary>
    public ViewerPresenter Presenter => _presenter;

    /// <summary>Where the playback clock stands, fraction included; null with no demo. The picture is re-read from it.</summary>
    public double? PlaybackPosition => _playback.Position;

    /// <summary>The settings' owner, for the window's menu.</summary>
    public SettingsPresenter Settings => _settingsOwner;

    /// <summary>The bindings in force, for the window's menu.</summary>
    public KeyBindings Bindings => _bindings;

    /// <summary>What the menu shows as chosen when it is built.</summary>
    public MenuState Menu => _settingsOwner.Menu(ProgramCustomFolder);

    /// <summary>The device's switches, the capture and the mode change: null until a device is created.</summary>
    private IRenderDevice? _device;

    /// <summary>The device itself, which this session disposes; null in a test that attached a stand-in.</summary>
    private Device3D? _ownedDevice;

    /// <summary>Whether a Direct3D device has been created for the viewport.</summary>
    public bool HasDevice => _device is not null;

    /// <summary>The paths the command line named, with its options taken out.</summary>
    public IReadOnlyList<string> Paths { get; }

    /// <summary>What a headless run that was given no single demo says when the window is up; null otherwise.</summary>
    private string? _failOnShow;

    /// <summary>The log line's start that says where the game folder was found at startup.</summary>
    public const string GameFolderLog = "game folder at startup: ";

    /// <summary>What that line says when discovery found nothing.</summary>
    public const string GameFolderNotFound = "not found";

    /// <summary>Opens a session on the real process, the installed maps and the saved settings, and its audio device.</summary>
    /// <typeparam name="TGdi">The window's GDI, as VGUI's fonts reach it.</typeparam>
    /// <param name="window">The window.</param>
    /// <param name="transport">Its playback controls.</param>
    /// <param name="loggers">Where the viewer reports what it did (D83).</param>
    /// <param name="arguments">The command line.</param>
    /// <param name="gdi">The window's GDI.</param>
    /// <returns>The session.</returns>
    public static ViewerSession Open<TGdi>(
        IViewerWindow window, IPlaybackView transport, ILoggerFactory loggers, IReadOnlyList<string> arguments, TGdi gdi)
        where TGdi : IVguiGdi
    {
        ArgumentNullException.ThrowIfNull(loggers);

        ViewerSession session = new(
            window, transport, loggers, arguments, gdi, ViewerHost.Process, MapProvider.Installed(), new SettingsPresenter(loggers));

        session._presenter.OpenAudio();

        return session;
    }

    /// <summary>Builds a session over the pieces given, opening no audio device.</summary>
    /// <param name="window">The window.</param>
    /// <param name="transport">Its playback controls.</param>
    /// <param name="loggers">Where the viewer reports what it did.</param>
    /// <param name="arguments">The command line.</param>
    /// <param name="gdi">The window's GDI.</param>
    /// <param name="host">The process.</param>
    /// <param name="maps">Where maps come from; the session disposes it.</param>
    /// <param name="settings">The settings' owner.</param>
    internal ViewerSession(
        IViewerWindow window,
        IPlaybackView transport,
        ILoggerFactory loggers,
        IReadOnlyList<string> arguments,
        IVguiGdi gdi,
        ViewerHost host,
        MapProvider maps,
        SettingsPresenter settings)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(loggers);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(maps);
        ArgumentNullException.ThrowIfNull(settings);

        // **Eight areas, so a factory rather than a logger.** Each keeps the category ViewerLog used as its area
        // string, so the log format is unchanged.
        _loggers = loggers;
        _log = loggers.CreateLogger("viewer");
        _renderLog = loggers.CreateLogger("render");
        _demoLog = loggers.CreateLogger("demo");
        _configLog = loggers.CreateLogger("config");

        _window = window;
        _transport = transport;
        _host = host;
        _maps = maps;
        _clock = new FrameClock(host.Clock(), host.Clock());

        // **Playback belongs to a presenter (D62).** What used to be three event handlers and a stopwatch in the
        // window — scrub, play/pause, speed, each with a rule about when to restart the watch — is
        // `PlaybackPresenter`, which knows nothing about a toolkit.
        _playback = new PlaybackPresenter(transport, host.Clock());

        // **What a load fills is composed by its owner** (D214), in the order the window's constructor built them.
        _settingsOwner = settings;
        _presenter = new ViewerPresenter(
            this, loggers, this, () => CameraWindowNow, () => _launch.SpectateWho, _maps, _settingsOwner, _playback, ClientConVar, _shutdown.Token);
        _moments = _presenter.Moments;
        _effects = _presenter.Effects;
        _camera = _presenter.Camera;

        // What the presenter asks of the frame at its own moment, attached once; it gives the HUD the rest (D214).
        _presenter.AttachWindow(
            new ViewerWindow(
                () => WindowFrameNow,
                ShowOnly,
                () => _bindings,
                ClientConVar,
                () => _vguiClock.Elapsed.TotalSeconds,
                () => _console.IsHeld(ViewerAction.ShowScores),
                () => _clock.LastFrameSeconds),
            gdi);

        _frames = _presenter.Frames;

        // What the options say of the settings, the game folder, the spectator, the lighting and the HUD is applied by
        // the presenter that owns each (`ViewerPresenter.ReadLaunch`).
        _launch = _presenter.ReadLaunch(arguments);

        // **`--look` and `--zoom` were parsed and read by nobody** (B226). The overhead placement is the map view's
        // successor, so they reshape what it frames.
        _camera.Free.LookAt = _launch.LookAt;
        _camera.Free.Zoom = _launch.Zoom;

        // **This line was MISSING for a day and `--shot` did nothing at all** (B196): the shot path stays out of the
        // launch record because taking the shot CONSUMES it, and staying out of the record is how it stopped being
        // assigned. No test passed `--shot`, so the whole option was covered by nobody.
        _opening = new OpeningSequence(
            _launch.ShotPath, OpeningFrames, SettleFrames, PatienceFrames);

        Paths = [.. _launch.Paths];

        // **The player's own TF2 controls, loaded before anything can be pressed (D69/D70).** Without it the
        // interpreter runs the shipped defaults for ever and every test of it exercises code the viewer never reaches.
        LoadUserConfig();
        _console.Triggered += OnConsoleAction;

        _playback.MomentChanged += (_, moment) =>
        {
            // **The tick drives the picture.** Scrubbing and playing both arrive here, so the
            // viewer has one path from "which moment" to "who is where" rather than two that can
            // disagree.
            if (!_presenter.HasTimeline)
            {
                return;
            }

            ShowMoment(moment.Position);
            _window.Redraw();
        };
    }

    /// <summary>Decides what the command line's paths mean, once the window has listed them, and says which.</summary>
    /// <remarks>
    /// **One file named on the command line is an instruction to open THAT demo.** This is the file-association case:
    /// double-clicking a .dem in Explorer has to end with the demo on screen. A folder is deliberately excluded:
    /// opening a folder means "here is a playlist", and picking one of its demos to start playing would be guessing
    /// which.
    ///
    /// **Say which branch was taken** (B401). A `--shot` run that quietly did not open its demo looked identical to
    /// one whose renderer drew nothing: no `[demo]` line at all is the same absence for "we chose not to open it" and
    /// "opening it failed".
    ///
    /// **Opened once the window is up** (<see cref="WindowShown"/>), not here: loading it before kept the window off
    /// screen for the whole decode and map read — 95 seconds on a 26-minute match.
    /// </remarks>
    public void PlanStartup()
    {
        if (Paths.Count == 0)
        {
            return;
        }

        if (Paths.Count == 1 && File.Exists(Paths[0]))
        {
            _openOnShow = Paths[0];
        }
        else if (Paths.Count == 1)
        {
            string missing = $"{Paths[0]} is not a file that exists; nothing was opened";
            _demoLog.LogWarning("{Message}", missing);
            _failOnShow = missing;
        }
        else
        {
            _demoLog.LogInformation(
                "{Message}",
                $"{Paths.Count} paths listed in the playlist; none opened on its own");

            // **A capture or a measurement has exactly one subject** (2026-10-11). A demo under a folder with a
            // space in its name, launched through something that joins its arguments unquoted, arrives as its
            // pieces: "a playlist", nothing opened, and `--shot` then drew 30,000 empty frames and exited zero.
            _failOnShow =
                $"{Paths.Count} paths were given where a capture takes one demo " +
                $"({string.Join(" | ", Paths)}); a path with a space in it must arrive as ONE argument";
        }
    }

    /// <summary>The window is up: opens the demo the command line named, or ends a headless run that has none.</summary>
    public void WindowShown()
    {
        if (_openOnShow is not null)
        {
            Loading = OpenCommandLineDemo();
        }
        else if (_failOnShow is { } reason)
        {
            FailHeadless(reason);
        }
    }

    /// <summary>The playlist's owner: the library, its order and the rows a search leaves (D214).</summary>
    private readonly PlaylistPresenter _library = new();

    /// <summary>Adds files and folders to the library, shows the list they leave and says how many demos there are.</summary>
    /// <param name="search">What the search box holds.</param>
    /// <param name="paths">The files and folders.</param>
    public void AddToLibrary(string search, params string[] paths)
    {
        // Opening, ordering, filtering and the count's words are the playlist owner's (`PlaylistPresenter.Add`).
        string count = _library.Add(search, paths);

        _window.ShowPlaylist(_library.Count);

        _window.Status = count;
    }

    /// <summary>Narrows the playlist to what a search leaves and shows it.</summary>
    /// <param name="search">What the search box holds.</param>
    public void FilterLibrary(string search)
    {
        _library.Filter(search);

        _window.ShowPlaylist(_library.Count);
    }

    /// <summary>One row of the playlist as it is shown now, or null past its end.</summary>
    /// <param name="index">The row.</param>
    /// <returns>Its name, folder and path.</returns>
    public PlaylistRow? PlaylistRow(int index) => _library.Row(index);

    /// <summary>Loads a demo a playlist row names, if one is selected.</summary>
    /// <param name="path">The selected row's path, or null when nothing is selected.</param>
    /// <returns>The load, or the nothing-selected result.</returns>
    public Task<DemoLoadResult> LoadSelected(string? path) =>
        path is null ? NothingSelected : _presenter.LoadDemoAsync(path);

    /// <summary>A playlist row was activated: loads it and keeps the load in <see cref="Loading"/>.</summary>
    /// <param name="path">The row's path, or null when nothing is selected.</param>
    public void Activate(string? path) => Loading = LoadSelected(path);

    /// <summary>Whether the startup prompt for a missing install should open, saying what discovery found.</summary>
    /// <returns>Whether to ask.</returns>
    /// <remarks>
    /// Never on a headless run (<c>--shot</c>, <c>--measure</c>): a modal dialog nobody sees would hang
    /// it. The UI suite passes <c>+cl_game_folder_ask 0</c>, because CI has no TF2.
    /// </remarks>
    public bool ShouldAskForGameFolder()
    {
        bool headless = IsHeadless;
        string? found = _maps.GameFolder();

        // Logged before any dialog: the UI test's control that discovery really failed.
        _log.LogInformation("{Message}", GameFolderLog + (found ?? GameFolderNotFound));

        return _settingsOwner.ShouldAskForGameFolder(found, headless);
    }

    /// <summary>The startup prompt was cancelled: stops it asking and says so.</summary>
    public void GameFolderDeclined() => _window.Status = _settingsOwner.DeclineGameFolder();

    /// <summary>A folder was picked as the game folder: validates it, remembers it, says which, and reads its configs.</summary>
    /// <param name="selected">The folder picked.</param>
    public void GameFolderChosen(string selected)
    {
        // Whether the folder is a TF2 folder, remembering it and the words for both outcomes are the settings owner's
        // (`SettingsPresenter.ChooseGameFolder`), which logs them; the window shows them.
        bool installOpen = _presenter.HasInstall;
        GameFolderChoice choice = _settingsOwner.ChooseGameFolder(selected, installOpen);

        _window.Status = choice.Status;

        if (!choice.Accepted)
        {
            return;
        }

        // Nothing has opened the archives yet, so the player's own config can be read from the new folder now.
        // The menu was built from the bindings before this read, so its shortcuts are redone from the new ones.
        if (!installOpen)
        {
            LoadUserConfig();
            _window.ShowBindings(_bindings);
        }
    }

    /// <summary>The export or compile in flight, held rather than <c>async void</c> (see <see cref="Loading"/>).</summary>
    public Task<string> FileWork { get; private set; } = Task.FromResult(string.Empty);

    /// <summary>Export was pressed: the file name to offer, or null — with the status saying why — when no demo is open.</summary>
    /// <returns>The suggested file name.</returns>
    public string? OfferExport()
    {
        // Before anything that can return early, so a press that did nothing is visible.
        _log.LogInformation("{Message}", $"export: pressed with {_presenter.DemoPath ?? "no demo open"}");

        // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
        if (_presenter.DemoPath is not { } demoPath)
        {
            _window.Status = "Open a demo first.";
            FileWork = Task.FromResult("Open a demo first.");

            return null;
        }

        return Path.GetFileNameWithoutExtension(demoPath) + ".txt";
    }

    /// <summary>Writes the open demo's assembly text — the CLI's <c>--asm</c> — to the file picked.</summary>
    /// <param name="output">Where to write it.</param>
    public void Export(string output)
    {
        if (_presenter.DemoPath is not { } demoPath)
        {
            return;
        }

        FileWork = RunFileWork($"Exporting {Path.GetFileName(demoPath)}...", output, written =>
            ViewerPresenter.ExportDemo(demoPath, written));
    }

    /// <summary>The file name to offer for a compiled demo.</summary>
    /// <param name="source">The assembly text picked.</param>
    /// <returns>Its name with a demo's extension.</returns>
    public static string OfferCompile(string source) => Path.GetFileNameWithoutExtension(source) + ".dem";

    /// <summary>Compiles an assembly text file back into a byte-identical demo — the CLI's <c>--compile</c>.</summary>
    /// <param name="source">The assembly text.</param>
    /// <param name="output">Where to write the demo.</param>
    public void Compile(string source, string output) =>
        FileWork = RunFileWork($"Compiling {Path.GetFileName(source)}...", output, written =>
            ViewerPresenter.CompileDemo(source, written));

    /// <summary>Runs an export or compile off the UI thread and puts its outcome in the status bar.</summary>
    private async Task<string> RunFileWork(string starting, string output, Func<string, string> work)
    {
        _window.Status = starting;
        string result;

        try
        {
            // Not cancelled on shutdown: a half-written file is worse than finishing this one.
            result = await Task.Run(() => work(output), CancellationToken.None).ConfigureAwait(true);
            _log.LogInformation("{Message}", result);
        }
        catch (Exception failure) when (
            failure is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            // Expected: a file that is missing, locked, or not assembly. Anything else is a defect
            // and propagates through FileWork with its stack trace.
            result = $"Failed: {failure.Message}";
            _log.LogError(failure, "{Message}", result);
        }

        _window.Status = result;
        return result;
    }

    /// <summary>Creates the Direct3D device for the viewport, hands it its settings and projects a map already read.</summary>
    /// <param name="handle">The viewport's window handle.</param>
    /// <param name="width">Its width in pixels.</param>
    /// <param name="height">Its height in pixels.</param>
    /// <returns>Whether there is a device, so frames can be drawn.</returns>
    public bool CreateDevice(nint handle, int width, int height) =>
        OpenDevice(
            () =>
            {
                _ownedDevice = Device3D.Create(handle, width, height, _loggers);

                // The hand-over (D214): the presenter gives the device every setting a new one takes, and tells the
                // scene where packed geometry goes (B193). The two settings that go through the menu come back as
                // these two calls: unticking Reflections is what calls `SetSpecular`, and the check marks follow
                // `SetFullbright`.
                _presenter.AttachDevice(_ownedDevice, () => _window.ShowSpecular(false), mode => SetFullbright((Fullbright)mode));

                return _ownedDevice;
            },
            width,
            height);

    /// <summary>The device's creation with the creating itself handed in, so its failure can be met without a GPU.</summary>
    /// <param name="create">Creates and attaches the device.</param>
    /// <param name="width">The viewport's width in pixels.</param>
    /// <param name="height">Its height in pixels.</param>
    /// <returns>Whether there is a device.</returns>
    internal bool OpenDevice(Func<IRenderDevice> create, int width, int height)
    {
        // A failure here is a machine or driver problem, not a bug in the demo being viewed, so
        // it is reported in the status line rather than crashing the shell - the rest of the
        // application (opening files, reading a trace) still works without a device.
        try
        {
            _device = create();

            _renderLog.LogInformation(
                "{Message}",
                _settingsOwner.FrameRateLine);
            _renderLog.LogInformation(
                "{Message}",
                $"device created for a {width}x{height} viewport");

            // **Only if there is nothing better to say.** The handle is created after the
            // constructor runs, so a demo opened from the command line has already reported itself
            // by now - and announcing the graphics device over the top of it threw that away. The
            // user saw "Direct3D ready." for a demo that had loaded fine, which reads like the
            // demo did not load.
            if (!_presenter.HasDemo)
            {
                _window.Status = "Direct3D ready.";
            }

            // **And project again, because a map may already be loaded.** The same ordering that
            // made the status line wrong: a demo opened from the command line loads its map in the
            // constructor, before this handle exists, so the upload of the textured world was
            // skipped for want of a device and nothing re-ran it. The map drew as an outline and
            // looked exactly like a renderer that had not been wired up.
            ProjectMap();

            return true;
        }
        catch (Exception failure) when (failure is InvalidOperationException or ArgumentException)
        {
            _window.Status = "Direct3D unavailable: " + failure.Message;
            _renderLog.LogWarning(failure, "{Message}", "creating the Direct3D device");

            return false;
        }
    }

    /// <summary>The viewport changed size: the projected world is stale and the device's buffers follow.</summary>
    public void ViewportResized()
    {
        _presenter.InvalidateWorld();

        (int width, int height) = _window.Viewport;

        if (_device is null || width <= 0 || height <= 0)
        {
            return;
        }

        _device.Resize(width, height);
    }

    /// <summary>Full screen is about to change: starts the clock the first frame after it stops.</summary>
    public void FullScreenChanging() => _fullScreenClock = Stopwatch.StartNew();

    /// <summary>The window has gone full screen: takes the display for itself when that is the mode chosen, and says so when refused.</summary>
    public void EnteredFullScreen()
    {
        if (_settingsOwner.FullScreenExclusive &&
            _device is not null &&
            !_device.SetExclusiveFullScreen(true))
        {
            _window.Status = SettingsPresenter.ExclusiveFullScreenRefused;
        }
    }

    /// <summary>The window is leaving full screen: gives the display back.</summary>
    public void LeavingFullScreen() => _device?.SetExclusiveFullScreen(false);

    /// <summary>A mouse button went down over the viewport.</summary>
    /// <param name="button">Its Source name: <c>MOUSE1</c>.</param>
    /// <param name="drags">Whether it is the button that drags the camera.</param>
    /// <param name="x">Where, in viewport pixels.</param>
    /// <param name="y">Where, in viewport pixels.</param>
    public void PointerDown(string button, bool drags, int x, int y)
    {
        if (drags)
        {
            _dragFrom = (x, y);
        }

        _console.KeyDown(button);
    }

    /// <summary>The pointer moved over the viewport: turns the camera by how far, while a drag is on.</summary>
    /// <param name="x">Where, in viewport pixels.</param>
    /// <param name="y">Where, in viewport pixels.</param>
    public void PointerMoved(int x, int y)
    {
        // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
        if (_dragFrom is not { } from || !_camera.Drag(x - from.X, y - from.Y))
        {
            return;
        }

        _dragFrom = (x, y);
        _presenter.InvalidateWorld();
    }

    /// <summary>A mouse button came up.</summary>
    /// <param name="button">Its Source name.</param>
    public void PointerUp(string button)
    {
        _dragFrom = null;
        _console.KeyUp(button);
    }

    /// <summary>The wheel turned over the viewport.</summary>
    /// <param name="forward">Whether it turned away from the user.</param>
    public void Wheel(bool forward) => _camera.Wheel(forward);

    /// <summary>A key came up, wherever the focus is.</summary>
    /// <param name="key">Its Source name.</param>
    public void KeyUp(string key) => _console.KeyUp(key);

    /// <summary>Shutdown's first step: tells background work to stop, and releases the corpse pass's source.</summary>
    /// <remarks>
    /// **First, before anything it might touch is released** (B402). A map fetch in flight resumes on the UI thread
    /// to set the status and build geometry; cancelling here is what makes its continuation return instead of writing
    /// into a disposed window. The corpse pass is linked to the same token and has just been told to stop; its source
    /// is ours to release (D181).
    /// </remarks>
    public void StopBackgroundWork()
    {
        if (_stopped)
        {
            return;
        }

        _stopped = true;
        _shutdown.Cancel();
        _presenter.ReleaseCorpseRecord();
    }

    /// <summary>Shutdown's second step, once nothing can ask for a frame: the device, then what drew through it.</summary>
    public void ReleaseDevice()
    {
        if (_deviceReleased)
        {
            return;
        }

        _deviceReleased = true;

        // Dropped before the device is disposed, so nothing can hand geometry to a dead one.
        _presenter.DetachDevice();

        _ownedDevice?.Dispose();
        _ownedDevice = null;
        _device = null;

        // Before the caller's log line, so a device that hangs on close is visible as a shutdown
        // that stalled here rather than as one that never reached the end.
        _presenter.Dispose();
    }

    /// <summary>Shutdown's last step here: the map provider and the shutdown token, each announced before it goes.</summary>
    /// <param name="releasing">Names the member about to be released, in the log (B402).</param>
    public void ReleaseServices(Action<string> releasing)
    {
        ArgumentNullException.ThrowIfNull(releasing);

        if (_servicesReleased)
        {
            return;
        }

        _servicesReleased = true;

        releasing("maps");
        _maps.Dispose();
        releasing("shutdown");
        _shutdown.Dispose();
    }

    private bool _stopped;

    private bool _deviceReleased;

    private bool _servicesReleased;

    /// <summary>The three shutdown steps in order, each once, for an owner with nothing to do between them.</summary>
    /// <remarks>The window calls the three itself, because its own teardown goes between them (B402).</remarks>
    public void Dispose()
    {
        StopBackgroundWork();
        ReleaseDevice();
        ReleaseServices(static _ => { });
    }
}
