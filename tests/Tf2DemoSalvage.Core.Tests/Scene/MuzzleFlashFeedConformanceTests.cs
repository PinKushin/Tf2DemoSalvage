using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// A weapon's muzzle flash as the client notices it — `m_nMuzzleFlashParity` against `m_nOldMuzzleFlashParity`
/// (`c_baseanimating.h:735`) (B415).
/// </summary>
public sealed class MuzzleFlashFeedConformanceTests
{
    [Test]
    public void Observe_AParityChangeOnAWeaponInView_IsAFlash()
    {
        // `ShouldMuzzleFlash` is `old != new`; the server's `DoMuzzleFlash` bumps the active weapon's two-bit counter.
        MuzzleFlashFeed feed = new();

        feed.Observe(12, entering: true, isWeapon: true, parity: 1, item: 13, team: 2, tick: 100);
        feed.Observe(12, entering: false, isWeapon: true, parity: 2, item: 13, team: 2, tick: 105);

        feed.All.ShouldHaveSingleItem().ShouldBe(new SceneMuzzleFlash(105, 12, 13, 2));
    }

    [Test]
    public void Observe_AWeaponEnteringView_IsNotAFlash()
    {
        // `NotifyShouldTransmit( SHOULDTRANSMIT_START )` calls `DisableMuzzleFlash`: "If he's been firing a bunch, then
        // he comes back into the PVS, his muzzle flash will show up even if he isn't firing now."
        MuzzleFlashFeed feed = new();

        feed.Observe(12, entering: true, isWeapon: true, parity: 1, item: 13, team: 2, tick: 100);
        feed.Observe(12, entering: true, isWeapon: true, parity: 3, item: 13, team: 2, tick: 200);

        feed.All.ShouldBeEmpty();
    }

    [Test]
    public void Observe_AnUnchangedParity_IsNotAFlash()
    {
        MuzzleFlashFeed feed = new();

        feed.Observe(12, entering: true, isWeapon: true, parity: 1, item: 13, team: 2, tick: 100);
        feed.Observe(12, entering: false, isWeapon: true, parity: 1, item: 13, team: 2, tick: 101);

        feed.All.ShouldBeEmpty();
    }

    [Test]
    public void Observe_NotAWeapon_IsNeverAFlash()
    {
        // The player's own counter is forwarded to its weapon by `CBaseCombatCharacter::DoMuzzleFlash`; anything else
        // that is not a weapon has no muzzle to flash.
        MuzzleFlashFeed feed = new();

        feed.Observe(3, entering: true, isWeapon: false, parity: 0, item: null, team: 2, tick: 100);
        feed.Observe(3, entering: false, isWeapon: false, parity: 1, item: null, team: 2, tick: 101);

        feed.All.ShouldBeEmpty();
    }
}
