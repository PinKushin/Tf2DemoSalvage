using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Audio;

/// <summary>Keeps the map's ambience playing as the listener moves through it.</summary>
/// <remarks>
/// **A per-frame system of its own, because that is what the engine makes it.**
/// <c>C_SoundscapeSystem : public CBaseGameSystemPerFrame</c> (<c>c_soundscape.cpp:78</c>) with
/// <c>Update( float frametime )</c> at <c>:530</c> and a named <c>UpdateLoopingSounds</c> at
/// <c>:497</c> — a game system, not something a window does. This lived in <c>MainForm</c> until
/// 2026-08-25, where it could not be tested without one (B188, B184).
///
/// **Nothing here needs a window and nothing here needed a new abstraction**, which is what made
/// the move worth doing rather than merely tidy: <see cref="SoundscapeMixer"/>,
/// <see cref="ActiveLoops"/> and <see cref="SoundscapeCatalog"/> were already in this project, and
/// <c>Tf2DemoSalvage.Audio</c> already referenced <c>Content</c> for
/// <see cref="BspLeafTree"/> and <see cref="BspVisibility"/>.
///
/// **The clock is passed in rather than read.** Valve's <c>Update</c> takes <c>frametime</c> for
/// the same reason a builder is told where the camera is rather than reaching for it: a system that
/// reads its own clock can only be tested in real time.
/// </remarks>
/// <param name="loops">The looping sounds in flight, shared with the rest of playback.</param>
/// <param name="sample">Opens a wave by name, or answers null when the install cannot supply it.</param>
/// <param name="audio">Where this reports what it chose and what it could not open.</param>
public sealed class SoundscapeSystem(
    ActiveLoops loops,
    Func<string, SoundSample?> sample,
    ILogger audio)
{
    /// <summary>How often a soundscape is CHOSEN, in seconds.</summary>
    /// <remarks>
    /// **Chosen on a timer, advanced every frame.** The choice traces rays and is slow-moving; the
    /// fade is cheap and has to be smooth, so they run at different rates.
    /// </remarks>
    private const double ChooseInterval = 0.25;

    /// <summary>The entity index soundscape voices are played under.</summary>
    /// <remarks>
    /// Negative, so it cannot collide with a real entity: the soundscape belongs to the map rather
    /// than to anything in it, and its channels are keyed by voice.
    /// </remarks>
    public const int SoundscapeEntity = -1000;

    private readonly SoundscapeMixer _mixer = new();
    private readonly HashSet<int> _voices = [];

    private double _chosenAt;
    private double _advancedAt;

    /// <summary>The map's soundscape scripts, or null before a map is read.</summary>
    public SoundscapeCatalog? Catalog { get; set; }

    /// <summary>Where the map puts its soundscapes, or null before a map is read.</summary>
    public SoundscapePlacements? Placements { get; set; }

    /// <summary>The map's leaf tree, for the cluster the listener stands in.</summary>
    public BspLeafTree? Leaves { get; set; }

    /// <summary>The map's PVS, so a soundscape behind a wall does not reach the listener.</summary>
    public BspVisibility? Visibility { get; set; }

    /// <summary>Forgets every voice, for a seek.</summary>
    /// <remarks>
    /// **Necessary because <c>StopAll</c> deletes the sources without telling this.** Leaving it out
    /// was a silent bug: the voice keys survived, so <see cref="Update"/> saw them as already
    /// playing and only ever called <c>SetGain</c> on sources that no longer existed. The map's room
    /// tone died at the first seek and never came back, with nothing reported anywhere.
    /// </remarks>
    public void Clear()
    {
        _voices.Clear();
        _mixer.Clear();
    }

    /// <summary>Chooses, fades and plays the map's ambience for one frame.</summary>
    /// <param name="output">The audio device.</param>
    /// <param name="listener">Where the ears are, which is the camera.</param>
    /// <param name="right">The listener's right vector, for the pan.</param>
    /// <param name="now">Seconds on the caller's audio clock.</param>
    /// <exception cref="ArgumentNullException"><paramref name="output"/> is null.</exception>
    public void Update(
        IAudioSink output,
        (float X, float Y, float Z) listener,
        (float X, float Y, float Z) right,
        double now)
    {
        ArgumentNullException.ThrowIfNull(output);

        if (Placements is not { } placements)
        {
            return;
        }

        if (now - _chosenAt >= ChooseInterval)
        {
            _chosenAt = now;

            Choose(placements, listener);
        }

        IReadOnlyList<SoundscapeVoice> voices = _mixer.Advance((float)(now - _advancedAt));

        _advancedAt = now;

        // Anything that finished fading is stopped, or the sink holds it for ever at its last
        // volume — a loop only ends when its channel is told to.
        foreach (int ended in SoundscapeMixer.Ended(voices, _voices))
        {
            output.Silence(SoundscapeEntity, ended);
            loops.Forget(SoundscapeEntity, ended);
            _voices.Remove(ended);
        }

        foreach (SoundscapeVoice voice in voices)
        {
            Start(output, voice, listener, right);
        }
    }

    /// <summary>Picks the soundscape that reaches the listener, and says when it changes.</summary>
    private void Choose(SoundscapePlacements placements, (float X, float Y, float Z) listener)
    {
        SoundscapePlacement? chosen = placements.Choose(
            listener.X,
            listener.Y,
            listener.Z,
            (from, to) => Leaves is not { } leaves ||
                leaves.IsClear(from.X, from.Y, from.Z, to.X, to.Y, to.Z),
            _mixer.Current,

            // **The listener is the camera, which is already at eye height** — the engine tests at
            // `EarPosition()` rather than at a player's origin, and a floor-level point resolves
            // into the solid leaf beneath it and reports no cluster at all. Measured while writing
            // the test for this: a captured player origin gave cluster −1, which silently disables
            // the filter rather than failing.
            Leaves?.ClusterAt(listener.X, listener.Y, listener.Z) ?? -1,
            Visibility);

        Soundscape? definition = chosen is { } placed && Catalog is { } catalog
            ? catalog.At(placed.Index)
            : null;

        // **Logged on every change, because a soundscape that is never CHOSEN and one that is
        // chosen and never heard are the same silence.** The loop logging below answers the second;
        // nothing answered the first, so "the outdoor ambience is missing" could not be narrowed
        // without a relaunch. Changes only — this is asked four times a second.
        if (chosen?.Id != _mixer.Current?.Id || chosen?.Index != _mixer.Current?.Index)
        {
            string loopNames = definition is { } script
                ? $"{script.Looping.Count.ToString(CultureInfo.InvariantCulture)} loops (" +
                    string.Join(
                        ", ",
                        script.Looping.Select(sound =>
                            sound.Position is { } slot
                                ? $"{sound.Wave}@{slot.ToString(CultureInfo.InvariantCulture)}"
                                : sound.Wave)) + ")"
                : "NO DEFINITION in the catalog";

            audio.LogInformation(
                "{Message}",
                chosen is { } next
                    ? $"soundscape {next.Index.ToString(CultureInfo.InvariantCulture)} " +
                      $"'{next.Name}' from placement " +
                      $"{next.Id.ToString(CultureInfo.InvariantCulture)}: {loopNames}"
                    : "no soundscape reaches the listener");
        }

        _mixer.MoveTo(chosen, definition);
    }

    /// <summary>Starts one voice, or re-gains it when it is already playing.</summary>
    private void Start(
        IAudioSink output,
        SoundscapeVoice voice,
        (float X, float Y, float Z) listener,
        (float X, float Y, float Z) right)
    {
        if (_voices.Contains(voice.Key))
        {
            // Already playing: the fade is a gain change, not a restart.
            output.SetGain(SoundscapeEntity, voice.Key, GainOf(voice, listener));
            return;
        }

        if (sample(voice.Wave) is not { } opened)
        {
            // Remembered as started even when it could not be opened, so a missing file is looked
            // up once rather than every frame of a three-second fade.
            audio.LogWarning("{Message}", $"soundscape loop '{voice.Wave}' would not open");
            _voices.Add(voice.Key);
            return;
        }

        audio.LogInformation(
            "{Message}",
            $"soundscape loop '{voice.Wave}' starting at gain " +
            $"{GainOf(voice, listener).ToString("0.###", CultureInfo.InvariantCulture)}" +
            (voice.Position is null ? " (at the listener)" : " (positioned)"));

        (float left, float rightGain) = voice.Position is { } at
            ? SoundGain.Pan(SoundGain.Rightward(listener, right, at))
            : (1f, 1f);

        output.Play(
            opened,
            left,
            rightGain,
            GainOf(voice, listener),
            pitch: 1f,
            SoundscapeEntity,
            voice.Key);

        _voices.Add(voice.Key);
    }

    /// <summary>A voice's gain, which is its fade times its distance falloff.</summary>
    /// <param name="voice">The voice.</param>
    /// <param name="listener">Where the ears are.</param>
    /// <returns>The gain to play it at.</returns>
    /// <remarks>
    /// **A positioned loop attenuates and an unpositioned one does not.** A soundscape sound with no
    /// position plays at the listener — it is room tone rather than a thing in the room — so
    /// distance would be zero and the falloff meaningless. One placed at a target is a source in the
    /// world and falls off from it.
    ///
    /// The script's own <c>attenuation</c> is Valve's attenuation unit rather than a soundlevel, so
    /// it is converted through <see cref="SoundAttenuation.ToSoundLevel"/> to reach the same curve
    /// everything else here uses.
    /// </remarks>
    public static float GainOf(SoundscapeVoice voice, (float X, float Y, float Z) listener)
    {
        if (voice.Position is not { } at)
        {
            return voice.Volume;
        }

        float dx = at.X - listener.X;
        float dy = at.Y - listener.Y;
        float dz = at.Z - listener.Z;

        float distance = MathF.Sqrt((dx * dx) + (dy * dy) + (dz * dz));

        int level = voice.Attenuation is { } attenuation
            ? SoundAttenuation.ToSoundLevel(attenuation)
            : 0;

        return voice.Volume * SoundGain.AtDistance(level, distance);
    }
}
