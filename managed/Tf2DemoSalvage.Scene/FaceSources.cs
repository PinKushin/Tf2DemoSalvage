using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>One scene an actor is in: its plan and when it started and stopped, demo seconds (B513).</summary>
/// <param name="Plan">The scene's resolved plan.</param>
/// <param name="StartedSeconds">When <c>m_bIsPlayingBack</c> went true.</param>
/// <param name="StoppedSeconds">When it went false, or null while it plays.</param>
public readonly record struct FaceScene(SceneTaunt Plan, double StartedSeconds, double? StoppedSeconds);

/// <summary>One sound an entity's mouth follows: its sentence and when it began (B513).</summary>
/// <param name="Sentence">The cached lip-sync data.</param>
/// <param name="StartedSeconds">When the sound began, demo seconds.</param>
/// <param name="IgnorePhonemes"><c>SND_IGNORE_PHONEMES</c>.</param>
public readonly record struct FaceVoice(Sentence Sentence, double StartedSeconds, bool IgnorePhonemes)
{
    /// <summary>How fast the source plays — the pitch over 100 — which scales its elapsed time as the mixer's does.</summary>
    public double Rate { get; init; } = 1d;
}

/// <summary>What drives faces in a recording: every scene each actor plays, and every sentence-bearing sound (B513).</summary>
/// <remarks>
/// **Every scene, not the gesture slot's.** <c>C_SceneEntity::StartEvent</c> dispatches each event to the actor it names
/// (<c>c_sceneentity.cpp:459</c>), and <c>C_BaseFlex</c> keeps one <c>m_SceneEvents</c> list for all of them, so two scenes
/// on one player — a taunt and a pain expression — both reach the face. The gesture slot keeps only the latest scene
/// and drops one with no gesture (<c>PlayerProps.Choreographed</c>), which is right for the gesture and wrong here.
///
/// **The mouth (<c>CMouthInfo</c>, <c>mouthinfo.h</c>) holds up to four sources, keyed by the sound** — which channels
/// feed it is <see cref="Voices"/>.
/// </remarks>
public sealed class FaceSources
{
    /// <summary><c>CHAN_AUTO</c>, soundflags.h:22.</summary>
    private const int AutoChannel = 0;

    /// <summary><c>CHAN_STATIC</c>, soundflags.h:28.</summary>
    private const int StaticChannel = 6;

    /// <summary><c>CHAN_VOICE</c>, soundflags.h:24.</summary>
    private const int VoiceChannel = 2;

    /// <summary><c>CHAN_VOICE2</c>, soundflags.h:29.</summary>
    private const int VoiceChannel2 = 7;

    /// <summary><c>MAX_VOICE_DATA</c>, mouthinfo.h.</summary>
    private const int MouthSources = 4;

    private readonly Dictionary<int, List<FaceScene>> _scenes = [];
    private readonly Dictionary<int, List<(SceneSound Sound, double Start)>> _voices = [];
    private readonly IReadOnlyDictionary<string, Sentence> _sentences;
    private readonly IReadOnlyDictionary<string, float> _lengths;
    private readonly Func<string, float?>? _readLength;
    private readonly Dictionary<string, float?> _readLengths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<string, FlexSettings?> _expressions;

