using System;
using System.Collections.Generic;
using System.Linq;

namespace Tf2DemoSalvage.Audio;

/// <summary>One of a soundscape's loops while it is playing, fading in or out.</summary>
/// <param name="Key">
/// What identifies this voice to the sink. A soundscape's loops are not entities, so they are given
/// synthetic channel numbers on one reserved entity — see <see cref="SoundscapeMixer"/>.
/// </param>
/// <param name="Wave">The file to play.</param>
/// <param name="Volume">Where the fade currently stands, 0 to the script's own volume.</param>
/// <param name="Position">Where to play it, or <c>null</c> to play at the listener.</param>
/// <param name="Attenuation">The script's attenuation, or <c>null</c> for none.</param>
public readonly record struct SoundscapeVoice(
    int Key,
    string Wave,
    float Volume,
    (float X, float Y, float Z)? Position,
    float? Attenuation);

/// <summary>
/// Crossfades between soundscapes the way the client does.
/// </summary>
/// <remarks>
/// **The fade is not decoration; without it every threshold is a click.** cp_process carries 42
/// soundscape entities and a player crosses between them constantly, so switching sets instantly
/// would be audible on almost every corner. `soundscape_fadetime` defaults to **3 seconds**
/// (<c>c_soundscape.cpp:42</c>).
///
/// **Transcribed from the client's own model.** `C_SoundscapeSystem::UpdateLoopingSounds`
/// (<c>c_soundscape.cpp:497</c>) gives each looping sound a `volumeCurrent` and a `volumeTarget`,
/// approaches the first toward the second by `frametime / soundscape_fadetime` each frame, and drops
/// the sound once both are zero. `StartNewSoundscape` sets every existing target to zero and adds
/// the new soundscape's loops — so the two sets overlap for the duration of the fade rather than one
/// replacing the other.
///
/// **A change is detected on index OR entity**, matching `UpdateAudioParams`: the same soundscape
/// reached from a different `env_soundscape` restarts it, because the positions it plays at come
/// from that entity and are not the same ones.
/// </remarks>
public sealed class SoundscapeMixer
{
    /// <summary><c>soundscape_fadetime</c>'s default, in seconds.</summary>
    public const float FadeSeconds = 3f;

    /// <summary>What is playing, keyed the way the sink keys a voice.</summary>
    private readonly Dictionary<int, Fading> _playing = [];

    /// <summary>The placement currently in force, so a change can be noticed.</summary>
    private SoundscapePlacement? _current;

    /// <summary>Rises as soundscapes come and go, so two runs never share a key.</summary>
    /// <remarks>
    /// **The client does the same** — `m_loopingSoundId++` in `StartNewSoundscape`. Reusing a key
    /// while the old voice is still fading out would have the new sound adopt the old one's fade.
    /// </remarks>
    private int _generation;

    /// <summary>One loop, with the two volumes the client's model gives it.</summary>
    /// <remarks>
    /// A class rather than a record struct because both volumes are mutated in place every advance,
    /// which is what `loopingsound_t` does — and a struct in a dictionary would need writing back on
    /// every change, which is the kind of thing that gets forgotten once.
    /// </remarks>
    private sealed class Fading(
        string wave,
        float target,
        (float X, float Y, float Z)? position,
        float? attenuation)
    {
        public string Wave { get; } = wave;

        public (float X, float Y, float Z)? Position { get; } = position;

        public float? Attenuation { get; } = attenuation;

        public float Target { get; set; } = target;

        public float Current { get; set; }
    }

    /// <summary>How many voices the soundscape is currently running.</summary>
    public int Count => _playing.Count;

    /// <summary>The soundscape in force, or <c>null</c> when none is.</summary>
    public SoundscapePlacement? Current => _current;

