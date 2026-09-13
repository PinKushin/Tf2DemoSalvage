using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// How a body loses speed and spin each step — <c>FUN_180078250</c> and <c>FUN_180077a20</c> (B58, D146, B369).
/// </summary>
/// <remarks>
/// **The `.phy` has carried damping since the reader was written and nothing applied it.** Every ragdoll element declares
/// `damping` and `rotdamping`, measured across the game's models as **linear zero on every element and rotational 4 to 16
/// per joint** — so a corpse without this spins on and settles at the wrong rate.
///
/// **The engine reaches it from the gravity controller** (`FUN_180074c80`): for each core without flag `0x10`,
/// `FUN_180078250(core, (double)step)`, then `FUN_180077950(core)` flushes the staged push, then gravity is added — see
/// <see cref="IvpGravity"/> and <see cref="IvpPush"/>.
///
/// **Read again instruction by instruction on 2026-09-13** (`docs/findings/51`, *The damping applier read again*), which
/// replaced a transcription of the decompiled routine and found four divergences in the port it had produced:
///
/// <code>
/// FUN_180078250(core, dt):  a core in movement state 2 or more damps by its factors plus 0.1f, as floats; otherwise
///                           FUN_180077a20(core, dt, core+0x30, (double)core+0x50)
/// FUN_180077a20(core, dt, r, s):
///   a = ((float)((double)r.x·dt), (float)((double)r.y·dt), (float)((double)r.z·dt))
///   f = !((a.y² + a.x²) + a.z² ≥ 0.5f) ? (1f − a.x, 1f − a.y, 1f − a.z)                     -- COMISS/JNC: a NaN takes this
///                                      : (expf(−a.x), (float)exp((double)−a.y), (float)exp((double)−a.z))
///   k = dt·s;  k' = !(k ≥ 0.25) ? 1.0 − k : exp(−k)                                          -- COMISD/JNC
///   ω = (f.x·ω.x, f.y·ω.y, f.z·ω.z) in float;  v = (float)((double)v·k') per lane
/// </code>
///
/// **The `x` lane calls `expf` and the other two `exp`**, so three equal factors need not damp three lanes equally; the
/// port had one `MathF.Exp` factor for all three, the threshold as `3·s²`, the products in float from a float step, and the
/// speed factor in float. <see cref="IvpMath"/> carries the library's own `expf` and `exp`.
///
/// **The calm branch is not carried.** A core whose state byte `+0x1` is 2 or more damps harder by `0.1f` on every factor;
/// this project does not hold IVP's movement states, so every body damps as a moving one. *Which bodies TF2 holds calm, and
/// when, is unread* — `docs/RISKS.md` B369 names it.
/// </remarks>
public static class IvpDamping
{
    /// <summary><c>DAT_1800ea984</c>: <c>0.5f</c>, against the grouped sum of the three squared rotation products.</summary>
    private const float RotationThreshold = 0.5f;

    /// <summary><c>DAT_1800efdf8</c>: <c>0.25</c>, a double, against the single speed product.</summary>
    private const double SpeedThreshold = 0.25d;

    /// <summary>Damps every body's speed and spin for one step, as the gravity controller does.</summary>
    /// <param name="bodies">The bodies to damp.</param>
    /// <param name="delta">The timestep, which the controller widens from a float.</param>
    /// <exception cref="ArgumentNullException"><paramref name="bodies"/> is null.</exception>
    /// <remarks>
    /// **Inside the same `0x10` gate as gravity, and before the add**, because that is where the engine calls it from: a
    /// body that skips gravity skips this too.
    /// </remarks>
    public static void Apply(IReadOnlyList<IvpRigidBody> bodies, float delta)
    {
        ArgumentNullException.ThrowIfNull(bodies);

        for (int index = 0; index < bodies.Count; index++)
        {
            IvpRigidBody body = bodies[index];

            if (body.SkipsGravity)
            {
                continue;
            }

            // **Per axis, and IVP holds three where Valve supplies one** — see `IvpRigidBody.RotationDamping`.
            Damp(body, delta, (body.RotationDamping, body.RotationDamping, body.RotationDamping), body.Damping);
        }
    }

    /// <summary><c>FUN_180077a20</c>: one core's damping for one step.</summary>
    /// <param name="body">The body, whose angular velocity and velocity are damped.</param>
    /// <param name="delta">The timestep, as the double the routine takes.</param>
    /// <param name="rotation">The three rotation damping factors, <c>core+0x30..0x38</c>.</param>
    /// <param name="speedDamping">The speed damping, <c>core+0x50</c> widened.</param>
    /// <exception cref="ArgumentNullException"><paramref name="body"/> is null.</exception>
    public static void Damp(IvpRigidBody body, double delta, (float X, float Y, float Z) rotation, double speedDamping)
    {
        ArgumentNullException.ThrowIfNull(body);

        float x = (float)(rotation.X * delta);
        float y = (float)(rotation.Y * delta);
        float z = (float)(rotation.Z * delta);

        float factorX;
        float factorY;
        float factorZ;

        if ((y * y) + (x * x) + (z * z) >= RotationThreshold)
        {
            factorX = IvpMath.Expf(-x);
            factorY = (float)IvpMath.Exp(-(double)y);
            factorZ = (float)IvpMath.Exp(-(double)z);
        }
        else
        {
            factorX = 1f - x;
            factorY = 1f - y;
            factorZ = 1f - z;
        }

        double product = delta * speedDamping;
        double scale = product >= SpeedThreshold ? IvpMath.Exp(-product) : 1d - product;

        body.AngularVelocity = (
            factorX * body.AngularVelocity.X,
            factorY * body.AngularVelocity.Y,
            factorZ * body.AngularVelocity.Z);

        body.Velocity = (
            (float)(body.Velocity.X * scale),
            (float)(body.Velocity.Y * scale),
            (float)(body.Velocity.Z * scale));
    }
}
