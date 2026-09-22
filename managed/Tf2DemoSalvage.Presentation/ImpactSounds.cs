using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Core.Primitives;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Presentation;

/// <summary>One bullet's landing as `ImpactCallback` hears it.</summary>
/// <param name="Tick">When the impact fires.</param>
/// <param name="At">Where the sound plays: the decal trace's end, or the server's origin when it met nothing.</param>
/// <param name="ImpactSound">The struck surface's `bulletimpact` script name, or null when it declares none.</param>
/// <param name="Ricochets">Whether `Impact` returned true for a `DMG_BULLET`, which offers the shrapnel ricochet.</param>
public readonly record struct BulletLanding(int Tick, (float X, float Y, float Z) At, string? ImpactSound, bool Ricochets);

/// <summary>The sounds a bullet makes where it lands — `ImpactCallback`'s (`tf_fx_impacts.cpp:51-134`), which no demo carries (B415).</summary>
/// <remarks>
/// <code>
/// bPlaySound = ( MainViewOrigin() − vecOrigin ).LengthSqr() &lt; 1024²
/// if ( Impact( … ) )  … if ( bPlaySound &amp;&amp; RandomInt( 1, 10 ) ≤ 3 &amp;&amp; iDamageType == DMG_BULLET )  "Bounce.Shrapnel" at vecOrigin
/// if ( bPlaySound )  PlayImpactSound( … )   // the surface's bulletimpact, CLocalPlayerFilter, at the trace's end
/// </code>
/// **The camera gate is decided when the sound starts**, so each sound carries it as <see cref="SceneSound.AudibleWithin"/>
/// rather than being filtered here. `PlayImpactSound` has no route in TF (`g_pImpactSoundRouteFn` is never set), so it is a
/// plain `EmitSound`. *Interpolated:* the draws, as for explosions — each landing is its own stream of Valve's generator.
/// </remarks>
public static class ImpactSounds
{
    /// <summary>`ImpactCallback`'s camera gate — 1024 units.</summary>
    public const float AudibleWithin = 1024f;

    /// <summary>The ricochet's script name.</summary>
    public const string Shrapnel = "Bounce.Shrapnel";

    /// <summary>Where each landing's draws start, clear of the explosions' seeds.</summary>
    private const int FirstSeed = 1 << 20;

    /// <summary>The sounds for every landing, in their order.</summary>
    /// <param name="landings">The bullets, in tick order.</param>
    /// <param name="scripts">Every soundscript entry the game loaded, by name.</param>
    /// <returns>Their sounds, each gated to the camera.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static IReadOnlyList<SceneSound> For(IReadOnlyList<BulletLanding> landings, IReadOnlyDictionary<string, SoundScriptEntry> scripts)
    {
        ArgumentNullException.ThrowIfNull(landings);
        ArgumentNullException.ThrowIfNull(scripts);

        List<SceneSound> sounds = [];
        UniformRandomStream random = new();

        for (int index = 0; index < landings.Count; index++)
        {
            BulletLanding landing = landings[index];

            random.SetSeed(FirstSeed + index);

            // `random->RandomInt( 1, 10 ) <= 3`, asked only when `Impact` returned true for a bullet.
            if (landing.Ricochets && random.RandomInt(1, 10) <= 3 &&
                scripts.TryGetValue(Shrapnel, out SoundScriptEntry bounce) && bounce.Waves.Count > 0)
            {
                sounds.Add(ExplosionSounds.FromWorldAt(bounce, random, landing.Tick, landing.At) with { AudibleWithin = AudibleWithin });
            }

            if (landing.ImpactSound is { } name &&
                scripts.TryGetValue(name, out SoundScriptEntry impact) && impact.Waves.Count > 0)
            {
                sounds.Add(ExplosionSounds.FromWorldAt(impact, random, landing.Tick, landing.At) with { AudibleWithin = AudibleWithin });
            }
        }

        return sounds;
    }
}
