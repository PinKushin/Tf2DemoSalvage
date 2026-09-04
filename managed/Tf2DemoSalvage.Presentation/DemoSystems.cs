using System;

using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Content.Assets;
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
    private readonly MomentPresenter _moments;
    private readonly PlayerAppearances _appearances;
    private readonly SoundPresenter _sound;
    private readonly PlaybackPresenter _playback;
    private readonly ActiveLoops _loops;
    private readonly ILogger _audioLog;
    private readonly ILogger _demoLog;

    /// <summary>Binds the systems that will be told about demos.</summary>
    /// <param name="spectator">Whose eyes can be borrowed.</param>
    /// <param name="moment">The scene rebuilt for each tick.</param>
    /// <param name="moments">What samples that scene's contents out of the demo.</param>
    /// <param name="appearances">The player appearance, whose demo half is set here.</param>
    /// <param name="sound">The sound emitter.</param>
    /// <param name="playback">The transport, which owns the clock.</param>
    /// <param name="loops">The looping sounds in flight.</param>
    /// <param name="loggers">Where each system reports.</param>
    /// <exception cref="ArgumentNullException">A collaborator is null.</exception>
    public DemoSystems(
        SpectatorView spectator,
        MomentScene moment,
        MomentPresenter moments,
        PlayerAppearances appearances,
        SoundPresenter sound,
        PlaybackPresenter playback,
        ActiveLoops loops,
        ILoggerFactory loggers)
    {
        ArgumentNullException.ThrowIfNull(spectator);
        ArgumentNullException.ThrowIfNull(moment);
        ArgumentNullException.ThrowIfNull(moments);
        ArgumentNullException.ThrowIfNull(appearances);
        ArgumentNullException.ThrowIfNull(sound);
        ArgumentNullException.ThrowIfNull(playback);
        ArgumentNullException.ThrowIfNull(loops);
        ArgumentNullException.ThrowIfNull(loggers);

        _spectator = spectator;
        _moment = moment;
        _moments = moments;
        _appearances = appearances;
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
    /// <param name="autoPlay">Whether playback starts as soon as the clock exists.</param>
    /// <param name="autoPlayReason">What asked for it, for the log.</param>
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
    public void Open(
        DemoTimeline? timeline, int lastTick, AudioOutput? audio, bool autoPlay, string autoPlayReason)
    {
        // **Silenced BEFORE the schedule changes.** Loops in flight belong to the demo being closed;
        // replacing the schedule first leaves them playing over the new one with no owner.
        audio?.StopAll();
        _loops.Clear();

        _spectator.Eyes = timeline is { } eyes ? new TimelineEyes(eyes) : null;
        _moment.Viewmodels = timeline is { } weapons ? new TimelineViewmodels(weapons) : null;
        // **The corpses need the install half too**, which `_appearances` already carries on its own
        // lifecycle — so the source reads it per call rather than being given a table now (B315).
        _moments.Source = timeline is { } moments
            ? new TimelineMoments(moments) { ClassModels = CorpseModels, Items = CorpseItems }
            : null;
        _sound.Schedule = timeline is { } withSound ? new SoundSchedule(withSound.Sounds) : null;

        // **Forgotten rather than rebuilt.** The archives open later than this, so building the
        // appearance now reads nothing and caches that nothing for the life of the demo — the first
        // attempt did exactly that. `PlayerAppearances` fills it on the first moment that can
        // answer.
        _moment.Appearance = DemoAppearance.None;

        // **The demo half of the appearance.** The install half is set by `LevelSystems.Install`,
        // which happens on the first map read and never again — two lifetimes, so two setters. This
        // one is nulled on the failure path with everything else, or a closed demo would go on
        // supplying the weapon-role table for the next one.
        _appearances.Timeline = timeline;

        _audioLog.LogInformation(
            "{Message}",
            $"{timeline?.Sounds.Count ?? 0} sounds on the timeline; " +
            (audio is null ? "no audio device, so none will play" : "output is open"));

        // **The transport's length is set HERE, and moving it here is the whole of B223.** It was
        // the window's job, one line after this call — and because sizing the controls also clears
        // the playing flag, it silently undid the autoplay below. Nothing logged it: the flag's
        // setter deliberately does not raise, so the viewer wrote "playback started at load" and
        // then sat paused for ever, which is exactly what the owner reported.
        //
        // **Before the early return, because a demo has a length even when it has no clock.** A
        // recording whose schema failed to decode still has a tick count in its header, and its
        // scrub bar has to come alive — that is the ordinary case while decoding is still being
        // finished, not an edge one.
        //
        // This is the same argument the autoplay block below already made about ordering, applied
        // to the step that was outside the method. The remarks there claimed the shape "removes
        // rather than documents" the hazard; it removed the half that was inside.
        _playback.SetDemoLength(lastTick);

        if (timeline is not { } demo)
        {
            // **The clock is a source like the others and was the one this method did not clear.**
            // Every line above nulls the previous demo's state; the presenter kept the previous
            // demo's clock, so `HasDemo` stayed true and `Position` went on answering from a
            // recording that had been closed.
            //
            // It was masked until 2026-08-29 by `TransportBar.SetDemoLength` switching playback off
            // as a side effect — nothing ever advanced the stale clock, so nothing showed. Taking
            // that side effect out (D55: the View decides nothing) is what made it reachable, and
            // `DemoSystemsTests` could not have caught it either way: its existing case asserts
            // `HasDemo` is false on a presenter that was never loaded, so the precondition already
            // equalled the assertion.
            _playback.Load(null);

            return;
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
        // **A bool rather than the environment variable's VALUE, since 2026-08-29.** It used to take
        // the string so the window could hand it straight from `Environment`, which read as tidy and
        // was the reason the feature could not be tested: the only way to exercise it was to set a
        // process-wide variable for the whole run. `--autoplay` is an option now, so the decision is
        // made where the command line is read and this is told the answer.
        if (autoPlay)
        {
            _playback.Play();

            _demoLog.LogInformation("{Message}", $"{autoPlayReason}; playback started at load");
        }

        // **Returned the clock until 2026-08-26, and nothing in production used it.** `MainForm`
        // stored it in a field that duplicated the one `_playback.Load` had just been given, and
        // once that copy was removed the return value's only readers were tests — which is the
        // shape B206 and B207 were both about. Ask `PlaybackPresenter` instead: it owns the clock,
        // and `HasDemo` and `Position` are the questions a caller actually has.
    }

    /// <summary>The class table a corpse's model comes from, null while the install is unread.</summary>
    /// <returns>An index in, a model path out — <c>PlayerClassModels.Model</c>.</returns>
    /// <remarks>
    /// **A method rather than a lambda, and not only because a lambda cannot be a nullable
    /// delegate** (CS8978). Reading it per call is the point: the archives open on their own
    /// schedule, so a demo loaded before them has to start showing corpses when they arrive rather
    /// than never — the same two-lifetime problem `PlayerAppearances` was built for, which is why
    /// it is asked rather than a table being captured at `Open`.
    /// </remarks>
    private Func<int, string?>? CorpseModels() =>
        _appearances.Game?.Classes is { } classes ? classes.Model : null;

    /// <summary>The econ schema a corpse's cosmetics are filtered by, or null before the install.</summary>
    /// <returns><c>items_game.txt</c>, parsed once and shared with the weapon path.</returns>
    /// <remarks>
    /// **Asked per call rather than captured**, for the reason <see cref="CorpseModels"/> gives: the
    /// archives open on their own schedule, and a demo opened first would keep a null schema for its
    /// whole life — quietly drawing the cosmetics the engine drops.
    /// </remarks>
    private ItemSchema? CorpseItems() => _appearances.Game?.Weapons.Items;
}
