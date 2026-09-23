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
/// rather than being filtered here. **`PlayImpactSound` IS routed in TF, for bullets**: `FX_FireBullets` sets the route to
/// `ImpactSoundGroup` around every shot (`tf_fx_shared.cpp:226`), which drops a repeat of the same sound within 300 units
/// in that shot — `ShotSoundGroup`, applied where a client bullet lands. This said the route was never set until a
/// comparison with TF2 heard 26 impact sounds from us against its 16. *Interpolated:* the draws, as for explosions — each
/// landing is its own stream of Valve's generator.
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

        for (int index = 0; index < landings.Count; index++)
        {
            sounds.AddRange(For(landings[index], SeedFor(index), scripts));
        }

        return sounds;
    }

    /// <summary>One landing's sounds, drawn from its own seed — for a bullet the client decides at play time.</summary>
    /// <param name="landing">The bullet.</param>
    /// <param name="seed">Its generator's seed.</param>
    /// <param name="scripts">Every soundscript entry the game loaded, by name.</param>
    /// <returns>Its sounds, each gated to the camera.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="scripts"/> is null.</exception>
    public static IReadOnlyList<SceneSound> For(BulletLanding landing, int seed, IReadOnlyDictionary<string, SoundScriptEntry> scripts)
    {
        ArgumentNullException.ThrowIfNull(scripts);

        List<SceneSound> sounds = [];
        UniformRandomStream random = new();

        random.SetSeed(seed);

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

        return sounds;
    }

    /// <summary>The seed the list form gives its landing at <paramref name="index"/>.</summary>
    /// <param name="index">The landing's place in its list, or any number unique to a landing emitted alone.</param>
    /// <returns>The generator's seed.</returns>
    public static int SeedFor(int index) => FirstSeed + index;
}
