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
public readonly record struct FaceVoice(Sentence Sentence, double StartedSeconds, bool IgnorePhonemes);

/// <summary>What drives faces in a recording: every scene each actor plays, and every sentence-bearing sound (B513).</summary>
/// <remarks>
/// **Every scene, not the gesture slot's.** <c>C_SceneEntity::StartEvent</c> dispatches each event to the actor it names
/// (<c>c_sceneentity.cpp:459</c>), and <c>C_BaseFlex</c> keeps one <c>m_SceneEvents</c> list for all of them, so two scenes
/// on one player — a taunt and a pain expression — both reach the face. The gesture slot keeps only the latest scene
/// and drops one with no gesture (<c>PlayerProps.Choreographed</c>), which is right for the gesture and wrong here.
///
/// **The mouth (<c>CMouthInfo</c>, <c>mouthinfo.h</c>) holds up to four sources, keyed by the sound.** Which channels the
/// engine registers is in <c>engine.dll</c>'s mixer and was not located in disassembly; this takes the two voice
/// channels, <c>CHAN_VOICE</c> and <c>CHAN_VOICE2</c> — **interpolated** — and a source lasts while its sound plays,
/// until a later sound or a stop on the same channel replaces it.
/// </remarks>
public sealed class FaceSources
{
    /// <summary><c>CHAN_VOICE</c>, soundflags.h:24.</summary>
    private const int VoiceChannel = 2;

    /// <summary><c>CHAN_VOICE2</c>, soundflags.h:29.</summary>
    private const int VoiceChannel2 = 7;

    /// <summary><c>MAX_VOICE_DATA</c>, mouthinfo.h.</summary>
    private const int MouthSources = 4;

    private readonly Dictionary<int, List<FaceScene>> _scenes = [];
    private readonly Dictionary<int, List<(SceneSound Sound, double Start)>> _voices = [];
    private readonly IReadOnlyDictionary<string, Sentence> _sentences;
    private readonly Func<string, FlexSettings?> _expressions;

    /// <summary>Indexes a recording's scenes and voice sounds.</summary>
    /// <param name="scenes">Every scene playback the timeline saw.</param>
    /// <param name="plan">A scene's plan by name, or null when it cannot be read.</param>
    /// <param name="sounds">Every sound, in tick order.</param>
    /// <param name="intervalPerTick">Seconds per tick.</param>
    /// <param name="sentences">Lip-sync data by sound path (<see cref="SoundCacheFile.Normalise"/>).</param>
    /// <param name="expressions">Reads an expression file by name, for the phoneme classes.</param>
    public FaceSources(
        IEnumerable<SceneChoreography> scenes,
        Func<string, SceneTaunt?> plan,
        IEnumerable<SceneSound> sounds,
        double intervalPerTick,
        IReadOnlyDictionary<string, Sentence> sentences,
        Func<string, FlexSettings?> expressions)
    {
        ArgumentNullException.ThrowIfNull(scenes);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(sounds);

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

        foreach (SceneSound sound in sounds)
        {
            if (sound.Channel is not (VoiceChannel or VoiceChannel2))
            {
                continue;
            }

            if (!_voices.TryGetValue(sound.EntityIndex, out List<(SceneSound, double)>? list))
            {
                list = [];
                _voices[sound.EntityIndex] = list;
            }

            list.Add((sound, (sound.Tick * intervalPerTick) + sound.DelaySeconds));
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

    /// <summary>The voice sources an entity's mouth holds now, at most four.</summary>
    /// <param name="entity">The entity.</param>
    /// <param name="seconds">Demo seconds.</param>
    /// <returns>Each channel's playing sound that carries a sentence.</returns>
    public IReadOnlyList<FaceVoice> Voices(int entity, double seconds)
    {
        if (!_voices.TryGetValue(entity, out List<(SceneSound Sound, double Start)>? sounds))
        {
            return [];
        }

        List<FaceVoice> found = [];

        foreach (int channel in (int[])[VoiceChannel, VoiceChannel2])
        {
            (SceneSound Sound, double Start)? latest = null;

            foreach ((SceneSound sound, double start) in sounds)
            {
                if (sound.Channel == channel && start <= seconds)
                {
                    latest = (sound, start);
                }
            }

            if (latest is not { } playing || playing.Sound.IsStop ||
                !_sentences.TryGetValue(SoundPath(playing.Sound.Name), out Sentence? sentence) ||
                seconds - playing.Start >= sentence.Length)
            {
                continue;
            }

            if (found.Count < MouthSources)
            {
                found.Add(new FaceVoice(sentence, playing.Start, playing.Sound.IgnoresPhonemes));
            }
        }

        return found;
    }

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
