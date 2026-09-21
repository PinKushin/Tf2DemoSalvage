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
    public void EffectName_ByTeamChargeAndMarker_IsUpdateEffectsBranch()
    {
        // The charge wins over the marker; the marker only applies uncharged; team 3 takes the else branch.
        HealBeamFeed.EffectName(2, chargeRelease: false, targeted: false).ShouldBe("medicgun_beam_red");
        HealBeamFeed.EffectName(2, chargeRelease: true, targeted: true).ShouldBe("medicgun_beam_red_invun");
        HealBeamFeed.EffectName(3, chargeRelease: false, targeted: true).ShouldBe("medicgun_beam_blue_targeted");
    }

    [Test]
    public void Observe_AnItemAndAMedic_AreCarriedOnTheBeam()
    {
        // The item picks the `custom_particlesystem` beside the beam; the medic is compared with the local player.
        HealBeamFeed feed = new();

        feed.Observe(50, entering: true, target: 3, chargeRelease: false, team: 2, tick: 10, item: 411, owner: 7);

        SceneHealBeam beam = feed.All.ShouldHaveSingleItem();

        beam.Item.ShouldBe(411);
        beam.Owner.ShouldBe(7);
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
