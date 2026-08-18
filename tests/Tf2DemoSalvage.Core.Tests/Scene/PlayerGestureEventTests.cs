using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// The event→gesture mapping, against <c>CTFPlayerAnimState::DoAnimationEvent</c> over
/// <c>CMultiPlayerAnimState::DoAnimationEvent</c>.
/// </summary>
/// <remarks>
/// Every expectation is read straight from the SDK switch and stated by hand — the activity name is
/// the literal <c>ACT_*</c> the case passes to <c>RestartGesture</c>, the slot is its first
/// argument, and the auto-kill is its third (defaulting to <c>true</c>). The cases that call
/// <c>RestartMainSequence</c> or <c>ClearAnimationState</c> instead of <c>RestartGesture</c> start no
/// gesture and are asserted null — that is the load-bearing half, because getting it wrong would lay
/// a fire gesture over a jump the main sequence already handles.
/// </remarks>
public sealed class PlayerGestureEventTests
{
    private static GestureContext Plain => new();

    [Test]
    public void PrimaryFireStandsByDefault()
    {
        // else-branch of the TF override: bInDuck ? CROUCH : STAND, no swim variant for a plain gun.
        GestureTrigger trigger = Map(PlayerAnimEvent.AttackPrimary, Plain);

        trigger.Slot.ShouldBe(GestureSlot.AttackAndReload);
        trigger.ActivityName.ShouldBe("ACT_MP_ATTACK_STAND_PRIMARYFIRE");
        trigger.ActivityNumber.ShouldBeNull();
        trigger.AutoKill.ShouldBeTrue();
    }

    [Test]
    public void PrimaryFireCrouchesWhenDucking()
    {
        Map(PlayerAnimEvent.AttackPrimary, new GestureContext(InDuck: true))
            .ActivityName.ShouldBe("ACT_MP_ATTACK_CROUCH_PRIMARYFIRE");
    }

    [Test]
    public void MinigunPrimaryFireSwimsWhenInWater()
    {
        // The minigun branch is the only primary-fire path with a swim variant; swim wins over duck.
        Map(PlayerAnimEvent.AttackPrimary, new GestureContext(IsMinigun: true, InSwim: true))
            .ActivityName.ShouldBe("ACT_MP_ATTACK_SWIM_PRIMARYFIRE");
    }

    [Test]
    public void ZoomedSniperFiresADeployedGesture()
    {
        Map(PlayerAnimEvent.AttackPrimary, new GestureContext(IsSniperZoomed: true))
            .ActivityName.ShouldBe("ACT_MP_ATTACK_STAND_PRIMARYFIRE_DEPLOYED");
    }

    [Test]
    public void SecondaryFireSwimsAtWaistDeep()
    {
        // ATTACK_SECONDARY tests GetWaterLevel() >= WL_Waist directly, so InSwim alone drives it.
        Map(PlayerAnimEvent.AttackSecondary, new GestureContext(InSwim: true))
            .ActivityName.ShouldBe("ACT_MP_ATTACK_SWIM_SECONDARYFIRE");
    }

    [Test]
    public void ReloadStandsByDefaultAndCrouchesAndSwims()
    {
        Map(PlayerAnimEvent.Reload, Plain).ActivityName.ShouldBe("ACT_MP_RELOAD_STAND");
        Map(PlayerAnimEvent.Reload, new GestureContext(InDuck: true)).ActivityName.ShouldBe("ACT_MP_RELOAD_CROUCH");
        Map(PlayerAnimEvent.Reload, new GestureContext(InSwim: true)).ActivityName.ShouldBe("ACT_MP_RELOAD_SWIM");
    }

    [Test]
    public void ReloadTakesTheAirwalkVariantWhenAirWalking()
    {
        // The TF override's own case: airwalk beats the base stand/crouch/swim choice entirely.
        Map(PlayerAnimEvent.Reload, new GestureContext(InAirWalk: true))
            .ActivityName.ShouldBe("ACT_MP_RELOAD_AIRWALK");
    }

