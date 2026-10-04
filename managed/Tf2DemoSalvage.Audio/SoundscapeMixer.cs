using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Primitives;

namespace Tf2DemoSalvage.Audio;

/// <summary>One of a soundscape's loops while it is playing, fading in or out.</summary>
/// <param name="Key">
/// What identifies this voice to the sink. A soundscape's loops are not entities, so they are given
/// synthetic channel numbers on one reserved entity — see <see cref="SoundscapeMixer"/>.
/// </param>
/// <param name="Wave">The file to play.</param>
/// <param name="Volume">Where the fade currently stands, 0 to the script's own volume.</param>
/// <param name="Pitch">The pitch it was started at, 100 unshifted — <c>loopingsound_t::pitch</c>.</param>
/// <param name="Position">Where to play it, or <c>null</c> to play at the listener.</param>
/// <param name="SoundLevel">
/// Its <c>soundlevel_t</c>, which a positioned loop falls off by. An ambient loop carries <c>SNDLVL_NORM</c>
/// (<c>c_soundscape.cpp:1097</c>) and is not spatialised.
/// </param>
public readonly record struct SoundscapeVoice(
    int Key,
    string Wave,
    float Volume,
    int Pitch,
    (float X, float Y, float Z)? Position,
    int SoundLevel);

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
///
/// **The list is `m_loopingSounds`, in its own order** (B463): loops are appended in script order, removed with
/// `FastRemove` (the last moved into the hole), and `AddLoopingSound` scans it from the END. That order decides which
/// old loop a new one reclaims, so it is kept rather than replaced with a dictionary.
/// </remarks>
public sealed class SoundscapeMixer
{
    /// <summary><c>soundscape_fadetime</c>'s default, in seconds.</summary>
    public const float FadeSeconds = 3f;

    /// <summary>
    /// <c>VectorsAreEqual( position, sound.position, 0.1f )</c>'s tolerance (<c>c_soundscape.cpp:1124</c>).
    /// </summary>
    private const float SamePlace = 0.1f;

    /// <summary>
    /// <c>// non-ambients at 0 volume are culled, so start at 0.05</c> (<c>c_soundscape.cpp:1167-1178</c>).
    /// </summary>
    private const float PositionedStart = 0.05f;

    /// <summary>What is playing — <c>m_loopingSounds</c>.</summary>
    private readonly List<LoopingSound> _looping = [];

    /// <summary>The draws for ranged values — see <see cref="MoveTo"/>.</summary>
    private readonly UniformRandomStream _random = new();

    /// <summary>The placement currently in force, so a change can be noticed.</summary>
    private SoundscapePlacement? _current;

    /// <summary>Rises as soundscapes come and go — <c>m_loopingSoundId</c>.</summary>
    /// <remarks>
    /// **The client does the same** — `m_loopingSoundId++` in `StartNewSoundscape`. A loop already claimed by the
    /// soundscape now starting carries this id, which is what stops two identical loops in one soundscape sharing
    /// a slot (`sound.id != m_loopingSoundId`, <c>:1112</c>).
    /// </remarks>
    private int _generation;

    /// <summary>The next sink key, so a sound that is stopped and restarted never reuses one still sounding.</summary>
    private int _nextKey;

    /// <summary>One loop, with the two volumes the client's model gives it — <c>loopingsound_t</c>.</summary>
    /// <remarks>
    /// A class rather than a record struct because the volumes are mutated in place every advance and
    /// <c>AddLoopingSound</c> refills a reclaimed slot, which is what `loopingsound_t` does.
    /// </remarks>
    private sealed class LoopingSound
    {
        /// <summary>The sink's key for the sound now in this slot.</summary>
        public int Key { get; set; }

        public string Wave { get; set; } = string.Empty;

        public int Pitch { get; set; }

        /// <summary>Where it plays, or null for an ambient loop — <c>isAmbient</c>.</summary>
        public (float X, float Y, float Z)? Position { get; set; }

        public int SoundLevel { get; set; }

        public int Id { get; set; }

        public float Target { get; set; }

        public float Current { get; set; }
    }

    /// <summary>How many voices the soundscape is currently running.</summary>
    public int Count => _looping.Count;

    /// <summary>The soundscape in force, or <c>null</c> when none is.</summary>
    public SoundscapePlacement? Current => _current;

