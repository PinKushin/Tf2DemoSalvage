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

    [Test]
    public void BuildJacobian_ANullCore_ReturnsNull() =>
        IvpTangentialSolve.BuildJacobian(null, default, (1f, 0f, 0f), (0f, 1f, 0f), (1f, 1f, 1f, 1f)).ShouldBeNull();

    /// <remarks>
    /// **A body at the identity orientation with unit inverse inertia and unit axis factors**: the world rotation is
    /// the identity, so the row IS the local `arm × axis` cross product, the mass row equals the row, and the
    /// diagonal is the row's own squared length.
    /// </remarks>
    [Test]
    public void BuildJacobian_TheIdentityOrientationAndUnitInverseInertia_TheRowIsTheLocalCrossProduct()
    {
        IvpRigidBody core = new()
        {
            InverseInertia = (1f, 1f, 1f),
            CoreMatrix = IvpMatrix.FromRotation((0f, 0f, 0f, 1f), (0d, 0d, 0d)),
        };

        (IvpJacobianRow Axis0, IvpJacobianRow Axis1)? rows =
            IvpTangentialSolve.BuildJacobian(core, (0f, 0f, 1f), (1f, 0f, 0f), (0f, 1f, 0f), (1f, 1f, 1f, 1f));

        rows.ShouldNotBeNull();

        // arm (0,0,1) x axis0 (1,0,0) = (0*0-1*0, 1*1-0*0, 0*0-0*1) = (0, 1, 0).
        rows.Value.Axis0.Row.ShouldBe((0f, 1f, 0f, 1f));
        rows.Value.Axis0.MassRow.ShouldBe((0f, 1f, 0f, 1f));
        rows.Value.Axis0.Diagonal.ShouldBe(2f);

        // arm (0,0,1) x axis1 (0,1,0) = (0*0-1*1, 1*0-0*0, 0*1-0*0) = (-1, 0, 0).
        rows.Value.Axis1.Row.ShouldBe((-1f, 0f, 0f, 1f));
        rows.Value.Axis1.MassRow.ShouldBe((-1f, 0f, 0f, 1f));
        rows.Value.Axis1.Diagonal.ShouldBe(2f);
    }

    /// <remarks>
    /// Halving the inverse inertia halves the mass row's x/y/z, since the row itself is unchanged — but the diagonal is
    /// `dot(Row, MassRow)`, and the `w = 1` lane is untouched by inverse inertia on either side, so it still contributes
    /// `1×1 = 1` on top of the halved `y×y` term (`1 × 0.5 = 0.5`), for `1.5`, not a plain half of the unscaled `2`.
    /// </remarks>
    [Test]
    public void BuildJacobian_AHalvedInverseInertia_HalvesTheXyzTermButNotTheWLane()
    {
        IvpRigidBody core = new()
        {
            InverseInertia = (0.5f, 0.5f, 0.5f),
            CoreMatrix = IvpMatrix.FromRotation((0f, 0f, 0f, 1f), (0d, 0d, 0d)),
        };

        (IvpJacobianRow Axis0, IvpJacobianRow Axis1)? rows =
            IvpTangentialSolve.BuildJacobian(core, (0f, 0f, 1f), (1f, 0f, 0f), (0f, 1f, 0f), (1f, 1f, 1f, 1f));

        rows.ShouldNotBeNull();
        rows.Value.Axis0.MassRow.ShouldBe((0f, 0.5f, 0f, 1f));
        rows.Value.Axis0.Diagonal.ShouldBe(1.5f);
    }

    [Test]
    public void ApplyImpulse_ANullCore_DoesNothing() =>
        Should.NotThrow(() => IvpTangentialSolve.ApplyImpulse(
            null, (1f, 0f, 0f), (0f, 1f, 0f), default, (1f, 1f), sign: 1f));

    /// <remarks>
    /// **Linear push uses the raw axis scaled by inverse mass; angular push uses the mass row directly**, with no
    /// second inverse-inertia scaling. Unit inverse mass and a unit axis-0 impulse of <c>2</c> along <c>(1,0,0)</c>
    /// give a linear push of exactly <c>(2,0,0)</c>.
    /// </remarks>
    [Test]
    public void ApplyImpulse_AnAxis0Impulse_PushesAlongAxis0ScaledByInverseMass()
    {
        IvpRigidBody core = new() { InverseMass = 1f };
        IvpJacobianRow axis0Row = new((1f, 0f, 0f, 1f), (3f, 0f, 0f, 1f), 4f);
        IvpJacobianRow axis1Row = new((0f, 1f, 0f, 1f), (0f, 3f, 0f, 1f), 4f);

        IvpTangentialSolve.ApplyImpulse(core, (1f, 0f, 0f), (0f, 1f, 0f), (axis0Row, axis1Row), (2f, 0f), sign: 1f);

        core.PendingVelocity.ShouldBe((2f, 0f, 0f));
        core.PendingAngularVelocity.ShouldBe((6f, 0f, 0f));
    }

    /// <remarks>The second core takes the negated sign, flipping both the linear and angular push.</remarks>
    [Test]
    public void ApplyImpulse_TheSecondCore_TakesTheNegatedSign()
    {
        IvpRigidBody core = new() { InverseMass = 1f };
        IvpJacobianRow axis0Row = new((1f, 0f, 0f, 1f), (3f, 0f, 0f, 1f), 4f);
        IvpJacobianRow axis1Row = new((0f, 1f, 0f, 1f), (0f, 3f, 0f, 1f), 4f);

        IvpTangentialSolve.ApplyImpulse(core, (1f, 0f, 0f), (0f, 1f, 0f), (axis0Row, axis1Row), (2f, 0f), sign: -1f);

        core.PendingVelocity.ShouldBe((-2f, 0f, 0f));
        core.PendingAngularVelocity.ShouldBe((-6f, 0f, 0f));
    }

    /// <remarks>Applying twice accumulates onto whatever was already pending, rather than replacing it.</remarks>
    [Test]
    public void ApplyImpulse_APendingPushAlreadyStaged_Accumulates()
    {
        IvpRigidBody core = new() { InverseMass = 1f, PendingVelocity = (1f, 0f, 0f) };
        IvpJacobianRow axis0Row = new((1f, 0f, 0f, 1f), (0f, 0f, 0f, 1f), 1f);
        IvpJacobianRow axis1Row = default;

        IvpTangentialSolve.ApplyImpulse(core, (1f, 0f, 0f), (0f, 0f, 0f), (axis0Row, axis1Row), (1f, 0f), sign: 1f);

        core.PendingVelocity.ShouldBe((2f, 0f, 0f));
    }
}
