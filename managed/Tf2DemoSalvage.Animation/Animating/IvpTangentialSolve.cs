using System;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// One core's jacobian row for one tangent axis, and its mass-weighted response — <c>IvpRigidBody::BuildJacobian</c>
/// (<c>18009d010</c>).
/// </summary>
/// <param name="Row">
/// The axis rotated into world space by the core's matrix, <c>w = 1</c> — <c>CoreMatrix.Rotate(arm × axis)</c>.
/// </param>
/// <param name="MassRow">The row scaled by the core's inverse inertia, component-wise, then by the material's axis factor.</param>
/// <param name="Diagonal">This row's own contribution to the solve's diagonal — <c>dot(Row, MassRow)</c>.</param>
public readonly record struct IvpJacobianRow(
    (float X, float Y, float Z, float W) Row, (float X, float Y, float Z, float W) MassRow, float Diagonal);

/// <summary>
/// IVP's tangential (Coulomb friction) solve for one contact — <c>IvpContact::TryInvertSymmetric</c> (<c>1800868d0</c>),
/// <c>IvpRigidBody::BuildJacobian</c> (<c>18009d010</c>), and the routines built on them (B369, D172).
/// </summary>
/// <remarks>
/// **Read from the disassembly in full.** Not yet wired into <see cref="IvpFrictionSystem"/> — see `docs/HANDOFF.md`,
/// item 3, for the remaining pieces (the sticking branch's anchor state, `SolveOncePerPsi`'s per-pair contact list).
/// </remarks>
public static class IvpTangentialSolve
{
    /// <summary>Inverts a symmetric 2×2 matrix — <c>IvpContact::TryInvertSymmetric</c>.</summary>
    /// <param name="a">Row 0, column 0.</param>
    /// <param name="b">Row 0, column 1 (equal to row 1, column 0 — the matrix is symmetric).</param>
    /// <param name="d">Row 1, column 1.</param>
    /// <returns>The inverse, or null when the determinant's square is under <c>1e-38</c>.</returns>
    /// <remarks>Guards against a near-singular matrix by the SQUARE of the determinant, not the determinant itself.</remarks>
    public static ((double A, double B) Row0, (double A, double B) Row1)? TryInvertSymmetric(double a, double b, double d)
    {
        double determinant = (a * d) - (b * b);

        if (!(1e-38 <= determinant * determinant))
        {
            return null;
        }

        double reciprocal = 1d / determinant;

        return ((reciprocal * d, -(reciprocal * b)), (-(reciprocal * b), reciprocal * a));
    }

    /// <summary>
    /// Builds one core's jacobian rows for the tangential solve's two axes — <c>IvpRigidBody::BuildJacobian</c>.
    /// </summary>
    /// <param name="core">The core; null for a static side, which contributes no row.</param>
    /// <param name="arm">The contact point, relative to the core's position, in world space.</param>
    /// <param name="axis0">The slide's first tangent axis, in world space.</param>
    /// <param name="axis1">The slide's second tangent axis, in world space.</param>
    /// <param name="axisFactors">
    /// The material axis-friction factors the mass row is additionally scaled by — <c>+0x40/0x44/0x48/0x4c</c> on the
    /// native's own per-core material block, read at the call site and handed in rather than re-derived here.
    /// </param>
    /// <returns>The two rows, or null when <paramref name="core"/> is null.</returns>
    /// <remarks>
    /// **Only two axes: the native's third row capacity is always passed a null pointer at every call site this
    /// project reaches**, so it never runs — this project's tangential solve is a plain 2×2 system, matching
    /// <see cref="TryInvertSymmetric"/>'s own shape, and this method mirrors that rather than carrying dead capacity.
    /// **Not yet pinned by an oracle probe** — read from the disassembly's exact operations and offsets, but the
    /// off-diagonal cross-term this needs when both axes come from the SAME core (the native's <c>+0x1f</c>/<c>+0x24</c>
    /// accumulation) is not yet carried by this method; see `docs/HANDOFF.md`, item 3.
    /// </remarks>
    public static (IvpJacobianRow Axis0, IvpJacobianRow Axis1)? BuildJacobian(
        IvpRigidBody? core,
        (float X, float Y, float Z) arm,
        (float X, float Y, float Z) axis0,
        (float X, float Y, float Z) axis1,
        (float X, float Y, float Z, float W) axisFactors)
    {
        if (core is null)
        {
            return null;
        }

        IvpJacobianRow row0 = Row(core, arm, axis0, axisFactors);
        IvpJacobianRow row1 = Row(core, arm, axis1, axisFactors);

        return (row0, row1);
    }

    private static IvpJacobianRow Row(
        IvpRigidBody core, (float X, float Y, float Z) arm, (float X, float Y, float Z) axis, (float X, float Y, float Z, float W) axisFactors)
    {
        (float X, float Y, float Z) cross = (
            (arm.Y * axis.Z) - (arm.Z * axis.Y),
            (arm.Z * axis.X) - (arm.X * axis.Z),
            (arm.X * axis.Y) - (arm.Y * axis.X));

        (double X, double Y, double Z) world = core.CoreMatrix.Rotate(((double)cross.X, (double)cross.Y, (double)cross.Z));
        (float X, float Y, float Z, float W) row = ((float)world.X, (float)world.Y, (float)world.Z, 1f);

        (float X, float Y, float Z, float W) massRow = (
            row.X * core.InverseInertia.X * axisFactors.X,
            row.Y * core.InverseInertia.Y * axisFactors.Y,
            row.Z * core.InverseInertia.Z * axisFactors.Z,
            row.W * axisFactors.W);

        float diagonal = (row.X * massRow.X) + (row.Y * massRow.Y) + (row.Z * massRow.Z) + (row.W * massRow.W);

        return new IvpJacobianRow(row, massRow, diagonal);
    }

