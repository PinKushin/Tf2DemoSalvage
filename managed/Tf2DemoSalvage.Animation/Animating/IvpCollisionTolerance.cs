using System;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// IVP's collision thresholds, every one of them derived from one quarter-inch tolerance (B369).
/// </summary>
/// <remarks>
/// **Read from `vphysics.dll`, instruction by instruction** — `docs/findings/51`, *The block is linear in one collision
/// tolerance `d`, plus gravity `g`*, and *A correction first* under *The impact solver and the friction system's
/// bookkeeping*. `FUN_180098fd0` fills a block of floats at `18012d540` from a double `d` and a double `g`, and three
/// callers run it; the last decides what a running environment reads:
///
/// - `FUN_180002540`, at load, with `0.01` and `9.81`;
/// - the environment constructor `FUN_1800114f0`, with `d = (0.25f − 1e-4f) × 0.0254f` widened and `9.81`;
/// - `CPhysicsEnvironment::SetGravity` (`FUN_1800150f0`), which the client calls once at physics init
///   (`game/client/physics.cpp:177`), with `d` = the block's own `[1]` widened (`FUN_180082250`) and `g` = the
///   converted gravity's length (`FUN_18006fc60`).
///
/// **So the block a simulation reads is the derivation run twice, the second time on its own margin**, and every field
/// here is taken from that second run, in metres as the engine computes it — no conversion, because this project's
/// callers of this class now run in metres too.
///
/// **What these are FOR is the opposite of what this project's `IvpContact.Slop` does, and that is the point of carrying
/// them.** IVP keeps a pair at least <see cref="Margin"/> apart and schedules its next look no later than
/// `(distance − ε) / speedBound`; `IvpContact` lets a pair penetrate by 0.25 before pushing back.
/// </remarks>
public static class IvpCollisionTolerance
{
    /// <summary><c>DAT_18011f008</c>, dumped: the preset tolerance, in inches.</summary>
    private const float Preset = 0.25f;

    /// <summary><c>DAT_1800eb144</c>, dumped: taken off the preset before the conversion.</summary>
    private const float Trim = 1e-4f;

    /// <summary><c>DAT_1800fd578</c>: <c>0.1f</c> widened, <c>block[0]</c>'s share of <c>d</c>.</summary>
    private const double EpsilonShare = 0.1f;

    /// <summary><c>DAT_1800fd588</c>: <c>0.9f</c> widened, added to <c>block[0]</c> for <c>block[1]</c>.</summary>
    private const double MarginShare = 0.9f;

    /// <summary><c>DAT_1800fdf70</c>: <c>1/64</c>, the margin ramp's step.</summary>
    private const double RampStep = 0.015625d;

    /// <summary>How many margins the ramp holds, <c>block[2]</c> through <c>block[0x41]</c>.</summary>
    private const int RampEntries = 64;

    /// <summary><c>DAT_1800fd580</c>: <c>0.3f</c> widened, added to <c>block[0x43]</c> for <c>block[0x44]</c>.</summary>
    private const double ParallelEdgeShare = 0.3f;

    /// <summary><c>DAT_1800fcfb0</c>: <c>20.0</c>, <c>block[0x48]</c>'s share of <c>d</c> over <c>block[0x43]</c>.</summary>
    private const double EstimateShare = 20d;

    /// <summary><c>DAT_1800ea968</c>: <c>0.1f</c>, a float, <c>block[0x49]</c>'s share of <c>block[1]</c>.</summary>
    private const float EdgeTargetShare = 0.1f;

    /// <summary>The tolerance <c>d</c> the environment constructor passes, in metres — <c>(0.25f − 1e-4f) × 0.0254f</c>.</summary>
    /// <remarks>Both operations in float, as `FUN_1800114f0` takes them (`SUBSS`, `MULSS`), before the widening for the call.</remarks>
    public static readonly float Metres = (Preset - Trim) * IvpTransform.MetresPerInch;

    /// <summary>The block as <c>SetGravity</c> leaves it: the derivation run again on the constructor's own margin.</summary>
    private static readonly Block Settled = Block.Derive(Block.Derive(Metres).MarginMetres);

    /// <summary><c>block[1]</c>, <c>DAT_18012d544</c>, in metres: the margin the push-out estimate measures a gap against.</summary>
    public static readonly float Margin = Settled.MarginMetres;

    /// <summary><c>block[0x42]</c>, in metres: the margin ramp's far end, where the impact loop's search for the closest contact starts.</summary>
    public static readonly float RampEnd = Settled.RampEndMetres;

    /// <summary>
    /// <c>block[0]</c>, in metres: taken off a pair's distance before the recheck divides it by the speed bound.
    /// </summary>
    public static readonly float Epsilon = Settled.EpsilonMetres;

    /// <summary>
    /// <c>block[0x49]</c>, <c>DAT_18012d664</c>, in metres: the factor on the face core's <c>+0x54</c> in the
    /// vertex-face search's edge target.
    /// </summary>
    public static readonly float EdgeTargetScale = Settled.EdgeTargetScaleMetres;

