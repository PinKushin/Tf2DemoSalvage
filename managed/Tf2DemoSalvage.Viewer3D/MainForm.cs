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

    /// <summary>Where every player stood, for every moment the demo recorded.</summary>
    /// <summary>The map's BSP tree, for finding which leaf a model stands in.</summary>
    private BspLeafTree? _leaves;

    /// <summary>The ambient light each leaf holds, indexed by leaf.</summary>
    private IReadOnlyList<AmbientSamples> _ambient = [];

    /// <summary>The map's sun, when it has one.</summary>
    private BspWorldLight? _sun;

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
    private bool _freeLook;

    /// <summary>Pitch and yaw of the free camera, in degrees.</summary>
    /// <remarks>
    /// Starts at a shallow angle rather than at zero: a camera on the horizon looking across a map
    /// shows mostly wall, and the first thing anyone wants from this view is to see whether the
    /// players are standing up.
    /// </remarks>
    private (float Pitch, float Yaw) _freeAngles = (35f, 0f);

    /// <summary>How far the free camera sits from what it is looking at, in world units.</summary>
    private float _freeDistance = 800f;

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
            AccessibleName = "Full screen",
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

        ToolStripMenuItem view = new("&View") { Name = "ViewMenu", AccessibleName = "View menu" };
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
                    _sun = BspWorldLights.Sun(BspWorldLights.Read(bytes));

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
                        WornModelPaths());
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
                    (_freeLook ? FreeLookCamera().ToMatrix() : camera.ToMatrix()),
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
                        _overlays);
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

        return leaf >= 0 && leaf < _ambient.Count
            ? _ambient[leaf].Nearest(x, y, z)
            : default;
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
        player.IsPlaying && player.PlayerClass is { } playerClass
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

        return paths;
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
    private FreeCamera FreeLookCamera()
    {
        (float centreX, float centreY) = _lookingAt ?? MapCamera().Centre;

        float height = _heightRange is { } range
            ? (range.Lowest + range.Highest) / 2f
            : 0f;

        return FreeCamera.Orbiting(
            (centreX, centreY, height),
            _freeAngles.Pitch,
            _freeAngles.Yaw,
            _freeDistance,
            Math.Max(1, _viewport.ClientSize.Width) /
                (float)Math.Max(1, _viewport.ClientSize.Height));
    }

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

        // **D21: the camera projects height, so it has to know the range.** The geometry carries
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

        timeline.PlayersAt(tick, _players);
        timeline.PropsAt(tick, _props);

        // Packing is a no-op after the first sighting of each model, so this costs a dictionary
        // lookup per entity per frame once the demo has been running for a moment.
        // **Players become props, rather than getting a pipeline of their own.** A player is a
        // model at a pose, which is exactly what the prop path already draws, lights and
        // interpolates - and a second implementation would agree with the first only until one of
        // them gained a feature. The pose comes from the timeline, so they move and turn.
        _drawn.Clear();
        _drawn.AddRange(_props);

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

                    // **Which way the legs run.** A movement sequence is a blend grid and these
                    // are its coordinates; without them the grid's corner is taken, which is one
                    // fixed direction regardless of facing.
                    MoveX = player.MoveX,
                    MoveY = player.MoveY,

                    // **RED is skin 0 and BLU is skin 1**, which is the game's own convention:
                    // m_nSkin = ( team == TF_TEAM_RED ) ? 0 : 1. Without it every player draws in
                    // the model's first family, which is red - both teams in red.
                    Skin = player.Team == SceneTeams.Blu ? 1 : 0,
                }));
        }

        bool grew = _models.Add(_drawn, ModelGeometry);

        // **Now the models are loaded, so a player's sequence can be chosen.** Nothing on the wire
        // carries one, and picking it needs the model's own merged sequence table - which only
        // exists after the model has been read.
        for (int index = 0; index < _drawn.Count; index++)
        {
            SceneProp prop = _drawn[index];

            if (prop.Pose.Speed is { } speed &&
                _models.SequenceFor(prop.ModelPath, speed) is var chosen and >= 0)
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

        _models.Instances(_drawn, _instances, LightAt, SunAt, seconds);

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
            if (!player.IsPlaying)
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

                // **The rate the recording server ran, not a constant.** It is a server setting, so
                // a box left at its default runs 33 where a configured one runs 66, and replaying
                // at the wrong rate reads as a slow or fast server rather than as a defect.
                _clock = new PlaybackClock(_timeline.IntervalPerTick, _demo.LastTick);

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
            _overlay.Show(this);

            // Positioned AFTER the layout settles, not now. At this point the form has changed
            // border style and window state but has not re-laid-out, so the viewport still reports
            // its windowed rectangle - and the overlay lands wherever the bottom of the small
            // viewport used to be, which on a maximised window is the middle of the screen.
            BeginInvoke(() => _overlay?.PositionOver(_viewport));

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

        ViewerLog.Write("render", $"{_framesDrawn / elapsed:0.#} frames a second");

        _framesDrawn = 0;
        _rateReportedAt = now;
    }

    private void RenderFrame()
    {
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
            _instances);

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
        if (_freeLook)
        {
            _freeDistance = Math.Clamp(_freeDistance / step, 100f, 20_000f);
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

        // **F toggles the free camera.** The map view is what a demo is normally watched from, so
        // this is a mode rather than a replacement — and switching keeps the same subject in the
        // middle, since the free camera orbits whatever the map view was centred on.
        if (keyData == Keys.F)
        {
            _freeLook = !_freeLook;
            _worldIsStale = true;
            _viewport.Invalidate();

            ViewerLog.Write(
                "render",
                _freeLook
                    ? $"free camera on: pitch {_freeAngles.Pitch:0.#}, yaw {_freeAngles.Yaw:0.#}, " +
                      $"distance {_freeDistance:0}"
                    : "free camera off, back to the map view");

            return true;
        }

        if (keyData == Keys.F12)
        {
            CaptureViewport(Path.Combine(
                Path.GetDirectoryName(ViewerLog.Path) ?? ".",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"shot-{DateTime.Now:yyyyMMdd-HHmmss}.png")));

            return true;
        }

        if (keyData == Keys.Escape && IsFullScreen)
        {
            SetFullScreen(false);
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

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

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
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
