using System;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// A constraint frame's three axes, in the engine's own permutation (B58, D146).
/// </summary>
/// <remarks>
/// **The order is chosen mechanically, not by index.** `FUN_1800393d0` picks the primary as the
/// axis whose rotation moves the two anchors most — weighted by each body's inverse mass at
/// `core+0x4c` — and then orders the remaining two by how wide their declared range is. Naming the
/// slots rather than indexing them is what stops the three from being quietly shuffled, which is a
/// mistake nothing in the output would report.
/// </remarks>
/// <param name="Primary">The axis the twist is measured about.</param>
/// <param name="Narrower">The swing axis with the smaller declared range.</param>
/// <param name="Wider">The swing axis with the larger.</param>
public readonly record struct IvpConstraintFrame(
    (float X, float Y, float Z) Primary,
    (float X, float Y, float Z) Narrower,
    (float X, float Y, float Z) Wider)
{
    /// <summary>The frame a TF2 ragdoll's REFERENCE side carries — <c>ragdoll_shared.cpp:245</c>.</summary>
    public static IvpConstraintFrame Identity => new((1f, 0f, 0f), (0f, 1f, 0f), (0f, 0f, 1f));
}

/// <summary>
/// One ragdoll joint: two frames, three limits, and the three quantities they clamp (B58, D146).
/// </summary>
/// <remarks>
/// **Only ONE of a joint's three limits is an angle**, which is the single most surprising thing
/// read out of `vphysics.dll` and the reason this type exists rather than three copies of one axis:
/// `FUN_180038620` writes four lanes at `geom+0x100` and two dumped masks pick a different quantity
/// per lane — `−atan2(…)` for the twist, and two DOT PRODUCTS for the other two.
///
/// **The two frames are `constraintToReference` and `constraintToAttached`**, copied into the
/// constraint by `FUN_1800393d0` and rotated per body by `FUN_180038620`. Shipping both is what
/// makes each constraint axis map to the SAME world vector through either body at the bind pose —
/// which is why the bind pose reads `0`, `0`, `1` and why that is a usable control.
///
/// **The rows are stored in the engine's own permutation**, chosen mechanically rather than by
/// index: `FUN_1800393d0` picks the primary as the axis whose rotation moves the two anchors most
/// (weighted by each body's inverse mass at `core+0x4c`), then orders the other two by range. So
/// slot 0 is the primary, slot 1 the narrower swing, slot 2 the wider.
///
/// **What is NOT settled** is the cone limit's sense: its bounds are half the WIDER swing's range in
/// radians and its measure is a cosine, so a few degrees off the bind pose the clamp fires. At the
/// bind pose itself the axis is degenerate and retired, so a settled corpse is not fighting it.
/// `docs/findings/51` carries the arithmetic and what would falsify the reading.
/// </remarks>
public sealed class IvpRagdollConstraint
{
    /// <summary>The reference body's constraint axes — <c>constraintToReference</c>.</summary>
    public required IvpConstraintFrame FrameA { get; init; }

    /// <summary>The attached body's constraint axes — <c>constraintToAttached</c>.</summary>
    public required IvpConstraintFrame FrameB { get; init; }

    /// <summary>The twist limit — the block at <c>constraint+0xb0</c>.</summary>
    public IvpJointAxis Twist { get; } = new();

    /// <summary>The cone limit — <c>constraint+0xcc</c>, measured as a COSINE.</summary>
    public IvpJointAxis Cone { get; } = new();

    /// <summary>The swing limit — <c>constraint+0xe8</c>, measured as a SINE.</summary>
    public IvpJointAxis Swing { get; } = new();

    /// <summary>Reads the three deflections off the two bodies.</summary>
    /// <param name="a">The reference body.</param>
    /// <param name="b">The attached body.</param>
    /// <exception cref="ArgumentNullException">Either body is null.</exception>
    /// <remarks>
    /// **The twist is built from three vectors and none of them is a joint axis on its own:**
    ///
    /// <code>
    /// m = normalise( A[primary]_world + B[primary]_world )   // the BISECTOR, cached at geom+0x110
    /// p = normalise( A[wider]_world × m )
    /// q = B[wider]_world
    /// twist = −atan2( q·p , q·(p×m) )
    /// </code>
    ///
    /// **`geom+0x110` was written up as the joint's anchor and is not** — it is this bisector, and
    /// it is the axis `FUN_180036f80` builds its Jacobian around. The anchors are elsewhere, at
    /// `constraint+0x60` and `+0xa0`.
    ///
    /// **The two dots are taken against the same vector**, body B's primary axis, which is what
    /// makes one of them a cosine and the other a sine: dotted with A's primary they measure the
    /// angle between the same axis seen two ways, and with A's wider swing they measure a
    /// perpendicular.
    /// </remarks>
    public void Measure(IvpRigidBody a, IvpRigidBody b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        (float X, float Y, float Z) primaryA = IvpQuaternion.Rotate(a.Orientation, FrameA.Primary);
        (float X, float Y, float Z) widerA = IvpQuaternion.Rotate(a.Orientation, FrameA.Wider);
        (float X, float Y, float Z) primaryB = IvpQuaternion.Rotate(b.Orientation, FrameB.Primary);
        (float X, float Y, float Z) widerB = IvpQuaternion.Rotate(b.Orientation, FrameB.Wider);

        Cone.Angle = Dot(primaryB, primaryA);
        Swing.Angle = Dot(primaryB, widerA);

        (float X, float Y, float Z) bisector = Normalise(
            (primaryA.X + primaryB.X, primaryA.Y + primaryB.Y, primaryA.Z + primaryB.Z));

        // **`m × A[wider]`, and the order is the whole of it.** Written the other way round the
        // twist reads −π at the bind pose instead of zero, which is how the sign was caught. The
        // engine spells it out as `a.z*m.y − a.y*m.z`, which is the NEGATED `a × m`.
        (float X, float Y, float Z) perpendicular = Normalise(Cross(bisector, widerA));

        Twist.Angle = -MathF.Atan2(
            Dot(widerB, perpendicular), Dot(widerB, Cross(perpendicular, bisector)));
    }

