using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;

using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.GameSystems;

namespace Tf2DemoSalvage.Presentation;

/// <summary>What each phase of a sound update cost, in stopwatch ticks.</summary>
/// <param name="Advance">Advancing the schedule, and a seek's silence.</param>
/// <param name="Reclaim">Releasing voices that had finished.</param>
/// <param name="Loops">Re-attenuating every tracked loop to the listener.</param>
/// <param name="Soundscape">Choosing and fading the map's ambience.</param>
/// <param name="Starting">Playing everything that begins this tick.</param>
/// <remarks>
/// **Returned rather than logged here, because the format is the view's business and the timing is
/// not.** The presenter can see the phase boundaries and the view owns what a slow line looks like;
/// putting both in one place is what made the frame ledger's `sound` bucket a single number for as
/// long as it was (B191).
/// </remarks>
public readonly record struct SoundPhases(
    long Advance,
    long Reclaim,
    long Loops,
    long Soundscape,
    long Starting);

/// <summary>Decides what should be audible at a tick, and tells a sink to make it so.</summary>
/// <remarks>
/// **A presenter, because that is what this project decided a non-window job is** (D54, D62). It
/// lived in <c>MainForm.PlaySounds</c> until 2026-08-25, where nothing about it could be asserted
/// without an STA, a device and the desktop lock (B188, B184).
///
/// **The engine's split is the same.** <c>CSoundEmitterSystem : CBaseGameSystem</c>
/// (<c>SoundEmitterSystem.cpp:134</c>) decides what to emit and calls through <c>enginesound</c>;
/// the mixer is on the far side of an interface. Ours calls through <see cref="IAudioSink"/> for
/// exactly that reason — a test needs no sound card.
///
/// **Told where the ears are rather than asking.** The listener is the camera, which is view state,
/// so it arrives as an argument. Same lesson as <c>SetupRenderInfo_t</c>, which carries the render
/// origin rather than letting the builder reach for it (<c>clientleafsystem.h:75</c>).
/// </remarks>
/// <param name="soundscape">The map's ambience, updated as part of each pass.</param>
/// <param name="loops">The looping sounds in flight, shared with the soundscape.</param>
/// <param name="sample">Opens a sound by name, or answers null.</param>
/// <param name="audio">Where this reports loops starting and crossing into silence.</param>
public sealed class SoundPresenter(
    SoundscapeSystem soundscape,
    ActiveLoops loops,
    Func<string, SoundSample?> sample,
    ILogger audio) : IGameSystem
{
    /// <summary>How many sounds actually reached the output.</summary>
    private int _submitted;

    /// <summary>How many one-shots were dropped for coming out silent.</summary>
    private int _silenced;

    /// <summary>How many reports have been written, to bound them.</summary>
    private int _audioReports;

    /// <summary>Says what the sound path is actually DOING, at widening intervals.</summary>
    /// <remarks>
    /// **The one number that distinguishes "no sound" from "a healthy sound system".** Reported at
    /// 1, 10, 100, 1000 … so a run that submits nothing says so immediately and a run that works
    /// says it once and then goes quiet. Both counts, because "submitted 0, silenced 4,000" and
    /// "submitted 0, silenced 0" are completely different faults — a wrong gain curve against
    /// nothing being scheduled at all — and the log could previously tell them apart not at all.
    /// </remarks>
    private void ReportAudioOutput()
    {
        int total = _submitted + _silenced;

        // 1, 10, 100, 1000, ... — dense where a fault shows and silent once it is working.
        if (total <= 0 || total % (int)Math.Pow(10, Math.Min(4, _audioReports)) != 0)
        {
            return;
        }

        _audioReports++;

        audio.LogDebug(
            "{Message}",
            $"sound output: {_submitted} submitted, {_silenced} dropped for zero gain");
    }

    /// <inheritdoc/>
    public string Name => "soundemitter";

    // **`LevelShutdownPreEntity() => Schedule = null` was here, and it silenced the viewer
    // entirely** — removed 2026-08-29 (B228).
    //
    // `MainForm.Apply` opens the demo and THEN reads the map: `DemoSystems.Open` set the schedule,
    // `LoadMap` called `ClearMap` called `LevelSystems.Shutdown()`, and this nulled it again before
    // a single frame was drawn. `Update` then returned at its first guard for the rest of the
    // session. Measured on cp_process_f12: 23,772 sounds on the timeline, 542 precached, 110 frames
    // drawn, and not one sound submitted.
    //
    // **The lifetime is Valve's answer rather than a preference.** `CSoundEmitterSystem` does not
    // implement `LevelShutdownPreEntity` at all; its `LevelShutdownPostEntity`
    // (`SoundEmitterSystem.cpp:333`) clears `soundemitterbase->ClearSoundOverrides()`, which is
    // map-scoped SCRIPT data. `C_SoundscapeSystem::LevelShutdownPreEntity` (`c_soundscape.cpp:120`)
    // is literally `{}`, and its voices are stopped post-entity by `OnStopAllSounds()`. Neither
    // clears anything resembling a play queue — a live client has none, because it receives sounds
    // as events.
    //
    // **A schedule is a cursor into one DEMO's sound list**, so its owner is the demo.
    // `DemoSystems.Open` already holds both ends: it builds one for a timeline that decoded and
    // nulls it for one that did not. The old note here was right that carrying a schedule across a
    // load would index the previous demo's sounds — and that is exactly what `Open` prevents, on
    // the path that actually knows a demo changed.
    //
    // **The per-frame split it also documented is unchanged and still Valve's**: deciding what to
    // emit is `CBaseGameSystem` (`SoundEmitterSystem.cpp:134`), driven from events, while
    // `C_SoundscapeSystem` next door IS per-frame because a soundscape fades and re-chooses on a
    // timer.

    /// <summary>Below this a loop is treated as silent, for the crossing log only.</summary>
    /// <remarks>
    /// Valve's own <c>MIN_AUDIBLE_VOLUME</c> is <c>1.01e-3</c> (<c>sound.cpp:314</c>), the threshold
    /// the engine uses when computing how far an <c>ambient_generic</c> carries. Reused so "audible"
    /// means the same thing in the log as it does to the engine.
    /// </remarks>
    private const float InaudibleGain = 1.01e-3f;

    /// <summary>Whether each tracked loop was audible last pass, so only crossings are logged.</summary>
    private readonly Dictionary<(int Entity, int Channel), bool> _audible = [];

    /// <summary>Which sounds are due as playback moves, or null before a demo is opened.</summary>
    public SoundSchedule? Schedule { get; set; }

    /// <summary>Brings the audible world up to date for one tick.</summary>
    /// <param name="output">Where sound goes.</param>
    /// <param name="tick">The tick being played.</param>
    /// <param name="listener">Where the ears are, which is the camera.</param>
    /// <param name="right">The listener's right vector, for the pan.</param>
    /// <param name="now">Seconds on the caller's audio clock, for the soundscape fade.</param>
    /// <returns>What each phase cost, for the caller's own ledger.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="output"/> is null.</exception>
    public SoundPhases Update(
        IAudioSink output,
        int tick,
        (float X, float Y, float Z) listener,
        (float X, float Y, float Z) right,
        double now)
    {
        ArgumentNullException.ThrowIfNull(output);

        long began = Stopwatch.GetTimestamp();

        if (Schedule is not { } schedule)
        {
            return default;
        }

        IReadOnlyList<SceneSound> starting = schedule.Advance(tick);

        // **A seek silences what is in flight.** Those sounds belong to the moment the viewer has
        // just left, and letting them finish plays the old place over the new one. The loops go with
        // them: those voices no longer exist, so following them would re-attenuate nothing.
        if (schedule.Jumped)
        {
            output.SilenceAll();
            loops.Clear();

            // **The soundscape has to be forgotten too, and leaving it out was a silent bug.**
            // Silencing everything deletes the soundscape's sources as well, but its voice keys
            // survived — so it saw them as already playing and only ever changed the gain of sources
            // that no longer existed. The map's room tone died at the first seek and never came
            // back, with nothing reported anywhere.
            soundscape.Clear();
        }

        long advanced = Stopwatch.GetTimestamp();

        output.Reclaim();

        long reclaimed = Stopwatch.GetTimestamp();

        // **Every pass, before the early exit below, because this is the whole of B169.** A loop is
        // started once and runs for the match, so its attenuation has to follow the listener or it
        // keeps whatever the camera implied at the instant it began — which made the map's ambience
        // inaudible. Ordering matters: this must not sit after the `starting.Count == 0` return, or
        // loops would only be re-attenuated on the passes something else happened to start, which is
        // most obviously wrong exactly when the map is quiet.
        Attenuate(output, listener);

        long looped = Stopwatch.GetTimestamp();

        soundscape.Update(output, listener, right, now);

        long soundscaped = Stopwatch.GetTimestamp();

        // **Re-establishing, not replaying.** `Advance` carries EVENTS, and a looping ambient is
        // state: cp_process starts six `)ambient/machine_hum.wav` at tick 4 and does not mention
        // them again until a round restart minutes later. Opening the demo, or seeking anywhere past
        // tick 4, therefore left the map's machinery silent for the rest of the recording with
        // nothing to explain it — a live client never has this problem because it starts the loop
        // once and the source simply keeps running.
        bool reestablishing = schedule.Repositioned;

        if (reestablishing)
        {
            starting = schedule.LiveAt(tick);
        }

        foreach (SceneSound sound in starting)
        {
            Start(output, sound, listener, right, reestablishing);
        }

        return new SoundPhases(
            advanced - began,
            reclaimed - advanced,
            looped - reclaimed,
            soundscaped - looped,
            Stopwatch.GetTimestamp() - soundscaped);
    }

    /// <summary>Re-attenuates every tracked loop to where the listener now stands.</summary>
    private void Attenuate(IAudioSink output, (float X, float Y, float Z) listener)
    {
        foreach ((int entity, int channel, float gain) in
            loops.GainsAt(listener.X, listener.Y, listener.Z))
        {
            output.SetGain(entity, channel, gain);

            // **Logged only when a loop crosses between silent and audible**, which is the question
            // a listener actually has: "I walked up to the machine and heard nothing". Per-pass
            // logging would be sixty lines a second per loop and unreadable; the crossing is a
            // handful of lines for a whole match and says whether the gain ever came up at all.
            bool audible = gain > InaudibleGain;

            if (_audible.TryGetValue((entity, channel), out bool was) && was == audible)
            {
                continue;
            }

            _audible[(entity, channel)] = audible;

            audio.LogDebug(
                "{Message}",
                $"loop entity {entity.ToString(CultureInfo.InvariantCulture)} chan " +
                $"{channel.ToString(CultureInfo.InvariantCulture)} is now " +
                $"{(audible ? "audible" : "silent")} at gain " +
                $"{gain.ToString("0.####", CultureInfo.InvariantCulture)}");
        }
    }

    /// <summary>Plays, or silences, one sound the schedule produced.</summary>
    private void Start(
        IAudioSink output,
        SceneSound sound,
        (float X, float Y, float Z) listener,
        (float X, float Y, float Z) right,
        bool reestablishing)
    {
        // **A stop silences a channel rather than starting anything, and dropping it is audible.**
        // Measured: 15 of movement-test-pov-cp_process's 89 sounds are stops, and they name the
        // looping ones — doors/door_metal_rusty_move five times against eight starts,
        // metal_box_scrape_rough_loop four, )ambient/machine_hum six. Unhonoured, each loop runs the
        // length of its file, which the owner heard as "gate sounds are either playing too slow or
        // just playing too long".
        if (sound.IsStop)
        {
            output.Silence(sound.EntityIndex, sound.Channel);
            loops.Forget(sound.EntityIndex, sound.Channel);
            return;
        }

        if (sound.Name.Length == 0 || sample(sound.Name) is not { } opened)
        {
            return;
        }

        // **Only loops are re-established.** `LiveAt` answers which sounds still hold a named
        // channel, and a one-shot that was never explicitly stopped still holds one — a voice line
        // from four minutes ago is "live" by that rule and finished long ago in fact. Starting those
        // on arrival would be the seek-replay stutter `Advance` refuses to make.
        if (reestablishing && !opened.Loops)
        {
            return;
        }

        (float X, float Y, float Z) source = (sound.OriginX, sound.OriginY, sound.OriginZ);

        float distance = MathF.Sqrt(
            ((source.X - listener.X) * (source.X - listener.X)) +
            ((source.Y - listener.Y) * (source.Y - listener.Y)) +
            ((source.Z - listener.Z) * (source.Z - listener.Z)));

        // **Attenuation comes from the SOUNDLEVEL, for every sound, with no special case.**
        //
        // This first read `bIsAmbient` as "plays at full volume everywhere", and the owner heard
        // exactly what that produces: "the ambient sounds were way way too loud compared to
        // everything else, and started playing at the start of the demo even though i was in free
        // cam and no one was in the spawn room".
        //
        // It was invented. `bIsAmbient` is written and read on the wire (`soundinfo.h:185`) and **no
        // published client or engine code reads it for gain** — it is a routing hint for the ambient
        // channel. What Valve actually uses is the soundlevel, and `AtDistance` already implements
        // the one case that matters: SNDLVL_NONE means no attenuation, so a genuinely global sound
        // is global because its own data says so.
        //
        // The general lesson is D80's, one layer up: a special case that makes something audible is
        // indistinguishable from a working feature until somebody listens.
        float gain = sound.Volume * SoundGain.AtDistance(sound.SoundLevel, distance);

        // **A loop is started even when it is currently inaudible, and a one-shot is not.** Silence
        // now is permanent for a one-shot, so skipping it saves a buffer nobody hears. A loop that
        // is out of range at the moment it starts is the ordinary case — the map's ambience begins
        // on the first tick, wherever the camera happens to be — and refusing it would mean the
        // sound never exists to be turned up when the listener walks over.
        if (gain <= 0f && !opened.Loops)
        {
            // **The audio subsystem had no output-level instrument at all**, which is why "we lost
            // sound at some point in the past 2 days" could not be placed. Every audio line in a run
            // is setup — output opened, N sounds on the timeline, N precached — and all of those are
            // CAPABILITIES. None of them says a sound was ever submitted, so total silence and a
            // healthy subsystem produce identical logs. See
            // `docs/memory/measure-the-output-not-the-capability.md`, which this is a textbook case
            // of and which was written about a different subsystem.
            //
            // This is the drop that can silence everything: a one-shot whose gain came out zero is
            // discarded here, so a wrong gain curve removes nearly all sound and reports nothing.
            _silenced++;
            ReportAudioOutput();
            return;
        }

        (float left, float rightGain) = SoundGain.Pan(SoundGain.Rightward(listener, right, source));

        // **Pan and gain go separately.** The pan is baked into the samples and fixed for the life
        // of the sound; the gain is a scalar on the source and is what SetGain moves as the listener
        // travels (B169).
        _submitted++;
        ReportAudioOutput();

        output.Play(
            opened,
            left,
            rightGain,
            gain,

            // TF2 sends a percentage where 100 is unshifted. Measured across a real match: 100
            // dominates with a spread of 95..99 around it, which is the engine's own random
            // variation and not a decode fault.
            sound.Pitch > 0 ? sound.Pitch / 100f : 1f,

            // **The channel is what makes a stop possible and a voice line replace itself.** Passed
            // through rather than defaulted, because CHAN_AUTO is a real value with its own meaning
            // — the engine picks the channel and the sound is meant to overlap.
            sound.EntityIndex,
            sound.Channel);

        if (!opened.Loops)
        {
            return;
        }

        // **Every looping sound is logged as it starts, because a loop is the only kind whose
        // silence can be a defect rather than an event.** A one-shot that never plays is gone in a
        // second; a loop that never plays is a piece of the map missing for the whole recording, and
        // from the speakers "never started", "started at gain zero and never came up" and "started
        // and was stopped again" are the same silence.
        audio.LogDebug(
            "{Message}",
            $"loop '{sound.Name}' entity " +
            $"{sound.EntityIndex.ToString(CultureInfo.InvariantCulture)} chan " +
            $"{sound.Channel.ToString(CultureInfo.InvariantCulture)} at tick " +
            $"{sound.Tick.ToString(CultureInfo.InvariantCulture)}: " +
            $"{distance.ToString("0", CultureInfo.InvariantCulture)} units away, " +
            $"sndlvl {sound.SoundLevel.ToString(CultureInfo.InvariantCulture)}, " +
            $"gain {gain.ToString("0.###", CultureInfo.InvariantCulture)}" +
            (reestablishing ? " (re-established)" : string.Empty));

        // **Followed from here so it can be re-attenuated as the listener moves.** Only loops: a
        // one-shot is over before the listener has travelled far enough for its gain to be wrong,
        // and tracking hundreds of them would be a per-pass walk for nothing.
        //
        // CHAN_AUTO loops are tracked too even though a stop cannot reach them, because the gain
        // update keys on the same pair and an untracked loop is the bug either way. They end when
        // the demo does.
        loops.Track(sound);
    }
}