    /// <summary>Moves to a soundscape, crossfading from whatever was playing.</summary>
    /// <param name="placement">The chosen placement, or <c>null</c> for params naming no entity — which starts nothing and leaves every loop playing.</param>
    /// <param name="soundscape">Its definition, or <c>null</c> when the catalog has none.</param>
    /// <remarks>
    /// **Does nothing when neither the soundscape nor the entity changed**, which is the common
    /// case — this is asked every update and the answer is usually the same. `UpdateAudioParams`
    /// returns early on exactly that condition.
    ///
    /// ***Interpolated:* the draws.** <c>RandomInterval</c> draws from the engine's global stream, whose state no demo
    /// records. Here it is Valve's generator seeded by the placement, so a seek back to the same entity draws the
    /// same values.
    /// </remarks>
    public void MoveTo(SoundscapePlacement? placement, Soundscape? soundscape)
    {
        // **Index AND entity, which is `UpdateAudioParams`'s own condition.** The same soundscape
        // reached from a different `env_soundscape` is a change, because the positions its loops
        // play at come from that entity — cp_process has 21 entities all naming `Gorge.Inside`.
        if (_current is { } held && placement is { } next &&
            held.Index == next.Index &&
            held.Id == next.Id)
        {
            return;
        }

        _current = placement;

        // **No entity, or an index the client has no soundscape for, starts nothing** — `StartNewSoundscape` is called
        // only `if ( audio.entIndex > 0 && audio.soundscapeIndex >= 0 && audio.soundscapeIndex < m_soundscapes.Count() )`
        // (`c_soundscape.cpp:562-566`); otherwise only `m_params` changes, and the loops already sounding carry on (B463,
        // B484). No entity used to fade everything out.
        //
        // Stryker disable once : a mutant that empties the guard body leaves 'placed'
        // unassigned (CS0165), and Safe Mode then drops every mutation in this method — B410.
        if (placement is not { } placed || soundscape is null)
        {
            return;
        }

        // Every existing loop starts fading out. They keep playing meanwhile, which is the whole
        // point of a crossfade — the new room's ambience rises as the old one falls.
        foreach (LoopingSound sound in _looping)
        {
            sound.Target = 0f;
        }

        _generation++;

        _random.SetSeed(placed.Id);

        foreach (SoundscapeSound sound in soundscape.Looping)
        {
            PlayLooping(sound, placed);
        }
    }

    /// <summary><c>ProcessPlayLooping</c>'s second half: draw, then start the loop or suppress it.</summary>
    private void PlayLooping(SoundscapeSound sound, SoundscapePlacement placed)
    {
        // `masterVolume * RandomInterval(...)` with master 1.0 at the top level (`:610`, `:735`); the pitch is an int,
        // so the draw truncates on assignment (`:739`).
        float volume = sound.Volume.Random(_random);
        int pitch = (int)sound.Pitch.Random(_random);
        int level = sound.Level.Draw(_random);

        // `if ( volume != 0 && pSoundName != NULL )` (`:790`). A loop with no `volume` key draws zero and never
        // starts; it used to play at full volume.
        if (volume == 0f)
        {
            return;
        }

        // `if ( positionIndex < 0 ) positionIndex = ambientPositionOverride` — -1 at the top level — and then
        // `if ( positionIndex < 0 ) AddLoopingAmbient` (`:769-793`). A negative position is an ambient loop.
        if (sound.Position is not { } slot || slot < 0)
        {
            AddLoopingSound(sound.Wave, null, volume, SoundAttenuation.Normal, pitch);
            return;
        }

        // **A position the map did not supply SUPPRESSES the sound.** `if ( positionIndex > 31 ||
        // !(m_params.localBits & (1<<positionIndex)) ) { // suppress sounds if the position isn't available; return; }`
        // (`:797-801`). The placement keeps each slot at its own index, unset ones null (B464).
        //
        // **Falling back to the listener instead is loud and wrong**, and it was: this first played an unavailable
        // position at the listener with no attenuation, which on cp_process meant SEVEN copies of machine_hum
        // stacked in the listener's ear. The owner heard it immediately — "its specifically the cpu sound".
        if (slot >= placed.Positions.Count || placed.Positions[slot] is not { } at)
        {
            return;
        }

        AddLoopingSound(sound.Wave, at, volume, level, pitch);
    }

