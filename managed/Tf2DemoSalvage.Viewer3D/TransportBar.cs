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

    /// <summary>The continuous speed slider (D97).</summary>
    public const string SpeedBarId = "SpeedBar";

    // **The speed ladder was here until 2026-08-26** (D97, D90). It is `TimeScale.ShuttleStops` in
    // Presentation, with the clamping and the spoken description that were beside it.
    //
    // The owner caught this going the wrong way — *"this fix touched the 3d viewer doesnt it?"* —
    // when a continuous slider was about to be built here, which would have ADDED a range, a clamp
    // and a position mapping to a view. The widget keeps the widget; the quantity does not live in a
    // WinForms control.

    private readonly Button _start;
    private readonly Button _slower;
    private readonly Button _playPause;
    private readonly Button _faster;
    private readonly Button _end;
    private readonly TrackBar _scrub;

    /// <summary>The continuous speed control (D97).</summary>
    /// <remarks>
    /// **The buttons could not reach what D97 made reachable.** The model went continuous from 0.01
    /// to 8 while the only way in was eleven fixed stops, so the fine band — the one frame-exact
    /// review needs — existed and had no control. The owner asked for the slider beside the buttons
    /// rather than instead of them: presets for the speeds people actually use, and a slider for the
    /// rest.
    ///
    /// **Integer positions, because that is what a `TrackBar` is**, mapped onto the range by
    /// <see cref="TimeScale.At"/>. The mapping is Presentation's; this control only reports where the
    /// thumb is.
    /// </remarks>
    private readonly TrackBar _speedBar;

    private readonly Label _speed;
    private readonly Label _tick;

    // **`_speedIndex` was here until 2026-08-26** (D90). It was an index into `TimeScale.ShuttleStops`
    // — a view holding a position in a table owned somewhere else, which had to be re-homed after
    // every slider drag to stay honest. The slider is the state now and the ladder is a function of
    // the speed on it, so there is nothing to keep in step.

    /// <summary>Guards the slider's own event while a button moves the thumb.</summary>
    /// <remarks>
    /// **The same guard `_suppressScrubEvent` exists for, and for the same reason.** A button press
    /// sets the thumb, the thumb raises `ValueChanged`, and the handler would announce a speed change
    /// the user did not make — which on the scrub bar was a seek loop.
    /// </remarks>
    private bool _suppressSpeedEvent;

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

            // **Above the slider, at the owner's direction** — *"the readout could be above the
            // slider"*. The two then read as one control with its value on top, rather than a number
            // sitting next to a bar and belonging to neither it nor the scrub bar beyond it.
            Top = 1,

            // **Left-anchored since it moved beside the speed slider** (2026-08-26). It was
            // right-anchored while it lived by the tick counter; keeping that would have pinned it
            // to the far edge while `LayoutChildren` placed it on the left, so it would drift on
            // every resize.
            Anchor = AnchorStyles.Left | AnchorStyles.Top,
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

        // **Spans both directions**, at the owner's request: left of centre runs the demo backwards,
        // right runs it forwards, and reverse stops being a mode to find. The centre is the SLOWEST
        // speed rather than a stop, because zero is not in the range — stopping is the play button's
        // job, and a centre that meant stopped would be a second pause control disagreeing with the
        // first.
        //
        // **Narrow on purpose.** It sits between the buttons and the scrub bar, and the scrub bar is
        // the one that must stay long — a speed slider competing with the one for POSITION would
        // shrink the important control for a setting most people leave alone.
        //
        // `TickStyle.None` to match `_scrub`: 60,001 positions of tick marks is a grey smear.
        _speedBar = new TrackBar
        {
            Name = SpeedBarId,
            AccessibleName = "Playback speed",
            AccessibleDescription =
                "Sets playback speed continuously. Right of centre plays forwards, left plays "
                + "backwards, and the centre is the slowest speed in either direction.",
            // **Below its readout, and shorter than the scrub bar to make room for it.** The bar is
            // 44 pixels tall and a default `TrackBar` is 45, so stacking a label above one needs the
            // slider told a height rather than left to its own. `TickStyle.None` is what makes a
            // short one look right — the ticks are what need the space.
            Top = 16,
            Height = 26,
            Width = 140,
            Minimum = -TimeScale.Positions,
            Maximum = TimeScale.Positions,
            Value = TimeScale.From(1d).Position(),
            TickStyle = TickStyle.None,
            Enabled = false,

            // Left-anchored, unlike `_scrub`: the growth belongs to the scrub bar.
            Anchor = AnchorStyles.Left | AnchorStyles.Top,
        };
        _speedBar.ValueChanged += OnSpeedBarChanged;

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
        Controls.Add(_speedBar);
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
        _speedBar.Enabled = playable;
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

    /// <summary>Where playback is, for the readout and for anyone asking.</summary>
    /// <remarks>
    /// **Read off the scrub bar**, which is the control that holds it, for the same reason
    /// <see cref="PlaybackSpeed"/> reads off the speed slider: one source of truth per quantity.
    /// </remarks>
    public DemoPosition Position => new(_scrub.Value, _scrub.Maximum);

    private void UpdateTickLabel()
    {
        // **The format is `DemoPosition`'s** (D90). It was a literal here, and a UI test kept a
        // second copy of it in a private parser — two places knowing one format is two places to
        // change, and a rewording would have reddened the test with nothing wrong.
        _tick.Text = Position.Label();
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
            _speedBar.Dispose();
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

        // **Speed slider then its readout, both beside the buttons** (D97). The order left to right
        // is: transport buttons, speed slider, speed readout, scrub bar, tick readout.
        //
        // **The readout moved to the slider, and the owner is why.** He looked for it *"near the
        // slider not next to the demo play track bar"* and did not find it — it lived at the far
        // right by the tick counter, which was fine while it was the only speed indicator. With a
        // control to describe, a readout at the opposite end of the bar is a label for something
        // else. Then: *"the readout could be above the slider"*, so it is.
        //
        // Centred over the slider rather than left-aligned with it, because the text changes width
        // — `0.01x` against `-8x` — and a left-aligned label makes the group look like it shifts.
        _speedBar.Left = left + gap;
        _speed.Left = _speedBar.Left + ((_speedBar.Width - _speed.Width) / 2);

        _tick.Left = Math.Max(margin, ClientSize.Width - _tick.Width - margin);
        _scrub.Left = _speedBar.Right + (gap * 3);
        _scrub.Width = Math.Max(80, _tick.Left - _scrub.Left - (gap * 3));
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

    /// <summary>Put playback back to normal speed, forwards.</summary>
    /// <remarks>
    /// **Normal is 1×, not the slider's minimum**, which is the whole reason the owner wanted `HOME`
    /// for it: *"'Home means minimum' is literally 1x when it comes to video playback, its the
    /// default too"*. The slider's actual minimum is 8× in REVERSE, since the range is mirrored for
    /// reverse playback (D97), so "go to the left-hand end" would be the opposite of what the key
    /// means in every video player there is.
    ///
    /// **Also clears reverse**, and that is why this is not `demo_timescale 1`: Valve's command sets
    /// a speed and the engine has no concept of playing backwards at all, so there is no command of
    /// Valve's that means what this one means.
    ///
    /// Shares `StepSpeed`'s shape deliberately — moving the slider rather than holding a speed of its
    /// own, so the thumb, the readout and what is playing cannot disagree.
    /// </remarks>
    public void ResetSpeed()
    {
        _suppressSpeedEvent = true;
        _speedBar.Value = TimeScale.From(1d).Position();
        _suppressSpeedEvent = false;

        UpdateSpeedLabel();

        SpeedChanged?.Invoke(this, new SpeedEventArgs(PlaybackSpeed.Speed));
    }

    private void StepSpeed(int direction)
    {
        // **Moves the slider rather than holding a speed of its own**, so the thumb, the readout and
        // what is playing cannot disagree. Suppressed because the slider's own handler would
        // otherwise announce the change a second time.
        //
        // **Which rung is next is `TimeScale`'s** (D90). This kept an index into the stops table and
        // re-homed it after every drag; the slider is the state now and the ladder is a function of
        // the speed on it.
        _suppressSpeedEvent = true;
        _speedBar.Value = TimeScale.Step(PlaybackSpeed.Speed, direction).Position();
        _suppressSpeedEvent = false;

        UpdateSpeedLabel();

        SpeedChanged?.Invoke(this, new SpeedEventArgs(PlaybackSpeed.Speed));
    }

    /// <summary>The slider was dragged, so the speed changed continuously.</summary>
    /// <remarks>
    /// **Nothing to re-home any more.** This used to move a ladder index to the nearest stop so the
    /// next button press stepped from the speed on screen; the slider is the only state now and
    /// `TimeScale.Step` reads the ladder off it, so the same behaviour costs no field here (D90).
    /// </remarks>
    private void OnSpeedBarChanged(object? sender, EventArgs e)
    {
        if (_suppressSpeedEvent)
        {
            return;
        }

        UpdateSpeedLabel();

        SpeedChanged?.Invoke(this, new SpeedEventArgs(PlaybackSpeed.Speed));
    }

    private void Seek(int tick)
    {
        // Deliberately NOT suppressed: a jump is a seek the caller must hear about, unlike
        // ShowTick, which is playback reporting where it has got to.
        _scrub.Value = Math.Clamp(tick, _scrub.Minimum, _scrub.Maximum);
    }

    /// <summary>The speed the transport is set to.</summary>
    /// <remarks>
    /// **A `TimeScale` rather than a `double`**, so the range, the clamp and the wording travel with
    /// the number instead of being restated by whoever holds it (D97).
    /// </remarks>
    /// <summary>The speed the transport is set to.</summary>
    /// <remarks>
    /// **Read off the SLIDER, so there is one source of truth.** The buttons move the thumb rather
    /// than keeping a speed of their own; two holders of one value is how a readout comes to
    /// disagree with what is playing, which is [[one-place-or-it-drifts]] in a control.
    ///
    /// Not named `Scale`: `Control.Scale(SizeF)` already owns that name, and hiding a base member to
    /// save five characters is how a resize silently stops resizing.
    /// </remarks>
    public TimeScale PlaybackSpeed => TimeScale.At(_speedBar.Value);

    private void UpdateSpeedLabel()
    {
        TimeScale scale = PlaybackSpeed;

        _speed.Text = scale.Label();
        _speed.AccessibleName = scale.Description();

        LayoutChildren();
    }

    /// <summary>What a screen reader says for a playback speed.</summary>
    /// <param name="speed">The speed, negative for reverse.</param>
    /// <returns>The spoken description.</returns>
    /// <remarks>
    /// **The wording moved to <see cref="TimeScale.Description"/> on 2026-08-26** (D97, D90) — it is
    /// a rule about a quantity, and a second frontend should get it without reinventing it. This
    /// forwards, and remains only because the UI suite addresses it by name.
    ///
    /// **Public so a test can ask rather than assume**, which is the part worth keeping. A UI test
    /// that types the expected wording out by hand is asserting on a string constant it also owns,
    /// which passes until someone rewords the label and then fails without anything being wrong.
    /// Worse here: the wording it was written against came from a hand-written literal in the
    /// constructor that no update ever reproduced, so the test failed against a bar that was working.
    /// </remarks>
    public static string SpeedDescription(double speed) => TimeScale.From(speed).Description();
}
