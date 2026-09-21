using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// A medigun's beams as `CWeaponMedigun::UpdateEffects` makes them (`tf_weapon_medigun.cpp:2352`): one effect per target,
/// re-made when the target or `m_bChargeRelease` changes, stopped when there is none (B415).
/// </summary>
public sealed class HealBeamFeedConformanceTests
{
    [Test]
    public void Observe_ATargetFromTickTenToTwenty_IsOneBeamOverThoseTicks()
    {
        HealBeamFeed feed = new();

        feed.Observe(50, entering: true, target: null, chargeRelease: false, team: 2, tick: 5);
        feed.Observe(50, entering: false, target: 3, chargeRelease: false, team: 2, tick: 10);
        feed.Observe(50, entering: false, target: null, chargeRelease: false, team: 2, tick: 20);

        feed.All.ShouldHaveSingleItem().ShouldBe(new SceneHealBeam(50, 3, false, 2, 10, 20));
    }

    [Test]
    public void Observe_ChargeReleased_IsANewBeam()
    {
        // `ForceHealingTargetUpdate` on `m_bChargeRelease` changing re-runs `UpdateEffects`: the old effect stops and
        // `medicgun_beam_red_invun` starts.
        HealBeamFeed feed = new();

        feed.Observe(50, entering: true, target: 3, chargeRelease: false, team: 2, tick: 10);
        feed.Observe(50, entering: false, target: 3, chargeRelease: true, team: 2, tick: 30);

        feed.All.Count.ShouldBe(2);
        feed.All[0].End.ShouldBe(30);
        feed.All[1].ShouldBe(new SceneHealBeam(50, 3, true, 2, 30, null));
    }

    [Test]
    public void Observe_AnotherTarget_IsANewBeam()
    {
        HealBeamFeed feed = new();

        feed.Observe(50, entering: true, target: 3, chargeRelease: false, team: 2, tick: 10);
        feed.Observe(50, entering: false, target: 4, chargeRelease: false, team: 2, tick: 15);

        feed.All.Count.ShouldBe(2);
        feed.All[1].Target.ShouldBe(4);
    }

    [Test]
    public void Leave_AMedigunLeavingView_EndsItsBeam()
    {
        HealBeamFeed feed = new();

        feed.Observe(50, entering: true, target: 3, chargeRelease: false, team: 2, tick: 10);
        feed.Leave(50, tick: 12);

        feed.All.ShouldHaveSingleItem().End.ShouldBe(12);
    }
}
