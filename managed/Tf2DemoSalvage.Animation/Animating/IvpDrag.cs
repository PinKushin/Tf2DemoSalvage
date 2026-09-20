using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>An object's two drag scalars — <c>FUN_18001bc60</c> and <c>FUN_18001bb20</c> (B369, D172).</summary>
/// <remarks>
/// **Read from the disassembly, 2026-09-16.** `CPhysicsObject` keeps a linear basis at `+0x28`, an angular one at `+0x34` and
/// a coefficient for each at `+0x58` and `+0x5c`; this port keeps them on the core, the one object a body is here.
/// </remarks>
public static class IvpDrag
{
    /// <summary><c>DAT_18011f000</c>: <c>0.0254f</c>, inches to metres.</summary>
    private const float MetresPerInch = 0.0254f;

    /// <summary><c>DAT_1800ea984</c>: <c>0.5f</c>.</summary>
    private const float Half = 0.5f;

    /// <summary><c>DAT_1800ed2e4</c>: <c>0.25f</c>.</summary>
    private const float Quarter = 0.25f;

    /// <summary><c>DAT_1800ed2e8</c>: <c>0.33333334f</c>.</summary>
    private const float Third = 0.33333334f;

    /// <summary><c>DAT_18011f004</c>: <c>39.3701f</c>, metres to inches.</summary>
    private const float InchesPerMetre = 39.3701f;

    /// <summary>A collide's box in Source inches, at the origin with no turn — <c>CollideGetAABB</c>, <c>FUN_180028940</c>.</summary>
    /// <param name="surface">The collide's ledge tree.</param>
    /// <returns>The box.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="surface"/> is null.</exception>
    /// <remarks>
    /// **Read from the disassembly, 2026-09-16.** A single-ledge surface takes one support point along each Source axis
    /// (`FUN_18002b4d0`, a hill climb to the ledge's extreme point, no margin); a tree folds the six supports of every terminal ledge
    /// into a box seeded at `±100000.5f` (`FUN_18002dde0`, `FUN_1800b6d90`). Either way the answer is each axis's extreme point,
    /// carried out through the matrix the query builds (`FUN_1800264e0`), which with no turn is
    /// <code>
    /// source = (39.3701f·p.x, 39.3701f·p.z, −39.3701f·p.y)      -- the IVP point p, in metres
    /// </code>
    /// </remarks>
    public static ((float X, float Y, float Z) Min, (float X, float Y, float Z) Max) CollideBox(PhysicsLedgeTree surface)
    {
        ArgumentNullException.ThrowIfNull(surface);

        (float X, float Y, float Z) min = (float.MaxValue, float.MaxValue, float.MaxValue);
        (float X, float Y, float Z) max = (float.MinValue, float.MinValue, float.MinValue);

        Fold(surface.Root, ref min, ref max);

        return (min, max);

        static void Fold(PhysicsLedgeTreeNode node, ref (float X, float Y, float Z) min, ref (float X, float Y, float Z) max)
        {
            if (!node.IsTerminal)
            {
                Fold(node.Left!, ref min, ref max);
                Fold(node.Right!, ref min, ref max);
                return;
            }

            // Stryker disable once : a mutant that empties the guard body leaves 'ledge'
            // unassigned (CS0165), and Safe Mode then drops every mutation in this method — B410.
            if (node.Ledge is not { } ledge)
            {
                return;
            }

            foreach (Vector3 point in ledge.Points)
            {
                (float x, float y, float z) = (InchesPerMetre * point.X, InchesPerMetre * point.Z, -InchesPerMetre * point.Y);
                min = (MathF.Min(min.X, x), MathF.Min(min.Y, y), MathF.Min(min.Z, z));
                max = (MathF.Max(max.X, x), MathF.Max(max.Y, y), MathF.Max(max.Z, z));
            }
        }
    }

