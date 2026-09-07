using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// The killing blow a corpse is created with — <c>RagdollCreate</c> (B58, D146).
/// </summary>
/// <remarks>
/// **`m_vecForce`, `m_nForceBone` and `m_vecRagdollVelocity` were decoded and read by nothing**,
/// and between them they are the wire's whole account of how a body left its feet
/// ([[a-tf2-corpse-is-simulated-not-sent]]). The law is `ragdoll_shared.cpp:405` — see
/// <see cref="RagdollSimulation.Kill"/> for the quoted source.
///
/// **A push is STAGED, not applied**, which is what most of these tests are really pinning:
/// `FUN_180077950` drains the staging pair at the start of the next step, so a corpse's launch is
/// visible one step after creation and not before.
/// </remarks>
public sealed class RagdollDeathForceConformanceTests
{
    private const float Step = 1f / 66f;

    /// <remarks>
    /// **The struck bone takes the WHOLE force through its centre** —
    /// `list[forceBone]->ApplyForceCenter( nudgeForce )` — so it gains `F / m` of speed and no spin
    /// of its own. Ten kilos and a thousand of impulse is a hundred units a second.
    /// </remarks>
    [Test]
    public void Kill_OnTheStruckBone_AddsTheWholeForceOverItsMass()
    {
        RagdollSimulation simulation = Simulation();

        simulation.Kill((0f, 0f, 1000f), forceBone: 0);

        // Staged, so nothing has moved yet — the control on the staging itself.
        simulation.Environment.Bodies[0].Velocity.Z.ShouldBe(0f);

        simulation.Step();

        // **`F / m` less one step of gravity, exactly.** The step that flushes the push also adds
        // `g · dt`, because the engine's order is damping, flush, gravity — so a prediction of the
        // bare 100 would be wrong by 12.12 and this test earned that correction.
        simulation.Environment.Bodies[0].Velocity.Z.ShouldBe(100f - Gravity, 1e-2f);
    }

    /// <summary>One step of Source gravity, which every prediction here has to carry.</summary>
    private const float Gravity = 800f * Step;

    /// <remarks>
    /// **Every other body takes a share by MASS, at the struck bone's position** — an offset push,
    /// so the rest of the corpse swings about the hit rather than sliding with it. The child here
    /// is two kilos of a twelve-kilo body, so its share is a sixth of the force and its speed is
    /// that over its own mass.
    /// </remarks>
    [Test]
    public void Kill_OnEveryOtherBody_SharesTheForceByMassAtTheStruckPosition()
    {
        RagdollSimulation simulation = Simulation();

        simulation.Kill((0f, 0f, 1200f), forceBone: 0);
        simulation.Step();

        // share = 2 / 12; impulse = 200; over 2 kg that is 100 units a second, less one step of
        // gravity for the same reason as the test above.
        simulation.Environment.Bodies[1].Velocity.Z.ShouldBe(100f - Gravity, 1e-2f);

        // **And it SPINS, which is the whole point of the offset form.** The child is not at the
        // struck bone's position, so the same impulse gives it angular velocity too — a centre
        // push would leave this at zero.
        (simulation.Environment.Bodies[1].AngularVelocity.X != 0f ||
         simulation.Environment.Bodies[1].AngularVelocity.Y != 0f).ShouldBeTrue();
    }

    /// <remarks>
    /// **No force bone means no push at all**, because the engine's second loop is gated on a
    /// `forcePosition` that only the first branch sets. The engine zeroes `m_vecForce` outright for
    /// a corpse it gave a death animation (`c_tf_player.cpp:847`), so this is the common case and
    /// not an error path.
    /// </remarks>
    [Test]
    public void Kill_WithNoForceBone_PushesNothing()
    {
        RagdollSimulation simulation = Simulation();

        simulation.Kill((0f, 0f, 1000f), forceBone: -1);
        simulation.Step();

        // Gravity has run, so the control is that nothing went UP.
        simulation.Environment.Bodies[0].Velocity.Z.ShouldBeLessThan(0f);
        simulation.Environment.Bodies[1].Velocity.Z.ShouldBeLessThan(0f);
    }

    /// <remarks>
    /// **The inherited velocity is mass-independent**, because `AddVelocity` is velocities and not
    /// forces on Valve's own instruction (`vphysics_interface.h:784`). The two bodies here differ
    /// in mass by five times and must come out identical — a push divided by mass would not.
    /// </remarks>
    [Test]
    public void Inherit_WithBodiesOfDifferentMass_GivesThemTheSameVelocity()
    {
        RagdollSimulation simulation = Simulation();

        simulation.Inherit((200f, 0f, 0f));
        simulation.Step();

        simulation.Environment.Bodies[0].Velocity.X.ShouldBe(200f, 1e-2f);
        simulation.Environment.Bodies[1].Velocity.X.ShouldBe(200f, 1e-2f);
    }

    /// <summary>A two-body ragdoll, ten kilos and two, with no world to land on.</summary>
    private static RagdollSimulation Simulation()
    {
        PhysicsModel physics = PhysicsModel.From(
            [
                new PhysicsSolid(0, "bip_root", "", "flesh", 10f, 1f, 0f, 0f, 100f, 0f),
                new PhysicsSolid(1, "bip_child", "bip_root", "flesh", 2f, 1f, 0f, 0f, 20f, 0f),
            ],
            [],
            2,
            checksum: 0);

        RagdollBody body = RagdollBody.Build(physics, RagdollSkeletons.Straight())!;

        List<(Vector3 Position, Quaternion Orientation)> start =
        [
            (new Vector3(0f, 0f, 0f), Quaternion.Identity),

            // **Offset sideways as well as up, and that is a corrected condition rather than a
            // tidier one.** With the child directly above the struck bone, the lever arm is
            // parallel to an upward force and the cross product is zero — so an offset push and a
            // centre push predict the SAME spin of none, and the test could not tell them apart.
            (new Vector3(10f, 0f, 20f), Quaternion.Identity),
        ];

        return RagdollSimulation.Create(body, Step, start);
    }
}
