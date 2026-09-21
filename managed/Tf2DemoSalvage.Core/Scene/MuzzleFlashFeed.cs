using System.Collections.Generic;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>One weapon's muzzle flash, at the tick the client would notice it (B415).</summary>
/// <param name="Tick">The tick its parity changed.</param>
/// <param name="Weapon">The weapon's entity index, whose `muzzle` attachment the effects follow.</param>
/// <param name="Item">Its `m_iItemDefinitionIndex`, which names its script and any item muzzle override; null if unsent.</param>
/// <param name="Team">Its team, which `GetMuzzleFlash( team )` reads.</param>
public readonly record struct SceneMuzzleFlash(int Tick, int Weapon, int? Item, int Team);

/// <summary>Every weapon muzzle flash a demo carries — `m_nMuzzleFlashParity` as `C_BaseAnimating` reads it (B415).</summary>
/// <remarks>
/// <code>
/// // server: CTFWeaponBaseGun::DoFireEffects → CBaseCombatCharacter::DoMuzzleFlash → pWeapon->DoMuzzleFlash()
/// //         m_nMuzzleFlashParity = ( m_nMuzzleFlashParity + 1 ) &amp; 3                   // EF_MUZZLEFLASH_BITS 2
/// // client: C_BaseAnimating::DoAnimationEvents:  if ( old != new ) { old = new; ProcessMuzzleFlashEvent() }
/// //         NotifyShouldTransmit( START ):          old = new                           // entering view never flashes
/// </code>
/// **Only whether it flashed is decided here.** Whether it is DRAWN — the weapon visible, the first-person case taken
/// by the viewmodel — is the renderer's question, as `DoAnimationEvents` asks it per frame.
/// </remarks>
public sealed class MuzzleFlashFeed
{
    /// <summary>The property the counter travels in.</summary>
    public const string ParityKey = "DT_BaseAnimating.m_nMuzzleFlashParity";

    private readonly Dictionary<int, int> _old = [];
    private readonly List<SceneMuzzleFlash> _flashes = [];

    /// <summary>Every flash, in tick order.</summary>
    public IReadOnlyList<SceneMuzzleFlash> All => _flashes;

    /// <summary>Takes one applied entity update.</summary>
    /// <param name="entity">Its index.</param>
    /// <param name="entering">Whether it entered view on this update, which resets the old parity.</param>
    /// <param name="isWeapon">Whether its class is a combat weapon.</param>
    /// <param name="parity">Its parity after the update, or null when it has none.</param>
    /// <param name="item">Its item definition.</param>
    /// <param name="team">Its team.</param>
    /// <param name="tick">The tick.</param>
    public void Observe(int entity, bool entering, bool isWeapon, int? parity, int? item, int team, int tick)
    {
        if (!isWeapon || parity is not { } now)
        {
            return;
        }

        if (!entering && _old.TryGetValue(entity, out int old) && old != now)
        {
            _flashes.Add(new SceneMuzzleFlash(tick, entity, item, team));
        }

        _old[entity] = now;
    }
}
