namespace Tf2DemoSalvage.Core.Scene;

/// <summary>One hitscan shot as `CTEFireBullets` carried it (B415).</summary>
/// <param name="Tick">The demo tick its packet arrived on.</param>
/// <param name="Shooter">The shooter's entity index: `m_iPlayer + 1`.</param>
/// <param name="Origin">`m_vecOrigin`, where the bullets start.</param>
/// <param name="Pitch">`m_vecAngles[0]`, in degrees. There is no roll on the wire, and the client zeroes it.</param>
/// <param name="Yaw">`m_vecAngles[1]`.</param>
/// <param name="WeaponId">`m_iWeaponID`, the `TF_WEAPON_*` index.</param>
/// <param name="Mode">`m_iMode`: 0 primary, 1 secondary.</param>
/// <param name="Seed">`m_iSeed`, the first bullet's.</param>
/// <param name="Spread">`m_flSpread`.</param>
/// <param name="Critical">`m_bCritical`.</param>
/// <param name="By">
/// The shooter as the client's entity list had them when the shot arrived, or null when there was no such player —
/// and then `FX_FireBullets` returns before doing anything.
/// </param>
/// <remarks>
/// **No bullet paths**: the client rebuilds every pellet from the seed and traces it itself —
/// `docs/findings/57-the-shot-is-a-seed.md`.
/// </remarks>
public readonly record struct SceneShot(
    int Tick,
    int Shooter,
    (float X, float Y, float Z) Origin,
    float Pitch,
    float Yaw,
    int WeaponId,
    int Mode,
    int Seed,
    float Spread,
    bool Critical,
    ShotShooter? By);

/// <summary>What the client knew about a shooter when their shot arrived.</summary>
/// <param name="Team">`GetTeamNumber()`, which picks a tracer's `_red` or `_blue`.</param>
/// <param name="Weapon">The active weapon's entity index, or null when they held none.</param>
/// <param name="Item">That weapon's `m_iItemDefinitionIndex`, or null when unknown.</param>
public readonly record struct ShotShooter(int Team, int? Weapon, int? Item);
