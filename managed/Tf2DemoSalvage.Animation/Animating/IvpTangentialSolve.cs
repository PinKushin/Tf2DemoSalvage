using System;

namespace Tf2DemoSalvage.Animation.Animating;

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
}
