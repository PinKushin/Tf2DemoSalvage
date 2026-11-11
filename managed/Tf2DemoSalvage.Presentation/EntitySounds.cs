using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Core.Primitives;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Presentation;

/// <summary>`C_BaseEntity::EmitSound( filter, entindex, name, &amp;origin )` — a script sound from an entity.</summary>
public static class EntitySounds
{
    /// <summary>The sound a script entry makes from an entity, drawn from a stream seeded by tick and entity.</summary>
    /// <param name="tick">When.</param>
    /// <param name="entity">`GetSoundSourceIndex()`.</param>
    /// <param name="name">The script name.</param>
    /// <param name="at">Where.</param>
    /// <param name="scripts">The sound scripts.</param>
    /// <param name="emitted">
    /// True for `EmitSound` by script name, which deals the wave; false for a plain `GetParametersForSound` — a footstep,
    /// a physics impact, a sound patch — which only reads the deck (B503).
    /// </param>
    /// <param name="phase">Where on the client's frame it is made (B505).</param>
    /// <returns>
    /// The sound, or null when no script declares the name — `GetParametersForSound` fails and nothing plays. An emitted
    /// raw name plays as itself (<see cref="PlaysDirectly"/>, B569).
    /// </returns>
    public static SceneSound? Emit(
        int tick,
        int entity,
        string name,
        (float X, float Y, float Z) at,
        IReadOnlyDictionary<string, SoundScriptEntry> scripts,
        bool emitted,
        ClientSoundPhase phase)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (emitted && PlaysDirectly(name))
        {
            // `enginesound->EmitSound` with `CBaseEntity::EmitSound`'s `EmitSound_t`, which sets only the name, time and
            // origin (`SoundEmitterSystem.cpp:1190-1206`); the rest are its constructor's (`shareddefs.h:832-848`):
            // channel 0 (`CHAN_AUTO`), `VOL_NORM`, `SNDLVL_NONE`, `PITCH_NORM`. No script, so no deck is dealt.
            return new SceneSound(
                tick, name, ExplosionSounds.NotPrecached, entity, Channel: 0, Volume: 1f, SoundLevel: 0, Pitch: 100,
                DelaySeconds: 0f, at.X, at.Y, at.Z)
            {
                Order = new ClientSoundOrder(phase, 0, 0),
            };
        }

        if (scripts is null || !scripts.TryGetValue(name, out SoundScriptEntry entry) || entry.Waves.Count == 0)
        {
            return null;
        }

        return ExplosionSounds.FromWorldAt(entry, Stream(tick, entity), tick, at, emitted) with
        {
            EntityIndex = entity,
            Order = new ClientSoundOrder(phase, 0, 0),
        };
    }

    /// <summary>Whether `CSoundEmitterSystem::EmitSound` plays a name as itself rather than as a script.</summary>
    /// <param name="name">The emitted name.</param>
    /// <returns>True for a name containing `.wav` or `.mp3` in either case, or starting with `!` (a sentence).</returns>
    /// <remarks>`SoundEmitterSystem.cpp:556-559`: both extensions by `Q_stristr`, before the script lookup.</remarks>
    internal static bool PlaysDirectly(string name) =>
        name.Contains(".wav", StringComparison.OrdinalIgnoreCase) ||
        name.Contains(".mp3", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith('!');

    /// <summary>The stream an entity's sounds at a tick draw from.</summary>
    /// <param name="tick">When.</param>
    /// <param name="entity">The entity.</param>
    /// <returns>A stream seeded by both.</returns>
    /// <remarks>The engine draws from its global stream, which no demo records; seeded so a seek hears the same wave.</remarks>
    public static UniformRandomStream Stream(int tick, int entity)
    {
        UniformRandomStream random = new();
        random.SetSeed(ImpactSounds.SeedFor((tick * 64) + entity));
        return random;
    }
}