    /// <summary>Moves to a soundscape, crossfading from whatever was playing.</summary>
    /// <param name="placement">The chosen placement, or <c>null</c> to fade everything out.</param>
    /// <param name="soundscape">Its definition, or <c>null</c> when the catalog has none.</param>
    /// <remarks>
    /// **Does nothing when neither the soundscape nor the entity changed**, which is the common
    /// case — this is asked every update and the answer is usually the same. `UpdateAudioParams`
    /// returns early on exactly that condition.
    /// </remarks>
    public void MoveTo(SoundscapePlacement? placement, Soundscape? soundscape)
    {
        // **Index AND entity, which is `UpdateAudioParams`'s own condition.** The same soundscape
        // reached from a different `env_soundscape` is a change, because the positions its loops
        // play at come from that entity — cp_process has 21 entities all naming `Gorge.Inside`, and
        // treating them as one would leave the hums at whichever entity was entered first.
        if (_current is { } held && placement is { } next &&
            held.Index == next.Index &&
            held.Id == next.Id)
        {
            return;
        }

        // **...but a restart REUSES a loop that is already playing, and that is Valve's, not a
        // shortcut.** `UpdateAudioParams` does restart on `entIndex`, so reading only that far
        // suggests the ambience must fade out and back in. It does not: `StartNewSoundscape` zeroes
        // every target, and then `AddLoopingSound` reclaims the matching slot before the fade can
        // act on it (`c_soundscape.cpp:1100-1133`), under a comment that states the reason —
        // *"will reuse existing entry (fade from current volume) if possible / this prevents
        // pops"*.
        //
        // Its rule is exactly this one:
        //
        //   - an AMBIENT (unpositioned) loop reuses its slot whenever the wave and pitch match,
        //     with no positional condition at all — `if (isAmbient == true && sound.isAmbient ==
        //     true) { // reuse this sound }`;
        //   - a POSITIONED loop reuses only where the position agrees within 0.1 units, under the
        //     note *"Will always restart/crossfade positional sounds"*.
        //
        // So the loops that survive an entity change are the ones nothing about the change could
        // have altered. On cp_process that is all of them — its 44 entities carry no `position0..7`
        // keys and `localBits` is 0 in every sample of all three demos — which is why the outdoor
        // wind and birds must play continuously there and were instead inaudible: this restarted
        // them every few hundred milliseconds against a three-second fade, while choosing the right
        // soundscape the whole time.
        if (_current is { } previous && placement is { } arriving &&
            previous.Index == arriving.Index &&
            Resolve(previous, soundscape).SequenceEqual(Resolve(arriving, soundscape)))
        {
            _current = placement;

            return;
        }

        if (_current is null && placement is null)
        {
            return;
        }

        _current = placement;

        // Every existing loop starts fading out. They keep playing meanwhile, which is the whole
        // point of a crossfade — the new room's ambience rises as the old one falls.
        foreach (Fading fading in _playing.Values)
        {
            fading.Target = 0f;
        }

        _generation++;

        if (placement is not { } placed || soundscape is null)
        {
            return;
        }

        for (int index = 0; index < soundscape.Looping.Count; index++)
        {
            SoundscapeSound sound = soundscape.Looping[index];

            // **`position N` names one of the entity's own targets**, which is how a single
            // soundscape covers a whole map: Gorge.Inside places seven hums at positions 0 to 6.
            (float X, float Y, float Z)? at = null;

            if (sound.Position is { } slot)
            {
                // **A position the map did not supply SUPPRESSES the sound.** The engine is
                // explicit — `if ( positionIndex > 31 || !(m_params.localBits & (1<<positionIndex)) )
                // { // suppress sounds if the position isn't available; return; }`
                // (`c_soundscape.cpp:797`).
                //
                // **Falling back to the listener instead is loud and wrong**, and it was: this
                // first played an unavailable position at the listener with no attenuation, which
                // on cp_process meant SEVEN copies of machine_hum stacked in the listener's ear,
                // because Gorge.Inside places seven and that map's entities carry no position keys
                // at all. The owner heard it immediately — "its specifically the cpu sound".
                //
                // A soundscape written for a map whose entities do not supply its positions is
                // simply quieter there, which is what the game does.
                if (slot >= placed.Positions.Count)
                {
                    continue;
                }

                at = placed.Positions[slot];
            }

            _playing[(_generation * 64) + index] =
                new Fading(sound.Wave, sound.Volume, at, sound.Attenuation)
                {
                    // **From zero, so it fades IN.** Starting at the target would make the new
                    // room arrive instantly while the old one faded, which is half a crossfade.
                    Current = 0f,
                };
        }
    }

