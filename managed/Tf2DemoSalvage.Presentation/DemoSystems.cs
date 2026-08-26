using System;

using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation;

/// <summary>Tells every system about a newly-opened demo.</summary>
/// <remarks>
/// **This was the wiring half of <c>MainForm.Apply</c>** (B188, D90), and it is the shape that broke
/// three times: `MomentScene.Viewmodels`, `MomentScene.Upload` and `SpectatorView.Eyes` were each an
/// assignment written inline, and each became a property nobody set. Two of the three shipped, with
/// 620 viewer tests green. Gathering them here does not make that impossible, but it makes it ONE
/// place to read and one place to test.
///
/// **This is the demo mirror of <see cref="LevelSystems"/>, and it is OURS rather than Valve's —
/// which was checked rather than assumed.** In the engine, playing a demo IS loading a level:
/// `playdemo` runs the ordinary level-load path, so every system gets `LevelInitPreEntity` and there
/// is no separate "a demo was opened" event to copy.
///
/// **Why that does not bind here**, which is the other half the owner's test requires. This viewer
/// opens a demo BEFORE it knows whether the map exists — the map is named by the demo, may not be
/// installed, and may have to be downloaded. So demo-apply necessarily precedes level-load and
/// cannot be a level hook. The engine never faces that because a client cannot play a demo whose map
/// it lacks; ours must, because being able to is the point of the program (B201).
///
/// **The order matters and is not alphabetical.** Sound is silenced before the schedule is replaced,
/// or the loops still in flight belong to the previous demo and keep playing over the new one.
/// </remarks>
public sealed class DemoSystems
{
    private readonly SpectatorView _spectator;
    private readonly MomentScene _moment;
    private readonly SoundPresenter _sound;
    private readonly PlaybackPresenter _playback;
    private readonly ActiveLoops _loops;
    private readonly ILogger _audioLog;
    private readonly ILogger _demoLog;

    /// <summary>Binds the systems that will be told about demos.</summary>
    /// <param name="spectator">Whose eyes can be borrowed.</param>
    /// <param name="moment">The scene rebuilt for each tick.</param>
    /// <param name="sound">The sound emitter.</param>
    /// <param name="playback">The transport, which owns the clock.</param>
    /// <param name="loops">The looping sounds in flight.</param>
    /// <param name="loggers">Where each system reports.</param>
    /// <exception cref="ArgumentNullException">A collaborator is null.</exception>
    public DemoSystems(
        SpectatorView spectator,
        MomentScene moment,
        SoundPresenter sound,
        PlaybackPresenter playback,
        ActiveLoops loops,
        ILoggerFactory loggers)
    {
        ArgumentNullException.ThrowIfNull(spectator);
        ArgumentNullException.ThrowIfNull(moment);
        ArgumentNullException.ThrowIfNull(sound);
        ArgumentNullException.ThrowIfNull(playback);
        ArgumentNullException.ThrowIfNull(loops);
        ArgumentNullException.ThrowIfNull(loggers);

        _spectator = spectator;
        _moment = moment;
        _sound = sound;
        _playback = playback;
        _loops = loops;
        _audioLog = loggers.CreateLogger("audio");
        _demoLog = loggers.CreateLogger("demo");
    }

    /// <summary>Hands a newly-opened demo to everything that reads one.</summary>
    /// <param name="timeline">The decoded demo, or null when it carried no schema.</param>
    /// <param name="lastTick">The demo's final tick, for the clock.</param>
    /// <param name="audio">The output to silence, or null when the machine has no device.</param>
    /// <param name="autoPlay">The autoplay variable's value, or null when it is unset.</param>
    /// <param name="autoPlayName">Its name, for the log.</param>
    /// <returns>The playback clock, or null when there is no timeline to run one over.</returns>
    /// <remarks>
    /// **Every source is set on BOTH paths, including the null one.** A demo whose schema failed to
    /// decode still has to clear the previous demo's eyes, viewmodels and schedule — leaving them is
    /// how a closed demo goes on being spectated. That is why each is a conditional expression
    /// rather than an `if` that only assigns when there is something to assign.
    ///
    /// **A schedule holds a cursor into ONE timeline's sound list**, so it is replaced rather than
    /// updated; carrying one across a load indexes the previous demo's sounds.
    /// </remarks>
    public PlaybackClock? Open(
        DemoTimeline? timeline, int lastTick, AudioOutput? audio, string? autoPlay, string autoPlayName)
    {
        // **Silenced BEFORE the schedule changes.** Loops in flight belong to the demo being closed;
        // replacing the schedule first leaves them playing over the new one with no owner.
        audio?.StopAll();
        _loops.Clear();

        _spectator.Eyes = timeline is { } eyes ? new TimelineEyes(eyes) : null;
        _moment.Viewmodels = timeline is { } weapons ? new TimelineViewmodels(weapons) : null;
        _sound.Schedule = timeline is { } withSound ? new SoundSchedule(withSound.Sounds) : null;

        // **Forgotten rather than rebuilt.** The archives open later than this, so building the
        // appearance now reads nothing and caches that nothing for the life of the demo — the first
        // attempt did exactly that. `DemoAppearance.Ensure` fills it on the first moment that can
        // answer.
        _moment.Appearance = DemoAppearance.None;

        _audioLog.LogInformation(
            "{Message}",
            $"{timeline?.Sounds.Count ?? 0} sounds on the timeline; " +
            (audio is null ? "no audio device, so none will play" : "output is open"));

        if (timeline is not { } demo)
        {
            return null;
        }

        // **The rate the recording server ran, not a constant.** It is a server setting, so a box
        // left at its default runs 33 where a configured one runs 66, and replaying at the wrong
        // rate reads as a slow or fast server rather than as a defect.
        PlaybackClock clock = new(demo.IntervalPerTick, lastTick);

        // The presenter owns playback over this clock from here (D62).
        _playback.Load(clock);

        // **Autoplay lives INSIDE this method because its ordering was a real bug, twice.** It has
        // to happen after the clock exists: `PlayingChanged` starts the stopwatch only
        // `if (playing && _clock is not null)`, so started earlier the button showed playing while
        // no time was fed to a clock that did not exist, and the demo sat still until the user
        // paused and played again. A separate public method would let a caller get that order
        // wrong, which is the hazard this shape removes rather than documents.
        //
        // **Through the presenter, not by assigning the flag** — which is what the first version
        // did. `TransportBar.Playing`'s setter deliberately does not raise `PlayPauseToggled` (it
        // would make the presenter re-enter its own handler), so assigning it relabelled the button
        // and left the elapsed clock stopped. The owner: "the ui says its playing but the demo is
        // not actually playing, no ticks go by, i have to 'pause' which does nothing then hit play
        // again to get it started". It came back a second time because nothing tested it.
        //
        // **Why the environment can start playback at all:** a demo's first tick is before the match
        // begins — no capture points, no holograms, nobody carrying anything. A launch-and-log run,
        // the only way to ask the renderer a question with nobody at the keyboard, otherwise
        // measures an almost empty scene and reports "never drawn" for models that had not appeared.
        //
        // **The VALUE is passed in rather than read here**, so a test need not set a process-wide
        // variable, and the one place that reads the environment is the window that owns the process.
        if (autoPlay is { Length: > 0 })
        {
            _playback.Play();

            _demoLog.LogInformation("{Message}", $"{autoPlayName} is set; playback started at load");
        }

        return clock;
    }
}
