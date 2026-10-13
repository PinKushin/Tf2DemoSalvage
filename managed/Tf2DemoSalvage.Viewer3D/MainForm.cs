using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

// **`Content.Bsp` and `System.Net.Http` were imported here until 2026-08-26** (B208). Neither
// contributed a type any more — HTTP survived only inside a COMMENT — and a stale using is a false
// statement about what this file depends on, which is exactly what `ImplicitUsings` is disabled to
// prevent.
//
// **`Content.Assets` stays, and checking it was the point.** A grep for likely type names said it
// was dead too; the compiler said `GlyphAtlas` and `SchemeFont`. Guessing which types a namespace
// contributes is not a test of whether it is used — removing it and building is.
//
// `Core.Scene` is the honest remaining coupling: the window holds a `DemoTimeline`, a
// `PlaybackClock`, and lists of `ScenePlayer` and `ScenePoint` to hand to the presenters that act
// on them. It reasons about none of them.
using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Core.Text;
using Tf2DemoSalvage.Logging;
using Tf2DemoSalvage.Scene.Hud;
using Tf2DemoSalvage.Scene.Prediction;

namespace Tf2DemoSalvage.Viewer3D;

/// <summary>
/// The viewer shell: menu, viewport and status line.
/// </summary>
/// <remarks>
/// **WinForms hosts the chrome; Direct3D owns only the viewport.** Menus, a timeline and an
/// entity list are ordinary controls, and drawing them in D3D would mean writing a UI toolkit to
/// avoid using one. The renderer takes the viewport panel's handle and presents into that
/// rectangle, so the two never fight over the same surface.
///
/// **Every control carries an automation id and an accessible name**, because the UI tests are a
/// stated part of this project and a control that automation cannot address is a control that
/// cannot be tested. WinForms exposes both through <see cref="Control.Name"/> — which is what
/// UIA reports as AutomationId — and <see cref="Control.AccessibleName"/>, which is what a screen
/// reader announces. They are different properties and both are needed: the first is a stable
/// identifier that must not be translated, the second is prose that should be.
///
/// The device is created on first paint rather than in the constructor, because a swap chain
/// needs a real handle and the panel does not have one until it is shown. Constructing the form
/// therefore stays free of side effects, which is also what lets it be tested without a display.
/// </remarks>
internal class MainForm : Form, IFrameSteps, ILoadView, ICameraView
{
    /// <summary>Automation id of the Direct3D viewport panel.</summary>
    public const string ViewportId = "Viewport";

    /// <summary>Automation id of the status line.</summary>
    public const string StatusId = "StatusLabel";

    /// <summary>Automation id of the File menu.</summary>
    public const string FileMenuId = "FileMenu";

    /// <summary>Automation id of the File &gt; Open item.</summary>
    public const string OpenDemoItemId = "OpenDemoMenuItem";

    /// <summary>Automation id of the File &gt; Export assembly item.</summary>
    public const string ExportItemId = "ExportMenuItem";

    /// <summary>Automation id of the File &gt; TF2 folder item (D210).</summary>
    public const string GameFolderItemId = "GameFolderMenuItem";

    /// <summary>Accessible name of the File &gt; TF2 folder item, which is how UIA reaches a menu item.</summary>
    public const string GameFolderItemName = "TF2 folder";

    /// <summary>How a refused folder choice begins, in the status bar and the log.</summary>
    public const string NotAGameFolder = SettingsPresenter.NotAGameFolder;

    /// <summary>How the startup log line naming the discovered game folder begins.</summary>
    public const string GameFolderLog = "game folder at startup: ";

    /// <summary>What <see cref="GameFolderLog"/> says when discovery found nothing.</summary>
    public const string GameFolderNotFound = "not found";

    /// <summary>How an accepted folder choice begins, in the status bar and the log.</summary>
    public const string GameFolderSet = SettingsPresenter.GameFolderSet;

    /// <summary>Automation id of the File &gt; Compile assembly item.</summary>
    public const string CompileItemId = "CompileMenuItem";

    /// <summary>Automation id of the File &gt; Exit item.</summary>
    public const string ExitItemId = "ExitMenuItem";

    /// <summary>Automation id of the open button.</summary>
    public const string OpenButtonId = "OpenButton";

    /// <summary>Automation id of the open-folder button.</summary>
    public const string OpenFolderButtonId = "OpenFolderButton";

    /// <summary>Automation id of the playlist.</summary>
    public const string PlaylistId = "Playlist";

    /// <summary>Automation id of the playlist search box.</summary>
    public const string SearchId = "PlaylistSearch";

    /// <summary>Automation id of the export button.</summary>
    public const string ExportButtonId = "ExportButton";

    /// <summary>Automation id of the compile button.</summary>
    public const string CompileButtonId = "CompileButton";

    /// <summary>Automation id of the View &gt; Full screen item.</summary>
    public const string FullScreenItemId = "FullScreenMenuItem";

    /// <summary>Automation id of the diagnostic surface-colour toggle.</summary>
    public const string SurfaceColoursItemId = "SurfaceColoursMenuItem";

    /// <summary>Automation id of the brush outline toggle.</summary>
    public const string WireframeItemId = "WireframeMenuItem";

    /// <summary>Automation id of the frame rate meter toggle — Valve's <c>cl_showfps</c>.</summary>
    public const string FrameRateItemId = "FrameRateMenuItem";

    /// <summary>Automation id of the position readout toggle — Valve's <c>cl_showpos</c>.</summary>
    public const string PositionReadoutItemId = "PositionReadoutMenuItem";

    /// <summary>Automation id of the reflections toggle — Valve's <c>mat_specular</c>.</summary>
    public const string SpecularItemId = "SpecularMenuItem";

    /// <summary>Automation id of the highlights toggle — Valve's <c>mat_phong</c>.</summary>
    public const string PhongItemId = "PhongMenuItem";

    /// <summary>Automation id prefix of the lighting submenu — Valve's <c>mat_fullbright</c>.</summary>
    /// <remarks>
    /// Each item's id is this plus its <see cref="Fullbright"/> value, so automation can name a
    /// state rather than an index — an index would silently point at a different mode the moment
    /// the order changed.
    /// </remarks>
    public const string FullbrightItemId = "FullbrightMenuItem";

    /// <summary>Automation id of the world pass toggle — Valve's <c>r_drawworld</c>.</summary>
    public const string DrawWorldItemId = "DrawWorldMenuItem";

    /// <summary>Automation id of the entity pass toggle — Valve's <c>r_drawentities</c>.</summary>
    public const string DrawEntitiesItemId = "DrawEntitiesMenuItem";

    /// <summary>Automation id prefix of the debug views submenu; each item appends its mode.</summary>
    public const string DebugMenuItemId = "DebugMenuItem";

    /// <summary>Automation id of the View menu, which has to be opened to reach its items.</summary>
    public const string ViewMenuId = "ViewMenu";

    /// <summary>Automation id of the Hud menu — the picker over the program's own <c>custom/</c> folder (D193).</summary>
    public const string HudMenuId = "HudMenu";

    /// <summary>Accessible names of the menu entries, which is how automation reaches them.</summary>
    /// <remarks>
    /// **A WinForms menu item exposes no AutomationId.** Its accessible object does not implement
    /// the property at all, so a search by id does not come back empty — it throws
    /// "The requested property 'AutomationId' is not supported" on the first item it inspects. The
    /// Name assigned in code reaches the designer and nothing else.
    ///
    /// The accessible name is the identifier a menu item genuinely has, and it is also the thing a
    /// screen reader announces, so tying tests to it means a rename that would confuse a user
    /// breaks a test rather than passing silently. Named here so the two sides cannot drift.
    /// </remarks>
    public const string ViewMenuName = "View menu";

    /// <summary>Accessible name of the full screen item.</summary>
    public const string FullScreenItemName = "Full screen";

    /// <summary>Accessible name of the screenshot item.</summary>
    public const string ScreenshotItemName = "Save a screenshot";

    /// <summary>Automation id of the screenshot item.</summary>
    /// <remarks>
    /// **Added because F12 was the only way to reach it.** A function key is not a route for
    /// someone driving the program by keyboard-navigation or a screen reader — there is nothing to
    /// find, nothing that announces itself, and nothing in any menu that says the feature exists.
    /// A UI test hit the same wall from the other side and reached for synthesized key presses,
    /// which go to whatever window holds the foreground and twice landed in the tester's browser.
    ///
    /// Both problems have the same fix, and that is the useful part: **anything a test can only do
    /// by faking input is something a person may have no way to do at all.**
    /// </remarks>
    public const string ScreenshotItemId = "ScreenshotMenuItem";

    /// <summary>Automation id of the borderless full-screen mode item.</summary>
    public const string BorderlessItemId = "BorderlessModeMenuItem";

    /// <summary>Automation id of the exclusive full-screen mode item.</summary>
    public const string ExclusiveItemId = "ExclusiveModeMenuItem";

    /// <summary>Automation id of the texture quality menu.</summary>
    public const string TextureQualityMenuId = "TextureQualityMenu";

    /// <summary>The scene's surface. Disposed by <c>base.Dispose</c>, not by us (B402).</summary>
    /// <remarks>
    /// **Every child control here carries this suppression, and the reason is measured.** Each sits
    /// in `Controls`, which `Form.Dispose` walks, so CA2213 is satisfied by a base the analyzer
    /// cannot follow. Disposing them EARLY from our own override killed the process on every CI
    /// run — first at `shutdown: releasing viewport`, then, once the viewport was skipped, at
    /// `releasing transport` and `releasing actions` on two different runners. Two different
    /// members rules out any one control being at fault.
    ///
    /// **It did not fix the crash, and the removal stays anyway.** With none of them disposed here
    /// the log reached `shutdown: every member released` and the process still died, so the fatal
    /// call is further down. These disposals were redundant regardless — `Form.Dispose` walks
    /// `Controls` — and re-adding them would only put the marker back in front of the real cause.
    /// </remarks>
    [SuppressMessage(
        "Usage",
        "CA2213:Disposable fields should be disposed",
        Justification = "Disposed by base.Dispose, which walks Controls; ours was redundant (B402).")]
    private readonly Panel _viewport;

    /// <inheritdoc cref="_viewport" />
    [SuppressMessage(
        "Usage",
        "CA2213:Disposable fields should be disposed",
        Justification = "Disposed by base.Dispose, which walks Controls; ours was redundant (B402).")]
    private readonly ToolStripStatusLabel _status;

    /// <inheritdoc cref="_viewport" />
    [SuppressMessage(
        "Usage",
        "CA2213:Disposable fields should be disposed",
        Justification = "Disposed by base.Dispose, which walks Controls; ours was redundant (B402).")]
    [SuppressMessage(
        "Major Code Smell",
        "S1450:Private fields only used as local variables in methods should become local variables",
        Justification = "Held for the lifetime of the form; only its disposal moved to the base (B402).")]
    private readonly FlowLayoutPanel _actions;

    /// <inheritdoc cref="_viewport" />
    [SuppressMessage(
        "Usage",
        "CA2213:Disposable fields should be disposed",
        Justification = "Disposed by base.Dispose, which walks Controls; ours was redundant (B402).")]
    private readonly TransportBar _transport;

    /// <inheritdoc cref="_viewport" />
    [SuppressMessage(
        "Usage",
        "CA2213:Disposable fields should be disposed",
        Justification = "Disposed by base.Dispose, which walks Controls; ours was redundant (B402).")]
    private readonly ListView _playlist;

    /// <inheritdoc cref="_viewport" />
    [SuppressMessage(
        "Usage",
        "CA2213:Disposable fields should be disposed",
        Justification = "Disposed by base.Dispose, which walks Controls; ours was redundant (B402).")]
    private readonly TextBox _search;

    /// <summary>The playlist's owner: the library, its order and the rows a search leaves. This window gets strings and counts (D214).</summary>
    private readonly PlaylistPresenter _library = new();

    /// <summary>Owns what a load fills and the load itself; this window is its <see cref="ILoadView"/> (D214).</summary>
    private readonly ViewerPresenter _presenter;

    /// <summary>The composite presenter, for the tests that ask it what a load left (the demo, the map).</summary>
    internal ViewerPresenter Presenter => _presenter;

    // **`_scene` was here until 2026-08-26** (D98) — entity positions already projected to clip
    // space, held that way so the render loop did no per-frame work beyond handing them over. That
    // caching was the right shape for a projection that only changed on a scrub or a resize, and it
    // is exactly the shape that stopped being possible once the camera moved every frame.

    // `_mapFill` was here: the map's surfaces projected to clip space for the overhead fallback.
    // Dead by construction — drawn only when there was no textured map, and built from the map — so
    // it could never produce a visible triangle. Deleted with the projection that built it.

    /// <summary>Where maps come from: the disk first, then the network.</summary>
    /// <remarks>
    /// **Replaced a `MapDownloader?` created on first need** (B188, D90). The lazy construction is
    /// still there and is still right — a viewer that never meets a missing map should never open an
    /// `HttpClient` — but it belongs to the thing that owns the download, not to the window that
    /// happens to notice a map is absent.
    /// </remarks>
    private readonly MapProvider _maps = MapProvider.Installed();

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

    /// <summary>Which member <see cref="Dispose(bool)"/> is releasing, for the crash line (B402).</summary>
    private string _releasing = string.Empty;

    // **`_weaponRoles` was here until 2026-08-25** (B188, D90). It is inside the appearance that
    // `DemoAppearance.Ensure` builds, and `_moment.Appearance` is now the only cache — where there
    // used to be two things to keep in step, a nullable field and a record built from it.
    //
    // **That pairing is what made this member dangerous.** `GameAppearance` CAPTURES the roles, so
    // an appearance built before they were read answers null for every weapon suffix for ever, and
    // that does not fail: it falls back to the primary forms and draws the wrong animation on every
    // player. One cache cannot get out of step with itself.

    // `_drawn` moved with the rebuild that fills it (B188, D90). It is `MomentScene.Drawn`, still a
    // field there so the per-frame allocation happens once rather than per tick.

    // `_surfaceList` was here until 2026-08-25. It is `_loaded.Level.Surfaces` — the field was a
    // copy of one property of a record the form was already holding, and keeping both is how they
    // come to disagree: the catch below cleared the copy and left the record.

    // `_assets` is `_loaded.Assets` — nullable there for the same reason it was here: textures
    // failing costs the textures, not the map.

    // **`_level` was here until 2026-08-25 and it is the reason this warning is worth reading.**
    // `MapLevel` was collapsed into `LoadedMap` and this field was left behind: still declared,
    // still cleared to null in `ClearMap`, still READ by `mat_leafvis` — and never assigned
    // anything. So the overlay drew nothing on every map, with 620 viewer tests green, because
    // `_level?.Leaves` on a permanently-null field is a legal expression that answers null (B196).
    //
    // **That is the answer to "should maps be their own project" (D92): no, because the problem was
    // never a missing boundary.** A cluster of eleven fields that came out of one type is fixed by
    // keeping the type — and by keeping exactly ONE of it, which is the half that got missed.

    // The PVS was a field here until 2026-08-25 (B188). It is read at map load and handed to
    // SoundscapeSystem, which is the only thing that ever asked it anything — so it is a local in
    // ReadMap now. Its reason lives with the code that uses it: only the soundscapes in the
    // listener's own cluster contend, which is what CSoundscapeSystem precomputes at map load
    // (B177). Without it a placement on the far side of the map wins on a long clear traceline.

    /// <summary>The factory, kept so the pieces this builds get their own categories (D83).</summary>
    private readonly ILoggerFactory _loggers;

    /// <summary>One logger per area ViewerLog used to take as a string argument.</summary>
    /// <remarks>
    /// **A logger's category is exactly the old area string**, so `_mapLog.LogInformation(...)`
    /// produces the same `[map]` line the old call did — which matters because the UI suite counts
    /// literal substrings in that file and several diagnostics here are greps over it.
    ///
    /// Fields rather than a lookup by name: a dictionary would move the area from a compile-time
    /// fact to a runtime string, which is the direction this conversion is travelling away from.
    ///
    /// **`_assetLog` is gone, and its absence is a measure of the refactor.** Every `[assets]` line
    /// this form used to write came from reading a map, and reading a map does not happen here any
    /// more — `LoadedMap` and `GameContent` create their own from the factory (D83). The remaining
    /// eight are what a WINDOW still has to say.
    /// </remarks>
    private readonly ILogger _log;

    private readonly ILogger _renderLog;

    private readonly ILogger _demoLog;

    private readonly ILogger _configLog;

    // What light the map casts is `LevelLighting`, the engine's `ComputeLighting` behind an
    // interface (`cdll_int.h:392`). It stopped being a field here on 2026-08-25: the scene owns the
    // one the models ask, and the asset loader takes it as a local while the map is being read. The
    // ambient samples, the world lights and the sun were three fields before that (B188, D90).

    // `_heightRange` is `_loaded.HeightRange`, recorded during the world build rather than after it
    // — a camera projects height on the very first frame, and taking it afterwards leaves one frame
    // drawn with a pass-through depth.

    // **`_players` and `_props` were here until 2026-08-26** (B188, D90). They existed only because
    // `ShowMoment` sampled the timeline in the window; `MomentPresenter` owns them now, and reuses
    // them for the same reason — a moment is rebuilt every frame while playing, so fresh lists would
    // be two allocations a frame.

    // `_moment`, `_moments` and `_effects` below are the load owner's: it constructs them and the frame here still
    // asks them (D214). The model set was a fourth until the world upload's follow-up left with `ProjectMap`.

    // `_weapons` was here until the load set stopped asking the window for it. The form held a
    // second reference to `GameContent.Weapons` so `DemoModelPaths` could reach it — and keeping the
    // two in step was the reason both were assigned in one block. `DemoModels` reads it off the
    // install itself now, so there is one holder and nothing to keep in step.

    /// <summary>Assembles what one moment draws: the draw list, the packing, the poses.</summary>
    /// <remarks>
    /// **`ShowMoment` and the four members it drove** (B188, D90). It is told the tick, the camera
    /// and the followed entity through <see cref="MomentInfo"/> — <c>SetupRenderInfo_t</c>'s
    /// arrangement (<c>clientleafsystem.h:75</c>) — rather than reaching back here for them, and the
    /// one thing it needed a window for is <see cref="IModelUpload"/>.
    /// </remarks>
    private readonly MomentScene _moment;

    /// <summary>Samples the demo for a moment and hands it to <see cref="_moment"/>.</summary>
    /// <remarks>
    /// **The last non-view work in <c>ShowMoment</c>** (B188, D90). The owner's question is what
    /// found it — *"does the view need to hold them to pass them on?"* — and it did not: the two
    /// scene buffers were fields of this form purely because the sampling happened here.
    /// </remarks>
    private readonly MomentPresenter _moments;

    // **`_clock` was here until 2026-08-26.** It held the `PlaybackClock` that turns real time into
    // demo ticks — the SAME object `PlaybackPresenter` already had, because `DemoSystems.Open`
    // handed it to the presenter and returned it to this window as well.
    //
    // Two references to one piece of state is how two answers to one question appear. The presenter
    // owns the clock (D62), so it now answers `Position` and takes `Seek`, and this window asks it
    // rather than keeping a copy.

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

    // **`_worldIsStale` was here until 2026-08-26** (B188, D90). It is
    // `WorldPresenter.NeedsProjecting`, beside `TexturesAreCurrent`, which it resembled in every
    // respect except where it lived — both answer "is what the projector produced still good".
    //
    // The window still reports the nine events that invalidate it, which is right: a resize, a
    // camera mode change, a dolly, a drag, a spectator switch and a map load are all things a window
    // knows and a presenter does not.

    // **`_zoom` and `_lookingAt` were here until 2026-08-26** (D98) — how far into the orthographic
    // map the view was magnified, and where it was centred. Both are meaningless without that
    // projection: the free camera has a position, not a zoom and a centre.

    // **`_freeAngles` is gone entirely as of 2026-08-26** (B206). It was an accessor onto
    // `FreeCameraController.Angles` for the drag and wheel handlers to read and write; both now ask
    // the controller to move itself, so the window neither reads nor writes the camera's angles.
    //
    // It went in two steps, each one announced by the analyzer: the setter died when `Drag` moved,
    // and the property died when `Dolly` moved. That is a cleaner proof than reading the file — the
    // second removal was not planned, it was reported.

