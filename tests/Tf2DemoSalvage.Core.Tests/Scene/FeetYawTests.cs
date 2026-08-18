using System;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// The feet-yaw state machine, against <c>ComputePoseParam_AimYaw</c> and
/// <c>ConvergeYawAngles</c>.
/// </summary>
/// <remarks>
/// **A player's body is drawn at their FEET yaw, not their eye yaw**, and the torso twists to make
/// up the difference. That is the whole reason this exists: the two are equal while moving, so
/// using the eye yaw has looked correct for everything except a player turning on the spot, where
/// the feet should stay planted and the waist should twist.
///
/// Every number below is from the source rather than chosen: 45 degrees of twist before the feet
/// step, 720 degrees a second of turn, a 60-degree fade, and a movement threshold of one unit a
/// second on the THREE-dimensional velocity.
/// </remarks>
public sealed class FeetYawTests
{
    /// <summary>A tick at 66 per second, which is what a competitive server runs.</summary>
    private const float Tick = 1f / 66f;

    [Test]
    public void TheFeetStartUnderTheEyes()
    {
        // `if ( m_flLastAimTurnTime <= 0.0f ) { m_flGoalFeetYaw = m_flEyeYaw; m_flCurrentFeetYaw =
        // m_flEyeYaw; }` — without this a player who has never moved is drawn facing due east while
        // looking somewhere else entirely.
        FeetYaw feet = default;

        feet.Advance(eyeYaw: 120f, speed: 0f, deltaSeconds: Tick);

        feet.Current.ShouldBe(120f);
        feet.AimYaw(120f).ShouldBe(0f, "the torso is not twisted at rest");
    }

    [Test]
    public void MovingPutsTheFeetUnderTheEyes()
    {
        // "The feet match the eye direction when moving - the move yaw takes care of the rest."
        FeetYaw feet = default;

        feet.Advance(0f, speed: 0f, Tick);

        // **The approach is asymptotic, not a straight 720 degrees a second**, and assuming
        // otherwise is what made the first version of this test fail at 88.11 degrees after twenty
        // ticks — correct behaviour that I had predicted wrongly.
        //
        // FADE_TURN_DEGREES scales the rate by `delta / 60` once the remaining turn is under sixty,
        // so each tick covers about a fifth of what is left and the gap decays geometrically. It
        // becomes linear at the one per cent floor and only then closes, which takes about forty
        // ticks from ninety degrees. Sixty is comfortably past that.
        for (int step = 0; step < 60; step++)
        {
            feet.Advance(90f, speed: 300f, Tick);
        }

        feet.Current.ShouldBe(90f, 0.5f);
        feet.AimYaw(90f).ShouldBe(0f, 0.5f, "a running player is not twisted at the waist");
    }

    [Test]
    public void StandingStillTheFeetStayPutAndTheTorsoTwists()
    {
        // **The case this whole file exists for.** Standing still and turning the view 30 degrees
        // leaves the feet where they were, because 30 is inside the 45-degree allowance — so the
        // body is still drawn at the old yaw and body_yaw carries the difference.
        FeetYaw feet = default;

        feet.Advance(0f, speed: 0f, Tick);

        for (int step = 0; step < 20; step++)
        {
            feet.Advance(30f, speed: 0f, Tick);
        }

        feet.Current.ShouldBe(0f, 0.5f, "the feet do not follow a small turn");

        // Negated, as SetPoseParameter( m_iAimYaw, -flAimYaw ) negates it.
        feet.AimYaw(30f).ShouldBe(-30f, 0.5f);
    }

    [Test]
    public void PastTheAllowanceTheFeetStepRound()
    {
        // Beyond 45 degrees the goal jumps by exactly 45 toward the eyes — `m_flGoalFeetYaw +=
        // ( 45.0f * flSide )` — rather than tracking them. Valve marks the branch as unfinished in
        // place, with the comment "Do something better here!".
        FeetYaw feet = default;

        feet.Advance(0f, speed: 0f, Tick);

        for (int step = 0; step < 40; step++)
        {
            feet.Advance(90f, speed: 0f, Tick);
        }

        // The feet have moved, and they have not simply snapped to the eyes.
        feet.Current.ShouldBeGreaterThan(20f, "the feet step round once the twist exceeds 45");

        MathF.Abs(feet.AimYaw(90f))
            .ShouldBeLessThanOrEqualTo(46f, "and the remaining twist is within the allowance");
    }

    [Test]
    public void TheTurnRateIsBoundedPerStep()
    {
        // 720 degrees a second at 66 ticks is under 11 degrees a tick, and the fade scales it down
        // further below a 60-degree turn. A single step must therefore never cover a 90-degree turn
        // — an implementation missing the rate would arrive in one step and pass every test above.
        FeetYaw feet = default;

        feet.Advance(0f, speed: 0f, Tick);
        feet.Advance(90f, speed: 300f, Tick);

        feet.Current.ShouldBeLessThan(
            15f,
            "one tick at 720 degrees a second cannot cover ninety degrees");
    }

    [Test]
    public void TurningThroughTheWrapUsesTheRawMagnitudeAndTheNormalisedSign()
    {
        // **Valve takes the magnitude BEFORE normalising and the sign after**, which is not a
        // tidy-up waiting to happen — it changes behaviour at the wrap:
        //
        //     float flDeltaYaw = flGoalYaw - flCurrentYaw;
        //     float flDeltaYawAbs = fabs( flDeltaYaw );
        //     flDeltaYaw = AngleNormalize( flDeltaYaw );
        //
        // Going from 170 to −170 is twenty degrees the short way, but the raw difference is 340: the
        // fade saturates at full rate rather than easing, while the direction still comes from the
        // normalised −20 and turns the short way. So the step is the FULL rate and it goes
        // downward through 180.
        FeetYaw feet = default;

        feet.Advance(170f, speed: 0f, Tick);
        feet.Advance(-170f, speed: 300f, Tick);

        // A full-rate step at 66 ticks is 720/66 = 10.9 degrees, taken downward from 170 and
        // normalised — so just past the wrap rather than eased a fraction of a degree.
        feet.Current.ShouldBe(-179.1f, 0.2f);
    }
}
