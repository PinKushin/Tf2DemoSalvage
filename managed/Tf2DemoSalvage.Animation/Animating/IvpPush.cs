using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// Pushing a body, and the staging the engine pushes into — <c>FUN_180077950</c> (B58, D146).
/// </summary>
/// <remarks>
/// **Valve's own header settles the arithmetic, and it is not what the names suggest.**
/// `ApplyForceCenter` and `ApplyForceOffset` are commented *"force vector is direction &amp;
/// magnitude of impulse kg in / s"* (`vphysics_interface.h:803`) — impulses, not forces, so a push
/// divides by mass once and does not integrate over time. `AddVelocity` two lines above says the
/// opposite in its own comment: *"These are velocities, not forces. i.e. They will have the same
/// effect regardless of the object's mass or inertia"*.
///
/// **Nothing here reaches a velocity directly.** Everything stages into
/// <see cref="IvpRigidBody.PendingVelocity"/>, which `FUN_180077950` drains at the start of the
/// next step — see <see cref="Flush"/>. That is the engine's arrangement rather than a buffer
/// invented here, and it is why a corpse's creation force shows up one step after it is applied.
/// </remarks>
public static class IvpPush
{
    /// <summary>Drains every body's staged velocity into its real one — <c>FUN_180077950</c>.</summary>
    /// <param name="bodies">The bodies to flush.</param>
    /// <exception cref="ArgumentNullException"><paramref name="bodies"/> is null.</exception>
    /// <remarks>
    /// **Cleared after the add, in the same routine.** A stage that survived its flush would apply
    /// the same push every step for the life of the corpse.
    /// </remarks>
    public static void Flush(IReadOnlyList<IvpRigidBody> bodies)
    {
        ArgumentNullException.ThrowIfNull(bodies);

        for (int index = 0; index < bodies.Count; index++)
        {
            IvpRigidBody body = bodies[index];

            body.AngularVelocity = (
                body.AngularVelocity.X + body.PendingAngularVelocity.X,
                body.AngularVelocity.Y + body.PendingAngularVelocity.Y,
                body.AngularVelocity.Z + body.PendingAngularVelocity.Z);

            body.Velocity = (
                body.Velocity.X + body.PendingVelocity.X,
                body.Velocity.Y + body.PendingVelocity.Y,
                body.Velocity.Z + body.PendingVelocity.Z);

            body.PendingAngularVelocity = (0f, 0f, 0f);
            body.PendingVelocity = (0f, 0f, 0f);
        }
    }

    /// <summary>Stages a velocity change directly — <c>AddVelocity</c>.</summary>
    /// <param name="body">The body to push.</param>
    /// <param name="velocity">The world-space velocity to add.</param>
    /// <param name="angular">The angular velocity to add.</param>
    /// <exception cref="ArgumentNullException"><paramref name="body"/> is null.</exception>
    /// <remarks>
    /// **Mass-independent, on Valve's own instruction**: *"These are velocities, not forces"*
    /// (`vphysics_interface.h:784`). Dividing by mass here would be the same mistake in reverse as
    /// treating an impulse as a force.
    /// </remarks>
    public static void AddVelocity(
        IvpRigidBody body, (float X, float Y, float Z) velocity, (float X, float Y, float Z) angular)
    {
        ArgumentNullException.ThrowIfNull(body);

        body.PendingVelocity = (
            body.PendingVelocity.X + velocity.X,
            body.PendingVelocity.Y + velocity.Y,
            body.PendingVelocity.Z + velocity.Z);

        body.PendingAngularVelocity = (
            body.PendingAngularVelocity.X + angular.X,
            body.PendingAngularVelocity.Y + angular.Y,
            body.PendingAngularVelocity.Z + angular.Z);
    }

    /// <summary>Stages an impulse through the centre of mass — <c>ApplyForceCenter</c>.</summary>
    /// <param name="body">The body to push.</param>
    /// <param name="impulse">The impulse, in kg·in/s.</param>
    /// <exception cref="ArgumentNullException"><paramref name="body"/> is null.</exception>
    /// <remarks>
    /// **Through the centre, so it adds no spin.** That is the whole difference from
    /// <see cref="ApplyForceOffset"/>, and it is why the engine gives the force bone this one and
    /// every other body the offset version.
    /// </remarks>
    public static void ApplyForceCenter(IvpRigidBody body, (float X, float Y, float Z) impulse)
    {
        ArgumentNullException.ThrowIfNull(body);

        AddVelocity(
            body,
            (impulse.X * body.InverseMass,
             impulse.Y * body.InverseMass,
             impulse.Z * body.InverseMass),
            (0f, 0f, 0f));
    }

    /// <summary>Stages an impulse at a point, which also spins the body — <c>ApplyForceOffset</c>.</summary>
    /// <param name="body">The body to push.</param>
    /// <param name="impulse">The impulse, in kg·in/s.</param>
    /// <param name="at">Where it is applied, in world space.</param>
    /// <exception cref="ArgumentNullException"><paramref name="body"/> is null.</exception>
    /// <remarks>
    /// **The lever arm is measured from the body's own position**, and the angular part is
    /// `r × J` through the inverse inertia — the same expression a contact uses, because it is the
    /// same physics.
    /// </remarks>
    public static void ApplyForceOffset(
        IvpRigidBody body, (float X, float Y, float Z) impulse, (float X, float Y, float Z) at)
    {
        ArgumentNullException.ThrowIfNull(body);

        (float X, float Y, float Z) arm = (
            at.X - (float)body.Position.X,
            at.Y - (float)body.Position.Y,
            at.Z - (float)body.Position.Z);

        (float X, float Y, float Z) turn = (
            (arm.Y * impulse.Z) - (arm.Z * impulse.Y),
            (arm.Z * impulse.X) - (arm.X * impulse.Z),
            (arm.X * impulse.Y) - (arm.Y * impulse.X));

        AddVelocity(
            body,
            (impulse.X * body.InverseMass,
             impulse.Y * body.InverseMass,
             impulse.Z * body.InverseMass),
            (turn.X * body.InverseInertia.X,
             turn.Y * body.InverseInertia.Y,
             turn.Z * body.InverseInertia.Z));
    }
}
