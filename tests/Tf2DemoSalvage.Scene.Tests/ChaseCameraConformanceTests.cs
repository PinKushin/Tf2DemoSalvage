using System;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// <see cref="ChaseCamera"/> against <c>C_HLTVCamera::CalcChaseCamView</c>.
/// </summary>
/// <remarks>
/// **Written before the implementation, off <c>game/client/hltvcamera.cpp</c>**, so what it asserts
/// is the engine's behaviour rather than a description of whatever got built. The single-target
/// auto-director path is the whole of what this viewer needs:
///
/// <code>
///   targetOrigin1 = target1->GetRenderOrigin();
///   if      ( !target1->IsAlive() )              targetOrigin1 += VEC_DEAD_VIEWHEIGHT;
///   else if ( target1->GetFlags() &amp; FL_DUCKING ) targetOrigin1 += VEC_DUCK_VIEW;
///   else                                         targetOrigin1 += VEC_VIEW;
///
///   cameraAngles = target1->EyeAngles();
///   cameraAngles.x = 0; // no PITCH
///   cameraAngles.z = 0; // no ROLL
///   if ( !target1->IsAlive() ) angleOffset.x = 15;
///   cameraAngles += angleOffset;
///
///   AngleVectors( cameraAngles, &amp;forward );
///   VectorMA( targetOrigin1, -m_flDistance, forward, cameraOrigin );
/// </code>
///
/// with <c>m_flDistance = 96</c> and <c>m_flPhi = m_flTheta = m_flOffset = 0</c> from
/// <c>C_HLTVCamera::Reset</c> (<c>hltvcamera.cpp:93</c>).
///
/// **Heights come from TF2's game rules, not from the per-class table** — <c>VEC_VIEW</c> is a flat
/// 72 and <c>VEC_DUCK_VIEW</c> a flat 45 (<c>g_TFViewVectors</c>), and <c>VEC_DEAD_VIEWHEIGHT</c> is
/// <c>Vector( 0, 0, 14 )</c> (<c>tf_gamerules.cpp:1323</c>). <see cref="PlayerEye"/> already carried
/// the dead one, with a comment warning it is a chase TARGET height rather than an eye height.
/// </remarks>
public sealed class ChaseCameraConformanceTests
{
    /// <summary>Where the camera should sit, computed from the citation rather than the code.</summary>
    /// <remarks>
    /// The camera is <c>m_flDistance</c> back along the direction it faces, so with a yaw of zero
    /// (facing +x) it sits 96 units in −x from the point it looks at.
    /// </remarks>
    private const float Back = ChaseCamera.Distance;

    [Test]
    public void Approach_WhenAWallBlocksCloserThanTheCameraIs_MovesInAtOnce()
    {
        // `else { cameraOrigin = trace.endpos; m_flLastDistance = dist; }` — blocking bites the
        // same frame. A camera that eased inwards would spend that time inside the wall.
        ChaseCamera.Approach(blockedAt: 30f, lastDistance: 96f, seconds: 1f / 60f)
            .ShouldBe(30f, 0.01f);
    }

    [Test]
    public void Approach_AfterTheWallClears_EasesOutAtThirtyTwoUnitsASecond()
    {
        // `m_flLastDistance += gpGlobals->frametime * 32.0f` — half a second from 30 is 30 + 16.
        ChaseCamera.Approach(blockedAt: ChaseCamera.Distance, lastDistance: 30f, seconds: 0.5f)
            .ShouldBe(46f, 0.01f);
    }

    [Test]
    public void Approach_WithNothingBlocking_StopsAtTheFullDistance()
    {
        // **The control, and without it the recovery has no ceiling.** When the trace is clear its
        // endpos IS the wanted camera position, so `dist` equals m_flDistance and the growth cannot
        // carry the camera past it however long it runs.
        ChaseCamera.Approach(blockedAt: ChaseCamera.Distance, lastDistance: 95f, seconds: 10f)
            .ShouldBe(ChaseCamera.Distance, 0.01f);
    }

    [Test]
    public void View_ForALivingTarget_LooksFromBehindAtStandingEyeHeight()
    {
        // Yaw 0 faces +x, so the camera sits 96 units behind in −x, level with a 72-unit eye.
        (float X, float Y, float Z, float Pitch, float Yaw, float Roll) view =
            ChaseCamera.View(100f, 200f, 0f, eyeYaw: 0f, alive: true, ducking: false);

        view.X.ShouldBe(100f - Back, 0.01f);
        view.Y.ShouldBe(200f, 0.01f);
        view.Z.ShouldBe(72f, 0.01f);

        // "no PITCH", "no ROLL", and the camera looks the way the target looks.
        view.Pitch.ShouldBe(0f, 0.01f);
        view.Yaw.ShouldBe(0f, 0.01f);
        view.Roll.ShouldBe(0f, 0.01f);
    }

    [Test]
    public void View_ForADuckingTarget_LooksAtTheLowerDuckHeight()
    {
        // VEC_DUCK_VIEW rather than VEC_VIEW: 45 instead of 72.
        ChaseCamera.View(0f, 0f, 0f, eyeYaw: 0f, alive: true, ducking: true)
            .Z.ShouldBe(45f, 0.01f);
    }

    [Test]
    public void View_ForADeadTarget_LooksOverTheRagdollNotThroughIt()
    {
        // VEC_DEAD_VIEWHEIGHT is 14, and the engine's comment is "look over ragdoll, not through".
        // The camera is pitched down fifteen degrees, so it is also RAISED relative to the target:
        // looking down from 96 units away puts the camera 96*sin(15) above what it looks at.
        (float X, float Y, float Z, float Pitch, float Yaw, float Roll) view =
            ChaseCamera.View(0f, 0f, 0f, eyeYaw: 0f, alive: false, ducking: false);

        view.Pitch.ShouldBe(ChaseCamera.DeadPitch, 0.01f);

        view.Z.ShouldBe(
            PlayerEye.DeadChaseTarget + (Back * MathF.Sin(15f * MathF.PI / 180f)),
            0.01f,
            "a camera pitched down fifteen degrees sits above the point it looks at");

        view.X.ShouldBe(-Back * MathF.Cos(15f * MathF.PI / 180f), 0.01f);
    }

    [Test]
    public void View_ForATargetFacingNorth_SitsSouthOfThem()
    {
        // **The control for the yaw arithmetic, and it is the case that catches a sign error.** With
        // yaw 90 the target faces +y, so the camera must sit in −y. A camera placed with the wrong
        // sign is directly in front of the player, which looks almost right while showing the world
        // from the opposite side.
        (float X, float Y, float Z, float Pitch, float Yaw, float Roll) view =
            ChaseCamera.View(0f, 0f, 0f, eyeYaw: 90f, alive: true, ducking: false);

        view.X.ShouldBe(0f, 0.01f);
        view.Y.ShouldBe(-Back, 0.01f);
        view.Yaw.ShouldBe(90f, 0.01f);
    }

    [Test]
    public void View_ForATargetOffTheOrigin_IsRelativeToThem()
    {
        // A translation control: the whole result moves with the target, so nothing is anchored to
        // the map origin. An earlier bug in this project put a bound at the world origin exactly
        // because a position was dropped somewhere in a chain like this.
        (float X, float Y, float Z, float Pitch, float Yaw, float Roll) view =
            ChaseCamera.View(-1000f, 2500f, 128f, eyeYaw: 0f, alive: true, ducking: false);

        view.X.ShouldBe(-1000f - Back, 0.01f);
        view.Y.ShouldBe(2500f, 0.01f);
        view.Z.ShouldBe(128f + 72f, 0.01f);
    }
}
