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
    private DemoTimeline? _timeline;

    /// <summary>Reused between frames; PlayersAt and PropsAt fill them rather than allocating.</summary>
    private readonly List<ScenePlayer> _players = [];

    private readonly List<SceneProp> _props = [];

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
                    _assets = MapAssets.Load(bytes, _archives, (int)_settings.TextureQuality);
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
                _device.SetCamera(camera.ToMatrix(), _surfaceColours.Checked, _heightCut);

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

        return _lookingAt is { } centre ? zoomed.LookingAt(centre.X, centre.Y) : zoomed;
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

        // **Entities first, so players draw over them.** A ScenePoint carries no depth - there is
        // no Z on it and nothing sorts these - so the list order IS the draw order, and appending
        // the entities afterwards put every dropped weapon and pickup on top of the people. The
        // players are the thing being watched; everything else is context behind them.
        AppendProps(points, camera);

        foreach (ScenePlayer player in players)
        {
            // **Spectators and the SourceTV camera are CTFPlayer entities too**, with real
            // positions that follow the action - so drawing everything puts convincing dots on the
            // map where nobody is standing.
            if (!player.IsPlaying)
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

    /// <summary>Adds a marker for every model-bearing entity at the current moment.</summary>
    /// <remarks>
    /// **Markers rather than models, and only for now.** The renderer bakes the world into one
    /// buffer at load, which suits brushwork and static props and cannot express a thing that
    /// moves; drawing real models needs a per-object transform in the shader. Until then a marker
    /// says truthfully where each entity is, which is what makes the tracks visible at all.
    ///
    /// Deliberately dimmer than the team colours: these are rockets, pickups, doors and dropped
    /// weapons, and they outnumber the players by an order of magnitude. Drawn as brightly they
    /// would bury the thing the viewer is for.
    /// </remarks>
    private void AppendProps(List<ScenePoint> points, TopDownCamera camera)
    {
        foreach (SceneProp prop in _props)
        {
            // Brush models are doors and lifts - parts of the map rather than things in it - and a
            // marker at a door's origin says nothing a viewer wants. Sprites are glows.
            if (prop.Kind != SceneModelKind.Studio)
            {
                continue;
            }

            (float x, float y) = camera.Project(prop.Pose.X, prop.Pose.Y);

            points.Add(new ScenePoint(x, y, 0.55f, 0.55f, 0.50f));
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
            RenderFrame();
        }
        while (!MessageQueue.HasWork());
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

        // The clear colour is the whole picture for now, and that is deliberate: it is the
        // evidence that the swap chain is bound to this panel and presenting. A viewport that
        // stays the form's grey looks identical whether the device failed or simply drew nothing.
        _device?.DrawFrame(
            0.06f,
            0.07f,
            0.09f,
            _mapFill,
            _outline.Checked ? _mapLines : [],
            _scene);

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
