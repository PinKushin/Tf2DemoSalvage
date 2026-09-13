using System;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// IVP's collision thresholds, every one of them a multiple of one quarter-inch tolerance (B369).
/// </summary>
/// <remarks>
/// **Read from `vphysics.dll`, with every multiplier dumped** — `docs/findings/51`, *The block is linear
/// in one collision tolerance `d`, plus gravity `g`*. The environment constructor, `FUN_1800114f0`,
/// calls the block's initialiser with `(DAT_18011f008 − DAT_1800eb144) × DAT_18011f000`, which dumps as
/// `(0.25 − 1e-4) × 0.0254` metres. `CPhysicsEnvironment::SetGravity` re-derives the block whenever
/// gravity changes and logs the tolerance as `"0.250 tolerance"`.
///
/// **What these are FOR is the opposite of what this project's `IvpContact.Slop` does, and that is the
/// point of carrying them.** IVP's pair scheduler keeps a pair at least <see cref="Margin"/> apart and
/// schedules its next look no later than `(distance − ε) / speedBound`; ours lets a pair penetrate
/// by 0.25 before pushing back. The number coincides and the question does not. These constants are
/// the engine's, pinned before the scheduler that reads them is written.
/// </remarks>
public static class IvpCollisionTolerance
{
    /// <summary><c>DAT_18011f008</c>, dumped: the preset tolerance, in inches.</summary>
    private const float Preset = 0.25f;

    /// <summary><c>DAT_1800eb144</c>, dumped: taken off the preset before the conversion.</summary>
    private const float Trim = 1e-4f;

    /// <summary><c>0.1</c>, <c>DAT_1800fd578</c>: the recheck's epsilon as a share of the tolerance.</summary>
    private const float EpsilonShare = 0.1f;

    /// <summary>
    /// <c>2 × (2.3 − 1.0)</c>: <c>block[0x45] = √(2 · (block[0x44] − block[1]) · g)</c> with
    /// <c>block[0x44] = 2.3·d</c> and <c>block[1] = d</c>.
    /// </summary>
    private const float FallHeights = 2.6f;

    /// <summary>The tolerance <c>d</c>, in metres — <c>(0.25 − 1e-4) × 0.0254</c>.</summary>
    public const float Metres = (Preset - Trim) * IvpTransform.MetresPerInch;

    /// <summary>
    /// The collision margin, <c>block[1] = 0.1·d + 0.9·d = d</c>, back in Source units.
    /// </summary>
    /// <remarks>
    /// Through <see cref="IvpTransform.InchesPerMetre"/> rather than a reciprocal, because that is the
    /// constant `SetGravity` uses for the same conversion, so the round trip does not return 0.2499.
    /// </remarks>
    public const float Margin = Metres * IvpTransform.InchesPerMetre;

    /// <summary>
    /// <c>block[0] = 0.1·d</c>, in Source units: taken off a pair's distance before the recheck divides
    /// it by the speed bound.
    /// </summary>
    public const float Epsilon = EpsilonShare * Margin;

    /// <summary><c>0.1</c>: <c>block[0x49]</c> as a share of the tolerance.</summary>
    private const float EdgeTargetShare = 0.1f;

    /// <summary>
    /// <c>block[0x49] = 0.1·d</c>, <c>DAT_18012d664</c>, in Source units: the factor on the face core's
    /// <c>+0x54</c> in the vertex-face search's edge target.
    /// </summary>
    public const float EdgeTargetScale = EdgeTargetShare * Margin;

    /// <summary><c>block[1]</c>, where the margin ramp starts: <c>0.1·d + 0.9·d</c>.</summary>
    private const float RampStart = Margin;

    /// <summary><c>block[0x42]</c>, where the margin ramp ends — also <c>d</c>.</summary>
    private const float RampEnd = Margin;

    /// <summary>How many margins the ramp holds, <c>block[2]</c> through <c>block[0x41]</c>.</summary>
    private const int RampEntries = 64;

    /// <summary>The margin for a class — <c>DAT_18012d548[class]</c>, which is <c>block[2 + class]</c>.</summary>
    /// <param name="marginClass">The mindist's byte at bits 22–29 of its <c>+0x20</c>.</param>
    /// <returns>The margin, in Source units.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="marginClass"/> is off the 64-entry ramp.</exception>
    /// <remarks>
    /// **`block[1] + (block[0x42] − block[1]) · class / 64`, and both ends are `d`**, so the whole ramp is the
    /// margin. The engine indexes by a byte and past 63 reads the block's later fields; what sets the byte is
    /// not read, so a class off the ramp is refused rather than answered.
    /// </remarks>
    public static float MarginFor(int marginClass)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(marginClass);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(marginClass, RampEntries);

        return RampStart + ((RampEnd - RampStart) * marginClass / RampEntries);
    }

    /// <summary>The closing speed below which the pair scheduler leaves a pair alone.</summary>
    /// <param name="gravity">Gravity's magnitude in Source units per second squared.</param>
    /// <returns>The threshold in Source units per second.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="gravity"/> is negative.</exception>
    /// <remarks>
    /// **`√(2.6 · d · g)`, the speed of a fall through `1.3·d`, with `g` in metres** — `SetGravity`
    /// multiplies gravity by `0.0254` before the block is derived, so the conversion happens there
    /// and the result comes back through <see cref="IvpTransform.InchesPerMetre"/>. Read against
    /// `DAT_18012d654` in `FUN_180099380`.
    /// </remarks>
    public static float ClosingSpeedThreshold(float gravity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(gravity);

        float metresPerSecondSquared = gravity * IvpTransform.MetresPerInch;

        return MathF.Sqrt(FallHeights * Metres * metresPerSecondSquared) * IvpTransform.InchesPerMetre;
    }
}
