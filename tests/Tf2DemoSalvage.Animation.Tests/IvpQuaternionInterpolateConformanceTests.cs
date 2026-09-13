using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// A rotation part-way through a step, as IVP computes it — <c>FUN_180071060</c> (B369).
/// </summary>
/// <remarks>
/// **Read from the disassembly**, because the decompiler dropped both `sin` arguments —
/// `docs/findings/51`, *`interpolate` is `FUN_180071060`*:
///
/// <code>
///   dot  = from · to;  sign = dot > 0 ? +1 : (dot = −dot, −1)
///   dot ≥ 0.999:  out = from + (sign·to − from)·t, renormalised by two linearised steps
///   otherwise:    out = sin((1 − t)θ)/sin θ · from + sign · sin(tθ)/sin θ · to,   θ = acos(dot)
/// </code>
///
/// **It is the first layer of B369's port.** IVP finds the moment a hull vertex reaches a face by
/// evaluating both bodies at lattice times inside the step (`FUN_1800734e0`), and every one of those
/// transforms rotates through this.
///
/// **What is deliberately NOT tested, and why: the 0.999 cut-over and the renormalisation.** Above the
/// cut, lerp and slerp differ by at most `8e-12` and the linearised renormalisation differs from the
/// textbook one by `9e-17` — measured by running the read sequence in doubles at dots of 0.9991,
/// 0.99991 and 0.99999. A float cannot see either, so a test claiming to pin the branch could not
/// fail. The cases below are the ones a wrong implementation actually changes.
/// </remarks>
public sealed class IvpQuaternionInterpolateConformanceTests
{
    private const float Tolerance = 1e-6f;

    /// <summary>Ninety degrees about Z: <c>(0, 0, sin 45°, cos 45°)</c>.</summary>
    private static readonly (float X, float Y, float Z, float W) QuarterTurnAboutZ =
        (0f, 0f, 0.70710677f, 0.70710677f);

    private static readonly (float X, float Y, float Z, float W) Identity = (0f, 0f, 0f, 1f);

    /// <remarks>
    /// **A quarter of the way, not half, because at a half a normalised lerp and a slerp coincide by
    /// symmetry** — a midpoint test cannot tell them apart. A quarter of 90° is 22.5°, which is
    /// `(0, 0, sin 11.25°, cos 11.25°)` = `(0, 0, 0.19509032, 0.98078528)`; a normalised lerp lands at
    /// about `0.1875` instead, far outside the tolerance.
    /// </remarks>
    [Test]
    public void Interpolate_AQuarterOfAQuarterTurn_IsTheSlerpNotANormalisedLerp()
    {
        (float X, float Y, float Z, float W) at = IvpQuaternion.Interpolate(Identity, QuarterTurnAboutZ, 0.25f);

        at.X.ShouldBe(0f, Tolerance);
        at.Y.ShouldBe(0f, Tolerance);
        at.Z.ShouldBe(0.19509032f, Tolerance);
        at.W.ShouldBe(0.98078528f, Tolerance);
    }

    /// <remarks>
    /// **The short path.** `(0, 0, 0, −1)` is the same rotation as the identity, and the dot is `−1`;
    /// the engine negates the dot and flips the target's sign, which puts both ends at the identity, so
    /// every point between them is the identity too. Without the flip the lerp would pass through zero.
    /// </remarks>
    [Test]
    public void Interpolate_TowardTheNegatedIdentity_StaysAtTheIdentity()
    {
        (float X, float Y, float Z, float W) at = IvpQuaternion.Interpolate(Identity, (0f, 0f, 0f, -1f), 0.5f);

        at.X.ShouldBe(0f, Tolerance);
        at.Y.ShouldBe(0f, Tolerance);
        at.Z.ShouldBe(0f, Tolerance);
        at.W.ShouldBe(1f, Tolerance);
    }

    /// <remarks>
    /// **The sign reaches the slerp branch too.** `(0, 0, −sin 45°, −cos 45°)` is the quarter turn
    /// written the long way round; the dot is `−0.7071`, so the engine flips it and slerps toward the
    /// negated target. At a fraction of one the result is therefore the quarter turn with POSITIVE
    /// components — not the target as given.
    /// </remarks>
    [Test]
    public void Interpolate_AtFractionOneTowardANegatedTarget_ReturnsTheTargetFlipped()
    {
        (float X, float Y, float Z, float W) at =
            IvpQuaternion.Interpolate(Identity, (0f, 0f, -0.70710677f, -0.70710677f), 1f);

        at.Z.ShouldBe(0.70710677f, Tolerance);
        at.W.ShouldBe(0.70710677f, Tolerance);
    }

    /// <remarks>
    /// `sin(θ)/sin θ` weights the start fully and `sin(0)` weights the target not at all.
    /// </remarks>
    [Test]
    public void Interpolate_AtFractionZero_ReturnsTheStart()
    {
        (float X, float Y, float Z, float W) at = IvpQuaternion.Interpolate(QuarterTurnAboutZ, Identity, 0f);

        at.Z.ShouldBe(0.70710677f, Tolerance);
        at.W.ShouldBe(0.70710677f, Tolerance);
    }
}
