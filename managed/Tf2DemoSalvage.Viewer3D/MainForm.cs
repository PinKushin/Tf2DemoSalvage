using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

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

    /// <summary>Automation id of the export button.</summary>
    public const string ExportButtonId = "ExportButton";

    /// <summary>Automation id of the compile button.</summary>
    public const string CompileButtonId = "CompileButton";

    /// <summary>Automation id of the View &gt; Full screen item.</summary>
    public const string FullScreenItemId = "FullScreenMenuItem";

    private readonly Panel _viewport;
    private readonly ToolStripStatusLabel _status;
    private readonly FlowLayoutPanel _actions;
    private readonly TransportBar _transport;
    private readonly ListView _playlist;
    private readonly DemoLibrary _library = new();

    private LoadedDemo? _demo;

    private readonly ToolStripMenuItem _fullScreen;

    private Device3D? _device;
    private bool _rendering;
    private OverlayWindow? _overlay;
    private FormBorderStyle _borderBeforeFullScreen;
    private FormWindowState _stateBeforeFullScreen;
    private Rectangle _boundsBeforeFullScreen;

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
        _playlist = new ListView
        {
            Name = PlaylistId,
            AccessibleName = "Playlist",
            AccessibleDescription = "Demos available to play, grouped by folder.",
            Dock = DockStyle.Right,
            Width = 280,
            View = View.Details,
            FullRowSelect = true,
            HideSelection = false,
            MultiSelect = true,
            ShowGroups = true,
        };
        _playlist.Columns.Add("Demo", 260);

        // Double-click and Enter both load, matching how a file browser and a video player behave.
        // Selecting alone does not: browsing a playlist should not read headers off disk.
        _playlist.ItemActivate += (_, _) => LoadSelected();

        _transport = new TransportBar();

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

        ToolStripMenuItem view = new("&View") { Name = "ViewMenu", AccessibleName = "View menu" };
        view.DropDownItems.Add(_fullScreen);

        file.DropDownItems.Add(open);
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(exit);
        menu.Items.Add(file);
        menu.Items.Add(view);

        // Added in reverse z-order: WinForms docks the LAST control added first, so Fill must go
        // in before every docked edge or the viewport ends up underneath them.
        // Docking is applied in REVERSE add order, so the last bottom control added claims the
        // outermost edge. Adding transport then actions then the status strip puts them on screen
        // as transport, actions, status from the middle outward - the action row under the play
        // bar, which is what it is about.
        Controls.Add(_viewport);
        Controls.Add(_playlist);
        Controls.Add(_transport);
        Controls.Add(_actions);
        Controls.Add(statusStrip);
        Controls.Add(menu);
        MainMenuStrip = menu;

        if (initialPaths.Length > 0)
        {
            AddToLibrary(initialPaths);
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
        if (_playlist.SelectedItems.Count == 0)
        {
            return;
        }

        LoadDemo((string)_playlist.SelectedItems[0].Tag!);
    }

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
            _demo = LoadedDemo.Load(path);
            _transport.SetDemoLength(_demo.LastTick);
            _status.Text = _demo.Describe();
        }
        catch (Exception failure) when (failure is IOException or InvalidDataException)
        {
            _demo = null;
            _transport.SetDemoLength(0);
            _status.Text = "Could not open " + System.IO.Path.GetFileName(path) + ": " + failure.Message;
        }
    }

    /// <summary>The playback controls, exposed for the tests that address them.</summary>
    public TransportBar Transport => _transport;

    /// <summary>Whether the viewport is filling the screen.</summary>
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

        if (fullScreen)
        {
            _borderBeforeFullScreen = FormBorderStyle;
            _stateBeforeFullScreen = WindowState;

            // **Bounds too, not just border and state.** Removing the border and putting it back
            // recalculates the client area from the same outer size, so the client - and the
            // viewport filling it - comes back NARROWER than it went. Measured by the UI test:
            // 984 pixels wide before, 968 after, and it loses another 16 on every toggle.
            _boundsBeforeFullScreen = Bounds;

            MainMenuStrip!.Visible = false;
            _actions.Visible = false;
            _playlist.Visible = false;
            Controls.Remove(_transport);

            FormBorderStyle = FormBorderStyle.None;

            // Normal first: a maximised window ignores a border-style change until it is
            // restored, so going straight to maximised leaves the old frame on screen.
            WindowState = FormWindowState.Normal;
            WindowState = FormWindowState.Maximized;

            _overlay = new OverlayWindow(_transport) { Height = _transport.Height + 8 };
            _overlay.Show(this);

            // Positioned AFTER the layout settles, not now. At this point the form has changed
            // border style and window state but has not re-laid-out, so the viewport still reports
            // its windowed rectangle - and the overlay lands wherever the bottom of the small
            // viewport used to be, which on a maximised window is the middle of the screen.
            BeginInvoke(() => _overlay?.PositionOver(_viewport));
            return;
        }

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
        MainMenuStrip!.Visible = true;
        _actions.Visible = true;
        _playlist.Visible = true;
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
            _status.Text = "Direct3D ready.";

            // Idle-driven rather than a timer: WinForms raises Idle whenever the message queue
            // empties, so the viewport redraws as fast as the UI allows and stops entirely while
            // the user is dragging a menu around. A timer would keep presenting underneath it.
            Application.Idle += OnIdle;
            _rendering = true;
        }
        catch (Exception failure) when (failure is InvalidOperationException or ArgumentException)
        {
            _status.Text = "Direct3D unavailable: " + failure.Message;
        }
    }

    private void OnIdle(object? sender, EventArgs e)
    {
        // The clear colour is the whole picture for now, and that is deliberate: it is the
        // evidence that the swap chain is bound to this panel and presenting. A viewport that
        // stays the form's grey looks identical whether the device failed or simply drew nothing.
        _device?.ClearAndPresent(0.06f, 0.07f, 0.09f);
    }

    private void OnViewportResize(object? sender, EventArgs e)
    {
        _overlay?.PositionOver(_viewport);

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

        RefreshPlaylist();

        _status.Text = _library.Entries.Count switch
        {
            0 => "No demos found.",
            1 => "1 demo.",
            int count => string.Create(CultureInfo.InvariantCulture, $"{count} demos."),
        };
    }

    /// <summary>Rebuilds the playlist, one group per folder.</summary>
    private void RefreshPlaylist()
    {
        _playlist.BeginUpdate();
        _playlist.Items.Clear();
        _playlist.Groups.Clear();

        foreach (IGrouping<string, DemoEntry> folder in _library.Entries.GroupBy(e => e.Folder))
        {
            ListViewGroup group = new(folder.Key) { Name = folder.Key };
            _playlist.Groups.Add(group);

            foreach (DemoEntry entry in folder)
            {
                // The full path rides along in Tag: the list shows a file name, and two demos in
                // different folders routinely share one.
                _playlist.Items.Add(new ListViewItem(entry.Name, group) { Tag = entry.Path });
            }
        }

        _playlist.EndUpdate();
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
            if (_rendering)
            {
                // Before the device goes: an Idle handler that outlives the swap chain presents
                // into freed memory, and that is a crash on exit rather than a leak.
                Application.Idle -= OnIdle;
                _rendering = false;
            }

            _device?.Dispose();
            _device = null;

            // Both are in Controls, which base.Dispose already walks - but the analyzer cannot
            // see that ownership, and stating it costs nothing and is true.
            _viewport.Dispose();
            _status.Dispose();
            _actions.Dispose();
            _transport.Dispose();
            _playlist.Dispose();
            _overlay?.Dispose();
            _fullScreen.Dispose();
        }

        base.Dispose(disposing);
    }
}
