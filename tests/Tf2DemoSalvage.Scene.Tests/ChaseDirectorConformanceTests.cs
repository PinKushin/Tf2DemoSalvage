using System;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// The director's chase parameters and its second target, from <c>CalcChaseCamView</c>.
/// </summary>
/// <remarks>
/// **These are the parts of the chase camera this project left out**, each with an accurate comment
/// saying so, which is the shape D117 exists to stop. The engine, in order:
///
/// <code>
///   QAngle angleOffset( m_flPhi, m_flTheta, 0 );
///   QAngle cameraAngles = m_aCamAngle;
///
///   if ( bManual )                                  engine->GetViewAngles( cameraAngles );
///   else if ( target2 )                           { forward = targetOrigin2 - targetOrigin1;
///                                                   VectorAngles( forward, cameraAngles );
///                                                   cameraAngles.z = 0; }
///   else if ( m_iTraget2 == 0 || m_iTraget2 == m_iTraget1 )
///                                                 { cameraAngles = target1->EyeAngles();
///                                                   cameraAngles.x = 0;
///                                                   cameraAngles.z = 0; }
///   else                                            angleOffset.Init();
///
///   if ( !bManual )
///   {
///       if ( !target1->IsAlive() ) angleOffset.x = 15;
///       cameraAngles += angleOffset;
///   }
///
///   AngleVectors( cameraAngles, &amp;forward );
///   VectorNormalize( forward );
///   VectorMA( targetOrigin1, -m_flDistance, forward, cameraOrigin );
///   targetOrigin1.z += m_flOffset;
/// </code>
///
/// **Note the last line, which is the detail most likely to be got wrong.** <c>m_flOffset</c> is
/// added AFTER the camera position is computed, so it moves the point looked at — and therefore
/// where the wall trace starts — while leaving the camera exactly where it was. Reading the
/// assignment in source order gives the opposite behaviour, and it would look plausible.
/// </remarks>
public sealed class ChaseDirectorConformanceTests
{
    [Test]
    public void View_WithATheta_SwingsTheCameraRoundTheTarget()
    {
        // `angleOffset( m_flPhi, m_flTheta, 0 )` is a QAngle: pitch, yaw, roll. So theta is the YAW
        // added to the target's, and with a yaw of 90 the camera moves from -x to -y.
        (float X, float Y, float Z, float Pitch, float Yaw, float Roll) view = ChaseCamera.View(
            0f, 0f, 0f, eyeYaw: 0f, alive: true, ducking: false,
            new ChaseSettings(Theta: 90f), lookAt: null);

        view.Yaw.ShouldBe(90f, 0.01f);
        view.X.ShouldBe(0f, 0.01f);
        view.Y.ShouldBe(-ChaseCamera.Distance, 0.01f);
    }

    [Test]
    public void View_WithAPhi_PitchesTheCameraDownAndRaisesIt()
    {
        // Phi is the PITCH of the same offset. Pitching down while sitting behind lifts the camera:
        // 96 * sin(20) above what it looks at, and 96 * cos(20) behind it.
        (float X, float Y, float Z, float Pitch, float Yaw, float Roll) view = ChaseCamera.View(
            0f, 0f, 0f, eyeYaw: 0f, alive: true, ducking: false,
            new ChaseSettings(Phi: 20f), lookAt: null);

        view.Pitch.ShouldBe(20f, 0.01f);
        view.X.ShouldBe(-ChaseCamera.Distance * MathF.Cos(20f * MathF.PI / 180f), 0.01f);
        view.Z.ShouldBe(
            72f + (ChaseCamera.Distance * MathF.Sin(20f * MathF.PI / 180f)), 0.01f);
    }

    [Test]
    public void View_WithADistance_SitsThatFarBack()
    {
        ChaseCamera.View(
            0f, 0f, 0f, eyeYaw: 0f, alive: true, ducking: false,
            new ChaseSettings(Back: 200f), lookAt: null)
            .X.ShouldBe(-200f, 0.01f);
    }

    [Test]
    public void View_WithAnOffset_LeavesTheCameraWhereItWas()
    {
        // **`targetOrigin1.z += m_flOffset` comes AFTER the camera is placed.** So the offset cannot
        // move the camera, however natural it looks to apply it first. This is the case that fails
        // if the assignment is read in source order.
        (float X, float Y, float Z, float Pitch, float Yaw, float Roll) raised = ChaseCamera.View(
            0f, 0f, 0f, eyeYaw: 0f, alive: true, ducking: false,
            new ChaseSettings(Rise: 64f), lookAt: null);

        (float X, float Y, float Z, float Pitch, float Yaw, float Roll) plain = ChaseCamera.View(
            0f, 0f, 0f, eyeYaw: 0f, alive: true, ducking: false,
            new ChaseSettings(), lookAt: null);

        raised.Z.ShouldBe(plain.Z, 0.01f, "m_flOffset raises what is looked at, not the camera");
        raised.X.ShouldBe(plain.X, 0.01f);
    }

    [Test]
    public void View_WithASecondTarget_LooksFromOneTowardsTheOther()
    {
        // `forward = targetOrigin2 - targetOrigin1; VectorAngles( forward, cameraAngles );`
        // A second target due north of the first means the camera looks north — yaw 90 — from
        // ninety-six units south of the first, REGARDLESS of which way the first is facing.
        (float X, float Y, float Z, float Pitch, float Yaw, float Roll) view = ChaseCamera.View(
            0f, 0f, 0f, eyeYaw: 180f, alive: true, ducking: false,
            new ChaseSettings(), lookAt: (0f, 500f, 72f));

        view.Yaw.ShouldBe(90f, 0.5f);
        view.Y.ShouldBe(-ChaseCamera.Distance, 0.5f);
    }

    [Test]
    public void View_WithASecondTargetAbove_PitchesUpTowardsIt()
    {
        // VectorAngles takes the full direction, so a target overhead pitches the camera. Source's
        // pitch is NEGATIVE for up, which is the convention this whole project already uses.
        ChaseCamera.View(
            0f, 0f, 0f, eyeYaw: 0f, alive: true, ducking: false,
            new ChaseSettings(), lookAt: (500f, 0f, 572f))
            .Pitch.ShouldBeLessThan(0f, "looking up is a negative pitch in Source");
    }

    [Test]
    public void View_WithASecondTargetAndADeadFirst_StillPitchesDownFifteen()
    {
        // `if ( !target1->IsAlive() ) angleOffset.x = 15;` runs after the angles are chosen, so it
        // applies to the second-target case too — it REPLACES phi rather than adding to it.
        (float X, float Y, float Z, float Pitch, float Yaw, float Roll) view = ChaseCamera.View(
            0f, 0f, 0f, eyeYaw: 0f, alive: false, ducking: false,
            new ChaseSettings(Phi: 40f), lookAt: (500f, 0f, 14f));

        view.Pitch.ShouldBe(ChaseCamera.DeadPitch, 0.01f);
    }
}
