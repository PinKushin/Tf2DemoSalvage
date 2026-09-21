using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>Which particle system an explosion draws, and which way up — <c>TFExplosionCallback</c> (B415).</summary>
/// <remarks>
/// **`tf_fx_explosions.cpp:42-170`, in its own order**, which is what decides the answer:
///
/// <code>
/// const char *pszEffect = "ExplosionCore_wall";
/// if ( iCustomParticleIndex != INVALID_STRING_INDEX )
///     pszEffect = GetParticleSystemNameFromIndex( iCustomParticleIndex );
/// if ( pWeaponInfo )
/// {
///     if ( iCustomParticleIndex == INVALID_STRING_INDEX )
///     {
///         if ( bIsWater )              { if ( … m_szExplosionWaterEffect  ) pszEffect = …WaterEffect;  }
///         else if ( bIsPlayer || bInAir ) { if ( … m_szExplosionPlayerEffect ) pszEffect = …PlayerEffect; }
///         else                            { if ( … m_szExplosionEffect       ) pszEffect = …Effect;       }
///     }
/// }
/// </code>
///
/// **Water is exclusive of the player-or-air branch**, not layered on top of it — an `else if`. And each of the
/// three only wins when the script's string is non-empty, so a weapon that declares two of them falls back to
/// `ExplosionCore_wall` for the third rather than to one of the others.
///
/// **`bInAir` shares the player branch, and that is the whole reason the weapon scripts had to be read.** The
/// rocket launcher's `ExplosionEffect` is `ExplosionCore_wall`, the same as the default — but its
/// `ExplosionPlayerEffect` is `ExplosionCore_MidAir`, and 449 of `demostf-cp_process_f12`'s 2,786 blasts are in
/// mid air. Skipping the scripts would have looked right for every wall hit and been wrong for every airburst.
///
/// **What is NOT evaluated here, stated rather than hidden:**
///
/// - **`bIsWater`** is `UTIL_PointContents( vecOrigin ) &amp; CONTENTS_WATER`, and this project does not evaluate BSP
///   contents at a point. The caller passes `false`, so a blast in water takes the air or wall effect.
/// - **`GameRules()->TranslateEffectForVisionFilter( "particles", pszEffect )`**, which `TFExplosionCallback`
///   applies last. Unread.
/// - **The sound.** `m_nDefID` and `m_nSound` pick a replacement sound out of the item definition and nothing
///   about what is drawn.
/// </remarks>
public sealed class ExplosionEffects
{
    /// <summary>What every explosion starts as, before a script or a custom index can change it.</summary>
    public const string DefaultEffect = "ExplosionCore_wall";

    /// <summary>`m_szExplosionEffect`'s key in the weapon script.</summary>
    private const string WallKey = "ExplosionEffect";

    /// <summary>`m_szExplosionPlayerEffect`'s key.</summary>
    private const string PlayerKey = "ExplosionPlayerEffect";

    /// <summary>`m_szExplosionWaterEffect`'s key.</summary>
    private const string WaterKey = "ExplosionWaterEffect";

    private readonly Func<string, byte[]?> _readFile;

    /// <summary>
    /// Scripts already read, by alias, including the misses — a weapon with no script must not be looked up 2,786
    /// times, and a null here means "asked and absent" rather than "not asked".
    /// </summary>
    private readonly Dictionary<string, WeaponScript?> _scripts = new(StringComparer.Ordinal);

    /// <summary>Reads explosion effects out of the game's own weapon scripts.</summary>
    /// <param name="readFile">Opens a path out of the game's content, or answers null.</param>
    /// <exception cref="ArgumentNullException"><paramref name="readFile"/> is null.</exception>
    public ExplosionEffects(Func<string, byte[]?> readFile)
    {
        ArgumentNullException.ThrowIfNull(readFile);

        _readFile = readFile;
    }