    // FreeEntryDistance (800 units) went with the orbit placement on 2026-08-22 (D67). The camera
    // no longer sits a fixed distance from a focus point — it is placed above the map at whatever
    // height frames the play area, which is a computed distance rather than a chosen one.

    /// <summary>Which camera the viewport is drawn through, and every decision about changing it (D214).</summary>
    private readonly CameraPresenter _camera;

    // **`DegreesPerPixel` was here until 2026-08-26** (B206). It is
    // `FreeCameraController.DegreesPerPixel`, along with the drag arithmetic it belonged to — which
    // this file had one copy of and the unused `FreeLookState` had another. How far a drag turns a
    // camera is not a fact about a window.

    /// <summary>Which key performs which action (D68).</summary>
    /// <remarks>
    /// **Actions are bound, not keys, which is how TF2 works.** Its spectator HUD prints
    /// `[%jump%]` beside "Switch Camera Mode" and substitutes the player's own binding — nothing in
    /// the game hardcodes Space. Defaults follow TF2 where TF2 has an equivalent, and the table is
    /// meant to be loaded from settings so a user can rebind exactly as they would in the game.
    /// </remarks>
    private KeyBindings _bindings = new();

    /// <summary>Where a drag started, in viewport pixels.</summary>
    private Point? _dragFrom;

    // **`_heightCut` was here until 2026-08-26** (B213). Its doc explained it as "what lets an
    // OVERHEAD VIEW see inside a building" — which names the projection it belonged to, and that
    // projection went with D98.
    //
    // It survived the ortho removal because it looked like a rendering feature rather than a piece
    // of the overhead camera: the shader discarded on depth, and under an orthographic top-down
    // view depth IS height. Under the free camera it is distance from the eye, so the control cut
    // away whatever was nearest. The owner's verdict — *"that was a ortho thing that should be
    // ripped out and never worked in the first place"* — is what a half measure leaves behind
    // (D98: *"half measures are why we have old ortho code unused, still around"*).

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

    // `_surfaces` was here: the map's faces in world units, kept so the overhead fill could be
    // re-projected on resize. The fill is gone, so the only thing that ever read this is gone with
    // it — the cascade the owner predicted when the projection came out.

    /// <summary>The main menu, and the items whose checked state is read from here.</summary>
    /// <remarks>
    /// **Eleven fields and a dictionary were here until 2026-08-26** (B188, D90), each holding one
    /// menu item, alongside the 363 lines of constructor that built them. They are properties of
    /// <see cref="ViewerMenu"/> now, so a reader of `_menu.Wireframe.Checked` can see where the
    /// value comes from — which `_wireframe.Checked` could not say.
    ///
    /// **Still view code, and still WinForms.** Unlike the rest of the thin-view work this is not a
    /// layer fix: menus belong on this side. What was wrong was that one constructor built the
    /// window, composed twenty collaborators, laid out the controls AND built the menu.
    /// </remarks>
    private readonly ViewerMenu _menu;

    /// <summary>Raises or lowers what the log accepts, wired by the composition root.</summary>
    /// <remarks>
    /// **A delegate rather than the provider itself**, because the form has no business holding a
    /// sink: it knows the setting, `Program` knows the provider, and this is the seam between them.
    /// Null in every test, where there is no file to write to and nothing to turn down.
    ///
    /// Hidden from the designer (WFO1000): a public property on a Form is otherwise assumed to be
    /// something the designer should serialise, and a delegate is not.
    /// </remarks>
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Action<LogLevel>? SetLogVerbosity { get; set; }

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

    // The "have we already reported this viewmodel" state moved into ViewmodelScene on 2026-08-24
    // (B188). It belongs with the decision it dedupes: what the lines answer is "which model,
    // playing what", which is a question about a WEAPON and changes when the player switches — and
    // holding that flag on the form is what let it drift out of step with the thing it described.

    // `_specular`, `_fullbrightMenu`, `_drawWorld`, `_drawEntities` and `_debugMenu` were here
    // until 2026-08-26. They are properties of `ViewerMenu` (B188, D90).

    /// <summary>Which <c>mat_fullbright</c> substitution is showing.</summary>
    public Fullbright Fullbright { get; private set; } = Fullbright.Off;

    /// <summary>Which of Valve's per-surface debug views are showing.</summary>
    private DebugModes _debug = DebugModes.None;

    // `_entityClasses` was here until 2026-08-25, with `LoadEntityPalette` beside it. Valve's entity
    // palette is `GameContent.EntityClasses` — read from the FGDs the game ships, which makes it
    // install data rather than window state (B188, D90).

    // `_brushModelClasses` was here until 2026-08-25. It is `_level.BrushModelClasses` — the form
    // was copying the record's dictionary into a field of its own, entry by entry, on every map
    // read. `MapLevel` already built that join from the entity lump, which is where it belongs.
    //
    // `EntityTint` and Hammer's default colour went with them, to `LoadedMap` — the tint needs the
    // install's FGD palette AND this map's brush-model classes, and the only place that holds both
    // is the loaded map. The window was the join only because it happened to hold both fields.

    /// <summary>Chooses a lighting substitution and ticks the matching menu item.</summary>
    /// <param name="mode">Which substitution to show.</param>
    /// <remarks>
    /// Public so a test can drive it without synthesising a menu click. The menu items call this
    /// too, so there is one path rather than a UI path and a test path that can disagree.
    /// </remarks>
    public void SetFullbright(Fullbright mode)
    {
        Fullbright = mode;

        foreach (ToolStripItem entry in _menu.FullbrightMenu.DropDownItems)
        {
            if (entry is ToolStripMenuItem item)
            {
                item.Checked = item.Name == FullbrightItemId + mode;
            }
        }

        _renderLog.LogInformation("{Message}", $"mat_fullbright {(int)mode}");

        if (_device is { } device)
        {
            device.Fullbright = mode;
        }

        _viewport.Invalidate();
    }
    // `_borderlessMode`, `_exclusiveMode` and `_textureQualityItems` were here until 2026-08-26.
    // They are `ViewerMenu.Borderless`, `.Exclusive` and `.TextureQualityItems` (B188, D90).

    /// <summary>The settings' owner; this window talks to it in plain values (D214).</summary>
    private readonly SettingsPresenter _settingsOwner;

    /// <summary>Controls hidden while full screen, all of them direct children of the form.</summary>
    /// <remarks>
    /// **Direct children, and that is the whole invariant.** Hiding a control does not give its
    /// space back unless the hidden control is the one that is docked. When the playlist gained a
    /// search box the two moved into a panel, and the code kept hiding the playlist and the search
    /// box - so full screen left a 280-pixel empty panel docked to the right and the viewport came
    /// out the wrong width. Nothing about that is visible in a unit test through Control.Visible,
    /// which reports effective visibility and reads false for everything on a form never shown.
    /// </remarks>
    private readonly List<Control> _hiddenInFullScreen = [];

    private Device3D? _device;
    private bool _rendering;
    private OverlayWindow? _overlay;
    private FormBorderStyle _borderBeforeFullScreen;
    private FormWindowState _stateBeforeFullScreen;
    private Rectangle _boundsBeforeFullScreen;
    private int _transportIndexBeforeFullScreen;

    /// <summary>Builds the shell. No device is created here; see the remarks on the type.</summary>
    /// <param name="initialPaths">
    /// Files or folders to open at startup, as passed on the command line by a file association.
    /// </param>
    /// <remarks>
    /// **The command line goes through the same code as the Open buttons**, deliberately. A file
    /// association that had its own loading path would drift from the in-application one - the
    /// two would disagree about folders, about multi-select, about what counts as a demo - and
    /// the difference would only show up for whichever one is used less. There is one entry
    /// point, <see cref="AddToLibrary"/>, and both callers use it.
    /// </remarks>
    /// <summary>Opens the window with no logging, for a test that wants a form and not a log.</summary>
    /// <remarks>
    /// **An overload rather than an optional parameter, because `params` cannot follow one.**
    /// `MainForm(ILoggerFactory? loggers = null, params string[] paths)` would bind
    /// `new MainForm("a.dem")` to the factory and fail. Thirty test call sites construct this
    /// directly and none of them wants a log, so the shape they already use keeps working.
    /// </remarks>
    public MainForm(params string[] initialPaths)
        : this(NullLoggerFactory.Instance, initialPaths)
    {
    }

    /// <summary>Opens the window, reporting through the given loggers.</summary>
    /// <param name="loggers">Where the viewer reports what it did (D83).</param>
    /// <param name="initialPaths">Files or folders to open.</param>
    public MainForm(ILoggerFactory loggers, params string[] initialPaths)
    {
        ArgumentNullException.ThrowIfNull(loggers);

        // **Eight areas, so a factory rather than a logger.** MainForm is the one type here that
        // writes to most of them — map, assets, render, demo, audio, spectate, viewer, config — and
        // each keeps the category ViewerLog used as its area string, so the log format is unchanged.
        _loggers = loggers;
        _log = loggers.CreateLogger("viewer");
        _renderLog = loggers.CreateLogger("render");
        _demoLog = loggers.CreateLogger("demo");
        _configLog = loggers.CreateLogger("config");

        // **A local, not a field.** Both readers are constructed here and neither hands it back, so
        // the window has no reason to keep a reference — and keeping one is what would let a later
        // edit report a phase to a ledger nobody prints.
        FrameLedger ledger = new();

        _transport = new TransportBar();

        // **Playback belongs to a presenter now, not to this form (D62).** What used to be three
        // event handlers and a stopwatch here — scrub, play/pause, speed, each with a rule about
        // when to restart the watch — is `PlaybackPresenter`, which knows nothing about WinForms
        // and has sixteen tests. None of that logic could be tested while it lived in this file.
        //
        // The form keeps exactly one job in this area: the moment changed, so redraw.
        _playback = new PlaybackPresenter(_transport, new StopwatchTime());

        // **What a load fills is composed by its owner** (D214): the model set, the sound cache, soundscape and
        // presenter, the moment scene and presenter, the frame presenter, the level's systems and the demo's — in the
        // order this constructor built them. The three fields below are the same objects, kept because the frame still
        // asks them; nothing here constructs or replaces one.
        _settingsOwner = new SettingsPresenter(loggers);
        _presenter = new ViewerPresenter(
            this, loggers, ledger, _viewmodelScene, this, () => CameraWindowNow, () => _launch.SpectateWho, _maps, _settingsOwner, _playback, ClientConVar, _shutdown.Token);
        _moment = _presenter.Moment;
        _moments = _presenter.Moments;
        _effects = _presenter.Effects;
        _camera = _presenter.Camera;

        _listener = () => _camera.FirstPerson ? _camera.FirstPersonCamera() : _camera.FreeLookCamera();
        _listenerEyes = () => _camera.FirstPerson ? _camera.FollowedEntity() : null;
        // What the presenter asks of this window at its own moment, attached once; it gives the HUD the rest (D214).
        _presenter.AttachWindow(
            new ViewerWindow(
                () => WindowFrameNow,
                ShowOnly,
                () => _bindings,
                ClientConVar,
                () => _vguiClock.Elapsed.TotalSeconds,
                () => _console.IsHeld(ViewerAction.ShowScores),
                () => _clock.LastFrameSeconds),
            _gdi);

        _frames = new FrameReporter(ledger, _presenter.Models, new StopwatchTime(), _renderLog);


        // **A capture flag, because the alternative was asking a person to press F12.** Several
        // rendering defects this session were found by the owner photographing their own screen and
        // describing it, which is slow for them and leaves the loop dependent on someone being at
        // the machine. "--shot <file>" loads, seeks, draws, writes a PNG and exits; "--tick <n>"
        // says when.
        //
        // Deliberately not a test harness: it drives the real viewer through the real renderer,
        // which is the whole reason the offscreen target was deleted. See CaptureViewport.
        // **Parsing a command line is not window work** (B188, D90). What comes back is a record;
        // the six `_shot*` fields it used to write into are gone, and the two things it configures
        // that live elsewhere — the settings and the spectator target — are applied here where both
        // are visible together.
        // What the options say of the settings, the game folder, the spectator, the lighting and the HUD is applied by
        // the presenter that owns each, in the order this constructor applied them (`ViewerPresenter.ReadLaunch`).
        _launch = _presenter.ReadLaunch(initialPaths);

        // **`--look` and `--zoom` were parsed here and read by nobody** (B226). D98 removed the
        // orthographic camera they were written for and kept the fields with a note saying what
        // they should mean for a world-placed camera was "a question for whoever reimplements
        // them". An accepted-and-dropped option is the same defect as B223, and the answer needed
        // no invention: the overhead placement is the map view's successor, so they reshape what it
        // frames.
        _camera.Free.LookAt = _launch.LookAt;
        _camera.Free.Zoom = _launch.Zoom;

        // **This line was MISSING for a day and `--shot` did nothing at all** (B196). `_shotPath`
        // stayed out of the record because taking the shot CONSUMES it, and staying out of the
        // record is exactly how it stopped being assigned: the six `_shot*` fields the parser used
        // to write into went away together, and the one that had to survive them was not wired back
        // up. `TakeAutomaticShot` then read a permanently-null field and returned every frame.
        //
        // Nothing failed. No test passes `--shot`, so the whole option was covered by nobody.
        _opening = new OpeningSequence(
            _launch.ShotPath, OpeningFrames, SettleFrames, PatienceFrames);

        initialPaths = [.. _launch.Paths];

        // **The player's own TF2 controls, loaded before anything can be pressed (D69/D70).** This
        // is what makes the console a feature rather than a capability: without it the interpreter
        // runs the shipped defaults for ever and every test of it exercises code the viewer never
        // reaches. Done in the constructor because the alternative — loading with the map, where the
        // archives are already opened — would leave the keys wrong until a demo was opened.
        LoadUserConfig();
        _console.Triggered += OnConsoleAction;

        _presenter.OpenAudio();

        Text = "TF2 Demo Salvage";
        Name = MainWindowId;
        AccessibleName = "TF2 Demo Salvage viewer";
        Width = 1280;
        Height = 720;
        ApplyGeometryOverride();

        // **A `ViewportPanel` rather than a `Panel`, so it can actually hold focus** (B216). The
        // `TabStop` below was set on a plain panel and did nothing — WinForms gates focus on
        // `ControlStyles.Selectable`, which `Panel` clears. See `ViewportPanel` for what that cost.
        _viewport = new ViewportPanel
        {
            Name = ViewportId,
            AccessibleName = "Demo viewport",
            AccessibleDescription = "Top-down view of the demo being played back.",
            AccessibleRole = AccessibleRole.Graphic,
            Dock = DockStyle.Fill,

            // The panel is a presentation surface, not a painting surface. Letting WinForms paint
            // its background produces a flicker between its fill and the swap chain's present.
            TabStop = true,
        };
        _viewport.HandleCreated += OnViewportHandleCreated;
        _viewport.Resize += OnViewportResize;
        // **A Panel does not take focus, so its own wheel event may never fire.** The form's does,
        // and the pointer position is converted into the viewport - which also means the wheel
        // works without clicking the map first, the behaviour anyone expects from a map viewer.
        _viewport.MouseWheel += OnViewportWheel;
        MouseWheel += OnFormWheel;
        _viewport.MouseDown += OnViewportMouseDown;
        _viewport.MouseMove += OnViewportMouseMove;
        _viewport.MouseUp += OnViewportMouseUp;

        // **No entity or class list.** That is what the parser works in, not what someone watching
        // a demo wants on screen - anyone who needs it can export the assembly script, which says
        // far more than a two-column list ever would.
        //
        // The action row sits UNDER the play bar: these are things done to the demo as a whole,
        // where the transport is about the moment being watched.
        _actions = new FlowLayoutPanel
        {
            Name = "ActionBar",
            AccessibleName = "Demo actions",
            Dock = DockStyle.Bottom,
            Height = 40,
            Padding = new Padding(8, 4, 8, 4),
            FlowDirection = FlowDirection.LeftToRight,
        };

        _actions.Controls.Add(ActionButton(
            OpenButtonId, "Open", "Open one or more demo files.", (_, _) => OpenDemo()));
        _actions.Controls.Add(ActionButton(
            OpenFolderButtonId, "Open folder", "Open a folder of demos as a playlist.",
            (_, _) => OpenFolder()));
        _actions.Controls.Add(ActionButton(
            ExportButtonId, "Export", "Export the demo as assembly text that compiles back to it.",
            (_, _) => FileWork = ExportDemo()));
        _actions.Controls.Add(ActionButton(
            CompileButtonId, "Compile", "Rebuild a byte-identical demo from its assembly text.",
            (_, _) => FileWork = CompileDemo()));

        // The playlist replaces the entity list that used to sit here. It lists demos and the
        // folder each came from - navigation, not parser internals - and allows multi-select so
        // several can be opened at once the way a file browser does.
        // **Virtual mode, and the reason is measured rather than precautionary.** Someone with
        // thousands of POV demos filters this list on every keystroke, and rebuilding it is the
        // whole cost: at 20,000 entries, matching takes 0.20 ms and populating a real grouped
        // ListView takes 188 ms - a thousand times more. In virtual mode the control asks for the
        // rows it is about to draw, so the same case costs 0.4 ms and stays flat as the archive
        // grows.
        //
        // The price is grouping, which virtual mode does not support. Folder becomes a column
        // instead and the list is sorted by folder then name, so it still reads as folders - and
        // unlike a group header, a column can be read next to a name that shares it.
        _playlist = new ListView
        {
            Name = PlaylistId,
            AccessibleName = "Playlist",
            AccessibleDescription = "Demos available to play, listed by folder.",
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            HideSelection = false,
            MultiSelect = true,
            VirtualMode = true,
        };
        _playlist.Columns.Add("Demo", 170);
        _playlist.Columns.Add("Folder", 100);
        _playlist.RetrieveVirtualItem += OnRetrieveVirtualItem;

        // Double-click and Enter both load, matching how a file browser and a video player behave.
        // Selecting alone does not: browsing a playlist should not read headers off disk.
        // **Not `async void`.** The returned task is kept in `Loading`, so a failure has somewhere
        // to be reported and a test has something to await — an `async void` handler throws on a
        // thread with no handler and takes the process down instead.
        _playlist.ItemActivate += (_, _) => Loading = LoadSelected();

        // A real archive folder is hundreds of files called esea_match_13977649.dem, where
        // scrolling finds nothing. The box sits above the list rather than beside it so the list
        // keeps its full width for names that are already too long for the column.
        _search = new TextBox
        {
            Name = SearchId,
            AccessibleName = "Search demos",
            AccessibleDescription = "Type to narrow the playlist by demo name or folder.",
            Dock = DockStyle.Top,
            PlaceholderText = "Search demos...",
        };

        // Filtering as the user types, with no button to press. The work is a substring scan over
        // a list already in memory, so there is nothing to debounce.
        _search.TextChanged += (_, _) => RefreshPlaylist();

        Panel playlistPanel = new()
        {
            Name = "PlaylistPanel",
            AccessibleName = "Playlist panel",
            Dock = DockStyle.Right,
            Width = 280,
        };

        // Fill before Top, for the same reason the form itself adds the viewport first.
        playlistPanel.Controls.Add(_playlist);
        playlistPanel.Controls.Add(_search);

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
            _viewport.Invalidate();
        };

        _status = new ToolStripStatusLabel
        {
            Name = StatusId,
            Text = "No demo loaded.",
        };

        // **No static AccessibleName on a live readout.** UIA reports Name, and Name comes from
        // AccessibleName whenever it is set - so labelling this "Status" made every status
        // message invisible to automation and to a screen reader alike, which both then read the
        // word "Status" forever. Caught by the first UI test that asked what the status said.
        _status.TextChanged += (_, _) => _status.AccessibleName = _status.Text;
        _status.AccessibleName = _status.Text;

        StatusStrip statusStrip = new() { Name = "StatusStrip", AccessibleName = "Status bar" };
        statusStrip.Items.Add(_status);