    [Test]
    public void DuckAndSwimResolveOppositelyForReloadAndForAttacks()
    {
        // **The precedence discriminator.** A ducking player who is also waist-deep exposes the one
        // difference between the two orderings: base reload picks duck FIRST
        // (`if (FL_DUCKING) CROUCH; else if (m_bInSwim) SWIM`), while secondary fire lets swim
        // OVERRIDE duck (`baseActivity = bInDuck ? CROUCH : STAND; if (water) baseActivity = SWIM`).
        // Setting only one condition at a time would let a swapped helper pass.
        GestureContext duckAndSwim = new(InDuck: true, InSwim: true);

        Map(PlayerAnimEvent.Reload, duckAndSwim).ActivityName.ShouldBe("ACT_MP_RELOAD_CROUCH");
        Map(PlayerAnimEvent.AttackSecondary, duckAndSwim).ActivityName.ShouldBe("ACT_MP_ATTACK_SWIM_SECONDARYFIRE");
    }

    [Test]
    public void FlinchGoesToItsOwnSlot()
    {
        GestureTrigger trigger = Map(PlayerAnimEvent.FlinchHead, Plain);

        trigger.Slot.ShouldBe(GestureSlot.Flinch);
        trigger.ActivityName.ShouldBe("ACT_MP_GESTURE_FLINCH_HEAD");
    }

    [Test]
    public void DoubleJumpUsesTheJumpSlotAndTheLoserVariant()
    {
        Map(PlayerAnimEvent.DoubleJump, Plain).Slot.ShouldBe(GestureSlot.Jump);
        Map(PlayerAnimEvent.DoubleJump, Plain).ActivityName.ShouldBe("ACT_MP_DOUBLEJUMP");
        Map(PlayerAnimEvent.DoubleJump, new GestureContext(IsLoser: true))
            .ActivityName.ShouldBe("ACT_MP_DOUBLEJUMP_LOSERSTATE");
    }

    [Test]
    public void PreFireHoldsForANormalWeaponAndAutoKillsForAMinigun()
    {
        // bAutoKillPreFire = bIsMinigun. A sniper's aim-start prefire holds until the shot; the
        // minigun's windup auto-kills so the fire loop can take over cleanly.
        Map(PlayerAnimEvent.AttackPre, Plain).AutoKill.ShouldBeFalse();
        Map(PlayerAnimEvent.AttackPre, Plain).ActivityName.ShouldBe("ACT_MP_ATTACK_STAND_PREFIRE");
        Map(PlayerAnimEvent.AttackPre, new GestureContext(IsMinigun: true)).AutoKill.ShouldBeTrue();
    }

    [Test]
    public void PostFireAutoKills()
    {
        GestureTrigger trigger = Map(PlayerAnimEvent.AttackPost, Plain);

        trigger.ActivityName.ShouldBe("ACT_MP_ATTACK_STAND_POSTFIRE");
        trigger.AutoKill.ShouldBeTrue();
    }

    [Test]
    public void StunBeginHoldsAndStunEndAutoKills()
    {
        // BEGIN and MIDDLE pass bAutoKill=false explicitly; END takes the default true.
        Map(PlayerAnimEvent.StunBegin, Plain).Slot.ShouldBe(GestureSlot.Custom);
        Map(PlayerAnimEvent.StunBegin, Plain).ActivityName.ShouldBe("ACT_MP_STUN_BEGIN");
        Map(PlayerAnimEvent.StunBegin, Plain).AutoKill.ShouldBeFalse();
        Map(PlayerAnimEvent.StunEnd, Plain).AutoKill.ShouldBeTrue();
    }

