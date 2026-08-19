using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// <c>move_x</c> and <c>move_y</c> against <c>ComputePoseParam_MoveYaw</c>, term by term.
/// </summary>
/// <remarks>
/// **The engine's arithmetic, quoted so the predictions below are derived rather than chosen**
/// (<c>multiplayer_animstate.cpp:1566</c>):
///
/// <code>
/// float flAngle = AngleNormalize( m_flEyeYaw );
/// float flYaw = flAngle - m_PoseParameterData.m_flEstimateYaw;
/// flYaw = AngleNormalize( -flYaw );
/// ...
/// if ( mp_slammoveyaw.GetBool() ) { flYaw = SnapYawTo( flYaw ); }
/// vecCurrentMoveYaw.x =  cos( DEG2RAD( flYaw ) );
/// vecCurrentMoveYaw.y = -sin( DEG2RAD( flYaw ) );
/// // push edges out to -1 to 1 box
/// float flInvScale = MAX( fabs( vecCurrentMoveYaw.x ), fabs( vecCurrentMoveYaw.y ) );
/// if ( flInvScale != 0.0f ) { vecCurrentMoveYaw.x /= flInvScale; vecCurrentMoveYaw.y /= flInvScale; }
/// </code>
///
/// and <c>m_flEstimateYaw</c> is the direction of travel while moving:
/// <c>atan2( vecEstVelocity.y, vecEstVelocity.x ) * 180 / M_PI</c>.
///
/// Three divergences are fixed here and each fails a different case:
///
/// <list type="number">
/// <item><b>The sign was inverted.</b> Two negations cancel to <c>flYaw = estimateYaw − eyeYaw</c>,
/// and this project computed <c>eyeYaw − estimateYaw</c>. Invisible when running dead forward,
/// because <c>flYaw</c> is zero either way — it swaps STRAFE LEFT with STRAFE RIGHT, which is why
/// the earlier measurement of a forward run at <c>move_x = 1.000</c> could not see it.</item>
/// <item><b>The snap was unconditional.</b> <c>SnapYawTo</c> runs only under
/// <c>mp_slammoveyaw</c>, declared <c>ConVar mp_slammoveyaw( "mp_slammoveyaw", "0",
/// FCVAR_REPLICATED | FCVAR_DEVELOPMENTONLY, ... )</c> — off in shipped TF2 and not settable in a
/// normal build. Applying it quantised every direction to eight compass points.</item>
/// <item><b>The box push-out was missing.</b> A diagonal came out at 0.707 on each axis, which is
/// mid-cell, so the corner animations of the nine-way blend were never reached at all.</item>
/// </list>
///
/// **The fourth divergence is not fixed and is stated rather than hidden.** After the push-out the
/// engine scales both components by <c>flSpeed / flMaxSpeed</c> when the player is moving slower
/// than the sequence was authored for, where <c>flMaxSpeed</c> is
/// <c>GetSequenceGroundSpeed( GetSequence() )</c>. That is the authored ground speed of the chosen
/// animation, which comes from <c>mstudiomovement_t</c> in the model — this layer decodes a demo and
/// has never read a model. Recorded in B101; a player easing along therefore still animates at a
/// full-magnitude blend.
/// </remarks>
public sealed class MoveYawConformanceTests
{
    /// <summary>How far a keyframe pair moves, over enough ticks to count as running.</summary>
    private const float Distance = 200f;

    [Test]
    public void RunningStraightForward_IsFullyForward()
    {
        // flYaw = 0, so x = cos 0 = 1 and y = -sin 0 = 0. The push-out divides by 1 and changes
        // nothing. This is the case the POV recording already measured at 1.000, kept here as the
        // control for the sign change below: a fix that flipped it would break this.
        (float x, float y) = Parameters(headingDegrees: 0f, bodyYaw: 0f);

        x.ShouldBe(1f, 1e-3f);
        y.ShouldBe(0f, 1e-3f);
    }

    [Test]
    public void RunningStraightBackward_IsFullyBackward()
    {
        (float x, float y) = Parameters(headingDegrees: 180f, bodyYaw: 0f);

        x.ShouldBe(-1f, 1e-3f);
        y.ShouldBe(0f, 1e-3f);
    }