    /// <summary>Indexes a recording's scenes and voice sounds.</summary>
    /// <param name="scenes">Every scene playback the timeline saw.</param>
    /// <param name="plan">A scene's plan by name, or null when it cannot be read.</param>
    /// <param name="sounds">Every sound, in tick order.</param>
    /// <param name="intervalPerTick">Seconds per tick.</param>
    /// <param name="sentences">Lip-sync data by sound path (<see cref="SoundCacheFile.Normalise"/>).</param>
    /// <param name="expressions">Reads an expression file by name, for the phoneme classes.</param>
    /// <param name="lengths">Each cached sound's length in seconds, by path; null knows none.</param>
    /// <param name="readLength">
    /// A sound's length read from its own file, for one the cache does not list (<see cref="SoundLength"/>); null reads none.
    /// </param>
    public FaceSources(
        IEnumerable<SceneChoreography> scenes,
        Func<string, SceneTaunt?> plan,
        IEnumerable<SceneSound> sounds,
        double intervalPerTick,
        IReadOnlyDictionary<string, Sentence> sentences,
        Func<string, FlexSettings?> expressions,
        IReadOnlyDictionary<string, float>? lengths = null,
        Func<string, float?>? readLength = null)
    {
        _readLength = readLength;
        ArgumentNullException.ThrowIfNull(scenes);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(sounds);

        _lengths = lengths ?? new Dictionary<string, float>();
        _sentences = sentences ?? throw new ArgumentNullException(nameof(sentences));
        _expressions = expressions ?? throw new ArgumentNullException(nameof(expressions));

        foreach (SceneChoreography run in scenes)
        {
            if (run.Scene.Length == 0 || plan(run.Scene) is not { } resolved)
            {
                continue;
            }

            double? stopped = run.StoppedTick is { } stop ? stop * intervalPerTick : null;

            foreach (int actor in run.Actors)
            {
                if (!_scenes.TryGetValue(actor, out List<FaceScene>? list))
                {
                    list = [];
                    _scenes[actor] = list;
                }

                list.Add(new FaceScene(resolved, run.Tick * intervalPerTick, stopped));
            }
        }

        Dictionary<int, HashSet<int>> mouthsOf = [];

        foreach (SceneSound sound in sounds)
        {
            // The mouth is the speaker's when there is one, else the sound's own entity — the mouth update reads the
            // channel's speaker entity first (engine.dll 0x180046cf0: `+0xb4` when not -1, else the source `+0xac`).
            // A stop carries no speaker (ClearStopFields, soundinfo.h:153), so it reaches every mouth its entity's sounds
            // have fed, where Voices matches it by entity and channel.
            int mouth = sound.SpeakerEntity >= 0 ? sound.SpeakerEntity : sound.EntityIndex;
            double start = (sound.Tick * intervalPerTick) + sound.DelaySeconds;

            if (!mouthsOf.TryGetValue(sound.EntityIndex, out HashSet<int>? fed))
            {
                fed = [sound.EntityIndex];
                mouthsOf[sound.EntityIndex] = fed;
            }

            fed.Add(mouth);

            foreach (int target in sound.IsStop ? fed : [mouth])
            {
                if (!_voices.TryGetValue(target, out List<(SceneSound, double)>? list))
                {
                    list = [];
                    _voices[target] = list;
                }

                list.Add((sound, start));
            }
        }

        // A delayed sound starts after later ticks' sounds: the walk in Voices needs start order (stable, so one tick's
        // sounds keep the server's order).
        foreach (int entity in _voices.Keys.ToList())
        {
            _voices[entity] = [.. _voices[entity].OrderBy(one => one.Start)];
        }
    }

    /// <summary>The scenes an actor is playing now, in the order they started.</summary>
    /// <param name="actor">The entity.</param>
    /// <param name="seconds">Demo seconds.</param>
    /// <returns>The runs that have begun and not stopped.</returns>
    public IReadOnlyList<FaceScene> Scenes(int actor, double seconds) =>
        _scenes.TryGetValue(actor, out List<FaceScene>? runs)
            ? [.. runs.Where(run => run.StartedSeconds <= seconds && (run.StoppedSeconds is not { } stop || seconds < stop))]
            : [];

