using System;
using System.Globalization;
using System.Windows.Forms;

namespace Tf2DemoSalvage.Viewer3D;

/// <summary>
/// Playback controls: play/pause, a scrub bar and the current tick.
/// </summary>
/// <remarks>
/// **Its own control rather than fields on the form**, because the form is already the thing that
/// owns the device, the menu and the layout, and a transport bar is a coherent piece with its own
/// state. It also means the automation ids sit next to the controls they name.
///
/// The bar holds no timer and drives no playback. It reports what the user asked for through
/// <see cref="Scrubbed"/> and <see cref="PlayPauseToggled"/>, and shows whatever tick it is told
/// about — so playback can be driven by the render loop later without this control changing.
/// </remarks>
internal sealed class TransportBar : UserControl, IPlaybackView
{
    /// <summary>Automation id of the play/pause button.</summary>
    public const string PlayButtonId = "PlayPauseButton";

    /// <summary>Automation id of the scrub bar.</summary>
    public const string ScrubBarId = "ScrubBar";

    /// <summary>Automation id of the tick readout.</summary>
    public const string TickLabelId = "TickLabel";

    /// <summary>Automation id of the jump-to-start button.</summary>
    public const string StartButtonId = "StartButton";

    /// <summary>Automation id of the slower/reverse button.</summary>
    public const string SlowerButtonId = "SlowerButton";

    /// <summary>Automation id of the faster button.</summary>
    public const string FasterButtonId = "FasterButton";

    /// <summary>Automation id of the jump-to-end button.</summary>
    public const string EndButtonId = "EndButton";

    /// <summary>Automation id of the speed readout.</summary>
    public const string SpeedLabelId = "SpeedLabel";

    /// <summary>The speeds the shuttle buttons step through.</summary>
    /// <remarks>
    /// **Negative speeds are real here and impossible in TF2.** The engine streams a demo forward
    /// and each snapshot is a delta on the last, so it has nothing to step back into. This viewer
    /// decodes the whole demo to absolute positions first, so reverse costs what forward costs.
    ///
    /// The ladder is the one a video editor uses — halves and doubles either side of one — and it
    /// omits zero, because stopping is what the play button is for.
    /// </remarks>
    private static readonly double[] Speeds =
        [-4, -2, -1, -0.5, -0.25, 0.25, 0.5, 1, 2, 4, 8];

    private readonly Button _start;
    private readonly Button _slower;
    private readonly Button _playPause;
    private readonly Button _faster;
    private readonly Button _end;
    private readonly TrackBar _scrub;
    private readonly Label _speed;
    private readonly Label _tick;

    private int _speedIndex = Array.IndexOf(Speeds, 1.0);

    private bool _playing;
    private bool _suppressScrubEvent;

