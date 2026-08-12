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
/// <see cref="TickChanged"/> and <see cref="PlayingChanged"/>, and shows whatever tick it is told
/// about — so playback can be driven by the render loop later without this control changing.
/// </remarks>
internal sealed class TransportBar : UserControl
{
    /// <summary>Automation id of the play/pause button.</summary>
    public const string PlayButtonId = "PlayPauseButton";

    /// <summary>Automation id of the scrub bar.</summary>
    public const string ScrubBarId = "ScrubBar";

    /// <summary>Automation id of the tick readout.</summary>
    public const string TickLabelId = "TickLabel";

    private readonly Button _playPause;
    private readonly TrackBar _scrub;
    private readonly Label _tick;

    private bool _playing;
    private bool _suppressScrubEvent;

    /// <summary>Builds the transport bar.</summary>
    public TransportBar()
    {
        Name = "TransportBar";
        AccessibleName = "Playback controls";
        Height = 44;
        Dock = DockStyle.Bottom;

        _playPause = new Button
        {
            Name = PlayButtonId,
            AccessibleName = "Play",
            AccessibleDescription = "Starts or pauses playback of the loaded demo.",
            Text = "Play",
            Width = 80,
            Left = 8,
            Top = 8,
            Enabled = false,
        };
        _playPause.Click += (_, _) => Playing = !Playing;

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

        Controls.Add(_playPause);
        Controls.Add(_scrub);
        Controls.Add(_tick);
        Resize += (_, _) => LayoutChildren();
        LayoutChildren();
    }

    /// <summary>Raised when the user moves the scrub bar.</summary>
    public event EventHandler<int>? TickChanged;

    /// <summary>Raised when playback is started or paused.</summary>
    public event EventHandler<bool>? PlayingChanged;

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
            PlayingChanged?.Invoke(this, _playing);
        }
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
        _scrub.Enabled = playable;
        _playPause.Enabled = playable;
        Playing = false;
        UpdateTickLabel();
    }

    /// <summary>Moves the readout and scrub bar without raising <see cref="TickChanged"/>.</summary>
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
            TickChanged?.Invoke(this, _scrub.Value);
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
            // All three are in Controls, which the base walks - but the analyzer cannot see that
            // ownership, and saying so costs nothing and is true.
            _playPause.Dispose();
            _scrub.Dispose();
            _tick.Dispose();
        }

        base.Dispose(disposing);
    }

    private void LayoutChildren()
    {
        const int margin = 8;
        _tick.Left = Math.Max(margin, ClientSize.Width - _tick.Width - margin);
        _scrub.Width = Math.Max(80, _tick.Left - _scrub.Left - margin);
    }
}
