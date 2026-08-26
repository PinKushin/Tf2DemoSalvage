using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
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
using Tf2DemoSalvage.Core.Scene;
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
internal class MainForm : Form, IFrameSteps
{
    /// <summary>Automation id of the Direct3D viewport panel.</summary>
    public const string ViewportId = "Viewport";

    /// <summary>Automation id of the status line.</summary>
    public const string StatusId = "StatusLabel";

    /// <summary>Automation id of the File menu.</summary>
    public const string FileMenuId = "FileMenu";

    /// <summary>Automation id of the File &gt; Open item.</summary>
    public const string OpenDemoItemId = "OpenDemoMenuItem";

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

    /// <summary>Automation id of the reflections toggle — Valve's <c>mat_specular</c>.</summary>
    public const string SpecularItemId = "SpecularMenuItem";

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

    private readonly Panel _viewport;
    private readonly ToolStripStatusLabel _status;
    private readonly FlowLayoutPanel _actions;
    private readonly TransportBar _transport;
    private readonly ListView _playlist;
    private readonly TextBox _search;

    /// <summary>The library sorted for display: folder first, then name.</summary>
    private IReadOnlyList<DemoEntry> _ordered = [];

    /// <summary>The rows the playlist is currently showing.</summary>
    private IReadOnlyList<DemoEntry> _shown = [];
    private readonly DemoLibrary _library = new();

    private LoadedDemo? _demo;

    // **`_scene` was here until 2026-08-26** (D98) — entity positions already projected to clip
    // space, held that way so the render loop did no per-frame work beyond handing them over. That
    // caching was the right shape for a projection that only changed on a scrub or a resize, and it
    // is exactly the shape that stopped being possible once the camera moved every frame.

    // `_mapFill` was here: the map's surfaces projected to clip space for the overhead fallback.
    // Dead by construction — drawn only when there was no textured map, and built from the map — so
    // it could never produce a visible triangle. Deleted with the projection that built it.

    /// <summary>The map, read and ready to draw, or null when none is open.</summary>
    /// <remarks>
    /// **Eleven fields until 2026-08-25** — the outline, the assets, the height range and the eight
    /// that were `MapLevel` unpacked. Reading a map is not window work and none of it happens here
    /// any more: what is left is handing the result to the systems that asked for it, which is the
    /// engine's `LevelInitPreEntity` shape (`igamesystem.h:39`).
    /// </remarks>
    private LoadedMap? _loaded;

    /// <summary>Where maps come from: the disk first, then the network.</summary>
    /// <remarks>
    /// **Replaced a `MapDownloader?` created on first need** (B188, D90). The lazy construction is
    /// still there and is still right — a viewer that never meets a missing map should never open an
    /// `HttpClient` — but it belongs to the thing that owns the download, not to the window that
    /// happens to notice a map is absent.
    /// </remarks>
    private readonly MapProvider _maps = MapProvider.Installed();

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

    private readonly ILogger _mapLog;

    private readonly ILogger _renderLog;

    private readonly ILogger _demoLog;

    private readonly ILogger _audioLog;

    private readonly ILogger _spectateLog;

    private readonly ILogger _configLog;

    // What light the map casts is `LevelLighting`, the engine's `ComputeLighting` behind an
    // interface (`cdll_int.h:392`). It stopped being a field here on 2026-08-25: the scene owns the
    // one the models ask, and the asset loader takes it as a local while the map is being read. The
    // ambient samples, the world lights and the sun were three fields before that (B188, D90).

    // `_heightRange` is `_loaded.HeightRange`, recorded during the world build rather than after it
    // — a camera projects height on the very first frame, and taking it afterwards leaves one frame
    // drawn with a pass-through depth.

    /// <summary>The decoded demo, for the things a window still asks it.</summary>
    /// <remarks>
    /// **Still here, and worth saying why rather than leaving it implied.** The sampling left on
    /// 2026-08-26 (B188, D90) and took the two buffers with it, but four callers in this file still
    /// need the timeline itself — <c>EnsureWeaponRoles</c>, the model precache, the sound precache
    /// and <c>DemoSystems.Open</c>. Each of those hands it to something in Presentation or Scene; the
    /// window holds the reference and asks it nothing.
    /// </remarks>
    private DemoTimeline? _timeline;

    // **`_players` and `_props` were here until 2026-08-26** (B188, D90). They existed only because
    // `ShowMoment` sampled the timeline in the window; `MomentPresenter` owns them now, and reuses
    // them for the same reason — a moment is rebuilt every frame while playing, so fresh lists would
    // be two allocations a frame.

    /// <summary>Entity models, packed once in model space and posed by the GPU.</summary>
    // Constructed in the constructor rather than inline, so it gets the form's loggers (D83).
    private readonly EntityModelSet _models;

    // `_weapons` was here until the load set stopped asking the window for it. The form held a
    // second reference to `GameContent.Weapons` so `DemoModelPaths` could reach it — and keeping the
    // two in step was the reason both were assigned in one block. `DemoModels` reads it off the
    // install itself now, so there is one holder and nothing to keep in step.

    /// <summary>Whose eyes the first-person view is using, and where they are.</summary>
    /// <remarks>
    /// **Valve's <c>CalcView</c> dispatch, and it is not window work** (<c>c_baseplayer.h:112</c>).
    /// The form asks it two questions — which entity to hide, and where the eye is — and supplies
    /// only the viewport's aspect ratio (B188, D90).
    /// </remarks>
    private readonly SpectatorView _spectator;

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

    /// <summary>The systems told about a newly-opened demo, as `_levels` are told about a level.</summary>
    /// <remarks>
    /// **The demo mirror of <c>LevelSystems</c>, and ours rather than Valve's — checked, not
    /// assumed.** In the engine, playing a demo IS loading a level, so systems get `LevelInit*` and
    /// there is no separate event. It does not bind here because this viewer opens a demo BEFORE it
    /// knows whether the map exists, which the engine never faces: a client cannot play a demo whose
    /// map it lacks, and ours must (B201).
    /// </remarks>
    private readonly DemoSystems _demoSystems;

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

    // `_texturesUploaded` was here until 2026-08-26. It is `WorldPresenter.TexturesAreCurrent`,
    // beside the code that reads it (B188, D90).
    //
    // **It has to exist at all because `HasWorldTextures` answers a different question.** The device
    // knows whether textures are RESIDENT, which stays true across a map change — they are simply
    // the wrong ones. Only something that knows about levels can say "resident AND for this one".

    /// <summary>The world upload, and whether this level's textures are the resident ones.</summary>
    private readonly WorldPresenter _world;

    /// <summary>The leaf outline for <c>mat_leafvis</c>, and the warning it may write.</summary>
    /// <remarks>
    /// **Was `_reportedNoLeafBox`, a bool in this window** (B188, D90), whose only job was to stop a
    /// per-frame warning becoming a per-frame warning. The latch, the log and the three reasons it
    /// chooses between now sit with the outline they describe — the same arrangement `MomentScene`
    /// already uses for "no player appearance".
    /// </remarks>
    private readonly LeafBoxes _leafBoxes;

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

    /// <summary>Which camera the viewport is drawn through.</summary>
    /// <remarks>
    /// **Free by default, because after D98 there is no other kind.** The orthographic top-down
    /// camera is gone; what remains is the free camera and the first-person one, and a demo opens
    /// into the free camera.
    ///
    /// **A stale doc block sat above this one until 2026-08-26**, describing a deleted `_freeLook`
    /// bool — *"Off by default, because the top-down view is what this viewer is for"*. Two
    /// <c>&lt;summary&gt;</c> tags on one field, of which the compiler says nothing, and the first
    /// asserted the exact opposite of what the code now does. Residue of the ortho removal itself:
    /// the field went, its documentation did not.
    /// </remarks>
    private CameraMode _cameraMode = CameraMode.Free;