    /// <summary>The particle system a blast draws.</summary>
    /// <param name="blast">The explosion.</param>
    /// <param name="struckPlayer">`bIsPlayer` — whether the entity it names is a player.</param>
    /// <param name="inWater">`bIsWater`. See the type's remarks: nothing computes this yet.</param>
    /// <param name="customParticleName">
    /// `GetParticleSystemNameFromIndex`, or null when the `ParticleEffectNames` table is not to hand. Measured on
    /// `demostf-cp_process_f12`: **not one** of its 2,786 explosions sets `m_iCustomParticleIndex`, so this is the
    /// branch that costs nothing to leave unwired and would cost a wrong effect to leave unread.
    /// </param>
    /// <returns>The system's name, never empty.</returns>
    public string NameFor(
        SceneExplosion blast,
        bool struckPlayer,
        bool inWater = false,
        Func<int, string?>? customParticleName = null)
    {
        if (blast.HasCustomParticle)
        {
            return customParticleName?.Invoke(blast.CustomParticleIndex) is { Length: > 0 } named
                ? named
                : DefaultEffect;
        }

        // Stryker disable all : emptying the guard body, or the Logical mutator turning '||' into '&&', leaves
        // 'alias' or 'script' unassigned on the fall-through (CS0165), and Safe Mode drops the method — B410.
        if (TfWeaponAliases.ForExplosion(blast.WeaponId) is not { } alias ||
            Script(alias) is not { } script)
        {
            return DefaultEffect;
        }

        // Stryker restore all

        // Valve's own order, and the `else if` is load-bearing: water is exclusive of the player-or-air branch
        // rather than layered on it, so an underwater blast that hit a player takes the water effect.
        string key = WallKey;

        if (inWater)
        {
            key = WaterKey;
        }
        else if (struckPlayer || blast.InAir)
        {
            key = PlayerKey;
        }

        return script.Value(key) is { Length: > 0 } effect ? effect : DefaultEffect;
    }

    /// <summary>Every particle system a demo's explosions could draw.</summary>
    /// <param name="blasts">Every explosion the demo carried.</param>
    /// <returns>The distinct system names, including <see cref="DefaultEffect"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="blasts"/> is null.</exception>
    /// <remarks>
    /// **What a map load needs to know, and the reason it is a SET rather than one name per blast.** The particle
    /// manifest's 106 files declare 9,050 systems naming 910 distinct materials; uploading all of those to draw a
    /// few is not affordable, so `MapAssets` resolves textures for the systems a demo reaches and reads the
    /// definitions for everything.
    ///
    /// **The branch each blast will take, which is known here**: `bIsPlayer` is resolved when the blast is decoded
    /// (<see cref="SceneExplosion.StruckPlayer"/>), so the set is exactly what will be asked for rather than both
    /// branches of every blast.
    ///
    /// **The default is always included**, because a weapon with no script falls to it and a load that had not
    /// resolved it would draw nothing at all for those.
    /// </remarks>
    public IReadOnlyCollection<string> Used(IEnumerable<SceneExplosion> blasts)
    {
        ArgumentNullException.ThrowIfNull(blasts);

        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase) { DefaultEffect };

        foreach (SceneExplosion blast in blasts)
        {
            names.Add(NameFor(blast, blast.StruckPlayer));
        }

        return names;
    }

    /// <summary>The angles the effect's control point takes.</summary>
    /// <param name="blast">The explosion.</param>
    /// <returns>Pitch, yaw and roll in degrees.</returns>
    /// <remarks>
    /// `angExplosion.Init()` — three zeros — in mid air, and `VectorAngles( vecNormal, angExplosion )` otherwise.
    /// `AngleVectors.Angles` is this project's one copy of that function, and it wraps into <c>[0, 360)</c> as
    /// Valve's does.
    /// </remarks>
    public static (float Pitch, float Yaw, float Roll) AnglesFor(SceneExplosion blast) =>
        blast.InAir
            ? (0f, 0f, 0f)
            : AngleVectors.Angles(blast.Normal.X, blast.Normal.Y, blast.Normal.Z);

    /// <summary>One weapon script, read once whether or not it exists.</summary>
    private WeaponScript? Script(string alias)
    {
        // Stryker disable once : a mutated condition leaves 'already' unassigned in the body, CS0165 — B410.
        if (_scripts.TryGetValue(alias, out WeaponScript? already))
        {
            return already;
        }

        WeaponScript? read = WeaponScript.Read(_readFile, alias);

        _scripts[alias] = read;

        return read;
    }
}
