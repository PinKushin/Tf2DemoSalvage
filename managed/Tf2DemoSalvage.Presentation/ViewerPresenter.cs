using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation;

/// <summary>
/// The viewer window's composite presenter (D92): it owns the frame presenter, the camera presenter, what a load fills —
/// the model set, the moment, the sound, the game systems — and the load itself: the decode, the map read behind the
/// render barrier, and the demo becoming the open one (D90, D214).
/// </summary>
/// <remarks>
/// **Was `LoadPresenter` for one slice.** It began as the owner of a load, then took the sound device, the camera
/// presenter and the draw's decisions, each because it already held what they read. D92 names the role — a composite
/// view gets a composite presenter that owns its sub-presenters — and the name now says it.
///
/// **Moved out of the window whole, in the order and on the threads it ran there** [no engine counterpart: the engine
/// loads a level on one thread behind a loading screen it draws itself; the worker and the barrier are this viewer's,
/// B146]. The window constructed these collaborators, kept the four things a load assigns (the demo, its timeline, the
/// install, the map) as its own fields, and ran the load from a click handler. It keeps what a window has: the dialog,
/// the progress display, the device, the camera — each asked for through <see cref="ILoadView"/> at the moment the old
/// code reached for it — and one call.
///
/// **Why one owner and not a second presenter beside <see cref="FramePresenter"/>:** a load tears down and fills what
/// the frame draws from, so two owners would each need the other. This one constructs the frame presenter, as that one
/// constructs its parts (D92: a presenter composes downward and never reaches sideways).
///
/// **The threads, because a move must not change them** (B146, B507):
/// the decode and the map read run on workers (`Task.Run`); everything that touches the window goes through
/// <see cref="ILoadView.OnUi{T}"/>; <see cref="ReadingMap"/> is raised on the UI thread before the map read and lowered
/// on it afterwards, and the render loop draws nothing in between.
/// </remarks>
public sealed partial class ViewerPresenter : IDisposable
{
    private readonly ILoadView _view;
    private readonly ILogger _log;
    private readonly ILogger _mapLog;
    private readonly ILogger _renderLog;
    private readonly ILogger _demoLog;
    private readonly ILogger _audioLog;
    private readonly SpectatorView _spectator;
    private readonly CameraPresenter _camera;
    private readonly ILoggerFactory _loggers;
    private readonly Func<CameraWindow> _window;
    private readonly Func<string?> _spectateWho;
    private readonly MapProvider _maps;
    private readonly SettingsPresenter _settingsOwner;
    private readonly CancellationToken _shutdown;

    /// <summary>The window's settings as they are now: the old code read the window's field at each use.</summary>
    private ViewerSettings _settings => _settingsOwner.Current;

    /// <summary>Entity models, packed once in model space and posed by the GPU.</summary>
    private readonly EntityModelSet _models;

    /// <summary>Assembles what one moment draws: the draw list, the packing, the poses.</summary>
    private readonly MomentScene _moment;

    /// <summary>Samples the demo for a moment and hands it to <see cref="_moment"/>.</summary>
    private readonly MomentPresenter _moments;

    /// <summary>Decides what should be audible at a tick.</summary>
    private readonly SoundPresenter _sound;

    /// <summary>The `snd_musicvolume` each sound system reads every pass, one-shots and loops then soundscapes (B545).</summary>
    internal (float Sounds, float Soundscapes) MusicVolumes => (_sound.MusicVolume(), _soundscape.MusicVolume());

    /// <summary>Everything a frame makes around its models (D92, D214).</summary>
    private readonly FramePresenter _effects;

    /// <summary>The looping sounds in flight, shared by the presenter and the soundscape.</summary>
    /// <remarks>
    /// **Still here only because two collaborators share it**, and the window is where they are
    /// both constructed. The schedule, the audible-crossing memory and Valve's MIN_AUDIBLE_VOLUME
    /// went with SoundPresenter on 2026-08-25 (B188) — they were its state, not a window's.
    /// </remarks>
    private readonly ActiveLoops _loops = new();

    /// <summary>The registered game systems, told about every level load and teardown.</summary>
    /// <remarks>
    /// **Valve's arrangement: a LIST walked at the level boundary** —
    /// `LevelInitPreEntityAllSystems( pMapName )` (`igamesystem.h:77`) — rather than one method
    /// reaching into six collaborators. That reaching is what let B193 and B196 drop assignments
    /// with every test still green, and what left teardown split across three places with one
    /// system torn down nowhere at all.
    ///
    /// **Three systems, and the two absences were checked rather than assumed.** Valve models the
    /// renderables builder as a game system (`IClientLeafSystem : … IGameSystemPerFrame`), and the
    /// soundscape and sound emitter likewise; it does NOT model model-geometry or the sample cache
    /// that way — `IVModelInfo` and `IEngineSound` are plain interfaces set up once. So those two
    /// are configured rather than walked.
    /// </remarks>
    private readonly LevelSystems _levels;

    /// <summary>The map's ambience, chosen and faded as the listener moves through it.</summary>
    /// <remarks>
    /// **A system rather than five fields and a method** (B188). The mixer, the voices the sink is
    /// holding, the choose timer, the fade clock and the two constants all moved with it — they were
    /// its state, not the window's, and nothing about any of them needed a window.
    ///
    /// Valve's own arrangement: <c>C_SoundscapeSystem : CBaseGameSystemPerFrame</c>
    /// (<c>c_soundscape.cpp:78</c>).
    /// </remarks>
    private readonly SoundscapeSystem _soundscape;

    /// <summary>Decoded sounds, kept because a demo plays the same footstep hundreds of times.</summary>
    /// <remarks>
    /// **The engine's sample cache, behind the engine's own interface shape.** <c>IEngineSound</c>
    /// carries <c>PrecacheSound</c>, <c>IsSoundPrecached</c> and <c>PrefetchSound</c> together
    /// (<c>IEngineSound.h:89-91</c>); game code asks rather than holding samples. The dictionary,
    /// the unopened count, the precache flag and <c>Sample</c> were all fields and methods here
    /// (B188, D90).
    /// </remarks>
    private readonly SoundCache _sounds;

    /// <summary>The systems told about a newly-opened demo, as `_levels` are told about a level.</summary>
    /// <remarks>
    /// **The demo mirror of <c>LevelSystems</c>, and ours rather than Valve's — checked, not
    /// assumed.** In the engine, playing a demo IS loading a level, so systems get `LevelInit*` and
    /// there is no separate event. It does not bind here because this viewer opens a demo BEFORE it
    /// knows whether the map exists, which the engine never faces: a client cannot play a demo whose
    /// map it lacks, and ours must (B201).
    /// </remarks>
    private readonly DemoSystems _demoSystems;

