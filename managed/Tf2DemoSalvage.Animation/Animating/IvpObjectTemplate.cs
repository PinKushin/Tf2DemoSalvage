using System;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// What vphysics hands IVP to create a polygon object, and the core mass and rotational inertia IVP derives
/// from it (B403).
/// </summary>
/// <remarks>
/// **Read from the disassembly** (`docs/findings/51`, *An IVP object's core sits at the hull's mass center*):
/// `FUN_18001c9d0` fills the template from `objectparams_t`, and the object initializer `FUN_180073df0`
/// turns the template and the hull into the core's mass and per-axis inertia.
///
/// **Only the branch vphysics takes is carried.** `FUN_18001c9d0` always writes `1` to the template's
/// `+0x28`, which tells the initializer the three inertia values are FACTORS on the hull's own; the other
/// branch, where they are absolute, is never reached from a `.phy`.
/// </remarks>
/// <param name="Mass">The mass, <c>+0x20</c>.</param>
/// <param name="InertiaFactor">The per-axis inertia factor, <c>+0x30/+0x34/+0x38</c>.</param>
/// <param name="RotationInertiaLimit">The inertia floor as a fraction of the inertia's length, <c>+0x40</c>.</param>
/// <param name="Damping">Linear damping, <c>+0x48</c>.</param>
/// <param name="RotationDamping">Rotational damping, <c>+0x50/+0x58/+0x60</c>.</param>
public readonly record struct IvpObjectTemplate(
    double Mass,
    (float X, float Y, float Z) InertiaFactor,
    float RotationInertiaLimit,
    double Damping,
    double RotationDamping)
{
    private const float LightestMass = 0.1f;
    private const float HeaviestMass = 50000f;
    private const float DefaultInertia = 1f;
    private const float LargestInertia = 1e18f;

    /// <summary><c>DAT_1800fb100</c>, below which the core's mass is taken as one.</summary>
    private const double NegligibleMass = 1e-8d;

    /// <summary><c>DAT_1800ea9b8</c>.</summary>
    private const double One = 1d;

    /// <summary>Fills the template from a solid's parameters — <c>FUN_18001c9d0</c>.</summary>
    /// <param name="mass"><c>objectparams_t::mass</c>.</param>
    /// <param name="inertia"><c>objectparams_t::inertia</c>, a scale.</param>
    /// <param name="damping"><c>objectparams_t::damping</c>.</param>
    /// <param name="rotationDamping"><c>objectparams_t::rotdamping</c>.</param>
    /// <param name="rotationInertiaLimit"><c>objectparams_t::rotInertiaLimit</c>.</param>
    /// <returns>The template.</returns>
    /// <remarks>
    /// **The mass is `MINSS(MAXSS(mass, 0.1f), 50000f)` in float**, and `MAXSS` answers its second operand when
    /// either is NaN, so NaN becomes the lightest mass. **The inertia scale is kept only when `COMISS` finds it
    /// above zero** — NaN is not — and is then held at `1e18`.
    /// </remarks>
    public static IvpObjectTemplate FromParameters(
        float mass, float inertia, float damping, float rotationDamping, float rotationInertiaLimit)
    {
        float clampedMass = Smaller(Larger(mass, LightestMass), HeaviestMass);
        float factor = Smaller(inertia > 0f ? inertia : DefaultInertia, LargestInertia);

        return new IvpObjectTemplate(
            Mass: clampedMass,
            InertiaFactor: (factor, factor, factor),
            RotationInertiaLimit: rotationInertiaLimit,
            Damping: damping,
            RotationDamping: rotationDamping);
    }

    /// <summary>The core's mass and per-axis rotational inertia for a hull — the inertia block of <c>FUN_180073df0</c>.</summary>
    /// <param name="hullInertia">The hull's own rotational inertia per unit mass, as the surface manager's virtual <c>+0x18</c> gives it.</param>
    /// <returns>The mass stored at <c>core+0x2c</c> and the inertia at <c>core+0x20/+0x24/+0x28</c>.</returns>
    /// <remarks>
    /// Each axis is `(float)((double)(hull × factor) × mass)`, the product taken in float first. **The floor is
    /// the limit times the inertia's LENGTH** — `FUN_18006e120` is `sqrt` of the float sum of squares — and
    /// raises only an axis below it. A limit of zero skips it, and so does NaN, because `UCOMISS` sets the zero
    /// flag on an unordered compare.
    /// </remarks>
    public (float Mass, (float X, float Y, float Z) Inertia) CoreInertia((float X, float Y, float Z) hullInertia)
    {
        double mass = Mass;

        if (mass < NegligibleMass || double.IsNaN(mass))
        {
            mass = One;
        }

        float x = (float)((double)(hullInertia.X * InertiaFactor.X) * mass);
        float y = (float)((double)(hullInertia.Y * InertiaFactor.Y) * mass);
        float z = (float)((double)(hullInertia.Z * InertiaFactor.Z) * mass);

        if (RotationInertiaLimit != 0f && !float.IsNaN(RotationInertiaLimit))
        {
            float squared = (x * x) + (y * y) + (z * z);
            float floor = (float)(Math.Sqrt(squared) * RotationInertiaLimit);

            x = floor > x ? floor : x;
            y = floor > y ? floor : y;
            z = floor > z ? floor : z;
        }

        return ((float)mass, (x, y, z));
    }

    /// <summary><c>MAXSS</c>: the first operand only when it is greater, so NaN on either side gives the second.</summary>
    private static float Larger(float value, float bound) => value > bound ? value : bound;

    /// <summary><c>MINSS</c>: the first operand only when it is less, so NaN on either side gives the second.</summary>
    private static float Smaller(float value, float bound) => value < bound ? value : bound;
}
