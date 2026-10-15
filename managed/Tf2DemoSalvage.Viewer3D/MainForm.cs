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
using Tf2DemoSalvage.Logging;

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
internal class MainForm : Form, IViewerWindow, IIdleHost
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
    public const string GameFolderLog = ViewerSession.GameFolderLog;

    /// <summary>What <see cref="GameFolderLog"/> says when discovery found nothing.</summary>
    public const string GameFolderNotFound = ViewerSession.GameFolderNotFound;

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

    /// <summary>The View menu's Volume submenu.</summary>
    public const string VolumeMenuId = "VolumeMenu";

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

    /// <summary>The viewer without its window: every decision this form used to make that was not about a control (D214, slice 9).</summary>
    [SuppressMessage(
        "Usage",
        "CA2213:Disposable fields should be disposed",
        Justification = "Released by Dispose in three announced steps with this window's own teardown between them (B402); its Dispose is those steps.")]
    private readonly ViewerSession _session;

    /// <summary>The session, for the composition root and the tests that drive it.</summary>
    internal ViewerSession Session => _session;

    /// <summary>The composite presenter, for the tests that ask it what a load left (the demo, the map).</summary>
    internal ViewerPresenter Presenter => _session.Presenter;

    // **`_scene` was here until 2026-08-26** (D98) — entity positions already projected to clip
    // space, held that way so the render loop did no per-frame work beyond handing them over. That
    // caching was the right shape for a projection that only changed on a scrub or a resize, and it
    // is exactly the shape that stopped being possible once the camera moved every frame.

    // `_mapFill` was here: the map's surfaces projected to clip space for the overhead fallback.
    // Dead by construction — drawn only when there was no textured map, and built from the map — so
    // it could never produce a visible triangle. Deleted with the projection that built it.

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
    private readonly ILogger _renderLog;

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

    // **`_clock` was here until 2026-08-26.** It held the `PlaybackClock` that turns real time into
    // demo ticks — the SAME object `PlaybackPresenter` already had, because `DemoSystems.Open`
    // handed it to the presenter and returned it to this window as well.
    //
    // Two references to one piece of state is how two answers to one question appear. The presenter
    // owns the clock (D62), so it now answers `Position` and takes `Seek`, and this window asks it
    // rather than keeping a copy.

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

    // **`DegreesPerPixel` was here until 2026-08-26** (B206). It is
    // `FreeCameraController.DegreesPerPixel`, along with the drag arithmetic it belonged to — which
    // this file had one copy of and the unused `FreeLookState` had another. How far a drag turns a
    // camera is not a fact about a window.

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

    // The "have we already reported this viewmodel" state moved into ViewmodelScene on 2026-08-24
    // (B188). It belongs with the decision it dedupes: what the lines answer is "which model,
    // playing what", which is a question about a WEAPON and changes when the player switches — and
    // holding that flag on the form is what let it drift out of step with the thing it described.

    // `_specular`, `_fullbrightMenu`, `_drawWorld`, `_drawEntities` and `_debugMenu` were here
    // until 2026-08-26. They are properties of `ViewerMenu` (B188, D90).

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

    // `_borderlessMode`, `_exclusiveMode` and `_textureQualityItems` were here until 2026-08-26.
    // They are `ViewerMenu.Borderless`, `.Exclusive` and `.TextureQualityItems` (B188, D90).

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
        _renderLog = loggers.CreateLogger("render");

        _transport = new TransportBar();

        // **Playback belongs to a presenter now, not to this form (D62).** What used to be three
        // event handlers and a stopwatch here — scrub, play/pause, speed, each with a rule about
        // when to restart the watch — is `PlaybackPresenter`, which knows nothing about WinForms
        // and has sixteen tests. None of that logic could be tested while it lived in this file.
        //
        // The form keeps exactly one job in this area: the moment changed, so redraw.
        //
        // It is the session's now, with everything else this constructor composed (D214, slice 9): the settings, the
        // presenters, the launch options, the opening sequence and the player's own controls, in the order they were
        // built here.
        _session = ViewerSession.Open(this, _transport, loggers, initialPaths, _gdi);

        // **What a load fills is composed by its owner** (D214): the model set, the sound cache, soundscape and
        // presenter, the moment scene and presenter, the frame presenter, the level's systems and the demo's — in the
        // order this constructor built them. The fields below are the same objects, kept because the frame still
        // asks them; nothing here constructs or replaces one.

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

        // **`--look` and `--zoom` were parsed here and read by nobody** (B226). D98 removed the
        // orthographic camera they were written for and kept the fields with a note saying what
        // they should mean for a world-placed camera was "a question for whoever reimplements
        // them". An accepted-and-dropped option is the same defect as B223, and the answer needed
        // no invention: the overhead placement is the map view's successor, so they reshape what it
        // frames.

        // **This line was MISSING for a day and `--shot` did nothing at all** (B196). `_shotPath`
        // stayed out of the record because taking the shot CONSUMES it, and staying out of the
        // record is exactly how it stopped being assigned: the six `_shot*` fields the parser used
        // to write into went away together, and the one that had to survive them was not wired back
        // up. `TakeAutomaticShot` then read a permanently-null field and returned every frame.
        //
        // Nothing failed. No test passes `--shot`, so the whole option was covered by nobody.
        initialPaths = [.. _session.Paths];

        // **The player's own TF2 controls, loaded before anything can be pressed (D69/D70).** This
        // is what makes the console a feature rather than a capability: without it the interpreter
        // runs the shipped defaults for ever and every test of it exercises code the viewer never
        // reaches. Done in the constructor because the alternative — loading with the map, where the
        // archives are already opened — would leave the keys wrong until a demo was opened.

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
            (_, _) => ExportDemo()));
        _actions.Controls.Add(ActionButton(
            CompileButtonId, "Compile", "Rebuild a byte-identical demo from its assembly text.",
            (_, _) => CompileDemo()));

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
        _playlist.ItemActivate += (_, _) => _session.Activate(SelectedPath());

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
                ExportDemo: () => BeginInvoke(ExportDemo),
                CompileDemo: () => BeginInvoke(CompileDemo),
                Exit: Close,
                SetFullScreen: SetFullScreen,
                SetFullScreenExclusive: _session.SetFullScreenExclusive,
                SetTextureQuality: _session.SetTextureQuality,
                SetSurfaceColours: _session.SetSurfaceColours,
                SetFrameRateMeter: _session.SetFrameRateMeter,
                SetPositionReadout: _session.SetPositionReadout,
                SetWireframe: _session.SetWireframe,
                SetFullbright: _session.SetFullbright,
                SetDrawWorld: _session.SetDrawWorld,
                SetDrawEntities: _session.SetDrawEntities,
                SetDebugMode: _session.SetDebugMode,
                SetSpecular: _session.SetSpecular,
                SetPhong: _session.SetPhong,
                Screenshot: _session.CaptureViewportToFile,
                SetChosenHud: _session.SetChosenHud,
                // Posted for the same reason as Export: the menu closes before the dialog opens.
                ChooseGameFolder: () => BeginInvoke(ChooseGameFolder),
                SetVolume: _session.SetVolume,
                SetMuteLoseFocus: _session.SetMuteLoseFocus),

            // **Our OWN custom/ folder, never `tf/custom`** (D193) — the picker lists what a player
            // imported into the program, not what is in their TF2 install.
            () => _session.Menu,

            // **The menu's shortcuts come from the same table the flight keys do** (B214, D101).
            // Fourteen `ShortcutKeys = Keys.<something>` literals lived in `ViewerMenu` until now,
            // six of them on keys TF2 binds to something else.
            _session.Bindings);

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

        // **The command line goes through the same code as the Open buttons**: the paths are listed here, and what
        // one path, a missing path or several mean - open it once the window is up, or end a headless run - is the
        // session's (`ViewerSession.PlanStartup`), registered after the prompt above so it runs after it.
        if (initialPaths.Length > 0)
        {
            AddToLibrary(initialPaths);
            _session.PlanStartup();
            Shown += (_, _) => _session.WindowShown();
        }
    }

    /// <summary>Environment variable pinning the window size, as WIDTHxHEIGHT.</summary>
    public const string WindowSizeVariable = "TF2VIEW_WINDOW_SIZE";

    /// <summary>Environment variable pinning the window position, as X,Y.</summary>
    public const string WindowPositionVariable = "TF2VIEW_WINDOW_POS";

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
    public DemoLoadResult LoadDemo(string path) => _session.Presenter.LoadDemo(path);

    /// <summary>Opens a demo without freezing the window: <see cref="ViewerPresenter.LoadDemoAsync"/>.</summary>
    /// <param name="path">Full path to the demo.</param>
    /// <returns>A task that completes when the demo is on screen.</returns>
    public Task<DemoLoadResult> LoadDemoAsync(string path) => _session.Presenter.LoadDemoAsync(path);

    /// <summary>Loads the demo selected in the playlist, if any.</summary>
    /// <remarks>
    /// **A failure to load is reported, never thrown.** Half the point of this project is opening
    /// demos other software rejects, so a file that will not parse is an expected outcome and has
    /// to leave the application usable - the user picks another one from the same playlist.
    /// </remarks>
    public Task<DemoLoadResult> LoadSelected() => _session.LoadSelected(SelectedPath());

    /// <summary>The path of the first selected playlist row, or null when nothing is selected.</summary>
    private string? SelectedPath() =>
        _playlist.SelectedIndices.Count == 0 ? null : _session.PlaylistRow(_playlist.SelectedIndices[0])?.Path;

    /// <summary>The load in flight, kept by the session so it is observed rather than discarded.</summary>
    public Task<DemoLoadResult> Loading => _session.Loading;

    // `_modelsUploaded` was here until 2026-08-25. It is `MomentScene.Uploaded` now, beside the
    // packing it guards (B188, D90) — and the question it answers is unchanged: "is the packed
    // geometry on the device right now", which is NOT "has the set grown". Conflating them was B148.

    // `MapLines` was here: the map outline projected into clip space for the overhead view. It had
    // no consumer in production or in a test, and the view it served was removed. Deleted with the
    // projection that built it (B98's ghost); `MapOutline` still computes the play-area BOUNDS,
    // which is a different object and is still read.

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

    // `_shotDelay` was here until 2026-08-26. It is `OpeningSequence`'s countdown (B188, D90).

    // `ReadCaptureOptions` is `LaunchOptionsReader.Read` in Presentation (B188, D90), and it returns
    // a record rather than writing into six fields as it goes. Thirteen tests came with the move; it
    // had none, because reaching it meant constructing a form — so every option was covered only by
    // whichever UI test happened to pass one.

    // **`_openingDone` was here until 2026-08-26.** It is `OpeningSequence.Applied` (B188, D90),
    // set by this window because applying can fail — with no demo open there is nothing to seek to.
    //
    // The reason it is latched rather than inferred from the countdown moved with it, and is worth
    // keeping: the countdown keeps running after it reaches zero, and re-applying the seek every
    // frame would pin the transport to one tick — a viewer that cannot be scrubbed, which is the
    // opposite of the point.

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
    public T OnUi<T>(Func<T> work) =>
        IsHandleCreated && InvokeRequired ? Invoke(work) : work();

    // **What the session asks of this window** (`IViewerWindow`, D214): each member below is one control read or one
    // control write. What any of them is FOR is the session's.

    /// <inheritdoc/>
    string? IViewerWindow.Status
    {
        get => _status.Text;
        set => _status.Text = value;
    }

    /// <inheritdoc/>
    void IViewerWindow.ReportLoading(string stage, double? fraction) => _loading.Report(stage, fraction);

    /// <inheritdoc/>
    void IViewerWindow.HideLoading() => _loading.Visible = false;

    /// <inheritdoc/>
    (int Width, int Height) IViewerWindow.Viewport => (_viewport.ClientSize.Width, _viewport.ClientSize.Height);

    /// <inheritdoc/>
    bool IViewerWindow.HasFocus => ContainsFocus;

    /// <inheritdoc/>
    bool IViewerWindow.IsActiveApp => ForegroundProbe.IsActiveApp();

    /// <inheritdoc/>
    void IViewerWindow.Redraw() => _viewport.Invalidate();

    /// <inheritdoc/>
    void IViewerWindow.Close(bool posted)
    {
        if (posted)
        {
            BeginInvoke(Close);
        }
        else
        {
            Close();
        }
    }

    /// <inheritdoc/>
    void IViewerWindow.LeaveFullScreen() => SetFullScreen(false);

    /// <inheritdoc/>
    void IViewerWindow.ShowSurfaceColours(bool ticked) => _menu.SurfaceColours.Checked = ticked;

    /// <inheritdoc/>
    void IViewerWindow.ShowSpecular(bool ticked) => _menu.Specular.Checked = ticked;

    /// <inheritdoc/>
    void IViewerWindow.ShowFullbright(int mode)
    {
        foreach (ToolStripItem entry in _menu.FullbrightMenu.DropDownItems)
        {
            if (entry is ToolStripMenuItem item)
            {
                item.Checked = item.Name == FullbrightItemId + (Fullbright)mode;
            }
        }
    }

    /// <inheritdoc/>
    void IViewerWindow.ShowFullScreenExclusive(bool exclusive)
    {
        _menu.Borderless.Checked = !exclusive;
        _menu.Exclusive.Checked = exclusive;
    }

    /// <inheritdoc/>
    void IViewerWindow.ShowTextureQuality(string quality)
    {
        foreach (KeyValuePair<string, ToolStripMenuItem> item in _menu.TextureQualityItems)
        {
            item.Value.Checked = item.Key == quality;
        }
    }

    /// <inheritdoc/>
    void IViewerWindow.ShowBindings(KeyBindings bindings) => _menu.ApplyBindings(bindings);

    // **What a load asks of this window** (`ILoadView`, D214). The load itself — the decode, the map read behind the
    // render barrier, the demo becoming the open one — is `ViewerPresenter`'s; each member below is the part of it that
    // is a window's, called at the moment the load reached for it when it lived here.

    /// <summary>The playback controls, exposed for the tests that address them.</summary>
    public TransportBar Transport => _transport;

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
        _session.FullScreenChanging();

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
            _session.EnteredFullScreen();

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
        _session.LeavingFullScreen();

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
    public bool HasDevice => _session.HasDevice;

    /// <summary>Creates the swap chain once the panel has a real window handle.</summary>
    private void OnViewportHandleCreated(object? sender, EventArgs e)
    {
        // Creating the device, handing it its settings, saying so and projecting a map already read are the
        // session's (`ViewerSession.CreateDevice`), which answers false when the machine has no device to give: the
        // rest of the application - opening files, reading a trace - still works without one.
        if (!_session.CreateDevice(_viewport.Handle, _viewport.ClientSize.Width, _viewport.ClientSize.Height))
        {
            return;
        }

        // Idle-driven rather than a timer: WinForms raises Idle whenever the message queue
        // empties, so the viewport redraws as fast as the UI allows and stops entirely while
        // the user is dragging a menu around. A timer would keep presenting underneath it.
        Application.Idle += OnIdle;

        // Registered beside the idle loop because they are the two halves of the same thing:
        // the filter records what is held, the loop moves the camera by it.
        _keyReleases = new KeyReleaseFilter(_session);
        Application.AddMessageFilter(_keyReleases);

        _rendering = true;
    }

    /// <summary>Windows has nothing else for this thread: the session draws frames until it has (`ViewerSession.Idle`).</summary>
    private void OnIdle(object? sender, EventArgs e) => _session.Idle(this);

    /// <inheritdoc/>
    bool IIdleHost.ArrivesWithin(double seconds) => MessageQueue.ArrivesWithin(seconds);

    /// <inheritdoc/>
    uint? IIdleHost.Waiting() => MessageQueue.Waiting();

    /// <inheritdoc/>
    void IIdleHost.Sleep(int milliseconds) => Thread.Sleep(milliseconds);

    /// <inheritdoc/>
    void IIdleHost.Yield() => Thread.Yield();

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

    // **`AdvancePlayback` was here until 2026-08-26** (B188, B203, D90). It is now `Simulate`, the
    // first stage of `FrameSequence`, and its note is worth keeping: **nothing is invalidated
    // there.** The idle loop it runs inside already draws every frame, and asking for a repaint as
    // well is what made the mouse sluggish over the transport buttons — paint messages queued
    // faster than the pump could drain them.

    // `_idleBursts` was here until 2026-08-25. It is `FrameLedger.Yielded()` — one of six
    // per-second counters this window kept and reset by hand (B188, D90). What stays is
    // `_idleEndedBy` above, because naming a Windows message is the only part of that report a
    // second frontend could not produce.

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
        _session.ReleaseHeldKeys();

        base.OnDeactivate(e);
    }

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
    private sealed class KeyReleaseFilter(ViewerSession session) : IMessageFilter
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
                session.KeyUp(KeyNames.NameOf((Keys)(int)m.WParam));
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

    // `_framesDrawn` was here until 2026-08-25. It is the count `FrameLedger.Drew` keeps — the last
    // of the six per-second counters this window reset by hand.
    //
    // **`_rateReportedAt`, `_collections`, `GarbageThisSecond` and `CountFrame` followed on
    // 2026-08-26** (B188, D90). The clock is `FrameReporter`'s `IElapsedTime`, which is what lets a
    // second pass in a test without one passing; the collection deltas, the quiet-second threshold
    // and the format string are `GarbageCounter`. Neither had a test, because reaching either meant
    // constructing a form.

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
        _session.Wheel(e.Delta > 0);

        // **The zoom-at-cursor branch was here until 2026-08-26** (D98). It magnified the
        // orthographic map and recentred so the world point under the pointer stayed put — correct
        // for a projection that no longer exists.
        //
        // **The wheel is not lost, and never was**: in the free camera it flies, which is the branch
        // above and the one that was always used. The owner: *"the mouse wheel was working in free
        // cam before, i used it all the time"*. What is gone is its SECOND meaning, not the wheel.
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

        _session.PointerDown(KeyNames.NameOf(e.Button), e.Button == MouseButtons.Left, e.Location.X, e.Location.Y);
    }

    private void OnViewportMouseMove(object? sender, MouseEventArgs e) => _session.PointerMoved(e.Location.X, e.Location.Y);

    private void OnViewportMouseUp(object? sender, MouseEventArgs e) => _session.PointerUp(KeyNames.NameOf(e.Button));

    // **`WorldAt` was here until 2026-08-26** (D98). It answered "which world point is under this
    // pixel", which only has an answer for a projection that maps the screen onto a plane. A
    // perspective view of a 3D world does not: a pixel is a ray, not a point, and answering it
    // properly is a trace rather than an unprojection.

    private void OnViewportResize(object? sender, EventArgs e)
    {
        _overlay?.PositionOver(_viewport);
        _session.ViewportResized();
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

    /// <inheritdoc />
    /// <remarks>
    /// **What a key does is the session's** (`ViewerSession.KeyPressed`): which bound action it is, whether the focused
    /// control keeps it, and the one literal, Escape out of full screen. This translates: the key's Source name, whether
    /// Control or Alt is held, what kind of control has the keyboard, and how a binding's key string compares with this
    /// press - the four things only a toolkit can say.
    /// </remarks>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        Keys pressed = keyData;

        return _session.KeyPressed(
                new KeyPress(
                    KeyNames.NameOf(keyData & Keys.KeyCode),
                    (keyData & (Keys.Control | Keys.Alt)) != Keys.None,
                    FocusKind(),
                    binding => pressed == KeyNames.Resolve(binding),
                    keyData == Keys.Escape,
                    $"{keyData} sent to {FromHandle(msg.HWnd)?.Name ?? "no control"}"))
            || base.ProcessCmdKey(ref msg, keyData);
    }

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
        // Opening, ordering, filtering, the count's words and when the list is shown are the session's.
        _session.AddToLibrary(_search.Text, paths);
    }

    /// <summary>Rebuilds the playlist from the library and the search box.</summary>
    /// <remarks>
    /// Nothing is constructed here. Setting <see cref="ListView.VirtualListSize"/> tells the
    /// control how many rows exist, and it then asks for the handful it needs to paint - which is
    /// what keeps a keystroke cheap on an archive of thousands.
    /// </remarks>
    private void RefreshPlaylist()
    {
        _session.FilterLibrary(_search.Text);
    }

    /// <summary>Tells the list control how many rows there are now.</summary>
    void IViewerWindow.ShowPlaylist(int rows)
    {
        // Selection indices refer to the OLD list. Clearing first avoids the control asking for a
        // row that the new, shorter list does not have.
        _playlist.SelectedIndices.Clear();
        _playlist.VirtualListSize = rows;
        _playlist.Invalidate();
    }

    /// <summary>Supplies one row of the playlist on demand.</summary>
    private void OnRetrieveVirtualItem(object? sender, RetrieveVirtualItemEventArgs e)
    {
        // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
        if (_session.PlaylistRow(e.ItemIndex) is not { } entry)
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
    public Task<string> FileWork => _session.FileWork;

    /// <summary>Writes the open demo's assembly text — the CLI's <c>--asm</c> — to a file the user picks.</summary>
    private void ExportDemo()
    {
        // Whether there is a demo to export, the name to offer and the work itself are the session's; the dialog is here.
        if (_session.OfferExport() is not { } suggested)
        {
            return;
        }

        using SaveFileDialog dialog = new()
        {
            Filter = "Demo assembly (*.txt)|*.txt|All files (*.*)|*.*",
            Title = "Export demo assembly",
            FileName = suggested,
        };

        if (ShowModal(dialog) == DialogResult.OK)
        {
            _session.Export(dialog.FileName);
        }
    }

    /// <summary>Compiles an assembly text file back into a byte-identical demo — the CLI's <c>--compile</c>.</summary>
    private void CompileDemo()
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
                return;
            }

            source = open.FileName;
        }

        using SaveFileDialog save = new()
        {
            Filter = "Source demos (*.dem)|*.dem|All files (*.*)|*.*",
            Title = "Save compiled demo",
            FileName = ViewerSession.OfferCompile(source),
        };

        if (ShowModal(save) == DialogResult.OK)
        {
            _session.Compile(source, save.FileName);
        }
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
        // What discovery found, the line that says so and whether to ask are the session's; the dialog is here.
        if (_session.ShouldAskForGameFolder())
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
            InitialDirectory = _session.Settings.GameFolder ?? string.Empty,
        };

        if (ShowModal(dialog) != DialogResult.OK)
        {
            if (atStartup)
            {
                _session.GameFolderDeclined();
            }

            return;
        }

        // Whether it is a TF2 folder, remembering it, the words for both outcomes and reading its configs are the
        // session's (`ViewerSession.GameFolderChosen`); this window shows what it says.
        _session.GameFolderChosen(dialog.SelectedPath);
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
            _session.StopBackgroundWork();

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

            // The device, then what drew through it: before the log line below, so a device that hangs on close is
            // visible as a shutdown that stalled here rather than as one that never reached the end.
            _session.ReleaseDevice();

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
                _session.ReleaseServices(Releasing);

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