        // **The menu was 363 lines here** (B188, D90) — about half this constructor, and the largest
        // single thing left in the file. It is view code and it stays view code; what was wrong was
        // that one constructor built the window, composed twenty collaborators, laid out the
        // controls AND built the menu.
        //
        // **Fourteen delegates rather than a `this`.** `ViewerMenu` holding a `MainForm` would be a
        // menu for this one window; holding the actions describes what a viewer frontend has to be
        // able to do, which is the list any replacement must satisfy. The owner's reason for wanting
        // the seam: *"that makes the swap to ImGUI or QT or any other cross platform UI frontend
        // much easier"*.
        _menu = new ViewerMenu(
            new ViewerMenuActions(
                OpenDemo: OpenDemo,
                // Posted, so the menu's click returns and the menu closes before the modal dialog
                // opens; a dialog shown inside the click left UIA's Invoke waiting on it.
                ExportDemo: () => BeginInvoke(() => FileWork = ExportDemo()),
                CompileDemo: () => BeginInvoke(() => FileWork = CompileDemo()),
                Exit: Close,
                SetFullScreen: SetFullScreen,
                SetFullScreenExclusive: SetFullScreenExclusive,
                SetTextureQuality: SetTextureQuality,
                SetSurfaceColours: SetSurfaceColours,
                SetFrameRateMeter: SetFrameRateMeter,
                SetPositionReadout: SetPositionReadout,
                SetWireframe: SetWireframe,
                SetFullbright: SetFullbright,
                SetDrawWorld: SetDrawWorld,
                SetDrawEntities: SetDrawEntities,
                SetDebugMode: SetDebugMode,
                SetSpecular: SetSpecular,
                SetPhong: SetPhong,
                Screenshot: CaptureViewportToFile,
                SetChosenHud: SetChosenHud,
                // Posted for the same reason as Export: the menu closes before the dialog opens.
                ChooseGameFolder: () => BeginInvoke(ChooseGameFolder)),

            // **Our OWN custom/ folder, never `tf/custom`** (D193) — the picker lists what a player
            // imported into the program, not what is in their TF2 install.
            _settingsOwner.Menu(ProgramCustomFolder),

            // **The menu's shortcuts come from the same table the flight keys do** (B214, D101).
            // Fourteen `ShortcutKeys = Keys.<something>` literals lived in `ViewerMenu` until now,
            // six of them on keys TF2 binds to something else.
            _bindings);

        MenuStrip menu = _menu.Strip;


        // Added in reverse z-order: WinForms docks the LAST control added first, so Fill must go
        // in before every docked edge or the viewport ends up underneath them.
        // Docking order, established by MEASURING rather than by reasoning about it. A LATER
        // added bottom control ends up LOWER on screen, so transport before actions puts the
        // action row beneath the play bar - operations on the demo as a whole below the controls
        // for the moment being watched, which is what was asked for.
        //
        // Written this way round because both plausible theories were tried and only the numbers
        // settled it: play button top=666, open button top=709. A UI test now pins it, since a
        // form that was never shown has no layout for a unit test to inspect.
        Controls.Add(_viewport);
        Controls.Add(playlistPanel);
        Controls.Add(_transport);
        Controls.Add(_actions);
        Controls.Add(statusStrip);
        Controls.Add(menu);
        Controls.Add(_loading);
        MainMenuStrip = menu;

        // The docked controls that give their space to the viewport in full screen. The menu is
        // handled separately because a MenuStrip is not hidden the same way.
        _hiddenInFullScreen.Add(_actions);
        _hiddenInFullScreen.Add(playlistPanel);

        // The status strip too: 22 pixels measured on a 1080-line display, and full screen means
        // the map gets the display. What the status line says is not worth a band across it, and
        // the transport moves to a transparent overlay for the same reason.
        _hiddenInFullScreen.Add(statusStrip);

        // **The scene starts focused, not the playlist** (B216). WinForms hands focus to the first
        // tab stop otherwise, and every shortcut is then asked about a list nobody is using. This is
        // also what a person expects: the window opens looking at the demo, so the demo has the keys.
        ActiveControl = _viewport;

        // **A missing install asks for the folder (D210), before the demo below opens and reads its
        // map** — registered first, so it runs first, and the dialog is modal, so the open waits.
        Shown += (_, _) => AskForGameFolderIfMissing();

