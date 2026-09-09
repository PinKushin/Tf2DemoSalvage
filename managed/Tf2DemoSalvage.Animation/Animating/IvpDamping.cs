using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// How a body loses speed and spin each step — <c>FUN_180077a20</c> (B58, D146).
/// </summary>
/// <remarks>
/// **The `.phy` has carried damping since the reader was written and nothing applied it.** Every
/// ragdoll element declares `damping` and `rotdamping`, measured across the game's models as
/// **linear zero on every element and rotational 4 to 16 per joint** — so a corpse without this
/// spins on and settles at the wrong rate, which is the exact shape of a decoded field with no
/// consumer this project has shipped three times.
///
/// **The engine reaches it from the gravity step, and it was already named as unread there.**
/// `FUN_180074c80` calls `FUN_180078250(core, dt)` and `FUN_180077950(core)` before adding gravity,
/// inside the same `0x10` gate — see <see cref="IvpGravity"/>, which said those two "have not been
/// read". The first is this.
///
/// <code>
/// void FUN_180078250(longlong core, double dt)
/// {
///   if (1 &lt; *(byte *)(core + 1)) {
///     local_18 = *(float *)(core + 0x30) + DAT_1800ea968;   // 0.1
///     local_10 = *(float *)(core + 0x38) + DAT_1800ea968;
///     local_14 = *(float *)(core + 0x34) + DAT_1800ea968;
///     FUN_180077a20(core, dt, &amp;local_18, (double)(*(float *)(core + 0x50) + DAT_1800ea968));
///     return;
///   }
///   FUN_180077a20(core, dt, (float *)(core + 0x30), (double)*(float *)(core + 0x50));
/// }
/// </code>
///
/// So `core+0x30/0x34/0x38` is a THREE-AXIS rotation damping and `core+0x50` a single speed
/// damping, and the applier is:
///
/// <code>
/// fVar4 = (float)((double)*param_3 * dt);            // per axis
/// fVar5 = ...; fVar6 = ...;
/// if (DAT_1800ea984 &lt;= fVar5*fVar5 + fVar4*fVar4 + fVar6*fVar6) {   // 0.5
///     ... exp(-d) per axis ...                       // sign flipped by XOR with 0x80000000
/// } else {
///     fVar4 = DAT_1800ea988 - fVar4;                 // 1.0 - d
///     fVar5 = DAT_1800ea988 - fVar5;
///     fVar6 = DAT_1800ea988 - fVar6;
/// }
/// dVar3 = dt * param_4;
/// if (DAT_1800efdf8 &lt;= dVar3) { dVar3 = exp(-dVar3); }              // 0.25
/// else { dVar3 = DAT_1800ea9b8 - dVar3; }                           // 1.0
/// *(float *)(core + 0x130) = fVar4 * *(float *)(core + 0x130);      // angular velocity
/// *(float *)(core + 0x138) = fVar6 * *(float *)(core + 0x138);
/// *(float *)(core + 0x134) = fVar5 * *(float *)(core + 0x134);
/// *(float *)(core + 0x140) = *(float *)(core + 0x140) * dVar3;      // linear velocity
/// *(float *)(core + 0x144) = *(float *)(core + 0x144) * dVar3;
/// *(float *)(core + 0x148) = *(float *)(core + 0x148) * dVar3;
/// </code>
///
/// **`+0x140` is the LINEAR velocity and `+0x130` the angular, read from the other side**: gravity
/// accumulates into `+0x140/0x144/0x148` (<see cref="IvpGravity"/>), and an acceleration is added
/// to a linear velocity. So the three-axis term damps spin and the scalar damps speed, which is
/// also why the `.phy` spells `rotdamping` separately from `damping`.
///
/// **Every constant is settled in the disassembly, not in the decompiled C**
/// (`docs/memory/settle-a-constant-in-the-disassembly.md`): `DAT_1800ea984` is `0.5`,
/// `DAT_1800ea988` is `1.0`, `DAT_1800efdf8` is `0.25` as a double, `DAT_1800ea9b8` is `1.0` as a
/// double, and the XOR masks are the float and double sign bits, which is what makes those calls
/// `exp(−x)` rather than `exp(x)`.
///
/// **The `core+1 >= 2` branch adding `0.1` is NOT implemented, and the byte is unidentified.** The
/// value matches `g_PhysDefaultObjectParams`' `0.1` damping exactly, which is suggestive and is not
/// evidence; nothing traced sets that byte. It is transcribed above so the next reader has it.
/// </remarks>
public static class IvpDamping
{
    /// <summary>Damps every body's speed and spin for one step.</summary>
    /// <param name="bodies">The bodies to damp.</param>
    /// <param name="delta">The timestep.</param>
    /// <exception cref="ArgumentNullException"><paramref name="bodies"/> is null.</exception>
    /// <remarks>
    /// **Inside the same `0x10` gate as gravity, and before the add**, because that is where the
    /// engine calls it from: a body that skips gravity skips this too.
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

            // **Per axis, and IVP holds three where Valve supplies one** — see
            // `IvpRigidBody.RotationDamping`. The lanes are written in the order x, z, y in the
            // binary, which is instruction scheduling: each lane multiplies its own.
            float spin = body.RotationDamping * delta;

            // **The rotational branch tests the SUM OF SQUARES against 0.5**, not each lane, so
            // three small products can still cross it together.
            float factor = (3f * spin * spin) >= RotationThreshold
                ? MathF.Exp(-spin)
                : 1f - spin;

            body.AngularVelocity = (
                body.AngularVelocity.X * factor,
                body.AngularVelocity.Y * factor,
                body.AngularVelocity.Z * factor);

            // **The linear half has its own threshold and tests the product itself.** 0.25 against
            // the rotational 0.5, and one number for both would be wrong on either side of it.
            float speed = body.Damping * delta;

            float scale = speed >= SpeedThreshold ? MathF.Exp(-speed) : 1f - speed;

            body.Velocity = (
                body.Velocity.X * scale,
                body.Velocity.Y * scale,
                body.Velocity.Z * scale);
        }
    }

    /// <summary><c>DAT_1800ea984</c>, tested against the sum of the three squared products.</summary>
    private const float RotationThreshold = 0.5f;

    /// <summary><c>DAT_1800efdf8</c>, a double, tested against the single product.</summary>
    private const float SpeedThreshold = 0.25f;
}