    /// <summary>Builds the transport bar.</summary>
    public TransportBar()
    {
        Name = "TransportBar";
        AccessibleName = "Playback controls";
        Height = 44;
        Dock = DockStyle.Bottom;

        // **Laid out the way a video player is**, because that is what someone reaching for it
        // expects: jump to start, shuttle down, play, shuttle up, jump to end, then the scrub bar
        // filling the width, then the readouts at the right.
        _start = Shuttle(StartButtonId, "|<", "Jump to start", "Moves playback to the first tick.");
        _slower = Shuttle(
            SlowerButtonId,
            "<<",
            "Slower or reverse",
            "Steps the speed down, through one quarter to reverse.");

        _playPause = new Button
        {
            Name = PlayButtonId,
            AccessibleName = "Play",
            AccessibleDescription = "Starts or pauses playback of the loaded demo.",
            Text = "Play",
            Width = 72,
            Top = 8,
            Enabled = false,
        };
        _playPause.Click += (_, _) => TogglePlayingByUser();

        _faster = Shuttle(
            FasterButtonId, ">>", "Faster", "Steps the speed up, to a maximum of eight times.");
        _end = Shuttle(EndButtonId, ">|", "Jump to end", "Moves playback to the last tick.");

        _speed = new Label
        {
            Name = SpeedLabelId,

            // Live readout, so no fixed AccessibleName - UpdateSpeedLabel keeps both in step, and
            // it is called at the end of this constructor to set the starting values. Both were
            // written out by hand here as well, in a THIRD format ("speed 1x" against the
            // "speed 1 times" every later update produces), so a screen reader heard one wording
            // before the first speed change and a different one after it. A UI test asserting on
            // the readout was written against this literal and failed the moment the speed moved,
            // which read as a broken transport bar.
            Text = "1x",
            AutoSize = true,
            Top = 12,
            Anchor = AnchorStyles.Right | AnchorStyles.Top,
        };

        _scrub = new TrackBar
        {
            Name = ScrubBarId,
            AccessibleName = "Scrub bar",
            AccessibleDescription = "Moves playback to a tick in the demo.",
            Left = 96,
            Top = 4,
            Width = 600,
            Minimum = 0,
            Maximum = 0,
            TickStyle = TickStyle.None,
            Enabled = false,

            // Anchored so the bar grows with the window while the button and readout stay put.
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
        };
        _scrub.ValueChanged += OnScrubValueChanged;

        // Wired here rather than beside the buttons: these lambdas read _scrub, and the compiler
        // is right that it does not exist yet at the point the buttons are built.
        _start.Click += (_, _) => Seek(0);
        _end.Click += (_, _) => Seek(_scrub.Maximum);
        _slower.Click += (_, _) => StepSpeed(-1);
        _faster.Click += (_, _) => StepSpeed(1);

        _tick = new Label
        {
            Name = TickLabelId,

            // Same reason as the status label: a fixed AccessibleName here would hide the tick
            // it exists to report. UpdateTickLabel keeps the two in step.
            AccessibleName = "tick 0 / 0",
            Text = "tick 0 / 0",
            AutoSize = true,
            Top = 12,
            Anchor = AnchorStyles.Right | AnchorStyles.Top,
        };

        Controls.Add(_start);
        Controls.Add(_slower);
        Controls.Add(_playPause);
        Controls.Add(_faster);
        Controls.Add(_end);
        Controls.Add(_scrub);
        Controls.Add(_speed);
        Controls.Add(_tick);
        Resize += (_, _) => LayoutChildren();

        // One source of truth for the readout, including its starting state.
        UpdateSpeedLabel();
    }

    /// <summary>Raised when the user moves the scrub bar.</summary>
    public event EventHandler<TickEventArgs>? Scrubbed;

    /// <summary>Raised when playback is started or paused.</summary>
    public event EventHandler<PlayingEventArgs>? PlayPauseToggled;

    /// <summary>Raised when the speed changes; negative means reverse.</summary>
    public event EventHandler<SpeedEventArgs>? SpeedChanged;

    /// <summary>Whether playback is running.</summary>
    /// <remarks>
    /// Not designer-serialised: this is runtime state, not a property someone sets in a designer,
    /// and WFO1000 asks every settable control property to say which it is.
    /// </remarks>
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Playing
    {
        get => _playing;
        set
        {
            if (_playing == value)
            {
                return;
            }

            _playing = value;

            // Both the label and the accessible name change: automation reads the latter, and a
            // button that says "Pause" while announcing "Play" is a control that tests and users
            // disagree about.
            _playPause.Text = _playing ? "Pause" : "Play";
            _playPause.AccessibleName = _playing ? "Pause" : "Play";
        }
    }

    /// <summary>Toggles playback as the USER, raising <see cref="PlayPauseToggled"/>.</summary>
    /// <remarks>
    /// **The setter above deliberately does not raise, and this is why.** It used to, which
    /// conflated two different things: the user pressing the button, and somebody assigning the
    /// property. `SetDemoLength` assigns it, and the presenter assigns it when playback reaches an
    /// end — so a raising setter meant the presenter re-entered its own handler through the control
    /// it had just updated.
    ///
    /// The control already drew this distinction for ticks: `ShowTick` moves the readout "without
    /// raising TickChanged". Playing simply never got the same treatment, and the gap only became
    /// visible when `IPlaybackView` had to write the rule down (D62).
    /// </remarks>
    private void TogglePlayingByUser()
    {
        Playing = !Playing;
        PlayPauseToggled?.Invoke(this, new PlayingEventArgs(_playing));
    }

    /// <summary>The tick currently shown.</summary>
    public int CurrentTick => _scrub.Value;

    /// <summary>The last tick the loaded demo reaches.</summary>
    public int LastTick => _scrub.Maximum;

    /// <summary>Enables the controls for a demo of the given length.</summary>
    /// <param name="lastTick">Highest tick in the demo; zero disables playback.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="lastTick"/> is negative.</exception>
    public void SetDemoLength(int lastTick)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(lastTick);

        // Suppressed so loading a demo does not look like the user scrubbing to zero, which would
        // otherwise fire a seek before anything is ready to serve one.
        _suppressScrubEvent = true;
        _scrub.Maximum = lastTick;
        _scrub.Value = 0;
        _suppressScrubEvent = false;