    [Test]
    public void VoiceCommandGestureCarriesItsActivityInData()
    {
        // RestartGesture( GESTURE_SLOT_ATTACK_AND_RELOAD, (Activity)nData ) — the activity is dynamic.
        GestureTrigger trigger = Map(
            PlayerAnimEvent.VoiceCommandGesture, new GestureContext(NData: 1502));

        trigger.Slot.ShouldBe(GestureSlot.AttackAndReload);
        trigger.ActivityName.ShouldBeNull();
        trigger.ActivityNumber.ShouldBe(1502);
    }

    [Test]
    public void CustomGestureCarriesItsActivityInData()
    {
        GestureTrigger trigger = Map(
            PlayerAnimEvent.CustomGesture, new GestureContext(NData: 1088));

        trigger.Slot.ShouldBe(GestureSlot.Custom);
        trigger.ActivityNumber.ShouldBe(1088);
    }

    [Test]
    public void GrenadeThrowUsesTheGrenadeSlot()
    {
        Map(PlayerAnimEvent.AttackGrenade, Plain).Slot.ShouldBe(GestureSlot.Grenade);
        Map(PlayerAnimEvent.AttackGrenade, Plain).ActivityName.ShouldBe("ACT_MP_ATTACK_STAND_GRENADE");
    }

    [Test]
    public void SuperPrimaryFireHasItsOwnActivity()
    {
        Map(PlayerAnimEvent.AttackPrimarySuper, Plain).ActivityName.ShouldBe("ACT_MP_ATTACK_STAND_PRIMARY_SUPER");
        Map(PlayerAnimEvent.AttackPrimarySuper, new GestureContext(InSwim: true))
            .ActivityName.ShouldBe("ACT_MP_ATTACK_SWIM_PRIMARY_SUPER");
    }

    [Test]
    public void EventsThatDriveTheMainSequenceStartNoGesture()
    {
        // These call RestartMainSequence / ClearAnimationState / a pose reset, never RestartGesture.
        // The main sequence is this project's PlayerActivityState, which already handles them.
        PlayerGestureEvent.Map(PlayerAnimEvent.Jump, Plain).ShouldBeNull();
        PlayerGestureEvent.Map(PlayerAnimEvent.Swim, Plain).ShouldBeNull();
        PlayerGestureEvent.Map(PlayerAnimEvent.Die, Plain).ShouldBeNull();
        PlayerGestureEvent.Map(PlayerAnimEvent.Spawn, Plain).ShouldBeNull();
        PlayerGestureEvent.Map(PlayerAnimEvent.SnapYaw, Plain).ShouldBeNull();
        PlayerGestureEvent.Map(PlayerAnimEvent.Custom, Plain).ShouldBeNull();
        PlayerGestureEvent.Map(PlayerAnimEvent.CustomSequence, Plain).ShouldBeNull();
        PlayerGestureEvent.Map(PlayerAnimEvent.Cancel, Plain).ShouldBeNull();
    }

    [Test]
    public void EventsThatAreDeadInTheSdkStartNoGesture()
    {
        // Grenade draw/throw have no handler; CustomGestureSequence and DoubleJumpCrouch are
        // commented out. A null here is the faithful result, not a missing implementation —
        // z1800 carries 19 DoubleJumpCrouch events and the engine draws nothing for them.
        PlayerGestureEvent.Map(PlayerAnimEvent.Grenade1Draw, Plain).ShouldBeNull();
        PlayerGestureEvent.Map(PlayerAnimEvent.Grenade2Throw, Plain).ShouldBeNull();
        PlayerGestureEvent.Map(PlayerAnimEvent.CustomGestureSequence, Plain).ShouldBeNull();
        PlayerGestureEvent.Map(PlayerAnimEvent.DoubleJumpCrouch, Plain).ShouldBeNull();
    }

    /// <summary>Unwraps the trigger, failing the test if the event started no gesture.</summary>
    private static GestureTrigger Map(PlayerAnimEvent anEvent, GestureContext context) =>
        PlayerGestureEvent.Map(anEvent, context)
            ?? throw new global::System.InvalidOperationException($"{anEvent} started no gesture");
}
