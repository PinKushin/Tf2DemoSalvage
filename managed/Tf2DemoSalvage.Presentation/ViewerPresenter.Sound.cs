using System;
using System.Diagnostics;

using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation;

/// <content>
/// The sound device and the frame's sound step: opened beside what plays through it, and stepped with the listener a
/// window supplies (D214). The engine's split is the same — `CSoundEmitterSystem : CBaseGameSystem`
/// (`SoundEmitterSystem.cpp:134`) decides what to emit and calls through `enginesound`; the device is not game code's.
/// </content>
public sealed partial class ViewerPresenter
{
    /// <summary>The audio device, or null when the machine has none.</summary>
    private AudioOutput? _audio;

    /// <summary>A clock that runs for the life of the window.</summary>
    /// <remarks>
    /// **Its own, because the flight clock is restarted every frame** and so can only report a
    /// frame's duration, never a running time. A crossfade needs both: when the choice was last made
    /// and how long since the last advance.
    ///
    /// Real time rather than demo time, deliberately. `soundscape_fadetime` is three seconds of
    /// wall clock in the engine, so a fade tied to demo ticks would stretch when playback slows and
    /// vanish when it is scrubbed.
    /// </remarks>
    private readonly Stopwatch _audioClock = Stopwatch.StartNew();

    /// <summary>Opens the sound device, and says in the log whether there is one.</summary>
    /// <remarks>
    /// **A call and not part of the constructor**, so the line it logs stays where the window logged it: after the
    /// launch options and the player's configs have said their piece.
    /// </remarks>
    public void OpenAudio()
    {
        // **Opened once, here, and null is a normal answer.** The measurement boxes and CI have no
        // sound card, and neither does a machine with audio disabled — a viewer that refused to
        // start over that would be worse than one that draws in silence. Reported either way, so
        // "there is no sound" and "the sound is not working" are distinguishable in the log.
        _audio = AudioOutput.TryCreate();

        _audioLog.LogInformation(
            "{Message}",
            _audio is null
                ? "no audio device could be opened; playback will be silent"
                : "audio output opened");
    }

    /// <summary>Steps the sound for this frame, with the ears wherever the camera is and in whoever's eyes it is (B483).</summary>
    /// <param name="tick">The transport's tick.</param>
    /// <param name="focused">Whether the window has focus, for `snd_mute_losefocus`.</param>
    public void PlaySounds(int tick, bool focused) =>
        PlaySounds(
            () => _camera.FirstPerson ? _camera.FirstPersonCamera() : _camera.FreeLookCamera(),
            () => _camera.FirstPerson ? _camera.FollowedEntity() : null,
            tick,
            focused);

    /// <summary>Starts whatever the recording plays at this tick.</summary>
    /// <remarks>
    /// **The spatialisation is Valve's and stays Valve's (D80).** The gain comes from
    /// <c>SoundGain.AtDistance</c> against the sound's own <c>SNDLVL</c>, and the pan from
    /// <c>SoundGain.Pan</c> — the sink is handed finished stereo and applies no distance model of
    /// its own. Anything else would replace a curve this project can compare against the engine
    /// with one it cannot.
    /// </remarks>
    /// <param name="listener">The camera the ears are at, asked for only when there is a device; null for none.</param>
    /// <param name="listenerEyes">Whose eyes the listener is in, or null outside first person; asked for after the camera.</param>
    /// <param name="focused">Whether the window has focus, for `snd_mute_losefocus`.</param>
    /// <param name="tick">The transport's tick.</param>
    internal void PlaySounds(Func<FreeCamera?> listener, Func<int?> listenerEyes, int tick, bool focused = true)
    {
        ArgumentNullException.ThrowIfNull(listener);
        ArgumentNullException.ThrowIfNull(listenerEyes);

        // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
        if (_audio is not { } output)
        {
            return;
        }

        PlaySoundsOver(output, listener, listenerEyes, tick, focused);
    }

    /// <summary><see cref="PlaySounds(int, bool)"/> over a sink a test can read.</summary>
    internal void PlaySoundsOver(IAudioSink output, Func<FreeCamera?> listener, Func<int?> listenerEyes, int tick, bool focused = true)
    {
        // The master gain first, so a change reaches what is already playing and a lost focus silences even a paused frame.
        output.MasterGain = _settingsOwner.MasterGain(focused);

        // **What is left here is the LISTENER and nothing else** (B188). Deciding what should be
        // audible moved to SoundPresenter; a window's remaining business is that the ears are
        // wherever the camera is, which is view state and cannot come from anywhere else.
        // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
        if (SoundListener.From(listener())
            is not { } ears)
        {
            return;
        }

        long soundAt = Stopwatch.GetTimestamp();

        // In the recorder's eyes, its networked soundscape rather than a simulation (B483).
        int? eyes = listenerEyes();
        SceneSoundscape? recorded = _timeline is { } timeline
            ? SoundPresenter.RecordedSoundscape(timeline, tick, eyes)
            : null;

        SoundPhases phases = _sound.Update(
            output,
            tick,
            ears.Origin,
            ears.Right,
            _audioClock.Elapsed.TotalSeconds,
            recorded);

        StallReport.Sounds(phases, Stopwatch.GetTimestamp() - soundAt, _audioLog);
    }
}