    /// <summary>What the installed game provides, opened once and reused for every map.</summary>
    /// <remarks>
    /// **`_archives`, `_classModels` and `_entityClasses` were three fields opened inside the first
    /// map read** (B188, D90). None of them is per-map: they are what the INSTALL supplies, and
    /// they sat there because that is where the first caller happened to be.
    ///
    /// Null until a map is read, because locating the install is the one thing that waits for a
    /// reason to happen.
    ///
    /// **Opening it stopped being this window's job on 2026-08-26** (B188, D90) —
    /// `LevelSystems.Install` owns the branch, the caching and the `OpenGame` call. What is left
    /// here is B208's arrangement and nothing else: the content is carried from the map read to the
    /// two precaches **as a value**, so a wrong order has nothing to pass. Every remaining use is
    /// either that assignment or one of those hand-offs; nothing asks it a question.
    ///
    /// **So this field is not a leftover, and the distinction matters** — the reason to keep it is
    /// the same reason B208 introduced it, and deleting it would restore the silent-ordering bug it
    /// was created to prevent.
    /// </remarks>
    private GameContent? _game;

    /// <summary>Each class model's gib list, read once per install — see where <c>Gibs</c> is set.</summary>
    private readonly Dictionary<string, IReadOnlyList<PhysicsBreakPiece>> _breakPieces =
        new(StringComparer.Ordinal);

    /// <summary>The install <see cref="_breakPieces"/> was read from, so a new one starts empty.</summary>
    private GameContent? _breakPiecesFor;

    /// <summary>The decoded demo, for the things a window still asks it.</summary>
    /// <remarks>
    /// **Still here, and worth saying why rather than leaving it implied.** The sampling left on
    /// 2026-08-26 (B188, D90) and took the two buffers with it, but four callers in this file still
    /// need the timeline itself — <c>EnsureWeaponRoles</c>, the model precache, the sound precache
    /// and <c>DemoSystems.Open</c>. Each of those hands it to something in Presentation or Scene; the
    /// window holds the reference and asks it nothing.
    /// </remarks>
    private DemoTimeline? _timeline;

    private LoadedDemo? _demo;

    /// <summary>The map, read and ready to draw, or null when none is open.</summary>
    /// <remarks>
    /// **Eleven fields until 2026-08-25** — the outline, the assets, the height range and the eight
    /// that were `MapLevel` unpacked. Reading a map is not window work and none of it happens here
    /// any more: what is left is handing the result to the systems that asked for it, which is the
    /// engine's `LevelInitPreEntity` shape (`igamesystem.h:39`).
    /// </remarks>
    private LoadedMap? _loaded;

    /// <summary>Shown when no TF2 installation could be found.</summary>
    /// <remarks>
    /// **It names the fix, not just the fault** (B211). "Map not found" was what this said before,
    /// which sent the reader looking for the wrong thing entirely — and the demo still plays without
    /// a map, so the sentence has to say that too or it reads as a refusal.
    /// </remarks>
    public const string NoGameInstalled =
        "No Team Fortress 2 installation found, so maps and models cannot be loaded. "
        + "The demo will still play. Install TF2 through Steam, or put maps in the viewer's own "
        + "maps folder.";

    /// <summary>Composes what a load fills. Nothing is read and no device is touched.</summary>
    /// <param name="view">The window: its thread, its status line, its progress display, its device and its camera.</param>
    /// <param name="loggers">The viewer's loggers, by area.</param>
    /// <param name="cameraView">The window as the camera sees it.</param>
    /// <param name="window">The window's tick and shape, asked for when a camera is.</param>
    /// <param name="spectateWho">`--spectate`'s text as launched, or null; asked for when a demo opens.</param>
    /// <param name="maps">Where maps and the install are found.</param>
    /// <param name="settings">The window's settings as they are when asked.</param>
    /// <param name="playback">Owns the transport and the clock; the demo's systems drive it.</param>
    /// <param name="clientConVars">The watcher's own ConVar values, or null where unset (D190).</param>
    /// <param name="shutdown">Cancelled when the window closes, so the workers do not come back to a disposed one (B402).</param>
    public ViewerPresenter(
        ILoadView view,
        ILoggerFactory loggers,
        ICameraView cameraView,
        Func<CameraWindow> window,
        Func<string?> spectateWho,
        MapProvider maps,
        SettingsPresenter settings,
        PlaybackPresenter playback,
        Func<string, string?> clientConVars,
        CancellationToken shutdown)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(loggers);
        ArgumentNullException.ThrowIfNull(spectateWho);
        ArgumentNullException.ThrowIfNull(maps);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(playback);
        ArgumentNullException.ThrowIfNull(clientConVars);

        _view = view;
        _spectateWho = spectateWho;
        _loggers = loggers;
        _window = window;
        _jumps = playback;
        _spectator = new SpectatorView(loggers.CreateLogger("spectate"));
        _camera = new CameraPresenter(cameraView, _spectator, loggers, window, () => _timeline, () => _loaded, () => settings.Current);
        _maps = maps;
        _settingsOwner = settings;
        _shutdown = shutdown;
        _models = new EntityModelSet(loggers)
        {
            // `cl_threaded_bone_setup`'s fork, on the one pool the particle step shares (finding 105 C).
            BoneWorkers = new WorkerLane(FramePresenter.Workers)
            {
                Threshold = FramePresenter.ThresholdFrom(BoneThresholdVariable, 1),
            },
        };
        _log = loggers.CreateLogger("viewer");
        _mapLog = loggers.CreateLogger("map");
        _renderLog = loggers.CreateLogger("render");
        _demoLog = loggers.CreateLogger("demo");
        _audioLog = loggers.CreateLogger("audio");
        _world = new WorldPresenter(_renderLog);
        _leafBoxes = new LeafBoxes(_renderLog);

        // **Given its collaborators rather than reaching for them**, which is what lets it be
        // tested without a window: the loops it shares with one-shot playback, the decode cache,
        // and somewhere to report. Its map-dependent state arrives when a map is read.
        // A first play in the frame is read and decoded on the pool, not the render thread (B568, finding 90).
        _sounds = new SoundCache(_audioLog) { OffFrame = static work => System.Threading.ThreadPool.QueueUserWorkItem(_ => work()) };
        // `snd_musicvolume` is asked for on every pass, so the first frame and a live change both read the setting (B545).
        // A sound asked for while it loads plays late, when it arrives (finding 90).
        _soundscape = new SoundscapeSystem(_loops, _sounds.Sample, _audioLog) { MusicVolume = () => _settingsOwner.MusicVolume, Loading = _sounds.Loading };
        _sound = new SoundPresenter(_soundscape, _loops, _sounds.Sample, _audioLog)
        {
            Loading = _sounds.Loading,
            Held = _sounds.HeldSample,
            ClientSoundLog = _renderLog,
            MusicVolume = () => _settingsOwner.MusicVolume,
        };

        // **A real source that answers unlit, rather than a null field checked at every call.** The
        // asset loader and the model set both take it as a delegate, and a null there is the shape
        // that hid a missed wiring across 193 call sites once already (D83).
        _moment = new MomentScene(_models, new ViewmodelScene(), _renderLog)
        {
            Lighting = LevelLighting.Unlit(_renderLog),
            Weapons = WeaponModels.None(_renderLog),

            // Every pose, the frame's and a seek's, counts on the device the frame last posed over (B581 rung 3).
            HandOffs = () => _handingDevice?.HandOffs ?? 0,
        };

        // **A local, not a field.** Both readers are constructed here and neither hands it back — keeping one is what
        // would let a later edit report a phase to a ledger nobody prints.
        FrameLedger ledger = new();

