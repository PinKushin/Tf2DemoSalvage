using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Core.Primitives;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Presentation;

/// <summary>Every explosion's sound, emitted as the client emits it — a sound no demo carries (B415).</summary>
/// <remarks>
/// **`TFExplosionCallback` plays it itself**: `C_BaseEntity::EmitSound( filter, SOUND_FROM_WORLD, pszSound,
/// &amp;vecOrigin )` with a <c>CLocalPlayerFilter</c> (`tf_fx_explosions.cpp:162-163`), so the server never sends it
/// and `svc_Sounds` never carries it. A viewer that plays only what the demo recorded is silent for every blast.
///
/// **What the engine does with the name** is `CSoundEmitterSystem::EmitSoundByHandle`
/// (`SoundEmitterSystem.cpp:450-525`): `GetParametersForSoundEx` resolves the script entry — its channel and
/// soundlevel, a volume and a pitch drawn from its ranges, one of its waves — and hands them to
/// `enginesound->EmitSound` from entity 0 at the blast's origin. `EmitSound_t`'s flags default to 0
/// (`shareddefs.h:837`) and this overload sets none, so `SND_CHANGE_PITCH`/`SND_CHANGE_VOL` never override the draws.
///
/// ***Interpolated:* the draws.** The engine takes them from its global random stream, whose state depends on every
/// other draw the client made and is recorded nowhere. Each blast here is its own stream of Valve's generator, seeded
/// by the blast's place in the recording, so a seek replays the same wave rather than a different one each time.
/// </remarks>
public static class ExplosionSounds
{
    /// <summary>`SOUND_FROM_WORLD` — the entity an explosion's sound comes from.</summary>
    public const int FromWorld = 0;

    /// <summary>What <see cref="SceneSound.SoundNumber"/> holds for a sound no precache index named.</summary>
    public const int NotPrecached = -1;

    /// <summary>The sound each blast makes, in the blasts' own order.</summary>
    /// <param name="blasts">The explosions, in tick order.</param>
    /// <param name="soundFor">The script name a blast plays — <c>ExplosionEffects.SoundFor</c>.</param>
    /// <param name="scripts">Every soundscript entry the game loaded, by name.</param>
    /// <returns>One sound per blast whose name a script declares.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static IReadOnlyList<SceneSound> For(
        IReadOnlyList<SceneExplosion> blasts,
        Func<SceneExplosion, string> soundFor,
        IReadOnlyDictionary<string, SoundScriptEntry> scripts)
    {
        ArgumentNullException.ThrowIfNull(blasts);
        ArgumentNullException.ThrowIfNull(soundFor);
        ArgumentNullException.ThrowIfNull(scripts);

        List<SceneSound> sounds = new(blasts.Count);
        UniformRandomStream random = new();

        for (int index = 0; index < blasts.Count; index++)
        {
            SceneExplosion blast = blasts[index];

            // `GetParametersForSoundEx` fails for a name no script declares, and `EmitSoundByHandle` returns.
            if (!scripts.TryGetValue(soundFor(blast), out SoundScriptEntry entry) || entry.Waves.Count == 0)
            {
                continue;
            }

            random.SetSeed(index);

            sounds.Add(FromWorldAt(entry, random, blast.Tick, (blast.X, blast.Y, blast.Z)));
        }

        return sounds;
    }

    /// <summary>`EmitSoundByHandle` from `SOUND_FROM_WORLD` at an origin: one wave, a volume and a pitch drawn from the entry.</summary>
    /// <param name="entry">The script entry, which has at least one wave.</param>
    /// <param name="random">The stream the draws come from, already seeded.</param>
    /// <param name="tick">When it starts.</param>
    /// <param name="at">Where.</param>
    /// <returns>The sound.</returns>
    internal static SceneSound FromWorldAt(SoundScriptEntry entry, UniformRandomStream random, int tick, (float X, float Y, float Z) at)
    {
        string wave = entry.Waves[random.RandomInt(0, entry.Waves.Count - 1)];
        float volume = random.RandomFloat(entry.Volume.Low, entry.Volume.High);

        // `CSoundParameters::pitch` is an int, so the float draw truncates on assignment.
        int pitch = (int)random.RandomFloat(entry.Pitch.Low, entry.Pitch.High);

        return new SceneSound(
            tick, wave, NotPrecached, FromWorld, entry.Channel, volume, entry.SoundLevel, pitch, DelaySeconds: 0f, at.X, at.Y, at.Z);
    }

    /// <summary>Two tick-ordered sound lists as one, the demo's own first where they share a tick.</summary>
    /// <param name="demo">What the recording carried, in tick order.</param>
    /// <param name="effects">What the client emits itself, in tick order.</param>
    /// <returns>Both, in tick order.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// A merge rather than a sort, because <see cref="SoundSchedule"/> walks the result with a cursor and both inputs
    /// are already in order; a sort would also be free to reorder two sounds on one tick, which a merge never does.
    /// </remarks>
    public static IReadOnlyList<SceneSound> Merged(IReadOnlyList<SceneSound> demo, IReadOnlyList<SceneSound> effects)
    {
        ArgumentNullException.ThrowIfNull(demo);
        ArgumentNullException.ThrowIfNull(effects);

        List<SceneSound> merged = new(demo.Count + effects.Count);

        int fromDemo = 0;
        int fromEffects = 0;

        while (fromDemo < demo.Count || fromEffects < effects.Count)
        {
            bool takeDemo = fromEffects == effects.Count ||
                (fromDemo < demo.Count && demo[fromDemo].Tick <= effects[fromEffects].Tick);

            merged.Add(takeDemo ? demo[fromDemo++] : effects[fromEffects++]);
        }

        return merged;
    }
}
