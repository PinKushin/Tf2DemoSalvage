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

    /// <summary>
    /// The two cores' relative velocity at the contact, projected onto both tangent axes — part of
    /// <c>IvpContact::TangentialSlipVelocity</c>'s accumulation, ahead of <c>SolveTangentialPair</c>'s own target-minus-current
    /// right-hand side.
    /// </summary>
    /// <param name="first">The first core, or null for a static side.</param>
    /// <param name="firstArm">The contact point relative to the first core's position, in world space.</param>
    /// <param name="second">The second core, or null for a static side.</param>
    /// <param name="secondArm">The contact point relative to the second core's position, in world space.</param>
    /// <param name="axis0">The slide's first tangent axis, in world space.</param>
    /// <param name="axis1">The slide's second tangent axis, in world space.</param>
    /// <returns>
    /// The relative velocity's component along each axis — the first core's own velocity minus the second's, matching
    /// <see cref="IvpMindist.Normal"/>'s own convention of pointing from the second body toward the first.
    /// </returns>
    /// <remarks>
    /// **This is the CURRENT slip only.** `SolveTangentialPair`'s actual right-hand side additionally mixes in the
    /// pair's own stored, scaled slip target (<see cref="IvpContactPoint.Slide"/>) before subtracting this — not yet
    /// carried here; see `docs/HANDOFF.md`, item 3.
    /// </remarks>
    public static (double Axis0, double Axis1) RelativeVelocity(
        IvpRigidBody? first,
        (float X, float Y, float Z) firstArm,
        IvpRigidBody? second,
        (float X, float Y, float Z) secondArm,
        (float X, float Y, float Z) axis0,
        (float X, float Y, float Z) axis1)
    {
        (float X, float Y, float Z) relative = default;

        if (first is { } firstCore)
        {
            (float X, float Y, float Z) velocity = firstCore.PointVelocity(firstArm, firstCore.Velocity, firstCore.AngularVelocity);
            relative = (relative.X + velocity.X, relative.Y + velocity.Y, relative.Z + velocity.Z);
        }

        if (second is { } secondCore)
        {
            (float X, float Y, float Z) velocity = secondCore.PointVelocity(secondArm, secondCore.Velocity, secondCore.AngularVelocity);
            relative = (relative.X - velocity.X, relative.Y - velocity.Y, relative.Z - velocity.Z);
        }

        double onAxis0 = (relative.X * axis0.X) + (relative.Y * axis0.Y) + (relative.Z * axis0.Z);
        double onAxis1 = (relative.X * axis1.X) + (relative.Y * axis1.Y) + (relative.Z * axis1.Z);

        return (onAxis0, onAxis1);
    }

    /// <summary>
    /// Clamps a contact's stored slide to a pair's friction-cone budget, carrying any excess forward —
    /// <c>IvpFrictionSystem::SolveOncePerPsi</c>'s own pre-clamp, run once per PSI before the tangential solve reads
    /// the slide.
    /// </summary>
    /// <param name="slide">The contact's stored slide — <see cref="IvpContactPoint.Slide"/>.</param>
    /// <param name="budget">The pair's own friction-cone budget for this PSI.</param>
    /// <param name="friction">The contact's friction factor — <see cref="IvpContactPoint.Friction"/>.</param>
    /// <param name="pushOut">The contact's push-out estimate — <see cref="IvpContactRecord.PushOut"/>.</param>
    /// <param name="carry">The excess already carried from a previous clamp.</param>
    /// <returns>
    /// The slide, unchanged when it is already inside the budget (allowing for a <c>1e-6</c> slack on the squared
    /// magnitude), and the carry, updated only when it was clamped.
    /// </returns>
    /// <remarks>
    /// **The excess is the SLIDE'S OWN magnitude past the budget, weighted by friction and push-out** — not the
    /// clamped fraction, the raw distance clamping removed — matching the native's
    /// <c>(|slide| − budget) × friction × pushOut</c>, added to whatever was already carried.
    /// </remarks>
    public static ((float Span, float CrossSpan) Slide, float Carry) ClampSlide(
        (float Span, float CrossSpan) slide, float budget, float friction, float pushOut, float carry)
    {
        float magnitudeSquared = (slide.Span * slide.Span) + (slide.CrossSpan * slide.CrossSpan);

        if (!((budget * budget) + 1e-6f < magnitudeSquared))
        {
            return (slide, carry);
        }

        float inverseMagnitude = IvpVector.ReciprocalSquareRoot(magnitudeSquared);
        float magnitude = inverseMagnitude * magnitudeSquared;
        float scale = budget * inverseMagnitude;

        return ((slide.Span * scale, slide.CrossSpan * scale), ((magnitude - budget) * friction * pushOut) + carry);
    }

    /// <summary>
    /// Solves for the two-axis friction impulse that would cancel a contact's slip — <c>IvpFrictionSystem::SolveTangentialPair</c>
    /// (<c>1800857c0</c>), the non-sticking branch.
    /// </summary>
    /// <param name="first">The first core, or null for a static side.</param>
    /// <param name="firstArm">The contact point relative to the first core's position, in world space.</param>
    /// <param name="second">The second core, or null for a static side.</param>
    /// <param name="secondArm">The contact point relative to the second core's position, in world space.</param>
    /// <param name="axis0">The slide's first tangent axis, in world space.</param>
    /// <param name="axis1">The slide's second tangent axis, in world space.</param>
    /// <param name="firstAxisFactors">The material axis factors for the first core's rows.</param>
    /// <param name="secondAxisFactors">The material axis factors for the second core's rows.</param>
    /// <param name="slide">The contact's stored slide, already clamped by <see cref="ClampSlide"/> this PSI.</param>
    /// <param name="inverseStep">The environment's reciprocal PSI step.</param>
    /// <returns>The impulse, or null when the 2×2 system is singular — <see cref="TryInvertSymmetric"/> refused it.</returns>
    /// <remarks>
    /// **The target is the stored slide converted from a position error to a corrective velocity** —
    /// <c>slide × inverseStep</c> — matching a position error divided by time. The right-hand side is that target
    /// less the contact's actual current relative velocity (<see cref="RelativeVelocity"/>); the impulse solves the
    /// 2×2 system for the push that would close the gap between them. **The cone clip against the pair's own
    /// friction budget is a separate step**, applied by the caller once this returns — matching the native, where
    /// the clip happens after this call, not inside it.
    /// </remarks>
    public static (float Span, float CrossSpan)? Solve(
        IvpRigidBody? first,
        (float X, float Y, float Z) firstArm,
        IvpRigidBody? second,
        (float X, float Y, float Z) secondArm,
        (float X, float Y, float Z) axis0,
        (float X, float Y, float Z) axis1,
        (float X, float Y, float Z, float W) firstAxisFactors,
        (float X, float Y, float Z, float W) secondAxisFactors,
        (float Span, float CrossSpan) slide,
        double inverseStep)
    {
        (IvpJacobianRow Axis0, IvpJacobianRow Axis1)? firstRows = BuildJacobian(first, firstArm, axis0, axis1, firstAxisFactors);
        (IvpJacobianRow Axis0, IvpJacobianRow Axis1)? secondRows = BuildJacobian(second, secondArm, axis0, axis1, secondAxisFactors);

        (double A, double B, double D) system = System(firstRows, secondRows);

        if (TryInvertSymmetric(system.A, system.B, system.D) is not { } inverse)
        {
            return null;
        }

        (double Axis0, double Axis1) relative = RelativeVelocity(first, firstArm, second, secondArm, axis0, axis1);

        double rhs0 = (slide.Span * inverseStep) - relative.Axis0;
        double rhs1 = (slide.CrossSpan * inverseStep) - relative.Axis1;

        float impulseSpan = (float)((inverse.Row0.A * rhs0) + (inverse.Row0.B * rhs1));
        float impulseCrossSpan = (float)((inverse.Row1.A * rhs0) + (inverse.Row1.B * rhs1));

        return (impulseSpan, impulseCrossSpan);
    }

    /// <summary>Clips an impulse to a magnitude budget — the same shape <see cref="ClampSlide"/> uses, without a carry term.</summary>
    /// <param name="impulse">The impulse.</param>
    /// <param name="budget">The pair's own friction-cone budget.</param>
    /// <returns>The impulse, unchanged when its magnitude is already within the budget.</returns>
    public static (float Span, float CrossSpan) ClipImpulse((float Span, float CrossSpan) impulse, float budget)
    {
        float magnitudeSquared = (impulse.Span * impulse.Span) + (impulse.CrossSpan * impulse.CrossSpan);

        if (!(budget * budget < magnitudeSquared))
        {
            return impulse;
        }

        float scale = budget * IvpVector.ReciprocalSquareRoot(magnitudeSquared);

        return (impulse.Span * scale, impulse.CrossSpan * scale);
    }
}
