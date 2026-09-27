using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Core.Primitives;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Presentation;

/// <summary>
/// Resolves a HUD element's `game_sounds.txt` name — announcer lines, chat pings — into a playable sound, on the
/// far side of <see cref="Scene.Hud.HudSoundEmitter"/> so <c>Tf2DemoSalvage.Scene</c> never references audio.
/// </summary>
/// <remarks>
/// **`C_BaseEntity::EmitSound` with `SOUND_FROM_LOCAL_PLAYER` and a `CLocalPlayerFilter`**
/// (`tf_hud_deathnotice.cpp:751`, `hud_basechat.cpp:793`) — always the local listener's own 2D sound, from no
/// particular place in the world. `EmitSoundByHandle` resolves the script exactly as <see cref="ExplosionSounds"/>
/// does for a world sound: one wave, a volume and a pitch drawn from the entry's ranges.
/// </remarks>
public static class HudSounds
{
    /// <summary>`SOUND_FROM_LOCAL_PLAYER` (shareddefs.h): entity 0, the same slot a world sound (`SOUND_FROM_WORLD`) uses.</summary>
    public const int FromLocalPlayer = 0;

    /// <summary>Resolves one script name, drawing its wave, volume and pitch from a stream seeded by the tick and name.</summary>
    /// <param name="tick">When it was asked for.</param>
    /// <param name="scriptName">The `game_sounds.txt` entry — <c>"Game.Domination"</c>, <c>"HudChat.Message"</c>, and so on.</param>
    /// <param name="scripts">Every soundscript entry the game loaded, by name.</param>
    /// <returns>The sound, or null when no script declares the name — `GetParametersForSound` fails and nothing plays.</returns>
    public static SceneSound? Emit(int tick, string scriptName, IReadOnlyDictionary<string, SoundScriptEntry> scripts)
    {
        ArgumentNullException.ThrowIfNull(scriptName);

        if (scripts is null || !scripts.TryGetValue(scriptName, out SoundScriptEntry entry) || entry.Waves.Count == 0)
        {
            return null;
        }

        // The engine draws from its global stream, which no demo records; seeded so a seek hears the same wave.
        UniformRandomStream random = new();
        random.SetSeed(ImpactSounds.SeedFor((tick * 131) + scriptName.GetHashCode(StringComparison.Ordinal)));

        return ExplosionSounds.FromWorldAt(entry, random, tick, (0f, 0f, 0f)) with { EntityIndex = FromLocalPlayer };
    }
}