    /// <summary>
    /// Shorthand for the free camera, so the flight and drag handlers read as they did.
    /// </summary>
    /// <remarks>
    /// **Kept deliberately rather than replaced everywhere.** A dozen sites ask "is the free camera
    /// on" to decide whether a drag turns the view or pans the map, and rewriting each to compare
    /// against an enum would be a dozen chances to write the comparison backwards for no gain. The
    /// mode is the state; this is a reading of it.
    /// </remarks>
    private bool _freeLook => _cameraMode == CameraMode.Free;

    /// <summary>Whether the viewport is drawn through a player's eyes.</summary>
    private bool _firstPerson => _cameraMode == CameraMode.FirstPerson;

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

    /// <summary>Where the free camera is, once it has been placed.</summary>
    /// <remarks>
    /// **A position, not an orbit.** Orbiting a point was the first version and it could not do the
    /// one thing the view was added for: a side-on look at a player. The orbit centre sat at the
    /// middle of the map's height range, so levelling the camera looked out well above everybody's
    /// heads, and there was no way to bring it down — the owner's "not low enough for actual side
    /// on, and the camera doesn't move".
    ///
    /// So the camera flies. The orbit maths is still what PLACES it on entry, which keeps whatever
    /// the map view was centred on in the middle of the first frame.
    /// </remarks>
    private (float X, float Y, float Z)? _freeOrigin
    {
        get => _freeCamera.Origin;
        set => _freeCamera.Origin = value;
    }

    /// <summary>Where the free camera is and how it is placed.</summary>
    private readonly FreeCameraController _freeCamera;

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
        if (SetLogVerbosity is not { } set)
        {
            return;
        }

        // **`ViewerSettings.Verbosity` rather than a switch here** (B208). The mapping was stated in
        // `Developer`'s own documentation over in `Scene` and implemented here — the same rule
        // written twice, in two projects, with only one of them running.
        LogLevel level = _settings.Verbosity;

        set(level);

        _log.LogInformation(
            "{Message}",
            string.Create(
                CultureInfo.InvariantCulture,
                $"developer {_settings.Developer}; the log accepts {level} and above"));
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

    /// <summary>Preferences remembered between runs.</summary>
    private ViewerSettings _settings = ViewerSettings.Load();

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
        _models = new EntityModelSet(loggers);
        _log = loggers.CreateLogger("viewer");
        _mapLog = loggers.CreateLogger("map");
        _renderLog = loggers.CreateLogger("render");
        _demoLog = loggers.CreateLogger("demo");
        _audioLog = loggers.CreateLogger("audio");
        _spectateLog = loggers.CreateLogger("spectate");
        _configLog = loggers.CreateLogger("config");

        // **Given its collaborators rather than reaching for them**, which is what lets it be
        // tested without a window: the loops it shares with one-shot playback, the decode cache,
        // and somewhere to report. Its map-dependent state arrives when a map is read.
        _sounds = new SoundCache(_audioLog);
        _soundscape = new SoundscapeSystem(_loops, _sounds.Sample, _audioLog);
        _sound = new SoundPresenter(_soundscape, _loops, _sounds.Sample, _audioLog);
        _freeCamera = new FreeCameraController(_renderLog);

        // **A real source that answers unlit, rather than a null field checked at every call.** The
        // asset loader and the model set both take it as a delegate, and a null there is the shape
        // that hid a missed wiring across 193 call sites once already (D83).
        _spectator = new SpectatorView(_spectateLog);

        _moment = new MomentScene(_models, _viewmodelScene, _renderLog)
        {
            Lighting = LevelLighting.Unlit(_renderLog),
            Weapons = WeaponModels.None(_renderLog),
        };

        // **A local, not a field.** Both readers are constructed here and neither hands it back, so
        // the window has no reason to keep a reference — and keeping one is what would let a later
        // edit report a phase to a ledger nobody prints.
        FrameLedger ledger = new();

