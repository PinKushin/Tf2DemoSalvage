using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// `CTFPlayerAnimState::HandleDucking` and `HandleMoving` (`tf_playeranimstate.cpp:1297-1392`), whole (B437).
/// </summary>
/// <remarks>
/// <code>
/// bInDuck = FL_DUCKING, dropped when the model lacks TranslateActivity( ACT_MP_CROUCHWALK ) and !IsLoser()
/// if ( bInDuck )
///     if ( speed &lt; MOVING_MINIMUM_SPEED || IsLoser() )
///         ACT_MP_CROUCH_IDLE, or ACT_MP_CROUCH_DEPLOYED_IDLE if IsAiming() || the deployed hold
///     else
///         GetAirDash() &gt; 0 ? ACT_MP_DOUBLEJUMP_CROUCH : ACT_MP_CROUCHWALK,
///         then ACT_MP_CROUCH_DEPLOYED if IsAiming() and not a minigun
/// HandleMoving: IsLoser() → the base run; IsAiming() → ACT_MP_DEPLOYED / _IDLE; the hold → ACT_MP_DEPLOYED_IDLE
/// </code>
/// </remarks>
public sealed class TfHandleDuckingConformanceTests
{
    private const int Crouched = PlayerActivityState.Ducking | PlayerActivityState.OnGround;
    private const int Standing = PlayerActivityState.OnGround;
    private const float Moving = 200f;
    private const float Still = 0f;

    private static PlayerActivity For(int flags, float speed, TfPosture posture) =>
        PlayerActivityState.For(flags, speed, waistDeep: false, alive: true, jumping: null, posture);

    [Test]
    public void For_ADuckingPlayer_CrouchIdlesOrWalksByTheEnginesThreshold()
    {
        For(Crouched, Still, default).ShouldBe(PlayerActivity.CrouchIdle);
        For(Crouched, Moving, default).ShouldBe(PlayerActivity.CrouchWalk);
        For(Crouched, PlayerActivityState.MovingMinimumSpeed, default)
            .ShouldBe(PlayerActivity.CrouchWalk, "`speed < MOVING_MINIMUM_SPEED` idles: exactly the threshold walks");
    }

    [Test]
    public void For_ADuckingPlayerAiming_CrouchesDeployedUnlessAMinigunMoves()
    {
        For(Crouched, Still, new TfPosture(IsAiming: true)).ShouldBe(PlayerActivity.CrouchDeployedIdle);
        For(Crouched, Moving, new TfPosture(IsAiming: true)).ShouldBe(PlayerActivity.CrouchDeployed);
        For(Crouched, Moving, new TfPosture(IsAiming: true, AimsMinigun: true))
            .ShouldBe(PlayerActivity.CrouchWalk, "the heavy does not deployed-crouch-walk");
        For(Crouched, Still, new TfPosture(IsAiming: true, AimsMinigun: true))
            .ShouldBe(PlayerActivity.CrouchDeployedIdle, "but he does crouch deployed while still");
    }

    [Test]
    public void For_TheDeployedHold_CrouchIdlesDeployedAndStandsDeployed()
    {
        For(Crouched, Still, new TfPosture(HoldsDeployedPose: true)).ShouldBe(PlayerActivity.CrouchDeployedIdle);
        For(Crouched, Moving, new TfPosture(HoldsDeployedPose: true))
            .ShouldBe(PlayerActivity.CrouchWalk, "the hold reaches only the crouch IDLE");
        For(Standing, Still, new TfPosture(HoldsDeployedPose: true)).ShouldBe(PlayerActivity.DeployedIdle);
    }

    [Test]
    public void For_AnAirDashingDucker_PlaysTheDoubleJumpCrouch()
    {
        For(Crouched, Moving, new TfPosture(AirDashing: true)).ShouldBe(PlayerActivity.DoubleJumpCrouch);
        For(Crouched, Still, new TfPosture(AirDashing: true)).ShouldBe(PlayerActivity.CrouchIdle, "only while moving");
    }

    [Test]
    public void For_AModelWithoutTheCrouchWalk_DropsTheDuckUnlessALoser()
    {
        For(Crouched, Moving, new TfPosture(LacksCrouchWalk: true)).ShouldBe(PlayerActivity.Run);
        For(Crouched, Moving, new TfPosture(LacksCrouchWalk: true, IsLoser: true))
            .ShouldBe(PlayerActivity.CrouchIdle, "a ducking loser crouch-idles, moving or not");
    }

    [Test]
    public void For_AStandingPlayerAiming_IsDeployedAndALoserIsNot()
    {
        For(Standing, Moving, new TfPosture(IsAiming: true)).ShouldBe(PlayerActivity.Deployed);
        For(Standing, Still, new TfPosture(IsAiming: true)).ShouldBe(PlayerActivity.DeployedIdle);
        For(Standing, Moving, new TfPosture(IsAiming: true, IsLoser: true)).ShouldBe(PlayerActivity.Run);
        For(Standing, Still, default).ShouldBe(PlayerActivity.StandIdle, "the control");
    }

    [Test]
    public void IdealName_TheNewActivities_AreTheEnginesNames()
    {
        PlayerActivityState.IdealName(PlayerActivity.CrouchDeployedIdle).ShouldBe("ACT_MP_CROUCH_DEPLOYED_IDLE");
        PlayerActivityState.IdealName(PlayerActivity.CrouchDeployed).ShouldBe("ACT_MP_CROUCH_DEPLOYED");
        PlayerActivityState.IdealName(PlayerActivity.DoubleJumpCrouch).ShouldBe("ACT_MP_DOUBLEJUMP_CROUCH");
        PlayerActivityState.IdealName(PlayerActivity.Deployed).ShouldBe("ACT_MP_DEPLOYED");
        PlayerActivityState.IdealName(PlayerActivity.DeployedIdle).ShouldBe("ACT_MP_DEPLOYED_IDLE");
    }
}
