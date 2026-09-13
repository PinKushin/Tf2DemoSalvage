using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// A core's rotation bounds for the step just integrated — <c>FUN_180099d60</c> (B369).
/// </summary>
/// <remarks>
/// **Read from the disassembly** (`D:\ghidra-proj\out\core_54_80_candidates.log`). The integrator `FUN_180099a00`
/// hands it the core and the step's rotation quaternion from `FUN_180099fc0`, and it reads the quaternion's vector
/// part `v`:
///
/// <code>
///   if |v|² ≤ 1e-19 (COMISD/JBE, NaN included):  axis = (1, 0, 0), x = 0
///   else  r = five Newton steps of 1/√|v|² from the bit-built guess;  x = (float)(r·|v|²)
///         axis[i] = (float)((row_i of core+0x90) · v × r)
///   core+0x1c0 = axis
///   core+0x80  = ((2x + x³·(1/3)) + 2·(0.40414·x⁵)) × core+0x1d8      -- all float
///   core+0x254 = core+0x80 × core+0x8
/// </code>
///
/// The vector part of a turn by `θ` has length `sin(θ/2)`, and `2x + x³/3` begins the series of `2·asin x`;
/// the `x⁵` coefficient is larger than that series', so the bound sits above the angle. *That this is its purpose
/// is INFERRED from the arithmetic.*
/// </remarks>
public sealed class IvpCoreSpeedBoundConformanceTests
{
    private static readonly IvpMatrix Identity = IvpMatrix.FromRotation((0f, 0f, 0f, 1f), (0d, 0d, 0d));

    /// <remarks>
    /// **A core that did not turn has no bound and the X axis.** `|v|²` of zero is under `1e-19`, so the branch
    /// skips the root and stores `(1, 0, 0)` with a bound of zero.
    /// </remarks>
    [Test]
    public void From_ARotationTooSmallToHaveAnAxis_IsZeroAboutX()
    {
        IvpCoreSpeedBound bound = IvpCoreSpeedBound.From((0d, 0d, 0d), Identity, inverseStep: 66f, surfaceRadius: 2f);

        bound.Angular.ShouldBe(0f);
        bound.Surface.ShouldBe(0f);
        bound.Axis.ShouldBe((1f, 0f, 0f));
    }

    /// <remarks>
    /// **The x⁵ coefficient is `2 × 0.40414`**, dumped as the float `0.40414`. At `x = 0.5` the bound over a unit step
    /// is `1 + 0.125/3 + 0.80828/32` = `1.0669254`; the arcsine series' own `3/20` would give `1.0463542`, and
    /// dropping the term `1.0416667`.
    /// </remarks>
    [Test]
    public void From_AHalfLengthVectorPart_BoundsByTheEnginesSeries()
    {
        IvpCoreSpeedBound bound = IvpCoreSpeedBound.From((0.5d, 0d, 0d), Identity, inverseStep: 1f, surfaceRadius: 1f);

        bound.Angular.ShouldBe(1.0669254f, 1e-6f);
    }

    /// <remarks>**The bound is per second**: the per-step series times the inverse step, here sixty-six.</remarks>
    [Test]
    public void From_AnInverseStep_ScalesTheBound()
    {
        IvpCoreSpeedBound bound = IvpCoreSpeedBound.From((0.5d, 0d, 0d), Identity, inverseStep: 66f, surfaceRadius: 1f);

        bound.Angular.ShouldBe(1.0669254f * 66f, 1e-4f);
    }

    /// <remarks>
    /// **The surface bound is the angular one times `core+0x8`** — how fast a point that far out can move.
    /// </remarks>
    [Test]
    public void From_ASurfaceRadius_ScalesTheAngularBoundIntoTheSurfaceBound()
    {
        IvpCoreSpeedBound bound = IvpCoreSpeedBound.From((0.5d, 0d, 0d), Identity, inverseStep: 1f, surfaceRadius: 3f);

        bound.Surface.ShouldBe(bound.Angular * 3f);
    }

    /// <remarks>
    /// **The axis is the unit vector part put through the core's matrix, row by row.** A turn about local `+Z` on a
    /// core turned a quarter about X points along world `−Y`; left unrotated it would be `+Z`.
    /// </remarks>
    [Test]
    public void From_ACoreTurnedAQuarterAboutX_PutsTheAxisThroughItsMatrix()
    {
        IvpMatrix quarterAboutX = IvpMatrix.FromRotation((0.70710677f, 0f, 0f, 0.70710677f), (0d, 0d, 0d));

        IvpCoreSpeedBound bound = IvpCoreSpeedBound.From((0d, 0d, 0.25d), quarterAboutX, inverseStep: 1f, surfaceRadius: 1f);

        bound.Axis.X.ShouldBe(0f, 1e-6f);
        bound.Axis.Y.ShouldBe(-1f, 1e-6f);
        bound.Axis.Z.ShouldBe(0f, 1e-6f);
    }

    /// <remarks>**A NaN turn takes the no-axis branch**, because `COMISD` against `1e-19` sets the carry on unordered.</remarks>
    [Test]
    public void From_ANaNRotation_IsZeroAboutX()
    {
        IvpCoreSpeedBound bound =
            IvpCoreSpeedBound.From((double.NaN, 0d, 0d), Identity, inverseStep: 1f, surfaceRadius: 1f);

        bound.Angular.ShouldBe(0f);
        bound.Axis.ShouldBe((1f, 0f, 0f));
    }
}
