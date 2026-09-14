namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// A core's rotation bounds for the step just integrated — what <c>FUN_180099d60</c> writes to <c>core+0x80</c>,
/// <c>core+0x254</c> and <c>core+0x1c0</c> (B369).
/// </summary>
/// <param name="Angular">How fast the core can be turning, per second — <c>core+0x80</c>.</param>
/// <param name="Surface">How fast a point <c>core+0x8</c> out can be moving because of it — <c>core+0x254</c>.</param>
/// <param name="Axis">The step's rotation axis through the core's matrix — <c>core+0x1c0..0x1c8</c>.</param>
/// <remarks>
/// **Read from the disassembly** (`docs/findings/51`, *`FUN_1800a1b50` field by field*). The integrator
/// `FUN_180099a00` calls it every step with the rotation quaternion `FUN_180099fc0` built
/// (<see cref="IvpIntegrator.Rotate(IvpRigidBody, float, int, bool)"/>); the vertex-face search sums two cores' <see cref="Angular"/> for its edge
/// speed, and the pair scheduler scales <see cref="Axis"/> by <see cref="Surface"/>. *That the series bounds the
/// angle `2·asin |v|` is INFERRED from its arithmetic.*
/// </remarks>
public readonly record struct IvpCoreSpeedBound(float Angular, float Surface, (float X, float Y, float Z) Axis)
{
    /// <summary>The Newton steps of the reciprocal root this routine inlines — one more than <c>FUN_18006ecf0</c>'s.</summary>
    private const int InlineNewtonSteps = 5;

    /// <summary><c>DAT_1800ed2e8</c>, the float <c>1/3</c>.</summary>
    private const float Third = 0.33333334f;

    /// <summary><c>DAT_1800fdf88</c>, dumped; doubled before it is added.</summary>
    private const float FifthPowerFactor = 0.40414f;

    /// <summary><c>DAT_1800ea988</c>: the axis a core that did not turn is given.</summary>
    private const float AxisFallback = 1f;

    /// <summary>Bounds a core's turn over one step — <c>FUN_180099d60</c>.</summary>
    /// <param name="rotation">The vector part of the step's rotation quaternion.</param>
    /// <param name="core">The core's matrix at <c>core+0x90</c>.</param>
    /// <param name="inverseStep">The core's inverse step, <c>core+0x1d8</c>.</param>
    /// <param name="surfaceRadius">The core's <c>+0x8</c>, from the surface manager's radius slot.</param>
    /// <returns>The three bounds.</returns>
    /// <remarks>
    /// **`|v|²` at or under `1e-19`, or NaN, takes the no-axis branch** (`COMISD`/`JBE`): the axis is `(1, 0, 0)`
    /// and the bound zero, with no multiplication by the step. Otherwise `x = (float)(r·|v|²)` with `r` five Newton
    /// steps of `1/√|v|²`, each axis component is `(float)(row · v × r)`, and in float
    /// `Angular = ((2x + x³·(1/3)) + 2·((x³·x²)·0.40414)) × inverseStep`. `Surface` is `Angular × surfaceRadius` in
    /// both branches. *Which moment `core+0x90` holds when the integrator calls this is not read.*
    /// </remarks>
    public static IvpCoreSpeedBound From(
        (double X, double Y, double Z) rotation, IvpMatrix core, float inverseStep, float surfaceRadius)
    {
        double squared = (rotation.X * rotation.X) + (rotation.Y * rotation.Y) + (rotation.Z * rotation.Z);

        if (squared <= IvpVector.DirectionThreshold || double.IsNaN(squared))
        {
            return new IvpCoreSpeedBound(0f, 0f * surfaceRadius, (AxisFallback, 0f, 0f));
        }

        double root = IvpVector.ReciprocalSquareRoot(squared, InlineNewtonSteps);
        float length = (float)(root * squared);

        (double X, double Y, double Z) turned = core.Rotate(rotation);
        (float X, float Y, float Z) axis = ((float)(turned.X * root), (float)(turned.Y * root), (float)(turned.Z * root));

        float lengthSquared = length * length;
        float cubed = lengthSquared * length;
        float fifth = cubed * lengthSquared * FifthPowerFactor;
        float angular = ((length + length) + (cubed * Third) + (fifth + fifth)) * inverseStep;

        return new IvpCoreSpeedBound(angular, angular * surfaceRadius, axis);
    }
}