    /// <summary>The voice sources an entity's mouth holds now (<c>CMouthInfo</c>), at most four, in the order added.</summary>
    /// <param name="entity">The entity.</param>
    /// <param name="seconds">Demo seconds.</param>
    /// <returns>Each source with the start of the channel last mixing it.</returns>
    /// <remarks>
    /// **The mixer's rule, read in disassembly** (x64 <c>engine.dll</c>, <c>snd_mix.cpp</c>'s
    /// <c>MIX_MixChannelsToPaintbuffer</c> at <c>0x18003f7f0</c>): a channel feeds a mouth when it comes from the local
    /// player (<c>-2</c>), OR is on <c>CHAN_VOICE</c> or <c>CHAN_VOICE2</c>, OR its source carries a sentence — any channel
    /// at all. Each mix then calls the mouth update (<c>0x180046cf0</c>), which adds the source when it has a sentence
    /// (<c>AddSource</c>, refusing past four: "out of voice sources, won't lipsync"), or sets an existing one's elapsed
    /// time to the mixer's sample position over the source's rate — so pitch speeds it. Freeing the channel
    /// (<c>0x1800449d0</c>) removes the source, and **when the source is not in the mouth, empties it**: a voice line
    /// without a sentence ending clears every sentence still playing.
    ///
    /// A channel ends when its source runs out: the cache's sample count over the pitch, or for a sound the cache does not
    /// count, the file's own length (<see cref="SoundLength"/>). The mouth is the speaker entity's when the sound names one.
    /// </remarks>
    public IReadOnlyList<FaceVoice> Voices(int entity, double seconds)
    {
        if (!_voices.TryGetValue(entity, out List<(SceneSound Sound, double Start)>? sounds))
        {
            return [];
        }

        List<(string Path, FaceVoice Voice)> mouth = [];
        List<(int Channel, string Path, double End, bool Feeds)> playing = [];

        void Free(int at)
        {
            (_, string path, _, bool feeds) = playing[at];
            playing.RemoveAt(at);

            if (!feeds)
            {
                return;
            }

            int index = mouth.FindIndex(one => string.Equals(one.Path, path, StringComparison.OrdinalIgnoreCase));

            if (index >= 0)
            {
                mouth.RemoveAt(index);
            }
            else
            {
                mouth.Clear();
            }
        }

        void EndBefore(double time)
        {
            for (int at = EarliestEnd(playing, time); at >= 0; at = EarliestEnd(playing, time))
            {
                Free(at);
            }
        }

        foreach ((SceneSound sound, double start) in sounds)
        {
            if (start > seconds)
            {
                break;
            }

            EndBefore(start);
            string path = SoundPath(sound.Name);

            // SND_CHANGE_VOL / SND_CHANGE_PITCH retune a channel already playing; no channel starts.
            if (!sound.IsStop && (sound.ChangesVolume || sound.ChangesPitch))
            {
                continue;
            }

            // A stop, or a new sound on an entity's own channel, frees what was there (SND_PickChannel overrides the
            // same entity and channel; CHAN_AUTO and CHAN_STATIC take a free one).
            if (sound.IsStop || sound.Channel is not (AutoChannel or StaticChannel))
            {
                for (int at = playing.Count - 1; at >= 0; at--)
                {
                    if (playing[at].Channel == Slot(sound) &&
                        (!sound.IsStop || string.Equals(playing[at].Path, path, StringComparison.OrdinalIgnoreCase)))
                    {
                        Free(at);
                    }
                }
            }

            if (sound.IsStop)
            {
                continue;
            }

            _sentences.TryGetValue(path, out Sentence? sentence);
            bool feeds = sound.Channel is VoiceChannel or VoiceChannel2 || sentence is not null;
            double rate = sound.Pitch > 0 ? sound.Pitch / 100d : 1d;
            double end = LengthOf(path) is { } length ? start + (length / rate) : double.PositiveInfinity;

            playing.Add((Slot(sound), path, end, feeds));

            if (sentence is null)
            {
                continue;
            }

            FaceVoice voice = new(sentence, start, sound.IgnoresPhonemes) { Rate = rate };
            int existing = mouth.FindIndex(one => string.Equals(one.Path, path, StringComparison.OrdinalIgnoreCase));

            if (existing >= 0)
            {
                mouth[existing] = (path, voice);
            }
            else if (mouth.Count < MouthSources)
            {
                mouth.Add((path, voice));
            }
        }

        EndBefore(seconds);
        return [.. mouth.Select(one => one.Voice)];
    }

    /// <summary>
    /// How long a sound plays: the cache's count when it lists one, else the file's own (read once). The mixer frees a
    /// channel when its source runs out — a wave's data chunk, an MP3's last frame.
    /// </summary>
    private float? LengthOf(string path)
    {
        if (_lengths.TryGetValue(path, out float cached))
        {
            return cached;
        }

        if (_readLength is null)
        {
            return null;
        }

        if (!_readLengths.TryGetValue(path, out float? read))
        {
            read = _readLength(path);
            _readLengths[path] = read;
        }

        return read;
    }

    /// <summary>A channel's identity: the entity that made the sound and its channel, three bits (soundinfo.h:299).</summary>
    private static int Slot(SceneSound sound) => (sound.EntityIndex << 3) | (sound.Channel & 7);

    /// <summary>The playing channel that ends first at or before a time, or -1.</summary>
    private static int EarliestEnd(List<(int Channel, string Path, double End, bool Feeds)> playing, double time)
    {
        int found = -1;

        for (int at = 0; at < playing.Count; at++)
        {
            if (playing[at].End <= time && (found < 0 || playing[at].End < playing[found].End))
            {
                found = at;
            }
        }

        return found;
    }

    /// <summary>
    /// <c>GetInterpolationAmount( LATCH_ANIMATION_VAR )</c> in seconds — the delay <c>m_iv_flexWeight</c> is read at, the
    /// same one every interpolated variable of a demo is (<c>c_baseentity.cpp:5937</c>).
    /// </summary>
    public double InterpolationSeconds { get; init; } = 0.1d;

    /// <summary>An expression file by name, for the phoneme classes.</summary>
    /// <param name="name">As <c>FindSceneFile</c> takes it.</param>
    /// <returns>The file, or null.</returns>
    public FlexSettings? Expression(string name) => _expressions(name);

    /// <summary>A sound name as the cache keys it: the mixer's prefix characters dropped, under <c>sound\</c>.</summary>
    private static string SoundPath(string name)
    {
        string path = name.TrimStart('*', '#', '@', '>', '<', '^', ')', '}', '$', '!', '?', '&', '~', '`', '+', '%', '(');

        return SoundCacheFile.Normalise("sound/" + path);
    }
}
