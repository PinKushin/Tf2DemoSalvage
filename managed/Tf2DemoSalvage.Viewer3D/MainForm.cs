using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;
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
internal class MainForm : Form
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

    /// <summary>What the viewport draws: entity positions already projected to clip space.</summary>
    /// <remarks>
    /// Held as projected points rather than world positions so the render loop does no work per
    /// frame beyond handing them over. Re-projection happens when the tick or the viewport size
    /// changes, which is far rarer than a frame.
    /// </remarks>
    private IReadOnlyList<ScenePoint> _scene = [];

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

    /// <summary>Fetches maps that are not installed; created on first need.</summary>
    private MapDownloader? _downloader;

    /// <summary>What the installed game provides, opened once and reused for every map.</summary>
    /// <remarks>
    /// **`_archives`, `_classModels` and `_entityClasses` were three fields opened inside the first
    /// map read** (B188, D90). None of them is per-map: they are what the INSTALL supplies, and
    /// they sat there because that is where the first caller happened to be.
    ///
    /// Null until a map is read, because locating the install is the one thing that waits for a
    /// reason to happen.
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

    private DemoTimeline? _timeline;

    /// <summary>Reused between frames; PlayersAt and PropsAt fill them rather than allocating.</summary>
    private readonly List<ScenePlayer> _players = [];

    private readonly List<SceneProp> _props = [];

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

    /// <summary>Turns real time into demo ticks at the rate the recording server ran.</summary>
    private PlaybackClock? _clock;

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

    /// <summary>Whether the resident textures belong to the map currently loaded.</summary>
    private bool _texturesUploaded;

    /// <summary>Whether the empty leaf outline has already been explained for this map.</summary>
    /// <remarks>
    /// **Once per map, not once per frame.** `LeafBoxLines` runs on every frame the overlay is on,
    /// and a warning written from there unguarded is B191 exactly: one log line per frame taking a
    /// machine-wide lock and a disk flush, which cost 120 ms of a 133 ms frame the last time.
    /// Cleared by `ClearMap`, so the next map gets its own answer.
    /// </remarks>
    private bool _reportedNoLeafBox;

    /// <summary>Whether the viewport has changed size since the world was last projected.</summary>
    private bool _worldIsStale;

    /// <summary>How far the view is zoomed in past the fitted whole map.</summary>
    /// <remarks>
    /// **Free to change now, which it was not before.** The projection used to be baked into every
    /// vertex, so zooming meant rebuilding 2.6 million of them; the camera is a matrix, so this is
    /// a 64-byte upload and can be driven by a mouse wheel.
    /// </remarks>
    private float _zoom = 1f;

    /// <summary>Where the view is centred, or null to keep it on the whole map.</summary>
    private (float X, float Y)? _lookingAt;

    /// <summary>Whether the view is the free camera rather than the map's top-down one.</summary>
    /// <remarks>
    /// **Off by default, because the top-down view is what this viewer is for.** A demo is watched
    /// from above; the free camera is for looking AT something — which until now was impossible,
    /// and is why a player model lying on its back survived a day of screenshots taken from
    /// directly overhead.
    /// </remarks>
    /// <summary>Which camera the viewport is drawn through.</summary>
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

    /// <summary>Pitch and yaw of the free camera, in degrees.</summary>
    /// <remarks>
    /// Starts at a shallow angle rather than at zero: a camera on the horizon looking across a map
    /// shows mostly wall, and the first thing anyone wants from this view is to see whether the
    /// players are standing up.
    /// </remarks>
    /// <remarks>
    /// **The state itself moved to <see cref="FreeCameraController"/> on 2026-08-25** (D90). These
    /// two are accessors onto it, so the flight and drag handlers that live in this window — which
    /// genuinely are input handling — read and write one place.
    /// </remarks>
    private (float Pitch, float Yaw) _freeAngles
    {
        get => _freeCamera.Angles;
        set => _freeCamera.Angles = value;
    }

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

    /// <summary>Degrees the free camera turns per pixel dragged.</summary>
    /// <remarks>
    /// A quarter of a degree, so a full turn is about a screen and a half of dragging. Source's own
    /// mouse sensitivity is a different quantity — it scales a raw device count rather than a
    /// pixel — so this is chosen for the drag rather than taken from the engine.
    /// </remarks>
    private const float DegreesPerPixel = 0.25f;

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

    /// <summary>
    /// How much of the map's height is cut away from the top, from 0 to 1.
    /// </summary>
    /// <remarks>
    /// **What lets an overhead view see inside a building.** A roof is an upward-facing surface, so
    /// nothing culls it and everything under it - the hallways into last on cp_process, the rooms
    /// under the domes at mid - is simply hidden. Slicing the map at a height is how every level
    /// editor solves this, and it costs nothing here because the shader discards on the depth the
    /// vertices already carry.
    /// </remarks>
    private float _heightCut;

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

    private readonly ToolStripMenuItem _fullScreen;

    /// <summary>Draws flat colours by surface kind instead of the map's own textures.</summary>
    private readonly ToolStripMenuItem _surfaceColours;

    /// <summary>Draws the brush outline over the map.</summary>
    /// <remarks>
    /// **Off by default now that the map has textures.** The outline was the entire picture when
    /// nothing else drew, and it stayed switched on out of habit - over a textured map it is
    /// clutter that hides the thing it was standing in for.
    /// </remarks>
    private readonly ToolStripMenuItem _wireframe;

    /// <summary>TF2's <c>cl_showfps</c>, on F8 (B174).</summary>
    private readonly ToolStripMenuItem _frameRate;

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

        LogLevel level = _settings.Developer switch
        {
            >= 2 => LogLevel.Trace,
            1 => LogLevel.Debug,
            _ => LogLevel.Information,
        };

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

    private readonly ToolStripMenuItem _specular;
    private readonly ToolStripMenuItem _fullbrightMenu;
    private readonly ToolStripMenuItem _drawWorld;
    private readonly ToolStripMenuItem _drawEntities;
    private readonly ToolStripMenuItem _debugMenu;

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

        foreach (ToolStripItem entry in _fullbrightMenu.DropDownItems)
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
    private readonly ToolStripMenuItem _borderlessMode;
    private readonly ToolStripMenuItem _exclusiveMode;

    /// <summary>The texture quality items, so the tick can be moved between them.</summary>
    private readonly Dictionary<TextureQuality, ToolStripMenuItem> _textureQualityItems = [];

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
        _shotPath = _launch.ShotPath;

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

        MenuStrip menu = new() { Name = "MainMenu", AccessibleName = "Main menu" };
        ToolStripMenuItem file = new("&File")
        {
            Name = FileMenuId,
            AccessibleName = "File menu",
        };

        ToolStripMenuItem open = new("&Open demo...")
        {
            Name = OpenDemoItemId,
            AccessibleName = "Open demo",
            ShortcutKeys = Keys.Control | Keys.O,
        };
        open.Click += (_, _) => OpenDemo();

        ToolStripMenuItem exit = new("E&xit")
        {
            Name = ExitItemId,
            AccessibleName = "Exit",
        };
        exit.Click += (_, _) => Close();

        _fullScreen = new ToolStripMenuItem("&Full screen")
        {
            Name = FullScreenItemId,
            AccessibleName = FullScreenItemName,
            ShortcutKeys = Keys.F11,
            CheckOnClick = true,
        };
        _fullScreen.CheckedChanged += (_, _) => SetFullScreen(_fullScreen.Checked);

        // **Both modes offered, because neither is right for everyone.** Borderless always works
        // and alt-tabs instantly; exclusive is the lower-latency path and can be refused by DXGI.
        _borderlessMode = new ToolStripMenuItem("&Borderless")
        {
            Name = BorderlessItemId,
            AccessibleName = "Borderless full screen",
            Checked = _settings.FullScreenMode == FullScreenMode.Borderless,
        };
        _borderlessMode.Click += (_, _) => SetFullScreenMode(FullScreenMode.Borderless);

        _exclusiveMode = new ToolStripMenuItem("&Exclusive")
        {
            Name = ExclusiveItemId,
            AccessibleName = "Exclusive full screen",
            Checked = _settings.FullScreenMode == FullScreenMode.Exclusive,
        };
        _exclusiveMode.Click += (_, _) => SetFullScreenMode(FullScreenMode.Exclusive);

        ToolStripMenuItem fullScreenMode = new("Full screen &mode")
        {
            Name = "FullScreenModeMenu",
            AccessibleName = "Full screen mode",
        };
        fullScreenMode.DropDownItems.Add(_borderlessMode);
        fullScreenMode.DropDownItems.Add(_exclusiveMode);

        // **Texture detail, chosen from the game's own mip chain.** Not a quality slider over
        // something resampled here: each level is an image Valve generated when the texture was
        // made, so a lower setting is a smaller read and a smaller upload rather than extra work.
        ToolStripMenuItem textureQuality = new("&Texture quality")
        {
            Name = TextureQualityMenuId,
            AccessibleName = "Texture quality",
        };

        foreach (TextureQuality quality in new[]
        {
            TextureQuality.Full, TextureQuality.High, TextureQuality.Medium, TextureQuality.Low,
        })
        {
            TextureQuality chosen = quality;
            int pixels = (int)quality;

            ToolStripMenuItem item = new(
                pixels == 0
                    ? "&Full"
                    : string.Create(CultureInfo.InvariantCulture, $"{quality} ({pixels} px)"))
            {
                Name = "TextureQuality" + quality,
                AccessibleName = "Texture quality " + quality,
                Checked = _settings.TextureQuality == quality,
            };

            item.Click += (_, _) => SetTextureQuality(chosen);
            _textureQualityItems.Add(quality, item);
            textureQuality.DropDownItems.Add(item);
        }

        ToolStripMenuItem view = new("&View") { Name = ViewMenuId, AccessibleName = ViewMenuName };
        // **A diagnostic view, kept in the product deliberately.** It answers "is anything here,
        // and what kind of thing is it", which a textured picture cannot - and which cost hours
        // this session when terrain, a material and a prop each went missing while the map still
        // looked like a map.
        _surfaceColours = new ToolStripMenuItem("Surface &colours")
        {
            Name = SurfaceColoursItemId,
            CheckOnClick = true,
            ShortcutKeys = Keys.F9,
        };

        _surfaceColours.CheckedChanged += (_, _) =>
        {
            // **The legend goes in the log, because a colour nobody can name is not an answer.**
            // Violet was read as "the sign" and white as "an uncoloured surface" during the B154
            // hunt, both wrong, and there was nowhere to look it up.
            _renderLog.LogInformation(
                "{Message}",
                _surfaceColours.Checked
                    ? "surface colours on — grey-blue brushwork, green terrain, orange props, " +
                      "violet overlays, Valve's magenta chequer where a material resolved to " +
                      "nothing; brush entities take their own FGD colour, magenta where the class " +
                      "states none, as Hammer draws them"
                    : "surface colours off");

            _device?.ClearWorld();
            _worldIsStale = true;
        };

        // **A menu item as well as the cvar, because a cvar nobody can find is a cvar nobody uses.**
        // The owner's words are the requirement: "we need a fps overlay too, we dont have one so i
        // have no idea what fps we are rendering at and cant tell stutter in the demo from stutter
        // in the decode, from stutter in fps" — and later, "we might have a fps overlay i just dont
        // normally turn on, which you launching for me to check the sounds would have allowed me to
        // check". Something reached for mid-investigation has to be one keypress away.
        //
        // **It sets `cl_showfps 2`, not 1**, because the smoothed meter is the one that answers his
        // question. Mode 1 is an instantaneous rate that jumps every frame; mode 2 carries the worst
        // and best single frame beside the average, and an occasional long frame against a healthy
        // average is exactly what stutter looks like.
        //
        // **F8, which is free.** F9 is surface colours, F10 wireframe, F11 full screen and F12 the
        // screenshot — and F11 colliding with full screen silently broke it for days (B165), so a
        // new shortcut gets checked against the four already here rather than assumed spare.
        _frameRate = new ToolStripMenuItem("&Frame rate")
        {
            Name = FrameRateItemId,
            CheckOnClick = true,
            Checked = _settings.ShowFrameRate != 0,
            ShortcutKeys = Keys.F8,
            AccessibleName = "Frame rate",
            AccessibleDescription =
                "Draws TF2's own frame rate meter in the top right: the average, the worst and best " +
                "single frame in brackets, and how long this frame took.",
        };

        _frameRate.CheckedChanged += (_, _) =>
        {
            // Written back into the settings rather than held beside them, so the config file, a
            // launch option and this menu are one value rather than three that can disagree.
            _settings = _settings with { ShowFrameRate = _frameRate.Checked ? 2 : 0 };

            _renderLog.LogInformation(
                "{Message}",
                string.Create(CultureInfo.InvariantCulture, $"cl_showfps {_settings.ShowFrameRate}"));
        };

        // **Valve's `mat_wireframe`, replacing the brush outline that used to sit on F10.** The
        // outline drew precomputed BSP edge segments as an overlay — 60,764 of them, built for the
        // overhead view and, as the owner put it, "like an ortho overlay". It could not answer the
        // question a wireframe is for, because it drew edges from the map file rather than the
        // triangles actually submitted: no props, no models, nothing about what reached the GPU.
        //
        // This one is a rasteriser fill mode over every pass, so an edge on screen means that
        // triangle was drawn. That is the difference between "not submitted" and "submitted and
        // invisible", which nothing else in this viewer can distinguish.
        _wireframe = new ToolStripMenuItem("&Wireframe")
        {
            Name = WireframeItemId,
            CheckOnClick = true,
            Checked = false,
            ShortcutKeys = Keys.F10,
            AccessibleName = "Wireframe",
            AccessibleDescription =
                "Draws every surface as edges only, so geometry that never reached the screen can " +
                "be told apart from geometry that is drawn but invisible.",
        };

        _wireframe.CheckedChanged += (_, _) =>
        {
            _renderLog.LogInformation("{Message}", $"mat_wireframe {(_wireframe.Checked ? 1 : 0)}");

            if (_device is { } device)
            {
                device.Wireframe = _wireframe.Checked;
            }

            _viewport.Invalidate();
        };

        // **`mat_specular`, and it is a diagnostic before it is a preference.** A cubemap
        // reflection is ADDED to an opaque surface, so a prop whose envmap term dominates draws in
        // the colour of whatever its cubemap holds — against a sky, that is the sky, and the prop
        // reads as geometry that was never drawn. Surface colours returns from the shader before
        // the reflection is added, which is why a surface can be invisible in the textured view
        // and present in the category view: the same triangles, coloured differently.
        // **No shortcut, because F8 was already the frame rate's and every function key is taken.**
        // This carried ShortcutKeys = Keys.F8 alongside the frame-rate item, so two menu items
        // claimed one key and one of them silently did nothing — the same defect as F12, found in
        // the same audit, and the third instance of it in this file after B165's F11.
        //
        // The frame rate keeps F8 because it has a stated reason: it mirrors TF2's own cl_showfps
        // (B174). Reflections is a debug toggle with no such claim, and inventing Ctrl+F8 for it
        // would be an arbitrary answer to a question nobody asked. The menu still reaches it.
        _specular = new ToolStripMenuItem("&Reflections")
        {
            Name = SpecularItemId,
            CheckOnClick = true,
            Checked = true,
            AccessibleName = "Reflections",
            AccessibleDescription =
                "Adds cubemap reflections to surfaces that ask for them. Turn off to see whether " +
                "a reflection is hiding a surface.",
        };

        // **A submenu of three, because `mat_fullbright` has three states.** Offering it as a
        // checkbox would be the same mistake as reading the cvar's name and assuming a boolean —
        // and it is the more useful state, lighting-only, that a checkbox would drop.
        _fullbrightMenu = new ToolStripMenuItem("&Lighting")
        {
            Name = FullbrightItemId,
            AccessibleName = "Lighting",
            AccessibleDescription =
                "Substitutes the lighting or the texture, to tell a shadow apart from a dark " +
                "texture and a painted shape apart from a lit one.",
        };

        foreach ((Fullbright mode, string label, Keys key) in new[]
        {
            (Fullbright.Off, "&Normal", Keys.F5),
            (Fullbright.NoLighting, "&No lighting (mat_fullbright 1)", Keys.F6),
            (Fullbright.LightingOnly, "Lighting &only (mat_fullbright 2)", Keys.F7),
        })
        {
            Fullbright chosen = mode;

            ToolStripMenuItem item = new(label)
            {
                Name = FullbrightItemId + chosen,
                ShortcutKeys = key,
                Checked = chosen == Fullbright.Off,
            };

            item.Click += (_, _) => SetFullbright(chosen);

            _fullbrightMenu.DropDownItems.Add(item);
        }

        // **`r_drawworld` and `r_drawentities`, which answer "which pass owns this".** The question
        // comes up the moment something is drawn twice, in the wrong order, or by code nobody
        // expected — and it took a day to answer by hand when static props turned out to be
        // inheriting the overlay pass's blend state (B154).
        _drawWorld = new ToolStripMenuItem("Draw &world")
        {
            Name = DrawWorldItemId,
            CheckOnClick = true,
            Checked = true,
            AccessibleName = "Draw world",
            AccessibleDescription = "Draws map brushwork and its overlays. Turn off to see only entities.",
        };

        _drawWorld.CheckedChanged += (_, _) =>
        {
            _renderLog.LogInformation("{Message}", $"r_drawworld {(_drawWorld.Checked ? 1 : 0)}");

            if (_device is { } world)
            {
                world.DrawWorld = _drawWorld.Checked;
            }

            _viewport.Invalidate();
        };

        _drawEntities = new ToolStripMenuItem("Draw &entities")
        {
            Name = DrawEntitiesItemId,
            CheckOnClick = true,
            Checked = true,
            AccessibleName = "Draw entities",
            AccessibleDescription = "Draws static props and models. Turn off to see only the map.",
        };

        _drawEntities.CheckedChanged += (_, _) =>
        {
            _renderLog.LogInformation("{Message}", $"r_drawentities {(_drawEntities.Checked ? 1 : 0)}");

            if (_device is { } entities)
            {
                entities.DrawEntities = _drawEntities.Checked;
            }

            _viewport.Invalidate();
        };

        // **A submenu of independent switches, because Valve's are independent cvars.** Grouping
        // them as radio items would be tidier and would misrepresent the engine: mat_drawflat and
        // mat_luxels compose, and seeing a luxel grid on flat-shaded geometry is a legitimate thing
        // to want when a shadow looks wrong and you cannot tell whether the texture is confusing
        // you.
        _debugMenu = new ToolStripMenuItem("&Debug views")
        {
            Name = DebugMenuItemId,
            AccessibleName = "Debug views",
            AccessibleDescription =
                "Valve's per-surface debug visualisations: flat shading, the luxel grid, and " +
                "normal maps shown as colour.",
        };

        foreach ((string label, string cvar, Keys key) in new[]
        {
            ("Flat &shading (mat_drawflat)", nameof(DebugModes.DrawFlat), Keys.F1),
            ("&Luxel grid (mat_luxels)", nameof(DebugModes.Luxels), Keys.F2),
            ("&Normal maps (mat_normalmaps)", nameof(DebugModes.NormalMaps), Keys.F3),
            ("Bump &basis (mat_bumpbasis)", nameof(DebugModes.BumpBasis), Keys.F4),
            // **Not F11, which is full screen — this collided and full screen lost.** The debug
            // group runs F1..F4 and every remaining function key was already taken (F5..F7
            // lighting, F8 reflections, F9 surface colours, F10 wireframe, F11 full screen, F12
            // capture), so this one reached for F11 without checking. WinForms dispatches a
            // duplicate shortcut to one item, and the later registration won: pressing F11 toggled
            // the leaf box and the window never went full screen.
            //
            // **Three UI tests went red the moment it landed and stayed red**, which is the part
            // worth keeping. The owner spotted it by eye — "the app never went full screen, it did
            // seem to try to start the leaf debug though" — and that sentence names both halves of
            // a collision that no single test could describe.
            //
            // Off the function-key run rather than onto Shift+F11, deliberately: a modified twin of
            // the full-screen key is a mis-press away from the bug this fixes. Ctrl+L is mnemonic
            // for leaf, and the menu shows the binding.
            ("Leaf &box (mat_leafvis)", nameof(DebugModes.LeafVis), Keys.Control | Keys.L),

            // **Ctrl+T, and for the same reason as Ctrl+L above: the function keys are full.** The
            // last of B153's set, and the only one that needed the asset rather than a shader
            // branch — every VTF's thumbnail had been skipped on the way past until now.
            ("Low-res &image (mat_showlowresimage)",
                nameof(DebugModes.ShowLowResImage),
                Keys.Control | Keys.T),
        })
        {
            string which = cvar;

            ToolStripMenuItem item = new(label)
            {
                Name = DebugMenuItemId + which,
                CheckOnClick = true,
                ShortcutKeys = key,
            };

            item.CheckedChanged += (sender, _) =>
            {
                if (sender is not ToolStripMenuItem toggled)
                {
                    return;
                }

                _debug = which switch
                {
                    nameof(DebugModes.DrawFlat) => _debug with { DrawFlat = toggled.Checked },
                    nameof(DebugModes.Luxels) => _debug with { Luxels = toggled.Checked },
                    nameof(DebugModes.NormalMaps) => _debug with { NormalMaps = toggled.Checked },
                    nameof(DebugModes.BumpBasis) => _debug with { BumpBasis = toggled.Checked },
                    _ => _debug with { LeafVis = toggled.Checked },
                };

                _renderLog.LogInformation("{Message}", $"debug views: {_debug}");

                if (_device is { } device)
                {
                    device.Debug = _debug;
                }

                // **Ask for a repaint.** The viewport draws on demand rather than continuously,
                // so updating a shader constant is not enough on its own — the change reaches the
                // GPU and then waits for an unrelated event to show it. That is what made these
                // appear only when the camera moved.
                _viewport.Invalidate();
            };

            _debugMenu.DropDownItems.Add(item);
        }

        _specular.CheckedChanged += (_, _) =>
        {
            _renderLog.LogInformation("{Message}", $"mat_specular {(_specular.Checked ? 1 : 0)}");

            if (_device is { } device)
            {
                device.Specular = _specular.Checked;
            }

            // A repaint, not a world rebuild: this is a shader constant and the geometry is
            // untouched. The rebuild was why reflections appeared instantly while every other
            // debug view waited — it was doing far more work to get the same repaint.
            _viewport.Invalidate();
        };

        // **F12 is bound ONCE, in ProcessCmdKey, and this item only DISPLAYS it.** It carried
        // ShortcutKeys = Keys.F12 as well, so the key was registered twice — by the menu and by the
        // form — and pressing it did nothing at all: no file, no log line, no error. The owner spotted
        // the shape immediately: "if f12 is double bound it wont work".
        //
        // This is the second time in this file. B165 was the same mistake on F11, which silently
        // broke full screen for days. A shortcut belongs to one owner; the other one says so in
        // text.
        ToolStripMenuItem screenshot = new("Save a &screenshot")
        {
            Name = ScreenshotItemId,
            ShortcutKeyDisplayString = "F12",
            AccessibleName = ScreenshotItemName,
            AccessibleDescription = "Writes a picture of the viewport beside the viewer's log.",
        };

        screenshot.Click += (_, _) => CaptureViewportToFile();

        view.DropDownItems.Add(screenshot);
        view.DropDownItems.Add(_wireframe);
        view.DropDownItems.Add(_specular);
        view.DropDownItems.Add(_fullbrightMenu);
        view.DropDownItems.Add(_drawWorld);
        view.DropDownItems.Add(_drawEntities);
        view.DropDownItems.Add(_debugMenu);
        view.DropDownItems.Add(_surfaceColours);
        view.DropDownItems.Add(_frameRate);
        view.DropDownItems.Add(_fullScreen);
        view.DropDownItems.Add(fullScreenMode);
        view.DropDownItems.Add(textureQuality);

        file.DropDownItems.Add(open);
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(exit);
        menu.Items.Add(file);
        menu.Items.Add(view);

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
        string? size = Environment.GetEnvironmentVariable(WindowSizeVariable);
        string? position = Environment.GetEnvironmentVariable(WindowPositionVariable);

        if (!string.IsNullOrWhiteSpace(size))
        {
            string[] parts = size.Split('x', StringSplitOptions.TrimEntries);
            if (parts.Length == 2 &&
                int.TryParse(parts[0], CultureInfo.InvariantCulture, out int width) &&
                int.TryParse(parts[1], CultureInfo.InvariantCulture, out int height) &&
                width > 0 && height > 0)
            {
                Width = width;
                Height = height;
            }
        }

        if (string.IsNullOrWhiteSpace(position))
        {
            return;
        }

        string[] coordinates = position.Split(',', StringSplitOptions.TrimEntries);
        if (coordinates.Length == 2 &&
            int.TryParse(coordinates[0], CultureInfo.InvariantCulture, out int x) &&
            int.TryParse(coordinates[1], CultureInfo.InvariantCulture, out int y))
        {
            StartPosition = FormStartPosition.Manual;
            Location = new Point(x, y);
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
        return ReadMapNamed(mapName);
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

    /// <summary>What went wrong reading the map, for the UI thread to show.</summary>
    /// <remarks>
    /// **A field rather than a direct `_status.Text`, because the read runs off the UI thread now.**
    /// Setting a control property from a worker is the kind of thing that works until the day a
    /// handle exists.
    /// </remarks>
    private string? _mapProblem;

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
        _models.Geometry = EntityModelSet.NoGeometry;

        // **One field where four were cleared, and the fourth was never cleared at all.** `_terrain`
        // and `_overlays` were dropped here while `_brushModels` and `_leaves` were left pointing at
        // the previous map's lumps until the next read replaced them. Nothing read them in between —
        // `ClearMap` is followed immediately by a read — but "correct because of the call order two
        // methods away" is the kind of thing a record makes impossible to get wrong.
        //
        // `_level` was cleared here too until 2026-08-25. It was a SECOND map field left behind by
        // the move that created `LoadedMap`, never assigned anything but null, and `mat_leafvis`
        // read it (B196).
        _reportedNoLeafBox = false;
        _texturesUploaded = false;
        _mapProblem = null;

        // **The models go with the world, because the world owned their buffer.** `ClearWorld`
        // disposes the `WorldRenderer`, and `_modelVertices` is one of its fields — so the packed
        // set that is still in memory has nowhere on the device to live until it is uploaded again
        // (B148).
        _moment.Uploaded = false;

        _device?.ClearWorld();
    }

    /// <summary>Finds and reads a map. Safe off the UI thread once <see cref="ClearMap"/> has run.</summary>
    /// <remarks>
    /// **Verified by reading rather than assumed**: across its hundred and forty lines this path
    /// touches no control, no demo, no timeline and no device — the one exception was a `_status.Text`
    /// assignment in a catch, which now records <see cref="_mapProblem"/> for the UI thread to show.
    /// </remarks>
    private bool ReadMapNamed(string mapName)
    {
        string? path = FindMap(mapName);

        if (path is null)
        {
            // Not on this machine. Fetch it the way joining a server would - in the background,
            // because a 40 MB download must not freeze the window, and the demo is watchable
            // without a map anyway.
            _mapLog.LogInformation("{Message}", $"{mapName} is not installed; fetching it");
            _ = DownloadMapAsync(mapName);
            return false;
        }

        _mapLog.LogInformation("{Message}", $"found {path}");

        return ReadMap(mapName, path);
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
        _status.Text = "Downloading map " + mapName + "...";

        try
        {
            _downloader ??= MapDownloader.Create(MapDownloader.DefaultFolder);

            string? downloaded = await _downloader
                .TryDownloadAsync(mapName, CancellationToken.None)
                .ConfigureAwait(true);

            if (downloaded is null)
            {
                _status.Text = _downloader.DescribeFailure(mapName);
                return;
            }

            if (ReadMap(mapName, downloaded))
            {
                _status.Text = (_demo?.Describe() ?? mapName) + "  (map downloaded)";
            }
        }
        catch (ArgumentException failure)
        {
            // A demo header naming something that is not a map name. The downloader refuses it,
            // and it is not worth failing the load over.
            _status.Text = "Map " + mapName + " could not be fetched: " + failure.Message;
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

            if (_game is null)
            {
                // **Opened once, and everything it provides is set together.** The archives, the
                // editor palette, the class scripts and the item schema all come off disk here and
                // are asked on every frame afterwards — none of it is per-map, and none of it was
                // ever window work (B188, D90).
                //
                // **Every source it feeds is assigned in this one block**, because assignments
                // scattered across a method are how three of them were missed separately (B193). If
                // a collaborator needs something from the install, it is wired here.
                _game = GameContent.Open(FindGameFolder(), _loggers);

                _sounds.Read = _game.Archives.Read;
                _moment.Weapons = _game.Weapons;

                // The soundscape catalog reads from the same archives but belongs to the audio
                // layer, so `GameContent` deliberately does not hold it — Scene would have to
                // reference Audio for an edge that forbids nothing (D92).
                _soundscape.Catalog = _game.Archives.IsEmpty
                    ? null
                    : SoundscapeCatalog.Load(_game.Archives.Read);
            }

            _texturesUploaded = false;

            // **Reading a map is not window work, and now none of it happens here** (B188, D90).
            // What is left is handing the result to the systems that asked for it — which is the
            // `LevelInitPreEntity` shape the engine uses (`igamesystem.h:39`): the window says "here
            // is the map", and each system takes what it needs.
            LoadedMap map = LoadedMap.Read(
                bytes,
                _game,
                _timeline,
                (int)_settings.TextureQuality,
                _surfaceColours.Checked,
                _loggers);

            // **The LEVEL survives a content failure now, and it did not before.** The old catch set
            // `_level = null` alongside `_assets = null`, throwing away lumps that had read
            // perfectly because the TEXTURES did not — so `mat_leafvis` went blank on a map whose
            // BSP tree was fine. `LoadedMap` separates the two: the lumps are read or they throw,
            // and the content is a nullable beside them.
            _loaded = map;
            _mapProblem = map.Problem;

            _moment.Lighting = map.Lighting;
            _models.Geometry = map.Assets is { } content
                ? content.Geometry
                : EntityModelSet.NoGeometry;

            _soundscape.Placements = _soundscape.Catalog is { } loaded
                ? SoundscapePlacements.From(map.Level.Entities, loaded, map.Level.Leaves)
                : null;

            _soundscape.Leaves = map.Level.Leaves;
            _soundscape.Visibility = map.Level.Visibility;

            _soundscape.Clear();

            _audioLog.LogInformation(
                "{Message}",
                map.Level.Visibility is { HasData: true } pvs
                    ? $"visibility: {pvs.ClusterCount.ToString(CultureInfo.InvariantCulture)} " +
                      "clusters, so soundscape selection is restricted to what the listener can see"
                    : "no visibility data, so every soundscape on the map contends");

            _audioLog.LogInformation(
                "{Message}",
                _soundscape.Placements is { } placed
                    ? $"{placed.Placements.Count} soundscape placements, " +
                      string.Join(
                          ", ",
                          placed.Placements
                              .GroupBy(placement => placement.Name)
                              .Select(group => $"{group.Count()}x {group.Key}"))
                    : "no archives, so no soundscapes");

            ProjectMap();
            return !map.Outline.IsEmpty;
        }
        catch (Exception failure) when (failure is IOException or InvalidDataException)
        {
            _status.Text = "Map " + mapName + " could not be read: " + failure.Message;
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
        try
        {
            string? game = FindGameFolder() ?? Tf2ConfigFiles.DefaultGameFolder;

            if (game is null)
            {
                _configLog.LogInformation("{Message}", "no TF2 install found; using the built-in bindings");
                return;
            }

            IReadOnlyList<string> configs = Tf2ConfigFiles.Read(game, _loggers.LogTo());

            if (configs.Count == 0)
            {
                _configLog.LogInformation("{Message}", $"no configs under {game}; using the built-in bindings");
                return;
            }

            _console.Load(configs);
            _bindings = _console.Bindings();

            _configLog.LogInformation(
                "{Message}",
                $"{configs.Count} files, {_console.Applied} of {_console.Bound} binds applied");

            foreach ((ViewerAction action, string key) in _bindings.All())
            {
                _configLog.LogInformation("{Message}", $"  {action,-20} {key}");
            }

            // **The controls their config left unreachable, named rather than left to be noticed.**
            // A key bound to a TF2 command this viewer does not implement — `bind "SHIFT" "+duck"`
            // is the real example — takes that key away from whatever used to answer to it, and the
            // symptom is a control that silently does nothing.
            if (_console.Unbound() is { Count: > 0 } unbound)
            {
                _configLog.LogInformation(
                    "{Message}",
                    $"no key reaches: {string.Join(", ", unbound)} " +
                    "(their config bound those keys to commands this viewer has no equivalent for)");
            }
        }
        catch (Exception failure) when (failure is IOException or ArgumentException
                                            or UnauthorizedAccessException or NotSupportedException)
        {
            _configLog.LogInformation(failure, "could not read the TF2 configs");
        }
    }

    /// <summary>Finds the game's <c>tf</c> folder, for its materials and textures.</summary>
    /// <remarks>
    /// The same Steam library search the map locator uses, stopping one level higher: the locator
    /// wants <c>tf/maps</c> and this wants <c>tf</c> itself, where the archives and the custom
    /// folder live. Null when the game is not installed, which costs the stock textures and
    /// nothing else.
    /// </remarks>
    private static string? FindGameFolder()
    {
        try
        {
            string steam = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Steam", "steamapps", "libraryfolders.vdf");

            return new MapLocator(steam, MapDownloader.DefaultFolder).FindGameFolder();
        }
        catch (Exception failure) when (failure is IOException or ArgumentException)
        {
            return null;
        }
    }

    private static string? FindMap(string mapName)
    {
        try
        {
            string steam = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Steam", "steamapps", "libraryfolders.vdf");

            string ours = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Tf2DemoSalvage", "maps");

            return new MapLocator(steam, ours).Find(mapName);
        }
        catch (ArgumentException)
        {
            // A demo header naming something that is not a map name. The locator refuses it, and
            // it is not worth failing the whole load over.
            return null;
        }
    }

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
        TopDownCamera camera = MapCamera();

        if (map.Assets is not { } assets || map.Level.Surfaces.Count == 0 || _device is null)
        {
            return;
        }

        try
        {
            // **Textures first, and only once per map.** They do not depend on the camera, so a
            // resize needs new vertices and nothing else.
            if (!_texturesUploaded || !_device.HasWorldTextures)
            {
                using (_renderLog.Time("uploading textures"))
                {
                    _device.UploadWorldTextures(assets);
                }

                _texturesUploaded = true;
            }

            // **The camera is a matrix, so a resize is not a rebuild.** The world's vertices are in
            // map coordinates and never move; only the view does. That is what took a viewport
            // change from 0.33 seconds to a 64-byte upload, and it is the reason a free camera or a
            // per-player view can exist at all.
            _device.SetCamera(ViewMatrix(camera), _surfaceColours.Checked, _heightCut);

            // **Logged because this is the whole cost of a resize**, and a rebuild is not. Counting
            // these against "building the world" lines is what proves the geometry survived a
            // viewport change rather than being quietly rebuilt: many camera lines and one build
            // line is the fix working, one of each per resize is not.
            _renderLog.LogInformation(
                "{Message}",
                $"camera set for a {_viewport.ClientSize.Width}x{_viewport.ClientSize.Height} viewport");

            if (_device.HasWorld)
            {
                return;
            }

            MapWorld built;

            using (_renderLog.Time("building the world"))
            {
                built = map.BuildWorld(camera, _loggers);
            }

            _renderLog.LogInformation(
                "{Message}",
                $"world: {built.Vertices.Count} vertices in {built.Batches.Count} material " +
                $"batches for a {_viewport.ClientSize.Width}x{_viewport.ClientSize.Height} viewport");

            _device.UploadWorldGeometry(built);
        }
        catch (Exception failure) when (
            failure is InvalidOperationException or InvalidDataException or IOException)
        {
            _device.ClearWorld();
            _texturesUploaded = false;
            _status.Text = "Textures unavailable: " + failure.Message;
            _renderLog.LogWarning(failure, "{Message}", "uploading the textured world");
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

    /// <summary>Reads each weapon's animation role, once both the demo and the game are open.</summary>
    /// <remarks>
    /// **Lazy because the two things it needs arrive in the opposite order to the obvious one.**
    /// The first version built this beside the timeline, which is where the weapon classes become
    /// known — and the archives are opened AFTER that, so <c>_archives</c> was null every time and
    /// the roles were never read. Nothing failed: every suffix came back null, the lookup fell back
    /// to the primary forms, and the viewer drew exactly what it had drawn before. The unit tests
    /// passed throughout, because they call <c>WeaponRoles</c> directly.
    ///
    /// It was caught by a line missing from the log, which is the only instrument that could have
    /// caught it — the defect is in the wiring, and every component was correct.
    /// </remarks>
    private void EnsureWeaponRoles() =>
        _moment.Appearance = DemoAppearance.Ensure(_moment.Appearance, _timeline, _game, _demoLog);

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

    /// <summary>Where an automatic capture goes; cleared once taken, so it happens once.</summary>
    /// <remarks>
    /// **Mutable where the rest of <see cref="_launch"/> is not**, because this one is not only a
    /// request: taking the shot consumes it. Kept beside the record rather than inside it, since a
    /// record of what was ASKED for should not be edited to record what has been done.
    /// </remarks>
    private string? _shotPath;

    /// <summary>Frames to let the world settle before the opening state is applied.</summary>
    /// <remarks>
    /// **Counted in frames, not seconds**, so it measures settled frames rather than guessing at a
    /// machine. Restarted when a demo loads, because a demo opened from the playlist arrives after
    /// the window did — see the note in `Apply`.
    /// </remarks>
    private const int OpeningFrames = 45;

    private int _shotDelay = OpeningFrames;

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
        if (_shotPath is null && _openingDone)
        {
            return;
        }

        if (_shotDelay-- > 0)
        {
            if (_shotDelay == 40)
            {
                ApplyOpeningState();
            }

            return;
        }

        if (_shotPath is not { } path)
        {
            return;
        }

        _shotPath = null;

        CaptureViewport(path);
        BeginInvoke(Close);
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
    /// <see cref="Apply"/> when a demo finishes loading. <see cref="_openingDone"/> makes the second
    /// of those a no-op.
    /// </remarks>
    private void ApplyOpeningState()
    {
        if (_openingDone || _timeline is null)
        {
            return;
        }

        _openingDone = true;

        // **The clock too, not just the transport.** Moving the camera marks the world stale, and
        // the reprojection that follows re-reads the moment from the clock - so a capture that only
        // told the transport photographed tick zero while every log line said otherwise.
        _clock?.Seek(_launch.ShotTick);
        _transport.ShowTick(_launch.ShotTick);
        ShowMoment(_launch.ShotTick);

        _log.LogInformation("{Message}", $"opening state applied at tick {_launch.ShotTick}");

        if (_launch.SurfaceColours)
        {
            _surfaceColours.Checked = true;
        }

        // **After the seek, because entering the first-person view reads the moment.** The camera
        // is placed from the recorded view or from the followed player at the CURRENT tick, so
        // switching before the clock moves photographs the right mode at the wrong instant — and
        // the picture looks like a camera bug rather than an ordering one.
        if (_launch.FirstPerson)
        {
            _ = ToggleFirstPerson();
        }

        if (_launch.LookAt is { } centre)
        {
            _zoom = _launch.Zoom;
            _lookingAt = centre;
            _worldIsStale = true;
        }
    }

    /// <summary>Whether the opening tick, view and target have been applied.</summary>
    /// <remarks>
    /// Latched rather than inferred from the countdown, because the countdown keeps running after
    /// it reaches zero and re-applying the seek every frame would pin the transport to one tick —
    /// a viewer that cannot be scrubbed, which is the opposite of the point.
    /// </remarks>
    private bool _openingDone;

    /// <summary>The free camera, orbiting whatever the top-down view is centred on.</summary>
    /// <remarks>
    /// **Orbits the same point the map view is looking at**, so toggling between them does not
    /// move the subject — drag the map to a player, switch, and that player is still in the middle.
    ///
    /// The height it orbits is the middle of the map's own vertical range rather than the ground:
    /// a focus at floor level puts half the picture below the world, and the range is already known
    /// because the depth projection needs it.
    /// </remarks>
    /// <summary>The view matrix for whichever camera mode is active.</summary>
    /// <param name="map">The map camera, already built by the caller.</param>
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
    private float[] ViewMatrix(TopDownCamera map)
    {
        if (_firstPerson && FirstPersonCamera() is { } eye)
        {
            return eye.ToMatrix();
        }

        return _freeLook ? FreeLookCamera().ToMatrix() : map.ToMatrix();
    }

    /// <summary>The leaf outline to draw over the world, when that overlay is switched on.</summary>
    /// <param name="matrix">The view-projection the world is drawn with, row major.</param>
    /// <returns>Clip-space segments, or nothing when the mode is off or there is no leaf.</returns>
    /// <remarks>
    /// **What is left here is the TOGGLE, which is view state, and the eye, which the camera owns.**
    /// The tree walk and the projection are <see cref="LeafVis"/>'s — see there for what the box is
    /// and why it is drawn in clip space.
    ///
    /// The origin is the one the free camera flies from, so the box is the leaf the VIEWER is in
    /// rather than the one the recording happens to be looking from.
    ///
    /// **It reads `_loaded` rather than a field of its own, and that is the fix for a regression
    /// this method carried for a day** (B196). A separate `_level` survived the move that created
    /// <see cref="LoadedMap"/> and its assignment did not, so it held null for ever and this drew
    /// nothing on every map. One field, or it drifts.
    /// </remarks>
    private IReadOnlyList<((float X, float Y) From, (float X, float Y) To)> LeafBoxLines(
        float[] matrix)
    {
        if (!_debug.LeafVis)
        {
            return [];
        }

        IReadOnlyList<((float X, float Y) From, (float X, float Y) To)> lines =
            LeafVis.Lines(_loaded?.Level.Leaves, _freeOrigin ?? FreeLookCamera().Origin, matrix);

        // **Says which of the three silences this is, once** (D83). An overlay that is switched on
        // and draws nothing is the exact shape the regression above wore for a day, and it is
        // indistinguishable by eye from standing in a leaf whose box is off screen. Naming the
        // measurement is the difference between a diagnostic and a shrug.
        if (lines.Count == 0 && !_reportedNoLeafBox)
        {
            _reportedNoLeafBox = true;

            _renderLog.LogWarning("{Message}", WhyNoLeafBox());
        }

        return lines;
    }

    /// <summary>Which of the three reasons the leaf outline came back empty.</summary>
    /// <remarks>
    /// **A log must name what it measured.** "no leaf box" is true of all three and useful for
    /// none: a map that never loaded, a map with no BSP tree, and a camera standing in a leaf whose
    /// bounds the lump does not carry are three different problems with three different fixes, and
    /// only the first two are ours.
    /// </remarks>
    private string WhyNoLeafBox()
    {
        if (_loaded is null)
        {
            return "mat_leafvis is on with no map loaded";
        }

        return _loaded.Level.Leaves is null or { IsEmpty: true }
            ? "mat_leafvis is on but the map carried no BSP tree"
            : "mat_leafvis is on but the leaf under the camera has no bounds";
    }

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
            _worldIsStale = true;
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
        _worldIsStale = true;
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

    /// <summary>World units the free camera moves per wheel notch.</summary>
    /// <remarks>
    /// **A distance, unlike flight, because a wheel notch IS a discrete event.** Thirty-two units is
    /// half a player's height. Key-driven flight used to work the same way and could not — a held
    /// key is a duration and became one in <see cref="FreeFlight"/> (B97) — but a notch has no
    /// duration to integrate over.
    /// </remarks>
    private const float FlySpeed = 32f;

    // PlayerEyeHeight (VEC_VIEW, 64) went with FreeFocus on 2026-08-22 (D66): the free camera no
    // longer arrives at a player's eye height above the lowest floor, it arrives above the map
    // looking down. The constant is still correct about Source and is recorded here in case
    // anything wants it again — the first-person camera takes its eye position from the demo
    // rather than from a constant, so nothing does today.

    /// <summary>How wide the empty view is when no map is loaded, in world units.</summary>
    /// <remarks>
    /// Arbitrary and only ever seen as an empty viewport — nothing is drawn without a map. It
    /// exists so the camera is a real camera rather than a special case every caller has to know
    /// about.
    /// </remarks>
    private const float EmptyMapExtent = 1024f;

    private TopDownCamera MapCamera()
    {
        // **`_map` is genuinely null before a demo is opened, and this used to write `_map!`.**
        // Starting the viewer with no map crashed on the first layout with a NullReferenceException
        // out of here — the owner hit it running Viewer3D straight from the IDE, which is the
        // ordinary way to start it with no arguments.
        //
        // That `!` is the pattern this project's standards call a smell: it asserted a fact the
        // code could not support, and the assertion was simply false. Eleven call sites reach this,
        // several from layout and paint handlers that run before anything is loaded, so the answer
        // is a camera over nothing rather than a null every caller has to test.
        (float MinX, float MinY, float MaxX, float MaxY) bounds = _loaded?.Outline is { } loaded
            ? (loaded.MainBounds.MinX, loaded.MainBounds.MinY,
               loaded.MainBounds.MaxX, loaded.MainBounds.MaxY)
            : (-EmptyMapExtent, -EmptyMapExtent, EmptyMapExtent, EmptyMapExtent);

        TopDownCamera fitted = TopDownCamera.Fit(
            [
                (bounds.MinX, bounds.MinY),
                (bounds.MaxX, bounds.MaxY),
            ],
            Math.Max(1, _viewport.ClientSize.Width),
            Math.Max(1, _viewport.ClientSize.Height));

        TopDownCamera zoomed = _zoom > 1f ? fitted.WithZoom(_zoom) : fitted;

        TopDownCamera placed = _lookingAt is { } centre
            ? zoomed.LookingAt(centre.X, centre.Y)
            : zoomed;

        // **D35: the camera projects height, so it has to know the range.** The geometry carries
        // world Z now; without this the third row is a pass-through and every surface lands at a
        // depth of its own world height in units, which is far outside the clip range and draws
        // nothing at all.
        return _loaded?.HeightRange is { } range
            ? placed.WithHeights(range.Lowest, range.Highest)
            : placed;
    }

    /// <summary>Shows a set of world positions in the viewport.</summary>
    /// <param name="positions">World XY positions, in Source units.</param>
    /// <exception cref="ArgumentNullException"><paramref name="positions"/> is null.</exception>
    /// <remarks>
    /// Fits the camera to whatever is passed rather than to the map's bounds, because the map's
    /// bounds are not known until BSP reading exists - and fitting to the entities is what a
    /// viewer wants anyway when a scrub lands on a moment where everyone is in one corner.
    /// </remarks>
    public void ShowPositions(IReadOnlyList<(float X, float Y)> positions) =>
        _scene = MapOverview.Positions(
            positions,
            _loaded,
            MapCamera(),
            _viewport.ClientSize.Width,
            _viewport.ClientSize.Height);

    /// <summary>Draws the whole world at a moment: players and every model-bearing entity.</summary>
    /// <param name="tick">The moment to show, which may fall between ticks.</param>
    /// <remarks>
    /// **One path from "which moment" to "what is drawn".** Scrubbing and playing both come
    /// through here, so the two cannot disagree about what a tick looks like — which they did once
    /// before, when playback and the scrub bar each built the scene their own way.
    ///
    /// Takes a fractional tick rather than a whole one so the interpolation actually reaches the
    /// picture. Truncating here would leave every pose snapped to the last packet and make the
    /// whole interpolation layer a no-op that still passed its own tests.
    /// </remarks>
    public void ShowMoment(double tick)
    {
        if (_timeline is not { } timeline)
        {
            return;
        }

        // **The sampling stays with whoever holds the timeline; everything after it does not.** The
        // scene rebuild is TOLD the tick, the camera and the followed entity through `MomentInfo`,
        // which is `SetupRenderInfo_t`'s arrangement (`clientleafsystem.h:75`) rather than reaching
        // back into a window for them (B188, D90).
        // **Timed here, because `sampling` in the per-second line MEANS this.** For one commit that
        // counter was fed `phases.DrawList` — the draw-list build and its visibility filters, which
        // is a different quantity under the same name. A log that names the wrong thing misdirects
        // with authority (`docs/memory/a-log-must-name-what-it-measured.md`).
        long sampledAt = Stopwatch.GetTimestamp();

        timeline.PlayersAt(tick, _players);
        timeline.PropsAt(tick, _props);

        long sampleTicks = Stopwatch.GetTimestamp() - sampledAt;

        _samplingTicks += sampleTicks;

        // **Outside the timer, which is where it was and where it belongs.** It is cheap after the
        // first call, but the FIRST call reads the weapon scripts out of the archives and each one
        // costs an ICE decryption — so counting it as sampling would report one enormous `sampling`
        // spike for work that is not sampling. Putting it inside was a slip caught by diffing this
        // method against the original.
        //
        // Here rather than earlier because this is the first point where both the demo and the
        // game's archives are certain to be open. It hands the roles to the scene when it reads them.
        EnsureWeaponRoles();

        MomentPhases phases = _moment.Build(
            _players,
            _props,
            new MomentInfo(
                tick,
                _transport.CurrentTick,
                _firstPerson,
                FollowedEntity(),
                _firstPerson ? FirstPersonCamera() : null,
                timeline.IntervalPerTick,
                _settings.ViewmodelFieldOfView));

        _posingTicks += phases.Pose;

        // **Timed because it was, and the column went missing when the rebuild moved out.** Three
        // untimed steps once hid 129 ms of a 133 ms pose (B191), and this is the one still here.
        long playersAt = Stopwatch.GetTimestamp();

        ShowPlayers(_players);

        StallReport.Moment(phases, sampleTicks, Stopwatch.GetTimestamp() - playersAt, _renderLog);
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

    /// <summary>Draws the players recorded at one moment, coloured by team.</summary>
    /// <param name="players">The players, from the timeline.</param>
    /// <exception cref="ArgumentNullException"><paramref name="players"/> is null.</exception>
    /// <remarks>
    /// **Team two is RED and team three is BLU**, which is the engine's own numbering: nought is
    /// unassigned and one is spectator. A player whose team has not arrived yet is drawn grey
    /// rather than guessed at — a wrong team colour is worse than no colour, because it is read as
    /// information.
    /// </remarks>
    public void ShowPlayers(IReadOnlyList<ScenePlayer> players) =>
        _scene = MapOverview.Players(
            players,
            _loaded,
            MapCamera(),
            _viewport.ClientSize.Width,
            _viewport.ClientSize.Height,
            _moment.Appearance);

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
    public string CaptureFolder
    {
        get
        {
            // The log's folder, which is also where captures go — one directory, one retention
            // policy (D83). FileLogWriter owns the path so both writers agree on it.
            string beside = FileLogWriter.DefaultFolder;
            string? wanted = _settings.ScreenshotFolder;

            if (string.IsNullOrWhiteSpace(wanted))
            {
                return beside;
            }

            try
            {
                Directory.CreateDirectory(wanted);
                return wanted;
            }
            catch (Exception failure) when (
                failure is IOException or UnauthorizedAccessException or ArgumentException
                    or NotSupportedException)
            {
                _renderLog.LogWarning(failure, "{Message}", $"cannot write captures to {wanted}");
                return beside;
            }
        }
    }

    /// <summary>Captures the viewport to a stamped file beside the log.</summary>
    /// <remarks>
    /// The one place that decides where a screenshot goes, so the menu item and F12 cannot
    /// disagree about it. They were one copied expression apart, which is exactly how the viewer's
    /// two drawing paths drifted until one of them stopped showing decals.
    /// </remarks>
    public void CaptureViewportToFile()
    {
        string folder = CaptureFolder;

        CaptureViewport(Path.Combine(folder, CaptureName(DateTime.Now)));

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
        FileRetention.Keep(folder, "shot-*.png", CapturesKept);
    }

    /// <summary>What a capture taken at a given moment is called.</summary>
    /// <param name="when">When the capture was taken.</param>
    /// <returns>The file name, without a directory.</returns>
    /// <remarks>
    /// **Milliseconds, because seconds were not enough.** Two captures taken in the same second
    /// overwrote each other — measured 2026-08-20 while capturing the map view and the first-person
    /// view to compare them, 328 milliseconds apart, both landing in
    /// <c>shot-20260820-000241.png</c>. A second is not a long time for somebody pressing a key
    /// twice and it is no time at all for a UI test.
    ///
    /// It was noticed only because <c>SaveBackBuffer</c> had started logging what it wrote an hour
    /// earlier. Without that line the run reports success, one file exists, and nothing says which
    /// of the two views it holds.
    ///
    /// **Ordinal name order has to stay chronological**, because <see cref="FileRetention"/>
    /// decides what to delete by sorting the names — a stamp whose text order disagreed with its
    /// time order would keep the wrong captures and would do it silently, since the count would
    /// still come out right. A fixed-width, most-significant-first stamp is what guarantees that.
    ///
    /// Taken as a parameter rather than read inside, so the naming can be tested without waiting
    /// for a clock.
    /// </remarks>
    public static string CaptureName(DateTime when) =>
        string.Create(CultureInfo.InvariantCulture, $"shot-{when:yyyyMMdd-HHmmss-fff}.png");

    /// <summary>How many F12 captures to keep before the oldest are deleted.</summary>
    /// <remarks>
    /// Twenty rather than the logs' fifty, purely on size: a viewport capture is close to a
    /// megabyte where a run's log is tens of kilobytes.
    /// </remarks>
    private const int CapturesKept = 20;

    /// <summary>The points the viewport is currently drawing.</summary>
    public IReadOnlyList<ScenePoint> Scene => _scene;

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
        _loadsRequested++;

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
        int ticket = ++_loadsRequested;

        _status.Text = "Opening " + Path.GetFileName(path) + "...";

        try
        {
            ILogger demoLog = _demoLog;
            DecodedDemo decoded =
                await Task.Run(() => DecodedDemo.Read(path, demoLog)).ConfigureAwait(false);

            if (ticket != _loadsRequested)
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
                    bool found = ReadMapNamed(decoded.Demo.MapName);

                    // **Packed here rather than when a prop first appears, which is what Valve
                    // does** (D86). `CBaseEntity::PrecacheModel` sits behind `IsPrecacheAllowed()`
                    // and warns on an out-of-order precache: the engine loads models at level load,
                    // deliberately, so nothing is decoded mid-game.
                    //
                    // Ours was packing on sight, and it cost 385 ms in a single frame the first time
                    // a crowd of props came into view — measured 2026-08-24. Inside this Task.Run
                    // and before `_readingMap` is cleared, so it runs on the worker behind the
                    // barrier that already holds the render loop off during a map read (B146).
                    PrecacheModels(decoded.Timeline);

                    // Same timing, same reason, and the audio path is where the cost actually
                    // landed once the model stalls were gone.
                    PrecacheSounds(decoded.Timeline);

                    return found;
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

            return OnUi(() => ticket == _loadsRequested
                ? Apply(decoded, read)
                : Superseded(_demoLog, path));
        }
        catch (Exception failure) when (failure is IOException or InvalidDataException)
        {
            return OnUi(() => ticket == _loadsRequested
                ? CouldNotOpen(path, failure)
                : Superseded(_demoLog, path));
        }
    }

    /// <summary>Says a load was overtaken, without touching anything.</summary>
    // The logger is a parameter because this is static (D83).
    private static DemoLoadResult Superseded(ILogger demoLog, string path)
    {
        string message = $"discarding {Path.GetFileName(path)}: a newer demo was asked for";

        demoLog.LogInformation("{Message}", message);

        return new DemoLoadResult(DemoLoadOutcome.Superseded, message);
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

    /// <summary>How many loads have been asked for, so a stale one can tell.</summary>
    private int _loadsRequested;

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

        // **Every per-demo source, set in ONE place, because two of them were missed separately.**
        // `MomentScene.Viewmodels` was never assigned at all when the scene rebuild moved out — so
        // `AddViewmodel` returned on its first guard and the first-person weapon simply never drew,
        // with the viewer suite green throughout. That is B193's shape for the second time in three
        // commits, and the answer is that a demo's sources are assigned together where the demo
        // arrives rather than wherever each one happened to be constructed.
        _spectator.Eyes = _timeline is { } eyes ? new TimelineEyes(eyes) : null;
        _moment.Viewmodels = _timeline is { } weapons ? new TimelineViewmodels(weapons) : null;

        // **The sounds this recording plays, ready before the first frame asks.** Rebuilt per demo
        // rather than kept: a schedule holds a cursor into one timeline's list, and carrying it
        // across a load would index the previous demo's sounds.
        _sound.Schedule = _timeline is { } withSound ? new SoundSchedule(withSound.Sounds) : null;
        _audio?.StopAll();
        _loops.Clear();

        _audioLog.LogInformation(
            "{Message}",
            $"{_timeline?.Sounds.Count ?? 0} sounds on the timeline; " +
            (_audio is null ? "no audio device, so none will play" : "output is open"));

        _transport.SetDemoLength(_demo.LastTick);

        // **The appearance is FORGOTTEN here, not rebuilt, and the first attempt rebuilt it.** The
        // archives are opened later than this, so building now reads nothing — and caches that
        // nothing for the life of the demo. `DemoAppearance.Ensure` fills it in on the first moment
        // that can actually answer, which is why this is a reset rather than a build.
        _moment.Appearance = DemoAppearance.None;

        if (_timeline is { } timeline)
        {
            // **The rate the recording server ran, not a constant.** It is a server setting, so
            // a box left at its default runs 33 where a configured one runs 66, and replaying
            // at the wrong rate reads as a slow or fast server rather than as a defect.
            _clock = new PlaybackClock(timeline.IntervalPerTick, _demo.LastTick);

            // The presenter owns playback over this clock from here (D62).
            _playback.Load(_clock);

            // **Playback can be started by the environment, for measurement — and it has to
            // happen HERE, after the clock exists.** A demo's first tick is before the match
            // begins: no capture points, no holograms, nobody carrying anything. A
            // launch-and-log run, which is the only way to ask the renderer a question with
            // nobody driving it, therefore measures an almost empty scene and reports "never
            // drawn" for models that simply had not appeared yet.
            //
            // Set before this line it does nothing but look right, which is exactly what it
            // did: PlayingChanged starts the stopwatch only `if (playing && _clock is not
            // null)`, so the button showed playing while no time was fed to a clock that did
            // not exist, and the demo sat still until the user paused and played again.
            if (Environment.GetEnvironmentVariable(AutoPlayVariable) is { Length: > 0 })
            {
                // **Through the presenter, not by assigning the flag** — which is what this line
                // used to do, and it did not start anything. `TransportBar.Playing`'s setter
                // deliberately does not raise `PlayPauseToggled` (it would make the presenter
                // re-enter its own handler), so assigning it relabelled the button and left the
                // elapsed clock stopped. The owner: "the ui says its playing but the demo is not
                // actually playing, no ticks go by, i have to 'pause' which does nothing then hit
                // play again to get it started".
                //
                // The remark below this was already about that fault being found once. It came back
                // because nothing tested it — "we dont actually check playback in the ui tests".
                _playback.Play();

                _demoLog.LogInformation("{Message}", $"{AutoPlayVariable} is set; playback started at load");
            }
        }
        else
        {
            _clock = null;
        }

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
        PrecacheModels(_timeline);

        // Cheap to call twice for the same reason models are: `Sample` returns the cached decode,
        // so on the async path this finds the work already done.
        PrecacheSounds(_timeline);

        _status.Text = _mapProblem
            ?? (_demo.Describe() + (haveMap ? string.Empty : "  (map not found)"));

        // The first frame, so opening a demo shows the players standing where they started
        // rather than an empty map waiting for someone to press play.
        ShowPlayers(_timeline?.PlayersAt(_timeline.FirstTick) ?? []);

        // **Restart the settling countdown, rather than applying the opening state here.** A demo
        // opened from the playlist arrives long after the frame the countdown used to fire on, so
        // the state was being lost — but applying it at this instant is worse than useless: the
        // world has not settled, the textures are uploaded on a later frame, and the seek lands in a
        // scene that is not ready and then latches itself done.
        //
        // Restarting keeps the original reasoning intact — the countdown exists so the map, its
        // textures and the entity models are all in place first — and simply measures it from the
        // demo rather than from the window.
        if (!_openingDone)
        {
            _shotDelay = OpeningFrames;
        }

        return new DemoLoadResult(DemoLoadOutcome.Loaded, _status.Text);
    }

    /// <summary>Puts the form back into a state with no demo, and says why. UI thread only.</summary>
    private DemoLoadResult CouldNotOpen(string path, Exception failure)
    {
        _demo = null;
        _timeline = null;
        _clock = null;
        _scene = [];
        _transport.SetDemoLength(0);
        _status.Text = "Could not open " + System.IO.Path.GetFileName(path) + ": " + failure.Message;

        _demoLog.LogWarning(failure, "{Message}", $"opening {System.IO.Path.GetFileName(path)}");

        return new DemoLoadResult(DemoLoadOutcome.Failed, _status.Text);
    }

    /// <summary>The playback controls, exposed for the tests that address them.</summary>
    public TransportBar Transport => _transport;

    /// <summary>Whether the viewport is filling the screen.</summary>
    /// <summary>How full screen is entered.</summary>
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
        _borderlessMode.Checked = mode == FullScreenMode.Borderless;
        _exclusiveMode.Checked = mode == FullScreenMode.Exclusive;

        if (IsFullScreen && _device is not null)
        {
            bool wanted = mode == FullScreenMode.Exclusive;

            if (!_device.SetExclusiveFullScreen(wanted) && wanted)
            {
                _status.Text = "Exclusive full screen was refused; using borderless.";
            }
        }

        string? failure = _settings.Save();

        if (failure is not null)
        {
            // Reported rather than swallowed: a preference that silently does not stick is worse
            // than one that says so.
            _status.Text = "Setting saved for this session only: " + failure;
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

        foreach (KeyValuePair<TextureQuality, ToolStripMenuItem> item in _textureQualityItems)
        {
            item.Value.Checked = item.Key == quality;
        }

        string? failure = _settings.Save();

        _status.Text = failure is null
            ? "Texture quality: " + quality + ". Applies to the next map opened."
            : "Setting saved for this session only: " + failure;
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
        _fullScreen.Checked = fullScreen;
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
                _status.Text = "Exclusive full screen was refused; using borderless.";
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

    /// <summary>Moves playback on by however long the last frame took.</summary>
    /// <remarks>
    /// **Nothing is invalidated here.** The idle loop this runs inside already draws every frame,
    /// and asking for a repaint as well is what made the mouse sluggish over the transport
    /// buttons - paint messages queued faster than the pump could drain them.
    /// </remarks>
    private void AdvancePlayback() => _playback.Advance();

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
            if (FrameIsDue())
            {
                // **Counted only when something was drawn.** RenderFrame declines during a map
                // read, and counting those turned the per-second report into a measurement of how
                // fast an empty loop spins — 186 "frames a second" with every duration at zero.
                if (RenderFrame())
                {
                    CountFrame();
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
        _idleBursts++;
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

    /// <summary>How many times the render loop yielded since the last report.</summary>
    private long _idleBursts;

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

    /// <summary>Times the camera's frames, which run whether or not the demo is playing.</summary>
    private readonly Stopwatch _flyWatch = Stopwatch.StartNew();

    /// <summary>The key-release filter, kept so it can be removed on shutdown.</summary>
    private KeyReleaseFilter? _keyReleases;

    /// <summary>The longest frame since the rate was last reported, in seconds.</summary>
    private double _longestFrameSeconds;

    /// <summary>How long the last frame took, in seconds.</summary>
    /// <remarks>
    /// **The meter reads this rather than keeping its own clock** (B174). Two clocks measuring the
    /// frame rate is two answers to the question the meter exists to settle, and the camera's is
    /// already the authoritative one — it is what the flight speed is scaled by.
    /// </remarks>
    private double _lastFrameSeconds;

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

    /// <summary>Stopwatch ticks spent sampling the timeline since the last report.</summary>
    /// <remarks>
    /// **Split from posing because twenty milliseconds is a budget, not an answer** (B99). Paused,
    /// the viewer draws the whole uncalled map at 300 frames a second with a longest frame of
    /// 3.4 ms; playing, it manages 48. That difference is all CPU and all in this rebuild, and
    /// which half of the rebuild owns it decides what to fix.
    /// </remarks>
    private long _samplingTicks;

    /// <summary>Stopwatch ticks spent posing and lighting models since the last report.</summary>
    private long _posingTicks;

    /// <summary>Time spent in the device draw since the last report (B148).</summary>
    private long _drawTicks;

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

    /// <summary>When the last frame was presented.</summary>
    private long _lastFrameAt;

    /// <summary>Whether enough time has passed to draw another frame.</summary>
    /// <remarks>
    /// **The cap has to be applied here, because asking for vertical sync does not work.** The
    /// swap chain presents with a sync interval of one and the viewer was still measured at about
    /// 600 frames a second: a driver forcing vsync off globally outranks the present call. So the
    /// only ceiling that holds is one this program keeps itself.
    ///
    /// **This does not affect what is drawn, only how often.** The animation cycle is advanced
    /// from DEMO time - the tick and the demo's own interval - never from frame time, so a demo
    /// looks identical at 24 frames a second and at 300. That separation is the thing GoldSrc got
    /// wrong: tying movement to frame time made a player's speed depend on their frame rate, and
    /// advancing a cycle per rendered frame here would have made every animation slow down the
    /// moment a cap was applied.
    /// </remarks>
    private bool FrameIsDue()
    {
        if (_settings.FrameRateLimit <= 0)
        {
            return true;
        }

        long now = Stopwatch.GetTimestamp();

        if (_lastFrameAt == 0)
        {
            _lastFrameAt = now;
            return true;
        }

        double budget = 1d / _settings.FrameRateLimit;

        if ((now - _lastFrameAt) / (double)Stopwatch.Frequency < budget)
        {
            return false;
        }

        _lastFrameAt = now;
        return true;
    }

    /// <summary>How long a sleep of one millisecond actually takes, near enough.</summary>
    /// <remarks>
    /// **Windows does not sleep for a millisecond when asked.** The default timer granularity is
    /// about 15.6 milliseconds, so <c>Thread.Sleep(1)</c> returns after a whole tick of it - and a
    /// frame limiter built on that caps at about 64 frames a second whatever it was asked for.
    /// Measured exactly that way: a limit of 300 produced 63 to 66.
    /// </remarks>
    private const double SleepGranularitySeconds = 0.016;

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
        if (_settings.FrameRateLimit <= 0)
        {
            return;
        }

        double budget = 1d / _settings.FrameRateLimit;
        double waited = (Stopwatch.GetTimestamp() - _lastFrameAt) / (double)Stopwatch.Frequency;

        if (budget - waited > SleepGranularitySeconds)
        {
            Thread.Sleep(1);
            return;
        }

        Thread.Yield();
    }

    /// <summary>How many frames were drawn since the rate was last reported.</summary>
    private int _framesDrawn;

    /// <summary>When the frame rate was last reported.</summary>
    private long _rateReportedAt;

    /// <summary>Reports the frame rate once a second.</summary>
    /// <remarks>
    /// **Measured rather than assumed, because the answer is a claim about this machine.** The
    /// swap chain presents with a sync interval of one, so the rate should sit at the display's
    /// refresh - and "should" is exactly the kind of statement this project keeps finding wrong.
    /// A rate well under refresh means a frame is costing more than its slice, which is worth
    /// knowing before anyone starts optimising by guesswork.
    ///
    /// Once a second, so a log covering a whole session stays readable.
    /// </remarks>
    /// <summary>Collections and pause time since the last report, or empty when there were none.</summary>
    /// <remarks>
    /// **The instrument for a stall that is not a frame rate drop**, which is the owner's exact
    /// description of B163: *"the stutter isnt in engine fps, its stutter across the whole app, the
    /// fps doesnt drop, everything freezes for a half a second to maybe a second sometimes"*.
    ///
    /// A blocking gen2 collection does precisely that. It suspends every managed thread, so the
    /// window stops pumping and nothing is drawn — and because the frames on either side are as fast
    /// as ever, the AVERAGE rate barely moves. That is why an fps counter alone cannot see it and
    /// why the pair of numbers matters more than either alone.
    ///
    /// <c>GC.GetTotalPauseDuration()</c> is the runtime's own accounting of time spent with threads
    /// suspended, so this is not an inference from a gap in the log — it is the pause, reported by
    /// the thing that caused it.
    ///
    /// Printed only when something happened, so a quiet second stays one line.
    /// </remarks>
    private string GarbageThisSecond()
    {
        int gen0 = GC.CollectionCount(0);
        int gen1 = GC.CollectionCount(1);
        int gen2 = GC.CollectionCount(2);
        TimeSpan paused = GC.GetTotalPauseDuration();

        (int Gen0, int Gen1, int Gen2, TimeSpan Paused) since = (
            gen0 - _collections.Gen0,
            gen1 - _collections.Gen1,
            gen2 - _collections.Gen2,
            paused - _collections.Paused);

        _collections = (gen0, gen1, gen2, paused);

        if (since is { Gen0: 0, Gen1: 0, Gen2: 0 } && since.Paused < TimeSpan.FromMilliseconds(1))
        {
            return string.Empty;
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"; gc {since.Gen0}/{since.Gen1}/{since.Gen2} paused {since.Paused.TotalMilliseconds:0.#} ms");
    }

    /// <summary>Collection counts and pause time as of the last report.</summary>
    private (int Gen0, int Gen1, int Gen2, TimeSpan Paused) _collections;

    private void CountFrame()
    {
        _framesDrawn++;

        long now = Stopwatch.GetTimestamp();

        if (_rateReportedAt == 0)
        {
            _rateReportedAt = now;
            return;
        }

        double elapsed = (now - _rateReportedAt) / (double)Stopwatch.Frequency;

        if (elapsed < 1d)
        {
            return;
        }

        // **The worst frame, not just the average, because jitter is a spread and a mean hides
        // it.** Flying the camera used to re-project the whole map every frame (B98); the average
        // barely moved while the longest frame in each second grew enormously, which is exactly
        // what stutter is. A rate on its own could not have shown that, and did not.
        _renderLog.LogDebug(
            "{Message}",
            $"{_framesDrawn / elapsed:0.#} frames a second, " +
            $"longest {_longestFrameSeconds * 1000d:0.##} ms" +
            (_transport.Playing ? ", playing" : ", paused") +
            (_freeLook && _console.AnyHeld ? ", flying" : string.Empty) +
            $"; drawing {_drawTicks / (double)Stopwatch.Frequency * 1000d:0.#} ms" +
            $"; yielded {_idleBursts} times to {MessageName(_idleEndedBy)}" +
            $"; sampling {_samplingTicks / (double)Stopwatch.Frequency * 1000d:0.#} ms" +
            $", posing {_posingTicks / (double)Stopwatch.Frequency * 1000d:0.#} ms" +
            $" (lighting {_models.LightingTicks / (double)Stopwatch.Frequency * 1000d:0.#} ms)" +
            " of the second" +
            GarbageThisSecond());

        _models.LightingTicks = 0;

        _framesDrawn = 0;
        _rateReportedAt = now;
        _longestFrameSeconds = 0d;
        _samplingTicks = 0;
        _posingTicks = 0;
        _idleBursts = 0;
        _drawTicks = 0;
    }

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

        _device.SetCamera(
            ViewMatrix(MapCamera()),
            _surfaceColours.Checked,
            _heightCut);
    }

    // **`ReportSlowFrame` was here until 2026-08-25** (B188, D90). It is `StallReport.Frame`, and
    // its eight timestamp parameters are now a `FramePhases` record built by `FramePhases.Between`
    // — the same correction `MomentPhases` already carried. Eight `long`s in the right order is a
    // signature that can be called in the wrong one, and the failure is silent: seven plausible
    // numbers against the wrong labels.
    //
    // The reasoning is worth keeping, because it is why the ledger exists at all. It was built
    // after four rounds of one-suspect-at-a-time, three right and one wrong: the model upload, the
    // model packing and the per-frame logging were each found by instrumenting a hypothesis, and
    // the sound decode was instrumented on the same reasoning and recorded nothing. Proposing
    // suspects does not converge. A ledger over the whole frame does, because the RESIDUAL column
    // is the part nobody has thought to measure — and a frame that is slow with every named column
    // small says exactly that. `unaccounted` is Valve's own name for it.

    /// <summary>Packs every model the demo will ever show, before playback starts.</summary>
    /// <param name="timeline">The decoded timeline, or null when the demo carried none.</param>
    /// <remarks>
    /// **The engine's own timing** (D86). `CBaseEntity::PrecacheModel` is guarded by
    /// `IsPrecacheAllowed()` and warns on an out-of-order precache, because Source loads models at
    /// level load and not on sight. Packing when a prop first becomes visible is what cost 385 ms in
    /// one frame, and an asynchronous load would only move the hitch rather than remove it — the
    /// first appearance would still wait.
    ///
    /// **The timeline is a better list than `modelprecache`**, which is what the engine uses. The
    /// table names what the server precached, including models this recording never shows; the
    /// tracks name what actually appears. Both are known before the first frame, which is the part
    /// that matters.
    ///
    /// **Runs on the map-read worker**, so it costs nothing on the UI thread — the packing is CPU
    /// work over geometry the map read has already decoded into <c>_assets.EntityModels</c>, so by
    /// here it touches no files.
    ///
    /// A failure costs the precache and nothing else: anything missed is packed on sight exactly as
    /// before, which is slower rather than broken.
    /// </remarks>
    private void PrecacheModels(DemoTimeline? timeline)
    {
        if (timeline is null || _game is null)
        {
            return;
        }

        try
        {
            long packedAt = Stopwatch.GetTimestamp();

            _models.Precache(DemoModels.ToPack(timeline, _game));

            double packedSeconds =
                (Stopwatch.GetTimestamp() - packedAt) / (double)Stopwatch.Frequency;

            _renderLog.LogInformation(
                "{Message}",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"precached models in {packedSeconds * 1000d:0} ms " +
                    $"({_models.Count} packed, {_models.Vertices.Count} vertices)"));
        }
        catch (Exception failure) when (
            failure is InvalidDataException or ArgumentException or KeyNotFoundException)
        {
            _renderLog.LogWarning(failure, "precaching models");
        }
    }

    /// <summary>Decodes every sound the demo will play, before playback starts.</summary>
    /// <param name="timeline">The decoded timeline, or null when the demo carried none.</param>
    /// <remarks>
    /// **The engine's own timing, and here it is a refusal rather than a preference** (D86, D87).
    /// `CBaseEntity::PrecacheSound` opens with `if ( !CBaseEntity::IsPrecacheAllowed() )` and then
    /// `Assert( !"CBaseEntity::PrecacheSound:  too late" )` — `SoundEmitterSystem.cpp:1497`. Loading
    /// a sound during play is something Source treats as a programming error, and it passes
    /// `bPreload: true` to `enginesound->PrecacheSound` at `:1507`.
    ///
    /// **What this method is, now that <see cref="SoundCache.Precache"/> does the decoding: the
    /// LIST.** Which sounds are worth having ready is the only part that needs a demo and a map —
    /// the measurement that made a precache necessary lives with the code that acts on it.
    ///
    /// **Runs on the map-read worker on the async path**, where `_readingMap` holds the render loop
    /// off (B146) — so nothing else touches the cache while this fills it.
    ///
    /// A failure costs the precache and nothing else: anything missed is decoded on first play
    /// exactly as before, which is slower rather than broken.
    /// </remarks>
    private void PrecacheSounds(DemoTimeline? timeline)
    {
        if (timeline is null || _game is null)
        {
            return;
        }

        try
        {
            // **The map's ambience is listed alongside the demo's own sounds, because no demo
            // message names it.** A soundscape's loops come from the map's `env_soundscape`
            // entities via `scripts/soundscapes.txt`, so the timeline cannot list them and the
            // first pass here missed them entirely: measured 2026-08-25, `ambient/indoors.wav`
            // still cost 103 ms in one frame after the timeline's 395 sounds were already
            // precached.
            //
            // Every soundscape in the catalog rather than the ones this recording enters — which
            // soundscape is active changes as a player walks and a seek can land anywhere, so being
            // selective would only move the hitch to the next doorway.
            PrecacheResult result = _sounds.Precache(
                timeline.SoundsToPrecache()
                    .Concat(_soundscape.Catalog?.WaveNames() ?? []));

            _audioLog.LogInformation(
                "{Message}",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"precached {result.Decoded} of {result.Named} sounds " +
                    $"in {result.Seconds * 1000d:0} ms"));
        }
        catch (Exception failure) when (
            failure is InvalidDataException or ArgumentException or KeyNotFoundException)
        {
            _audioLog.LogWarning(failure, "precaching sounds");
        }
    }

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
    private IReadOnlyList<HudQuad> BuildOverlay()
    {
        _overlayQuads.Mode = _settings.ShowFrameRate;

        if (_overlayQuads.NeedsAtlas)
        {
            EnsureOverlayAtlas();
        }

        return _overlayQuads.Quads(
            _hudAtlas, _viewport.ClientSize.Width, _demo?.MapName, _lastFrameSeconds);
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
        double seconds = _flyWatch.IsRunning ? _flyWatch.Elapsed.TotalSeconds : 0d;
        _flyWatch.Restart();

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
        _longestFrameSeconds = Math.Max(_longestFrameSeconds, seconds);
        _lastFrameSeconds = seconds;

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
        FreeCamera? camera = _firstPerson ? FirstPersonCamera() : FreeLookCamera();

        if (camera is not { } ears)
        {
            return;
        }

        float yaw = ears.Angles.Yaw * (MathF.PI / 180f);
        (float X, float Y, float Z) right = (MathF.Sin(yaw), -MathF.Cos(yaw), 0f);
        (float X, float Y, float Z) listener = (ears.Origin.X, ears.Origin.Y, ears.Origin.Z);

        long soundAt = Stopwatch.GetTimestamp();

        SoundPhases phases = _sound.Update(
            output,
            _transport.CurrentTick,
            listener,
            right,
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
    /// **The return value exists because <see cref="CountFrame"/> was counting frames this method
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
        long frameAt = Stopwatch.GetTimestamp();

        PlaySounds();

        long soundedAt = Stopwatch.GetTimestamp();

        FlyCamera();

        // **Every frame, because the view can change without anything here being told.** This used
        // to be sent only from FlyCamera, and only when the free camera had actually moved — so in
        // the first-person view the recorded camera advanced every tick, produced a correct matrix,
        // and never reached the GPU. The owner: "pov isnt actually updating cam position at the
        // tick rate either, the only way to get the cam in pov to move is by clicking and moving
        // the mouse", which is mouse-look going through the flight path that does upload.
        //
        // Same shape as the debug views not appearing until the camera moved, and the same lesson:
        // a value computed correctly and not sent is indistinguishable from one computed wrongly.
        // Uploading unconditionally costs one 112-byte constant write per frame and removes the
        // whole class — a spectator switch, a scrub, a demo change and playback itself all move the
        // view without touching FlyCamera.
        UploadCamera();

        long flownAt = Stopwatch.GetTimestamp();

        // **Reprojected here rather than in the resize handler**, which is what coalesces a burst
        // of resizes into one rebuild. Idle runs when the message queue empties, so every layout
        // step of a full-screen transition - or every pixel of a window drag - is collapsed into
        // the single size that was current when the pump went quiet.
        if (_worldIsStale)
        {
            _worldIsStale = false;
            ProjectMap();

            // **The scene is projected too, so a camera change invalidates it as well.** Points
            // are stored in screen space while the world's vertices are not, so rebuilding one and
            // not the other left every dot at the pixel it had before the zoom while the map moved
            // underneath it. Playback hid this by rebuilding the scene every frame regardless; it
            // only showed while paused.
            //
            // Done here rather than beside each camera change: five places already set this flag,
            // and the next one added would have had the same bug again.
            ReprojectScene();
        }

        long projectedAt = Stopwatch.GetTimestamp();

        AdvancePlayback();

        long advancedAt = Stopwatch.GetTimestamp();

        TakeAutomaticShot();

        long shotAt = Stopwatch.GetTimestamp();

        // **Timed because everything else in a frame already was, and none of it accounted for
        // B148.** After a demo switch the viewer reports 20 frames a second with sampling, posing
        // and lighting all at zero — so the hundred milliseconds are somewhere none of those three
        // counters could see, and this is the only step left.
        IReadOnlyList<HudQuad> hud = BuildOverlay();

        long hudAt = Stopwatch.GetTimestamp();

        long drewAt = Stopwatch.GetTimestamp();

        _device?.DrawFrame(
            BackgroundRed,
            BackgroundGreen,
            BackgroundBlue,
            // Empty, always. The flat fill was drawn only when there was no textured map and was
            // built from the map, so it was dead in both branches (see ProjectMap). The parameter
            // stays until Device3D's signature is revised, which is a change to the render seam and
            // not to this frame.
            [],
            // **The line channel now carries mat_leafvis instead of the brush outline.** Those were
            // the BSP's own edge segments projected for the overhead view, and `mat_wireframe`
            // replaced them; the channel itself is exactly what a leaf box wants — clip-space
            // segments drawn over the world without depth, which is what an annotation should be.
            //
            // The projected outline is gone entirely now (B151 closed by deletion rather than by
            // the split it proposed): nothing read it, and 615 ms of a 679 ms frame was spent
            // building it. `MapOutline` still supplies the play-area bounds, which is the half that
            // was actually wanted.
            LeafBoxLines(ViewMatrix(MapCamera())),
            _scene,
            _moment.Instances,
            _moment.ViewmodelInstances,
            _moment.ViewmodelCamera?.ToMatrix(),
            hud);

        long finishedAt = Stopwatch.GetTimestamp();

        _drawTicks += finishedAt - drewAt;

        StallReport.Frame(
            FramePhases.Between(
                frameAt, soundedAt, flownAt, projectedAt, advancedAt, shotAt, hudAt, finishedAt),
            _renderLog);

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

        float step = e.Delta > 0 ? 1.25f : 1f / 1.25f;

        // In the free view the wheel moves the camera in and out instead of magnifying a flat map.
        // The near limit is a little over a player's height, so a model can be filled the frame
        // with without the near plane cutting into it.
        // In the free view the wheel flies forward and back, which is what a wheel does in every
        // editor and is far quicker than tapping W across a map.
        if (_freeLook)
        {
            (float sinPitch, float cosPitch) = MathF.SinCos(_freeAngles.Pitch * (MathF.PI / 180f));
            (float sinYaw, float cosYaw) = MathF.SinCos(_freeAngles.Yaw * (MathF.PI / 180f));

            float travel = e.Delta > 0 ? FlySpeed * 4f : -FlySpeed * 4f;
            (float X, float Y, float Z) where = _freeOrigin ?? FreeLookCamera().Origin;

            _freeOrigin = (
                where.X + (cosPitch * cosYaw * travel),
                where.Y + (cosPitch * sinYaw * travel),
                where.Z + (-sinPitch * travel));

            _worldIsStale = true;
            _viewport.Invalidate();
            return;
        }

        float zoomed = Math.Clamp(_zoom * step, 1f, 64f);

        if (Math.Abs(zoomed - _zoom) < float.Epsilon)
        {
            return;
        }

        // The world point under the cursor before the zoom has to stay under it afterwards, which
        // fixes where the new centre must be.
        (float worldX, float worldY) = WorldAt(e.Location);

        _zoom = zoomed;

        (float afterX, float afterY) = WorldAt(e.Location);

        (float centreX, float centreY) = MapCamera().Centre;

        _lookingAt = (centreX + (worldX - afterX), centreY + (worldY - afterY));

        _worldIsStale = true;
    }

    /// <summary>Rebuilds the projected scene after the camera has moved.</summary>
    /// <remarks>
    /// Uses the clock's position when there is one, so a paused viewer reprojects the moment it is
    /// actually showing rather than jumping to the scrub bar's whole tick.
    /// </remarks>
    private void ReprojectScene()
    {
        if (_timeline is null)
        {
            return;
        }

        ShowMoment(_clock?.Position ?? _transport.CurrentTick);
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

        // **In the free view a drag turns the camera rather than moving the map.** Pitch is
        // clamped by the camera itself, at the same 89 degrees the engine clamps a player to,
        // because the basis is degenerate looking exactly along the world's up axis.
        if (_freeLook)
        {
            _freeAngles = (
                Math.Clamp(_freeAngles.Pitch + ((e.Location.Y - from.Y) * DegreesPerPixel), -89f, 89f),
                _freeAngles.Yaw - ((e.Location.X - from.X) * DegreesPerPixel));

            _dragFrom = e.Location;
            _worldIsStale = true;
            return;
        }

        float perPixel = MapCamera().WorldUnitsPerPixel(_viewport.ClientSize.Width);
        (float centreX, float centreY) = MapCamera().Centre;

        // Y is inverted between the screen and the world, the same flip the projection makes.
        _lookingAt = (
            centreX - ((e.Location.X - from.X) * perPixel),
            centreY + ((e.Location.Y - from.Y) * perPixel));

        _dragFrom = e.Location;
        _worldIsStale = true;
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
        if (!_firstPerson || _timeline is not { } timeline)
        {
            return;
        }

        IReadOnlyList<ScenePlayer> players = timeline.PlayersAt(_transport.CurrentTick);

        if (SpectatorTarget.Next(
                players, _spectator.Spectating ?? FollowedEntity(), reverse) is not { } next)
        {
            _spectateLog.LogDebug("{Message}", "nobody else to follow at this tick");
            return;
        }

        _spectator.Spectating = next.EntityIndex;
        _worldIsStale = true;
        _viewport.Invalidate();

        _spectateLog.LogDebug(
            "{Message}",
            $"following entity {next.EntityIndex} (team {next.Team}) " +
            $"of {players.Count} at tick {_transport.CurrentTick}");
    }

    /// <summary>The world position under a point in the viewport.</summary>
    private (float X, float Y) WorldAt(Point point)
    {
        TopDownCamera camera = MapCamera();
        float perPixel = camera.WorldUnitsPerPixel(_viewport.ClientSize.Width);
        (float centreX, float centreY) = camera.Centre;

        return (
            centreX + ((point.X - (_viewport.ClientSize.Width / 2f)) * perPixel),
            centreY - ((point.Y - (_viewport.ClientSize.Height / 2f)) * perPixel));
    }

    private void OnViewportResize(object? sender, EventArgs e)
    {
        _overlay?.PositionOver(_viewport);
        _worldIsStale = true;

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
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // **Page DOWN descends through the map**, taking the roofs off first, and page up brings
        // them back. The obvious reading of the key is the one to follow: the first version had it
        // inverted, and pressing page down 166 times did nothing because the cut was already at
        // zero and the log said so.
        if (keyData is Keys.PageUp or Keys.PageDown or Keys.Home && _loaded is not null)
        {
            float step = keyData == Keys.PageDown ? 0.02f : -0.02f;

            _heightCut = keyData == Keys.Home ? 0f : Math.Clamp(_heightCut + step, 0f, 0.95f);

            _renderLog.LogDebug(
                "{Message}",
                string.Create(
                    CultureInfo.InvariantCulture, $"height cut {_heightCut:P0} of the map"));

            _status.Text = _heightCut > 0f
                ? string.Create(CultureInfo.InvariantCulture, $"Showing the lower {1f - _heightCut:P0} of the map. Page Down cuts deeper, Page Up or Home restores it.")
                : "Showing the whole map.";

            _worldIsStale = true;

            return true;
        }

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

            _worldIsStale = true;
            _viewport.Invalidate();

            // **Says what it did, not which mode it is in.** The old line reported "free camera on"
            // or "free camera off, back to the map view", and both are now false: there is one
            // camera and this key does not switch anything. A log that names the wrong quantity
            // misdirects with authority (`docs/memory/a-log-must-name-what-it-measured.md`).
            _renderLog.LogDebug("{Message}", "camera reset to the overhead placement");

            return true;
        }

        if (keyData == Keys.F12)
        {
            CaptureViewportToFile();
            return true;
        }

        // **Logged before the guard, so a key that ARRIVED is distinguishable from one that never
        // did.** Full screen has twice been reported as impossible to leave, and the two states
        // look identical from outside: the key reaching this method and being ignored, and the key
        // going to whichever window took the foreground. Only a line written here separates them.
        if (keyData is Keys.Escape or Keys.F11)
        {
            _renderLog.LogInformation(
                "{Message}",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{keyData} reached the form; full screen is {IsFullScreen}"));
        }

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
        _ordered = [.. _library.Entries
            .OrderBy(entry => entry.Folder, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)];

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
            _borderlessMode.Dispose();
            _exclusiveMode.Dispose();

            foreach (ToolStripMenuItem item in _textureQualityItems.Values)
            {
                item.Dispose();
            }
            _search.Dispose();
            _downloader?.Dispose();
            _overlay?.Dispose();
            _wireframe.Dispose();
            _frameRate.Dispose();
            _specular.Dispose();

            // Disposing the submenu disposes the three items it owns.
            _fullbrightMenu.Dispose();
            _drawWorld.Dispose();
            _drawEntities.Dispose();
            _debugMenu.Dispose();
            _surfaceColours.Dispose();
            _fullScreen.Dispose();
        }

        base.Dispose(disposing);
    }
}
