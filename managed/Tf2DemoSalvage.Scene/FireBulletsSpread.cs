using System;

using Tf2DemoSalvage.Core.Primitives;

namespace Tf2DemoSalvage.Scene;

/// <summary>Where a hitscan shot's bullets went — the direction half of <c>FX_FireBullets</c> (B415).</summary>
/// <remarks>
/// **`DT_TEFireBullets` carries no bullet paths**, so every pellet's direction is rebuilt from `m_iSeed`. The whole
/// account — the wire fields, the call path, and why Valve's own RNG had to be read out of `vstdlib.dll` rather
/// than taken from Numerical Recipes — is `docs/findings/57-the-shot-is-a-seed.md`.
///
/// The loop, `tf_fx_shared.cpp:307-386`:
///
/// <code>
/// AngleVectors( vecAngles, &amp;vecShootForward, &amp;vecShootRight, &amp;vecShootUp );   // once, before the loop
/// for ( int iBullet = 0; iBullet &lt; nBulletsPerShot; ++iBullet )
/// {
///     RandomSeed( iSeed );
///     float x = 0.f, y = 0.f;
///     ...
///     float flVariance = 0.5f;
///     if ( flVariance != 0.f )
///     {
///         x = RandomFloat( -flVariance, flVariance ) + RandomFloat( -flVariance, flVariance );
///         y = RandomFloat( -flVariance, flVariance ) + RandomFloat( -flVariance, flVariance );
///     }
///     fireInfo.m_vecDirShooting = vecShootForward + ( x * flSpread * vecShootRight ) + ( y * flSpread * vecShootUp );
///     fireInfo.m_vecDirShooting.NormalizeInPlace();
///     ...
///     ++iSeed;
/// }
/// </code>
///
/// **Two of Valve's branches are NOT reproduced, and both need data this layer does not have yet:**
///
/// - **Fixed weapon spread** (`tf_fx_shared.cpp:314-340`) replaces the random draws with a fixed pellet table when
///   `( nDamageType &amp; DMG_BUCKSHOT ) &amp;&amp; nBulletsPerShot &gt; 1 &amp;&amp; IsFixedWeaponSpreadEnabled( pWpn )`. Every one of
///   those three terms comes from the weapon script and a server convar, none of which is on the wire.
/// - **The first shot's accuracy bonus** (`tf_fx_shared.cpp:344-367`) sets `flVariance` from
///   `mult_spread_scale_first_shot` when bullet zero follows a long enough pause, which needs `m_flLastFireTime`.
///   With it, `flVariance` can be zero — and then the guard above means **no draws are taken at all**, so the
///   stream is not advanced. That is why the guard is quoted here although a constant 0.5 can never fail it.
///
/// Both are recorded rather than approximated: a plausible substitute for either would put a shotgun's pellets
/// somewhere plausible and wrong, which is exactly the failure the oracle-checked RNG exists to prevent.
/// </remarks>
public static class FireBulletsSpread
{
    /// <summary>`flVariance`, Valve's default, before any first-shot accuracy bonus.</summary>
    private const float Variance = 0.5f;

    /// <summary>Rebuilds one shot's bullet directions from its seed.</summary>
    /// <param name="pitch">`m_vecAngles[0]`, in degrees.</param>
    /// <param name="yaw">`m_vecAngles[1]`.</param>
    /// <param name="spread">`m_flSpread`, the cone's half-width in tangent units.</param>
    /// <param name="seed">`m_iSeed`. The FIRST bullet's seed; each later one adds its own index.</param>
    /// <param name="bullets">`m_nBulletsPerShot` from the weapon script — one for a rifle, several for a shotgun.</param>
    /// <returns>One unit direction per bullet, in firing order.</returns>
    /// <remarks>
    /// **The roll is zero because the wire has no roll**: `DT_TEFireBullets` sends two angles, and
    /// `C_TEFireBullets::PostDataUpdate` sets `m_vecAngles.z = 0` before calling in.
    ///
    /// **The seed run wraps as the engine's does.** `++iSeed` on an `int` past `int.MaxValue` is
    /// implementation-defined in C++ and wraps in practice; this addition is unchecked for the same reason, so a
    /// shot seeded near the top of the range rebuilds the same way rather than throwing.
    /// </remarks>
    public static (float X, float Y, float Z)[] Directions(float pitch, float yaw, float spread, int seed, int bullets)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bullets);

        (float X, float Y, float Z) forward = AngleVectors.Forward(pitch, yaw);
        (float X, float Y, float Z) right = AngleVectors.Right(pitch, yaw, 0f);
        (float X, float Y, float Z) up = AngleVectors.Up(pitch, yaw, 0f);

        (float X, float Y, float Z)[] directions = new (float, float, float)[bullets];

        // One stream reused across the bullets rather than one per bullet: SetSeed is the engine's whole
        // RandomSeed, so reseeding is what separates them and a fresh instance would only cost an allocation.
        UniformRandomStream stream = new();

        for (int bullet = 0; bullet < bullets; bullet++)
        {
            stream.SetSeed(seed + bullet);

            float x = stream.RandomFloat(-Variance, Variance) + stream.RandomFloat(-Variance, Variance);
            float y = stream.RandomFloat(-Variance, Variance) + stream.RandomFloat(-Variance, Variance);

            directions[bullet] = VectorMath.Normalized(
                forward.X + (x * spread * right.X) + (y * spread * up.X),
                forward.Y + (x * spread * right.Y) + (y * spread * up.Y),
                forward.Z + (x * spread * right.Z) + (y * spread * up.Z));
        }

        return directions;
    }
}
