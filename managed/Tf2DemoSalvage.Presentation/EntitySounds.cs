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
    /// <returns>The sound, or null when no script declares the name — `GetParametersForSound` fails and nothing plays.</returns>
    public static SceneSound? Emit(int tick, int entity, string name, (float X, float Y, float Z) at, IReadOnlyDictionary<string, SoundScriptEntry> scripts)
    {
        if (scripts is null || !scripts.TryGetValue(name, out SoundScriptEntry entry) || entry.Waves.Count == 0)
        {
            return null;
        }

        return ExplosionSounds.FromWorldAt(entry, Stream(tick, entity), tick, at) with { EntityIndex = entity };
    }

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