        _moments = new MomentPresenter(_moment, ledger, _renderLog);
        Frames = new FrameReporter(ledger, _models, new StopwatchTime(), _renderLog);

        // Everything a frame makes around its models, composed in one presenter (D92, D214).
        _effects = new FramePresenter(_models, _moment, _moments, _sound, _renderLog, _mapLog, _audioLog);

        // **The one fact that travels from the models back to the demo** (B269). Only a model says
        // which pose parameters wrap, and only the interpolator needs to know — `OnNewModel` in the
        // engine, where `m_iv_flPoseParameter.SetLooping` is called. Read through the property
        // rather than captured, because the source arrives with a demo and this runs before one is
        // open.
        _models.ModelResolved = (entity, looping, staticProp) =>
            _moments.Source?.OnNewModel(entity, looping, staticProp);

        // **Registered here, after every system exists, and this is the only place the list is
        // written.** A system added later is added to this call rather than to whichever method
        // happens to load a map — which is the arrangement that let three assignments go missing
        // separately (B193) and two more sit unnoticed for a day (B196).
        // **One holder, two setters, and the window is neither.** The appearance needs a demo and an
        // install, which arrive at different moments — `DemoSystems.Open` supplies the first and
        // `LevelSystems.Install` the second. Held as a local here because both take it and neither
        // hands it back, the same reason `FrameLedger` is a local (B188, D90).
        PlayerAppearances appearances = new(_demoLog);

        _levels = new LevelSystems(
            _moment, _models, _sounds, _soundscape, _sound, appearances, loggers);

        _moments.Appearances = appearances;

        // Registered after the presenter it drives, for the same reason `_levels` waits for the
        // scene: a system list is only as good as every member existing when it is built.
        _demoSystems = new DemoSystems(
            _spectator, _moment, _moments, appearances, _sound, playback, _loops, loggers)
        {
            // The watcher's demo_* ConVars, read by InterpolateViewpoint (B56, D190).
            ClientConVars = clientConVars,
        };

        // **The gib list comes from the INSTALL, not from the model set** (B371). It was wired to
        // `_models.BreakPiecesOf` on the reasoning that a break list is a fact about a loaded model
        // — and that is the trap `docs/memory/precache-what-the-engine-precaches.md#a-lookup-is-not-a-loader`
        // names: the model set answers out of a cache filled from the props DRAWN at a tick, and a
        // gibbed corpse draws no body, so it asked for a model that could not be there and got
        // "no pieces" for one that
        // declares nine. Nothing was drawn at all, which is what the picture showed.
        //
        // The engine reads it at precache time, from the collide data, before anything spawns:
        // `PrecachePropsForModel` (`props_shared.cpp:1239`). So does this.
        //
        // **Read ONCE per model per install, as precache is once.** This called `BreakPiecesOf` on
        // every frame for every gibbed corpse, which reopened the archive and reparsed the `.phy`
        // each time — measured at 3.5% of a Debug frame, and most of the 3 ms `sample` column.
        _demoSystems.Arrows = () => _loaded?.Arrows;
        _demoSystems.World = () => _loaded?.Level;

        // The ground's surface data, through the same texinfo-to-surfaceprop route a footstep and an impact take.
        _demoSystems.GroundSurface = trace =>
            _loaded?.ImpactDecals is { } decals ? _game?.Surfaces?.GetSurfaceData(decals.SurfacePropOfTrace(trace)) : null;

        _demoSystems.Gibs = model =>
        {
            // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
            if (_game is not { } install)
            {
                return [];
            }

            if (!ReferenceEquals(_breakPiecesFor, install))
            {
                _breakPieces.Clear();
                _breakPiecesFor = install;
            }

            if (!_breakPieces.TryGetValue(model, out IReadOnlyList<PhysicsBreakPiece>? pieces))
            {
                pieces = DemoModels.BreakPiecesOf(model, install);
                _breakPieces[model] = pieces;
            }

            return pieces;
        };