    [Test]
    public void MovingNinetyDegreesCounterClockwise_DrivesMoveYNegative()
    {
        // **The case the sign error broke, and the only kind that can show it.** Source yaw runs
        // counter-clockwise, so a player facing +X who travels along +Y is moving to their left.
        // estimateYaw = 90, eyeYaw = 0, so flYaw = 90 − 0 = 90 and y = -sin 90 = -1.
        //
        // The old code computed eyeYaw − estimateYaw = −90 and answered +1, putting the player on
        // the opposite corner of the blend grid: strafing left played the strafe-right animation.
        (float x, float y) = Parameters(headingDegrees: 90f, bodyYaw: 0f);

        x.ShouldBe(0f, 1e-3f);
        y.ShouldBe(-1f, 1e-3f);
    }

    [Test]
    public void MovingNinetyDegreesClockwise_DrivesMoveYPositive()
    {
        // The mirror, which is what makes the pair a measurement rather than one anchored value.
        (float x, float y) = Parameters(headingDegrees: -90f, bodyYaw: 0f);

        x.ShouldBe(0f, 1e-3f);
        y.ShouldBe(1f, 1e-3f);
    }

    [Test]
    public void TheBodyYawIsSubtracted_NotIgnored()
    {
        // Travelling due north while facing north is forward, however far north is from zero. A
        // implementation that used the heading alone would answer the same as the 90° case above.
        (float x, float y) = Parameters(headingDegrees: 90f, bodyYaw: 90f);

        x.ShouldBe(1f, 1e-3f);
        y.ShouldBe(0f, 1e-3f);
    }

    [Test]
    public void MoveYaw_ADiagonal_IsPushedOutToTheCorner()
    {
        // cos 45 = -sin 45 magnitude = 0.7071, and MAX of the two is 0.7071, so both become ±1.
        // Without the push-out this is (0.707, -0.707), which lands mid-cell and never reaches the
        // corner animation the nine-way blend keeps there.
        (float x, float y) = Parameters(headingDegrees: 45f, bodyYaw: 0f);

        x.ShouldBe(1f, 1e-3f);
        y.ShouldBe(-1f, 1e-3f);
    }

    [Test]
    public void AnOffAngleKeepsItsShape_BecauseTheSnapIsOff()
    {
        // **The case that separates snapping from not snapping**, chosen inside SnapYawTo's 23-to-67
        // band so the two disagree: snapped it would become 45° and answer (1, -1); unsnapped the
        // push-out divides by cos 30 = 0.8660 and gives (1, -0.5774).
        //
        // 30° is deliberate. An angle near a compass point predicts nearly the same value either
        // way, which is a condition where correct and broken agree.
        (float x, float y) = Parameters(headingDegrees: 30f, bodyYaw: 0f);

        x.ShouldBe(1f, 1e-3f);
        y.ShouldBe(-0.5774f, 1e-3f);
    }

    [Test]
    public void StandingStill_IsTheCentreOfTheGrid()
    {
        // bIsMoving is false, and the engine sets both parameters to zero outright rather than
        // computing a direction from a velocity that has none.
        ScenePropTrack track = new(entityIndex: 3, "models/player/scout.mdl");

        track.Add(0, new ScenePose { X = 0f, Y = 0f, Z = 0f, Yaw = 0f });
        track.Add(7, new ScenePose { X = 0f, Y = 0f, Z = 0f, Yaw = 0f });

        ScenePose pose = Sampled(track);

        pose.MoveX.ShouldBe(0f);
        pose.MoveY.ShouldBe(0f);
    }

    /// <summary>The parameters for a player travelling one way while facing another.</summary>
    /// <param name="headingDegrees">Which way they move, in world degrees.</param>
    /// <param name="bodyYaw">Which way they face.</param>
    private static (float X, float Y) Parameters(float headingDegrees, float bodyYaw)
    {
        double radians = headingDegrees * (Math.PI / 180d);

        float toX = (float)(Distance * Math.Cos(radians));
        float toY = (float)(Distance * Math.Sin(radians));

        ScenePropTrack track = new(entityIndex: 3, "models/player/scout.mdl");

        track.Add(0, new ScenePose { X = 0f, Y = 0f, Z = 0f, Yaw = bodyYaw });
        track.Add(7, new ScenePose { X = toX, Y = toY, Z = 0f, Yaw = bodyYaw });

        ScenePose pose = Sampled(track);

        return (pose.MoveX, pose.MoveY);
    }

    /// <summary>The pose one interpolation delay after the second keyframe.</summary>
    private static ScenePose Sampled(ScenePropTrack track)
    {
        DemoTimeline timeline = DemoTimeline.ForTracks([track]);

        List<SceneProp> props = [];
        timeline.PropsAt(13d, props);

        return props.Single().Pose;
    }
}
