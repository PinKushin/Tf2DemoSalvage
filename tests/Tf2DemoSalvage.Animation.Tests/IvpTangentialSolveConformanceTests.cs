using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>IVP's tangential (Coulomb friction) solve — <c>IvpContact::TryInvertSymmetric</c> and what is built on it (B369, D172).</summary>
/// <remarks>
/// **Read from the disassembly in full.** Not yet pinned by an oracle probe — see `docs/HANDOFF.md`, item 3.
/// </remarks>
public sealed class IvpTangentialSolveConformanceTests
{
    /// <remarks>The identity matrix inverts to itself.</remarks>
    [Test]
    public void TryInvertSymmetric_TheIdentity_InvertsToItself()
    {
        ((double A, double B) Row0, (double A, double B) Row1)? inverse = IvpTangentialSolve.TryInvertSymmetric(1d, 0d, 1d);

        inverse.ShouldNotBeNull();
        inverse.Value.Row0.ShouldBe((1d, 0d));
        inverse.Value.Row1.ShouldBe((0d, 1d));
    }

    /// <remarks><c>[[2,0],[0,4]]⁻¹ = [[0.5,0],[0,0.25]]</c> — a diagonal matrix inverts componentwise.</remarks>
    [Test]
    public void TryInvertSymmetric_ADiagonalMatrix_InvertsComponentwise()
    {
        ((double A, double B) Row0, (double A, double B) Row1)? inverse = IvpTangentialSolve.TryInvertSymmetric(2d, 0d, 4d);

        inverse.ShouldNotBeNull();
        inverse.Value.Row0.ShouldBe((0.5d, 0d));
        inverse.Value.Row1.ShouldBe((0d, 0.25d));
    }

    /// <remarks>
    /// <c>[[2,1],[1,2]]</c> has determinant 3, inverting to <c>(1/3)·[[2,-1],[-1,2]]</c> — the off-diagonal sign flips,
    /// the diagonal terms swap.
    /// </remarks>
    [Test]
    public void TryInvertSymmetric_AGenericSymmetricMatrix_InvertsByCramersRule()
    {
        ((double A, double B) Row0, (double A, double B) Row1)? inverse = IvpTangentialSolve.TryInvertSymmetric(2d, 1d, 2d);

        inverse.ShouldNotBeNull();
        inverse.Value.Row0.A.ShouldBe(2d / 3d, 1e-12);
        inverse.Value.Row0.B.ShouldBe(-1d / 3d, 1e-12);
        inverse.Value.Row1.A.ShouldBe(-1d / 3d, 1e-12);
        inverse.Value.Row1.B.ShouldBe(2d / 3d, 1e-12);
    }

    /// <remarks>
    /// **Guarded by the SQUARE of the determinant against `1e-38`, not the determinant itself** — a singular matrix
    /// (determinant zero) must refuse rather than divide.
    /// </remarks>
    [Test]
    public void TryInvertSymmetric_ASingularMatrix_ReturnsNull()
    {
        // [[1,1],[1,1]] has determinant 1*1 - 1*1 = 0.
        IvpTangentialSolve.TryInvertSymmetric(1d, 1d, 1d).ShouldBeNull();
    }
}