        bool playable = lastTick > 0;

        foreach (Control control in (Control[])[_start, _slower, _faster, _end])
        {
            control.Enabled = playable;
        }

        _scrub.Enabled = playable;
        _playPause.Enabled = playable;
        Playing = false;
        UpdateTickLabel();
    }

    /// <summary>Moves the readout and scrub bar without raising <see cref="Scrubbed"/>.</summary>
    /// <param name="tick">Tick to show; clamped to the demo's range.</param>
    /// <remarks>
    /// Used by playback to report where it has got to. Raising the event here would feed the
    /// control's own output back in as a seek request on every frame.
    /// </remarks>
    public void ShowTick(int tick)
    {
        _suppressScrubEvent = true;
        _scrub.Value = Math.Clamp(tick, _scrub.Minimum, _scrub.Maximum);
        _suppressScrubEvent = false;
        UpdateTickLabel();
    }

    private void OnScrubValueChanged(object? sender, EventArgs e)
    {
        UpdateTickLabel();

        if (!_suppressScrubEvent)
        {
            Scrubbed?.Invoke(this, new TickEventArgs(_scrub.Value));
        }
    }

    private void UpdateTickLabel()
    {
        _tick.Text = string.Create(
            CultureInfo.InvariantCulture, $"tick {_scrub.Value} / {_scrub.Maximum}");
        _tick.AccessibleName = _tick.Text;
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Every one is in Controls, which the base walks - but the analyzer cannot see that
            // ownership, and saying so costs nothing and is true.
            _start.Dispose();
            _slower.Dispose();
            _playPause.Dispose();
            _faster.Dispose();
            _end.Dispose();
            _scrub.Dispose();
            _speed.Dispose();
            _tick.Dispose();
        }

        base.Dispose(disposing);
    }

    private void LayoutChildren()
    {
        const int margin = 8;
        const int gap = 4;

        int left = margin;

        foreach (Control control in (Control[])[_start, _slower, _playPause, _faster, _end])
        {
            control.Left = left;
            left += control.Width + gap;
        }

        _tick.Left = Math.Max(margin, ClientSize.Width - _tick.Width - margin);
        _speed.Left = Math.Max(margin, _tick.Left - _speed.Width - (gap * 3));
        _scrub.Left = left + gap;
        _scrub.Width = Math.Max(80, _speed.Left - _scrub.Left - margin);
    }

    /// <summary>One of the small shuttle buttons either side of play.</summary>
    private static Button Shuttle(string id, string glyph, string name, string description) =>
        new()
        {
            Name = id,
            AccessibleName = name,
            AccessibleDescription = description,
            Text = glyph,
            Width = 40,
            Top = 8,
            Enabled = false,
        };

    private void StepSpeed(int direction)
    {
        _speedIndex = Math.Clamp(_speedIndex + direction, 0, Speeds.Length - 1);

        UpdateSpeedLabel();
        SpeedChanged?.Invoke(this, new SpeedEventArgs(Speeds[_speedIndex]));
    }

    private void Seek(int tick)
    {
        // Deliberately NOT suppressed: a jump is a seek the caller must hear about, unlike
        // ShowTick, which is playback reporting where it has got to.
        _scrub.Value = Math.Clamp(tick, _scrub.Minimum, _scrub.Maximum);
    }

    private void UpdateSpeedLabel()
    {
        double speed = Speeds[_speedIndex];

        _speed.Text = string.Create(CultureInfo.InvariantCulture, $"{speed:0.##}x");
        _speed.AccessibleName = SpeedDescription(speed);

        LayoutChildren();
    }

    /// <summary>What a screen reader says for a playback speed.</summary>
    /// <param name="speed">The speed, negative for reverse, as <see cref="Speeds"/> holds it.</param>
    /// <returns>The spoken description.</returns>
    /// <remarks>
    /// **Public so a test can ask rather than assume.** A UI test that types the expected wording
    /// out by hand is asserting on a string constant it also owns, which passes until someone
    /// rewords the label and then fails without anything being wrong. Worse here: the wording it
    /// was written against came from a hand-written literal in the constructor that no update ever
    /// reproduced, so the test failed against a bar that was working.
    /// </remarks>
    public static string SpeedDescription(double speed) => speed < 0
        ? string.Create(CultureInfo.InvariantCulture, $"speed {-speed:0.##} times, reversed")
        : string.Create(CultureInfo.InvariantCulture, $"speed {speed:0.##} times");
}