    /// <summary>Advances every fade and answers what should be playing now.</summary>
    /// <param name="seconds">How long since the last advance.</param>
    /// <returns>The voices, with their current volumes.</returns>
    /// <remarks>
    /// Voices that have finished fading out are dropped, exactly as `UpdateLoopingSounds` removes
    /// them once target and current are both zero. The caller is expected to stop the ones that
    /// stop appearing.
    /// </remarks>
    public IReadOnlyList<SoundscapeVoice> Advance(float seconds)
    {
        float amount = FadeSeconds > 0f ? seconds / FadeSeconds : 1f;

        List<SoundscapeVoice> voices = [];
        List<int> finished = [];

        foreach ((int key, Fading fading) in _playing)
        {
            fading.Current = Approach(fading.Target, fading.Current, amount);

            if (fading.Target == 0f && fading.Current == 0f)
            {
                finished.Add(key);
                continue;
            }

            voices.Add(new SoundscapeVoice(
                key, fading.Wave, fading.Current, fading.Position, fading.Attenuation));
        }

        foreach (int key in finished)
        {
            _playing.Remove(key);
        }

        return voices;
    }

    /// <summary>Forgets everything, for a seek or a new demo.</summary>
    public void Clear()
    {
        _playing.Clear();
        _current = null;
    }

    /// <summary>Which voices have just stopped, so the caller can silence them.</summary>
    /// <param name="live">The voices <see cref="Advance"/> just returned.</param>
    /// <param name="previous">The keys that were live before it.</param>
    /// <returns>The keys that are no longer playing.</returns>
    public static IReadOnlyList<int> Ended(
        IReadOnlyList<SoundscapeVoice> live, IReadOnlyCollection<int> previous)
    {
        ArgumentNullException.ThrowIfNull(live);
        ArgumentNullException.ThrowIfNull(previous);

        HashSet<int> still = [.. live.Select(voice => voice.Key)];

        return [.. previous.Where(key => !still.Contains(key))];
    }

    /// <summary>What a soundscape's loops become at a placement: the wave and where it plays.</summary>
    /// <remarks>
    /// **Resolved, because two placements of one soundscape differ only in what this produces.**
    /// A loop with no <c>position</c> is identical everywhere; a positioned one is identical only
    /// where the entity's targets agree. Comparing the resolved list answers "would restarting
    /// change anything" exactly, rather than by a proxy such as the entity id.
    ///
    /// A suppressed loop contributes nothing, which is right: one the engine refuses to play
    /// (<c>c_soundscape.cpp:797</c>) is not part of what is heard, so it cannot make two placements
    /// sound different.
    /// </remarks>
    private static List<(string Wave, float Pitch, (int X, int Y, int Z)? At)> Resolve(
        SoundscapePlacement placement, Soundscape? soundscape)
    {
        List<(string, float, (int, int, int)?)> resolved = [];

        if (soundscape is null)
        {
            return resolved;
        }

        foreach (SoundscapeSound sound in soundscape.Looping)
        {
            // **Wave and PITCH, because those are the engine's two conditions and volume is not
            // one of them.** `AddLoopingSound` matches on `sound.pitch == pitch` and the wave name;
            // a differing volume is written into `volumeTarget` and faded to, which is the whole
            // point of reusing the slot.
            if (sound.Position is not { } slot)
            {
                resolved.Add((sound.Wave, sound.Pitch, null));
                continue;
            }

            if (slot < placement.Positions.Count)
            {
                (float X, float Y, float Z) at = placement.Positions[slot];

                // **Compared at Valve's own tolerance rather than exactly.** `VectorsAreEqual(
                // position, sound.position, 0.1f )` — two entities whose targets differ by less
                // than a tenth of a unit are the same place, and comparing floats exactly would
                // restart a loop for a rounding difference no listener could hear.
                resolved.Add((
                    sound.Wave,
                    sound.Pitch,
                    (Tenths(at.X), Tenths(at.Y), Tenths(at.Z))));
            }
        }

        return resolved;

        static int Tenths(float value) => (int)MathF.Round(value * 10f);
    }

    /// <summary>Valve's <c>Approach</c>: move toward a goal by at most a step.</summary>
    private static float Approach(float target, float value, float step)
    {
        float difference = target - value;

        if (difference > step)
        {
            return value + step;
        }

        if (difference < -step)
        {
            return value - step;
        }

        return target;
    }
}