    /// <summary><c>block[0x43]</c>, <c>DAT_18012d64c</c>, in metres: the gap a new contact point starts with.</summary>
    /// <remarks>
    /// `(float)((double)block[0x42] + d)` — the margin ramp's far end plus one tolerance. Four other routines read it
    /// (`FUN_180084490`, `FUN_1800a9520`, `FUN_1800a9bf0`, `FUN_1800b28a0`); none is ported.
    /// </remarks>
    public static readonly float ContactGap = Settled.ContactGapMetres;

    /// <summary>
    /// <c>block[0x44]</c>, <c>DAT_18012d650</c>, in metres: the gap the edge–edge measure gives two edges with no
    /// crossing.
    /// </summary>
    /// <remarks>
    /// `(float)(d · 0.3f + (double)block[0x43])`. The closing-speed threshold is the speed of a fall from this height to the
    /// margin — <see cref="ClosingSpeedThreshold"/>.
    /// </remarks>
    public static readonly float ParallelEdgeGap = Settled.ParallelEdgeGapMetres;

    /// <summary><c>block[0x4a]</c>, <c>DAT_18012d668</c>, in metres: <c>(float)(d + d)</c>.</summary>
    /// <remarks>Its one reader so far is the impact solver `FUN_18008e290`, which adds it to a speed in IVP's own units —
    /// `(p5 + block[0x4a]) · 1.2f` — and <see cref="IvpImpactSolver"/> runs in those units.</remarks>
    public static readonly float TwiceTolerance = Settled.DoubledToleranceMetres;

    /// <summary><c>block[0x48]</c>, <c>DAT_18012d660</c>, in metres: <c>(float)(d·20 + (double)block[0x43])</c>, which is <c>22·d</c>.</summary>
    /// <remarks>The gap past which <see cref="IvpContactPoint.Estimate"/> gives a record no estimate.</remarks>
    public static readonly float EstimateGap = Settled.EstimateLimitMetres;

    /// <summary>The margin for a class — <c>DAT_18012d548[class]</c>, which is <c>block[2 + class]</c>.</summary>
    /// <param name="marginClass">The mindist's byte at bits 22–29 of its <c>+0x20</c>.</param>
    /// <returns>The margin, in metres.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="marginClass"/> is off the 64-entry ramp.</exception>
    /// <remarks>
    /// **`(float)(((double)(block[0x42] − block[1]) · class) · (1/64) + (double)block[1])`, and both ends are the margin**,
    /// so the whole ramp is the margin. The engine indexes by a byte and past 63 reads the block's later fields; what sets
    /// the byte is not read, so a class off the ramp is refused rather than answered.
    /// </remarks>
    public static float MarginFor(int marginClass)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(marginClass);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(marginClass, RampEntries);

        float span = Settled.RampEndMetres - Settled.MarginMetres;
        double margin = (span * (double)marginClass * RampStep) + Settled.MarginMetres;

        return (float)margin;
    }

    /// <summary>The closing speed below which the pair scheduler leaves a pair alone — <c>block[0x45]</c>.</summary>
    /// <param name="gravityLength">
    /// The IVP gravity's LENGTH, in metres per second squared — what `FUN_18006fc60` measures after `SetGravity` converts
    /// each Source component with `* 0.0254f` in float.
    /// </param>
    /// <returns>The threshold in metres per second.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="gravityLength"/> is negative.</exception>
    /// <remarks>
    /// **`(float)√((double)((block[0x44] − block[1]) + (block[0x44] − block[1])) · g)`, with `g` the length of the gravity
    /// `SetGravity` converted.** Read against `DAT_18012d654` in `FUN_180099380`.
    /// </remarks>
    public static float ClosingSpeedThreshold(double gravityLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(gravityLength);

        float fall = Settled.ParallelEdgeGapMetres - Settled.MarginMetres;

        return (float)Math.Sqrt((fall + fall) * gravityLength);
    }

    /// <summary>The block's fields this project reads, in metres, as one run of <c>FUN_180098fd0</c> leaves them.</summary>
    private readonly record struct Block(
        float EpsilonMetres,
        float MarginMetres,
        float RampEndMetres,
        float ContactGapMetres,
        float ParallelEdgeGapMetres,
        float EdgeTargetScaleMetres,
        float DoubledToleranceMetres,
        float EstimateLimitMetres)
    {
        /// <summary>One run of <c>FUN_180098fd0</c> on a tolerance.</summary>
        /// <param name="tolerance">The tolerance <c>d</c>, in metres, as the double the routine takes.</param>
        /// <returns>The block.</returns>
        /// <remarks>
        /// **`block[1]` is stored twice**, at `+0x4` and as the ramp's far end at `+0x108`, from one register.
        /// </remarks>
        public static Block Derive(double tolerance)
        {
            float epsilon = (float)(tolerance * EpsilonShare);
            float margin = (float)((tolerance * MarginShare) + epsilon);
            float contactGap = (float)(margin + tolerance);
            float parallelEdgeGap = (float)((tolerance * ParallelEdgeShare) + contactGap);

            return new Block(
                epsilon,
                margin,
                margin,
                contactGap,
                parallelEdgeGap,
                margin * EdgeTargetShare,
                (float)(tolerance + tolerance),
                (float)((tolerance * EstimateShare) + contactGap));
        }
    }
}