    /// <summary>A core's drag bases from its collide's box and orthographic areas — the arithmetic of <c>FUN_18001d4e0</c>.</summary>
    /// <param name="core">The core, whose inverse mass and inverse inertia are read and whose two bases are written.</param>
    /// <param name="min">The collide's box minimum, <c>CollideGetAABB</c> at the origin with no turn, in Source inches.</param>
    /// <param name="max">Its maximum.</param>
    /// <param name="areas">The collide's <c>CollideGetOrthographicAreas</c>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="core"/> is null.</exception>
    /// <remarks>
    /// **Read from the disassembly, 2026-09-16**, every product in float in the order written:
    /// <code>
    /// e = |(max − min)·0.0254f| per axis
    /// b = ((e.y·e.z)·a.x, (e.x·e.y)·a.y, (e.z·e.x)·a.z);  b = invMass·b
    /// h = e·0.5f;  q = (e·e)·0.25f;  t = 0.33333334f;  I = core+0x40..0x48
    /// ω.x = (((q.y·t)·h.x)·q.x + ((q.y·0.5)·q.y)·h.x + (h.x·q.y)·q.z)·I.x·a.y + (((q.z·t)·h.x)·q.x + ((q.z·0.5)·q.z)·h.x + (h.x·q.z)·q.y)·I.x·a.z
    /// ω.y = (((q.x·t)·h.z)·q.z + ((q.x·0.5)·q.x)·h.z + (h.z·q.x)·q.y)·I.y·a.z + (((q.y·t)·h.z)·q.z + ((q.y·0.5)·q.y)·h.z + (h.z·q.y)·q.x)·I.y·a.x
    /// ω.z = (((q.x·t)·h.y)·q.y + ((q.x·0.5)·q.x)·h.y + (q.x·h.y)·q.z)·I.z·a.y + (((q.z·t)·h.y)·q.y + ((q.z·0.5)·q.z)·h.y + (h.y·q.z)·q.x)·I.z·a.x
    /// </code>
    /// each three-term sum left to right. *The routine runs only for an object that is not static and has a collide; the builder
    /// seeds both bases with zero first (`FUN_18001c4c0`).*
    /// </remarks>
    public static void ComputeBasis(
        IvpRigidBody core, (float X, float Y, float Z) min, (float X, float Y, float Z) max, (float X, float Y, float Z) areas)
    {
        ArgumentNullException.ThrowIfNull(core);

        float ex = MathF.Abs((max.X - min.X) * MetresPerInch);
        float ey = MathF.Abs((max.Y - min.Y) * MetresPerInch);
        float ez = MathF.Abs((max.Z - min.Z) * MetresPerInch);
        float inverseMass = core.InverseMass;

        core.DragBasis = (
            inverseMass * ((ey * ez) * areas.X),
            inverseMass * ((ex * ey) * areas.Y),
            inverseMass * ((ez * ex) * areas.Z));

        float hx = ex * Half;
        float hy = ey * Half;
        float hz = ez * Half;
        float qx = (ex * ex) * Quarter;
        float qy = (ey * ey) * Quarter;
        float qz = (ez * ez) * Quarter;
        (float ix, float iy, float iz) = core.InverseInertia;

        core.AngularDragBasis = (
            (Lane(qy, qx, hx, (hx * qy) * qz) * ix * areas.Y) + (Lane(qz, qx, hx, (hx * qz) * qy) * ix * areas.Z),
            (Lane(qx, qz, hz, (hz * qx) * qy) * iy * areas.Z) + (Lane(qy, qz, hz, (hz * qy) * qx) * iy * areas.X),
            (Lane(qx, qy, hy, (qx * hy) * qz) * iz * areas.Y) + (Lane(qz, qy, hy, (hy * qz) * qx) * iz * areas.X));

        // (((q·t)·h)·p + ((q·0.5)·q)·h) + last
        static float Lane(float q, float p, float h, float last) => ((((q * Third) * h) * p) + (((q * Half) * q) * h)) + last;
    }

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
