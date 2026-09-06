using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// How gravity reaches a body's velocity in IVP — <c>FUN_180074c80</c> (B58, D146).
/// </summary>
/// <remarks>
/// **Gravity is not a field on a body; it is MEMBERSHIP OF A LIST.** That is the finding, and it
/// came from a published method name rather than from pattern matching:
/// `IPhysicsObject::EnableGravity( bool )` must switch something, and `FUN_18001ba30` shows what —
/// it adds the object to, or removes it from, a list held by a per-environment controller:
///
/// <code>
/// cVar2 = (**(code **)(*param_1 + 8))();               // IsStatic() — a static object is refused
/// if (cVar2 == '\0') {
///   cVar2 = (**(code **)(*param_1 + 0x38))(param_1);   // IsGravityEnabled()
///   if (param_2 != cVar2) {
///     lVar1 = *(longlong *)(param_1[2] + 0xe8);        // the controller
///     if (param_2 != '\0') { FUN_1800748b0(lVar1, …); return; }   // add
///     FUN_180074fb0(lVar1, …);                                    // remove
///   }
/// }
/// </code>
///
/// **The controller is a singleton at `env+0x0`**, vtable `0x1800ea728` — eight slots, bounded
/// neatly by the string `"sys:gravity"` immediately after them — and slot 4 walks its own list
/// (`controller+0x1e8`, count at `+0x1e2`) applying the acceleration. That is why nothing in the
/// simulation pipeline ever reads the gravity stored on the environment: `SetGravity` writes it to
/// `env+0x118/0x120/0x128` as doubles AND copies it into the controller as floats, and the
/// controller's copy is the one that does the work.
///
/// **`env+0x138` is confirmed by name** — the string `"m_gravityLength"` sits in `.rdata`.
/// </remarks>
public static class IvpGravity
{
    /// <summary>Applies one step of gravity to every body that takes it.</summary>
    /// <param name="bodies">The bodies registered for gravity.</param>
    /// <param name="gravity">The environment's acceleration, in units per second squared.</param>
    /// <param name="delta">The step, in seconds.</param>
    /// <param name="alternate">
    /// The second acceleration, for bodies that ask for it. Defaults to the same vector, which is
    /// TF2's case — nothing in the game sets a per-object gravity.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="bodies"/> is null.</exception>
    /// <remarks>
    /// **Verbatim from slot 4:**
    ///
    /// <code>
    /// if ((*pbVar4 &amp; 0x10) == 0) {
    ///   FUN_180078250((longlong)pbVar4,(double)*param_2);
    ///   FUN_180077950((longlong)pbVar4);
    ///   fVar1 = *param_2;
    ///   if ((*pbVar4 &amp; 0x20) == 0) { g = controller+0x10/0x14/0x18; }
    ///   else                        { g = controller+0x20/0x24/0x28; }
    ///   *(float *)(pbVar4 + 0x140) = g.x * fVar1 + *(float *)(pbVar4 + 0x140);
    ///   *(float *)(pbVar4 + 0x148) = g.z * fVar1 + *(float *)(pbVar4 + 0x148);
    ///   *(float *)(pbVar4 + 0x144) = g.y * fVar1 + *(float *)(pbVar4 + 0x144);
    /// }
    /// </code>
    ///
    /// - **`v += g * dt`, with NO mass term**, which is what makes it gravity: an acceleration must
    ///   not scale with mass, and nothing here is velocity-dependent, so it is not drag either.
    /// - **Bit `0x10` skips the body entirely**, including the two helper calls above the add.
    /// - **Bit `0x20` selects a SECOND acceleration.** IVP supports per-object gravity; carrying one
    ///   global vector would be right for TF2 and wrong for the engine.
    /// - **It accumulates**, so gravity and a constraint impulse in the same step both land.
    ///
    /// **What is NOT transcribed, and is named rather than skipped silently:** the two calls the
    /// engine makes before the add — `FUN_180078250(core, dt)` and `FUN_180077950(core)` — have not
    /// been read. They take the core and the step, so they are per-body per-step work that happens
    /// under the same `0x10` gate, and whatever they do is missing here.
    /// </remarks>
    public static void Apply(
        IReadOnlyList<IvpRigidBody> bodies,
        (float X, float Y, float Z) gravity,
        float delta,
        (float X, float Y, float Z)? alternate = null)
    {
        ArgumentNullException.ThrowIfNull(bodies);

        for (int index = 0; index < bodies.Count; index++)
        {
            IvpRigidBody body = bodies[index];

            if (body.SkipsGravity)
            {
                continue;
            }

            (float X, float Y, float Z) acceleration =
                body.UsesAlternateGravity && alternate is { } second ? second : gravity;

            body.Velocity = (
                (acceleration.X * delta) + body.Velocity.X,
                (acceleration.Y * delta) + body.Velocity.Y,
                (acceleration.Z * delta) + body.Velocity.Z);
        }
    }
}
