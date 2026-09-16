using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>An object's two drag scalars — <c>FUN_18001bc60</c> and <c>FUN_18001bb20</c> (B369, D172).</summary>
/// <remarks>
/// **Read from the disassembly, 2026-09-16.** `CPhysicsObject` keeps a linear basis at `+0x28`, an angular one at `+0x34` and
/// a coefficient for each at `+0x58` and `+0x5c`; this port keeps them on the core, the one object a body is here.
/// </remarks>
public static class IvpDrag
{
    /// <summary>The linear drag for a velocity — <c>FUN_18001bc60</c>.</summary>
    /// <param name="core">The core.</param>
    /// <param name="velocity">A world velocity.</param>
    /// <returns>The drag scalar.</returns>
    /// <remarks>
    /// <code>
    /// l = FUN_180070620(core+0x90, v)          -- into the core's axes
    /// (|l.x·b.x|·c + |l.y·b.y|) + |l.z·b.z|    -- float; the coefficient on the X term alone
    /// </code>
    /// </remarks>
    public static float Linear(IvpRigidBody core, (float X, float Y, float Z) velocity)
    {
        ArgumentNullException.ThrowIfNull(core);

        (float x, float y, float z) = core.CoreMatrix.RotateInverseNarrowed(velocity);
        (float bx, float by, float bz) = core.DragBasis;

        return ((MathF.Abs(x * bx) * core.DragCoefficient) + MathF.Abs(y * by)) + MathF.Abs(z * bz);
    }

    /// <summary>The angular drag for a spin — <c>FUN_18001bb20</c>.</summary>
    /// <param name="core">The core.</param>
    /// <param name="spin">A spin, in the core's axes as <c>core+0x130</c> holds it.</param>
    /// <returns>The drag scalar.</returns>
    /// <remarks>
    /// <code>
    /// (|b.y·w.y| + |b.x·w.x|·c) + |b.z·w.z|    -- float, no turn; the coefficient on the X term alone
    /// </code>
    /// </remarks>
    public static float Angular(IvpRigidBody core, (float X, float Y, float Z) spin)
    {
        ArgumentNullException.ThrowIfNull(core);

        (float bx, float by, float bz) = core.AngularDragBasis;

        return (MathF.Abs(by * spin.Y) + (MathF.Abs(bx * spin.X) * core.AngularDragCoefficient)) + MathF.Abs(bz * spin.Z);
    }
}

/// <summary>
/// The environment's air drag — the controller <c>CPhysicsEnvironment</c>'s constructor keeps at <c>env+0x10</c>, vtable
/// <c>1800ebfe8</c>, whose slot 4 is <c>FUN_180016370</c> and slot 5 answers <c>500</c> (B369, D172).
/// </summary>
/// <remarks>
/// **Read from the disassembly, 2026-09-16.** A core is filed under it by `EnableDrag` (`FUN_18001b9c0`), which the object
/// builder `FUN_18001b340` calls for an object that is not static and whose `dragCoefficient` is not zero.
/// </remarks>
public sealed class IvpDragController : IIvpUnitController
{
    /// <summary>The controller's priority, slot 5's constant.</summary>
    public const int DragPriority = 500;

    /// <summary><c>DAT_1800ea9f0</c>: <c>−0.5f</c>, the linear drag's share.</summary>
    private const float LinearShare = -0.5f;

    /// <summary><c>DAT_1800ea9f8</c>: <c>−1f</c>, the floor on either factor.</summary>
    private const float Floor = -1f;

    /// <summary>The air density — <c>+0x8</c>, <c>2.0f</c> from the constructor, <c>SetAirDensity</c>'s to change.</summary>
    public float AirDensity { get; set; } = 2f;

    /// <inheritdoc/>
    public int Priority => DragPriority;

    /// <inheritdoc/>
    /// <remarks>
    /// <code>
    /// every core, last first:
    ///     k = (event[0]·ρ)·(FUN_18001bc60(v)·−0.5f);  k = MAXSS(k, −1f)
    ///     k &lt; 0 (COMISS/JNC):  each lane v = (float)((double)v·(double)k) + v
    ///     k = −((FUN_18001bb20(ω)·ρ)·event[0]);  the same floor, test and update on ω
    /// </code>
    /// **`MAXSS` answers its second operand when either is a NaN**, so a NaN factor is the floor and stops the lane.
    /// </remarks>
    public void Advance(IvpSimulationUnit unit, IReadOnlyList<IvpRigidBody> cores, float psiStep)
    {
        ArgumentNullException.ThrowIfNull(cores);

        for (int index = cores.Count - 1; index >= 0; index--)
        {
            IvpRigidBody core = cores[index];

            float linear = Maxss((psiStep * AirDensity) * (IvpDrag.Linear(core, core.Velocity) * LinearShare));

            if (linear < 0f)
            {
                core.Velocity = Slow(core.Velocity, linear);
            }

            float angular = Maxss(-((IvpDrag.Angular(core, core.AngularVelocity) * AirDensity) * psiStep));

            if (angular < 0f)
            {
                core.AngularVelocity = Slow(core.AngularVelocity, angular);
            }
        }
    }

    /// <summary><c>MAXSS factor, −1f</c>: the factor when it is greater, else the floor — a NaN included.</summary>
    private static float Maxss(float factor) => factor > Floor ? factor : Floor;

    private static (float X, float Y, float Z) Slow((float X, float Y, float Z) lanes, float factor) =>
        ((float)((double)lanes.X * factor) + lanes.X,
         (float)((double)lanes.Y * factor) + lanes.Y,
         (float)((double)lanes.Z * factor) + lanes.Z);
}