        // **The same owner, for the same reason** (B395). A corpse's `m_nBody` is the player's,
        // copied at death (`c_tf_player.cpp:790-793`), and turning a cosmetic's declared part name
        // into an index needs the loaded `.mdl` — which is this set. Without it every corpse draws
        // at body 0 and wears its hats on top of the stock geometry they replace.
        _demoSystems.Bodygroups = _models;
    }

    /// <summary>The model set, for the window's frame reporter and its map projection.</summary>
    public EntityModelSet Models => _models;

    /// <summary>`TF2VIEW_BONE_THRESHOLD`: the bone lane's <see cref="WorkerLane.Threshold"/>, for measuring the fork against its serial pre-pass; debugging only.</summary>
    public const string BoneThresholdVariable = "TF2VIEW_BONE_THRESHOLD";

    /// <summary>How many pre-pass root lists were forked, ever — counted where the fork was taken, for `--measure`.</summary>
    public long BoneForks => _models.BoneWorkers.Forks;

    /// <summary>What the sound cache holds and how many decodes playback paid for, for `--measure` (finding 80).</summary>
    public SoundCacheHeld SoundsHeld => _sounds.Held;

    /// <summary>The moment scene, for the window's draw.</summary>
    public MomentScene Moment => _moment;

    /// <summary>The moment presenter, for the window's show and its frame.</summary>
    public MomentPresenter Moments => _moments;

    /// <summary>The camera presenter this owns, with the spectator the map gives a world and a demo gives eyes.</summary>
    public CameraPresenter Camera => _camera;

    /// <summary>The parts of the frame's `camera` and `hud` phases for the rate line; null, and untimed, unless measuring.</summary>
    public FrameParts? FrameParts
    {
        get => _effects.Parts;
        set => _effects.Parts = value;
    }

    /// <summary>The settings' owner, which the window talks to in plain values.</summary>
    public SettingsPresenter Settings => _settingsOwner;

    /// <summary>Reads the command line and applies what it says of the settings, the spectator, the lighting and the HUD.</summary>
    /// <param name="arguments">The command line.</param>
    /// <returns>What was asked for; the window keeps what is its own (the shot, the tick, the paths).</returns>
    public LaunchOptions ReadLaunch(IReadOnlyList<string> arguments)
    {
        LaunchOptions launch = _settingsOwner.ReadLaunch(arguments);

        // The watcher's `spec_autodirector` (B535).
        _camera.Spectate(launch.Spectate, _settings.AutoDirector);

        // The watcher's `mat_hdr_level` (B537), before any map is read: which lighting every loader takes.
        Content.Bsp.BspHdr.MatHdrLevel = _settings.HdrLevel;

        // **`--hud` wins over the remembered choice, which wins over TF2's stock HUD (D193).** A
        // launch option is for one run; the settings file is what the picker changes and expects to
        // stick without being passed again every time.
        _effects.ChosenHud = launch.Hud ?? _settings.ChosenHud;

        // `--windows` (D217 step 4): the window cache beside the whole timeline, for every demo this run opens.
        CheckWindows = launch.Windows;

        return launch;
    }

    /// <summary>The picker chose a HUD: draw it from the next overlay, and remember it.</summary>
    /// <param name="hud">The HUD's folder or archive, or null for TF2's stock one.</param>
    /// <returns>What to say on the status line, or null for nothing.</returns>
    public string? SetChosenHud(string? hud)
    {
        _effects.ChosenHud = hud;

        return _settingsOwner.SetChosenHud(hud);
    }

    /// <summary>The frame presenter this owns.</summary>
    public FramePresenter Effects => _effects;

    /// <summary>The demo currently open, or null.</summary>
    public LoadedDemo? Demo => _demo;

    /// <summary>The open demo's timeline, or null.</summary>
    public DemoTimeline? Timeline => _timeline;

    /// <summary>The install, once a map read or a decode has opened it.</summary>
    public GameContent? Game => _game;

    /// <summary>The map as it was read, or null when none is open.</summary>
    public LoadedMap? Map => _loaded;

    /// <summary>Whether a map is being read on another thread right now; the render loop draws nothing while it is.</summary>
    public bool ReadingMap => _readingMap;

    /// <summary>Asynchronous loads not yet finished; the `--shot` patience waits while any is.</summary>
    public int LoadsInFlight => _loadsInFlight;

    /// <summary>Lets go of the background corpse pass. The window cancels its shutdown token first, which the pass is linked to.</summary>
    public void ReleaseCorpseRecord()
    {
        _corpseRecording?.Dispose();
        _corpseRecording = null;
    }

    /// <summary>Closes the sound device, and the corpse pass if the window has not already let go of it.</summary>
    /// <remarks>
    /// **Two calls from a closing window, in the order it made them**: the corpse pass goes first, before the idle loop
    /// stops, and the sound device after the display device.
    /// </remarks>
    public void Dispose()
    {
        ReleaseCorpseRecord();
        ReleaseVoice();

        _audio?.Dispose();
        _audio = null;

        // `--windows`: the walk told to stop and every window build cancelled.
        _windowCheck?.Dispose();
        _windowCheck = null;
    }

    /// <summary>Loads the map a demo was recorded on, if a copy can be found.</summary>
    /// <param name="mapName">Map name from the demo header.</param>
    /// <returns>Whether a map was found and read.</returns>
    /// <remarks>
    /// **A missing or unreadable map is not an error.** A demo of a community map nobody has still
    /// plays; the viewport shows the players without a world behind them. Reporting it and
    /// carrying on is the behaviour this whole project is built around.
    /// </remarks>
    public bool LoadMap(string mapName)
    {
        ClearMap();
        return ReadMapNamed(mapName, _timeline).Drawn;
    }

    /// <summary>Whether a map is being read on another thread right now.</summary>
    /// <remarks>
    /// **Volatile because two threads share it and one of them is a tight loop.** The reader writes
    /// a dozen fields and the render loop reads them; this is what keeps the loop out from between.
    /// </remarks>
    private volatile bool _readingMap;

    // **`_mapProblem` was here until 2026-08-26, and it was a second copy of `_loaded.Problem`.**
    // Four assignments, in two perfectly matched pairs — `ClearMap` nulled both, `LoadMap` set both
    // from the same `map` — with nothing making them agree except that they were written next to
    // each other. Its own doc argued it had to be a field because the read runs off the UI thread,
    // which is true of `_loaded` as well and is therefore an argument for one field, not two.
    //
    // The status bar asks `_loaded?.Problem` now. That is the same value by construction rather
    // than by care, which is the whole of the difference.

    /// <summary>Drops the current map and its GPU resources. UI thread only.</summary>
    /// <remarks>
    /// **Separated from the reading because only this half belongs to the device (B146).**
    /// `ClearWorld` disposes Direct3D resources and an immediate context is owned by the thread that
    /// made it; everything after this point is file reading and arithmetic, which is where the
    /// thirteen to eighteen seconds go.
    /// </remarks>
    private void ClearMap()
    {
        // A map change replaces state the frame in flight draws from: a full drain first (B581, finding 99 section 5).
        _device?.Queue.Lock();

        _loaded = null;

        _effects.ClearMap();

        // **Every system is told the level is going, in reverse registration order** — Valve's
        // `LevelShutdownPreEntity`/`PostEntity`, which this window did not have.
        //
        // Teardown used to be split three ways and was asymmetric: two systems were reset here, the
        // soundscape was cleared inside the map READ, and the sound schedule was never torn down at
        // all. Adding a fifth system meant guessing which of the three places it belonged in.
        _levels.Shutdown();

        // **One field where four were cleared, and the fourth was never cleared at all.** `_terrain`
        // and `_overlays` were dropped here while `_brushModels` and `_leaves` were left pointing at
        // the previous map's lumps until the next read replaced them. Nothing read them in between —
        // `ClearMap` is followed immediately by a read — but "correct because of the call order two
        // methods away" is the kind of thing a record makes impossible to get wrong.
        //
        // `_level` was cleared here too until 2026-08-25. It was a SECOND map field left behind by
        // the move that created `LoadedMap`, never assigned anything but null, and `mat_leafvis`
        // read it (B196).
        _leafBoxes.Forget();
        _world.TexturesAreCurrent = false;

        // **The models go with the world, because the world owned their buffer.** `ClearWorld`
        // disposes the `WorldRenderer`, and `_modelVertices` is one of its fields — so the packed
        // set that is still in memory has nowhere on the device to live until it is uploaded again
        // (B148).
        // `_moment.Uploaded = false` was here. It is `MomentScene.LevelShutdownPreEntity`, told by
        // the walk above — the scene knows what it uploaded, so the scene is what forgets it.

        _view.ClearWorld();
    }

    /// <summary>Reads a map by name, and hands back the install it opened.</summary>
    /// <param name="mapName">The map the demo names.</param>
    /// <param name="timeline">The demo being opened, which decides what content loads with the map.</param>
    /// <returns>Whether a world was drawn, and the game content now open.</returns>
    /// <remarks>
    /// **Safe off the UI thread once <see cref="ClearMap"/> has run, verified by reading rather than
    /// assumed**: across its hundred and forty lines this path touches no control, no demo, no
    /// timeline and no device. The one exception was a `_status.Text` assignment in a catch, which
    /// records the failure on <see cref="LoadedMap.Problem"/> for the UI thread to show.
    ///
    /// **The `GameContent` is RETURNED rather than left in a field, and that is a correctness fix
    /// rather than a tidy-up** (B208). Reading a map is what opens the install, and both precaches
    /// begin `if (timeline is null || game is null) return;` — **silently**. So the three calls in
    /// `LoadDemoAsync` had a load-bearing order that nothing enforced: put either precache first and
    /// it does nothing at all, with no error and a green suite.
    ///
    /// Handing the content back makes the dependency an argument, so the wrong order has nothing to
    /// pass. **It is not airtight** — `_game` is still reachable as a field — but the natural form
    /// now carries the order, which is the difference between a comment and a shape.
    ///
    /// This is B203's lesson at a smaller scale: an order that matters, in a window, failing quietly.
    ///
    /// **The timeline is an argument for the same reason, and it was the same bug** (B393). This read
    /// `_timeline`, which `Apply` assigns — and `LoadDemoAsync` reads the map BEFORE `Apply`, so a demo
    /// opened from the playlist had its map read with the previous demo's timeline, or with none on
    /// the first open, and `MapAssets.Load` was told of no sprites and no worn items.
    /// </remarks>
    /// <param name="prepared">
    /// What <see cref="MapProvider.PrepareAsync"/> came back with for this demo, or null on a path that prepared
    /// nothing — the synchronous <see cref="LoadDemo"/> and a reload, which never fetch.
    /// </param>
    private (bool Drawn, GameContent? Game) ReadMapNamed(string mapName, DemoTimeline? timeline, MapFetch? prepared = null)
    {
        MapSearch found = _maps.Find(mapName);

        // **Three outcomes, because "not here" had two causes and only one earns a download**
        // (B211). This asked `Locate`, got null, and said "not installed; fetching it" — so a
        // machine with no TF2 at all was told about the MAP and watched a download start, which is
        // the wrong cause and useless work. The owner's requirement is that a missing install "must
        // just error and mention it", and mentioning the wrong thing is worse than silence.
        // **Says so and CARRIES ON, which the first version of this did not** (B211). Returning here
        // was a regression and CI caught it: with no TF2 the viewer stopped downloading the map, so
        // the world was never built. Every UI test failed with `worlds 0, textures 0`.
        //
        // **"No TF2 install" is not "nothing can be done about the map."** The downloader writes into
        // the viewer's OWN maps folder, which `Locate` searches, and a map there draws without the
        // game — models and stock textures are what go missing. Watching a demo on a machine with no
        // TF2 is the salvage case, not an error case.
        //
        // The owner's requirement was to *mention* it, and mentioning it is all this does.
        if (found.Outcome == MapOutcome.NoGame)
        {
            _view.Status = NoGameInstalled;
            _mapLog.LogWarning("{Message}", NoGameInstalled);
        }

        // **The fetch already happened, from the header, and the load waited for it** (B507). This branch started it
        // fire-and-forget once the decode was done, and the read it ended in ran on a pool thread outside the
        // `_readingMap` barrier and skipped both precaches: a first download left the view mapless or crashed it.
        // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
        if (found.Path is not { } path)
        {
            string absent = prepared?.Status is { Length: > 0 } why ? why : $"Map {mapName} is not installed";
            _view.Status = absent;
            _mapLog.LogWarning("{Message}", absent);
            return (false, _game);
        }

        _mapLog.LogInformation("{Message}", $"found {path}");

        return (ReadMap(mapName, path, timeline), _game);
    }

    // `DownloadMapAsync` was here until B507: a fetch started after the decode, fire-and-forget, whose map read ran
    // on a pool thread (the continuation of a task begun inside `Task.Run` has no UI context to return to) beside the
    // render loop. `MapProvider.PrepareAsync`, awaited inside `LoadDemoAsync`, replaces it. The D162 history it
    // carried — the recorded version is wanted, from the first source that has it — is in `docs/DECISIONS.md`.

    /// <summary>Reads a map file into the viewport's geometry.</summary>
    private bool ReadMap(string mapName, string path, DemoTimeline? timeline)
    {
        try
        {
            byte[] bytes = File.ReadAllBytes(path);

            _mapLog.LogInformation(
                "{Message}",
                $"loading {Path.GetFileName(path)} ({bytes.Length / 1024 / 1024} MB)");

            // **Opening the install is a lifecycle question, not a window's** (B188, D90). The
            // branch, the field and the `OpenGame` call beside it were all here; `LevelSystems.Install`
            // owns them and answers the same content every time after the first.
            //
            // It is deferred because the folder is not knowable until the user points at it, NOT
            // because it is slow — which is what the comment here used to say, and is the difference
            // between "this could be made eager" and "there is nothing to hurry".
            _game = _levels.Install(_maps.GameFolder);

            _world.TexturesAreCurrent = false;

            // **Reading a map is not window work and telling the systems about it is not either**
            // (B188, D90). `LevelSystems` is the engine's own shape: a registered LIST walked at the
            // level boundary — `LevelInitPreEntityAllSystems( pMapName )` (`igamesystem.h:77`) —
            // rather than one method reaching into six collaborators, which is the arrangement that
            // let B193 and B196 drop assignments unnoticed.
            LoadedMap map = _levels.Load(
                bytes,
                _game,
                timeline,
                (int)_settings.TextureQuality,
                mapName);

            // **The LEVEL survives a content failure, and it did not before.** The old catch set
            // `_level = null` alongside `_assets = null`, throwing away lumps that had read
            // perfectly because the TEXTURES did not — so `mat_leafvis` went blank on a map whose
            // BSP tree was fine. `LoadedMap` separates the two: the lumps are read or they throw,
            // and the content is a nullable beside them.
            _loaded = map;

            _effects.LevelInit(map, _settings.DoPixelVisibility);

            // **The chase camera clips against the world, and this is where it gets one.** Valve
            // traces a twelve-unit hull from the target to the camera and pulls the camera in to
            // whatever it hits (`hltvcamera.cpp`, and see `ChaseCamera`). Null until a map is open,
            // which is why `SpectatorView.World` is nullable rather than assumed.
            // **`MapLevel.Sweep`, which asks the BRUSHES and the TERRAIN** (B227). This asked only
            // `Leaves` until 2026-08-29, and displacements are not in the BSP tree at all — measured
            // on cp_badlands, where none of the 1191 displacement faces is reachable from any leaf —
            // so the camera passed straight through every hillside in the game.
            _spectator.World = (from, to, extent) => map.Level.Sweep(from, to, extent);

            // **And the corpses get the map's BAKED physics collision, which is a different thing**
            // (B58). `Sweep` above answers a camera's question about brush faces; this is
            // `LUMP_PHYSCOLLIDE`, the hulls the compiler baked and the engine feeds to
            // `CreatePolyObjectStatic`. Set before any corpse is seeded — a simulation created
            // without a world falls through the map for its whole life, and `Clear` on a map change
            // is what stops one outliving its geometry.
            StopCorpseRecord();
            _models.Corpses.Clear();

            // **The corpses' environment is the ported driver's, with the map's collide in it** (D172, D179): the world, the
            // static solids, the virtual terrain and the static props, as `PhysicsLevelInit` builds `physenv`. The game's surfaces
            // are what stop a corpse sliding; a viewer with no game folder gives every body the no-install friction.
            Tf2DemoSalvage.Animation.Animating.VphysicsSurfaceProps surfaces =
                _game?.Surfaces ?? new Tf2DemoSalvage.Animation.Animating.VphysicsSurfaceProps([]);
            _models.Corpses.Surfaces = surfaces;
            _models.Corpses.CreateWorld = IvpMapWorld.Factory(bytes, _game, surfaces, _mapLog);

            // **The map's detail models are packed inside `LevelSystems.Load`** (B363), beside the
            // geometry loader that reads them — a call here instead ran after something else had
            // already added the path with no geometry, and the packer skips a path it has seen.

            _view.MapRead();
            return !map.Outline.IsEmpty;
        }
        catch (Exception failure) when (failure is IOException or InvalidDataException)
        {
            _view.Status = MapProvider.CouldNotRead(mapName, failure);
            return false;
        }
    }

    /// <summary>Loads a demo by path and brings the transport up on it.</summary>
    /// <param name="path">Full path to the demo.</param>
    /// <remarks>
    /// Split from the window's <c>LoadSelected</c> so loading can be exercised without a live ListView:
    /// selection state needs a created window handle, so a form that was never shown reports no
    /// selection at all — which made the first version of this test fail for a reason that had
    /// nothing to do with loading.
    /// </remarks>
    public DemoLoadResult LoadDemo(string path)
    {
        // **Takes a ticket it never asks about**, so that starting a synchronous load supersedes any
        // async one already decoding. Both paths end by assigning the same fields.
        _loads.Take();

        try
        {
            return Apply(DecodedDemo.Read(
                path, _demoLog, interp: _settings.Interp, classes: _levels.Install(_maps.GameFolder).ClassAnimation, keepBytes: CheckWindows));
        }
        catch (Exception failure) when (failure is IOException or InvalidDataException)
        {
            return CouldNotOpen(path, failure);
        }
    }

    /// <summary>Opens a demo without freezing the window (B146).</summary>
    /// <param name="path">Full path to the demo.</param>
    /// <returns>A task that completes when the demo is on screen.</returns>
    /// <remarks>
    /// **The decode is seconds of work and it was running in the click handler.** Measured from the
    /// viewer's own log: 0.33–0.76 s for a small point-of-view recording, and **4.66–4.88 s** for
    /// `z1800.dem`, a 24-minute nine-versus-nine match. Windows marks a window that has not pumped
    /// messages for five seconds as "Not Responding", which is exactly the threshold a real match
    /// crosses — the owner watched it happen.
    ///
    /// **The split is decode-then-show, and the line between them is "does this touch the form".**
    /// <see cref="DecodedDemo.Read"/> reads and decodes and lives in another project entirely, so it
    /// cannot reach the UI at all; <see cref="Apply"/> assigns the fields, the transport, the
    /// clock and the map, and must be on the UI thread because it ends in Direct3D. The `await`
    /// returns to the UI thread on its own — WinForms installs a synchronisation context, so there
    /// is no marshalling to write and none to get wrong.
    ///
    /// **A newer request wins.** Double-clicking two demos in a row starts two decodes, and the
    /// slower one must not overwrite the faster: each takes a ticket and only the newest is shown.
    /// Without that, opening a big demo and changing your mind leaves you looking at the big one.
    ///
    /// **The synchronous <see cref="LoadDemo"/> stays** for the command line, the `--shot` capture
    /// path and the tests, where there is no window to freeze and a caller that returns before the
    /// demo exists is simply wrong.
    /// </remarks>
    public async Task<DemoLoadResult> LoadDemoAsync(string path)
    {
        int ticket = _loads.Take();

        _view.Status = DemoLoadResult.Opening(path);
        _loadsInFlight++;
        _view.ReportLoading(LoadingDecode, 0d);

        try
        {
            ILogger demoLog = _demoLog;
            Core.Net.ClientInterp interp = _settings.Interp;

            // **Posted, so a report can arrive after the decode has moved on** — the stage check
            // keeps a late one from dragging the overlay back to "decoding".
            bool decoding = true;
            IProgress<double> progress = new Progress<double>(fraction =>
            {
                if (decoding && _loads.IsCurrent(ticket))
                {
                    _view.ReportLoading(LoadingDecode, fraction);
                }
            });

            // **The shutdown token, on both worker hops** (B402). Neither read can be interrupted
            // part-way — a decode and a map read are one operation each — but the token means a
            // window that closes mid-load does not come back to a disposed form afterwards, which
            // is the same crash the map fetch had.
            // **The map is found or fetched from the HEADER, in parallel with the decode** (B507): the fetch used to start
            // only after a decode of tens of seconds, and its read then raced the render loop off the load's barrier.
            Task<MapFetch> mapReady = _maps.PrepareAsync(
                path,
                mapName => _mapLog.LogInformation("{Message}", $"{mapName} is not installed; fetching it"),
                _shutdown);

            // **The install is opened first, because the decode reads its class scripts** (B437): `DontDoAirwalk` and
            // `DontDoNewJump` are anim-state inputs the demo does not carry. `Install` opens once and answers the same
            // content every time after, so the map read below gets this same one.
            DecodedDemo decoded = await Task
                .Run(
                    () => DecodedDemo.Read(
                        path, demoLog, progress.Report, interp, _levels.Install(_maps.GameFolder).ClassAnimation, keepBytes: CheckWindows),
                    _shutdown)
                .ConfigureAwait(false);

            if (!_loads.IsCurrent(ticket))
            {
                return _view.OnUi(() => Superseded(_demoLog, path));
            }

            _view.OnUi(() =>
            {
                decoding = false;
                _view.ReportLoading(LoadingMap, fraction: null);
                return 0;
            });

            // **Waited for here, under the loading overlay** — a first download ends with the map loaded, never a mapless
            // view left behind (owner, 2026-10-05). An installed map's task finished long ago.
            MapFetch prepared = await mapReady.ConfigureAwait(false);

            if (!_loads.IsCurrent(ticket))
            {
                return _view.OnUi(() => Superseded(_demoLog, path));
            }

            // **The map read is the expensive half — 13 to 18 seconds of it (B146).** Dropping the
            // old map touches the device and stays here; finding and reading the new one touches
            // nothing but files and arithmetic, and goes to a worker.
            //
            // **`_readingMap` is what makes that safe.** The read assigns a dozen fields as it
            // goes, and the render loop reads them, so between the two the world is half replaced.
            // The flag holds the loop off until the marshal back, which is also the barrier that
            // publishes the writes.
            _view.OnUi(() =>
            {
                _readingMap = true;
                ClearMap();
                return 0;
            });

            bool read;

            try
            {
                read = await Task.Run(() =>
                {
                    // **The read comes first because it is what opens the install**, and both
                    // precaches return silently without one (B208). That order used to be three
                    // statements anyone could reorder into a silent no-op; it is now carried by
                    // `game`, which does not exist until the read has produced it.
                    (bool drawn, GameContent? game) =
                        ReadMapNamed(decoded.Demo.MapName, decoded.Timeline, prepared);

                    _view.OnUi(() =>
                    {
                        _view.ReportLoading(LoadingAssets, fraction: null);
                        return 0;
                    });

                    // **Packed here rather than when a prop first appears, which is what Valve
                    // does** (D86). `CBaseEntity::PrecacheModel` sits behind `IsPrecacheAllowed()`
                    // and warns on an out-of-order precache: the engine loads models at level load,
                    // deliberately, so nothing is decoded mid-game.
                    //
                    // Ours was packing on sight, and it cost 385 ms in a single frame the first time
                    // a crowd of props came into view — measured 2026-08-24. Inside this Task.Run
                    // and before `_readingMap` is cleared, so it runs on the worker behind the
                    // barrier that already holds the render loop off during a map read (B146).
                    DemoModels.Precache(_models, decoded.Timeline, game, _renderLog);

                    // Same timing, same reason, and the audio path is where the cost actually
                    // landed once the model stalls were gone. **No emitted sounds yet**: those join
                    // the schedule in `Apply`, which precaches them itself. **The scripts ARE here**:
                    // the map read above set the level's (`LevelSystems.Load`, on this thread), so the
                    // surfaces' sounds — 385 more names, 85–763 ms on f12 — decode on this worker,
                    // behind the barrier, rather than in `Apply` on the UI thread (finding 84).
                    DemoSounds.Precache(_sounds, decoded.Timeline, game, _soundscape, _audioLog, emitted: [], _sound.Scripts?.Entries, _models.AnimationEvents());

                    return drawn;
                }, _shutdown).ConfigureAwait(false);
            }
            finally
            {
                _view.OnUi(() =>
                {
                    _readingMap = false;
                    return 0;
                });
            }

            return _view.OnUi(() => _loads.IsCurrent(ticket)
                ? Apply(decoded, read)
                : Superseded(_demoLog, path));
        }
        catch (Exception failure) when (failure is IOException or InvalidDataException)
        {
            return _view.OnUi(() => _loads.IsCurrent(ticket)
                ? CouldNotOpen(path, failure)
                : Superseded(_demoLog, path));
        }
        finally
        {
            _view.OnUi(() =>
            {
                // **Hidden only when no newer load is still running**, which would otherwise lose
                // its overlay to the one it superseded.
                if (--_loadsInFlight == 0)
                {
                    _view.HideLoading();
                }

                return 0;
            });
        }
    }

    /// <summary>Stops the background corpse pass for the demo and map being closed (D181).</summary>
    private void StopCorpseRecord()
    {
        _corpseRecording?.Cancel();
        _corpseRecording?.Dispose();
        _corpseRecording = null;
    }

    /// <summary>Starts simulating every corpse in the open demo in the background, straight through, for seeks to read (D181).</summary>
    /// <remarks>
    /// **The live viewer reads the record the moment it reaches a tick, and D179's replay covers the rest**, so a seek ahead of the
    /// pass still works — it is only slower. The pass has its own environment and entities and shares nothing writable with the
    /// render thread: the map's model frames are fixed once the map is read, and the record publishes a tick only after writing it.
    /// </remarks>
    private void StartCorpseRecord()
    {
        StopCorpseRecord();

        // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
        if (_moments.Source is not TimelineMoments source || source.RecordedCorpses() is not { Count: > 0 } corpses)
        {
            return;
        }

        CorpseRecord record = new(corpses);
        _models.Corpses.Record = record;

        // **The corpse pass says what it heard, whichever path posed the corpse** — f12's 26990 death played none after a seek to
        // 26960, when only the record was read.
        _sound.ForgetCorpseSounds();
        _models.Corpses.FrictionHeard = _sound.CorpseFrictionHeard;
        _models.Corpses.ImpactHeard = _sound.CorpseImpactHeard;
        _corpseRecording = CancellationTokenSource.CreateLinkedTokenSource(_shutdown);

        Func<string, PropModels.ModelFrames?> geometry = _models.Geometry;
        Func<float, Tf2DemoSalvage.Animation.Animating.IvpRagdollWorld>? createWorld = _models.Corpses.CreateWorld;
        Tf2DemoSalvage.Animation.Animating.VphysicsSurfaceProps surfaces = _models.Corpses.Surfaces;
        float interval = source.IntervalPerTick;
        CancellationToken token = _corpseRecording.Token;
        ILogger log = _renderLog;

        CorpseRecording = Task.Run(
            () =>
            {
                Stopwatch taken = Stopwatch.StartNew();
                record.Run(geometry, createWorld, surfaces, interval, token);
                // Stryker disable once all : a mutated interpolated string cannot bind to its handler (CS1620) and Safe Mode drops the method - B410
                log.LogInformation(
                    "{Message}",
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"corpse record: {corpses.Count} bodies to tick {record.Reached} in {taken.Elapsed.TotalSeconds:F1} s"));
            },
            token);
    }

    /// <summary>Cancels the pass on a demo or map change, and on shutdown through <see cref="_shutdown"/>.</summary>
    private CancellationTokenSource? _corpseRecording;

    /// <summary>The background corpse pass, held so its outcome is observed — a test awaits it, and a fault is not lost (D181).</summary>
    public Task CorpseRecording { get; private set; } = Task.CompletedTask;

    /// <summary>The overlay's words for the decode.</summary>
    private const string LoadingDecode = "Decoding the demo";

    /// <summary>The overlay's words for the map read.</summary>
    private const string LoadingMap = "Loading the map";

    /// <summary>The overlay's words for the model and sound precache.</summary>
    private const string LoadingAssets = "Loading models and sounds";

    /// <summary>Asynchronous loads not yet finished; the `--shot` patience waits while any is.</summary>
    private int _loadsInFlight;

    /// <summary>Says a load was overtaken, without touching anything.</summary>
    // The logger is a parameter because this is static (D83).
    private static DemoLoadResult Superseded(ILogger demoLog, string path)
    {
        // **The wording is `DemoLoadResult`'s** (B188, D90). This method was already static so it
        // could not reach the form — the same shape `DecodedDemo` was in before it moved, and the
        // same note applies: the only thing keeping the sentence here was the file it sat in.
        //
        // What is left is the logging, which is a side effect the result cannot perform for itself.
        DemoLoadResult result = DemoLoadResult.Superseded(path);

        demoLog.LogInformation("{Message}", result.Message);

        return result;
    }

    /// <summary>Which load is still the one wanted.</summary>
    /// <remarks>
    /// **Was `_loadsRequested` and four bare comparisons against it** (B188, D90). The policy — a
    /// newer request wins — needed no window to state and could only be reached by racing two real
    /// loads against a real 24-minute demo.
    /// </remarks>
    private readonly LoadTickets _loads = new();

    // `Decoded` and `Decode` were here until 2026-08-25. They are `DecodedDemo` in the Scene project
    // now (B188, D90). Nothing about them was ever window work: the static was made static when the
    // load went off-thread, precisely so it could not reach the form — so the only thing keeping it
    // here, and untested, was the file it sat in. That is the drift D89 names.

    /// <summary>Puts a decoded demo on screen. UI thread only.</summary>
    /// <param name="decoded">The demo and its timeline.</param>
    /// <param name="read">Whether the map was already read off-thread, or null to read it here.</param>
    /// <returns>What to tell the caller.</returns>
    /// <remarks>
    /// **Everything here touches the form, and the last thing it does is Direct3D**, which is why
    /// the split is where it is rather than a few lines either side. `LoadMap` reads a BSP and
    /// uploads textures to the device, and a device is owned by the thread that made it.
    /// </remarks>
    private DemoLoadResult Apply(DecodedDemo decoded, bool? read = null)
    {
        _demo = decoded.Demo;
        _timeline = decoded.Timeline;

        // `--windows` (D217 step 4): the window cache walks beside the whole timeline and is checked on every seek.
        // The last demo's check stops first: its walk and its windows are cancelled.
        _windowCheck?.Dispose();
        _windowCheck = CheckWindows ? WindowCheck.For(decoded, new Core.Scene.WindowSettings(_settings.Interp), _demoLog) : null;

        _camera.DemoOpening(_spectateWho());

        // **One call where there were six assignments, and that is the whole point** (B193). The
        // eyes, the viewmodels, the sound schedule and the appearance were each written inline
        // here, and three of them separately became a property nobody set — two of those shipped,
        // with the viewer suite green throughout. `DemoSystems` is the demo mirror of
        // `LevelSystems`: one place to read, and one place a test can reach.
        //
        // **Two ways to ask for autoplay, and only one of them is testable.** `--autoplay` is the
        // option (D118); the environment variable predates it and still works, because a shell
        // somewhere may already export it and silently dropping that would be a regression nobody
        // could see. The window is still the only place that reads the environment — a process-wide
        // variable is the business of whatever owns the process.
        (bool autoPlay, string autoPlayReason) = _view.AutoPlay;

        _demoSystems.Open(
            _timeline,
            _demo.LastTick,
            _audio,
            autoPlay,
            autoPlayReason);

        _view.DemoOpened();
        _camera.DemoOpened();

        // **`_transport.SetDemoLength(_demo.LastTick)` was here, and it was the bug** (B223, D118).
        // `Open` starts autoplay; this line ran immediately afterwards and its last act is
        // `Playing = false`, which silently switched playback back off. `DemoSystems.Open` sets the
        // length itself now, before the clock exists, so there is no longer a gap for a second
        // caller to write into.

        // **The map may already have been read, off the UI thread (B146).** `LoadDemoAsync` does
        // that and passes what it found; the synchronous path passes null and reads it here, which
        // is what the command line, `--shot` and the tests want.
        bool haveMap = read ?? LoadMap(_demo.MapName);

        // **Here as well as on the load worker, because there are TWO load paths and only one of
        // them went through the worker.** `LoadDemoAsync` is the playlist's route; `LoadDemo` is the
        // command line's, `--shot`'s and the tests', and it calls Apply directly. The first attempt
        // put the precache only in the async path, so launching with a demo on the command line
        // precached nothing at all and the 425 ms stall was still in the log — which is
        // `docs/memory/one-place-or-it-drifts.md`, and the log is what caught it.
        //
        // Cheap to call twice: `EntityModelSet.Add` skips a model it already holds, so on the async
        // path this finds the work already done and returns.
        //
        // After `haveMap` rather than before, because packing reads the geometry the MAP read
        // decoded into `_assets.EntityModels`. Called earlier it would find nothing, mark every path
        // as seen, and models would never load at all.
        //
        // **This path reads `_game` rather than a returned value** (B208), unlike the one in
        // `LoadDemoAsync`, and the difference is real: here the map may have been read on either
        // path — `read ?? LoadMap(...)` — so there is no single call whose result could carry it.
        // The comment above is the whole of the guarantee here, which is exactly why the other call
        // site was changed to carry it structurally instead.
        DemoModels.Precache(_models, _timeline, _game, _renderLog);

        // **Every explosion's sound, which the client emits and no demo carries** (B415). After the map read,
        // because that is what opens the install whose scripts name them.
        IReadOnlyList<SceneSound> emitted = _demoSystems.AddEffectSounds(_game, DecalEffects.Landings(_loaded, _game, _timeline));

        // Cheap to call twice: `SoundCache.Precache` skips every name an earlier precache of this level listed, evicted or
        // not, so on the async path this decodes only the emitted ones, which the worker could not know yet (B570).
        DemoSounds.Precache(_sounds, _timeline, _game, _soundscape, _audioLog, emitted, _sound.Scripts?.Entries, _models.AnimationEvents());

        // **After the map and the models, because the pass reads both** (D181): the environment is the map's, and a corpse's
        // bones come from its model.
        StartCorpseRecord();

        // **Where the memory is, once everything is loaded** (B407): the process held ~16 GB after a map load and nobody could say
        // whether that was the managed heap or native memory (textures, the device). The two numbers answer it.
        //
        // **One blocking, compacting collection first** (D199, B433). A load allocates many GB and reaches few gen2
        // collections, so the dead half stayed committed and the line reported it as if live. Collected here, the
        // working set gives it back and the heap figure is the live set. Paid once per load, about a second.
        long collectAt = Stopwatch.GetTimestamp();
        System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
#pragma warning disable S1215 // The owner chose this collection (D199): the load's dead gigabytes stay committed without it.
        // Aggressive, not Forced: Forced left 8.7 GB committed on z1800 (working set 9.6 GB against a 4.5 GB live heap);
        // Aggressive also decommits the freed regions, which is what hands them back to Windows.
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
#pragma warning restore S1215
        double collectMs = Stopwatch.GetElapsedTime(collectAt).TotalMilliseconds;

        GCMemoryInfo heap = GC.GetGCMemoryInfo(GCKind.FullBlocking);
        // Stryker disable once all : a mutated interpolated string cannot bind to its handler (CS1620) and Safe Mode drops the method - B410
        _log.LogInformation(
            "{Message}",
            string.Create(
                CultureInfo.InvariantCulture,
                $"memory after load (after a {collectMs:F0} ms full collection): working set {Environment.WorkingSet / 1048576d:F0} MB, live managed heap {heap.HeapSizeBytes / 1048576d:F0} MB " +
                $"(committed {heap.TotalCommittedBytes / 1048576d:F0} MB, fragmented {heap.FragmentedBytes / 1048576d:F0} MB); " +
                $"entity model vertices {_models.Vertices.Count:N0} in {_models.VertexBytes / 1048576d:F0} MB"));

        _view.Status = _loaded?.Problem
            ?? (_demo.Describe() + (haveMap ? string.Empty : "  (map not found)"));

        // **A marker pass ran here so opening a demo showed the players standing where they
        // started, rather than an empty map waiting for someone to press play** (D98). The intent
        // survives the markers: `ShowMoment` below draws the first tick's MODELS for the same
        // reason, which is the half that was always the better answer.

        _view.DemoShown();

        return new DemoLoadResult(DemoLoadOutcome.Loaded, _view.Status);
    }

    /// <summary>Puts the form back into a state with no demo, and says why. UI thread only.</summary>
    private DemoLoadResult CouldNotOpen(string path, Exception failure)
    {
        _demo = null;
        _timeline = null;
        _windowCheck?.Dispose();
        _windowCheck = null;

        // **`DemoSystems.Open` with no timeline IS "close the demo", and calling it is what makes
        // this path complete** (B228). It was three hand-written lines — the clock, the transport
        // length, and nothing else — while `Open`'s null path clears every source there is: eyes,
        // viewmodels, moments, the sound schedule, the appearance and the loops in flight.
        //
        // That gap mattered as of 2026-08-29. The sound schedule used to be cleared by the LEVEL
        // teardown, which meant a failed load happened to be covered from the side; removing that
        // (it silenced every run, B228) leaves this the only place a failed load can let go of the
        // previous demo's sounds. Two hand-maintained lists of what a demo owns is the shape that
        // let three sources go missing separately in B193.
        _demoSystems.Open(_timeline, lastTick: 0, _audio, autoPlay: false, autoPlayReason: string.Empty);

        // **The wording is `DemoLoadResult`'s** (B188, D90), which also keeps the status line and
        // the returned message identically worded by construction rather than by `_status.Text`
        // being read back — two wordings for one event is how a log and a window come to disagree.
        DemoLoadResult result = DemoLoadResult.CouldNotOpen(path, failure);

        _view.Status = result.Message;

        _demoLog.LogWarning(failure, "{Message}", $"opening {System.IO.Path.GetFileName(path)}");

        return result;
    }
}
