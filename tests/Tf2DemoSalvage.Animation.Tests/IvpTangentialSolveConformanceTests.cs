using System;
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

    [Test]
    public void CrossTerm_NoRows_IsZero() => IvpTangentialSolve.CrossTerm(null).ShouldBe(0f);

    /// <remarks><c>dot(Axis0.MassRow, Axis1.Row)</c>, read straight off <c>BuildJacobian</c>'s own accumulation.</remarks>
    [Test]
    public void CrossTerm_TwoRows_IsTheDotOfTheFirstMassRowAndTheSecondRow()
    {
        IvpJacobianRow axis0 = new((0f, 0f, 0f, 0f), (1f, 2f, 3f, 4f), 0f);
        IvpJacobianRow axis1 = new((5f, 6f, 7f, 8f), (0f, 0f, 0f, 0f), 0f);

        // 1*5 + 2*6 + 3*7 + 4*8 = 5 + 12 + 21 + 32 = 70.
        IvpTangentialSolve.CrossTerm((axis0, axis1)).ShouldBe(70f);
    }

    /// <remarks>With only one side movable, the system is exactly that side's own diagonals and cross term.</remarks>
    [Test]
    public void System_OneStaticSide_IsExactlyTheMovableSidesOwnTerms()
    {
        IvpJacobianRow axis0 = new(default, default, 2f);
        IvpJacobianRow axis1 = new(default, default, 3f);

        (double A, double B, double D) system = IvpTangentialSolve.System((axis0, axis1), null);

        system.A.ShouldBe(2d);
        system.D.ShouldBe(3d);
        system.B.ShouldBe(0d);
    }

    /// <remarks>Both sides movable: the diagonals and cross terms sum across both cores.</remarks>
    [Test]
    public void System_BothSidesMovable_SumsBothCoresContributions()
    {
        IvpJacobianRow firstAxis0 = new(default, default, 2f);
        IvpJacobianRow firstAxis1 = new(default, default, 3f);
        IvpJacobianRow secondAxis0 = new(default, default, 5f);
        IvpJacobianRow secondAxis1 = new(default, default, 7f);

        (double A, double B, double D) system = IvpTangentialSolve.System((firstAxis0, firstAxis1), (secondAxis0, secondAxis1));

        system.A.ShouldBe(7d);
        system.D.ShouldBe(10d);
    }

    /// <remarks>A body sliding at <c>(3,0,0)</c> with no spin has a relative velocity of exactly that along the matching axis.</remarks>
    [Test]
    public void RelativeVelocity_OneMovingCoreAgainstAStaticSide_IsItsOwnVelocityAlongTheAxis()
    {
        IvpRigidBody first = new() { Velocity = (3f, 0f, 0f) };

        (double Axis0, double Axis1) relative = IvpTangentialSolve.RelativeVelocity(
            first, default, null, default, (1f, 0f, 0f), (0f, 1f, 0f));

        relative.Axis0.ShouldBe(3d, 1e-6);
        relative.Axis1.ShouldBe(0d, 1e-6);
    }

    /// <remarks>
    /// **The second core's velocity is SUBTRACTED**, matching the normal's own first-minus-second convention — two
    /// bodies approaching each other at <c>2</c> and <c>−2</c> along the axis have a relative velocity of <c>4</c>.
    /// </remarks>
    [Test]
    public void RelativeVelocity_BothCoresMoving_SubtractsTheSecondsVelocity()
    {
        IvpRigidBody first = new() { Velocity = (2f, 0f, 0f) };
        IvpRigidBody second = new() { Velocity = (-2f, 0f, 0f) };

        (double Axis0, double Axis1) relative = IvpTangentialSolve.RelativeVelocity(
            first, default, second, default, (1f, 0f, 0f), (0f, 1f, 0f));

        relative.Axis0.ShouldBe(4d, 1e-6);
    }

    [Test]
    public void RelativeVelocity_NeitherCoreMoving_IsZero() =>
        IvpTangentialSolve.RelativeVelocity(null, default, null, default, (1f, 0f, 0f), (0f, 1f, 0f)).ShouldBe((0d, 0d));

    /// <remarks>A slide inside the budget (allowing the <c>1e-6</c> slack) is left exactly as it was, carry included.</remarks>
    [Test]
    public void ClampSlide_ASlideInsideTheBudget_IsUnchanged()
    {
        ((float Span, float CrossSpan) Slide, float Carry) result =
            IvpTangentialSolve.ClampSlide((0.3f, 0.4f), budget: 1f, friction: 1f, pushOut: 1f, carry: 2f);

        result.Slide.ShouldBe((0.3f, 0.4f));
        result.Carry.ShouldBe(2f);
    }

    /// <remarks>
    /// <c>(3, 4)</c> has magnitude 5; clamped to a budget of 1 it becomes <c>(0.6, 0.8)</c> — scaled by <c>1/5</c> — and
    /// the excess, <c>5 − 1 = 4</c>, times friction 2 and push-out 3, is <c>24</c>, added to the existing carry of 1.
    /// </remarks>
    [Test]
    public void ClampSlide_ASlideOverTheBudget_ScalesItDownAndCarriesTheExcess()
    {
        ((float Span, float CrossSpan) Slide, float Carry) result =
            IvpTangentialSolve.ClampSlide((3f, 4f), budget: 1f, friction: 2f, pushOut: 3f, carry: 1f);

        result.Slide.Span.ShouldBe(0.6f, 1e-4f);
        result.Slide.CrossSpan.ShouldBe(0.8f, 1e-4f);
        result.Carry.ShouldBe(25f, 1e-2f);
    }

    /// <remarks>
    /// **A resting body pressed straight into a static floor, with no slide and no relative velocity, needs no
    /// friction impulse at all.** Both axes solve to zero.
    /// </remarks>
    [Test]
    public void Solve_NoSlideAndNoRelativeVelocity_SolvesToZero()
    {
        IvpRigidBody first = new() { InverseInertia = (1f, 1f, 1f), CoreMatrix = IvpMatrix.FromRotation((0f, 0f, 0f, 1f), (0d, 0d, 0d)) };

        (float Span, float CrossSpan)? impulse = IvpTangentialSolve.Solve(
            first, (0f, 0f, 1f), null, default,
            (1f, 0f, 0f), (0f, 1f, 0f),
            (1f, 1f, 1f, 1f), (1f, 1f, 1f, 1f),
            slide: (0f, 0f), inverseStep: 100d);

        impulse.ShouldNotBeNull();
        impulse.Value.Span.ShouldBe(0f, 1e-6f);
        impulse.Value.CrossSpan.ShouldBe(0f, 1e-6f);
    }

    /// <remarks>
    /// **A body sliding at <c>1</c> along axis0 with no stored slide** needs an impulse that opposes exactly that —
    /// the right-hand side is <c>0 − 1 = −1</c>, and with the diagonal alone (a static second side, no cross term)
    /// the impulse is <c>−1 / diagonal</c>.
    /// </remarks>
    [Test]
    public void Solve_ASlidingBodyWithNoStoredSlide_OpposesTheCurrentVelocity()
    {
        IvpRigidBody first = new() { Velocity = (1f, 0f, 0f), InverseInertia = (1f, 1f, 1f), CoreMatrix = IvpMatrix.FromRotation((0f, 0f, 0f, 1f), (0d, 0d, 0d)) };

        (float Span, float CrossSpan)? impulse = IvpTangentialSolve.Solve(
            first, (0f, 0f, 1f), null, default,
            (1f, 0f, 0f), (0f, 1f, 0f),
            (1f, 1f, 1f, 1f), (1f, 1f, 1f, 1f),
            slide: (0f, 0f), inverseStep: 100d);

        impulse.ShouldNotBeNull();
        impulse.Value.Span.ShouldBeLessThan(0f, "an impulse opposing a positive slide velocity is negative");
    }

    [Test]
    public void Solve_BothSidesStatic_TheSystemIsSingularAndSolveReturnsNull() =>
        IvpTangentialSolve.Solve(
            null, default, null, default,
            (1f, 0f, 0f), (0f, 1f, 0f),
            default, default,
            slide: (0f, 0f), inverseStep: 100d).ShouldBeNull();

    /// <remarks>An impulse already inside the budget is left exactly as it was.</remarks>
    [Test]
    public void ClipImpulse_AnImpulseInsideTheBudget_IsUnchanged() =>
        IvpTangentialSolve.ClipImpulse((0.3f, 0.4f), budget: 1f).ShouldBe((0.3f, 0.4f));

    /// <remarks><c>(3, 4)</c> has magnitude 5; clipped to a budget of 1 it becomes <c>(0.6, 0.8)</c>.</remarks>
    [Test]
    public void ClipImpulse_AnImpulseOverTheBudget_ScalesItDown()
    {
        (float Span, float CrossSpan) clipped = IvpTangentialSolve.ClipImpulse((3f, 4f), budget: 1f);

        clipped.Span.ShouldBe(0.6f, 1e-4f);
        clipped.CrossSpan.ShouldBe(0.8f, 1e-4f);
    }

    /// <remarks>A contact with no slide and no relative velocity solves to zero and applies nothing.</remarks>
    [Test]
    public void SolveContact_NoSlideAndNoRelativeVelocity_AppliesNoImpulse()
    {
        IvpRigidBody core = new() { InverseInertia = (1f, 1f, 1f), CoreMatrix = IvpMatrix.FromRotation((0f, 0f, 0f, 1f), (0d, 0d, 0d)) };
        IvpContactRecord record = new() { FirstCore = core, FirstArm = (0f, 0f, 1f), Span = (1f, 0f, 0f), CrossSpan = (0f, 1f, 0f) };
        IvpContactPoint point = ContactPoint(record);

        (float Span, float CrossSpan)? impulse = IvpTangentialSolve.SolveContact(point, budget: 10f, inverseStep: 100d);

        impulse.ShouldNotBeNull();
        impulse.Value.ShouldBe((0f, 0f));
        core.PendingVelocity.ShouldBe((0f, 0f, 0f));
        core.PendingAngularVelocity.ShouldBe((0f, 0f, 0f));
    }

    /// <remarks>A sliding body's own velocity produces a nonzero impulse, applied onto its pending push.</remarks>
    [Test]
    public void SolveContact_ASlidingBody_AppliesAnOpposingImpulseToItsPendingVelocity()
    {
        IvpRigidBody core = new() { Velocity = (1f, 0f, 0f), InverseInertia = (1f, 1f, 1f), CoreMatrix = IvpMatrix.FromRotation((0f, 0f, 0f, 1f), (0d, 0d, 0d)) };
        IvpContactRecord record = new() { FirstCore = core, FirstArm = (0f, 0f, 1f), Span = (1f, 0f, 0f), CrossSpan = (0f, 1f, 0f) };
        IvpContactPoint point = ContactPoint(record);

        (float Span, float CrossSpan)? impulse = IvpTangentialSolve.SolveContact(point, budget: 10f, inverseStep: 100d);

        impulse.ShouldNotBeNull();
        impulse.Value.Span.ShouldBeLessThan(0f, "the impulse opposes the core's own positive slide velocity");
        core.PendingVelocity.X.ShouldNotBe(0f, "SolveContact must apply the found impulse, not just return it");
    }

    /// <remarks>An impulse the raw solve would place over budget comes back clipped to it.</remarks>
    [Test]
    public void SolveContact_AnImpulseOverTheBudget_IsClippedBeforeItIsApplied()
    {
        IvpRigidBody core = new() { Velocity = (1000f, 0f, 0f), InverseInertia = (1f, 1f, 1f), CoreMatrix = IvpMatrix.FromRotation((0f, 0f, 0f, 1f), (0d, 0d, 0d)) };
        IvpContactRecord record = new() { FirstCore = core, FirstArm = (0f, 0f, 1f), Span = (1f, 0f, 0f), CrossSpan = (0f, 1f, 0f) };
        IvpContactPoint point = ContactPoint(record);

        (float Span, float CrossSpan)? impulse = IvpTangentialSolve.SolveContact(point, budget: 1f, inverseStep: 100d);

        impulse.ShouldNotBeNull();
        double magnitude = Math.Sqrt((impulse.Value.Span * impulse.Value.Span) + (impulse.Value.CrossSpan * impulse.Value.CrossSpan));
        magnitude.ShouldBe(1d, 1e-3d);
    }

    /// <remarks>
    /// A contact whose cone is shaped along the materials' own axes has no confirmed factor to read yet —
    /// <see cref="IvpContactPoint.UsesMaterialAxes"/>'s writer is unread, so this project refuses to guess one.
    /// </remarks>
    [Test]
    public void SolveContact_AContactUsingMaterialAxes_ThrowsRatherThanGuessTheFactor()
    {
        IvpRigidBody core = new() { InverseInertia = (1f, 1f, 1f), CoreMatrix = IvpMatrix.FromRotation((0f, 0f, 0f, 1f), (0d, 0d, 0d)) };
        IvpContactRecord record = new() { FirstCore = core, FirstArm = (0f, 0f, 1f), Span = (1f, 0f, 0f), CrossSpan = (0f, 1f, 0f) };
        IvpContactPoint point = ContactPoint(record);
        point.UsesMaterialAxes = true;

        Should.Throw<NotSupportedException>(() => IvpTangentialSolve.SolveContact(point, budget: 10f, inverseStep: 100d));
    }

    /// <remarks>A slide already inside the budget is untouched by the pair walk, and the contact is still solved.</remarks>
    [Test]
    public void SolveOncePerPair_AContactAlreadyInsideBudget_LeavesItsSlideUnchanged()
    {
        IvpRigidBody core = new() { InverseInertia = (1f, 1f, 1f), CoreMatrix = IvpMatrix.FromRotation((0f, 0f, 0f, 1f), (0d, 0d, 0d)) };
        IvpContactRecord record = new() { FirstCore = core, FirstArm = (0f, 0f, 1f), Span = (1f, 0f, 0f), CrossSpan = (0f, 1f, 0f) };
        IvpContactPoint point = ContactPoint(record);
        point.Slide = (0.1f, 0f);
        IvpFrictionPair pair = new(core, core);
        pair.Contacts.Add(point);

        IvpTangentialSolve.SolveOncePerPair(pair, budget: 10f, inverseStep: 100d);

        point.Slide.ShouldBe((0.1f, 0f));
    }

    /// <remarks>A slide over the budget is clamped to it before the contact is solved.</remarks>
    [Test]
    public void SolveOncePerPair_AContactOverTheBudget_ClampsItsSlideToTheBudget()
    {
        IvpRigidBody core = new() { InverseInertia = (1f, 1f, 1f), CoreMatrix = IvpMatrix.FromRotation((0f, 0f, 0f, 1f), (0d, 0d, 0d)) };
        IvpContactRecord record = new() { FirstCore = core, FirstArm = (0f, 0f, 1f), Span = (1f, 0f, 0f), CrossSpan = (0f, 1f, 0f) };
        IvpContactPoint point = ContactPoint(record);
        point.Slide = (100f, 0f);
        IvpFrictionPair pair = new(core, core);
        pair.Contacts.Add(point);

        IvpTangentialSolve.SolveOncePerPair(pair, budget: 1f, inverseStep: 100d);

        point.Slide.Span.ShouldBe(1f, 1e-3f);
    }

    [Test]
    public void SolveOncePerPair_ANullPair_ThrowsArgumentNullException() =>
        Should.Throw<ArgumentNullException>(() => IvpTangentialSolve.SolveOncePerPair(null!, budget: 1f, inverseStep: 100d));

    private static IvpContactPoint ContactPoint(IvpContactRecord record)
    {
        IvpCollisionObject first = new();
        IvpCollisionObject second = new();
        IvpMindist mindist = new(
            new IvpSynapse(new IvpLedgeEdge(0, 0), IvpFeatureKind.Point),
            new IvpSynapse(new IvpLedgeEdge(0, 0), IvpFeatureKind.Triangle),
            extraRadius: 0f)
        {
            Flags = 0xc0000,
        };

        new IvpMindistManager().LinkExact(mindist, first, second);

        IvpLedgeSide side = IvpContactGeometryConformanceTests.Anywhere();

        return new IvpContactPoint(mindist, first, side, second, side, now: 0d) { Record = record };
    }
}