    /// <summary>Applies the two-axis impulse to a core's staged pending push — <c>FUN_18009c620</c>.</summary>
    /// <param name="core">The core; null for a static side, which takes nothing.</param>
    /// <param name="axis0">The slide's first tangent axis, in world space — the same one <see cref="BuildJacobian"/> took.</param>
    /// <param name="axis1">The slide's second tangent axis, in world space.</param>
    /// <param name="rows">This core's own rows from <see cref="BuildJacobian"/>.</param>
    /// <param name="impulse">The two-axis impulse the solve found.</param>
    /// <param name="sign">
    /// <c>+1</c> for the first core, <c>−1</c> for the second — the native negates the axes (not the impulse) for the
    /// second side, which is the same thing since both enter linearly.
    /// </param>
    /// <remarks>
    /// **The linear push uses the raw axes scaled by inverse mass; the angular push uses the already inertia-scaled
    /// mass rows directly** — <see cref="IvpJacobianRow.MassRow"/> already carries <see cref="IvpRigidBody.InverseInertia"/>,
    /// so applying it again here would double-count it.
    /// </remarks>
    public static void ApplyImpulse(
        IvpRigidBody? core,
        (float X, float Y, float Z) axis0,
        (float X, float Y, float Z) axis1,
        (IvpJacobianRow Axis0, IvpJacobianRow Axis1) rows,
        (float Span, float CrossSpan) impulse,
        float sign)
    {
        if (core is null)
        {
            return;
        }

        float scaled0 = impulse.Span * sign;
        float scaled1 = impulse.CrossSpan * sign;

        core.PendingVelocity = (
            core.PendingVelocity.X + (((axis0.X * scaled0) + (axis1.X * scaled1)) * core.InverseMass),
            core.PendingVelocity.Y + (((axis0.Y * scaled0) + (axis1.Y * scaled1)) * core.InverseMass),
            core.PendingVelocity.Z + (((axis0.Z * scaled0) + (axis1.Z * scaled1)) * core.InverseMass));

        core.PendingAngularVelocity = (
            core.PendingAngularVelocity.X + (rows.Axis0.MassRow.X * scaled0) + (rows.Axis1.MassRow.X * scaled1),
            core.PendingAngularVelocity.Y + (rows.Axis0.MassRow.Y * scaled0) + (rows.Axis1.MassRow.Y * scaled1),
            core.PendingAngularVelocity.Z + (rows.Axis0.MassRow.Z * scaled0) + (rows.Axis1.MassRow.Z * scaled1));
    }

    /// <summary>One core's off-diagonal contribution to the 2×2 tangential system — <c>dot(Axis0.MassRow, Axis1.Row)</c>.</summary>
    /// <param name="rows">The core's own rows from <see cref="BuildJacobian"/>, or null for a static side.</param>
    /// <returns>The contribution, zero for a static side.</returns>
    /// <remarks>
    /// **The native's `+0x1f`/`+0x24` accumulation, read from `BuildJacobian`'s own body.** Two cores each contribute
    /// their own cross term to the SAME 2×2 system, summed exactly as the diagonals are — the axes are shared between
    /// both sides of a contact, but each core's mass response to them is its own.
    /// </remarks>
    public static float CrossTerm((IvpJacobianRow Axis0, IvpJacobianRow Axis1)? rows)
    {
        if (rows is not { } value)
        {
            return 0f;
        }

        return (value.Axis0.MassRow.X * value.Axis1.Row.X) +
            (value.Axis0.MassRow.Y * value.Axis1.Row.Y) +
            (value.Axis0.MassRow.Z * value.Axis1.Row.Z) +
            (value.Axis0.MassRow.W * value.Axis1.Row.W);
    }

    /// <summary>The 2×2 tangential system for a contact — both cores' diagonals and cross terms, summed.</summary>
    /// <param name="first">The first core's rows, or null for a static side.</param>
    /// <param name="second">The second core's rows, or null for a static side.</param>
    /// <returns>
    /// The system <c>[[a, b], [b, d]]</c>, ready for <see cref="TryInvertSymmetric"/> — <c>a</c> is the summed axis-0
    /// diagonal, <c>d</c> the summed axis-1 diagonal, and <c>b</c> the summed cross term.
    /// </returns>
    public static (double A, double B, double D) System(
        (IvpJacobianRow Axis0, IvpJacobianRow Axis1)? first, (IvpJacobianRow Axis0, IvpJacobianRow Axis1)? second)
    {
        double a = (first?.Axis0.Diagonal ?? 0f) + (second?.Axis0.Diagonal ?? 0f);
        double d = (first?.Axis1.Diagonal ?? 0f) + (second?.Axis1.Diagonal ?? 0f);
        double b = CrossTerm(first) + CrossTerm(second);

        return (a, b, d);
    }
}
