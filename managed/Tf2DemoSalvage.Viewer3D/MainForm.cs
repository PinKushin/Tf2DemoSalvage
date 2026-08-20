using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;

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
    public const string OutlineItemId = "OutlineMenuItem";

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

    /// <summary>The loaded map's outline, already projected to clip space.</summary>
    private IReadOnlyList<((float X, float Y) From, (float X, float Y) To)> _mapLines = [];

    /// <summary>The loaded map's filled surfaces, already projected to clip space.</summary>
    private IReadOnlyList<(float X, float Y, float Shade)> _mapFill = [];

    /// <summary>The loaded map in world units, kept so it can be re-projected on resize.</summary>
    private MapOutline? _map;

    /// <summary>Fetches maps that are not installed; created on first need.</summary>
    private MapDownloader? _downloader;

    /// <summary>The game's content, opened once and reused for every map.</summary>
    private GameArchives? _archives;

    /// <summary>Which model each class wears, read from the game's own class scripts.</summary>
    /// <remarks>
    /// **Read from the install, not hardcoded.** Only <c>m_iszCustomModel</c> is networked; a
    /// player's ordinary model is resolved locally by <c>CTFPlayerClassShared::GetModelName</c>
    /// from <c>m_iClass</c>, so a viewer has to do the same lookup the client does.
    /// </remarks>
    private PlayerClassModels? _classModels;

    /// <summary>Which activity suffix each weapon in this demo drives.</summary>
    /// <remarks>
    /// Per demo rather than per install, because it is built from the weapon classes the recording
    /// mentions — reading all 78 shipped scripts to answer for the four a match uses would be work
    /// for nothing, and each one costs an ICE decryption.
    /// </remarks>
    private WeaponRoles? _weaponRoles;

    /// <summary>Players turned into drawable models, rebuilt each frame.</summary>
    /// <remarks>
    /// Kept as a field so the per-frame allocation happens once rather than per tick.
    /// </remarks>
    private readonly List<SceneProp> _drawn = [];

    /// <summary>The loaded map's surfaces, kept so the world can be rebuilt on resize.</summary>
    private IReadOnlyList<BspSurface> _surfaceList = [];

    /// <summary>The loaded map's textures and lighting.</summary>
    private MapAssets? _assets;

    /// <summary>The loaded map's bytes, kept for reading displacement terrain on re-projection.</summary>
    /// <summary>The map's displacement lumps, read once rather than once per face.</summary>
    private BspTerrain? _terrain;

    /// <summary>The map's decals, read once and reused across every rebuild.</summary>
    private IReadOnlyList<BspOverlay>? _overlays;

    /// <summary>The map's models: the world, then one per piece of moving brushwork.</summary>
    private IReadOnlyList<BspModel>? _brushModels;

    /// <summary>Where every player stood, for every moment the demo recorded.</summary>
    /// <summary>The map's BSP tree, for finding which leaf a model stands in.</summary>
    private BspLeafTree? _leaves;

    /// <summary>The ambient light each leaf holds, indexed by leaf.</summary>
    private IReadOnlyList<AmbientSamples> _ambient = [];

    /// <summary>The map's sun, when it has one.</summary>
    private BspWorldLight? _sun;

    /// <summary>Every light the map compiled, for the direct term a model receives.</summary>
    private IReadOnlyList<BspWorldLight> _worldLights = [];

    /// <summary>How high and how low the loaded map goes, once it has been read.</summary>
    private (float Lowest, float Highest)? _heightRange;

    private DemoTimeline? _timeline;

    /// <summary>Reused between frames; PlayersAt and PropsAt fill them rather than allocating.</summary>
    private readonly List<ScenePlayer> _players = [];

    private readonly List<SceneProp> _props = [];

    /// <summary>Entity models, packed once in model space and posed by the GPU.</summary>
    private readonly EntityModelSet _models = new();

    private readonly List<ModelInstance> _instances = [];

    /// <summary>Last reported instance count, so the log records changes rather than frames.</summary>
    private int _lastInstanceCount = -1;

    /// <summary>Turns real time into demo ticks at the rate the recording server ran.</summary>
    private PlaybackClock? _clock;

    /// <summary>Real time since the last advance, which is what the clock consumes.</summary>
    /// <remarks>
    /// **Playback rides the idle loop rather than a timer of its own.** A first version used a
    /// 15 ms timer that invalidated the viewport on every firing - and the viewer already redraws
    /// on every idle, so paint messages never drained and the mouse went sluggish over the very
    /// buttons that had just been added. The comment beside Application.Idle warned about exactly
    /// this: a timer would keep presenting underneath it.
    ///
    /// Advancing in the idle loop is also closer to what an engine does: a frame takes however long
    /// it takes, and the clock is told how long that was.
    /// </remarks>
    private readonly Stopwatch _playWatch = new();

    /// <summary>Whether the resident textures belong to the map currently loaded.</summary>
    private bool _texturesUploaded;

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
    private CameraMode _cameraMode = CameraMode.Map;

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
    private (float Pitch, float Yaw) _freeAngles = (35f, 0f);

    /// <summary>How far the free camera sits from what it is looking at, in world units.</summary>
    /// <remarks>Only used to place the camera when the free view is first entered.</remarks>
    private const float FreeEntryDistance = 800f;

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
    private (float X, float Y, float Z)? _freeOrigin;

    /// <summary>Degrees the free camera turns per pixel dragged.</summary>
    /// <remarks>
    /// A quarter of a degree, so a full turn is about a screen and a half of dragging. Source's own
    /// mouse sensitivity is a different quantity — it scales a raw device count rather than a
    /// pixel — so this is chosen for the drag rather than taken from the engine.
    /// </remarks>
    private const float DegreesPerPixel = 0.25f;

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

    /// <summary>The loaded map's filled faces in world units, for the same reason.</summary>
    private MapSurfaces? _surfaces;

    private readonly ToolStripMenuItem _fullScreen;

    /// <summary>Draws flat colours by surface kind instead of the map's own textures.</summary>
    private readonly ToolStripMenuItem _surfaceColours;

    /// <summary>Draws the brush outline over the map.</summary>
    /// <remarks>
    /// **Off by default now that the map has textures.** The outline was the entire picture when
    /// nothing else drew, and it stayed switched on out of habit - over a textured map it is
    /// clutter that hides the thing it was standing in for.
    /// </remarks>
    private readonly ToolStripMenuItem _outline;
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
    public MainForm(params string[] initialPaths)
    {
        // **A capture flag, because the alternative was asking a person to press F12.** Several
        // rendering defects this session were found by the owner photographing their own screen and
        // describing it, which is slow for them and leaves the loop dependent on someone being at
        // the machine. "--shot <file>" loads, seeks, draws, writes a PNG and exits; "--tick <n>"
        // says when.
        //
        // Deliberately not a test harness: it drives the real viewer through the real renderer,
        // which is the whole reason the offscreen target was deleted. See CaptureViewport.
        initialPaths = ReadCaptureOptions(initialPaths);

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
        _playlist.ItemActivate += (_, _) => LoadSelected();

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

        // **The tick drives the picture.** Scrubbing and playing both raise this, so the viewer
        // has one path from "which moment" to "who is where" rather than two that can disagree.
        _transport.TickChanged += (_, tick) =>
        {
            if (_timeline is null)
            {
                return;
            }

            // A scrub is a seek: the clock takes the new position outright and drops whatever
            // part-tick it had accumulated, or the next tick after a drag arrives early.
            _clock?.Seek(tick);

            ShowMoment(tick);
            _viewport.Invalidate();
        };

        _transport.PlayingChanged += (_, playing) =>
        {
            if (playing && _clock is not null)
            {
                // **Restart the watch, not just the timer.** Whatever real time passed while
                // paused is not playback time, and feeding it to the clock on the first tick would
                // jump the demo forward by however long the user was reading the map.
                _playWatch.Restart();
            }
            else
            {
                _playWatch.Reset();
            }
        };

        // **The scale is applied to elapsed time, not to the tick rate**, which is how Valve's own
        // replay editor does it (replayperformanceeditor.cpp multiplies its elapsed by
        // host_timescale). Scaling the rate instead would move the current position the instant
        // the speed changed, because the position is measured in ticks.
        _transport.SpeedChanged += (_, speed) =>
        {
            if (_clock is not { } clock)
            {
                return;
            }

            clock.TimeScale = speed;

            // Restarted so the frame that straddles the change is not counted at the new speed.
            if (_transport.Playing)
            {
                _playWatch.Restart();
            }
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
            ViewerLog.Write(
                "render", $"surface colours {(_surfaceColours.Checked ? "on" : "off")}");

            _device?.ClearWorld();
            _worldIsStale = true;
        };

        _outline = new ToolStripMenuItem("Brush &outline")
        {
            Name = OutlineItemId,
            CheckOnClick = true,
            Checked = false,
            ShortcutKeys = Keys.F10,
        };

        _outline.CheckedChanged += (_, _) => ViewerLog.Write(
            "render", $"brush outline {(_outline.Checked ? "on" : "off")}");

        ToolStripMenuItem screenshot = new("Save a &screenshot")
        {
            Name = ScreenshotItemId,
            ShortcutKeys = Keys.F12,
            AccessibleName = ScreenshotItemName,
            AccessibleDescription = "Writes a picture of the viewport beside the viewer's log.",
        };

        screenshot.Click += (_, _) => CaptureViewportToFile();

        view.DropDownItems.Add(screenshot);
        view.DropDownItems.Add(_outline);
        view.DropDownItems.Add(_surfaceColours);
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
    public void LoadSelected()
    {
        if (_playlist.SelectedIndices.Count == 0)
        {
            return;
        }

        int index = _playlist.SelectedIndices[0];

        if (index < 0 || index >= _shown.Count)
        {
            return;
        }

        LoadDemo(_shown[index].Path);
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
        _map = null;
        _surfaces = null;
        _mapLines = [];
        _mapFill = [];
        _surfaceList = [];
        _assets = null;
        _terrain = null;
        _overlays = null;
        _texturesUploaded = false;
        _device?.ClearWorld();

        string? path = FindMap(mapName);

        if (path is null)
        {
            // Not on this machine. Fetch it the way joining a server would - in the background,
            // because a 40 MB download must not freeze the window, and the demo is watchable
            // without a map anyway.
            ViewerLog.Write("map", $"{mapName} is not installed; fetching it");
            _ = DownloadMapAsync(mapName);
            return false;
        }

        ViewerLog.Write("map", $"found {path}");

        return ReadMap(mapName, path);
    }

    /// <summary>Fetches a map that is not installed, then loads it.</summary>
    /// <remarks>
    /// **Downloading is a background operation with a visible outcome and no modal wait.** The
    /// viewer is already usable - players draw without a world behind them - so the map arriving
    /// is an improvement to a working view rather than something to block on.
    ///
    /// Failures are reported and nothing else happens. Most maps in a real archive are community
    /// maps no mirror carries, so "not found" is the ordinary answer.
    /// </remarks>
    private async Task DownloadMapAsync(string mapName)
    {
        _status.Text = "Downloading map " + mapName + "...";

        try
        {
            _downloader ??= new MapDownloader(new HttpClient(), MapDownloader.DefaultFolder);

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

            ViewerLog.Write("map", $"loading {Path.GetFileName(path)} ({bytes.Length / 1024 / 1024} MB)");

            BspGeometry geometry = BspGeometry.Read(bytes);
            _map = MapOutline.FromFaces(geometry.OverheadFaces);

            ViewerLog.Write(
                "map",
                $"{geometry.Faces.Count} faces, {geometry.OverheadFaces.Count} overhead, " +
                $"{_map.Segments.Count} outline segments");

            // The textured world: the game's own materials and the map's baked lighting. Failing
            // here costs the textures, not the map - the outline still draws.
            try
            {
                if (_archives is null)
                {
                    string? game = FindGameFolder();
                    ViewerLog.Write("assets", $"game folder: {game ?? "not found"}");
                    _archives = GameArchives.Open(game);
                    ViewerLog.Write(
                        "assets",
                        $"content sources: {(_archives.IsEmpty ? "none" : "archives plus " + _archives.FolderCount + " folders")}");

                    // **The class scripts, which is where a player's model actually comes from.**
                    // They are ICE-encrypted KeyValues in the install; nothing in the demo carries
                    // a player's model path unless the server overrode it.
                    _classModels = PlayerClassModels.Read(_archives.Read);

                    ViewerLog.Write(
                        "assets",
                        $"class models: {string.Join(", ", ClassModelPaths())}");
                }

                _texturesUploaded = false;

                // Read once here rather than per face inside the world builder. Every call reads
                // the header and decompresses both displacement lumps, and the builder asks 578
                // times on cp_process_final - which was most of an 830 ms rebuild, paid again on
                // every resize.
                try
                {
                    _terrain = BspTerrain.Create(bytes);
                }
                catch (InvalidDataException failure)
                {
                    _terrain = null;
                    ViewerLog.Warn("assets", "reading the map's terrain", failure);
                }

                try
                {
                    _overlays = BspOverlays.Read(bytes);
                    _brushModels = BspModels.Read(bytes);
                }
                catch (InvalidDataException failure)
                {
                    // Costs the decals, not the map. Reported rather than swallowed: the engine
                    // reads this lump on every map it opens.
                    _overlays = null;
                    ViewerLog.Warn("assets", "reading the map's decals", failure);
                }

                using (ViewerLog.Time("assets", "reading surfaces and textures"))
                {
                    _surfaceList = BspSurfaces.Read(bytes);

                    // **What lights anything that moves.** A model has no lightmap, so it takes
                    // the ambient cube of the leaf it stands in - which needs the tree to find the
                    // leaf and the samples to light it. Read with the map, since both come from
                    // the same file and neither changes afterwards.
                    _leaves = BspLeafTree.Read(bytes);
                    _ambient = BspAmbientLight.Read(bytes);

                    // The direct term. The ambient cube is the shade; this is what makes daylight
                    // bright, and it is the reason a pack outdoors looked like one indoors.
                    // Kept whole, not just the sun: the sun is the only light applied to the world
                    // surfaces, but a model also takes direct light from the point and spot lights
                    // around it (B95, D37), and those are the other 475 entries on cp_process.
                    _worldLights = BspWorldLights.Read(bytes);
                    _sun = BspWorldLights.Sun(_worldLights);

                    // **Every model the demo will ever show, loaded with the map.** The timeline
                    // is already built, so the whole set is known before anything is drawn - and
                    // loading them here means their materials join the map's table and the
                    // textures upload once. Loading during playback would grow that table and
                    // force a re-upload mid-match.
                    _assets = MapAssets.Load(
                        bytes,
                        _archives,
                        (int)_settings.TextureQuality,
                        DemoModelPaths(),
                        WornModelPaths(),

                        // Built from the surfaces just read rather than from a second pass over
                        // the file: the models lump names face RANGES, so it needs the same
                        // surface list the world was built from and nothing else.
                        //
                        // **No models lump means no brush entities, and that agrees with the world
                        // build on purpose.** MapWorld treats an absent lump as "build every
                        // face", so the doors stay baked into the static world exactly as they
                        // were before this work. The two decisions have to move together: holding
                        // faces back here while the world declined to bake them would lose the
                        // geometry entirely rather than degrade to the old behaviour.
                        BrushModels.Build(_brushModels ?? [], _surfaceList));
                }

                int displacements = 0;

                foreach (BspSurface surface in _surfaceList)
                {
                    displacements += surface.IsDisplacement ? 1 : 0;
                }

                ViewerLog.Write(
                    "assets",
                    $"{_surfaceList.Count} surfaces ({displacements} displacements), " +
                    $"{_assets.Resolved} materials resolved, {_assets.Missing} missing, " +
                    $"lightmap atlas {_assets.Lightmaps.Width}x{_assets.Lightmaps.Height}, " +
                    $"texture quality {_settings.TextureQuality}");
            }
            catch (Exception failure) when (failure is IOException or InvalidDataException)
            {
                _surfaceList = [];
                _assets = null;
                _status.Text = "Map content unavailable: " + failure.Message;
                ViewerLog.Warn("assets", "reading the map's content", failure);
            }

            // Filled from the main cluster only. Outside it is the 3D skybox room, which is
            // already outside the view - but leaving it in the height range flattens the shading
            // of everything that is inside.
            _surfaces = MapSurfaces.FromFaces(geometry.OverheadFaces, _map.MainBounds);
            ProjectMap();
            return !_map.IsEmpty;
        }
        catch (Exception failure) when (failure is IOException or InvalidDataException)
        {
            _status.Text = "Map " + mapName + " could not be read: " + failure.Message;
            return false;
        }
    }

    /// <summary>The map outline the viewport is drawing, in clip space.</summary>
    public IReadOnlyList<((float X, float Y) From, (float X, float Y) To)> MapLines => _mapLines;

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
        if (_map is null || _map.IsEmpty)
        {
            _mapLines = [];
            _mapFill = [];
            return;
        }

        TopDownCamera camera = MapCamera();
        List<((float X, float Y) From, (float X, float Y) To)> lines = new(_map.Segments.Count);

        foreach (((float X, float Y) from, (float X, float Y) to) in _map.Segments)
        {
            lines.Add((camera.Project(from.X, from.Y), camera.Project(to.X, to.Y)));
        }

        _mapLines = lines;

        if (_surfaces is null || _surfaces.IsEmpty)
        {
            _mapFill = [];
            return;
        }

        // Through the SAME camera as the outlines. Two cameras fitted separately would drift apart
        // by a pixel and leave every edge sitting beside its own surface rather than on it.
        List<(float X, float Y, float Shade)> fill = new(_surfaces.Triangles.Count);

        foreach (MapTriangle corner in _surfaces.Triangles)
        {
            (float x, float y) = camera.Project(corner.X, corner.Y);
            fill.Add((x, y, corner.Shade));
        }

        _mapFill = fill;

        // The textured world is projected through the SAME camera, then uploaded. It is rebuilt on
        // a resize because the projection is baked into the vertices - which is what keeps the
        // shader a sample and a multiply.
        if (_assets is { } assets && _surfaceList.Count > 0 && _device is not null)
        {
            try
            {
                // **Textures first, and only once per map.** They do not depend on the camera, so
                // a resize needs new vertices and nothing else - see UploadWorldGeometry.
                if (!_texturesUploaded || !_device.HasWorldTextures)
                {
                    using (ViewerLog.Time("render", "uploading textures"))
                    {
                        _device.UploadWorldTextures(assets);
                    }

                    _texturesUploaded = true;
                }

                // **The camera is a matrix now, so a resize is not a rebuild.** The world's
                // vertices are in map coordinates and never move; only the view does. This is what
                // took a viewport change from 0.33 seconds to a 64-byte upload, and it is the
                // reason a free camera or a per-player view can exist at all.
                // **One matrix either way**, which is the whole reason this could be added without
                // touching the renderer: the geometry is in map coordinates and only the view
                // changes, so a free camera is a different sixty-four bytes rather than a
                // different pipeline.
                _device.SetCamera(
                    ViewMatrix(camera),
                    _surfaceColours.Checked,
                    _heightCut);

                // **Logged because this is now the whole cost of a resize**, and a rebuild is not.
                // Counting these against "building the world" lines is what proves the geometry
                // survived a viewport change rather than being quietly rebuilt: many camera lines
                // and one build line is the fix working, and one of each per resize is not.
                ViewerLog.Write(
                    "render",
                    $"camera set for a {_viewport.ClientSize.Width}x{_viewport.ClientSize.Height} viewport");

                if (_device.HasWorld)
                {
                    return;
                }

                MapWorld built;

                using (ViewerLog.Time("render", "building the world"))
                {
                    // Recorded before the build so MapCamera can project height on the very first
                    // frame; taking it afterwards leaves one frame drawn with a pass-through depth.
                    _heightRange = MapWorldBuilder.HeightRange(_surfaceList, _map.MainBounds);

                    // The decal bias is a fraction of the depth buffer, and the depth buffer spans
                    // this range - so the same bias is worth a different distance on every map.
                    if (_heightRange is { } range && range.Highest > range.Lowest)
                    {
                        _device.SetDecalBias(range.Highest - range.Lowest);
                    }

                    built = MapWorldBuilder.Build(
                        _terrain,
                        _surfaceList,
                        assets.Materials,
                        assets.Lightmaps,
                        assets.Props,
                        camera,
                        _map.MainBounds,
                        _surfaceColours.Checked,
                        _overlays,
                        _brushModels);
                }

                ViewerLog.Write(
                    "render",
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
                ViewerLog.Warn("render", "uploading the textured world", failure);
            }
        }
    }

    /// <summary>
    /// Frames the map proper, not its full extent.
    /// </summary>
    /// <remarks>
    /// <c>MainBounds</c> rather than <c>Bounds</c>: a TF2 map carries its 3D skybox as ordinary
    /// world geometry placed far outside the playable space, and fitting to that pushed
    /// cp_process_final into a third of the viewport with an empty expanse beside it.
    /// </remarks>
    /// <summary>The ambient light at a world position.</summary>
    /// <remarks>
    /// **The leaf decides, which is how the engine does it.** A model takes the light measured
    /// inside the leaf it stands in, so two crates either side of a doorway are lit differently
    /// without either carrying a lightmap.
    ///
    /// An unlit answer is returned as a default cube, which the shader reads as "no cube supplied"
    /// and draws at full brightness rather than black - a model lit by a measurement nobody made
    /// is worse than one that is merely too bright.
    /// </remarks>
    private AmbientCube LightAt(float x, float y, float z)
    {
        if (_leaves is not { } tree || _ambient.Count == 0)
        {
            return default;
        }

        int leaf = tree.LeafAt(x, y, z);

        // **Blended, as Mod_LeafAmbientColorAtPos blends it.** vrad thins a leaf's samples down to
        // the ones an inverse-squared-distance average cannot already predict, so the stored set
        // only reconstructs the original lighting when it is interpolated. Taking the nearest read
        // back whichever survivor of that thinning was closest, which is why one capture point on
        // cp_process drew at 0.10 while its mirror image on a symmetric map drew at 0.39.
        AmbientCube bounced = leaf >= 0 && leaf < _ambient.Count
            ? _ambient[leaf].At(x, y, z)
            : default;

        // **And the direct term, which is the other half of what the engine gives a model.**
        // istudiorender.h describes the cube as "ambient, and lights that aren't in locallight[]",
        // so a cube carrying a nearby lamp's light is the shape the engine itself produces for
        // every light past the nearest four. Without this a prop out of daylight is lit by the
        // bounce alone, which is why anything indoors read as though it were in shade (B95).
        return LocalLights.AddTo(bounced, _worldLights, x, y, z);
    }

    /// <summary>The sun reaching a world position, or null when it does not.</summary>
    /// <remarks>
    /// **The trace is the feature, not an optimisation.** Valve describes a sky light as a
    /// "directional light with no falloff (surface must trace to SKY texture)" — applied without
    /// that condition it lights the inside of every building, which is worse than the shade this
    /// is meant to fix.
    ///
    /// Traced towards the sun, which is against the direction its light travels.
    /// </remarks>
    private SunLight? SunAt(float x, float y, float z)
    {
        if (_sun is not { } sun || _leaves is not { } tree)
        {
            return null;
        }

        if (!tree.SeesSky(x, y, z, -sun.Normal.X, -sun.Normal.Y, -sun.Normal.Z))
        {
            return null;
        }

        return new SunLight(
            sun.Intensity.Red,
            sun.Intensity.Green,
            sun.Intensity.Blue,
            sun.Normal.X,
            sun.Normal.Y,
            sun.Normal.Z);
    }

    /// <summary>One model's triangles, from the set preloaded with the map.</summary>
    /// <remarks>
    /// Answers null for anything the load did not find, which <see cref="EntityModelSet"/>
    /// remembers rather than asking again every frame. The miss was already reported once, at
    /// load, where a missing asset is worth reading.
    /// </remarks>
    private PropModels.ModelFrames? ModelGeometry(string path) =>
        _assets is { } assets &&
        assets.EntityModels.TryGetValue(path, out PropModels.ModelFrames? frames)
            ? frames
            : null;

    /// <summary>The model every playable class wears.</summary>
    /// <remarks>
    /// **Read from the install rather than listed here.** <c>CTFPlayerClassShared::GetModelName</c>
    /// returns <c>m_iszCustomModel</c> when a server has overridden it and otherwise
    /// <c>GetPlayerClassData( m_iClass )-&gt;GetModelName()</c>, which is the class script - so the
    /// class number is the only thing a demo needs to carry, and it does.
    ///
    /// The custom model is networked and is NOT honoured yet: nothing decodes
    /// <c>m_iszCustomModel</c>, so a server that replaced a player's model draws the stock one.
    /// Rare outside events and plugins, and stated rather than hidden.
    /// </remarks>
    private IEnumerable<string> ClassModelPaths()
    {
        if (_classModels is not { } models)
        {
            yield break;
        }

        for (int playerClass = PlayerClassModels.FirstClass;
            playerClass <= PlayerClassModels.LastPlayingClass;
            playerClass++)
        {
            if (models.Model(playerClass) is { } model)
            {
                yield return model;
            }
        }
    }

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
    private void EnsureWeaponRoles()
    {
        if (_weaponRoles is not null || _archives is not { } archives || _timeline is not { } timeline)
        {
            return;
        }

        // Only the classes this recording mentions: the archive holds 78 weapon scripts, a match
        // touches a handful, and each one costs an ICE decryption.
        // **Weapon AND holder**, because the role is not a property of the weapon alone: a shotgun
        // is a primary for an engineer and a secondary for a soldier, a heavy and a pyro.
        HashSet<(string Weapon, int? Class)> held = [];

        foreach (TimelineFrame frame in timeline.Frames)
        {
            foreach (ScenePlayer player in frame.Players)
            {
                if (player.WeaponClass is { } weapon)
                {
                    held.Add((weapon, player.PlayerClass));
                }
            }
        }

        _weaponRoles = WeaponRoles.Read(archives.Read, held);

        ViewerLog.Write(
            "demo",
            "weapon roles: " + string.Join(
                ", ",
                held.OrderBy(pair => pair.Weapon, StringComparer.Ordinal)
                    .ThenBy(pair => pair.Class)
                    .Select(pair =>
                        $"{pair.Weapon}/{pair.Class?.ToString(CultureInfo.InvariantCulture) ?? "?"}=" +
                        _weaponRoles.Suffix(pair.Weapon, pair.Class))));
    }

    /// <summary>The model a player is drawn as, or null when they are not drawn as one.</summary>
    /// <param name="player">The player.</param>
    /// <remarks>
    /// **One predicate, used by both the model pass and the dot pass.** A player drawn as a model
    /// must not also get a flat marker on top of it, and a player without a model must still get
    /// one or they vanish. Asking the question in two places is how the two answers drift apart -
    /// and they did: the markers were still being drawn over the models the moment those started
    /// working, which hid whether the models were there at all.
    /// </remarks>
    private string? PlayerModel(ScenePlayer player) =>
        player.IsPlaying && player.Drawn && player.PlayerClass is { } playerClass
            ? _classModels?.Model(playerClass)
            : null;

    /// <summary>Every distinct studio model the loaded demo shows, at any tick.</summary>
    /// <remarks>
    /// Brush models and sprites are excluded: a <c>*N</c> is map geometry and a sprite is a
    /// camera-facing quad, and neither is a <c>.mdl</c> the studio loader can read.
    /// </remarks>
    private HashSet<string> DemoModelPaths()
    {
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);

        // **Every class, not only the ones standing at tick zero.** A player can switch class at
        // any moment in a match, so a set built from who is playing now is missing whatever they
        // change to - and a model absent from this set is never packed, so the player would simply
        // vanish mid-round. Nine models is the whole roster and it is loaded once.
        foreach (string model in ClassModelPaths())
        {
            paths.Add(model);
        }

        if (_timeline is not { } timeline)
        {
            return paths;
        }

        foreach (ScenePropTrack track in timeline.Props)
        {
            if (track.Kind == SceneModelKind.Studio)
            {
                paths.Add(track.ModelPath);
            }
        }

        // **The first-person models, which are in neither of the sets above.** A viewmodel is not a
        // prop — it has no origin, so the timeline deliberately keeps it out of Props — and the
        // weapon in its hands is not an entity at all. Both are loaded here or they are never
        // loaded: this set is what MapAssets is given, and the loader is a dictionary lookup rather
        // than an on-demand read, so a model absent from it packs to nothing for ever.
        //
        // It cost a whole feature. The viewer resolved c_demo_arms.mdl, packed it, reported "0
        // instances" and drew nothing, with the model sitting in the archive the entire time.
        foreach (string arms in timeline.ViewmodelModels)
        {
            paths.Add(arms);
        }

        foreach (string weapon in HeldWeaponModels(timeline))
        {
            paths.Add(weapon);
        }

        return paths;
    }

    /// <summary>Every weapon model any player holds at any point in the demo.</summary>
    /// <remarks>
    /// **Resolved up front for the same reason the class models are.** A player switches weapon
    /// constantly and a set built from what is held right now is missing whatever they draw next —
    /// which does not fail loudly, it just leaves an empty hand.
    ///
    /// Distinct pairs rather than distinct players: a whole match resolves to a few dozen models.
    /// </remarks>
    private IEnumerable<string> HeldWeaponModels(DemoTimeline timeline)
    {
        if (ItemDefinitions() is null)
        {
            yield break;
        }

        HashSet<(int? Item, string? Weapon, int Class)> seen = [];

        foreach (TimelineFrame frame in timeline.Frames)
        {
            foreach (ScenePlayer player in frame.Players)
            {
                if (player.ActiveWeapon is null ||
                    !seen.Add((player.WeaponItem, player.WeaponClass, player.PlayerClass ?? 0)))
                {
                    continue;
                }

                if (WeaponModel(player) is { Length: > 0 } model)
                {
                    yield return model;
                }
            }
        }
    }

    /// <summary>The models the demo ever hangs off another entity's skeleton.</summary>
    /// <remarks>
    /// **These must be skinned rather than baked, and the reason is not performance.** A
    /// bone-merged item is placed entirely by its wearer's bones, so baking - which pre-transforms
    /// the vertices by one pose and throws the bone indices away - leaves nothing to attach it by.
    /// It then draws at the wearer's origin, which on a player is their FEET.
    ///
    /// Measured on cp_process: every cosmetic is a few thousand corners and a single sequence, so
    /// the corner budget baked all of them and the hats sat at ankle height while the merge
    /// reported nothing at all.
    /// </remarks>
    private HashSet<string> WornModelPaths()
    {
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);

        if (_timeline is not { } timeline)
        {
            return paths;
        }

        foreach (ScenePropTrack track in timeline.Props)
        {
            if (track.AttachedTo is not null && track.Kind == SceneModelKind.Studio)
            {
                paths.Add(track.ModelPath);
            }
        }

        return paths;
    }

    /// <summary>Where to write an automatic capture, when one was asked for.</summary>
    private string? _shotPath;

    /// <summary>Which tick to show before capturing.</summary>
    private int _shotTick;

    /// <summary>Whether an automatic capture should be taken from the player's own eyes.</summary>
    private bool _shotFirstPerson;

    /// <summary>Where to point the camera before capturing, in world units.</summary>
    private (float X, float Y)? _shotLookAt;

    /// <summary>How far to zoom in before capturing.</summary>
    private float _shotZoom = 1f;

    /// <summary>Whether to capture the category view rather than the textured one.</summary>
    private bool _shotSurfaceColours;

    /// <summary>Frames still to draw before the shutter, so the world is finished and settled.</summary>
    private int _shotDelay = 45;

    /// <summary>Pulls the capture options out of the paths, returning what is left.</summary>
    private string[] ReadCaptureOptions(string[] arguments)
    {
        List<string> paths = [];
        Queue<string> pending = new(arguments);

        // A queue rather than an indexed loop: an option consumes the value after it, and moving a
        // loop counter from inside the body is the shape analyzers rightly object to.
        while (pending.Count > 0)
        {
            string argument = pending.Dequeue();

            if (argument == "--shot" && pending.Count > 0)
            {
                _shotPath = pending.Dequeue();
                continue;
            }

            if (argument == "--look" && pending.Count > 1)
            {
                string x = pending.Dequeue();
                string y = pending.Dequeue();

                if (float.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out float worldX) &&
                    float.TryParse(y, NumberStyles.Float, CultureInfo.InvariantCulture, out float worldY))
                {
                    _shotLookAt = (worldX, worldY);
                    continue;
                }

                ViewerLog.Warn("viewer", $"--look {x} {y} is not a position; ignoring it");
                continue;
            }

            if (argument == "--colours")
            {
                _shotSurfaceColours = true;
                continue;
            }

            // **The capture that a person actually wants to look at is the first-person one**, and
            // until this flag existed the only route to it was the UI suite pressing V — which
            // meant it could only be taken on whichever demo that suite happens to open, at
            // whichever tick it could reach. See docs/findings/29 for what that produced: a
            // picture of a wall at the last tick of a solo recording.
            if (argument == "--first-person")
            {
                _shotFirstPerson = true;
                continue;
            }

            if (argument == "--zoom" && pending.Count > 0)
            {
                string value = pending.Dequeue();

                if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float zoom))
                {
                    _shotZoom = zoom;
                    continue;
                }

                ViewerLog.Warn("viewer", $"--zoom {value} is not a number; ignoring it");
                continue;
            }

            if (argument == "--tick" && pending.Count > 0)
            {
                string value = pending.Dequeue();

                if (int.TryParse(
                        value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int tick))
                {
                    _shotTick = tick;
                    continue;
                }

                // Not silent: a mistyped tick that quietly captures tick zero is a picture of the
                // wrong moment, which is worse than no picture.
                ViewerLog.Warn("viewer", $"--tick {value} is not a number; capturing tick 0");
                continue;
            }

            paths.Add(argument);
        }

        return [.. paths];
    }

    /// <summary>Takes the automatic capture once the world has settled, then closes.</summary>
    /// <remarks>
    /// **Counted in frames, not seconds.** The map, its textures and the entity models all load
    /// before the first frame is drawn, so a frame count after that is a count of settled frames -
    /// where a wall-clock wait would be a guess that fails on a slower machine or a bigger map.
    /// </remarks>
    private void TakeAutomaticShot()
    {
        if (_shotPath is not { } path)
        {
            return;
        }

        if (_shotDelay-- > 0)
        {
            if (_shotDelay == 40 && _timeline is not null)
            {
                // **The clock too, not just the transport.** Moving the camera marks the world
                // stale, and the reprojection that follows re-reads the moment from the clock - so
                // a capture that only told the transport photographed tick zero while every log
                // line said otherwise.
                _clock?.Seek(_shotTick);
                _transport.ShowTick(_shotTick);
                ShowMoment(_shotTick);

                if (_shotSurfaceColours)
                {
                    _surfaceColours.Checked = true;
                }

                // **After the seek, because entering the first-person view reads the moment.**
                // The camera is placed from the recorded view or from the followed player at the
                // CURRENT tick, so switching before the clock moves photographs the right mode at
                // the wrong instant — and the picture looks like a camera bug rather than an
                // ordering one.
                if (_shotFirstPerson)
                {
                    _ = ToggleFirstPerson();
                }

                if (_shotLookAt is { } centre)
                {
                    _zoom = _shotZoom;
                    _lookingAt = centre;
                    _worldIsStale = true;
                }
            }

            return;
        }

        _shotPath = null;

        CaptureViewport(path);
        BeginInvoke(Close);
    }

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
            _cameraMode = CameraMode.Map;
            _worldIsStale = true;
            _viewport.Invalidate();
            ViewerLog.Write("render", "first person off, back to the map view");
            return true;
        }

        if (FirstPersonCamera() is null)
        {
            ViewerLog.Warn(
                "render",
                "first person unavailable: this demo has no recorded camera and no player to " +
                "follow at this tick");

            _status.Text = "No first-person view here: nothing to follow at this tick.";
            return true;
        }

        _cameraMode = CameraMode.FirstPerson;
        _worldIsStale = true;
        _viewport.Invalidate();

        ViewerLog.Write(
            "render",
            _timeline?.HasRecordedView == true
                ? "first person on, following the recording's own camera"
                : "first person on, spectating a player (this demo has no recorded camera)");

        return true;
    }

    /// <summary>Puts the followed player's weapon in front of the camera.</summary>
    /// <param name="seconds">Demo time, for advancing the weapon's own animation.</param>
    /// <remarks>
    /// **A viewmodel has no position of its own, so this is where it gets one.** Its table is
    /// declared <c>BEGIN_NETWORK_TABLE_NOBASE</c> and carries no origin and no angles at all — the
    /// demo names the model and the pose, and <c>CBaseViewModel::CalcViewModelView</c> starts it at
    /// the eye:
    ///
    /// <code>
    /// QAngle vmangles = eyeAngles;
    /// Vector vmorigin = eyePosition;
    /// </code>
    ///
    /// **The bob, the lag and the shake that follow in the engine are deliberately not copied.**
    /// Every one of them is a function of movement and elapsed time rather than of anything the
    /// recording holds, so reproducing them would be this viewer inventing motion — which is the
    /// one thing it exists not to do. What is drawn is where the weapon was; how it swayed is not
    /// in the file.
    ///
    /// Mirrored, because a viewmodel is drawn mirrored and the cull flips with it. Getting that
    /// wrong does not fail, it draws the weapon inside out.
    /// </remarks>
    private void AddViewmodel(double seconds)
    {
        if (!_firstPerson ||
            _timeline is not { } timeline ||
            FollowedEntity() is not { } follower ||
            FirstPersonCamera() is not { } camera)
        {
            // **Dropping the camera is how "draw none" is said.** The instance list is owned by the
            // pose step and survives paused frames on purpose, so leaving it populated while first
            // person is off would keep a weapon on screen after V was pressed.
            _viewmodelCamera = null;
            return;
        }

        if (timeline.ViewmodelAt(_transport.CurrentTick, follower) is not { } weapon)
        {
            ViewerLog.Warn(
                "render",
                $"no viewmodel for entity {follower} at tick {_transport.CurrentTick}");
            return;
        }

        // **At the eye, which is where CalcViewModelView puts it**, and where it stays until the
        // reason it is not visible is understood rather than guessed at. Two offsets were tried and
        // neither helped: pushing it 24 units forward (the near plane is 7, so clipping was the
        // obvious suspect) and rotating its yaw by −90 (the posed geometry sits along +Y, which is
        // camera-left). See docs/findings/30 for what IS known.
        SceneProp prop = new(
            ViewmodelEntityIndex,
            weapon.ModelPath,
            SceneModelKind.Studio,
            new ScenePose
            {
                X = camera.Origin.X,
                Y = camera.Origin.Y,
                Z = camera.Origin.Z,
                Pitch = camera.Angles.Pitch,
                Yaw = camera.Angles.Yaw,
                Roll = camera.Angles.Roll,
                Sequence = weapon.Sequence,
                PlaybackRate = weapon.PlaybackRate,
            });

        // Packed on demand like any other model, so a weapon seen for the first time is loaded
        // rather than skipped — and skipped silently, since a missing model draws nothing.
        // **Whether the set grew, because packing is not uploading.** `Add` fills this process's
        // copy of the geometry; the renderer keeps its own on the GPU and only receives it when
        // `UploadModels` is called. The world's props do that whenever their set grows, and the
        // viewmodel's Add was ignoring the same signal — so the arms were packed, posed, instanced,
        // transformed correctly and submitted against geometry the renderer did not have.
        //
        // It said so on every frame: "a model was posed but the renderer has no geometry for it".
        bool grew = _models.Add([prop], ModelGeometry);

        // **Asked AFTER packing, because the sequence table does not exist until then.** The first
        // version of this asked first and got −1 every time — the model had no packed frames yet —
        // which read as "this model has no viewmodel idle" when it has one at merged index 3.
        //
        // **Merged sequence 1 on an arms model is `r_handposes`**, a one-frame pose holder whose
        // root bone sits at identity, leaving the arms in their authored Y-up space and off screen.
        // The animations a viewmodel plays start at 2 and carry ACT_*_VM_IDLE.
        int idle = _models.SequenceByActivity(weapon.ModelPath, "VM_IDLE");

        if (idle >= 0 && idle != weapon.Sequence)
        {
            prop = prop with { Pose = prop.Pose with { Sequence = idle } };
        }

        ViewerLog.Write(
            "render",
            string.Create(
                CultureInfo.InvariantCulture,
                $"viewmodel sequence: demo says {weapon.Sequence}, VM_IDLE is {idle}"));

        List<SceneProp> viewmodelProps = [prop];

        // **The weapon is a second model, parented to the arms.** In modern TF2 the networked
        // viewmodel carries the player's ARMS — c_sniper_arms, c_pyro_arms — and the gun is a
        // separate C_ViewmodelAttachmentModel the CLIENT creates and parents to it
        // (econ_entity.cpp:1153). It is not networked, so no demo carries it and it has to be
        // rebuilt from the item the player is holding.
        //
        // Drawn at the viewmodel's own transform because that is where the engine puts it: the
        // attachment is parented with SetLocalOrigin( vec3_origin ) and bone-merged, so its bones
        // take the arms' outright. Mirrored with them for the same reason they are.
        if (WeaponModelFor(follower) is { Length: > 0 } held)
        {
            // **Bone-merged onto the arms, not posed beside them.** The engine parents the
            // attachment with `SetLocalOrigin( vec3_origin )` and blends it through
            // `C_ViewmodelAttachmentModel::StandardBlendingRules`, so it has no pose of its own —
            // it takes the viewmodel's bone matrices by name, exactly as a hat takes a player's.
            //
            // Posed independently it sits at its own origin, which after the transform is AT the
            // camera and therefore inside the near plane: packed, instanced, drawn and invisible.
            // A weapon model carries one sequence and no animation to move it anywhere else.
            SceneProp gun = new(
                WeaponEntityIndex,
                held,
                SceneModelKind.Studio,
                new ScenePose
                {
                    X = camera.Origin.X,
                    Y = camera.Origin.Y,
                    Z = camera.Origin.Z,
                    Pitch = camera.Angles.Pitch,
                    Yaw = camera.Angles.Yaw,
                    Roll = camera.Angles.Roll,
                    Sequence = weapon.Sequence,
                    PlaybackRate = weapon.PlaybackRate,
                },
                AttachedTo: ViewmodelEntityIndex);

            grew |= _models.Add([gun], ModelGeometry);
            viewmodelProps.Add(gun);
        }

        if (grew && _device is { } packed)
        {
            packed.UploadModels(_models);

            ViewerLog.Write(
                "render",
                $"viewmodel models uploaded: {_models.Count} packed, " +
                $"{_models.Vertices.Count} vertices");
        }

        // **One call for both, because Instances CLEARS the list it is given.** Posing the arms and
        // then the weapon into the same list threw the arms away and drew the gun alone — a bug
        // that reads as "the arms do not work" and was invisible next to a viewmodel that was not
        // on screen for other reasons anyway.
        _models.Instances(viewmodelProps, _viewmodelInstances, LightAt, SunAt, seconds);

        // **Says what it produced, because nothing else can.** A viewmodel that resolves, packs
        // and then yields no instance is indistinguishable on screen from one that was never
        // looked up — and that distinction is exactly what went wrong the first time this ran.
        ViewerLog.Write(
            "render",
            $"viewmodel {weapon.ModelPath} seq {weapon.Sequence} at tick " +
            $"{_transport.CurrentTick}: {viewmodelProps.Count} props, " +
            $"{_viewmodelInstances.Count} instances");

        // **Kept OUT of the world list, because they are drawn in their own pass.** The engine
        // draws viewmodels after the world with a different projection and a compressed depth
        // range (CViewRender::DrawViewModels); putting them in with everything else is what left
        // them packed, posed, instanced, listed for drawing and invisible.
        //
        // **Not mirrored.** `cl_flipviewmodels` mirrors for a left-handed view and is off by
        // default — the owner, who has played the game: "the watch is the left hand, the weapon in
        // the right, unless you use left handed viewmodels, then its the opposite".
        // `C_BaseViewModel::InternalDrawModel` switches to MATERIAL_CULLMODE_CW *when* mirrored,
        // which is the same conditional from the renderer's side.
        _viewmodelCamera = new FreeCamera
        {
            Origin = camera.Origin,
            Angles = camera.Angles,
            Aspect = camera.Aspect,
            FarZ = camera.FarZ,
            FieldOfView = _settings.ViewmodelFieldOfView,
            NearZ = ViewmodelPass.NearPlane,
        };
    }

    /// <summary>The camera the viewmodel pass uses, or null when nothing is drawn in it.</summary>
    private FreeCamera? _viewmodelCamera;

    /// <summary>The model of the weapon in a player's hands, or <c>null</c>.</summary>
    /// <param name="player">The player being followed.</param>
    /// <remarks>
    /// **Two routes, and the second is needed more often than it looks.** A demo names the item the
    /// player holds — <c>m_iItemDefinitionIndex</c> — and the schema turns that into a model. But
    /// measured on z1800, 22 of 56 held weapons never send one, so the weapon's own class is used
    /// to find the stock item for it instead. Together they answered for 56 of 56.
    ///
    /// Both are lookups into <c>items_game.txt</c>, which is read once and kept: it is eight
    /// megabytes, and this is asked every frame.
    /// </remarks>
    private string? WeaponModelFor(int player) =>
        PlayerAt(_transport.CurrentTick, player) is { } holder ? WeaponModel(holder) : null;

    /// <summary>The model of the weapon a player is holding, or <c>null</c>.</summary>
    /// <param name="holder">The player, at whichever tick they were read.</param>
    /// <remarks>
    /// Shared by the draw path and by the load set, deliberately: the set decides which models are
    /// packed and the draw path decides which is shown, so a disagreement between them is a weapon
    /// that resolves and cannot be drawn — which is exactly the failure this feature already had
    /// once, from the other direction.
    /// </remarks>
    private string? WeaponModel(ScenePlayer holder)
    {
        if (ItemDefinitions() is not { } schema)
        {
            return null;
        }

        int playerClass = holder.PlayerClass ?? 0;

        if (holder.WeaponItem is { } item &&
            schema.ModelFor(item, playerClass) is { Length: > 0 } named)
        {
            return named;
        }

        if (holder.WeaponClass is not { } weaponClass)
        {
            return null;
        }

        foreach (string candidate in WeaponScriptName.Candidates(weaponClass, holder.PlayerClass))
        {
            if (schema.ModelForClass(candidate, playerClass) is { Length: > 0 } stock)
            {
                return stock;
            }
        }

        return null;
    }

    /// <summary>TF2's item schema, read from the installed game once.</summary>
    /// <remarks>
    /// Null when the game is not installed or the file is not where it should be, which is the
    /// same condition every other asset lookup here already tolerates — the viewer draws what it
    /// can find and says what it could not.
    /// </remarks>
    private ItemSchema? ItemDefinitions()
    {
        if (_itemSchema is not null || _itemSchemaMissing)
        {
            return _itemSchema;
        }

        if (_archives?.Read("scripts/items/items_game.txt") is not { } bytes)
        {
            // Recorded so the eight-megabyte read is not attempted every frame, and reported once
            // so a viewer with no weapons in hand says why.
            _itemSchemaMissing = true;
            ViewerLog.Warn("render", "no items_game.txt, so no weapon models in first person");
            return null;
        }

        _itemSchema = ItemSchema.Read(bytes);
        ViewerLog.Write("render", "item schema read");

        return _itemSchema;
    }

    /// <summary>The item schema, once read.</summary>
    private ItemSchema? _itemSchema;

    /// <summary>Whether the schema was looked for and not found.</summary>
    private bool _itemSchemaMissing;

    /// <summary>The slot the weapon in hand is drawn under, beside the arms.</summary>
    /// <remarks>
    /// Its own index rather than the viewmodel's, because the two are separate models packed and
    /// posed separately — sharing one would have the second overwrite the first's geometry.
    /// </remarks>
    private const int WeaponEntityIndex = 4097;

    /// <summary>Scratch list for the viewmodel's instances, reused between frames.</summary>
    private readonly List<ModelInstance> _viewmodelInstances = [];


    /// <summary>
    /// The entity slot the viewmodel is drawn under, which is not a real one.
    /// </summary>
    /// <remarks>
    /// A viewmodel is not in the scene the timeline builds — it has no position, so it is not a
    /// prop — and it still needs an index to be packed and posed like one. Chosen above every real
    /// slot so it cannot collide with an entity the demo describes.
    /// </remarks>
    private const int ViewmodelEntityIndex = 4096;

    /// <summary>Whose eyes the first-person camera is in, or <c>null</c> when it is not in any.</summary>
    /// <remarks>
    /// **The same choice the camera makes, asked separately** — the camera needs a position and
    /// the renderer needs an entity to hide, and deriving one from the other would let them
    /// disagree. On a point-of-view demo it is the recorder; on a SourceTV demo it is whoever is
    /// being spectated.
    /// </remarks>
    private int? FollowedEntity()
    {
        if (_timeline is not { } timeline)
        {
            return null;
        }

        if (timeline.RecordedViewAt(_transport.CurrentTick) is not null)
        {
            return timeline.RecorderEntityIndex;
        }

        // Whoever the camera is spectating, asked in one place so the two cannot disagree — this
        // decides which player is hidden from their own view, and a mismatch would hide the wrong
        // body or leave the spectated one standing in front of the lens.
        return SpectatorTarget.Choose(timeline.PlayersAt(_transport.CurrentTick))?.EntityIndex;
    }

    /// <summary>The camera for the first-person view, or <c>null</c> when there is none.</summary>
    /// <remarks>
    /// **Two mechanisms behind one mode, and which applies is a property of the demo.**
    ///
    /// A point-of-view demo carries the camera the recording client computed, in
    /// <c>democmdinfo_t</c>. That is used as it stands: it already accounts for death, spectating
    /// and every observer mode, and rebuilding it from the recorder's entity would be right while
    /// they lived and wrong for the rest — measured, the two part company by 169 units on the 2009
    /// demo the moment the recorder dies. Only the eye height is added, because the recorded origin
    /// is the feet.
    ///
    /// A SourceTV demo carries no camera, so the view is built from a player's own position and
    /// eye angles — what the engine does when you spectate in game, and what
    /// <see cref="FreeCamera.SpectatingEye"/> exists for. The heights differ between the two paths
    /// and that is Valve's doing rather than an approximation; see <see cref="PlayerEye"/>.
    /// </remarks>
    private FreeCamera? FirstPersonCamera()
    {
        if (_timeline is not { } timeline)
        {
            return null;
        }

        float aspect = _viewport.ClientSize.Height > 0
            ? _viewport.ClientSize.Width / (float)_viewport.ClientSize.Height
            : 16f / 9f;

        int tick = _transport.CurrentTick;

        if (timeline.RecordedViewAt(tick) is { } recorded)
        {
            ScenePlayer? recorder = PlayerAt(tick, timeline.RecorderEntityIndex);

            return FreeCamera.AtEye(
                recorded,
                recorder?.PlayerClass ?? 0,
                Ducking(recorder),
                aspect);
        }

        // No recorded camera: spectate somebody who is actually playing. Taking the first player
        // in the list took the SourceTV camera instead — see SpectatorTarget, and
        // docs/findings/29 for the three identical captures that found it.
        if (SpectatorTarget.Choose(timeline.PlayersAt(tick)) is not { } target)
        {
            return null;
        }

        return FreeCamera.SpectatingEye(
            (target.X, target.Y, target.Z),
            target.EyePitch ?? 0f,
            target.EyeYaw ?? target.Yaw,
            Ducking(target),
            aspect);
    }

    /// <summary>One player at a tick, by entity index.</summary>
    /// <remarks>
    /// <see cref="ScenePlayer"/> is a record STRUCT, so <c>FirstOrDefault</c> hands back a zeroed
    /// player rather than null and a <c>is null</c> check never fires — which would put the camera
    /// at the world origin with class zero rather than reporting that nobody was found.
    /// </remarks>
    private ScenePlayer? PlayerAt(int tick, int? entityIndex)
    {
        if (entityIndex is not { } index || _timeline is not { } timeline)
        {
            return null;
        }

        foreach (ScenePlayer player in timeline.PlayersAt(tick))
        {
            if (player.EntityIndex == index)
            {
                return player;
            }
        }

        return null;
    }

    /// <summary>Whether a player is crouched, which lowers the eye by more than a foot.</summary>
    /// <remarks>
    /// <c>FL_DUCKING</c> on <c>m_fFlags</c>. A player whose flags the recording never stated is
    /// treated as standing, which is what they usually are — the same default the animation state
    /// machine takes.
    /// </remarks>
    private static bool Ducking(ScenePlayer? player) =>
        player?.Flags is { } flags && (flags & PlayerActivityState.Ducking) != 0;

    private FreeCamera FreeLookCamera()
    {
        float aspect = Math.Max(1, _viewport.ClientSize.Width) /
            (float)Math.Max(1, _viewport.ClientSize.Height);

        // **A camera placed from the environment, for comparing against a capture from the game.**
        // TF2's `pos` and `ang` readouts give an exact viewpoint, and reproducing one by hand with
        // mouse and keys is neither quick nor repeatable. Parity work keeps needing the same frame
        // twice — once from the engine and once from here — so the coordinates are worth taking as
        // input. Applied once, like the orbit below, so the camera still flies afterwards.
        if (_freeOrigin is null &&
            Environment.GetEnvironmentVariable(CameraVariable) is { Length: > 0 } placement &&
            ParseCamera(placement) is { } placed)
        {
            _freeOrigin = placed.Origin;
            _freeAngles = (placed.Pitch, placed.Yaw);

            ViewerLog.Write(
                "render",
                $"free camera placed from {CameraVariable} at " +
                $"({placed.Origin.X:0.##},{placed.Origin.Y:0.##},{placed.Origin.Z:0.##}) " +
                $"pitch {placed.Pitch:0.##} yaw {placed.Yaw:0.##}");
        }

        // Placed the first time by orbiting what the map view was centred on, so entering the free
        // view does not move the subject. After that it is a position and it flies.
        _freeOrigin ??= FreeCamera.Orbiting(
            FreeFocus(), _freeAngles.Pitch, _freeAngles.Yaw, FreeEntryDistance, aspect).Origin;

        return new FreeCamera
        {
            Origin = _freeOrigin.Value,
            Angles = (_freeAngles.Pitch, _freeAngles.Yaw, 0f),
            Aspect = aspect,
        };
    }

    /// <summary>Reads a camera placement, or null when the text is not five numbers.</summary>
    /// <param name="text">Whitespace or comma separated <c>x y z pitch yaw</c>.</param>
    /// <returns>The placement, or <c>null</c>.</returns>
    /// <remarks>
    /// Null rather than a default placement, because a mistyped variable that silently put the
    /// camera at the origin would look like the viewer ignoring it — and the whole point is to be
    /// somewhere specific. The log line only prints when a placement was actually read.
    /// </remarks>
    internal static ((float X, float Y, float Z) Origin, float Pitch, float Yaw)? ParseCamera(
        string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string[] parts = text.Split(
            [' ', ',', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length < 5)
        {
            return null;
        }

        Span<float> values = stackalloc float[5];

        for (int index = 0; index < 5; index++)
        {
            if (!float.TryParse(
                    parts[index], NumberStyles.Float, CultureInfo.InvariantCulture,
                    out values[index]))
            {
                return null;
            }
        }

        return ((values[0], values[1], values[2]), values[3], values[4]);
    }

    /// <summary>What the free camera is aimed at when it is first entered.</summary>
    private (float X, float Y, float Z) FreeFocus()
    {
        (float centreX, float centreY) = _lookingAt ?? MapCamera().Centre;

        // **The players' height, not the middle of the map.** A map's vertical range includes its
        // skybox and its basements, so its midpoint is nowhere anybody stands; entering the free
        // view there put the camera above the rooftops. The lowest drawn geometry plus an eye
        // height is where the action is.
        float ground = _heightRange is { } range ? range.Lowest : 0f;

        return (centreX, centreY, ground + PlayerEyeHeight);
    }

    /// <summary>World units the free camera moves per wheel notch.</summary>
    /// <remarks>
    /// **A distance, unlike flight, because a wheel notch IS a discrete event.** Thirty-two units is
    /// half a player's height. Key-driven flight used to work the same way and could not — a held
    /// key is a duration and became one in <see cref="FreeFlight"/> (B97) — but a notch has no
    /// duration to integrate over.
    /// </remarks>
    private const float FlySpeed = 32f;

    /// <summary>Roughly where a player's eyes are above the floor, in world units.</summary>
    /// <remarks>
    /// <c>VEC_VIEW</c> is 64 for a standing Source player, which is what a demo is usually watched
    /// from and a sensible height to arrive at.
    /// </remarks>
    private const float PlayerEyeHeight = 64f;

    private TopDownCamera MapCamera()
    {
        TopDownCamera fitted = TopDownCamera.Fit(
            [
                (_map!.MainBounds.MinX, _map.MainBounds.MinY),
                (_map.MainBounds.MaxX, _map.MainBounds.MaxY),
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
        return _heightRange is { } range
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
    public void ShowPositions(IReadOnlyList<(float X, float Y)> positions)
    {
        ArgumentNullException.ThrowIfNull(positions);

        if (positions.Count == 0)
        {
            _scene = [];
            return;
        }

        // With a map loaded the players are projected through the MAP's camera, so they land where
        // they actually are in the world. Fitting to the players instead would place them
        // correctly relative to each other and wrongly relative to everything around them.
        TopDownCamera camera = _map is not null && !_map.IsEmpty
            ? MapCamera()
            : TopDownCamera.Fit(
                positions,
                Math.Max(1, _viewport.ClientSize.Width),
                Math.Max(1, _viewport.ClientSize.Height));

        List<ScenePoint> points = new(positions.Count);

        foreach ((float worldX, float worldY) in positions)
        {
            (float x, float y) = camera.Project(worldX, worldY);
            points.Add(new ScenePoint(x, y, 1f, 0.85f, 0.3f));
        }

        _scene = points;
    }

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

        long sampledAt = Stopwatch.GetTimestamp();

        timeline.PlayersAt(tick, _players);
        timeline.PropsAt(tick, _props);

        _samplingTicks += Stopwatch.GetTimestamp() - sampledAt;

        // Packing is a no-op after the first sighting of each model, so this costs a dictionary
        // lookup per entity per frame once the demo has been running for a moment.
        // **Players become props, rather than getting a pipeline of their own.** A player is a
        // model at a pose, which is exactly what the prop path already draws, lights and
        // interpolates - and a second implementation would agree with the first only until one of
        // them gained a feature. The pose comes from the timeline, so they move and turn.
        _drawn.Clear();
        _drawn.AddRange(_props);

        // Cheap after the first call, and this is the first point where both the demo and the
        // game's archives are certain to be open.
        EnsureWeaponRoles();

        foreach (ScenePlayer player in _players)
        {
            if (PlayerModel(player) is not { } model)
            {
                continue;
            }

            _drawn.Add(new SceneProp(
                player.EntityIndex,
                model,
                SceneModelKind.Studio,
                new ScenePose
                {
                    X = player.X,
                    Y = player.Y,
                    Z = player.Z,

                    // **Yaw only.** A player model stands upright however far the eyes are pitched
                    // - the server feeds pitch to the animation state to aim the torso, not to tip
                    // the whole body (tf_player.cpp:2689). Rolling a player by their view would
                    // lay them on their side every time they looked up.
                    Yaw = player.Yaw,
                    Scale = 1f,

                    // Left unset here and chosen below, once the model is loaded. Choosing it now
                    // asks a set that has not been given this model yet, which answers -1 - and
                    // -1 is a real answer meaning "no such sequence", so it looks like a lookup
                    // that failed rather than one that ran too early.
                    Speed = player.Speed,

                    // **The crouch and ground bits, for choosing an activity.** Carried on the pose
                    // because the model has not been read yet and the activity lookup needs it, so
                    // the choice happens in a second pass below.
                    Flags = player.Flags,

                    // **Which weapon they are holding, as the suffix it drives.** Same reason as
                    // the flags: resolved here where the player is known, used a pass later where
                    // the model is.
                    Slot = _weaponRoles?.Suffix(player.WeaponClass, player.PlayerClass),

                    // The jump clock, for the push-off versus the float.
                    AirborneSeconds = player.AirborneSeconds,

                    // Where they are looking, which aims the torso through body_pitch.
                    EyePitch = player.EyePitch,

                    // The eyes and the twist. Yaw above is the FEET, which is what the body is
                    // drawn at; these two carry where the player is actually looking and how far
                    // the torso is turned to get there.
                    EyeYaw = player.EyeYaw,
                    AimYaw = player.AimYaw,

                    // Waist deep is where a jump becomes a swim.
                    WaterLevel = player.WaterLevel,

                    // **Both halves of the air-walk meet here.** The timeline says the player rose
                    // fast enough to start one; the class script says whether their class does it
                    // at all, and only the medic opts out. Neither layer can answer both.
                    Airwalking = player.Airwalking &&
                        (player.PlayerClass is not { } airwalkClass ||
                         _classModels?.Airwalks(airwalkClass) != false),

                    // **Which way the legs run.** A movement sequence is a blend grid and these
                    // are its coordinates; without them the grid's corner is taken, which is one
                    // fixed direction regardless of facing.
                    MoveX = player.MoveX,
                    MoveY = player.MoveY,

                    // **RED is skin 0 and BLU is skin 1**, which is the game's own convention:
                    // m_nSkin = ( team == TF_TEAM_RED ) ? 0 : 1. Without it every player draws in
                    // the model's first family, which is red - both teams in red.
                    //
                    // **Deliberately computed here rather than read from the entity, and that stays
                    // true now that m_nSkin IS retained by the scene layer.** For a player the
                    // client computes this itself: c_tf_player.cpp:712-719 assigns m_nSkin from
                    // m_iTeam while setting the model, and the field is marked FTYPEDESC_PRIVATE in
                    // the prediction data. It is client state derived from team, not a value the
                    // server sends for players.
                    //
                    // So this line is not made redundant by retaining the property - checked, on
                    // exactly the suspicion that it had been. Props are the opposite case: a
                    // capture point's skin comes from ownership on the server and must be read.
                    //
                    // Not reproduced here: the client's two skin OVERRIDES, applied straight after
                    // the lines above - AdjustSkinIndexForZombie for Halloween, and the gold
                    // ragdoll from TF_DMG_CUSTOM_GOLD_WRENCH.
                    Skin = player.Team == SceneTeams.Blu ? 1 : 0,
                }));
        }

        // **The engine does not draw the player whose eyes you are using**, and cosmetics merge
        // onto their wearer's bones, so the hat goes with them. Without this the first-person view
        // is the inside of the recorder's own model and a hat hanging over the lens — which is
        // exactly what the first capture showed. See FirstPersonVisibility.
        if (_firstPerson && FollowedEntity() is { } looking)
        {
            IReadOnlyList<SceneProp> visible = FirstPersonVisibility.Visible(_drawn, looking);

            if (visible.Count != _drawn.Count)
            {
                List<SceneProp> kept = [.. visible];
                _drawn.Clear();
                _drawn.AddRange(kept);
            }
        }

        bool grew = _models.Add(_drawn, ModelGeometry);

        // **Now the models are loaded, so a player's sequence can be chosen.** Nothing on the wire
        // carries one, and picking it needs the model's own merged sequence table - which only
        // exists after the model has been read.
        for (int index = 0; index < _drawn.Count; index++)
        {
            SceneProp prop = _drawn[index];

            if (prop.Pose.Speed is { } speed &&
                _models.SequenceFor(
                    prop.ModelPath,
                    speed,
                    prop.Pose.Flags,

                    // **True because the dead never reach here, not because death is ignored.**
                    // PlayerModel refuses a player the engine would not draw, and TF2 turns a dead
                    // player off with EF_NODRAW while a separate CTFRagdoll becomes the corpse.
                    //
                    // This comment previously claimed a ragdoll was already doing that job, which
                    // was false in both directions: nothing here draws ragdolls, and dead players
                    // WERE reaching this call. With their ground flag clear they were then given
                    // ACT_MP_JUMP_FLOAT, so seventeen seconds of a respawn drew a soldier falling
                    // through the air.
                    alive: true,

                    // The weapon's suffix, or the primary forms when nothing resolved it — which is
                    // what the engine falls back to as well.
                    slot: prop.Pose.Slot ?? "PRIMARY",

                    // Splits the jump into its push-off and its float.
                    airborneSeconds: prop.Pose.AirborneSeconds,

                    // Supersedes the jump for a fast-rising player.
                    airwalking: prop.Pose.Airwalking,

                    // Waist deep turns a jump into a swim.
                    waterLevel: prop.Pose.WaterLevel) is var chosen and >= 0)
            {
                _drawn[index] = prop with { Pose = prop.Pose with { Sequence = chosen } };
            }
        }

        if (grew && _device is { } device)
        {
            device.UploadModels(_models);

            // **Logged because a model that draws nothing looks exactly like one that was never
            // uploaded.** The counts separate the two: no vertices means the packing failed, and
            // vertices with no instances means the posing did.
            ViewerLog.Write(
                "render",
                $"entity models: {_models.Count} packed, {_models.Vertices.Count} vertices");

            // **Named, not counted.** A count says how many arrived and nothing about which are
            // missing, and "the health packs are not drawing" is a question about names.
            foreach (string path in _models.Paths)
            {
                string indices = string.Join(
                    ", ",
                    _models.Batches(path).Select(batch => $"{batch.MaterialIndex}x{batch.VertexCount}"));

                ViewerLog.Write(
                    "render",
                    $"  packed {path}: {indices} of {_assets?.Textures.Count ?? 0} textures");
            }
        }

        // **Demo time, from the demo's own tick interval rather than an assumed 66.67.** The
        // cycle of an animation is advanced by elapsed time, the way the client advances it in
        // C_BaseAnimating::FrameAdvance - the server never sends one, so a viewer replaying only
        // what was networked leaves every health pack frozen on its first frame.
        double seconds = tick * (_timeline.IntervalPerTick > 0f
            ? _timeline.IntervalPerTick
            : PlaybackClock.DefaultIntervalPerTick);

        long posedAt = Stopwatch.GetTimestamp();

        _models.Instances(_drawn, _instances, LightAt, SunAt, seconds);

        AddViewmodel(seconds);

        _posingTicks += Stopwatch.GetTimestamp() - posedAt;

        if (_instances.Count != _lastInstanceCount)
        {
            _lastInstanceCount = _instances.Count;

            // **Named and counted.** "Some models are missing" is a question about which, and a
            // total cannot answer it - a demo only carries what the recorder could see, so an
            // absent pickup may be correct rather than broken.
            string names = string.Join(
                ", ",
                _instances
                    .GroupBy(instance => instance.ModelPath, StringComparer.Ordinal)
                    .Select(group => $"{group.Count()}x{Path.GetFileNameWithoutExtension(group.Key)}"));

            // **How many were actually lit, not just how many were drawn.** A model with no cube
            // draws at full brightness and looks like a rendering fault; the count is what says
            // whether the leaf lookup found anything, without anyone having to judge by eye.
            int unlit = _instances.Count(instance => instance.Light == default(AmbientCube));

            ViewerLog.Write(
                "render",
                $"drawing {_instances.Count} posed models ({unlit} unlit): {names}");

            // The first medkit's actual transform. A model posed with a zero scale collapses to a
            // point and draws nothing, while every count above still reads correctly.

        }

        ShowPlayers(_players);
    }

    /// <summary>Draws the players recorded at one moment, coloured by team.</summary>
    /// <param name="players">The players, from the timeline.</param>
    /// <exception cref="ArgumentNullException"><paramref name="players"/> is null.</exception>
    /// <remarks>
    /// **Team two is RED and team three is BLU**, which is the engine's own numbering: nought is
    /// unassigned and one is spectator. A player whose team has not arrived yet is drawn grey
    /// rather than guessed at — a wrong team colour is worse than no colour, because it is read as
    /// information.
    /// </remarks>
    public void ShowPlayers(IReadOnlyList<ScenePlayer> players)
    {
        ArgumentNullException.ThrowIfNull(players);

        if (players.Count == 0)
        {
            _scene = [];
            return;
        }

        TopDownCamera camera = _map is not null && !_map.IsEmpty
            ? MapCamera()
            : TopDownCamera.Fit(
                [.. players.Select(player => (player.X, player.Y))],
                Math.Max(1, _viewport.ClientSize.Width),
                Math.Max(1, _viewport.ClientSize.Height));

        List<ScenePoint> points = new(players.Count + _props.Count);

        foreach (ScenePlayer player in players)
        {
            // **Spectators and the SourceTV camera are CTFPlayer entities too**, with real
            // positions that follow the action - so drawing everything puts convincing dots on the
            // map where nobody is standing.
            //
            // **The dead are skipped here for the same reason, and the marker pass is where that
            // is easiest to get wrong.** A player the engine would not draw has no model, and the
            // rule below is "no model means a dot" - so removing dead players from the model pass
            // alone would have turned every corpse into a marker gliding around the map behind
            // whoever it was spectating, which is the same defect in a cheaper primitive.
            if (!player.IsPlaying || !player.Drawn)
            {
                continue;
            }

            // **A marker only for a player with no model.** Once the class models draw, a dot on
            // top of one hides the very thing it was standing in for - which is exactly what
            // happened, and made a working render look like a failed one.
            if (PlayerModel(player) is not null)
            {
                continue;
            }

            (float x, float y) = camera.Project(player.X, player.Y);

            (float red, float green, float blue) = player.Team == SceneTeams.Red
                ? (0.90f, 0.31f, 0.27f)
                : (0.34f, 0.60f, 0.78f);

            points.Add(new ScenePoint(x, y, red, green, blue));
        }

        _scene = points;
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
        _viewport.Invalidate();
    }

    /// <summary>Captures the viewport to a stamped file beside the log.</summary>
    /// <remarks>
    /// The one place that decides where a screenshot goes, so the menu item and F12 cannot
    /// disagree about it. They were one copied expression apart, which is exactly how the viewer's
    /// two drawing paths drifted until one of them stopped showing decals.
    /// </remarks>
    public void CaptureViewportToFile()
    {
        string folder = Path.GetDirectoryName(ViewerLog.Path) ?? ".";

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
    public void LoadDemo(string path)
    {
        try
        {
            ViewerLog.Write("demo", $"opening {Path.GetFileName(path)}");
            _demo = LoadedDemo.Load(path);
            ViewerLog.Write(
                "demo",
                $"{_demo.MapName}, {_demo.LastTick} ticks, protocol {_demo.NetworkProtocol}" +
                (_demo.LengthWasMeasured ? ", length measured (truncated)" : string.Empty));
            _transport.SetDemoLength(_demo.LastTick);

            // **Its own guard, because a timeline is not worth the demo.** A file with no schema,
            // or one truncated mid-packet, still has a header, a map name and a length worth
            // showing - so a failure here costs the player positions and nothing else.
            try
            {
                using (ViewerLog.Time("demo", "building the position timeline"))
                {
                    _timeline = DemoTimeline.Build(File.ReadAllBytes(path));
                }

                ViewerLog.Write(
                    "demo",
                    $"{_timeline.Frames.Count} recorded moments, ticks {_timeline.FirstTick} to " +
                    $"{_timeline.LastTick}");

                // The weapon roles are NOT built here, and the first attempt was. See
                // EnsureWeaponRoles: the archives are opened later than this, so building here
                // silently produced nothing.
                _weaponRoles = null;

                // **The rate the recording server ran, not a constant.** It is a server setting, so
                // a box left at its default runs 33 where a configured one runs 66, and replaying
                // at the wrong rate reads as a slow or fast server rather than as a defect.
                _clock = new PlaybackClock(_timeline.IntervalPerTick, _demo.LastTick);

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
                    _transport.Playing = true;

                    ViewerLog.Write(
                        "demo", $"{AutoPlayVariable} is set; playback started at load");
                }

                float interval = _timeline.IntervalPerTick > 0f
                    ? _timeline.IntervalPerTick
                    : PlaybackClock.DefaultIntervalPerTick;

                string source = _timeline.IntervalPerTick > 0f
                    ? "from svc_ServerInfo"
                    : "the engine default - the demo never said";

                ViewerLog.Write(
                    "demo",
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{interval:F6}s per tick ({1f / interval:F1} per second), {source}"));

                // **What is actually going to be drawn, said once per demo.** Counts here are what
                // a defect looks like from the outside: a team colour that never arrives shows up
                // as "0 red, 0 blu" the moment the file opens, rather than as grey dots that have
                // to be noticed and then chased through a seven-minute suite.
                ScenePlayer[] roster =
                [
                    .. _timeline.Frames
                        .SelectMany(frame => frame.Players)
                        .GroupBy(player => player.EntityIndex)
                        .Select(group => group.First()),
                ];

                ViewerLog.Write(
                    "demo",
                    $"roster: {roster.Count(p => p.Team == SceneTeams.Red)} red, " +
                    $"{roster.Count(p => p.Team == SceneTeams.Blu)} blu, " +
                    $"{roster.Count(p => p.Team is SceneTeams.Spectator or SceneTeams.Unassigned)} watching, " +
                    $"{roster.Count(p => p.Team is null)} unknown, " +
                    $"{roster.Count(p => p.PlayerClass is >= 1 and <= 9)} of {roster.Length} with a class");

                int drawn = _timeline.Frames.Count == 0
                    ? 0
                    : _timeline.PlayersAt(_timeline.Frames[_timeline.Frames.Count / 2].Tick)
                        .Count(player => player.IsPlaying);

                ViewerLog.Write("demo", $"{drawn} players drawn at the midpoint of the demo");
            }
            catch (Exception failure) when (
                failure is ArgumentException or InvalidDataException or IOException)
            {
                _timeline = null;
                ViewerLog.Warn("demo", "building the position timeline", failure);
            }

            bool haveMap = LoadMap(_demo.MapName);
            _status.Text = _demo.Describe() + (haveMap ? string.Empty : "  (map not found)");

            // The first frame, so opening a demo shows the players standing where they started
            // rather than an empty map waiting for someone to press play.
            ShowPlayers(_timeline?.PlayersAt(_timeline.FirstTick) ?? []);
        }
        catch (Exception failure) when (failure is IOException or InvalidDataException)
        {
            _demo = null;
            _timeline = null;
            _clock = null;
            _scene = [];
            _transport.SetDemoLength(0);
            _status.Text = "Could not open " + System.IO.Path.GetFileName(path) + ": " + failure.Message;
        }
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
            ViewerLog.Write("render", "full screen: " + ForegroundProbe.Describe(Handle) + FocusHere());

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

                ViewerLog.Write(
                    "render",
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
            _device = Device3D.Create(_viewport.Handle, _viewport.ClientSize.Width, _viewport.ClientSize.Height);
            _device.VerticalSync = _settings.VerticalSync;

            ViewerLog.Write(
                "render",
                $"frame rate limit {(_settings.FrameRateLimit > 0 ? _settings.FrameRateLimit + " a second" : "none")}, " +
                $"vertical sync {(_settings.VerticalSync ? "on" : "off")}");
            ViewerLog.Write(
                "render",
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
            _keyReleases = new KeyReleaseFilter(_heldKeys);
            Application.AddMessageFilter(_keyReleases);

            _rendering = true;
        }
        catch (Exception failure) when (failure is InvalidOperationException or ArgumentException)
        {
            _status.Text = "Direct3D unavailable: " + failure.Message;
            ViewerLog.Warn("render", "creating the Direct3D device", failure);
        }
    }

    private void KeepOverlayOnTheViewport(object? sender, LayoutEventArgs e) =>
        _overlay?.PositionOver(_viewport);

    /// <summary>Longest frame playback will believe in, in seconds.</summary>
    private const double MaximumFrameSeconds = 0.1;

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
    private void AdvancePlayback()
    {
        if (!_transport.Playing || _clock is not { } clock || _timeline is null)
        {
            return;
        }

        double elapsed = _playWatch.Elapsed.TotalSeconds;

        if (elapsed <= 0)
        {
            return;
        }

        _playWatch.Restart();

        // **A stall is not elapsed playback time.** Loading a map, dragging the window by its title
        // bar or a world rebuild all stop the loop for a while, and feeding that whole gap to the
        // clock teleports the demo forward by however long the hitch was. Capping the step turns a
        // hitch into a brief slowdown instead, which is what an engine does with its frame time.
        elapsed = Math.Min(elapsed, MaximumFrameSeconds);
        clock.Advance(elapsed);

        _transport.ShowTick(clock.Tick);

        // **The clock's fractional position, not its whole tick.** Truncating here would snap
        // every pose to the last packet and make the interpolation layer a no-op that still
        // passed every one of its own tests.
        ShowMoment(clock.Position);

        // Whichever end it is travelling towards: stopping only at the end would leave reverse
        // playback spinning against tick zero, still claiming to play.
        if ((clock.TimeScale > 0 && clock.AtEnd) || (clock.TimeScale < 0 && clock.AtStart))
        {
            _transport.Playing = false;
        }
    }

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
        do
        {
            if (!FrameIsDue())
            {
                WaitForTheNextFrame();
                continue;
            }

            RenderFrame();
            CountFrame();
        }
        while (!MessageQueue.HasWork());
    }

    /// <summary>Flight keys currently held down.</summary>
    /// <remarks>
    /// **Held state, because a keystroke is not a duration** (B97). Added on the key down message
    /// and removed on the key up, so the frame loop can ask what is down right now instead of
    /// inferring it from how fast Windows repeats.
    /// </remarks>
    private readonly HashSet<Keys> _heldKeys = [];

    /// <summary>Times the camera's frames, which run whether or not the demo is playing.</summary>
    private readonly Stopwatch _flyWatch = Stopwatch.StartNew();

    /// <summary>The key-release filter, kept so it can be removed on shutdown.</summary>
    private KeyReleaseFilter? _keyReleases;

    /// <summary>The longest frame since the rate was last reported, in seconds.</summary>
    private double _longestFrameSeconds;

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

    /// <summary>Whether either Shift key is down, for the speed multiplier.</summary>
    /// <remarks>
    /// Read from <see cref="Control.ModifierKeys"/> rather than tracked, because Shift alone is not
    /// a flight key and never enters the held set — and a modifier's state is exactly what WinForms
    /// already exposes.
    /// </remarks>
    private static bool IsShiftHeld() => (ModifierKeys & Keys.Shift) != 0;

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
    private void ReleaseHeldKeys() => _heldKeys.Clear();

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
    private sealed class KeyReleaseFilter(HashSet<Keys> held) : IMessageFilter
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
                held.Remove((Keys)(int)m.WParam & Keys.KeyCode);
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
        ViewerLog.Write(
            "render",
            $"{_framesDrawn / elapsed:0.#} frames a second, " +
            $"longest {_longestFrameSeconds * 1000d:0.##} ms" +
            (_transport.Playing ? ", playing" : ", paused") +
            (_freeLook && _heldKeys.Count > 0 ? ", flying" : string.Empty) +
            $"; sampling {_samplingTicks / (double)Stopwatch.Frequency * 1000d:0.#} ms" +
            $", posing {_posingTicks / (double)Stopwatch.Frequency * 1000d:0.#} ms" +
            $" (lighting {_models.LightingTicks / (double)Stopwatch.Frequency * 1000d:0.#} ms)" +
            " of the second");

        _models.LightingTicks = 0;

        _framesDrawn = 0;
        _rateReportedAt = now;
        _longestFrameSeconds = 0d;
        _samplingTicks = 0;
        _posingTicks = 0;
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

        // Every frame's duration passes through here, so this is where the worst one is noticed.
        _longestFrameSeconds = Math.Max(_longestFrameSeconds, Math.Min(seconds, MaximumFrameSeconds));

        if (!_freeLook || _heldKeys.Count == 0)
        {
            return;
        }

        // A stall is not flight time, for the same reason it is not playback time: a map load or a
        // window drag would otherwise fling the camera across the map when the loop resumes.
        seconds = Math.Min(seconds, MaximumFrameSeconds);

        (float X, float Y, float Z) moved = FreeFlight.Movement(
            _heldKeys, seconds, _freeAngles.Pitch, _freeAngles.Yaw, IsShiftHeld());

        if (moved == (0f, 0f, 0f))
        {
            return;
        }

        (float X, float Y, float Z) where = _freeOrigin ?? FreeLookCamera().Origin;

        _freeOrigin = (where.X + moved.X, where.Y + moved.Y, where.Z + moved.Z);

        // The view, and nothing else. Flight only happens in the free camera, where the map's
        // screen-space projection is not what is being drawn (B98).
        UploadCamera();
    }

    private void RenderFrame()
    {
        FlyCamera();

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

        AdvancePlayback();
        TakeAutomaticShot();

        _device?.DrawFrame(
            BackgroundRed,
            BackgroundGreen,
            BackgroundBlue,
            _mapFill,
            _outline.Checked ? _mapLines : [],
            _scene,
            _instances,
            _viewmodelInstances,
            _viewmodelCamera?.ToMatrix());

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

            ViewerLog.Write(
                "render",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"full screen {(IsFullScreen ? "on" : "off")} took " +
                    $"{clock.Elapsed.TotalMilliseconds:F0} ms to the first frame at " +
                    $"{_viewport.ClientSize.Width}x{_viewport.ClientSize.Height}"));
        }
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
        if (_map is null || _map.IsEmpty)
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

    private void OnViewportMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _dragFrom = e.Location;
        }
    }

    private void OnViewportMouseMove(object? sender, MouseEventArgs e)
    {
        if (_dragFrom is not { } from || _map is null || _map.IsEmpty)
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

    private void OnViewportMouseUp(object? sender, MouseEventArgs e) => _dragFrom = null;

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
        if (keyData is Keys.PageUp or Keys.PageDown or Keys.Home && _map is not null)
        {
            float step = keyData == Keys.PageDown ? 0.02f : -0.02f;

            _heightCut = keyData == Keys.Home ? 0f : Math.Clamp(_heightCut + step, 0f, 0.95f);

            ViewerLog.Write(
                "render",
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
        if (_freeLook && FreeFlight.IsFlightKey(keyData & Keys.KeyCode))
        {
            _heldKeys.Add(keyData & Keys.KeyCode);
            return true;
        }

        // **F toggles the free camera.** The map view is what a demo is normally watched from, so
        // this is a mode rather than a replacement — and switching keeps the same subject in the
        // middle, since the free camera starts where the map view was looking.
        // **V enters and leaves the first-person view.** Next to F for the free camera, and chosen
        // because it is what TF2 itself does not use for anything a demo watcher presses.
        if (keyData == Keys.V)
        {
            return ToggleFirstPerson();
        }

        if (keyData == Keys.F)
        {
            _cameraMode = _freeLook ? CameraMode.Map : CameraMode.Free;

            // Forgotten on the way out, so entering again places the camera at whatever the map
            // view is looking at NOW rather than where it was flown to half a match ago.
            if (!_freeLook)
            {
                _freeOrigin = null;

                // Nothing is flying any more, and a key still recorded as held would move the
                // camera the moment the free view was entered again.
                ReleaseHeldKeys();
            }

            _worldIsStale = true;
            _viewport.Invalidate();

            ViewerLog.Write(
                "render",
                _freeLook
                    ? $"free camera on: pitch {_freeAngles.Pitch:0.#}, yaw {_freeAngles.Yaw:0.#}, " +
                      $"distance {FreeEntryDistance:0}"
                    : "free camera off, back to the map view");

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
            ViewerLog.Write(
                "render",
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

            _device?.Dispose();
            _device = null;

            ViewerLog.Write(
                "render",
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
            _outline.Dispose();
            _surfaceColours.Dispose();
            _fullScreen.Dispose();
        }

        base.Dispose(disposing);
    }
}
