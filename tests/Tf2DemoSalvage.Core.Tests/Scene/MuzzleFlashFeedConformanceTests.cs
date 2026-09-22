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

    [Test]
    public void ObserveViewmodel_AParityChange_IsAFlashOfItsWeaponOnTheViewmodel()
    {
        // `CBasePlayer::DoMuzzleFlash` bumps each viewmodel's own counter (`baseplayer_shared.cpp:1771`), and
        // `CTFViewModel::ProcessMuzzleFlashEvent` flashes its owning weapon (`tf_viewmodel.cpp:348`).
        MuzzleFlashFeed feed = new();

        feed.ObserveViewmodel(1004, entering: true, parity: 0, weapon: 30, owner: 1, item: 18, team: 3, tick: 100);
        feed.ObserveViewmodel(1004, entering: false, parity: 1, weapon: 30, owner: 1, item: 18, team: 3, tick: 107);

        feed.All.ShouldHaveSingleItem().ShouldBe(new SceneMuzzleFlash(107, 30, 18, 3) { Viewmodel = 1004, Owner = 1 });
    }

    [Test]
    public void Observe_AWeaponWithAnOwner_CarriesTheOwner()
    {
        // `CTFRocketLauncher::CreateMuzzleFlashEffects` asks whether the owner is the local player (`:383`).
        MuzzleFlashFeed feed = new();

        feed.Observe(12, entering: true, isWeapon: true, parity: 1, item: 18, team: 2, tick: 100, owner: 1);
        feed.Observe(12, entering: false, isWeapon: true, parity: 2, item: 18, team: 2, tick: 105, owner: 1);

        feed.All.ShouldHaveSingleItem().Owner.ShouldBe(1);
    }

    [Test]
    public void ObserveViewmodel_NoWeapon_IsNotAFlash()
    {
        // `GetOwningWeapon()` null: `if ( !pWeapon … ) return;`.
        MuzzleFlashFeed feed = new();

        feed.ObserveViewmodel(1004, entering: true, parity: 0, weapon: null, owner: 1, item: null, team: 3, tick: 100);
        feed.ObserveViewmodel(1004, entering: false, parity: 1, weapon: null, owner: 1, item: null, team: 3, tick: 107);

        feed.All.ShouldBeEmpty();
    }
}
