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
    /// **Confirmed against <c>SolveOncePerPsi</c>'s own pre-clamp, read in full 2026-09-14** — the excess is the
    /// SLIDE'S OWN magnitude past the budget, weighted by friction and push-out, not the clamped fraction the raw
    /// distance clamping removed: <c>(|slide| − budget) × friction × pushOut</c>, added to whatever was already
    /// carried. **Not a collision after all**: the native sets contact <c>+0x91</c> — <see cref="IvpContactPoint.FirstMeasure"/> —
    /// TRUE whenever this clamp fires, and <see cref="IvpContactGeometry"/>'s own measures already clear it FALSE once
    /// they use it; one writer setting it and another clearing it is an ordinary flip-flop, not two fields sharing an
    /// offset. Read as a re-arm: a contact whose slide was just clipped has its position/slide history treated as
    /// fresh again next PSI. **This method itself stays pure** — see <see cref="SolveOncePerPair"/>, its only caller,
    /// for where the flag is actually set.
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
    /// the clip happens after this call, not inside it. **Confirmed against <c>SolveTangentialPair</c>'s own
    /// instructions, read in full 2026-09-14: the stored slide is used AS-IS, in whatever axes the current call's
    /// <see cref="RelativeVelocity"/> uses — no basis rotation from an older axis pair is applied.** This project's
    /// two tangent axes are recomputed fresh every PSI (see `IvpMindistCollide`), so there is no stale-basis case
    /// for a rotation to correct in the first place.
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

    /// <summary>
    /// The material axis-friction factors for one core's jacobian rows — <c>IvpContactPoint.UsesMaterialAxes</c>'s own
    /// scaling, read at <c>SolveTangentialPair</c>'s call site rather than inside <c>BuildJacobian</c> itself.
    /// </summary>
    /// <param name="usesMaterialAxes">Whether the contact's cone is shaped along the materials' own axes.</param>
    /// <returns>Identity — <c>(1, 1, 1, 1)</c> — for the ordinary isotropic case.</returns>
    /// <exception cref="NotSupportedException">
    /// <paramref name="usesMaterialAxes"/> is set. **Confirmed, 2026-09-14: nothing in this project's read of the
    /// disassembly writes <see cref="IvpContactPoint.UsesMaterialAxes"/> true** — its own doc comment already flags
    /// the writer as unread — so the anisotropic case has never been exercised and porting a guessed factor for it
    /// would be exactly the kind of invented structure this project refuses. Isotropic materials (factor 1 on every
    /// axis) are the only case this method — and so <see cref="SolveContact"/> — supports.
    /// </exception>
    private static (float X, float Y, float Z, float W) MaterialAxisFactors(bool usesMaterialAxes) =>
        !usesMaterialAxes
            ? (1f, 1f, 1f, 1f)
            : throw new NotSupportedException(
                "A contact using material-shaped friction axes was solved; the anisotropic factor is not yet read from the disassembly.");

    /// <summary>
    /// One contact's tangential (Coulomb friction) solve for a PSI, start to finish — <c>SolveTangentialPair</c>'s
    /// non-sticking branch, assembled from the contact's own <see cref="IvpContactRecord"/> and
    /// <see cref="IvpContactPoint"/> fields rather than caller-supplied axes.
    /// </summary>
    /// <param name="point">The contact, already clamped this PSI by <see cref="ClampSlide"/> if the caller runs that first.</param>
    /// <param name="inverseStep">The environment's reciprocal PSI step.</param>
    /// <returns>The clipped impulse applied to both cores, or null when the entry gate refused it or the 2×2 system was singular.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="point"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The contact has no record.</exception>
    /// <remarks>
    /// **Confirmed, 2026-09-14: `IvpContactRecord::Build` (<c>18008d0c0</c>) — already ported as
    /// <see cref="IvpContactRecord.Build"/> — is the SAME function that computes the tangent axes
    /// (<see cref="IvpContactRecord.Span"/>/<see cref="IvpContactRecord.CrossSpan"/>) and both arm vectors
    /// (<see cref="IvpContactRecord.FirstArm"/>/<see cref="IvpContactRecord.SecondArm"/>) that `SolveTangentialPair`
    /// reads — there is no separate axis/arm builder to find.**
    ///
    /// **The impulse's own clip budget is NOT the pair's `SolveOncePerPsi` budget — confirmed against
    /// `SolveTangentialPair`'s own instructions, read in full 2026-09-14 as a `vphysics-friction-solve` probe control.**
    /// `SolveTangentialPair` computes an entirely separate, per-contact value —
    /// <c>NormalPush × Friction × Step</c> (the forward step, the reciprocal of <paramref name="inverseStep"/>) — used
    /// BOTH as an entry gate (refusing the solve outright below roughly <c>1e-6</c>, matching a genuinely-unpushed
    /// contact having no friction to give) and as the clip on the solved impulse. A prior version of this method took
    /// a caller-supplied `budget` and used it for this clip — the SAME value <see cref="ClampSlide"/> uses for the
    /// STORED SLIDE's own, separate, pair-level pre-clamp — which was wrong: the two clips are unrelated in the
    /// native, one position-domain and pair-summed, the other force-domain and per-contact.
    /// </remarks>
    public static (float Span, float CrossSpan)? SolveContact(IvpContactPoint point, double inverseStep)
    {
        ArgumentNullException.ThrowIfNull(point);

        IvpContactRecord record = point.Record ?? throw new InvalidOperationException("A contact with no record was solved.");
        float step = (float)(1d / inverseStep);
        float clipBudget = point.NormalPush * point.Friction * step;

        if (clipBudget < 9.999999974752427e-07f)
        {
            return null;
        }

        (float X, float Y, float Z, float W) factors = MaterialAxisFactors(point.UsesMaterialAxes);

        (float Span, float CrossSpan)? impulse = Solve(
            record.FirstCore, record.FirstArm, record.SecondCore, record.SecondArm,
            record.Span, record.CrossSpan, factors, factors, point.Slide, inverseStep);

        if (impulse is not { } found)
        {
            return null;
        }

        (float Span, float CrossSpan) clipped = ClipImpulse(found, clipBudget);

        (IvpJacobianRow Axis0, IvpJacobianRow Axis1)? firstRows = BuildJacobian(record.FirstCore, record.FirstArm, record.Span, record.CrossSpan, factors);
        (IvpJacobianRow Axis0, IvpJacobianRow Axis1)? secondRows = BuildJacobian(record.SecondCore, record.SecondArm, record.Span, record.CrossSpan, factors);

        if (firstRows is { } first)
        {
            ApplyImpulse(record.FirstCore, record.Span, record.CrossSpan, first, clipped, 1f);
        }

        if (secondRows is { } second)
        {
            ApplyImpulse(record.SecondCore, record.Span, record.CrossSpan, second, clipped, -1f);
        }

        return clipped;
    }

    /// <summary>
    /// Every contact of one pair, clamped and solved against a shared friction-cone budget for this PSI —
    /// <c>IvpFrictionSystem::SolveOncePerPsi</c>'s own per-pair walk, the non-sticking branch of both dispatches.
    /// </summary>
    /// <param name="pair">The pair; its own <see cref="IvpFrictionPair.Contacts"/> is what is walked.</param>
    /// <param name="budget">
    /// The pair's own friction-cone budget for this PSI. **Not computed here** — the native derives it from a
    /// summed <c>NormalPush × Friction × &lt;an unnamed field, no writer found in this project's reads&gt;</c>; see
    /// `docs/HANDOFF.md`, item 3, for why porting that sum would mean guessing a value nothing has confirmed.
    /// </param>
    /// <param name="inverseStep">The environment's reciprocal PSI step.</param>
    /// <exception cref="ArgumentNullException"><paramref name="pair"/> is null.</exception>
    /// <exception cref="InvalidOperationException">A contact in the pair has no record.</exception>
    /// <remarks>
    /// **The sticking dispatch (<c>FUN_180085a80</c>, the byte at contact <c>+0x64</c>) is not carried** — every
    /// contact here is solved by <see cref="SolveContact"/>, the non-sticking branch only. **A clamped contact's
    /// <see cref="IvpContactPoint.FirstMeasure"/> is set true**, matching the native's own <c>+0x91</c> write inside
    /// this same loop — see <see cref="ClampSlide"/>'s own remarks for why this is a re-arm rather than a field
    /// collision with the byte's other, documented, writer.
    /// </remarks>
    public static void SolveOncePerPair(IvpFrictionPair pair, float budget, double inverseStep)
    {
        ArgumentNullException.ThrowIfNull(pair);

        float carry = 0f;

        foreach (IvpContactPoint contact in pair.Contacts)
        {
            IvpContactRecord record = contact.Record ?? throw new InvalidOperationException("A pair's contact has no record.");
            (float Span, float CrossSpan) before = contact.Slide;

            (contact.Slide, carry) = ClampSlide(before, budget, contact.Friction, record.PushOut, carry);

            if (contact.Slide != before)
            {
                contact.FirstMeasure = true;
            }

            SolveContact(contact, inverseStep);
        }
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
