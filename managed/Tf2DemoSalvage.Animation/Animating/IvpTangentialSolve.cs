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
}