        if (initialPaths.Length > 0)
        {
            AddToLibrary(initialPaths);

            // **One file named on the command line is an instruction to open THAT demo.** This is
            // the file-association case: double-clicking a .dem in Explorer has to end with the
            // demo on screen, because listing it in a playlist and waiting is not what opening a
            // file means anywhere else.
            //
            // It stays the same code path - AddToLibrary then LoadDemo, exactly what a
            // double-click in the playlist does - so the two cannot drift apart.
            //
            // A folder is deliberately excluded. Opening a folder means "here is a playlist", and
            // picking one of its demos to start playing would be guessing which.
            // **Say which branch was taken** (B401). A `--shot` run that quietly did not open its
            // demo looked identical to one whose renderer drew nothing, and the log distinguished
            // them nowhere: no `[demo]` line at all is the same absence for "we chose not to open
            // it" and "opening it failed".
            //
            // **Opened once the window is up, not inside this constructor.** Loading it here kept
            // the window off screen for the whole decode and map read — 95 seconds on a 26-minute
            // match — which is what the owner asked a loading screen for. The `--shot` countdown
            // waits on the load rather than on frames (see `TakeAutomaticShot`).
            if (initialPaths.Length == 1 && File.Exists(initialPaths[0]))
            {
                _openOnShow = initialPaths[0];
                Shown += (_, _) => Loading = OpenCommandLineDemo();
            }
            else if (initialPaths.Length == 1)
            {
                string missing = $"{initialPaths[0]} is not a file that exists; nothing was opened";
                _demoLog.LogWarning("{Message}", missing);
                Shown += (_, _) => FailHeadless(missing);
            }
            else
            {
                _demoLog.LogInformation(
                    "{Message}",
                    $"{initialPaths.Length} paths listed in the playlist; none opened on its own");

                // **A capture or a measurement has exactly one subject** (2026-10-11). A demo under a folder with a
                // space in its name, launched through something that joins its arguments unquoted, arrives as its
                // pieces: "a playlist", nothing opened, and `--shot` then drew 30,000 empty frames and exited zero.
                string several =
                    $"{initialPaths.Length} paths were given where a capture takes one demo " +
                    $"({string.Join(" | ", initialPaths)}); a path with a space in it must arrive as ONE argument";

                Shown += (_, _) => FailHeadless(several);
            }
        }
    }

    /// <summary>Environment variable pinning the window size, as WIDTHxHEIGHT.</summary>
    public const string WindowSizeVariable = "TF2VIEW_WINDOW_SIZE";

    /// <summary>Environment variable pinning the window position, as X,Y.</summary>
    public const string WindowPositionVariable = "TF2VIEW_WINDOW_POS";

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

    // **`CameraVariable` was declared HERE as well, and this copy was read by nothing** — removed
    // 2026-08-29. `FreeCameraController` declares the same name and is the only place that reads
    // the environment for it, so this was a second answer to one question with no way to notice the
    // two drifting apart (`docs/memory/one-place-or-it-drifts.md`). Found while auditing every
    // viewer environment variable after B223; the doc comment it carried was accurate, which is
    // exactly what made it look alive.

    /// <summary>
    /// Applies a window geometry override, so a developer can reproduce CI's tiny screen.
    /// </summary>
    /// <remarks>
    /// GitHub's Windows runners do have a desktop, but a small one - PokemonBattleJournal pins its
    /// UI tests to 754x512 at (85,78) to match. Layout bugs that only appear when the window is
    /// short are otherwise found by CI and not reproducible locally on a 1080p screen.
    ///
    /// **Position matters as much as size, and that is not obvious.** Setting only the size leaves
    /// the window at (0,0), where screen coordinates and window-relative coordinates are the same
    /// number - which hides any confusion between the two. PBJ hit exactly that: a coordinate
    /// space bug invisible locally until CI, whose window sits at an offset, failed on it.
    /// </remarks>
    private void ApplyGeometryOverride()
    {
        // **The parsing moved to `WindowGeometry`** (B208). What is left is the only view part:
        // reading the environment and assigning to the window. Whether `"0x720"` counts as a usable
        // size is a question about text, and it could not be tested while it lived here.
        if (WindowGeometry.Size(Environment.GetEnvironmentVariable(WindowSizeVariable))
            is { } size)
        {
            Width = size.Width;
            Height = size.Height;
        }

        if (WindowGeometry.Position(Environment.GetEnvironmentVariable(WindowPositionVariable))
            is { } at)
        {
            StartPosition = FormStartPosition.Manual;
            Location = new Point(at.X, at.Y);
        }
    }

    /// <summary>Loads a demo by path, on this thread: <see cref="ViewerPresenter.LoadDemo"/>.</summary>
    /// <param name="path">Full path to the demo.</param>
    public DemoLoadResult LoadDemo(string path) => _presenter.LoadDemo(path);

    /// <summary>Opens a demo without freezing the window: <see cref="ViewerPresenter.LoadDemoAsync"/>.</summary>
    /// <param name="path">Full path to the demo.</param>
    /// <returns>A task that completes when the demo is on screen.</returns>
    public Task<DemoLoadResult> LoadDemoAsync(string path) => _presenter.LoadDemoAsync(path);

    /// <summary>Loads the demo selected in the playlist, if any.</summary>
    /// <remarks>
    /// **A failure to load is reported, never thrown.** Half the point of this project is opening
    /// demos other software rejects, so a file that will not parse is an expected outcome and has
    /// to leave the application usable - the user picks another one from the same playlist.
    /// </remarks>
    public Task<DemoLoadResult> LoadSelected()
    {
        if (_playlist.SelectedIndices.Count == 0)
        {
            return NothingSelected;
        }

        // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
        if (_library.Row(_playlist.SelectedIndices[0]) is not { } selected)
        {
            return NothingSelected;
        }

        return _presenter.LoadDemoAsync(selected.Path);
    }

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

    // `_modelsUploaded` was here until 2026-08-25. It is `MomentScene.Uploaded` now, beside the
    // packing it guards (B188, D90) — and the question it answers is unchanged: "is the packed
    // geometry on the device right now", which is NOT "has the set grown". Conflating them was B148.

    // `MapLines` was here: the map outline projected into clip space for the overhead view. It had
    // no consumer in production or in a test, and the view it served was removed. Deleted with the
    // projection that built it (B98's ghost); `MapOutline` still computes the play-area BOUNDS,
    // which is a different object and is still read.

    /// <summary>Runs the player's own TF2 configs over the shipped defaults.</summary>
    /// <remarks>
    /// **Their controls, not ours, and that is the requirement in one line (D69).** Someone running
    /// mastercomfig has already decided every one of these once; being asked to decide again in a
    /// second, different settings file is the friction this exists to remove.
    ///
    /// **Failure here costs the player's bindings and nothing else.** The defaults are a complete,
    /// working set of controls, so a missing install, an unreadable VPK or a config full of things
    /// this viewer has never heard of all end the same way: the viewer starts. That is why the catch
    /// is broad rather than narrow — but it is logged with its message, because the alternative is a
    /// player whose config silently does nothing and no way to find out why.
    ///
    /// **The two counts are logged together deliberately.** "13 of 78 binds" and "0 of 0" say
    /// different things; "loaded" says neither.
    /// </remarks>
    private void LoadUserConfig()
    {
        // **Assigned only when something was actually read**, which is what the old body did by
        // returning early. `LoadFrom` answers null rather than handing back its own defaults, so an
        // unreadable config cannot quietly replace the bindings this form already has.
        if (_console.LoadFrom(_maps.GameFolder(), _loggers, _configLog, ProgramCustomFolder) is { } loaded)
        {
            _bindings = loaded;
        }
    }

    // **`FindGameFolder` and `FindMap` were here until 2026-08-26** (B188, D90). They are
    // `MapProvider.GameFolder` and `MapProvider.Locate` now, and between them they held two of the
    // THREE hand-typed copies of `ProgramFilesX86/Steam/steamapps/libraryfolders.vdf` this file
    // carried — the third being the downloader's default folder, which had to agree with `FindMap`'s
    // search path and did so only because the same components were typed in both.
    //
    // Where Steam puts things is not a fact about a window.

    // `LightAt`, `SunAt`, `ReportLightTerms` and their two report fields were here until
    // 2026-08-25. They are `LevelLighting` in the Scene project now (B188, D90): the engine answers
    // this query through `IVEngineClient::ComputeLighting` (`cdll_int.h:392`) rather than the window
    // owning the map's lighting lumps, and away from a form they can finally be tested — a control
    // pair for the leaf lookup, one for the sun's sky trace, and one asserting the per-place report
    // is `Debug` so a release run never pays for it (B191).
    //
    // `ModelGeometry` went the same way and for the same reason: it knew that geometry is
    // `MapAssets.EntityModels` keyed by path, so a second frontend would have had to know it too.
    // The lookup is `MapAssets.Geometry` and the renderer reads it through `EntityModelSet.Geometry`
    // — an interface pointer set at map load, which is how the client reaches `modelinfo`
    // (`IVModelInfo.h:146`) rather than being handed a source at every call.
    //
    // `ClassModelPaths` is `GameContent.ModelPaths` now, beside the class scripts it reads. It is
    // what the install says, not what the window knows.

    // **`EnsureWeaponRoles` was here until 2026-08-26** (B188, D90). It is `PlayerAppearances`,
    // asked by `MomentPresenter` once per moment, with the two halves set by whoever learns them:
    // `DemoSystems.Open` supplies the demo and `LevelSystems.Install` the install.
    //
    // Its own documentation explains why it has to be lazy, and the reason is worth keeping where
    // the code went: the first version built this beside the timeline, which is where the weapon
    // classes become known — and the archives open AFTER that, so the roles were never read.
    // Nothing failed. Every suffix came back null, the lookup fell back to the primary forms, and
    // the viewer drew exactly what it had drawn before. The unit tests passed throughout, because
    // they call `WeaponRoles` directly. It was caught by a line missing from the log, which is the
    // only instrument that could have caught it — the defect was in the wiring and every component
    // was correct.

    // **`PlayerModel` was here until 2026-08-25** (B188, D90). It was a one-line delegation to
    // `PlayerProps.ModelFor`, and a delegating wrapper is the view knowing that a domain operation
    // exists — which being short does not make into view code.
    //
    // It also built a SECOND `GameAppearance` on every call, beside the one `_moment.Appearance`
    // already holds. Its only caller was the marker pass, which now passes that one, so there is a
    // single appearance where there were two.
    //
    // The rule it carried is worth keeping written down, because it is the reason the two passes
    // must ask ONE question: a player drawn as a model must not also get a flat marker on top of
    // it, and a player without a model must still get one or they vanish. Asked in two places the
    // answers drift, and they did — the markers were still being drawn over the models the moment
    // those started working, which hid whether the models were there at all.

    // `DemoModelPaths` and `WornModelPaths` are `DemoModels.Needed` and `DemoModels.Worn` (B188,
    // D90) — a question about a demo and an install, asked from a window that had nothing to do
    // with either.

    /// <summary>What the command line asked for.</summary>
    /// <remarks>
    /// **Six `_shot*` fields until 2026-08-25**, written one at a time by a parser in this form.
    /// Reading a command line is not window work and could not be tested where it was, so every
    /// option was covered only by whichever UI test happened to pass one (B188, D90).
    /// </remarks>
    private LaunchOptions _launch;

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

    // `_shotDelay` was here until 2026-08-26. It is `OpeningSequence`'s countdown (B188, D90).

    // `ReadCaptureOptions` is `LaunchOptionsReader.Read` in Presentation (B188, D90), and it returns
    // a record rather than writing into six fields as it goes. Thirteen tests came with the move; it
    // had none, because reaching it meant constructing a form — so every option was covered only by
    // whichever UI test happened to pass one.

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
                BeginInvoke(Close);
                break;

            // **Nothing is written, on purpose** (B401). An empty PNG is what the three silent
            // breaks of `--shot` all produced, and it reads as "the viewer drew nothing" rather
            // than "the viewer was never given anything". A line and a non-capture say which.
            case OpeningStep.GiveUp:
                _log.LogWarning(
                    "{Message}",
                    $"no demo loaded after {PatienceFrames} frames, so --shot wrote nothing");
                _opening.TakeShotPath();
                BeginInvoke(Close);
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
        foreach (int through in OpeningSequence.SeekThrough(Environment.GetEnvironmentVariable("TF2VIEW_SEEK_THROUGH")))
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
            _menu.SurfaceColours.Checked = true;
        }

        // **After the seek, because entering the first-person view reads the moment.** The camera
        // is placed from the recorded view or from the followed player at the CURRENT tick, so
        // switching before the clock moves photographs the right mode at the wrong instant — and
        // the picture looks like a camera bug rather than an ordering one.
        //
        // Skipped when the mode is already first person, because on a point-of-view recording the
        // D128 lock entered it at load — and `ToggleFirstPerson` TOGGLES, so calling it again
        // would be an attempt to leave, refused with a warning on every `--first-person` run.
        if (_launch.FirstPerson && _camera.Mode != CameraMode.FirstPerson)
        {
            _ = _camera.ToggleFirstPerson();
        }

        // **Entered directly rather than by cycling** (D134). `CycleCameraMode` goes Free → first
        // person → third person, so reaching this by toggling would pass THROUGH first person and
        // photograph whichever one the count happened to land on. `EnterCamera` takes the mode it
        // is given, and it is the same call the key ends in — so the launch route and the key
        // cannot drift apart.
        if (_launch.ThirdPerson && _camera.Mode != CameraMode.ThirdPerson)
        {
            _ = _camera.EnterCamera(CameraMode.ThirdPerson);
        }

        // **`--look-at` and `--zoom` applied here until 2026-08-26** (D98). They centred and
        // magnified the orthographic map, and neither has a meaning for a camera that is placed in
        // the world rather than fitted to it. `LaunchOptions` still carries both fields; what they
        // should mean for a free camera — fly to a point, at what distance — is a question for
        // whoever reimplements them, and inventing an answer here would be the guess this project
        // keeps paying for.
    }

    // **`_openingDone` was here until 2026-08-26.** It is `OpeningSequence.Applied` (B188, D90),
    // set by this window because applying can fail — with no demo open there is nothing to seek to.
    //
    // The reason it is latched rather than inferred from the countdown moved with it, and is worth
    // keeping: the countdown keeps running after it reaches zero, and re-applying the seek every
    // frame would pin the transport to one tick — a viewer that cannot be scrubbed, which is the
    // opposite of the point.

    /// <summary>Demo time the last drawn frame covered: zero while paused.</summary>
    private double _demoFrameSeconds;

    // **`WhyNoLeafBox` was a wrapper here until 2026-08-26** (B208 moved its body, B188 removed the
    // wrapper). It is `LeafVis.WhyNothing`, called by `LeafBoxes` where the warning is written.
    //
    // The rule it carried is kept where the code went: **a log must name what it measured.** "no
    // leaf box" is true of all three causes and useful for none — a map that never loaded, a map
    // with no BSP tree, and a camera standing in a leaf whose bounds the lump does not carry are
    // three different problems with three different fixes, and only the first two are ours.

    // The prose about WHY a viewmodel is placed at the eye — `CBaseViewModel::CalcViewModelView`,
    // and why the bob, lag and shake are deliberately not copied — moved with the code that acts on
    // it, onto `MomentScene.AddViewmodel` (B188, D90). It was orphaned here the moment the placement
    // left, and commentary that outlives its code is how a comment starts describing something that
    // is no longer true.

    /// <summary>Decides what the first-person view contains; the scene supplies where and whose.</summary>
    /// <remarks>Constructed here and handed to <see cref="MomentScene"/>, which drives it.</remarks>
    private readonly ViewmodelScene _viewmodelScene = new();

    // `_viewmodelCamera` went with `AddViewmodel` on 2026-08-25 (B188, D90). It is
    // `MomentScene.ViewmodelCamera`, set by the pass that decides whether anything is drawn in it at
    // all — which is what makes "null means draw none" one fact rather than two that can disagree.

    // `WeaponModelFor`, `WeaponModel`, `ItemDefinitions` and the two fields that cached the schema
    // are `WeaponModels` in Scene (B188, D90). None of it was window work — it is two lookups into
    // items_game.txt — and none of it had a test, because reaching it meant constructing a form.

    // The viewmodel's instance list moved with the pass that fills it (B188, D90). It is
    // `MomentScene.ViewmodelInstances`, still reused between frames rather than allocated.

    // The three viewmodel entity indices moved to ViewmodelScene on 2026-08-24 (B188), with the
    // code that uses them. They are this project's own numbering rather than anything the engine
    // has — a viewmodel there is a real networked entity — so they belong beside the type that
    // assigns them rather than in the form that used to.

    // `Spectated`, `FirstPersonCamera`, `PlayerAt`, `Ducking` and `_spectating` are `SpectatorView`
    // in Scene (B188, D90). Valve computes a view on the PLAYER and dispatches on observer mode —
    // `C_BasePlayer::CalcView` (`c_baseplayer.h:112`) to `CalcObserverView` (`:455`) to
    // `CalcInEyeCamView`/`CalcChaseCamView`/`CalcRoamingView` (`:463`) — so none of it belonged in a
    // window there either. The only thing it wanted from one was the viewport's aspect ratio.

    // **Placing the free camera and parsing a placement moved to FreeCameraController on
    // 2026-08-25** (D90). Both are presenter work — the only thing either needed from a window was
    // the viewport's aspect ratio, which is one float and is now an argument. Their test moved with
    // them, out of the Windows-pinned suite (B184).

    // FreeFocus was deleted here on 2026-08-22 (D66). It anchored the free camera's entry placement
    // to `_heightRange.Lowest` plus an eye height, on the reasoning that the middle of a map's
    // vertical range is nowhere anybody stands — which is true, and the correction overshot: the
    // LOWEST drawn geometry is a basement floor or the underside of a displacement, so entering the
    // free view started the camera below the map rather than above it.
    //
    // `OverheadPlacement` replaces it, anchoring to the highest geometry within MainBounds and
    // taking whichever is greater of that and the distance needed to frame the play area.

    // **`FlySpeed` was here until 2026-08-26** (B204, B206). It was 32 units and every use of it
    // read `FlySpeed * 4f`, so it is `FreeCameraController.WheelTravel` at 128 now — the number a
    // reader had to compute is the number they read. Its note about a notch being a discrete event
    // rather than a duration went with it, since that is the reason it is a distance at all.

    // PlayerEyeHeight (VEC_VIEW, 64) went with FreeFocus on 2026-08-22 (D66): the free camera no
    // longer arrives at a player's eye height above the lowest floor, it arrives above the map
    // looking down. The constant is still correct about Source and is recorded here in case
    // anything wants it again — the first-person camera takes its eye position from the demo
    // rather than from a constant, so nothing does today.

    // `EmptyMapExtent` was here until 2026-08-26. It is `ViewCamera.EmptyMapExtent`, with the
    // overhead placement that is its only reader (B188, D90).
    //
    // **The reason it exists is worth keeping: `_loaded` is genuinely null before a demo is opened,
    // and this code used to write `_map!`.** Starting the viewer with no map crashed on the first
    // layout with a NullReferenceException — the owner hit it running Viewer3D straight from the
    // IDE, which is the ordinary way to start it with no arguments. That `!` asserted a fact the
    // code could not support, and the assertion was simply false. Eleven call sites reach the
    // overhead camera, several from layout and paint handlers that run before anything is loaded,
    // so the answer is a camera over NOTHING rather than a null every caller has to test.

    // **`MapCamera` was here until 2026-08-26** (D98) — the last thing that built the orthographic
    // projection. The free camera does not need it: it is placed rather than fitted, and it is
    // valid before a map exists, which is what the deleted null-safety above was protecting.


    // **`ShowPositions` and `ShowPlayers` were here until 2026-08-26** (D98). Both projected world
    // coordinates through the orthographic camera into flat `ScenePoint` markers — two dimensions,
    // no depth — which agreed with the view only while that view was itself orthographic.
    //
    // The markers come back as a free camera option that shares the real view matrix, so this is a
    // removal rather than a rejection. The rule any reimplementation has to obey is in D98, and it
    // was learned the hard way: a player drawn as a MODEL must not also get a marker on top, and a
    // player without one must still get a marker or they vanish.

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
        _presenter.ShowMoment(tick, _device);
    }

    /// <summary>The camera the ears are at, as the delegate the sound step asks once it has a device.</summary>
    private readonly Func<FreeCamera?> _listener;

    /// <summary>Whose eyes the listener is in, or null outside first person (B483).</summary>
    private readonly Func<int?> _listenerEyes;

    /// <summary>Builds the scene at a moment, with no replay before it.</summary>
    private void ShowOnly(double tick)
    {
        _shownTick = tick;

        _presenter.Show(tick, _transport.Playing);
    }

    // `HandsForFollowed` lived here for exactly one commit, and its own comment admitted what it
    // was: a shim kept because the weapon-model resolver had not moved yet. Both are gone — the
    // scene holds the roster this moment sampled, so it finds the followed player and asks
    // `IPlayerAppearance.Hands` and `WeaponModels` itself (B188, D90).

    // **`ReportSlowMoment` was here until 2026-08-25** (B188, D90). It is `StallReport.Moment` in
    // Presentation, with `ReportSlowFrame` and `ReportSlowSounds` beside it — around 190 lines of
    // arithmetic and formatting, none of which needed a window and none of which had a test,
    // because reaching them meant constructing a form.
    //
    // **They earned the tests they now have: B191 was found by reading these lines.** A report whose
    // own arithmetic was wrong would have sent that hunt somewhere else entirely.
    //
    // **One real defect went with the move.** This compared a WHOLE moment against
    // `MomentScene.StallSeconds` — a constant whose own documentation says it is "applied to one
    // step of a scene rebuild". A constant carries no scope, and borrowing one whose meaning is
    // stated to be narrower is how two independent judgements get tied together. `StallReport` has
    // its own, for the whole-step measurements it makes.

    // The team colours the markers used are not lost with them: **team two is RED and team three is
    // BLU**, the engine's own numbering, with nought unassigned and one spectator. A player whose
    // team has not arrived is drawn grey rather than guessed at, because a wrong team colour is
    // worse than none — it is read as information. `MapOverview` still holds that mapping for
    // whatever draws markers next.

    /// <summary>`TF2VIEW_PICK="x y"`: logs what lies under that pixel of the shot — <see cref="PickReport"/>.</summary>
    private void Pick()
    {
        string? report = _presenter.Pick(Environment.GetEnvironmentVariable("TF2VIEW_PICK"), _lastViewing);

        if (report is not null)
        {
            _renderLog.LogInformation("{Message}", report);
        }
    }

    /// <summary>What this window knows of the frame it is drawing; the presenter adds the demo, the map and the settings.</summary>
    private WindowFrame WindowFrameNow =>
        new(
            _transport.CurrentTick,
            _shownTick,
            _transport.Playing,
            _viewport.ClientSize.Width,
            _viewport.ClientSize.Height,
            (float)_clock.LastFrameSeconds,
            _demoFrameSeconds);

    /// <summary>Everything a frame makes around its models: lights, decals, sounds, particles, the target ID (D92, D214).</summary>
    private readonly FramePresenter _effects;

    /// <summary>The camera the last frame was set up with, for <see cref="Pick"/>.</summary>
    private FreeCamera? _lastViewing;

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
        _viewport.Invalidate();
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

        CaptureViewport(Path.Combine(folder, Captures.Name(DateTime.Now)));

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
        FileRetention.Keep(folder, Captures.Pattern, Captures.Kept);
    }

    // **`CaptureName` and `CapturesKept` were here until 2026-08-26** (B208). They are
    // `Captures.Name` and `Captures.Kept`, with `Captures.Pattern` beside them — the glob retention
    // deletes by, which has to agree with the name and previously did so only because the same
    // prefix was typed in two places.
    //
    // Their notes went with them: the 2026-08-20 overwrite that produced the millisecond stamp, the
    // requirement that ordinal name order stay chronological because `FileRetention` sorts by name,
    // and the size argument for twenty rather than the logs' fifty. `CaptureNameTests` moved too,
    // to `Presentation.Tests` — a test on a public static naming policy was never a viewer test.
    //

    // **`Scene` was here until 2026-08-26** (D98) — the projected marker points, exposed so a test
    // could read what the viewport was drawing. With the markers gone there is nothing flat left to
    // expose, and what the viewport draws now is a 3D scene that a property cannot summarise.

    /// <summary>The demo named on the command line, opened once the window is up; null when none was.</summary>
    private readonly string? _openOnShow;

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
        Console.Error.WriteLine(reason);
        Environment.ExitCode = 1;
        _opening.TakeShotPath();
        BeginInvoke(Close);
    }

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
            _status.Text = $"{_status.Text} {started}";
        }
    }

    /// <summary>UIA AutomationId of this window.</summary>
    public const string MainWindowId = "MainWindow";

    /// <summary>UIA AutomationId of the window shown while the program starts, before this one (D182).</summary>
    public const string StartupSplashId = "StartupSplash";

    /// <summary>UIA AutomationId of the loading overlay.</summary>
    public const string LoadingOverlayId = "LoadingOverlay";

    /// <summary>UIA AutomationId of the loading overlay's stage label.</summary>
    public const string LoadingStageId = "LoadingStage";

    /// <summary>UIA AutomationId of the loading overlay's progress bar.</summary>
    public const string LoadingProgressId = "LoadingProgress";

    /// <summary>What a demo load is doing, over the viewport.</summary>
    [SuppressMessage(
        "Usage",
        "CA2213:Disposable fields should be disposed",
        Justification = "Disposed by base.Dispose, which walks Controls; ours was redundant (B402).")]
    private readonly LoadingOverlay _loading = new();

    /// <summary>Runs something on the UI thread and waits for its answer.</summary>
    /// <remarks>
    /// **Explicit rather than relying on `await` returning to a captured context, and that is not a
    /// style preference — the context version deadlocked.** `ConfigureAwait(true)` posts the
    /// continuation to whatever `SynchronizationContext` was current when the load started. Under
    /// WinForms that is the message loop and it works; under NUnit it is a single-threaded test
    /// context that stops pumping while the test itself is awaiting, so the continuation was never
    /// run and the test hung until it was killed.
    ///
    /// **A form with no handle has no thread affinity**, which is why the fallback is simply to run
    /// the work here. Setting `Text` on a control that was never shown is legal from any thread —
    /// that is also why every existing test can drive `MainForm` without a message loop.
    /// </remarks>
    private T OnUi<T>(Func<T> work) =>
        IsHandleCreated && InvokeRequired ? Invoke(work) : work();

    // **What a load asks of this window** (`ILoadView`, D214). The load itself — the decode, the map read behind the
    // render barrier, the demo becoming the open one — is `ViewerPresenter`'s; each member below is the part of it that
    // is a window's, called at the moment the load reached for it when it lived here.

    /// <summary>What this window knows that a camera needs: the transport's tick, the tick last shown, its shape.</summary>
    private CameraWindow CameraWindowNow =>
        new(
            _transport.CurrentTick,
            _shownTick,
            _viewport.ClientSize.Width,
            _viewport.ClientSize.Height,
            IsFullScreen && _settingsOwner.FullScreenExclusive);

    /// <inheritdoc/>
    void ICameraView.Say(string? status) => _status.Text = status;

    /// <inheritdoc/>
    void ICameraView.CameraChanged()
    {
        _presenter.InvalidateWorld();
        _viewport.Invalidate();
    }

    /// <inheritdoc/>
    void ICameraView.ReleaseHeldKeys() => ReleaseHeldKeys();

    /// <inheritdoc/>
    void ICameraView.FailHeadless(string reason) => FailHeadless(reason);

    /// <inheritdoc/>
    T ILoadView.OnUi<T>(Func<T> work) => OnUi(work);

    /// <inheritdoc/>
    string ILoadView.Status
    {
        get => _status.Text ?? string.Empty;
        set => _status.Text = value;
    }

    /// <inheritdoc/>
    void ILoadView.ReportLoading(string stage, double? fraction) => _loading.Report(stage, fraction);

    /// <inheritdoc/>
    void ILoadView.HideLoading() => _loading.Visible = false;

    /// <inheritdoc/>
    (bool Play, string Reason) ILoadView.AutoPlay =>
        (_launch.AutoPlay || Environment.GetEnvironmentVariable(AutoPlayVariable) is { Length: > 0 },
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
        _presenter.ProjectMap(_device, _menu.SurfaceColours.Checked, (_viewport.ClientSize.Width, _viewport.ClientSize.Height));

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

    /// <summary>The playback controls, exposed for the tests that address them.</summary>
    public TransportBar Transport => _transport;

    /// <summary>How full screen is entered.</summary>
    // The stray second summary removed here on 2026-08-26 — "Whether the viewport is filling the
    // screen" — belongs to `IsFullScreen`, sixty lines below, which has its own.
    public bool FullScreenExclusive => _settingsOwner.FullScreenExclusive;

    /// <summary>Chooses how full screen is entered, and remembers it.</summary>
    /// <param name="exclusive">The exclusive mode; borderless otherwise.</param>
    /// <remarks>
    /// Applied immediately when already full screen, so the choice can be judged by making it
    /// rather than by restarting. What is remembered, saved and said is the settings owner's
    /// (`SettingsPresenter.SetFullScreenExclusive`); the check marks and the device are this window's.
    /// </remarks>
    public void SetFullScreenExclusive(bool exclusive)
    {
        _menu.Borderless.Checked = !exclusive;
        _menu.Exclusive.Checked = exclusive;

        // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
        if (_settingsOwner.SetFullScreenExclusive(exclusive, IsFullScreen && _device is { } device ? device.SetExclusiveFullScreen : null)
            is { } said)
        {
            _status.Text = said;
        }
    }

    /// <summary>How much texture detail is loaded, by level name.</summary>
    public string TextureQuality => _settingsOwner.TextureQuality;

    /// <summary>Chooses how much texture detail to load, and remembers it.</summary>
    /// <param name="quality">The detail level's name, as the menu's item carries it.</param>
    public void SetTextureQuality(string quality)
    {
        foreach (KeyValuePair<string, ToolStripMenuItem> item in _menu.TextureQualityItems)
        {
            item.Value.Checked = item.Key == quality;
        }

        _status.Text = _settingsOwner.SetTextureQuality(quality);
    }

    /// <summary>Turns the surface-category view on or off.</summary>
    /// <param name="on">Whether to colour surfaces by category.</param>
    /// <remarks>
    /// **A world rebuild rather than a repaint, unlike its neighbours below.** The category colour
    /// is baked into the vertex data, so the geometry has to be built again; the others are shader
    /// constants and a repaint is enough.
    /// </remarks>
    internal void SetSurfaceColours(bool on)
    {
        // **The legend goes in the log, because a colour nobody can name is not an answer.**
        // Violet was read as "the sign" and white as "an uncoloured surface" during the B154
        // hunt, both wrong, and there was nowhere to look it up.
        _renderLog.LogInformation(
            "{Message}",
            on
                ? "surface colours on — grey-blue brushwork, green terrain, orange props, " +
                  "violet overlays, Valve's magenta chequer where a material resolved to " +
                  "nothing; brush entities take their own FGD colour, magenta where the class " +
                  "states none, as Hammer draws them"
                : "surface colours off");

        // **`ClearWorld` throws away the models too, and only the map came back** (B219). It
        // disposes the world and empties `_packedModels` with the buffers they feed; `Invalidate`
        // re-projects and re-uploads the brushwork, and nothing re-ran `UploadModels`, because
        // `MomentScene` asks for one only when the model set GREW — which after a clear it has not.
        //
        // The owner: *"setting surface colors on removes the viewmodel and doesnt bring it back, so
        // i need you to restart the program"*. Every model, not just the viewmodel, and the log had
        // been saying so 444,242 times in seven seconds.
        //
        // **A repaint, not a rebuild, and the owner asked for exactly that**: *"it shouldnt, which
        // is why its super slow compared to any other view"*. It rebuilt because the colours were
        // baked into vertices — flat colours by surface kind in `MapWorldBuilder`, Valve's per-class
        // colours in `BrushModels` — so switching the view meant rebuilding every vertex in the map.
        //
        // Neither is baked now. A batch carries its `SurfaceCategory` and an instance carries its
        // class colour, so the view is a shader constant like `mat_specular`, and the flag reaches
        // the draw through `SetCamera` on the next frame.
        //
        // **B219 goes with it.** `ClearWorld` was here, and it discarded every model's GPU buffer
        // while only the map was re-uploaded — the viewmodel and every prop gone until a restart.
        // The fix that pairs it with `MomentScene.Uploaded = false` still stands for the other
        // callers; this one no longer needs either.
        _viewport.Invalidate();
    }

    /// <summary>Shows or hides Valve's frame rate meter.</summary>
    /// <param name="on">Whether to draw it.</param>
    /// <remarks>
    /// **Two, not one**, because `cl_showfps 2` is the form that names the worst and best frame as
    /// well as the average, and the smoothed single number is the one this project has repeatedly
    /// found hides a stall.
    /// </remarks>
    internal void SetFrameRateMeter(bool on) => _settingsOwner.SetFrameRateMeter(on);

    /// <summary>Shows or hides Valve's position readout.</summary>
    /// <param name="on">Whether to draw it.</param>
    /// <remarks>
    /// **One, not two.** `cl_showpos 1` reports the VIEW's own origin and angles, which is the
    /// question a screenshot is taken to answer — where the camera is. Mode 2 reports the watched
    /// player instead, and for a free-camera shot of a piece of geometry that says nothing about
    /// what is being looked at. Someone who wants the player can still write `cl_showpos 2` in a
    /// config; the menu offers the useful one.
    /// </remarks>
    internal void SetPositionReadout(bool on)
    {
        _settingsOwner.SetPositionReadout(on);

        // **Invalidated, unlike the frame-rate meter beside it.** That one is only ever switched on
        // while frames are already being produced, because it exists to measure them. This can be
        // switched on while playback is paused, and a paused viewer redraws on demand — without
        // this the readout would not appear until something else caused a frame.
        _viewport.Invalidate();
    }

    /// <summary>Valve's <c>mat_wireframe</c>.</summary>
    /// <param name="on">Whether to draw edges only.</param>
    internal void SetWireframe(bool on) =>
        SetRenderToggle("mat_wireframe", on, static (device, value) => device.Wireframe = value);

    /// <summary>Valve's <c>r_drawworld</c>.</summary>
    /// <param name="on">Whether to draw the level's brushwork.</param>
    internal void SetDrawWorld(bool on) =>
        SetRenderToggle("r_drawworld", on, static (device, value) => device.DrawWorld = value);

    /// <summary>Valve's <c>r_drawentities</c>.</summary>
    /// <param name="on">Whether to draw props and models.</param>
    internal void SetDrawEntities(bool on) =>
        SetRenderToggle("r_drawentities", on, static (device, value) => device.DrawEntities = value);

    /// <summary>Valve's <c>mat_specular</c>.</summary>
    /// <param name="on">Whether to draw specular reflections.</param>
    /// <remarks>
    /// **A repaint, not a world rebuild: this is a shader constant and the geometry is untouched.**
    /// The rebuild was why reflections appeared instantly while every other debug view waited — it
    /// was doing far more work to get the same repaint.
    /// </remarks>
    internal void SetSpecular(bool on) =>
        SetRenderToggle("mat_specular", on, static (device, value) => device.Specular = value);

    /// <summary>Valve's <c>mat_phong</c>.</summary>
    /// <param name="on">Whether materials asking for <c>$phong</c> get their highlight.</param>
    /// <remarks>
    /// **A repaint, like `mat_specular`** — a shader constant on the world camera, no geometry
    /// touched. Added for B170, where the question "is the highlight what washes out a modern
    /// weapon" had no instrument: this viewer had `mat_specular` for cubemap reflections and
    /// nothing at all for phong, so the one manipulation that would settle it could not be made.
    /// </remarks>
    internal void SetPhong(bool on) =>
        SetRenderToggle("mat_phong", on, static (device, value) => device.Phong = value);

    /// <summary>Turns one of Valve's per-surface debug views on or off.</summary>
    /// <param name="apply">Which flag the menu item sets, supplied by the item itself.</param>
    /// <param name="on">Whether to turn it on.</param>
    /// <remarks>
    /// **The flag arrives as a function rather than a name** (B210). It used to be a name matched by
    /// a `switch` with fewer arms than there were menu items, so one view was unreachable and its
    /// shortcut toggled a different one.
    /// </remarks>
    internal void SetDebugMode(Func<DebugModes, bool, DebugModes> apply, bool on)
    {
        ArgumentNullException.ThrowIfNull(apply);

        _debug = apply(_debug, on);

        _renderLog.LogInformation("{Message}", $"debug views: {_debug}");

        if (_device is { } device)
        {
            device.Debug = _debug;
        }

        // **Ask for a repaint.** The viewport draws on demand rather than continuously, so updating
        // a shader constant is not enough on its own — the change reaches the GPU and then waits for
        // an unrelated event to show it. That is what made these appear only when the camera moved.
        _viewport.Invalidate();
    }

    /// <summary>Logs a render switch, tells the device, and asks for a repaint.</summary>
    /// <param name="cvar">Valve's name for the switch, for the log.</param>
    /// <param name="on">The new state.</param>
    /// <param name="apply">How the device is told.</param>
    /// <remarks>
    /// **Four handlers were this same three-line body** — wireframe, draw-world, draw-entities and
    /// specular — each written out separately with its own `if (_device is { } …)`. They are one
    /// method because the fourth copy is where a difference starts hiding: B210 was exactly that,
    /// a list of near-identical cases where one did something else.
    ///
    /// **A repaint rather than a rebuild.** Every switch here is a shader constant, so the geometry
    /// on the device is still correct and only the picture is stale.
    /// </remarks>
    private void SetRenderToggle(string cvar, bool on, Action<Device3D, bool> apply)
    {
        _renderLog.LogInformation("{Message}", $"{cvar} {(on ? 1 : 0)}");

        if (_device is { } device)
        {
            apply(device, on);
        }

        _viewport.Invalidate();
    }

    /// <summary>The controls hidden while full screen, for tests to check what they are.</summary>
    public IReadOnlyList<Control> HiddenInFullScreen => _hiddenInFullScreen;

    public bool IsFullScreen { get; private set; }

    /// <summary>
    /// Enters or leaves full screen, moving the transport controls onto an overlay and back.
    /// </summary>
    /// <param name="fullScreen">Whether the viewport should fill the screen.</param>
    /// <remarks>
    /// **The transport bar is MOVED, not duplicated.** A second copy would be a second piece of
    /// state to keep in step with playback, and the two would drift - the windowed one showing a
    /// stale tick the moment anything updated only the overlay. Moving it also means the
    /// automation ids stay the same in both modes, so a UI test does not need to know which mode
    /// it is in to find the Play button.
    ///
    /// The previous border style and window state are remembered rather than assumed, because
    /// leaving full screen should restore a maximised window as maximised.
    /// </remarks>
    public void SetFullScreen(bool fullScreen)
    {
        if (IsFullScreen == fullScreen)
        {
            return;
        }

        IsFullScreen = fullScreen;
        _menu.FullScreen.Checked = fullScreen;
        _fullScreenClock = Stopwatch.StartNew();

        if (fullScreen)
        {
            _borderBeforeFullScreen = FormBorderStyle;
            _stateBeforeFullScreen = WindowState;

            // **Bounds too, not just border and state.** Removing the border and putting it back
            // recalculates the client area from the same outer size, so the client - and the
            // viewport filling it - comes back NARROWER than it went. Measured by the UI test:
            // 984 pixels wide before, 968 after, and it loses another 16 on every toggle.
            _boundsBeforeFullScreen = Bounds;

            // **Where the transport sat in the control collection, not just that it was there.**
            // Docking order is collection order, so putting it back with a plain Add appends it
            // and silently swaps it with the action row - the buttons end up above the play bar
            // and stay there. Only reproducible after using full screen once, which is why the
            // action-row test passed alone and failed in a full run.
            _transportIndexBeforeFullScreen = Controls.GetChildIndex(_transport);

            MainMenuStrip!.Visible = false;

            foreach (Control hidden in _hiddenInFullScreen)
            {
                hidden.Visible = false;
            }

            // **Focus has to be moved before the control holding it disappears.** The playlist
            // takes focus when the window opens and the playlist is hidden by full screen, which
            // leaves the form with no focused child at all — and every binding lives on
            // ProcessCmdKey, so the next F11 or Escape went nowhere and full screen could not be
            // left until the user alt-tabbed away and back. That restored a focused child, which
            // is why it looked like an intermittent glitch instead of what it was.
            //
            // Reasserted at the window level rather than on the viewport, which is a plain Panel
            // and therefore not selectable — Select() on it is a no-op that would have looked like
            // a fix. Activate() puts this form back as the active window whichever of the two took
            // it, the hidden child or the overlay appearing.
            Controls.Remove(_transport);

            FormBorderStyle = FormBorderStyle.None;

            // **Not Maximized, and this is the difference between full screen and a big window.**
            // A maximised window - borderless or not - is sized to the WORK AREA, which is the
            // screen minus the taskbar, so the taskbar stays on top of it. Setting the bounds to
            // the screen rectangle is what actually covers the whole display.
            //
            // Normal first, because a maximised window ignores a border-style change until it is
            // restored, and would otherwise keep the old frame on screen.
            WindowState = FormWindowState.Normal;
            Bounds = Screen.FromControl(this).Bounds;

            // **No TopMost here, deliberately, and the reason is worth keeping.** It was set once
            // to hide the taskbar, and it does - but TOPMOST does not mean "above the shell", it
            // means above every window on the desktop. Borderless then became indistinguishable
            // from exclusive from the user's side: nothing could be alt-tabbed in front of the
            // viewer, in the mode whose whole point is that things can be.
            //
            // Found by the owner with the one test that separates the two modes - "I cannot alt-tab
            // you above it, meaning it is exclusive" - while the status bar was simultaneously
            // reporting that exclusive had been REFUSED and borderless was in use. Both were true.
            // The mode selection was working perfectly and this flag was undoing it.
            //
            // How it got in is the part to remember: the change was verified by screenshot, the
            // taskbar was gone, and it was called done. The screenshot confirmed the one property
            // being looked at and said nothing about the one being broken.
            //
            // Windows hides the taskbar on its own for a borderless window that exactly covers the
            // monitor and holds the foreground, which is the mechanism games rely on and which
            // leaves alt-tab alone.

            // **The window is sized first, then the display is taken.** DXGI wants the window
            // already covering the output it is about to own; asking for exclusive from a small
            // window is the case it most often refuses.
            if (_settingsOwner.FullScreenExclusive &&
                _device is not null &&
                !_device.SetExclusiveFullScreen(true))
            {
                // Refused - another application holds the output, or this is a WARP device.
                // Borderless is already in effect, so this is a note rather than a failure.
                _status.Text = ViewerSettings.ExclusiveFullScreenRefused;
            }

            _overlay = new OverlayWindow(_transport);

            // **Only shown if there is something to overlay.** An overlay over a form that is not
            // on screen is meaningless at runtime, and in a test it is worse than meaningless: it
            // is a window that appears on the person's desktop and takes the foreground. The full
            // screen tests construct a MainForm without ever showing it, on the stated grounds that
            // the transition is state on controls and needs no display — which was true when they
            // were written and stopped being true the day full screen grew an overlay window.
            //
            // The transport bar still MOVES to the overlay either way, so everything those tests
            // measure is unchanged; what goes away is three windows opening and nothing happening
            // in them.
            if (Visible)
            {
                _overlay.Show(this);
            }

            // Positioned AFTER the layout settles, not now. At this point the form has changed
            // border style and window state but has not re-laid-out, so the viewport still reports
            // its windowed rectangle - and the overlay lands wherever the bottom of the small
            // viewport used to be, which on a maximised window is the middle of the screen.
            // **Logged on both sides of Activate, because the question is whether it WORKED.**
            // SetForegroundWindow is refused rather than obeyed for a process that is not already
            // foreground, so Activate can return having done nothing at all — and the symptom is
            // keys going to another application, which looks like the viewer ignoring them.
            _renderLog.LogInformation("{Message}", "full screen: " + ForegroundProbe.Describe(Handle) + FocusHere());

            BeginInvoke(() =>
            {
                _overlay?.PositionOver(_viewport);

                // Last thing, after every window has settled. Without it the keys stopped landing
                // on entering full screen and there was no way back out but alt-tab.
                Activate();

                // **Activation is not focus, and only the second one delivers a keystroke.**
                // Measured 2026-08-20: the window held the foreground on both sides of this
                // transition — the probe says so — and `ContainsFocus` was still false, because
                // full screen hides the playlist and the playlist is what had the focus. A form
                // with no focused child receives no key messages, so `ProcessCmdKey` never ran and
                // Escape had nowhere to land. The window sat full screen with the overlay up and
                // ignored every key until the user alt-tabbed away and back, which is what put a
                // focused child back.
                //
                // Cleared before focusing: `ActiveControl` still points at the hidden playlist, and
                // focusing a container walks to its active control — which would hand it straight
                // back to the control that cannot take it.
                ActiveControl = null;
                _ = Focus();

                _renderLog.LogInformation(
                    "{Message}",
                    "full screen after Activate: " + ForegroundProbe.Describe(Handle) + FocusHere());
            });

            // **And again on every layout while full screen.** One shot is not enough: the form is
            // still settling after this - border style, bounds and topmost each re-lay-out the
            // viewport - so the overlay ends up over wherever the viewport was mid-transition. It
            // was landing three quarters of the way up the map.
            Layout += KeepOverlayOnTheViewport;
            return;
        }

        // Released BEFORE the window is put back, and unconditionally rather than only when the
        // setting says exclusive: the setting can be changed while full screen, and what has to be
        // undone is what was done, not what is currently preferred.
        Layout -= KeepOverlayOnTheViewport;
        _device?.SetExclusiveFullScreen(false);

        if (_overlay is not null)
        {
            // Taken back out of the overlay before it closes, or disposing the overlay disposes
            // the transport bar with it.
            _overlay.Controls.Remove(_transport);
            _overlay.Close();
            _overlay.Dispose();
            _overlay = null;
        }

        _transport.Dock = DockStyle.Bottom;
        Controls.Add(_transport);
        Controls.SetChildIndex(_transport, _transportIndexBeforeFullScreen);
        MainMenuStrip!.Visible = true;

        foreach (Control hidden in _hiddenInFullScreen)
        {
            hidden.Visible = true;
        }

        FormBorderStyle = _borderBeforeFullScreen;
        WindowState = _stateBeforeFullScreen;

        // Only meaningful for a normal window: a maximised one owns its own geometry.
        if (_stateBeforeFullScreen == FormWindowState.Normal)
        {
            Bounds = _boundsBeforeFullScreen;
        }
    }

    /// <summary>The status line's current text.</summary>
    public string StatusText => _status.Text ?? string.Empty;

    /// <summary>Whether a Direct3D device has been created for the viewport.</summary>
    public bool HasDevice => _device is not null;

    /// <summary>Creates the swap chain once the panel has a real window handle.</summary>
    private void OnViewportHandleCreated(object? sender, EventArgs e)
    {
        // A failure here is a machine or driver problem, not a bug in the demo being viewed, so
        // it is reported in the status line rather than crashing the shell - the rest of the
        // application (opening files, reading a trace) still works without a device.
        try
        {
            _device = Device3D.Create(
                _viewport.Handle,
                _viewport.ClientSize.Width,
                _viewport.ClientSize.Height,
                _loggers);

            // Every setting a new device takes, in the order they were assigned here, is the settings owner's
            // (`SettingsPresenter.ApplyTo`). The two that go through the menu come back as these two calls, where
            // they stood: unticking Reflections is what calls `SetSpecular`, and the check marks follow `SetFullbright`.
            _settingsOwner.ApplyTo(_device, () => _menu.Specular.Checked = false, mode => SetFullbright((Fullbright)mode));

            // **Where packed geometry goes, and forgetting it draws NOTHING** (B193). Without this
            // the scene packs every model, poses it, transforms it correctly and submits it against
            // a vertex buffer the renderer never received — B148's symptom exactly, and silent.
            // Assigned here rather than at construction because the device does not exist until the
            // viewport has a handle.
            _moment.Upload = _device;

            _renderLog.LogInformation(
                "{Message}",
                _settingsOwner.FrameRateLine);
            _renderLog.LogInformation(
                "{Message}",
                $"device created for a {_viewport.ClientSize.Width}x{_viewport.ClientSize.Height} viewport");

            // **Only if there is nothing better to say.** The handle is created after the
            // constructor runs, so a demo opened from the command line has already reported itself
            // by now - and announcing the graphics device over the top of it threw that away. The
            // user saw "Direct3D ready." for a demo that had loaded fine, which reads like the
            // demo did not load.
            if (!_presenter.HasDemo)
            {
                _status.Text = "Direct3D ready.";
            }

            // **And project again, because a map may already be loaded.** The same ordering that
            // made the status line wrong: a demo opened from the command line loads its map in the
            // constructor, before this handle exists, so the upload of the textured world was
            // skipped for want of a device and nothing re-ran it. The map drew as an outline and
            // looked exactly like a renderer that had not been wired up.
            ProjectMap();

            // Idle-driven rather than a timer: WinForms raises Idle whenever the message queue
            // empties, so the viewport redraws as fast as the UI allows and stops entirely while
            // the user is dragging a menu around. A timer would keep presenting underneath it.
            Application.Idle += OnIdle;

            // Registered beside the idle loop because they are the two halves of the same thing:
            // the filter records what is held, the loop moves the camera by it.
            _keyReleases = new KeyReleaseFilter(_console);
            Application.AddMessageFilter(_keyReleases);

            _rendering = true;
        }
        catch (Exception failure) when (failure is InvalidOperationException or ArgumentException)
        {
            _status.Text = "Direct3D unavailable: " + failure.Message;
            _renderLog.LogWarning(failure, "{Message}", "creating the Direct3D device");
        }
    }

    private void KeepOverlayOnTheViewport(object? sender, LayoutEventArgs e) =>
        _overlay?.PositionOver(_viewport);

    // `MaximumFrameSeconds` was here until 2026-08-25. It is `FreeCameraController` s, because
    // flight was its only reader — which an analyzer said the moment `FlyCamera` handed the
    // movement over (CA1823, S1144).
    //
    // **Its old summary called it "the longest frame playback will believe in", and that was a
    // claim about a reader it did not have.** The playback clock does its own clamping; this one
    // only ever governed how far the camera may travel across a stall. A constant that names a
    // scope wider than its callers is how it gets borrowed for a use nobody checked — see D94, and
    // the whole-moment threshold that was borrowed from a per-step one three commits ago.

    // `StallSeconds` was here until 2026-08-25. It is `StallReport.StallSeconds`, and it went with
    // the three reporters that were its only readers — which an analyzer confirmed the moment they
    // left, by declaring the field unused (CA1823, S1144).
    //
    // **The reasoning went with it, and the other two copies in this solution are NOT duplicates of
    // it.** `SoundCache.StallSeconds` and `MomentScene.StallSeconds` hold the same number for
    // deliberately different scopes, each saying so where it is declared: one decode blocking the
    // draw thread, and one step of a scene rebuild. A constant carries no scope, so three symbols
    // that happen to agree on 0.03 are three judgements, not one fact repeated.

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

    // **`AdvancePlayback` was here until 2026-08-26** (B188, B203, D90). It is now `Simulate`, the
    // first stage of `FrameSequence`, and its note is worth keeping: **nothing is invalidated
    // there.** The idle loop it runs inside already draws every frame, and asking for a repaint as
    // well is what made the mouse sluggish over the transport buttons — paint messages queued
    // faster than the pump could drain them.

    /// <summary>Renders continuously for as long as Windows has nothing else for this thread.</summary>
    /// <remarks>
    /// **This loop is the render loop; the event only starts it.** <c>Application.Idle</c> fires
    /// once when the queue empties and not again until a message arrives and is dispatched, so a
    /// handler that draws one frame and returns draws at whatever rate Windows happens to be
    /// posting messages — irregular by nature, which is what the jitter was. Staying here while
    /// the queue is empty is the documented shape and it is also the engine's: Source pumps
    /// messages and then runs a frame, over and over, rather than waiting to be asked.
    ///
    /// Yielding the moment anything arrives is what keeps the UI responsive. The mistake before
    /// this one was the opposite — a timer invalidating the viewport, which queued paint messages
    /// faster than the pump could drain them and made the mouse sluggish over the transport bar.
    /// </remarks>
    private void OnIdle(object? sender, EventArgs e)
    {
        uint? waiting;

        // A cross-thread caller (UI Automation, over COM) sends its next call right after our reply.
        // Serve it before drawing, or each call waits a whole frame.
        if (FramePacer.AwaitsTraffic(_idleEndedBy) && MessageQueue.ArrivesWithin(FramePacer.TrafficWaitSeconds))
        {
            _idleEndedBy = MessageQueue.Waiting() ?? 0;
            _frames.Yielded();
            return;
        }

        do
        {
            // **`engine_no_focus_sleep`, before the frame decision rather than inside the wait**
            // (B209). The engine sleeps because nobody is looking, which is a different question
            // from whether the next frame is due — putting it in `WaitForTheNextFrame` would mean an
            // unfocused window still rendered at full rate whenever a frame WAS due, which is every
            // frame at any reachable limit.
            int idle = FramePacer.NoFocusSleep(ContainsFocus, _settingsOwner.NoFocusSleep);

            if (idle > 0)
            {
                Thread.Sleep(idle);
            }

            if (_clock.IsDue(_settingsOwner.FrameRateLimit))
            {
                // **Counted only when something was drawn.** RenderFrame declines during a map
                // read, and counting those turned the per-second report into a measurement of how
                // fast an empty loop spins — 186 "frames a second" with every duration at zero.
                if (RenderFrame())
                {
                    _frames.Drew(
                        _clock.LastFrameSeconds,
                        new FrameView(
                            _transport.Playing,
                            _camera.FreeLook && _console.AnyHeld,

                            // The one part of the line a second frontend could not produce: a
                            // Windows message id, named by the window that received it.
                            MessageName(_idleEndedBy)));
                }
            }
            else
            {
                WaitForTheNextFrame();
            }

            // The id of whatever ended this burst, kept for the per-second report. See B148: the
            // viewer drops to twenty frames a second after a demo switch, and because this loop
            // runs only while the queue is empty, that is a statement about who is posting messages
            // rather than about how long a frame takes.
            // Null, not zero: a WM_NULL is a message to yield to, see MessageQueue.Waiting.
            waiting = MessageQueue.Waiting();
        }
        while (waiting is null);

        _idleEndedBy = waiting.Value;
        _frames.Yielded();
    }

    /// <summary>Names the Windows messages worth recognising in a render report.</summary>
    /// <remarks>
    /// Only the ones that plausibly arrive in a tight loop. Anything else is reported as its number,
    /// which is enough to look up and better than a name this table guessed at.
    /// </remarks>
    private static string MessageName(uint message) => message switch
    {
        0x0000 => "nothing",
        0x000F => "WM_PAINT",
        0x0113 => "WM_TIMER",
        0x0200 => "WM_MOUSEMOVE",
        0x0007 => "WM_SETFOCUS",
        0x0008 => "WM_KILLFOCUS",
        0x0014 => "WM_ERASEBKGND",
        0x0018 => "WM_SHOWWINDOW",
        0x0046 => "WM_WINDOWPOSCHANGING",
        0x0047 => "WM_WINDOWPOSCHANGED",
        0x0084 => "WM_NCHITTEST",
        0x0020 => "WM_SETCURSOR",
        0x8000 => "WM_APP (a posted callback)",
        _ => $"message 0x{message:X4}",
    };

    /// <summary>The message that last ended a render burst.</summary>
    private uint _idleEndedBy;

    // `_idleBursts` was here until 2026-08-25. It is `FrameLedger.Yielded()` — one of six
    // per-second counters this window kept and reset by hand (B188, D90). What stays is
    // `_idleEndedBy` above, because naming a Windows message is the only part of that report a
    // second frontend could not produce.

    /// <summary>The user's controls, running as the engine would run them.</summary>
    /// <remarks>
    /// **Held state, because a keystroke is not a duration** (B97). Keys are pushed in on the key
    /// down and key up messages, so the frame loop can ask what is down right now instead of
    /// inferring it from how fast Windows repeats.
    ///
    /// **A console rather than a `HashSet&lt;Keys&gt;` (D69).** A TF2 config is a program: it binds
    /// keys to aliases, and those aliases redefine each other as they run. A set of held keys can
    /// answer "is W down" but not "what does W currently mean", and in a null-cancelling movement
    /// script — which is what most competitive configs are — those are different questions.
    ///
    /// **This is the single source of truth for the controls**; <see cref="_bindings"/> is a
    /// projection of it for the settings screen to display.
    /// </remarks>
    private readonly ConfigConsole _console = ConfigConsole.WithDefaults();

    /// <summary>
    /// A client ConVar's value: this viewer's settings file or a <c>+name value</c> launch option, which run last as
    /// Source's command-line cvars do, else the watcher's TF2 configs; null for the SDK default.
    /// </summary>
    private string? ClientConVar(string name) =>
        _settingsOwner.ConVar(name) ?? _console.ConVar(name);

    /// <summary>The frame clocks: when a frame may begin, and how long the last one took.</summary>
    /// <remarks>
    /// **Was `_flyWatch` and `_lastFrameAt`, two fields whose docs never mentioned each other**
    /// (B188, D90). They are still two clocks — Valve keeps at least four and names each by what it
    /// obeys, and the demo free camera flies by `absoluteframetime` while a limiter cannot pace
    /// itself by the duration of the frame it is deciding to allow. `FrameClock` states the
    /// relationship; `FrameTimingConformanceTests` carries the citations.
    /// </remarks>
    private readonly FrameClock _clock = new(new StopwatchTime(), new StopwatchTime());

    /// <summary>The key-release filter, kept so it can be removed on shutdown.</summary>
    private KeyReleaseFilter? _keyReleases;

    // `_longestFrameSeconds` was here until 2026-08-25. It is the maximum `FrameLedger.Drew` keeps.
    //
    // **The rule that lived with it is worth repeating where the clamp is, and it is:** this reading
    // must never pass through the free camera's stall clamp. It did once, so the worst frame could
    // not be reported as worse than 100 ms, and the owner's "half a second to maybe a second" met a
    // log saying `longest 100 ms` every time. A saturating instrument is worse than a missing one.

    // `_lastFrameSeconds` was here until 2026-08-26. It is `FrameClock.LastFrameSeconds`.
    //
    // **The rule it carried holds and is now citable** (B174): the meter reads the camera's clock
    // rather than keeping its own, because two clocks measuring the frame rate is two answers to the
    // question the meter exists to settle. Valve agrees — `cl_showfps` reads
    // `gpGlobals->absoluteframetime` (`vgui_fpspanel.cpp:166`), the same quantity
    // `CalcDemoViewOverride` flies its demo camera by (`view.cpp:153`).

    /// <summary>GDI, as VGUI's fonts reach it — the one Windows-bound piece of the surface (the Fonts project).</summary>
    private readonly GdiVgui _gdi = new();

    /// <summary>The frame-rate log's meter, independent of whether the on-screen one is shown.</summary>
    private readonly FpsMeter _loggedRate = new() { Mode = FpsMeter.Smoothed };

    /// <summary>Seconds since the window opened, for VGUI's 250 ms ticks.</summary>
    private readonly Stopwatch _vguiClock = Stopwatch.StartNew();

    // **The three phase totals were here until 2026-08-25.** They are `FrameLedger.Sampled`,
    // `.Posed` and `.Drawing` (B188, D90) — accumulators and a format string, with no window in any
    // of it.
    //
    // The reason they are three columns rather than one is worth keeping: **twenty milliseconds is
    // a budget, not an answer** (B99). Paused, the viewer draws the whole uncalled map at 300 frames
    // a second with a longest frame of 3.4 ms; playing, it manages 48. That difference is all CPU
    // and all in the moment rebuild, and which half of the rebuild owns it decides what to fix —
    // which a single total cannot say. B148 added the draw column for the same reason.

    // `IsShiftHeld` lived here, reading `Control.ModifierKeys` directly, on the grounds that a
    // modifier's state is something WinForms already knows. The console owns `+speed` now (D69), so
    // Shift is pressed into it like any other bound key and asking WinForms separately would be a
    // second source of truth for the same fact.

    /// <summary>Shows a file dialog over the viewer, then gives the keyboard back to it.</summary>
    /// <param name="dialog">The dialog.</param>
    /// <returns>What the dialog returned.</returns>
    /// <remarks>
    /// **Every file dialog goes through here** because one opened from a File menu that a screen
    /// reader expanded returns with WinForms back in keyboard menu mode, and every key then goes to
    /// the menu bar — see <see cref="ViewerMenu.ViewerMenuStrip"/>.
    /// </remarks>
    private DialogResult ShowModal(CommonDialog dialog)
    {
        DialogResult result = dialog.ShowDialog(this);
        _menu.Strip.LeaveStrandedMenuMode();
        return result;
    }

    /// <inheritdoc />
    protected override void OnDeactivate(EventArgs e)
    {
        // A key released while another window has focus never sends its key up to this one, so the
        // camera would fly on for ever after an alt-tab.
        ReleaseHeldKeys();

        base.OnDeactivate(e);
    }

    /// <summary>Forgets every held key, so nothing is left pressed.</summary>
    /// <remarks>
    /// **A key released while the window is not focused never sends its key up here**, so without
    /// this the camera would fly on for ever after an alt-tab — the classic held-key leak. Called on
    /// deactivation and on leaving the free view.
    /// </remarks>
    /// <remarks>
    /// **Rebuilt rather than cleared**, because a console holds more than held keys: it holds the
    /// alias table a config defined, and clearing that would silently unbind the user's controls on
    /// the first alt-tab. Reloading is not an option either — the configs may have come from
    /// anywhere. So the buttons are released one by one, which is what the engine's own
    /// empty-argument `KeyUp` path does.
    /// </remarks>
    private void ReleaseHeldKeys() => _console.ReleaseEverything();

    /// <summary>
    /// Sees key releases wherever focus is, so a held key can be known to have stopped.
    /// </summary>
    /// <remarks>
    /// **A form-level WndProc override does NOT see them, which is how this shipped broken once.**
    /// Key messages go to the focused window, and the viewport panel takes focus — the same reason
    /// the Escape handling lives where it does. <see cref="ProcessCmdKey"/> works for key DOWN only
    /// because WinForms walks it up the parent chain; there is no such courtesy for key up, so the
    /// camera kept every key it had ever been given and flew on for ever. Pressing the opposite
    /// direction then cancelled it to a standstill instead of reversing.
    ///
    /// A message filter runs before dispatch for every message on the thread, so focus stops
    /// mattering. It never consumes anything: returning false leaves the key to its normal handling.
    /// </remarks>
    private sealed class KeyReleaseFilter(ConfigConsole console) : IMessageFilter
    {
        /// <summary>WM_KEYUP.</summary>
        private const int WmKeyUp = 0x0101;

        /// <summary>WM_SYSKEYUP, which is what a key released with Alt down sends.</summary>
        private const int WmSysKeyUp = 0x0105;

        /// <inheritdoc />
        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg is WmKeyUp or WmSysKeyUp)
            {
                // The console decides what a release MEANS — for a scripted bind that is a whole
                // command line, not the removal of one key from a set.
                console.KeyUp(KeyNames.NameOf((Keys)(int)m.WParam));
            }

            return false;
        }
    }

    // **`_lastFrameAt`, `FrameIsDue` and `SinceLastFrame` were here until 2026-08-26** (B188, D90).
    // They are `FrameClock`, beside the flight clock they had never been compared against — two
    // fields measuring "time since the previous frame" whose documentation never mentioned each
    // other, which is what made "were the clocks consolidated?" a fair question with no answer in
    // the code.
    //
    // **They are still two, and Valve is why.** The engine keeps at least four time quantities and
    // names each by what it obeys; its own demo free camera flies by `absoluteframetime`
    // (`view.cpp:153`) while a limiter cannot pace itself by the duration of the frame it is
    // deciding whether to allow. See `FrameTimingConformanceTests`.
    //
    // The reasoning the old members carried moved with them: the cap has to be applied in this
    // program because asking for vertical sync does not work — the swap chain presents with a sync
    // interval of one and the viewer was still measured at about 600 frames a second, since a driver
    // forcing vsync off globally outranks the present call. And it changes only how OFTEN a frame is
    // drawn, never what is in it: the animation cycle advances from demo time, so a demo looks
    // identical at 24 frames a second and at 300. That separation is what GoldSrc got wrong.
    // **`SleepGranularitySeconds` was here until 2026-08-26** (B208). It is
    // `FramePacer.SleepGranularitySeconds`, and its measurement went with it: a limiter built on
    // sleep alone capped at about 64 frames a second whatever it was asked for, with a limit of 300
    // producing 63 to 66. How long Windows actually sleeps is not a fact about this window.

    /// <summary>Waits until the next frame is due, without burning a core doing it.</summary>
    /// <remarks>
    /// **Sleep when there is time to spare, yield when there is not.** A low cap - 24 or 30 for
    /// recording - has tens of milliseconds of budget and can afford a real sleep, which keeps the
    /// processor idle. A high cap has less budget than the clock's own granularity, so sleeping
    /// overshoots it entirely and the only accurate wait is to give up the timeslice and check
    /// again.
    ///
    /// This is a wait on a CLOCK, which is the one thing a frame limiter can legitimately do:
    /// there is no condition to synchronise on, because the condition is the passage of time.
    /// </remarks>
    private void WaitForTheNextFrame()
    {
        // **`FramePacer` decides, this acts** (B208). The threading primitive stays beside the
        // message pump; the policy — including the granularity threshold — is testable without any
        // test ever sleeping.
        switch (_clock.WaitFor(_settingsOwner.FrameRateLimit))
        {
            case FrameWait.Sleep:
                Thread.Sleep(1);
                break;

            case FrameWait.Yield:
                Thread.Yield();
                break;

            default:
                break;
        }
    }

    // `_framesDrawn` was here until 2026-08-25. It is the count `FrameLedger.Drew` keeps — the last
    // of the six per-second counters this window reset by hand.
    //
    // **`_rateReportedAt`, `_collections`, `GarbageThisSecond` and `CountFrame` followed on
    // 2026-08-26** (B188, D90). The clock is `FrameReporter`'s `IElapsedTime`, which is what lets a
    // second pass in a test without one passing; the collection deltas, the quiet-second threshold
    // and the format string are `GarbageCounter`. Neither had a test, because reaching either meant
    // constructing a form.

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
        if (_device is null || !_device.HasWorld)
        {
            return;
        }

        long viewAt = Stopwatch.GetTimestamp();

        // **The camera itself rather than its matrix**, so the projection and the cull volume are
        // derived from one thing. `SetCamera(float[])` still exists for the viewmodel pass, which
        // has a camera of its own and no business culling against the world's.
        // **The one per-frame view setup, so this is what advances the chase camera's recovery** —
        // `CViewRender::SetUpView` computes the view once and everything downstream reads it.
        FreeCamera viewing = _camera.ViewCameraNow(_demoFrameSeconds);

        _lastViewing = viewing;

        long deviceAt = Stopwatch.GetTimestamp();

        // **The fog in force at this tick, from the demo** (B139) — `EnableWorldFog` runs in every
        // view setup, so this is set beside the camera rather than once per map.
        TickFeeds feeds = _camera.FeedsNow();

        _device.WorldFog = feeds.WorldFog;
        _device.SkyFog = feeds.SkyFog;
        _device.MonitorCamera = feeds.MonitorCamera;
        _device.Tonemap = feeds.Tonemap;

        // The exposure walks by game frame time and resets when the followed player changes (B514).
        // **A pending capture is a paused frame, and gets a tick of time anyway** (B520): TF2 arrives at a tick by playing
        // to it, so its exposure has had seconds; a still that held the reset value of 1 was 1/0.70 too bright outdoors.
        _device.ExposureSeconds = _launch.ShotPath is not null && !_opening.Finished
            ? CaptureExposureSeconds
            : (float)_demoFrameSeconds;
        _device.ObserverTarget = _camera.FollowedEntity();

        _device.SetCamera(viewing, _menu.SurfaceColours.Checked);

        long particlesAt = Stopwatch.GetTimestamp();

        if (_device is { } drawing)
        {
            _presenter.DrawEffects(viewing, drawing);
        }

        // **Each piece named, because the `camera` column alone held 102 and 410 ms stalls** on f12
        // in seconds with no collection, and it is four unrelated pieces of work.
        StallReport.Camera(
            flyTicks,
            deviceAt - viewAt,
            particlesAt - deviceAt,
            Stopwatch.GetTimestamp() - particlesAt,
            _renderLog);
    }

    /// <summary>The moment <see cref="ShowMoment"/> last showed, fraction included — what the players are posed at.</summary>
    private double _shownTick;

    // **`ReportSlowFrame` was here until 2026-08-25** (B188, D90). It is `StallReport.Frame`, and
    // its eight timestamp parameters became a `FramePhases` record — the same correction
    // `MomentPhases` already carried. Eight `long`s in the right order is a signature that can be
    // called in the wrong one, and the failure is silent: seven plausible numbers against the wrong
    // labels.
    //
    // **That correction was half of one, and the other half arrived on 2026-08-26** (B203). Wrapping
    // the timestamps in a record stopped the CALL being mis-ordered, but `FramePhases.Between` still
    // named the frame's stages in its parameter list, so the order was written down twice. It is now
    // written once, executably, in `FrameSequence.Run`.
    //
    // The reasoning is worth keeping, because it is why the ledger exists at all. It was built
    // after four rounds of one-suspect-at-a-time, three right and one wrong: the model upload, the
    // model packing and the per-frame logging were each found by instrumenting a hypothesis, and
    // the sound decode was instrumented on the same reasoning and recorded nothing. Proposing
    // suspects does not converge. A ledger over the whole frame does, because the RESIDUAL column
    // is the part nobody has thought to measure — and a frame that is slow with every named column
    // small says exactly that. `unaccounted` is Valve's own name for it.

    // **`PrecacheModels` was here until 2026-08-26** (B208). It is `DemoModels.Precache` now, and
    // its notes went with it: the D86 timing argument, the 385 ms measurement, and the finding that
    // a demo's TIMELINE is a better precache list than the engine's own `modelprecache` table —
    // which names what the server precached, including models this recording never shows.
    //
    // **What stayed here is the one thing that is about this window**: the call runs inside
    // `LoadDemoAsync`'s `Task.Run`, before `_readingMap` is cleared, so it works on the map-read
    // worker behind the barrier that already holds the render loop off (B146). That is a fact about
    // our threading, not about precaching, and it is written at the call site where it applies.
    //
    // It outlived its twin by a day: `PrecacheSounds` moved out and this one, identical in shape,
    // was left behind. An asymmetry like that reads as deliberate afterwards, which is why the
    // second audit went member by member instead of grepping for arithmetic.

    // **The `PrecacheSounds` shim went with its twin on 2026-08-26** (B208). Both call sites name
    // `DemoSounds.Precache` directly now, so the window forwards nothing — and its D86/D87 citation
    // went with it: `CBaseEntity::PrecacheSound` asserts "too late" rather than merely preferring
    // early (`SoundEmitterSystem.cpp:1497`), which is a fact about the engine, not about a window.

    /// <summary>Where the picker looks for HUDs: this program's OWN <c>custom/</c> folder, never `tf/custom` (D193).</summary>
    private static string ProgramCustomFolder { get; } =
        System.IO.Path.Combine(AppContext.BaseDirectory, "custom");

    /// <summary>Changes the HUD and remembers the choice, hot-swapped with no restart.</summary>
    /// <param name="hud">A folder or `.vpk` under <see cref="ProgramCustomFolder"/>, or null for TF2's stock HUD.</param>
    /// <remarks>
    /// **The rebuild is not done here.** The presenter already compares the stored choice against what it last built
    /// and remakes its archives when they differ, so setting it is the whole swap; the next frame's HUD reads through
    /// the new archives. `--hud` sets it at launch and the remembered choice from settings is the fallback (D193).
    /// </remarks>
    private void SetChosenHud(string? hud)
    {
        // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
        if (_presenter.SetChosenHud(hud) is { } said)
        {
            _status.Text = said;
        }
    }

    /// <summary>This frame's VGUI: the client's HUD, then the tools panel and `CFPSPanel` on it above.</summary>
    /// <returns>The draw list.</returns>
    /// <remarks>What is built, and in which order, is the presenter's (D214); this is what a window knows of it.</remarks>
    public VguiDrawList? BuildOverlay() => _presenter.BuildOverlay(_device);

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

        Close();
    }

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

    /// <summary>The measured frames' phases, summed, for a paused measurement that writes no per-second line.</summary>
    private FramePhases _phasesMeasured;

    /// <summary>Whether <c>--then-seek</c> has already paused and seeked, so the second measurement is running.</summary>
    private bool _seekedForMeasure;

    /// <summary>Prints one measurement's heading and its per-second lines, the first dropped.</summary>
    private void PrintMeasured(string heading)
    {
        // The frame count beside the seconds: a paused frame writes no per-second line (the rate meter samples only
        // while the scene rebuilds), so for B420's paused measurement this mean IS the measurement.
        // Stryker disable once all : a mutated interpolated string cannot bind to its handler (CS1620) and Safe Mode drops the method - B410
        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{heading}; {_framesMeasured} frames, mean {(_framesMeasured > 0 ? _frameSeconds * 1000d / _framesMeasured : 0d):0.00} ms"));

        if (_framesMeasured > 0)
        {
            // The same phases the per-second line names, as means over every measured frame.
            double each(double total) => total * 1000d / Stopwatch.Frequency / _framesMeasured;
            FramePhases sum = _phasesMeasured;
            (int trails, int bursts, int quads) = _effects.ParticleCounts;

            // Stryker disable once all : a mutated interpolated string cannot bind to its handler (CS1620) and Safe Mode drops the method - B410
            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  phases, mean ms: sound {each(sum.Sound):0.00}, camera {each(sum.Camera):0.00}, project {each(sum.Project):0.00}, " +
                $"advance {each(sum.Advance):0.00}, capture {each(sum.Capture):0.00}, hud {each(sum.Hud):0.00}, draw {each(sum.Draw):0.00}; " +
                $"particles now: {trails} trails, {bursts} bursts, {quads} quads last frame"));
        }

        _framesMeasured = 0;
        _phasesMeasured = default;

        foreach (string line in _measured.Lines)
        {
            Console.WriteLine(line);
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
            // screen-space projection is not what is being drawn (B98).
            UploadCamera();
        }
    }

    /// <summary>Steps the sound for this frame, with the ears wherever the camera is.</summary>
    /// <remarks>
    /// **What is left here is the LISTENER and nothing else** (B188, D214): which camera the ears are at and whose eyes
    /// it is, asked for by <see cref="ViewerPresenter.PlaySounds"/> only once it knows there is a device to play through.
    /// </remarks>
    private void PlaySounds() => _presenter.PlaySounds(_listener, _listenerEyes, _transport.CurrentTick);

    // **Two hundred and thirty lines moved to SoundPresenter on 2026-08-25** (B188). Deciding what
    // should be audible at a tick is not a window's job, and none of it needed one: the schedule,
    // the seek, the loop re-attenuation, the soundscape and every start now sit behind
    // `IAudioSink`, where a test needs no sound card.
    //
    // Valve's split is the same — `CSoundEmitterSystem : CBaseGameSystem`
    // (`SoundEmitterSystem.cpp:134`) decides what to emit and calls through `enginesound`.

    // **`ReportSlowSounds` was here until 2026-08-25.** It is `StallReport.Sounds` (B188, D90).
    //
    // It was written because precaching the decodes did not empty this bucket: 394 sounds moved to
    // load time and the `sound` phase still read 27-105 ms on the frames that froze, so the
    // remaining cost was one of five and a single number could not say which.
    //
    // **Its old note claimed "what a slow line looks like is the view's business", and that was
    // wrong** — it is what a form happened to be doing, which is not the same thing. Formatting a
    // measurement needs no window, could not be tested inside one, and a second frontend would have
    // to write it again. The presenter measures the phases and Presentation formats them.

    // **The soundscape system moved to Tf2DemoSalvage.Scene on 2026-08-25** (B188). What stood here
    // was 165 lines with no window in any of it — Valve makes this a per-frame GAME system,
    // `C_SoundscapeSystem : CBaseGameSystemPerFrame` (`c_soundscape.cpp:78`), and a window class was
    // never the right home for a thing the engine models as a system of its own.
    //
    // The B173 note it carried is worth keeping where the code went, and it did: ambience is most of
    // what a map sounds like and none of it is in the demo, because a soundscape is chosen by the
    // SERVER from `env_soundscape` entities and reaches the client as an index in private
    // per-player data that a SourceTV recording carries for nobody.

    // `Sample` was here until 2026-08-25, with `_soundCache`, `_soundsUnopened` and `_precaching`.
    // It is `SoundCache` in the Audio project now (B188, D90) — the engine keeps its sample cache
    // behind `IEngineSound` (`IEngineSound.h:89-91`) and game code asks it, so a window owning one
    // was ours alone. Ten tests came with the move; it had none, because reaching it needed an STA
    // thread, a device and a TF2 install.

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
    private bool RenderFrame()
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

        FramePhases phases = FrameSequence.Run(this);

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
        bool onScreen = _presenter.HasDemo && _presenter.LoadsInFlight == 0;

        if (onScreen && _wasOnScreen)
        {
            _frameSeconds += _clock.LastFrameSeconds;
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
        }

        _wasOnScreen = onScreen;

        // **The log's own meter, always smoothed**, fed the frame time the on-screen one gets. The on-screen meter answers
        // nothing while `cl_showfps` is 0 — Valve's rule for drawing it — and with no TF2 install it is never built, so a
        // log that borrowed its reading went silent on a fresh profile and `--measure` counted 0 samples on CI.
        if (_frameRateLog.Report(_loggedRate.Sample(_clock.LastFrameSeconds), phases, _frameSeconds) is { } rate)
        {
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
        }

        // **Counted in seconds of PLAYBACK.** A wall clock would spend the first twenty seconds on
        // archives and the map, so `--measure 40` would be about two seconds of frames — which is
        // exactly the mistake this replaces.
        if (_launch.MeasureSeconds is not null && !_transport.Playing)
        {
            // A paused viewer repaints only when asked; a measurement of the paused frame asks every frame, as a user
            // moving the free camera does (B420 was measured that way).
            _viewport.Invalidate();
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
                    $"full screen {(IsFullScreen ? "on" : "off")} took " +
                    $"{clock.Elapsed.TotalMilliseconds:F0} ms to the first frame at " +
                    $"{_viewport.ClientSize.Width}x{_viewport.ClientSize.Height}"));
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
        if (_device is { } device)
        {
            _moments.PoseNow(device.Frustum, device.Views, device.Eye, device.ScreenView);
        }
        else
        {
            _moments.PoseNow();
        }

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
    public void Draw(VguiDrawList? overlay)
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

        DrawScene(overlay);
    }

    /// <summary>Hands this frame's scene to the device.</summary>
    private void DrawScene(VguiDrawList? overlay)
    {
        if (_device is null)
        {
            return;
        }

        // The recorder's screen overlay, resolved against the map's loaded materials (`CViewRender::PerformScreenOverlay`).
        _device.ScreenOverlay = _presenter.ScreenOverlay;

        // The followed player, whom a monitor view force-draws though the main view does not (B511).
        _device.MonitorOnlyModels = _moment.UndrawnInstances;

        _device.DrawFrame(
            BackgroundRed,
            BackgroundGreen,
            BackgroundBlue,
            // Empty, always. The flat fill was drawn only when there was no textured map and was
            // built from the map, so it was dead in both branches (see ProjectMap). The parameter
            // stays until Device3D's signature is revised, which is a change to the render seam and
            // not to this frame.
            [],
            // **The line channel carries mat_leafvis, in WORLD units, drawn depth-tested** (D95).
            // It used to be the BSP's own edge segments projected for the overhead view; that view
            // is gone (D49) and `mat_wireframe` replaced the outline, so the channel now does what
            // the engine's debug lines do — absolute coordinates, transformed on the GPU, occluded
            // by the geometry the box describes.
            //
            // The projected outline is gone entirely (B151 closed by deletion rather than by the
            // split it proposed): nothing read it, and 615 ms of a 679 ms frame was spent building
            // it. `MapOutline` still supplies the play-area bounds, which is the half that was
            // actually wanted.
            _presenter.LeafBoxLines(_debug.LeafVis),

            // **Empty since 2026-08-26** (D98), like the fill above it. This channel carried the
            // flat player markers, projected through the orthographic camera. The parameter stays
            // until `Device3D`'s signature is revised, which belongs with the render seam rather
            // than with this window.
            [],
            _moment.Instances,
            _moment.ViewmodelInstances,
            _moment.ViewmodelCamera?.ToMatrix(),
            overlay);
    }

    /// <summary>
    /// Notes that the viewport changed size, without doing the work yet.
    /// </summary>
    /// <remarks>
    /// **Entering full screen steps through seventeen distinct viewport sizes** - each control
    /// hidden, the border dropped, the window state changed, the taskbar uncovered - and projecting
    /// the world at every one of them meant seventeen rebuilds of a quarter of a million vertices
    /// for sixteen pictures nobody ever saw. Measured on cp_badlands from the viewer's own log.
    ///
    /// The swap chain is still resized immediately: it is cheap, and letting it lag the panel
    /// stretches the last frame across the new rectangle, which is visible.
    /// </remarks>
    /// <summary>Zooms about the pointer, the way every map viewer does.</summary>
    /// <remarks>
    /// **About the cursor rather than the centre**, so the thing being looked at stays under the
    /// pointer as the view magnifies. Zooming about the centre instead makes closing in on a
    /// corner a game of chasing it back into view.
    /// </remarks>
    private void OnViewportWheel(object? sender, MouseEventArgs e)
    {
        // `step` was `e.Delta > 0 ? 1.25f : 1f / 1.25f` here until 2026-08-26 (B208). The ratio is
        // `MapZoom.Step` and the direction is `In()` or `Out()`, so the window reads the wheel and
        // names neither.

        // Whether there is a map to fly over, whether the free camera is the one on and how far a notch
        // flies are the camera's (`CameraPresenter.Wheel`); that the WHEEL asks is this window's.
        _camera.Wheel(e.Delta > 0);

        // **The zoom-at-cursor branch was here until 2026-08-26** (D98). It magnified the
        // orthographic map and recentred so the world point under the pointer stayed put — correct
        // for a projection that no longer exists.
        //
        // **The wheel is not lost, and never was**: in the free camera it flies, which is the branch
        // above and the one that was always used. The owner: *"the mouse wheel was working in free
        // cam before, i used it all the time"*. What is gone is its SECOND meaning, not the wheel.
    }

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

    /// <summary>Routes a wheel turn anywhere over the viewport to the zoom.</summary>
    private void OnFormWheel(object? sender, MouseEventArgs e)
    {
        Point inViewport = _viewport.PointToClient(Cursor.Position);

        if (!_viewport.ClientRectangle.Contains(inViewport))
        {
            return;
        }

        OnViewportWheel(sender, new MouseEventArgs(e.Button, e.Clicks, inViewport.X, inViewport.Y, e.Delta));
    }

    /// <remarks>
    /// **The click goes into the console, so whatever the player bound the button to is what
    /// happens (B145).** That is how the game does it — `ClientModeShared::HandleSpectatorKeyInput`
    /// dispatches on `pszCurrentBinding`, the bound command STRING, not on a key code:
    ///
    /// <code>
    /// else if ( down &amp;&amp; pszCurrentBinding &amp;&amp; Q_strcmp( pszCurrentBinding, "+attack" ) == 0 )
    /// {
    ///     engine->ClientCmd( "spec_next" );
    ///     return 0;
    /// }
    /// </code>
    ///
    /// So somebody who has moved attack to a thumb button gets target cycling on that thumb button,
    /// without this method knowing anything about it.
    ///
    /// **The drag is kept separate and deliberately not routed through the console**, because it is
    /// not a bound action at all — it is a gesture, and it has no Source command to name it.
    /// </remarks>
    private void OnViewportMouseDown(object? sender, MouseEventArgs e)
    {
        // **Clicking the scene focuses it, which it could not do before B216.** Without this, focus
        // stays wherever it was — the playlist — so the window's idea of "the focused control" is a
        // list while somebody is flying a camera across a map. Every shortcut question downstream
        // depends on focus meaning what the user is working with.
        _viewport.Focus();

        if (e.Button == MouseButtons.Left)
        {
            _dragFrom = e.Location;
        }

        _console.KeyDown(KeyNames.NameOf(e.Button));
    }

    private void OnViewportMouseMove(object? sender, MouseEventArgs e)
    {
        // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
        if (_dragFrom is not { } from || !_camera.Drag(e.Location.X - from.X, e.Location.Y - from.Y))
        {
            return;
        }

        _dragFrom = e.Location;
        _presenter.InvalidateWorld();
    }

    private void OnViewportMouseUp(object? sender, MouseEventArgs e)
    {
        _dragFrom = null;
        _console.KeyUp(KeyNames.NameOf(e.Button));
    }

    /// <summary>Performs an action a bound key or button asked for.</summary>
    /// <remarks>
    /// **Only the instant actions arrive here.** Flight is held rather than triggered and is read
    /// per frame by <see cref="FlyCamera"/>; the console tells the two apart by
    /// <see cref="ConfigConsole.HeldActions"/> rather than by the command's spelling, because TF2
    /// writes the camera switch as `+jump` even though nothing about it is continuous.
    ///
    /// **Camera mode and reset are still handled in `ProcessCmdKey` by comparing key codes**, and
    /// that inconsistency is known rather than overlooked: moving them here means pressing every
    /// bound key into the console, which changes what `ProcessCmdKey` swallows and is a bigger
    /// change than this one. Filed as the remaining half of B145.
    /// </remarks>
    private void OnConsoleAction(object? sender, ViewerActionEventArgs e)
    {
        switch (e.Action)
        {
            case ViewerAction.CycleTargetForward:
                _camera.CycleTarget(reverse: false);
                break;

            case ViewerAction.CycleTargetReverse:
                _camera.CycleTarget(reverse: true);
                break;

            default:
                break;
        }
    }

    // **`WorldAt` was here until 2026-08-26** (D98). It answered "which world point is under this
    // pixel", which only has an answer for a projection that maps the screen onto a plane. A
    // perspective view of a 3D world does not: a pixel is a ray, not a point, and answering it
    // properly is a trace rather than an unprojection.

    private void OnViewportResize(object? sender, EventArgs e)
    {
        _overlay?.PositionOver(_viewport);
        _presenter.InvalidateWorld();

        if (_device is null || _viewport.ClientSize.Width <= 0 || _viewport.ClientSize.Height <= 0)
        {
            return;
        }

        _device.Resize(_viewport.ClientSize.Width, _viewport.ClientSize.Height);
    }

    /// <inheritdoc />
    /// <remarks>
    /// **Escape leaves full screen, and only then.** Handled here rather than as a KeyDown
    /// because the viewport panel takes focus and a form-level KeyDown would never see the key -
    /// ProcessCmdKey runs before the keystroke reaches any child control.
    ///
    /// Deliberately does nothing when windowed. Swallowing Escape everywhere would break the
    /// ordinary meaning of the key: cancelling a dialog, closing a menu.
    /// </remarks>
    /// <summary>The control that actually has the keyboard, however deeply nested.</summary>
    /// <returns>The focused control, or null when nothing on the form has focus.</returns>
    /// <remarks>
    /// **`Form.ActiveControl` is not this**, and assuming it was cost a wrong fix (B212). It answers
    /// with the active child of the FORM, so when focus is inside a `UserControl` — which
    /// <see cref="TransportBar"/> is, and `UserControl` derives from `ContainerControl` — it returns
    /// the transport bar rather than the slider inside it. A guard written against it therefore
    /// matched nothing and changed nothing, and the test that found the bug went on failing in
    /// exactly the same way, which is the worst outcome a fix can have.
    ///
    /// Each container on the chain holds its own `ActiveControl`, so the real answer is the bottom
    /// of that chain.
    /// </remarks>
    private Control? FocusedControl()
    {
        Control? focused = ActiveControl;

        while (focused is ContainerControl { ActiveControl: { } inner })
        {
            focused = inner;
        }

        return focused;
    }

    /// <summary>What kind of thing has focus, in terms no toolkit owns.</summary>
    /// <returns>The focused widget's kind.</returns>
    /// <remarks>
    /// **This is the whole WinForms-specific half of the shortcut guard**, and it is deliberately
    /// this small. <see cref="WidgetKeys"/> holds which keys each kind uses, in `Presentation`, which
    /// cannot reference a toolkit at all — so moving this viewer to another front end means writing
    /// these ten lines against that toolkit's focus API, not working the key rules out again.
    ///
    /// **Ordered from the specific to the general**, since `ComboBox` is a `Control` and
    /// `CheckBox` is a `ButtonBase`; a broader arm placed first would swallow the narrower ones.
    /// </remarks>
    private FocusedWidget FocusKind() => FocusedControl() switch
    {
        null => FocusedWidget.None,
        TextBoxBase => FocusedWidget.Text,
        TrackBar or ScrollBar or NumericUpDown => FocusedWidget.Slider,
        ListBox or ListView or ComboBox or TreeView or DataGridView => FocusedWidget.List,
        ButtonBase => FocusedWidget.Button,
        _ => FocusedWidget.Other,
    };

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // **Nothing is a shortcut while somebody is typing** (B212). `ProcessCmdKey` runs before any
        // control sees a key and returning true consumes it, so every binding below was reaching
        // over the search box.
        //
        // **`Space` is the DEFAULT bind for switch-camera-mode**, so typing `cp process` toggled
        // first person instead of inserting a space — and in the free camera the flight keys took
        // `w`, `a`, `s` and `d` as well. Neither needed an unusual configuration to hit; the shipped
        // defaults are enough.
        //
        // This is a guard on the binds rather than a binding of its own: no key is named here, so it
        // adds nothing to un-hardcode later (D101).
        // **Widened 2026-08-26 from "is it a text box" to "does the focused thing use this key".**
        // The old guard excused text alone, which was enough for the search box and wrong in general:
        // D101 lets a person bind anything, so `bind "UPARROW" "+forward"` in someone's config takes
        // the arrow keys away from the playlist, and binding `HOME` to a speed reset takes it from
        // both sliders. Nobody had to introduce that defect — it was waiting for one line of config.
        //
        // The decision is `WidgetKeys.Keeps`, in `Presentation`, which knows nothing about WinForms;
        // this line and `FocusKind` are the whole of the toolkit-specific part.
        // **`CTRL`/`ALT` is passed separately because the name is the BARE key.** `NameOf` masks the
        // modifiers off, so `Ctrl+R` and `r` arrive here spelled identically — and the playlist keeps
        // `r` for type-ahead. Without this the list would swallow `Ctrl+R` and reset-camera would
        // stop working whenever it had focus. `SHIFT` is not a command modifier: it types a capital.
        if (WidgetKeys.Keeps(
                FocusKind(),
                KeyNames.NameOf(keyData & Keys.KeyCode),
                (keyData & (Keys.Control | Keys.Alt)) != Keys.None))
        {
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // **Page DOWN descends through the map**, taking the roofs off first, and page up brings
        // them back. The obvious reading of the key is the one to follow: the first version had it
        // inverted, and pressing page down 166 times did nothing because the cut was already at
        // zero and the log said so.
        // **Not while a control that uses these keys has focus** (B212). `ProcessCmdKey` runs before
        // any control sees a key and returning true consumes it, so this stole Home, Page Up and
        // Page Down from everything on the form: Home in the search box changed the map's height cut
        // instead of moving the caret, and the scrub bar, the playlist and the speed slider all lost
        // their standard navigation.
        //
        // Found because a UI test pressed Home on the focused speed slider and nothing happened —
        // the same shape as B165's F11 and the F12 double-bind, which is now three times in this
        // file. **A form-level shortcut must not take a key a focused control already means
        // something by.**
        // **The height cut was here, on hardcoded Home, Page Up and Page Down** (B213, D101). It is
        // gone, and both reasons matter.
        //
        // **It never worked.** Its shader comment said "the cut is on depth, which is height" — an
        // equivalence that holds ONLY under the orthographic top-down projection D98 deleted. Under
        // a perspective camera `pos.z` is distance from the eye, so it cut away whatever was nearest
        // rather than whatever was highest. The owner: *"that was a ortho thing that should be
        // ripped out and never worked in the first place"*.
        //
        // **And the keys were hardcoded, which is now forbidden outright** (D101): *"no hard coded
        // controls ever"*, *"everything gets to be customized so runs through the config"*. Three
        // literal `Keys.` comparisons ahead of every control on the form, stealing Home from the
        // search box and Page Up from the playlist — found because a test pressed Home on a focused
        // slider and nothing happened (B212).

        // **Flying the camera.** W and S run along the way it is looking, A and D strafe, and
        // Space and Control lift and drop it along the world's up axis rather than the camera's —
        // which is what every editor does, because rising along a pitched view drifts sideways and
        // feels broken.
        //
        // **The key is only RECORDED here; the movement happens per frame** (B97). Moving on the
        // message meant Windows' auto-repeat set the speed: nothing for the repeat delay, then fixed
        // jumps at the repeat rate, and never two directions at once because auto-repeat reports
        // only the last key held. See FreeFlight.
        if (_camera.FreeLook && FreeFlight.IsFlightKey(keyData & Keys.KeyCode, _bindings))
        {
            _console.KeyDown(KeyNames.NameOf(keyData));
            return true;
        }

        // **Bound actions rather than hardcoded keys (D68).** TF2 does the same: its spectator HUD
        // prints `[%jump%]` beside "Switch Camera Mode" and substitutes whatever the player bound,
        // so nothing in the game hardcodes Space — it hardcodes the action. These resolve through
        // `_bindings`, which a settings file can override.
        //
        // **Switch camera mode defaults to Space**, which is what TF2 binds it to.
        if (keyData == KeyNames.Resolve(_bindings.KeyFor(ViewerAction.SwitchCameraMode)))
        {
            return _camera.CycleCameraMode();
        }

        // **Source's own `firstperson` and `thirdperson`, so a config reaches them by name.** They
        // ask for a mode outright where `SwitchCameraMode` cycles, which is the same pair of routes
        // the engine offers: the spectator HUD cycles with `+jump`, and the commands exist for
        // anyone who wants to land on one directly.
        if (keyData == KeyNames.Resolve(_bindings.KeyFor(ViewerAction.FirstPersonView)))
        {
            return _camera.EnterCamera(CameraMode.FirstPerson);
        }

        if (keyData == KeyNames.Resolve(_bindings.KeyFor(ViewerAction.ThirdPersonView)))
        {
            return _camera.EnterCamera(CameraMode.ThirdPerson);
        }

        // **The first binding added through B214's mechanism rather than as a literal** (B216). It
        // was agreed weeks ago and deliberately not built — *"do not hard code home, no new hard
        // codes"* — because there was nothing to add it to.
        if (keyData == KeyNames.Resolve(_bindings.KeyFor(ViewerAction.ResetSpeed)))
        {
            _transport.ResetSpeed();
            return true;
        }

        if (keyData == KeyNames.Resolve(_bindings.KeyFor(ViewerAction.ResetCamera)))
        {
            return _camera.ResetCamera();
        }

        // **Through the binding table, and its default key MOVED** (B214, D101). This was
        // `Keys.F12` — which TF2 gives to `replay_togglereplaytips` and Steam's overlay claims for
        // its own capture — while our F5 was a debug view. TF2's screenshot key is F5, so the two
        // were swapped against the game. `ViewerAction.Screenshot` speaks Valve's `screenshot`
        // command, so a config that rebinds screenshots moves this with it.
        if (keyData == KeyNames.Resolve(_bindings.KeyFor(ViewerAction.Screenshot)))
        {
            CaptureViewportToFile();
            return true;
        }

        // **Logged before the guard, so a key that ARRIVED is distinguishable from one that never
        // did.** Full screen has twice been reported as impossible to leave, and the two states
        // look identical from outside: the key reaching this method and being ignored, and the key
        // going to whichever window took the foreground. Only a line written here separates them.
        if (keyData == Keys.Escape ||
            keyData == KeyNames.Resolve(_bindings.KeyFor(ViewerAction.FullScreen)))
        {
            // Stryker disable once all : a mutated interpolated string cannot bind to its handler (CS1620) and Safe Mode drops the method - B410
            _renderLog.LogInformation(
                "{Message}",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{keyData} reached the form; full screen is {IsFullScreen}"));
        }

        // **Escape stays a literal, and this is the ONE exception D101 gets** (B214). Every other
        // key in this window now resolves through `KeyBindings`; this one is the way out of full
        // screen, so a config that rebound it away would leave a user with no menu, no title bar and
        // no key that closes either. TF2 binds `ESCAPE` to `escape` for the same structural reason —
        // it is the escape hatch, not a control.
        //
        // Stated rather than left as an oversight, because an unexplained literal in this method is
        // exactly what B212 and B214 were both about.
        if (keyData == Keys.Escape && IsFullScreen)
        {
            SetFullScreen(false);
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>Where keyboard focus sits inside this form, for the log.</summary>
    /// <remarks>
    /// **The foreground and the focus are different questions, and only the second was open.** The
    /// probe showed this window holding the foreground on both sides of the full-screen transition
    /// while Escape never reached <see cref="ProcessCmdKey"/> at all — so the key was not going to
    /// another application, it was being dropped inside this one.
    ///
    /// The candidate is stated in the transition itself: the playlist takes focus when the window
    /// opens, and full screen hides it. A control that is hidden while focused leaves the form with
    /// no focused child, and a keystroke with nowhere to land goes nowhere.
    /// </remarks>
    // Stryker disable once all : a mutated interpolated string cannot bind to its handler (CS1620) and Safe Mode drops the method - B410
    private string FocusHere() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"; active control {ActiveControl?.Name ?? "none"}" +
            $" (visible {ActiveControl?.Visible.ToString() ?? "n/a"})" +
            $", form contains focus {ContainsFocus}");

    /// <summary>Test seam onto <see cref="ProcessCmdKey"/>, which is protected.</summary>
    /// <param name="msg">The window message.</param>
    /// <param name="keyData">The key pressed.</param>
    /// <returns>Whether the key was handled.</returns>
    protected bool ProcessKey(ref Message msg, Keys keyData) => ProcessCmdKey(ref msg, keyData);

    /// <summary>Builds one action-row button with its automation id and accessible name.</summary>
    private static Button ActionButton(string id, string text, string description, EventHandler onClick)
    {
        Button button = new()
        {
            Name = id,
            AccessibleName = text,
            AccessibleDescription = description,
            Text = text,
            Width = 90,
            Height = 28,
        };
        button.Click += onClick;
        return button;
    }

    private void OpenFolder()
    {
        using FolderBrowserDialog dialog = new()
        {
            Description = "Choose a folder of demos. Subfolders are included.",
            UseDescriptionForTitle = true,
        };

        if (ShowModal(dialog) == DialogResult.OK)
        {
            AddToLibrary(dialog.SelectedPath);
        }
    }

    /// <summary>Adds paths to the library and refreshes the playlist.</summary>
    private void AddToLibrary(params string[] paths)
    {
        // Opening, ordering, filtering and the count's words are the playlist owner's (`PlaylistPresenter.Add`).
        string count = _library.Add(_search.Text, paths);

        ShowPlaylist();

        _status.Text = count;
    }

    /// <summary>Rebuilds the playlist from the library and the search box.</summary>
    /// <remarks>
    /// Nothing is constructed here. Setting <see cref="ListView.VirtualListSize"/> tells the
    /// control how many rows exist, and it then asks for the handful it needs to paint - which is
    /// what keeps a keystroke cheap on an archive of thousands.
    /// </remarks>
    private void RefreshPlaylist()
    {
        _library.Filter(_search.Text);

        ShowPlaylist();
    }

    /// <summary>Tells the list control how many rows there are now.</summary>
    private void ShowPlaylist()
    {
        // Selection indices refer to the OLD list. Clearing first avoids the control asking for a
        // row that the new, shorter list does not have.
        _playlist.SelectedIndices.Clear();
        _playlist.VirtualListSize = _library.Count;
        _playlist.Invalidate();
    }

    /// <summary>Supplies one row of the playlist on demand.</summary>
    private void OnRetrieveVirtualItem(object? sender, RetrieveVirtualItemEventArgs e)
    {
        // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
        if (_library.Row(e.ItemIndex) is not { } entry)
        {
            // Reachable during a resize that races a filter change. A blank row is survivable;
            // an exception out of a paint handler is not.
            e.Item = new ListViewItem(string.Empty);
            return;
        }

        ListViewItem row = new(entry.Name) { Tag = entry.Path };
        row.SubItems.Add(entry.Folder);
        e.Item = row;
    }

    /// <summary>The export or compile in flight, held rather than <c>async void</c> (see <see cref="Loading"/>).</summary>
    public Task<string> FileWork { get; private set; } = Task.FromResult(string.Empty);

    /// <summary>Writes the open demo's assembly text — the CLI's <c>--asm</c> — to a file the user picks.</summary>
    private Task<string> ExportDemo()
    {
        // Before anything that can return early, so a press that did nothing is visible.
        _log.LogInformation("{Message}", $"export: pressed with {_presenter.DemoPath ?? "no demo open"}");

        // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
        if (_presenter.DemoPath is not { } demoPath)
        {
            _status.Text = "Open a demo first.";
            return Task.FromResult(_status.Text);
        }

        using SaveFileDialog dialog = new()
        {
            Filter = "Demo assembly (*.txt)|*.txt|All files (*.*)|*.*",
            Title = "Export demo assembly",
            FileName = Path.GetFileNameWithoutExtension(demoPath) + ".txt",
        };

        return ShowModal(dialog) == DialogResult.OK
            ? RunFileWork($"Exporting {Path.GetFileName(demoPath)}...", dialog.FileName, output =>
                ViewerPresenter.ExportDemo(demoPath, output))
            : FileWork;
    }

    /// <summary>Compiles an assembly text file back into a byte-identical demo — the CLI's <c>--compile</c>.</summary>
    private Task<string> CompileDemo()
    {
        string source;

        using (OpenFileDialog open = new()
        {
            Filter = "Demo assembly (*.txt)|*.txt|All files (*.*)|*.*",
            Title = "Compile demo assembly",
        })
        {
            if (ShowModal(open) != DialogResult.OK)
            {
                return FileWork;
            }

            source = open.FileName;
        }

        using SaveFileDialog save = new()
        {
            Filter = "Source demos (*.dem)|*.dem|All files (*.*)|*.*",
            Title = "Save compiled demo",
            FileName = Path.GetFileNameWithoutExtension(source) + ".dem",
        };

        return ShowModal(save) == DialogResult.OK
            ? RunFileWork($"Compiling {Path.GetFileName(source)}...", save.FileName, output =>
                ViewerPresenter.CompileDemo(source, output))
            : FileWork;
    }

    /// <summary>Runs an export or compile off the UI thread and puts its outcome in the status bar.</summary>
    private async Task<string> RunFileWork(string starting, string output, Func<string, string> work)
    {
        _status.Text = starting;
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

        _status.Text = result;
        return result;
    }

    private void OpenDemo()
    {
        using OpenFileDialog dialog = new()
        {
            Filter = "Source demos (*.dem)|*.dem|All files (*.*)|*.*",
            Title = "Open demos",

            // Several at once, the way a file browser and every video player does it.
            Multiselect = true,
        };

        if (ShowModal(dialog) == DialogResult.OK)
        {
            AddToLibrary(dialog.FileNames);
        }
    }

    /// <summary>Opens the folder picker at startup when no TF2 install was found (D210).</summary>
    /// <remarks>
    /// Never on a headless run (<c>--shot</c>, <c>--measure</c>): a modal dialog nobody sees would hang
    /// it. The UI suite passes <c>+cl_game_folder_ask 0</c>, because CI has no TF2.
    /// </remarks>
    private void AskForGameFolderIfMissing()
    {
        bool headless = IsHeadless;
        string? found = _maps.GameFolder();

        // Logged before any dialog: the UI test's control that discovery really failed.
        _log.LogInformation("{Message}", GameFolderLog + (found ?? GameFolderNotFound));

        if (_settingsOwner.ShouldAskForGameFolder(found, headless))
        {
            ChooseGameFolder(atStartup: true);
        }
    }

    /// <summary>File &gt; TF2 folder: picks the <c>tf</c> folder, validates it, and saves it to the cfg (D210).</summary>
    private void ChooseGameFolder() => ChooseGameFolder(atStartup: false);

    /// <summary>Picks the <c>tf</c> folder, validates it, and saves it to the cfg (D210).</summary>
    /// <param name="atStartup">
    /// Whether this is the startup prompt for a missing install. Cancelling THAT stops it asking
    /// (<c>cl_game_folder_ask 0</c>): watching a demo without TF2 is the salvage case, not an error,
    /// and a dialog on every launch would be nagging. The menu entry still works.
    /// </param>
    /// <remarks>
    /// **Detection reads the live <c>cl_game_folder</c>** (<c>SteamInstall.ConfiguredFolder</c>), so the
    /// choice applies to this session even when the save fails; the status says it was not saved. Archives already opened stay open, so after a demo has loaded the new
    /// folder takes effect on the next start.
    /// </remarks>
    private void ChooseGameFolder(bool atStartup)
    {
        using FolderBrowserDialog dialog = new()
        {
            Description = atStartup
                ? "Team Fortress 2 was not found. Choose its tf folder, or Cancel to watch without maps and models."
                : "Choose the Team Fortress 2 tf folder.",
            UseDescriptionForTitle = true,
            InitialDirectory = _settingsOwner.GameFolder ?? string.Empty,
        };

        if (ShowModal(dialog) != DialogResult.OK)
        {
            if (atStartup)
            {
                _status.Text = _settingsOwner.DeclineGameFolder();
            }

            return;
        }

        // Whether the folder is a TF2 folder, remembering it and the words for both outcomes are the settings owner's
        // (`SettingsPresenter.ChooseGameFolder`), which logs them; this window shows them.
        bool installOpen = _presenter.Game is not null;
        GameFolderChoice choice = _settingsOwner.ChooseGameFolder(dialog.SelectedPath, installOpen);

        _status.Text = choice.Status;

        if (!choice.Accepted)
        {
            return;
        }

        // Nothing has opened the archives yet, so the player's own config can be read from the new folder now.
        // The menu was built from the bindings before this read, so its shortcuts are redone from the new ones.
        if (!installOpen)
        {
            LoadUserConfig();
            _menu.ApplyBindings(_bindings);
        }
    }

    /// <summary>
    /// How many times the shutdown block has run. One, for the lifetime of a form.
    /// </summary>
    /// <remarks>
    /// **WinForms disposes a shown form twice**: `Form.Close` disposes a top-level form, and
    /// `Application.Run` disposes it again when the message loop ends. Without a guard the whole
    /// shutdown block ran twice, and the second pass overwrote the first pass's timings with
    /// zeroes — every viewer log ends with `device released after 4 ms` followed immediately by
    /// `0 ms`, which reads as a fast exit however slow the real one was.
    ///
    /// **A count rather than a flag, because the flag cannot be tested.** After the fix a boolean
    /// reads true whether shutdown ran once or twice, so it is blind to the exact defect the
    /// guard exists to prevent. The count distinguishes them, and it does so per instance — the
    /// first version of this test counted lines in the process-wide viewer log, which every other
    /// fixture in the assembly writes to in parallel, and it failed one run in four for that
    /// reason rather than for anything about the code.
    /// </remarks>
    internal int ShutdownRuns { get; private set; }

    /// <summary>Names the member about to be released, in the field and in the log (B402).</summary>
    /// <param name="member">What is about to be disposed.</param>
    /// <remarks>
    /// **Before, not after.** A process that dies inside a disposal never reaches a line written
    /// afterwards, and never reaches the `catch` either when the failure is not a managed
    /// exception — which is what the run with only a marker and a handler demonstrated.
    /// </remarks>
    private void Releasing(string member)
    {
        _releasing = member;

        _renderLog.LogInformation("{Message}", $"shutdown: releasing {member}");
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        // Read BEFORE the counter moves, because the lines around `base.Dispose` below belong to
        // the same first shutdown as the block that increments it (B402).
        bool first = disposing && ShutdownRuns == 0;

        if (disposing && ShutdownRuns == 0)
        {
            ShutdownRuns++;

            // **First, before anything it might touch is released** (B402). A map fetch in flight
            // resumes on this thread to set the status and build geometry; cancelling here is what
            // makes its continuation return instead of writing into a disposed form.
            _shutdown.Cancel();

            // The corpse pass is linked to `_shutdown` and has just been told to stop; its source is ours to release (D181).
            _presenter.ReleaseCorpseRecord();

            // **Timed, because a slow exit is a defect nobody can diagnose from the outside.**
            // Two hundred textures, a lightmap atlas and a swap chain go here, and which of them
            // is slow is not guessable - the log says.
            Stopwatch closing = Stopwatch.StartNew();

            // **The viewport's own handlers go the same way, and missing them was B402.**
            // Disposing a control destroys its window and WinForms raises `Resize` on the way
            // down, straight into `OnViewportResize` — which runs two statements BEFORE its
            // `_device is null` guard: `_overlay?.PositionOver(_viewport)` and
            // `_world.Invalidate()`. An exception from either is thrown inside the window
            // procedure, and user32 turns that into `STATUS_FATAL_USER_CALLBACK_EXCEPTION`: the
            // process dies immediately after a clean shutdown line, reaching no managed handler.
            // Five CI runs reported exactly that, and the marker above named this call.
            //
            // **Which of the two throws is NOT established, and the overlay is ruled out for the
            // capture path**: it exists only in full screen (`_overlay = new OverlayWindow` sits
            // inside the full-screen branch) and `--shot` never goes there, so on that path the
            // reachable statement is the world invalidate against released device resources.
            // Unhooking removes the whole question rather than guarding one line of it.
            //
            // It is the same reasoning the Idle handler below already carries — "a handler that
            // outlives the swap chain presents into freed memory" — applied to the control's own
            // events rather than to the application's.
            _viewport.Resize -= OnViewportResize;
            _viewport.MouseWheel -= OnViewportWheel;
            _viewport.MouseDown -= OnViewportMouseDown;

            if (_rendering)
            {
                // Before the device goes: an Idle handler that outlives the swap chain presents
                // into freed memory, and that is a crash on exit rather than a leak.
                Application.Idle -= OnIdle;
                _rendering = false;
            }

            if (_keyReleases is not null)
            {
                // A filter left registered outlives the form and keeps a reference to it, which is
                // the ordinary way a WinForms window fails to be collected.
                Application.RemoveMessageFilter(_keyReleases);
                _keyReleases = null;
            }

            TimeSpan idleStopped = closing.Elapsed;

            // Dropped before the device is disposed, so nothing can hand geometry to a dead one.
            _moment.Upload = null;

            _device?.Dispose();
            _device = null;

            // Before the log line below, so a device that hangs on close is visible as a shutdown
            // that stalled here rather than as one that never reached the end.
            _presenter.Dispose();

            // Stryker disable once all : a mutated interpolated string cannot bind to its handler (CS1620) and Safe Mode drops the method - B410
            _renderLog.LogInformation(
                "{Message}",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"shutdown: idle stopped after {idleStopped.TotalMilliseconds:F0} ms, " +
                    $"device released after {closing.Elapsed.TotalMilliseconds:F0} ms"));

            // **Named one at a time, because the process has been dying somewhere in here and no
            // instrument could say where** (B402). `Dispose` runs inside the window procedure that
            // handles the close, so an exception escaping it propagates into user32 and comes back
            // as `STATUS_FATAL_USER_CALLBACK_EXCEPTION` — `Application.ThreadException` and
            // `AppDomain.UnhandledException` both stay silent, which is exactly what the CI run
            // with those handlers showed. The log ends on the line above and the exit code is
            // `0xC000041D`, so the fault is one of these calls and nothing said which.
            //
            // **The catch logs and continues to the next member.** A window being torn down cannot
            // be made healthy by abandoning the teardown half-way, and leaking the rest to spite
            // one failure would trade a named crash for an unnamed leak.
            // **Direct calls with a marker, rather than a helper taking a delegate.** The first
            // version passed `_viewport.Dispose` and friends to a `Release(string, Action)`, and
            // CA2213 and S2930 then reported six fields as never disposed — an analyzer follows a
            // call, not a delegate. The marker costs one assignment per member and keeps the
            // disposal visible to both the reader and the tools.
            // **A field rather than a local, and the analyzer is the reason.** As a local, every
            // assignment but the last is a dead store to Sonar's flow analysis — S1854 on each —
            // because it does not credit the `catch` with reading it. The field is written on the
            // same path and read in the handler, and says nothing false in between.
            // **Each member is announced BEFORE it is released, and the marker alone was not
            // enough** (B402). The run with only the marker and the `catch` logged neither a throw
            // nor the completion line, which rules out a managed exception: the process is dying
            // inside one of these calls without unwinding, so nothing downstream of it ever runs.
            // A line before each disposal makes the LAST line in the log name the member that
            // killed it, which no after-the-fact handler can do.
            //
            // **Ten lines on a shutdown that happens once per run**, and they come out the moment
            // this is understood. Cheap against a defect that has now cost five CI runs.
            // **The overlay before the viewport it TRACKS.** Not the B402 fix — it is null on the
            // capture path, so it cannot be that crash — but it positions itself over that control
            // and outliving it by even one message is the same hazard one object along. Ordered
            // deliberately rather than left to the sequence it happened to have.
            Releasing("overlay");

            try
            {
                _overlay?.Dispose();

                // **No child control is disposed here.** This was announced as the B402 fix and the
                // next CI run refuted it — the log reached `every member released` and the process
                // still died. The removal stays because it was right for its own reason: every one
                // of them is in `Controls`, which `base.Dispose` walks at the end of this method, so
                // these calls were redundant from the start — they existed to satisfy CA2213, and
                // the comment that used to sit here said as much while making them anyway.
                //
                // **It was measured rather than reasoned, in two steps.** Six CI runs died on the
                // first disposal, `shutdown: releasing viewport`. Skipping only the viewport —
                // one variable, so the answer could not be ambiguous — moved the crash FORWARD,
                // to `releasing transport` on one runner and `releasing actions` on another. Two
                // different members means no single control is at fault: disposing them early,
                // from our own override while the form is still alive, is what breaks. Had all
                // six been dropped at once the result would have looked identical to the viewport
                // being guilty.
                //
                // The analyzer is answered per field rather than obeyed: CA2213 asks whether each
                // is disposed, and each is, by a base it cannot follow.
                Releasing("maps");
                _maps.Dispose();
                Releasing("shutdown");
                _shutdown.Dispose();

                // Thirteen menu items were named here one at a time until 2026-08-26. `ViewerMenu`
                // owns them and disposes them beside the code that built them (B188, D90).
                Releasing("menu");
                _menu.Dispose();

                // VGUI's GDI fonts, DCs and bitmaps: after the device, which no longer draws them.
                Releasing("vgui gdi");
                _gdi.Dispose();

                _renderLog.LogInformation("{Message}", "shutdown: every member released");
            }
            // **Rethrown with the member in the message, not swallowed** (B402, S2139). Swallowing
            // would turn the red CI green while the fault stayed, which is deleting the only
            // instrument that has ever reported this; the log line names the member and the
            // rethrow keeps the failure a failure.
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                _renderLog.LogError(failure, "{Message}", $"shutdown: releasing {_releasing} threw");

                throw new InvalidOperationException(
                    $"releasing {_releasing} during shutdown threw", failure);
            }
        }

        // **The fourth attempt's fix was refuted by the run after it, and this line is why the
        // fifth is an instrument rather than another story** (B402). With no child control
        // disposed by us, the log reached `shutdown: every member released` and the process still
        // died — so the fatal call is inside `base.Dispose`, which destroys each child's window,
        // or later still. Nothing had ever distinguished those two, because our own override was
        // the last thing that wrote anything down.
        if (first)
        {
            Releasing("the base, which destroys every child window");
        }

        base.Dispose(disposing);

        if (first)
        {
            _renderLog.LogInformation("{Message}", "shutdown: the base returned");
        }
    }
}