    /// <summary>Builds a joint's three limits from the degree ranges a <c>.phy</c> declares.</summary>
    /// <param name="primary">The primary axis's minimum and maximum, in degrees.</param>
    /// <param name="narrower">The narrower swing's.</param>
    /// <param name="wider">The wider swing's.</param>
    /// <param name="reference">
    /// The reference body's frame — <c>constraintToReference</c>, already in the same permutation
    /// as the three ranges.
    /// </param>
    /// <param name="attached">The attached body's — <c>constraintToAttached</c>.</param>
    /// <returns>A joint with those frames and the three blocks bounded as the engine bounds them.</returns>
    /// <remarks>
    /// **Each block takes its bounds from a DIFFERENT axis than the one it measures**, which reads
    /// like a bug and is what `FUN_1800393d0` does:
    ///
    /// | block | bounds |
    /// |---|---|
    /// | twist | `−hi`, `−lo` of the primary — negated AND swapped, matching its negated angle |
    /// | cone | `range × ∓0.5` of the WIDER swing, re-centred because the frame carries the offset |
    /// | swing | `lo`, `hi` of the NARROWER swing, straight through |
    ///
    /// **The frames are the identity here**, which is what a TF2 ragdoll's reference frame actually
    /// is (`ragdoll_shared.cpp:245`); a real joint replaces the attached frame with the bone-to-bone
    /// transform.
    /// </remarks>
    public static IvpRagdollConstraint FromDegrees(
        (float Minimum, float Maximum) primary,
        (float Minimum, float Maximum) narrower,
        (float Minimum, float Maximum) wider)
    {
        IvpRagdollConstraint joint = new()
        {
            FrameA = IvpConstraintFrame.Identity,
            FrameB = IvpConstraintFrame.Identity,
        };

        Bound(joint.Twist, -primary.Maximum * DegreesToRadians, -primary.Minimum * DegreesToRadians);
        Bound(joint.Swing, narrower.Minimum * DegreesToRadians, narrower.Maximum * DegreesToRadians);

        float half = (wider.Maximum - wider.Minimum) * DegreesToRadians * Half;

        Bound(joint.Cone, -half, half);

        return joint;
    }

    /// <summary>Sets one block's bounds, disabling it when the range covers a full turn.</summary>
    /// <remarks>
    /// **The 2π test is on the range the AXIS declares, not on the bounds after redistribution.**
    /// `FUN_180037890` clears the enable byte with `if (fVar4 &lt;= fVar1 - fVar5)` where
    /// `DAT_1800eea18` dumps as `6.2831855`, and the comparison is `&gt;=` in that direction — so a
    /// range of exactly 2π is disabled.
    /// </remarks>
    private static void Bound(IvpJointAxis axis, float lower, float upper)
    {
        axis.Lower = lower;
        axis.Upper = upper;
        axis.Limited = upper - lower < FullTurn;
    }

    /// <summary>Scales a vector to unit length, leaving a degenerate one at zero.</summary>
    /// <remarks>
    /// **Zero rather than an infinity, as the engine has it.** `FUN_180038620` masks its refined
    /// reciprocal square root with `FLT_EPSILON &lt; |v|²`, so an axis that has gone degenerate
    /// contributes nothing instead of poisoning the frame.
    /// </remarks>
    private static (float X, float Y, float Z) Normalise((float X, float Y, float Z) vector)
    {
        float square = (vector.X * vector.X) + (vector.Y * vector.Y) + (vector.Z * vector.Z);

        if (square <= FloatEpsilon)
        {
            return default;
        }

        float scale = 1f / MathF.Sqrt(square);

        return (vector.X * scale, vector.Y * scale, vector.Z * scale);
    }

    /// <summary>The dot product.</summary>
    private static float Dot((float X, float Y, float Z) left, (float X, float Y, float Z) right) =>
        (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);

    /// <summary>The cross product.</summary>
    private static (float X, float Y, float Z) Cross(
        (float X, float Y, float Z) left, (float X, float Y, float Z) right) =>
        (
            (left.Y * right.Z) - (left.Z * right.Y),
            (left.Z * right.X) - (left.X * right.Z),
            (left.X * right.Y) - (left.Y * right.X));

    /// <summary><c>DAT_1800eb764</c>, dumped.</summary>
    private const float DegreesToRadians = 0.017453292f;

    /// <summary><c>DAT_1800ea984</c>, dumped.</summary>
    private const float Half = 0.5f;

    /// <summary><c>DAT_1800eea18</c>, dumped as 2π.</summary>
    private const float FullTurn = 6.2831855f;

    /// <summary><c>FLT_EPSILON</c> — the cutoff below which a vector is treated as degenerate.</summary>
    private const float FloatEpsilon = 1.1920929e-07f;
}