    /// <summary><c>AddLoopingSound</c> (<c>c_soundscape.cpp:1103-1197</c>): reclaim a slot if one fits, else add one.</summary>
    /// <param name="wave">The file.</param>
    /// <param name="position">Where it plays, or null for an ambient loop.</param>
    /// <param name="volume">The drawn volume, which becomes the target.</param>
    /// <param name="level">The drawn soundlevel.</param>
    /// <param name="pitch">The drawn pitch.</param>
    /// <remarks>
    /// **The reuse is per LOOP and across soundscapes**, under Valve's own note — <i>"will reuse existing entry (fade
    /// from current volume) if possible / this prevents pops"</i>. It was all-or-nothing within one soundscape index,
    /// so a wave two soundscapes share played twice across every threshold between them, one copy fading out under
    /// another fading in from silence.
    /// </remarks>
    private void AddLoopingSound(string wave, (float X, float Y, float Z)? position, float volume, int level, int pitch)
    {
        LoopingSound? slot = null;
        bool restart = false;

        // **From the END**, as `int soundSlot = m_loopingSounds.Count() - 1; while ( soundSlot >= 0 )` does — and the
        // new loops are added in script order, so the first new loop meets the LAST old one.
        for (int index = _looping.Count - 1; index >= 0; index--)
        {
            LoopingSound sound = _looping[index];

            if (sound.Id == _generation ||
                sound.Pitch != pitch ||
                !wave.Equals(sound.Wave, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // `if ( isAmbient == true && sound.isAmbient == true ) { // reuse this sound` — wherever it came from.
            if (position is null && sound.Position is null)
            {
                slot = sound;
                break;
            }

            // `else if ( isAmbient == sound.isAmbient )`: both positioned. The same place reuses the slot; a different
            // one STOPS the old sound at once and restarts the slot there (`StopLoopingSound`, `bForceSoundUpdate`),
            // at the volume the slot had — a new key is how the sink is told to do both.
            if (position is { } here && sound.Position is { } there)
            {
                slot = sound;
                restart = !VectorsAreEqual(here, there);
                break;
            }
        }

        if (slot is null)
        {
            // `can't find the sound in the list, make a new one`: an ambient loop starts at 0 and fades in; a
            // positioned one at 0.05, because the engine culls a non-ambient emitted at 0.
            slot = new LoopingSound { Key = _nextKey++, Current = position is null ? 0f : PositionedStart };
            _looping.Add(slot);
        }
        else if (restart)
        {
            slot.Key = _nextKey++;
        }

        // `fill out the slot` (`:1182-1189`).
        slot.Wave = wave;
        slot.Target = volume;
        slot.Pitch = pitch;
        slot.Id = _generation;
        slot.Position = position;
        slot.SoundLevel = level;
    }

    /// <summary>Advances every fade and answers what should be playing now.</summary>
    /// <param name="seconds">How long since the last advance.</param>
    /// <returns>The voices, with their current volumes.</returns>
    /// <remarks>
    /// Voices that have finished fading out are dropped, exactly as `UpdateLoopingSounds` removes
    /// them once target and current are both zero — from the end, with `FastRemove`. The caller is expected to stop
    /// the ones that stop appearing.
    /// </remarks>
    public IReadOnlyList<SoundscapeVoice> Advance(float seconds)
    {
        float amount = FadeSeconds > 0f ? seconds / FadeSeconds : 1f;

        for (int index = _looping.Count - 1; index >= 0; index--)
        {
            LoopingSound sound = _looping[index];

            // `if ( sound.volumeCurrent != sound.volumeTarget )` (`:512`) guards the removal too, so a slot that was
            // ALREADY at zero and zero — started and cancelled before any time passed — stays, silent, and can be
            // reclaimed by the next soundscape that plays its wave.
#pragma warning disable S1244 // Valve's own exact comparison: Approach lands ON the target, so equal means arrived.
            if (sound.Current == sound.Target)
#pragma warning restore S1244
            {
                continue;
            }

            sound.Current = Approach(sound.Target, sound.Current, amount);

            if (sound.Target == 0f && sound.Current == 0f)
            {
                // `FastRemove`: the last element moves into the hole, which is the order the next scan sees.
                _looping[index] = _looping[^1];
                _looping.RemoveAt(_looping.Count - 1);
            }
        }

        return [.. _looping.Select(sound =>
            new SoundscapeVoice(sound.Key, sound.Wave, sound.Current, sound.Pitch, sound.Position, sound.SoundLevel))];
    }

    /// <summary>Forgets everything, for a seek or a new demo.</summary>
    public void Clear()
    {
        _looping.Clear();
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

    /// <summary>Valve's <c>VectorsAreEqual</c> at <see cref="SamePlace"/> (<c>mathlib/vector.h:1303-1310</c>).</summary>
    private static bool VectorsAreEqual((float X, float Y, float Z) first, (float X, float Y, float Z) second) =>
        MathF.Abs(first.X - second.X) <= SamePlace &&
        MathF.Abs(first.Y - second.Y) <= SamePlace &&
        MathF.Abs(first.Z - second.Z) <= SamePlace;

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