        _moments = new MomentPresenter(_moment, ledger, _renderLog);
        _frames = new FrameReporter(ledger, _models, new StopwatchTime(), _renderLog);

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
            _moment, _models, _sounds, _soundscape, _sound, appearances, _loggers);

        _moments.Appearances = appearances;
        _world = new WorldPresenter(_renderLog);
        _leafBoxes = new LeafBoxes(_renderLog);

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
        _launch = LaunchOptionsReader.Read(initialPaths, _settings, _log);

        _settings = _launch.Settings;
        _spectator.Spectating = _launch.Spectate;

        // **This line was MISSING for a day and `--shot` did nothing at all** (B196). `_shotPath`
        // stayed out of the record because taking the shot CONSUMES it, and staying out of the
        // record is exactly how it stopped being assigned: the six `_shot*` fields the parser used
        // to write into went away together, and the one that had to survive them was not wired back
        // up. `TakeAutomaticShot` then read a permanently-null field and returned every frame.
        //
        // Nothing failed. No test passes `--shot`, so the whole option was covered by nobody.
        _opening = new OpeningSequence(_launch.ShotPath, OpeningFrames, SettleFrames);

        initialPaths = [.. _launch.Paths];

        // **The player's own TF2 controls, loaded before anything can be pressed (D69/D70).** This
        // is what makes the console a feature rather than a capability: without it the interpreter
        // runs the shipped defaults for ever and every test of it exercises code the viewer never
        // reaches. Done in the constructor because the alternative — loading with the map, where the
        // archives are already opened — would leave the keys wrong until a demo was opened.
        LoadUserConfig();
        _console.Triggered += OnConsoleAction;

        // **Opened once, here, and null is a normal answer.** The measurement boxes and CI have no
        // sound card, and neither does a machine with audio disabled — a viewer that refused to
        // start over that would be worse than one that draws in silence. Reported either way, so
        // "there is no sound" and "the sound is not working" are distinguishable in the log.
        _audio = AudioOutput.TryCreate();

        _audioLog.LogInformation(
            "{Message}",
            _audio is null
                ? "no audio device could be opened; playback will be silent"
                : "audio output opened");

        Text = "TF2 Demo Salvage";
        Name = "MainWindow";
        AccessibleName = "TF2 Demo Salvage viewer";
        Width = 1280;
        Height = 720;
        ApplyGeometryOverride();

        _viewport = new Panel
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
            ExportButtonId, "Export", "Export the demo as JSON or assembly script.", (_, _) => ExportDemo()));
        _actions.Controls.Add(ActionButton(
            CompileButtonId, "Compile", "Rebuild a demo from an assembly script.", (_, _) => CompileDemo()));

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

        _transport = new TransportBar();

        // **Playback belongs to a presenter now, not to this form (D62).** What used to be three
        // event handlers and a stopwatch here — scrub, play/pause, speed, each with a rule about
        // when to restart the watch — is `PlaybackPresenter`, which knows nothing about WinForms
        // and has sixteen tests. None of that logic could be tested while it lived in this file.
        //
        // The form keeps exactly one job in this area: the moment changed, so redraw.
        _playback = new PlaybackPresenter(_transport, new StopwatchTime());

        // Registered after the presenter it drives, for the same reason `_levels` waits for the
        // scene: a system list is only as good as every member existing when it is built.
        _demoSystems = new DemoSystems(
            _spectator, _moment, _moments, appearances, _sound, _playback, _loops, _loggers);

        _playback.MomentChanged += (_, moment) =>
        {
            // **The tick drives the picture.** Scrubbing and playing both arrive here, so the
            // viewer has one path from "which moment" to "who is where" rather than two that can
            // disagree.
            if (_timeline is null)
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
                Exit: Close,
                SetFullScreen: SetFullScreen,
                SetFullScreenMode: SetFullScreenMode,
                SetTextureQuality: SetTextureQuality,
                SetSurfaceColours: SetSurfaceColours,
                SetFrameRateMeter: SetFrameRateMeter,
                SetWireframe: SetWireframe,
                SetFullbright: SetFullbright,
                SetDrawWorld: SetDrawWorld,
                SetDrawEntities: SetDrawEntities,
                SetDebugMode: SetDebugMode,
                SetSpecular: SetSpecular,
                Screenshot: CaptureViewportToFile),
            _settings,

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
        MainMenuStrip = menu;

        // The docked controls that give their space to the viewport in full screen. The menu is
        // handled separately because a MenuStrip is not hidden the same way.
        _hiddenInFullScreen.Add(_actions);
        _hiddenInFullScreen.Add(playlistPanel);

        // The status strip too: 22 pixels measured on a 1080-line display, and full screen means
        // the map gets the display. What the status line says is not worth a band across it, and
        // the transport moves to a transparent overlay for the same reason.
        _hiddenInFullScreen.Add(statusStrip);

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
            if (initialPaths.Length == 1 && File.Exists(initialPaths[0]))
            {
                LoadDemo(initialPaths[0]);
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
    /// </remarks>
    public const string AutoPlayVariable = "TF2VIEW_AUTOPLAY";

    /// <summary>
    /// Places the free camera, as <c>x y z pitch yaw</c> — TF2's own <c>pos</c> and <c>ang</c>.
    /// </summary>
    /// <remarks>
    /// Written to take a `cl_showpos` readout with as little rearranging as possible: TF2 prints
    /// `pos: x y z` and `ang: pitch yaw roll`, so the five numbers go in that order and roll is
    /// ignored because this camera has none.
    /// </remarks>
    public const string CameraVariable = "TF2VIEW_CAMERA";

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

    /// <summary>The demo currently loaded, or <c>null</c> if none is.</summary>
    public LoadedDemo? Demo => _demo;

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

        int index = _playlist.SelectedIndices[0];

        if (index < 0 || index >= _shown.Count)
        {
            return NothingSelected;
        }

        return LoadDemoAsync(_shown[index].Path);
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
        return ReadMapNamed(mapName).Drawn;
    }

    // `_modelsUploaded` was here until 2026-08-25. It is `MomentScene.Uploaded` now, beside the
    // packing it guards (B188, D90) — and the question it answers is unchanged: "is the packed
    // geometry on the device right now", which is NOT "has the set grown". Conflating them was B148.

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
        _loaded = null;

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

        _device?.ClearWorld();
    }

    /// <summary>Reads a map by name, and hands back the install it opened.</summary>
    /// <param name="mapName">The map the demo names.</param>
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
    /// </remarks>
    private (bool Drawn, GameContent? Game) ReadMapNamed(string mapName)
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
            _status.Text = NoGameInstalled;
            _mapLog.LogWarning("{Message}", NoGameInstalled);
        }

        if (found.Path is not { } path)
        {
            // Not on this machine. Fetch it the way joining a server would - in the background,
            // because a 40 MB download must not freeze the window, and the demo is watchable
            // without a map anyway.
            _mapLog.LogInformation("{Message}", $"{mapName} is not installed; fetching it");
            _ = DownloadMapAsync(mapName);
            return (false, _game);
        }

        _mapLog.LogInformation("{Message}", $"found {path}");

        return (ReadMap(mapName, path), _game);
    }

    /// <summary>Fetches a map that is not installed, then loads it.</summary>
    /// <remarks>
    /// **Downloading is a background operation with a visible outcome and no modal wait.** The
    /// viewer is already usable - players draw without a world behind them - so the map arriving
    /// is an improvement to a working view rather than something to block on.
    ///
    /// Failures are reported and nothing else happens.
    ///
    /// **This used to say "not found" was the ordinary answer, because most maps in a real archive
    /// are community maps no mirror carries. That is wrong.** The owner: *"no its not normal not to
    /// find a map on fast dl, even old community ones"*. A failure here is the exception, not the
    /// rule — which matters, because the belief that it was routine is what justified keeping a
    /// no-map fallback that turned out to be dead by construction anyway (see ProjectMap).
    ///
    /// **And the version does not have to match the demo, which forecloses a whole line of work.**
    /// The hard case is not community maps but Valve's own: they revise a map in place, so the
    /// `cp_badlands` a 2013 demo was recorded on is gone, while a community map keeps its versioned
    /// filename for ever. That sounds like a reason to hunt period maps. It is not — the owner:
    ///
    /// > *"valve has never really blocked off a map as an update, so new demos will go through walls
    /// > on old maps, but old demos will play fine on new maps, just look like the people are
    /// > completely oblivious to a huge choke point and no one is using a part of the map."*
    ///
    /// Map updates ADD geometry rather than removing it, and the asymmetry runs the way this project
    /// needs: an old demo on a current map is correct everywhere the players actually went, and the
    /// only artefact is unused space. Fetching whatever the mirror has today is therefore right, and
    /// a period-map archive would be effort spent on the direction nobody plays.
    /// </remarks>
    private async Task DownloadMapAsync(string mapName)
    {
        _status.Text = MapProvider.Fetching(mapName);

        // **`ConfigureAwait(true)` here, `false` inside the provider, and the asymmetry is the
        // point.** Everything after this line touches `_status.Text` and `ReadMap`, so the
        // continuation has to come back to the UI thread. The provider is a library and must not
        // capture a context it knows nothing about.
        //
        // The `ArgumentException` that used to be caught here — a demo header naming something that
        // is not a map name — is handled inside the provider now and arrives as a status line, since
        // whether a name is fetchable is the downloader's question rather than the window's.
        MapFetch fetch = await _maps
            .FetchAsync(mapName, CancellationToken.None)
            .ConfigureAwait(true);

        if (fetch.Path is null)
        {
            _status.Text = fetch.Status;
            return;
        }

        if (ReadMap(mapName, fetch.Path))
        {
            _status.Text = (_demo?.Describe() ?? mapName) + "  (map downloaded)";
        }
    }

    /// <summary>Reads a map file into the viewport's geometry.</summary>
    private bool ReadMap(string mapName, string path)
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
                _timeline,
                (int)_settings.TextureQuality,
                _menu.SurfaceColours.Checked);

            // **The LEVEL survives a content failure, and it did not before.** The old catch set
            // `_level = null` alongside `_assets = null`, throwing away lumps that had read
            // perfectly because the TEXTURES did not — so `mat_leafvis` went blank on a map whose
            // BSP tree was fine. `LoadedMap` separates the two: the lumps are read or they throw,
            // and the content is a nullable beside them.
            _loaded = map;

            ProjectMap();
            return !map.Outline.IsEmpty;
        }
        catch (Exception failure) when (failure is IOException or InvalidDataException)
        {
            _status.Text = MapProvider.CouldNotRead(mapName, failure);
            return false;
        }
    }

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
        if (_console.LoadFrom(_maps.GameFolder(), _loggers, _configLog) is { } loaded)
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

    /// <summary>Projects the map through a camera fitted to its own bounds.</summary>
    /// <remarks>
    /// Fitted to the MAP rather than to the players. A camera that reframes itself around wherever
    /// the players happen to be turns every scrub into a jump - the world should sit still while
    /// the players move within it.
    /// </remarks>
    private void ProjectMap()
    {
        if (_loaded is not { } map || map.Outline.IsEmpty)
        {
            return;
        }

        // **The segment projection and the flat fill are gone, not skipped.** The first projected all
        // 60,764 of the BSP's edge segments for an overhead view that was removed; the second was
        // dead by construction, built FROM the map as a fallback for having no map. Together they
        // measured `project 615.8 ms` in a 679 ms frame, re-run on every camera change, for
        // triangles nothing would draw.
        //
        // **The early return that replaced them was a real bug, caught before it shipped.** Returning
        // here skips everything below, and everything below is the TEXTURED WORLD UPLOAD. Dropping
        // the fill must not drop the world.
        // **What is left here is the camera, the viewport and the status bar.** Deciding whether the
        // world needs uploading is a question about a level and about what already reached the GPU;
        // performing the upload needs a device, which arrives through `IWorldUpload` — the same seam
        // `IModelUpload` already uses, and the same reason: the decision becomes testable with a
        // fake, and the code that decides stops being the code that talks to Direct3D.
        //
        // **A `TopDownCamera` was passed alongside the matrix until 2026-08-26** (D98). It was
        // threaded down through `BuildWorld` into `MapWorld.Build`, which never read it — a leftover
        // of the top-down culling that broke the free camera once already. The matrix is what the
        // world is actually pointed with.
        WorldUpload result = _world.Project(
            map,
            _device,
            ViewMatrix(),
            _menu.SurfaceColours.Checked,
            (_viewport.ClientSize.Width, _viewport.ClientSize.Height),
            _loggers);

        if (result.Problem is { } problem)
        {
            _status.Text = problem;
        }
    }

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

    // `_shotDelay` was here until 2026-08-26. It is `OpeningSequence`'s countdown (B188, D90).

    // `ReadCaptureOptions` is `LaunchOptionsReader.Read` in Presentation (B188, D90), and it returns
    // a record rather than writing into six fields as it goes. Thirteen tests came with the move; it
    // had none, because reaching it meant constructing a form — so every option was covered only by
    // whichever UI test happened to pass one.

    /// <summary>Takes the automatic capture once the world has settled, then closes.</summary>
    /// <remarks>
    /// **Counted in frames, not seconds.** The map, its textures and the entity models all load
    /// before the first frame is drawn, so a frame count after that is a count of settled frames -
    /// where a wall-clock wait would be a guess that fails on a slower machine or a bigger map.
    /// </remarks>
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
        switch (_opening.Advance())
        {
            case OpeningStep.ApplyOpeningState:
                ApplyOpeningState();
                break;

            case OpeningStep.Capture when _opening.TakeShotPath() is { } path:
                CaptureViewport(path);
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
    /// <see cref="Apply"/> when a demo finishes loading. <c>OpeningSequence.Applied</c> makes the
    /// second of those a no-op.
    /// </remarks>
    private void ApplyOpeningState()
    {
        if (_opening.Applied || _timeline is null)
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
        _playback.Seek(_launch.ShotTick);
        _transport.ShowTick(_launch.ShotTick);
        ShowMoment(_launch.ShotTick);

        _log.LogInformation("{Message}", $"opening state applied at tick {_launch.ShotTick}");

        if (_launch.SurfaceColours)
        {
            _menu.SurfaceColours.Checked = true;
        }

        // **After the seek, because entering the first-person view reads the moment.** The camera
        // is placed from the recorded view or from the followed player at the CURRENT tick, so
        // switching before the clock moves photographs the right mode at the wrong instant — and
        // the picture looks like a camera bug rather than an ordering one.
        if (_launch.FirstPerson)
        {
            _ = ToggleFirstPerson();
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

    /// <summary>The free camera, orbiting whatever the top-down view is centred on.</summary>
    /// <remarks>
    /// **Orbits the same point the map view is looking at**, so toggling between them does not
    /// move the subject — drag the map to a player, switch, and that player is still in the middle.
    ///
    /// The height it orbits is the middle of the map's own vertical range rather than the ground:
    /// a focus at floor level puts half the picture below the world, and the range is already known
    /// because the depth projection needs it.
    /// </remarks>
    /// <summary>The view matrix for whichever camera is active.</summary>
    /// <returns>Sixteen floats for the camera constant buffer.</returns>
    /// <remarks>
    /// **One chooser rather than a conditional at each draw site.** There are two places that set
    /// the camera — the world draw and the resize path — and they were a copied ternary apart.
    /// Adding a third mode to a copied ternary is how the viewer's two drawing paths drifted until
    /// one of them stopped showing decals.
    ///
    /// First person falls back rather than failing: a demo can lose its subject mid-playback — the
    /// recorded view runs out before the first packet, and a spectated player can leave — and a
    /// black screen would read as a rendering fault rather than as the end of the material.
    /// </remarks>
    private float[] ViewMatrix() =>
        ViewCamera.Matrix(_firstPerson, FirstPersonCamera(), FreeLookCamera());

    /// <summary>The leaf outline to draw over the world, when that overlay is switched on.</summary>
    /// <returns>World-space segments, or nothing when the mode is off or there is no leaf.</returns>
    /// <remarks>
    /// **What is left here is the TOGGLE, which is view state, and the eye, which the camera owns.**
    /// The tree walk is <see cref="LeafVis"/>'s and the transform is the GPU's (D95) — this method
    /// no longer takes a matrix at all, because nothing on this side of the seam projects anything.
    ///
    /// The origin is the one the free camera flies from, so the box is the leaf the VIEWER is in
    /// rather than the one the recording happens to be looking from.
    ///
    /// **It reads `_loaded` rather than a field of its own, and that is the fix for a regression
    /// this method carried for a day** (B196). A separate `_level` survived the move that created
    /// <see cref="LoadedMap"/> and its assignment did not, so it held null for ever and this drew
    /// nothing on every map. One field, or it drifts.
    /// </remarks>
    private IReadOnlyList<((float X, float Y, float Z) From, (float X, float Y, float Z) To)> LeafBoxLines()
    {
        if (!_debug.LeafVis)
        {
            return [];
        }

        // **The warning and its once-per-map latch went with the outline** (B188, D90). Saying which
        // of the three silences this is, and saying it once, are both `LeafBoxes`' business — the
        // window's part is which camera to measure from.
        return _leafBoxes.Lines(_loaded, _freeOrigin ?? FreeLookCamera().Origin);
    }

    // **`WhyNoLeafBox` was a wrapper here until 2026-08-26** (B208 moved its body, B188 removed the
    // wrapper). It is `LeafVis.WhyNothing`, called by `LeafBoxes` where the warning is written.
    //
    // The rule it carried is kept where the code went: **a log must name what it measured.** "no
    // leaf box" is true of all three causes and useful for none — a map that never loaded, a map
    // with no BSP tree, and a camera standing in a leaf whose bounds the lump does not carry are
    // three different problems with three different fixes, and only the first two are ours.

    /// <summary>Enters or leaves the first-person view, saying why when it cannot be entered.</summary>
    /// <returns>Whether the key was handled.</returns>
    /// <remarks>
    /// **Refusing has to be visible.** A key that silently does nothing reads as a broken key, and
    /// the reason it can refuse is a real property of the demo rather than a failure — a recording
    /// with nobody in it has no eyes to borrow.
    /// </remarks>
    private bool ToggleFirstPerson()
    {
        if (_firstPerson)
        {
            _cameraMode = CameraMode.Free;
            _world.Invalidate();
            _viewport.Invalidate();
            _renderLog.LogInformation("{Message}", "first person off, back to the free camera");
            return true;
        }

        FirstPersonEntry entry = _spectator.Enter(_transport.CurrentTick, Aspect);

        if (!entry.Entered)
        {
            _renderLog.LogWarning("{Message}", entry.Message);
            _status.Text = entry.Status;
            return true;
        }

        _cameraMode = CameraMode.FirstPerson;
        _world.Invalidate();
        _viewport.Invalidate();

        _renderLog.LogInformation("{Message}", entry.Message);

        return true;
    }

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

    /// <summary>Whose eyes the first-person camera is in, or <c>null</c> when it is not in any.</summary>
    /// <remarks>
    /// **The same choice the camera makes, asked separately** — the camera needs a position and
    /// the renderer needs an entity to hide, and deriving one from the other would let them
    /// disagree. On a point-of-view demo it is the recorder; on a SourceTV demo it is whoever is
    /// being spectated.
    /// </remarks>
    private int? FollowedEntity() => _spectator.Followed(_transport.CurrentTick);

    // `Spectated`, `FirstPersonCamera`, `PlayerAt`, `Ducking` and `_spectating` are `SpectatorView`
    // in Scene (B188, D90). Valve computes a view on the PLAYER and dispatches on observer mode —
    // `C_BasePlayer::CalcView` (`c_baseplayer.h:112`) to `CalcObserverView` (`:455`) to
    // `CalcInEyeCamView`/`CalcChaseCamView`/`CalcRoamingView` (`:463`) — so none of it belonged in a
    // window there either. The only thing it wanted from one was the viewport's aspect ratio.

    /// <summary>The viewport's width over its height, which is all a camera needs from a window.</summary>
    /// <remarks>16:9 before the control has a size, so a camera built during construction is sane.</remarks>
    private float Aspect =>
        _viewport.ClientSize.Height > 0
            ? _viewport.ClientSize.Width / (float)_viewport.ClientSize.Height
            : 16f / 9f;

    /// <summary>The camera for the first-person view, or <c>null</c> when there is none.</summary>
    private FreeCamera? FirstPersonCamera() =>
        _spectator.Eye(_transport.CurrentTick, Aspect);

    /// <summary>The free camera, placed by the controller if nothing has placed it yet.</summary>
    /// <remarks>
    /// **All that is left here is the aspect ratio** (D90). Placing a camera, parsing a placement
    /// and framing a map are presenter work and moved to <see cref="FreeCameraController"/>; the
    /// viewport's width over its height is the only part that needs a window, and it is one float.
    /// </remarks>
    private FreeCamera FreeLookCamera()
    {
        // **Read from the settings every time, not captured once.** A config can be reloaded while
        // the viewer runs, and a field of view latched at construction would ignore it — which is
        // the shape of no-op this project keeps catching: the setting exists, the config is read,
        // and nothing downstream asks.
        _freeCamera.FieldOfView = _settings.FieldOfView;

        return _freeCamera.Camera(
            Math.Max(1, _viewport.ClientSize.Width) / (float)Math.Max(1, _viewport.ClientSize.Height),
            _loaded?.Outline,
            _loaded?.HeightRange is { } range ? range.Highest : 0f);
    }

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
        if (_timeline is null)
        {
            return;
        }

        // **`EnsureWeaponRoles()` was called here until 2026-08-26** (B188, D90). It was the last
        // non-view work in the frame path: one line reaching for `_timeline` and `_game` on every
        // frame, to keep `MomentScene.Appearance` current. `MomentPresenter` asks
        // `PlayerAppearances` now, still before the sampling and still outside both timers — the
        // first call reads weapon scripts out of the archives at an ICE decryption each, and
        // charging that to `sampling` or to `posing` would misname the same spike either way.
        _moments.Show(
            tick,
            new MomentView(
                _transport.CurrentTick,
                _firstPerson,
                FollowedEntity(),
                _firstPerson ? FirstPersonCamera() : null,
                _settings.ViewmodelFieldOfView));
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
        Captures.Folder(_settings.ScreenshotFolder, FileLogWriter.DefaultFolder, _renderLog);

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

    /// <summary>Loads a demo by path and brings the transport up on it.</summary>
    /// <param name="path">Full path to the demo.</param>
    /// <remarks>
    /// Split from <see cref="LoadSelected"/> so loading can be exercised without a live ListView:
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
            return Apply(DecodedDemo.Read(path, _demoLog));
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

        _status.Text = DemoLoadResult.Opening(path);

        try
        {
            ILogger demoLog = _demoLog;
            DecodedDemo decoded =
                await Task.Run(() => DecodedDemo.Read(path, demoLog)).ConfigureAwait(false);

            if (!_loads.IsCurrent(ticket))
            {
                return OnUi(() => Superseded(_demoLog, path));
            }

            // **The map read is the expensive half — 13 to 18 seconds of it (B146).** Dropping the
            // old map touches the device and stays here; finding and reading the new one touches
            // nothing but files and arithmetic, and goes to a worker.
            //
            // **`_readingMap` is what makes that safe.** The read assigns a dozen fields as it
            // goes, and the render loop reads them, so between the two the world is half replaced.
            // The flag holds the loop off until the marshal back, which is also the barrier that
            // publishes the writes.
            OnUi(() =>
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
                    (bool drawn, GameContent? game) = ReadMapNamed(decoded.Demo.MapName);

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
                    // landed once the model stalls were gone.
                    DemoSounds.Precache(_sounds, decoded.Timeline, game, _soundscape, _audioLog);

                    return drawn;
                }).ConfigureAwait(false);
            }
            finally
            {
                OnUi(() =>
                {
                    _readingMap = false;
                    return 0;
                });
            }

            return OnUi(() => _loads.IsCurrent(ticket)
                ? Apply(decoded, read)
                : Superseded(_demoLog, path));
        }
        catch (Exception failure) when (failure is IOException or InvalidDataException)
        {
            return OnUi(() => _loads.IsCurrent(ticket)
                ? CouldNotOpen(path, failure)
                : Superseded(_demoLog, path));
        }
    }

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

        // **One call where there were six assignments, and that is the whole point** (B193). The
        // eyes, the viewmodels, the sound schedule and the appearance were each written inline
        // here, and three of them separately became a property nobody set — two of those shipped,
        // with the viewer suite green throughout. `DemoSystems` is the demo mirror of
        // `LevelSystems`: one place to read, and one place a test can reach.
        //
        // **The environment is read HERE and its VALUE passed in.** A process-wide variable is the
        // window's business — it owns the process — and a system that read one could not be tested
        // without setting it for the whole run.
        _demoSystems.Open(
            _timeline,
            _demo.LastTick,
            _audio,
            Environment.GetEnvironmentVariable(AutoPlayVariable),
            AutoPlayVariable);

        _transport.SetDemoLength(_demo.LastTick);

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

        // Cheap to call twice for the same reason models are: `Sample` returns the cached decode,
        // so on the async path this finds the work already done.
        DemoSounds.Precache(_sounds, _timeline, _game, _soundscape, _audioLog);

        _status.Text = _loaded?.Problem
            ?? (_demo.Describe() + (haveMap ? string.Empty : "  (map not found)"));

        // **A marker pass ran here so opening a demo showed the players standing where they
        // started, rather than an empty map waiting for someone to press play** (D98). The intent
        // survives the markers: `ShowMoment` below draws the first tick's MODELS for the same
        // reason, which is the half that was always the better answer.

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

        return new DemoLoadResult(DemoLoadOutcome.Loaded, _status.Text);
    }

    /// <summary>Puts the form back into a state with no demo, and says why. UI thread only.</summary>
    private DemoLoadResult CouldNotOpen(string path, Exception failure)
    {
        _demo = null;
        _timeline = null;

        // **This replaced `_clock = null`, and it is not the same line spelled differently.** The
        // window's copy of the clock is gone, so the presenter's has to be cleared where the copy
        // used to be — and this is a MANUAL reset path: `CouldNotOpen` never calls
        // `DemoSystems.Open`, so nothing else unloads it. Dropping the line instead would have left
        // the presenter holding the PREVIOUS demo's clock after a failed load.
        _playback.Load(null);

        _transport.SetDemoLength(0);

        // **The wording is `DemoLoadResult`'s** (B188, D90), which also keeps the status line and
        // the returned message identically worded by construction rather than by `_status.Text`
        // being read back — two wordings for one event is how a log and a window come to disagree.
        DemoLoadResult result = DemoLoadResult.CouldNotOpen(path, failure);

        _status.Text = result.Message;

        _demoLog.LogWarning(failure, "{Message}", $"opening {System.IO.Path.GetFileName(path)}");

        return result;
    }

    /// <summary>The playback controls, exposed for the tests that address them.</summary>
    public TransportBar Transport => _transport;

    /// <summary>How full screen is entered.</summary>
    // The stray second summary removed here on 2026-08-26 — "Whether the viewport is filling the
    // screen" — belongs to `IsFullScreen`, sixty lines below, which has its own.
    public FullScreenMode FullScreenMode => _settings.FullScreenMode;

    /// <summary>Chooses how full screen is entered, and remembers it.</summary>
    /// <param name="mode">The mode to use.</param>
    /// <remarks>
    /// Applied immediately when already full screen, so the choice can be judged by making it
    /// rather than by restarting.
    /// </remarks>
    public void SetFullScreenMode(FullScreenMode mode)
    {
        _settings = _settings with { FullScreenMode = mode };
        _menu.Borderless.Checked = mode == FullScreenMode.Borderless;
        _menu.Exclusive.Checked = mode == FullScreenMode.Exclusive;

        if (IsFullScreen && _device is not null)
        {
            bool wanted = mode == FullScreenMode.Exclusive;

            if (!_device.SetExclusiveFullScreen(wanted) && wanted)
            {
                _status.Text = ViewerSettings.ExclusiveFullScreenRefused;
            }
        }

        string? failure = _settings.Save();

        if (failure is not null)
        {
            // Reported rather than swallowed: a preference that silently does not stick is worse
            // than one that says so.
            _status.Text = ViewerSettings.SavedForThisSessionOnly(failure);
        }
    }

    /// <summary>How much texture detail is loaded.</summary>
    public TextureQuality TextureQuality => _settings.TextureQuality;

    /// <summary>Chooses how much texture detail to load, and remembers it.</summary>
    /// <param name="quality">The detail level.</param>
    /// <remarks>
    /// The map is not reloaded here. Textures are decoded when a map is opened, so the change
    /// applies to the next one — which is honest about what it costs rather than pretending a
    /// setting is free.
    /// </remarks>
    public void SetTextureQuality(TextureQuality quality)
    {
        _settings = _settings with { TextureQuality = quality };

        foreach (KeyValuePair<TextureQuality, ToolStripMenuItem> item in _menu.TextureQualityItems)
        {
            item.Value.Checked = item.Key == quality;
        }

        string? failure = _settings.Save();

        _status.Text = failure is null
            ? "Texture quality: " + quality + ". Applies to the next map opened."
            : ViewerSettings.SavedForThisSessionOnly(failure);
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

        _device?.ClearWorld();
        _world.Invalidate();
    }

    /// <summary>Shows or hides Valve's frame rate meter.</summary>
    /// <param name="on">Whether to draw it.</param>
    /// <remarks>
    /// **Two, not one**, because `cl_showfps 2` is the form that names the worst and best frame as
    /// well as the average, and the smoothed single number is the one this project has repeatedly
    /// found hides a stall.
    /// </remarks>
    internal void SetFrameRateMeter(bool on)
    {
        _settings = _settings with { ShowFrameRate = on ? 2 : 0 };

        _renderLog.LogInformation(
            "{Message}",
            string.Create(CultureInfo.InvariantCulture, $"cl_showfps {_settings.ShowFrameRate}"));
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
            if (_settings.FullScreenMode == FullScreenMode.Exclusive &&
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
            _device.VerticalSync = _settings.VerticalSync;

            // **Where packed geometry goes, and forgetting it draws NOTHING** (B193). Without this
            // the scene packs every model, poses it, transforms it correctly and submits it against
            // a vertex buffer the renderer never received — B148's symptom exactly, and silent.
            // Assigned here rather than at construction because the device does not exist until the
            // viewport has a handle.
            _moment.Upload = _device;

            _renderLog.LogInformation(
                "{Message}",
                $"frame rate limit {(_settings.FrameRateLimit > 0 ? _settings.FrameRateLimit + " a second" : "none")}, " +
                $"vertical sync {(_settings.VerticalSync ? "on" : "off")}");
            _renderLog.LogInformation(
                "{Message}",
                $"device created for a {_viewport.ClientSize.Width}x{_viewport.ClientSize.Height} viewport");

            // **Only if there is nothing better to say.** The handle is created after the
            // constructor runs, so a demo opened from the command line has already reported itself
            // by now - and announcing the graphics device over the top of it threw that away. The
            // user saw "Direct3D ready." for a demo that had loaded fine, which reads like the
            // demo did not load.
            if (_demo is null)
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
        uint waiting;

        do
        {
            // **`engine_no_focus_sleep`, before the frame decision rather than inside the wait**
            // (B209). The engine sleeps because nobody is looking, which is a different question
            // from whether the next frame is due — putting it in `WaitForTheNextFrame` would mean an
            // unfocused window still rendered at full rate whenever a frame WAS due, which is every
            // frame at any reachable limit.
            int idle = FramePacer.NoFocusSleep(ContainsFocus, _settings.NoFocusSleep);

            if (idle > 0)
            {
                Thread.Sleep(idle);
            }

            if (_clock.IsDue(_settings.FrameRateLimit))
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
                            _freeLook && _console.AnyHeld,

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
            waiting = MessageQueue.Waiting();
        }
        while (waiting == 0);

        _idleEndedBy = waiting;
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

    // `_fpsMeter` was here until 2026-08-25. The meter belongs to `FpsOverlay` now, which composes
    // the whole readout — the mode, the sampling, the map name and Valve's placement — and needs no
    // window to do it (B188, D90).

    /// <summary>The overlay font's glyphs, packed; null until the overlay is first wanted.</summary>
    /// <remarks>
    /// **Built lazily, because most sessions never turn a HUD element on.** Rasterising a hundred
    /// glyphs is cheap but not free, and it needs a device to upload to — which does not exist until
    /// the viewport panel has a window handle.
    /// </remarks>
    private GlyphAtlas? _hudAtlas;

    /// <summary>Every character the HUD atlas carries: printable ASCII.</summary>
    /// <remarks>
    /// **Enough for the frame rate meter and not enough for a scoreboard**, which is a limit worth
    /// stating rather than discovering. Player names are UTF-8 and routinely are not ASCII at all —
    /// `docs/memory/international-names-are-required.md` — so B175 will need the atlas built from
    /// the characters actually present, or built on demand. The meter's own text is digits, letters
    /// and punctuation, all of which are here.
    /// </remarks>
    private const string HudCharacters =
        " !\"#$%&'()*+,-./0123456789:;<=>?@" +
        "ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`" +
        "abcdefghijklmnopqrstuvwxyz{|}~";

    /// <summary>
    /// The font TF2 draws its frame rate meter with.
    /// </summary>
    /// <remarks>
    /// **Read from `platform/Resource/SourceScheme.res`, which is where `DefaultFixedOutline`
    /// actually lives** — not `tf/resource/ClientScheme.res` and not hl2's, which is why every
    /// Source game's meter looks the same:
    ///
    /// <code>
    /// "DefaultFixedOutline" { "1" { "name" "Lucida Console" "tall" "10" "weight" "0" "outline" "1" } }
    /// </code>
    ///
    /// **A constant rather than a live read of the game's folder, and that is D85 rather than
    /// laziness.** TF2's files are an import SOURCE, not something this viewer reads in place, so
    /// reading a scheme out of the install at startup would be the exact pattern that decision was
    /// written to stop. Once importing exists, a user's own scheme replaces this; until then it is
    /// Valve's values, cited.
    /// </remarks>
    private static readonly SchemeFont MeterFont = new()
    {
        Name = "Lucida Console",
        Tall = 10,
        Weight = 0,
        Outline = true,
    };

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
        switch (_clock.WaitFor(_settings.FrameRateLimit))
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
    private void UploadCamera()
    {
        if (_device is null || !_device.HasWorld)
        {
            return;
        }

        _device.SetCamera(ViewMatrix(), _menu.SurfaceColours.Checked);
    }

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

    /// <summary>This frame's screen-space overlay, which is the frame-rate meter and nothing else.</summary>
    /// <returns>Quads in screen pixels, empty when there is nothing to draw.</returns>
    /// <remarks>
    /// **All that is left here is the atlas and the viewport width** (D90). Composing the readout —
    /// the mode, the sampling, the map name, Valve's placement — is <see cref="FpsOverlay"/>;
    /// rasterising a font is the one genuinely platform-bound part, because ours is GDI and a Linux
    /// port swaps it for FreeType (D84).
    ///
    /// **This was called <c>BuildHud</c> and the name was wrong.** Source's meter is
    /// <c>CFPSPanel : vgui::Panel</c> on <c>PANEL_TOOLS</c> (<c>vgui_int.cpp:209</c>), not a
    /// <c>CHudElement</c> — so a method named for the HUD that returns the fps readout would have
    /// had to split the moment a real HUD element existed. The <c>HudQuad</c> and
    /// <c>HudRenderer</c> names in <c>Render</c> name the screen-space LAYER and are correct.
    /// </remarks>
    public IReadOnlyList<HudQuad> BuildOverlay()
    {
        _overlayQuads.Mode = _settings.ShowFrameRate;

        if (_overlayQuads.NeedsAtlas)
        {
            EnsureOverlayAtlas();
        }

        return _overlayQuads.Quads(
            _hudAtlas, _viewport.ClientSize.Width, _demo?.MapName, _clock.LastFrameSeconds);
    }

    /// <summary>The frame-rate readout, which owns everything about it except the glyphs.</summary>
    private readonly FpsOverlay _overlayQuads = new();

    /// <summary>Rasterises the overlay font and gives it to the device, once.</summary>
    /// <remarks>
    /// **Called when the overlay is first wanted rather than at startup**, so a session that never
    /// switches it on never pays for it and never compiles the overlay shaders.
    ///
    /// **Stays in the view, and it is the one piece here that genuinely must.** <c>GdiGlyphRasteriser</c>
    /// is Windows — a Linux frontend supplies FreeType instead, which is exactly the seam D84 put
    /// behind <c>IGlyphRasteriser</c> in the portable project.
    ///
    /// A failure costs the overlay and nothing else. A viewer that refuses to play a demo because a
    /// font would not rasterise has its priorities backwards — the same argument the file logger is
    /// built around.
    /// </remarks>
    private void EnsureOverlayAtlas()
    {
        if (_hudAtlas is not null || _device is null)
        {
            return;
        }

        try
        {
            using GdiGlyphRasteriser rasteriser = new();

            GlyphAtlas atlas = GlyphAtlas.Build(rasteriser, MeterFont, HudCharacters);

            _device.SetHudAtlas(atlas.Pixels, atlas.Width, atlas.Height);
            _hudAtlas = atlas;

            _renderLog.LogDebug(
                "{Message}",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"hud font {MeterFont.Name} {MeterFont.Tall}px" +
                    $"{(MeterFont.Outline ? " outlined" : string.Empty)}: " +
                    $"{HudCharacters.Length} glyphs in a {atlas.Width}x{atlas.Height} atlas"));
        }
        catch (Exception failure) when (
            failure is InvalidOperationException or ArgumentException or ExternalException)
        {
            _renderLog.LogWarning(failure, "rasterising the hud font");
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

        if (!_freeLook)
        {
            return;
        }

        // **`Intent` is read exactly once per frame, and that is a requirement rather than a
        // convenience.** Reading a button consumes its impulse bits — Valve's `CInput::KeyState`
        // ends with `key->state &= 1` — so a second read in the same frame reports a plain held
        // button and loses the partial credit a tapped key earns. This is the only call site.
        //
        // **The old "nothing is held, so skip" guard is gone with it.** A key pressed and released
        // between two frames is up by the time anyone looks, so that guard would have thrown the
        // tap away; `Fly` returning false still costs nothing when genuinely idle.
        if (_freeCamera.Fly(_console.Intent(), seconds, FreeLookCamera().Origin))
        {
            // The view, and nothing else. Flight only happens in the free camera, where the map's
            // screen-space projection is not what is being drawn (B98).
            UploadCamera();
        }
    }

    /// <summary>The audio device, or null when the machine has none.</summary>
    private AudioOutput? _audio;

    /// <summary>The looping sounds in flight, shared by the presenter and the soundscape.</summary>
    /// <remarks>
    /// **Still here only because two collaborators share it**, and the window is where they are
    /// both constructed. The schedule, the audible-crossing memory and Valve's MIN_AUDIBLE_VOLUME
    /// went with SoundPresenter on 2026-08-25 (B188) — they were its state, not a window's.
    /// </remarks>
    private readonly ActiveLoops _loops = new();

    /// <summary>Decides what should be audible at a tick.</summary>
    private readonly SoundPresenter _sound;

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

    /// <summary>Starts whatever the recording plays at this tick.</summary>
    /// <remarks>
    /// **The spatialisation is Valve's and stays Valve's (D80).** The gain comes from
    /// <c>SoundGain.AtDistance</c> against the sound's own <c>SNDLVL</c>, and the pan from
    /// <c>SoundGain.Pan</c> — the sink is handed finished stereo and applies no distance model of
    /// its own. Anything else would replace a curve this project can compare against the engine
    /// with one it cannot.
    /// </remarks>
    private void PlaySounds()
    {
        if (_audio is not { } output)
        {
            return;
        }

        // **What is left here is the LISTENER and nothing else** (B188). Deciding what should be
        // audible moved to SoundPresenter; a window's remaining business is that the ears are
        // wherever the camera is, which is view state and cannot come from anywhere else.
        if (SoundListener.From(_firstPerson ? FirstPersonCamera() : FreeLookCamera())
            is not { } ears)
        {
            return;
        }

        long soundAt = Stopwatch.GetTimestamp();

        SoundPhases phases = _sound.Update(
            output,
            _transport.CurrentTick,
            ears.Origin,
            ears.Right,
            _audioClock.Elapsed.TotalSeconds);

        StallReport.Sounds(phases, Stopwatch.GetTimestamp() - soundAt, _audioLog);
    }

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
    /// <summary>A clock that runs for the life of the window.</summary>
    /// <remarks>
    /// **Its own, because the flight clock is restarted every frame** and so can only report a
    /// frame's duration, never a running time. A crossfade needs both: when the choice was last made
    /// and how long since the last advance.
    ///
    /// Real time rather than demo time, deliberately. `soundscape_fadetime` is three seconds of
    /// wall clock in the engine, so a fade tied to demo ticks would stretch when playback slows and
    /// vanish when it is scrubbed.
    /// </remarks>
    private readonly System.Diagnostics.Stopwatch _audioClock = System.Diagnostics.Stopwatch.StartNew();

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
    /// `docs/memory/a-log-must-name-what-it-measured.md` is about. It survived because a high frame
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
        if (_readingMap)
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
        FramePhases phases = FrameSequence.Run(this);

        _frames.Drawing(phases.Draw);

        StallReport.Frame(phases, _renderLog);

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
        FlyCamera();
        UploadCamera();
    }

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
        if (!_world.NeedsProjecting)
        {
            return;
        }

        _world.Projected();
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
    /// <param name="overlay">The quads built for this frame.</param>
    public void Draw(IReadOnlyList<HudQuad> overlay) =>
        _device?.DrawFrame(
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
            LeafBoxLines(),

            // **Empty since 2026-08-26** (D98), like the fill above it. This channel carried the
            // flat player markers, projected through the orthographic camera. The parameter stays
            // until `Device3D`'s signature is revised, which belongs with the render seam rather
            // than with this window.
            [],
            _moment.Instances,
            _moment.ViewmodelInstances,
            _moment.ViewmodelCamera?.ToMatrix(),
            overlay);

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
        if (_loaded is not { } shown || shown.Outline.IsEmpty)
        {
            return;
        }

        // `step` was `e.Delta > 0 ? 1.25f : 1f / 1.25f` here until 2026-08-26 (B208). The ratio is
        // `MapZoom.Step` and the direction is `In()` or `Out()`, so the window reads the wheel and
        // names neither.

        // In the free view the wheel moves the camera in and out instead of magnifying a flat map.
        // The near limit is a little over a player's height, so a model can be filled the frame
        // with without the near plane cutting into it.
        // In the free view the wheel flies forward and back, which is what a wheel does in every
        // editor and is far quicker than tapping W across a map.
        if (_freeLook)
        {
            // **The whole branch is one call now** (B204, B206). It was a hand-inlined copy of
            // `AngleVectors`' forward vector — the fourth in this repository — multiplied by a
            // travel distance spelled `FlySpeed * 4f`. How far a notch flies and along which vector
            // are both camera rules; that the WHEEL is what asks is the part this window owns.
            _freeCamera.Dolly(e.Delta > 0, FreeLookCamera().Origin);

            _world.Invalidate();
            _viewport.Invalidate();
            return;
        }

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
        if (e.Button == MouseButtons.Left)
        {
            _dragFrom = e.Location;
        }

        _console.KeyDown(KeyNames.NameOf(e.Button));
    }

    private void OnViewportMouseMove(object? sender, MouseEventArgs e)
    {
        if (_dragFrom is not { } from || _loaded is not { } shown || shown.Outline.IsEmpty)
        {
            return;
        }

        // **A drag turns the camera.** Pitch is clamped by the camera itself, at the same 89 degrees
        // the engine clamps a player to, because the basis is degenerate looking exactly along the
        // world's up axis.
        //
        // **The `if (_freeLook)` around this went with the pan branch on 2026-08-26** (D98). The
        // other arm slid the orthographic map by converting pixels to world units through
        // `WorldUnitsPerPixel`; with one camera left there is no second thing for a drag to mean.
        _freeCamera.Drag(e.Location.X - from.X, e.Location.Y - from.Y);

        _dragFrom = e.Location;
        _world.Invalidate();
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
                CycleTarget(reverse: false);
                break;

            case ViewerAction.CycleTargetReverse:
                CycleTarget(reverse: true);
                break;

            default:
                break;
        }
    }

    /// <summary>Follows the next or previous player.</summary>
    /// <remarks>
    /// **Only while spectating, which is the gate the game applies too.** `spec_next` does nothing
    /// unless `GetObserverMode() > OBS_MODE_FIXED`, and in the free camera the left button is
    /// already the look-around drag — cycling on it would fight the gesture.
    ///
    /// **A cycle that finds nobody leaves the camera where it is**, following
    /// `if ( target ) SetObserverTarget( target );`. The first seconds of a competitive match really
    /// are SourceTV alone, and a click then must not blank the view.
    /// </remarks>
    private void CycleTarget(bool reverse)
    {
        // **First person is the window's condition, not the spectator's.** Which player comes next
        // is a fact about the roster whether or not anyone is looking through their eyes; that this
        // key does nothing in the overhead view is a decision about this UI.
        if (!_firstPerson)
        {
            return;
        }

        SpectatorSwitch switched = _spectator.Cycle(_transport.CurrentTick, reverse);

        _spectateLog.LogDebug("{Message}", switched.Message);

        if (!switched.Switched)
        {
            return;
        }

        _world.Invalidate();
        _viewport.Invalidate();
    }

    // **`WorldAt` was here until 2026-08-26** (D98). It answered "which world point is under this
    // pixel", which only has an answer for a projection that maps the screen onto a plane. A
    // perspective view of a 3D world does not: a pixel is a ray, not a point, and answering it
    // properly is a trace rather than an unprojection.

    private void OnViewportResize(object? sender, EventArgs e)
    {
        _overlay?.PositionOver(_viewport);
        _world.Invalidate();

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
        if (WidgetKeys.Keeps(FocusKind(), KeyNames.NameOf(keyData & Keys.KeyCode)))
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
        if (_freeLook && FreeFlight.IsFlightKey(keyData & Keys.KeyCode, _bindings))
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
            return ToggleFirstPerson();
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
            // **F now RESETS the camera to the overhead placement rather than switching mode.**
            // It used to toggle between the map view and the free camera; with the orthographic
            // camera gone (D49) there is no second mode to switch to, and the overhead view is a
            // placement of this one. So the key keeps its meaning — "show me the whole map again" —
            // and drops the mode it used to carry.
            _cameraMode = CameraMode.Free;
            _freeOrigin = null;

            // A key still recorded as held would move the camera the instant it is re-placed.
            ReleaseHeldKeys();

            _world.Invalidate();
            _viewport.Invalidate();

            // **Says what it did, not which mode it is in.** The old line reported "free camera on"
            // or "free camera off, back to the map view", and both are now false: there is one
            // camera and this key does not switch anything. A log that names the wrong quantity
            // misdirects with authority (`docs/memory/a-log-must-name-what-it-measured.md`).
            _renderLog.LogDebug("{Message}", "camera reset to the overhead placement");

            return true;
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

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            AddToLibrary(dialog.SelectedPath);
        }
    }

    /// <summary>Adds paths to the library and refreshes the playlist.</summary>
    private void AddToLibrary(params string[] paths)
    {
        foreach (string path in paths)
        {
            _library.Open(path);
        }

        // Sorted once per library change rather than per keystroke: folder first so the list
        // reads as folders, then name within each.
        // **Beside the filter whose contract depends on it** (B188, D90). This was three lines here
        // while `PlaylistFilter.Apply` documented its dependence on them — a precondition stated in
        // one assembly and satisfied in another is a precondition nothing enforces.
        _ordered = PlaylistFilter.Order(_library.Entries);

        RefreshPlaylist();

        _status.Text = _library.Entries.Count switch
        {
            0 => "No demos found.",
            1 => "1 demo.",
            int count => string.Create(CultureInfo.InvariantCulture, $"{count} demos."),
        };
    }

    /// <summary>Rebuilds the playlist from the library and the search box.</summary>
    /// <remarks>
    /// Nothing is constructed here. Setting <see cref="ListView.VirtualListSize"/> tells the
    /// control how many rows exist, and it then asks for the handful it needs to paint - which is
    /// what keeps a keystroke cheap on an archive of thousands.
    /// </remarks>
    private void RefreshPlaylist()
    {
        _shown = PlaylistFilter.Apply(_ordered, _search.Text);

        // Selection indices refer to the OLD list. Clearing first avoids the control asking for a
        // row that the new, shorter list does not have.
        _playlist.SelectedIndices.Clear();
        _playlist.VirtualListSize = _shown.Count;
        _playlist.Invalidate();
    }

    /// <summary>Supplies one row of the playlist on demand.</summary>
    private void OnRetrieveVirtualItem(object? sender, RetrieveVirtualItemEventArgs e)
    {
        if (e.ItemIndex < 0 || e.ItemIndex >= _shown.Count)
        {
            // Reachable during a resize that races a filter change. A blank row is survivable;
            // an exception out of a paint handler is not.
            e.Item = new ListViewItem(string.Empty);
            return;
        }

        DemoEntry entry = _shown[e.ItemIndex];
        ListViewItem row = new(entry.Name) { Tag = entry.Path };
        row.SubItems.Add(entry.Folder);
        e.Item = row;
    }

    private void ExportDemo() => _status.Text = _demo is null
        ? "Open a demo first."
        : "Export is not wired up yet.";

    private void CompileDemo() => _status.Text = "Compile is not wired up yet.";

    private void OpenDemo()
    {
        using OpenFileDialog dialog = new()
        {
            Filter = "Source demos (*.dem)|*.dem|All files (*.*)|*.*",
            Title = "Open demos",

            // Several at once, the way a file browser and every video player does it.
            Multiselect = true,
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            AddToLibrary(dialog.FileNames);
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

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing && ShutdownRuns == 0)
        {
            ShutdownRuns++;
            // **Timed, because a slow exit is a defect nobody can diagnose from the outside.**
            // Two hundred textures, a lightmap atlas and a swap chain go here, and which of them
            // is slow is not guessable - the log says.
            Stopwatch closing = Stopwatch.StartNew();

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
            _audio?.Dispose();
            _audio = null;

            _renderLog.LogInformation(
                "{Message}",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"shutdown: idle stopped after {idleStopped.TotalMilliseconds:F0} ms, " +
                    $"device released after {closing.Elapsed.TotalMilliseconds:F0} ms"));

            // Both are in Controls, which base.Dispose already walks - but the analyzer cannot
            // see that ownership, and stating it costs nothing and is true.
            _viewport.Dispose();
            _status.Dispose();
            _actions.Dispose();
            _transport.Dispose();
            _playlist.Dispose();
            _search.Dispose();
            _maps.Dispose();
            _overlay?.Dispose();

            // Thirteen menu items were named here one at a time until 2026-08-26. `ViewerMenu` owns
            // them and disposes them beside the code that built them (B188, D90).
            _menu.Dispose();
        }

        base.Dispose(disposing);
    }
}
