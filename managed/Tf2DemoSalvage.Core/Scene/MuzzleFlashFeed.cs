using System.Collections.Generic;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>One weapon's muzzle flash, at the tick the client would notice it (B415).</summary>
/// <param name="Tick">The tick its parity changed.</param>
/// <param name="Weapon">The weapon's entity index, whose `muzzle` attachment the effects follow.</param>
/// <param name="Item">Its `m_iItemDefinitionIndex`, which names its script and any item muzzle override; null if unsent.</param>
/// <param name="Team">Its team, which `GetMuzzleFlash( team )` reads.</param>
public readonly record struct SceneMuzzleFlash(int Tick, int Weapon, int? Item, int Team)
{
    /// <summary>The viewmodel whose own counter changed, when the flash is first person's; null for the world weapon's.</summary>
    public int? Viewmodel { get; init; }

    /// <summary>The weapon's or viewmodel's owner — whose first-person view alone shows a viewmodel flash.</summary>
    public int? Owner { get; init; }
}

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
    /// <param name="owner">Its owner's index, or null when unsent.</param>
    public void Observe(int entity, bool entering, bool isWeapon, int? parity, int? item, int team, int tick, int? owner = null)
    {
        if (!isWeapon || parity is not { } now)
        {
            return;
        }

        if (!entering && _old.TryGetValue(entity, out int old) && old != now)
        {
            _flashes.Add(new SceneMuzzleFlash(tick, entity, item, team) { Owner = owner });
        }

        _old[entity] = now;
    }

    /// <summary>Takes one applied viewmodel update — the first-person flash (B415).</summary>
    /// <param name="viewmodel">The viewmodel's index.</param>
    /// <param name="entering">Whether it entered view on this update, which resets the old parity.</param>
    /// <param name="parity">Its `DT_BaseViewModel.m_nMuzzleFlashParity`, or null when unsent.</param>
    /// <param name="weapon">Its `m_hWeapon`'s index, the weapon whose effects it starts.</param>
    /// <param name="owner">Its `m_hOwner`'s index.</param>
    /// <param name="item">The weapon's item definition.</param>
    /// <param name="team">The weapon's team.</param>
    /// <param name="tick">The tick.</param>
    /// <remarks>
    /// <code>
    /// // CBasePlayer::DoMuzzleFlash (baseplayer_shared.cpp:1771): every viewmodel's DoMuzzleFlash, then the weapon's
    /// // CTFViewModel::ProcessMuzzleFlashEvent (tf_viewmodel.cpp:348): if ( !pWeapon || ShouldDrawLocalPlayer() ) return;
    /// //                                                              pWeapon->ProcessMuzzleFlashEvent()
    /// </code>
    /// </remarks>
    public void ObserveViewmodel(int viewmodel, bool entering, int? parity, int? weapon, int? owner, int? item, int team, int tick)
    {
        if (parity is not { } now)
        {
            return;
        }

        if (!entering && weapon is { } flashing && _old.TryGetValue(viewmodel, out int old) && old != now)
        {
            _flashes.Add(new SceneMuzzleFlash(tick, flashing, item, team) { Viewmodel = viewmodel, Owner = owner });
        }

        _old[viewmodel] = now;
    }
}
