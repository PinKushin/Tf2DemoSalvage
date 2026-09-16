using System;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// The translation half of a ragdoll joint — the part that holds two bodies together (B58, D146).
/// </summary>
/// <remarks>
/// **This is the term whose absence let a corpse come apart.** `IvpRagdollJoint` solved three
/// angular axes and nothing else, so a ragdoll's seventeen bodies fell independently and stayed
/// together only because they started together. Measured on `z1800`: at the moment each escaping
/// corpse passed z −50 its ROOT body was outside all geometry while the corpse still reported six
/// to eleven contacts — the limbs resting on a floor the pelvis had already gone through.
///
/// **The engine solves it immediately after the three axes, in the same function, behind a flag.**
/// `FUN_180036e10` dispatches the axes and returns; `FUN_180038620` then does this:
///
/// <code>
/// if (*(char *)(param_1 + 0x157) != '\0') {
///     fVar39 = az * Rz + origin + ay * Ry + ax * Rx;               // body A's anchor, in world
///     fVar22 = bz * Rz + origin + by * Ry + bx * Rx;               // body B's
///     *param_3 = fVar22 - fVar39;                                  // THE POSITION ERROR
///     FUN_180038070(param_3 + 4, &amp;local_b8, coreA, coreB, &amp;armA, &amp;armB);   // the 3x3 K matrix
///     fVar17 = *(float *)(param_1 + 0x14c);                        // gain on the velocity
///     fVar22 = *(float *)(param_1 + 0x150) * fVar51 * param_2[1];  // gain on the error
///     fVar21 = (0.0 - fVar17 * fVar52 * local_b8) + fVar22 * fVar21;
///     …                                                            // times the inverse of K
///     if ((*pbVar3 &amp; 0x12) == 0) { core[0x130] += …;  core[0x140] += …; }
///     if ((*pbVar4 &amp; 0x12) == 0) { core[0x130] += …;  core[0x140] += …; }
/// }
/// </code>
///
/// **Two things mark it out from everything else in the constraint path.** It is the only place
/// that writes LINEAR velocity — the three angular axes never touch `core+0x140` — and the only
/// term anywhere that reads a POSITION rather than a velocity. A solver made purely of velocity
/// constraints has no way to notice that two bodies have drifted apart.
///
/// **Both gains are 1.0 and the flag is set, settled in the disassembly** rather than read out of
/// decompiled C, per `docs/memory/nothing-is-closed.md#settle-a-constant-in-the-disassembly`. The
/// constraint template's own constructor writes all three literally and nothing overwrites them
/// before the constraint is built:
///
/// <code>
/// 18000c7cb  MOV dword ptr [RBX + 0xc8],0x3f800000     ; the velocity gain
/// 18000c7d5  MOV dword ptr [RBX + 0xcc],0x3f800000     ; the error gain
/// 18000c7df  MOV byte ptr  [RBX + 0xd3],0x1            ; translation ENABLED
/// </code>
///
/// **What is NOT established: the two per-body scales** `fVar51` and `fVar52`, chosen by a lane
/// mask between the solve's own arguments and a value off an `rsqrt` Newton refinement. Their
/// ordinary value for a normalised axis is one and that is what is taken here — stated rather than
/// buried, because it is the one number in this file that was not read.
///
/// **`FUN_180038070` is identified by ARITHMETIC, not by a name.** It reads each core's inverse
/// inertia at `+0x40..0x48` and inverse mass at `+0x4c` and forms cross products with the two arms,
/// which is the standard ball-socket effective-mass matrix
/// `K = (1/mA + 1/mB)·I − [rA]ˣ·IA⁻¹·[rA]ˣ − [rB]ˣ·IB⁻¹·[rB]ˣ` — the same quantities the contact
/// record precomputes as one scalar, here as a full 3×3.
/// </remarks>
public static class IvpBallSocket
{
    /// <summary>Pulls two bodies back onto their shared anchor.</summary>
    /// <param name="a">The reference body — the CHILD, as the engine orders them.</param>
    /// <param name="b">The attached body — the parent.</param>
    /// <param name="anchorA">Where the joint sits in <paramref name="a"/>'s own space.</param>
    /// <param name="anchorB">Where it sits in <paramref name="b"/>'s.</param>
    /// <param name="weight">This sweep's relaxation weight, which scales the error term alone.</param>
    /// <exception cref="ArgumentNullException">A body is null.</exception>
    /// <remarks>
    /// **Only the ERROR term takes the weight**, which is what the engine's own grouping says:
    /// `param_2[1]` multiplies `+0x150`'s gain and not `+0x14c`'s. So the velocity constraint is
    /// solved at full strength every sweep and the positional pull is relaxed — the drift is
    /// removed gradually where the motion is stopped at once.
    ///
    /// **An immovable body contributes no mass and takes no impulse**, matching the engine's
    /// `(*flags &amp; 0x12) == 0` guard on each side independently. That is what lets one end of a
    /// joint be pinned.
    /// </remarks>
    public static void Solve(
        IvpRigidBody a,
        IvpRigidBody b,
        (float X, float Y, float Z) anchorA,
        (float X, float Y, float Z) anchorB,
        float weight) => Solve(a, b, anchorA, anchorB, weight, 1f);

    /// <summary>Pulls two bodies back onto their shared anchor, with the gains the driver and the PSI event hand the solve.</summary>
    /// <param name="a">The reference body.</param>
    /// <param name="b">The attached body.</param>
    /// <param name="anchorA">The joint in <paramref name="a"/>'s space.</param>
    /// <param name="anchorB">The joint in <paramref name="b"/>'s space.</param>
    /// <param name="errorGain"><c>+0x150 · x · event+0x4</c>: the driver's error weight over one step.</param>
    /// <param name="velocityGain"><c>+0x14c · y</c>: the driver's velocity weight.</param>
    /// <exception cref="ArgumentNullException">A body is null.</exception>
    /// <remarks>
    /// **`fVar51` and `fVar52` are read** (2026-09-16): they are lanes <c>x</c> and <c>y</c> of <c>+0x2d0</c>, the solve's own
    /// <c>param_4</c>/<c>param_5</c> — one each for slot 3, the per-iteration tables for the sweeps — and <c>param_2</c> is the PSI
    /// event, whose <c>+0x4</c> is the inverse step: <c>wanted = (0 − y·rel) + (x·invStep)·error</c>.
    /// </remarks>
    public static void Solve(
        IvpRigidBody a,
        IvpRigidBody b,
        (float X, float Y, float Z) anchorA,
        (float X, float Y, float Z) anchorB,
        float errorGain,
        float velocityGain)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        bool movableA = !a.Immovable;
        bool movableB = !b.Immovable;

        if (!movableA && !movableB)
        {
            return;
        }

        // The arms, which are the anchors turned into world directions but not moved.
        (float X, float Y, float Z) armA = IvpQuaternion.Rotate(a.Orientation, anchorA);
        (float X, float Y, float Z) armB = IvpQuaternion.Rotate(b.Orientation, anchorB);

        // **Differenced in double before narrowing**, because the two positions are world
        // coordinates thousands of units from the origin and their difference is fractions of one.
        (float X, float Y, float Z) error = (
            (float)(b.Position.X - a.Position.X + (armB.X - armA.X)),
            (float)(b.Position.Y - a.Position.Y + (armB.Y - armA.Y)),
            (float)(b.Position.Z - a.Position.Z + (armB.Z - armA.Z)));

        (float X, float Y, float Z) spinA = Cross(a.AngularVelocity, armA);
        (float X, float Y, float Z) spinB = Cross(b.AngularVelocity, armB);

        // **A's anchor MINUS B's, and the order is load-bearing.** The impulse is applied `+P` to A
        // and `−P` to B, so the quantity this solve changes is `v(A) − v(B)`: taking the difference
        // the other way makes the velocity term ADD to the relative motion instead of cancelling
        // it, and the position term is unaffected, so the two halves fight.
        //
        // **It grew by about sixteen every tick, which is four sweeps of a sign error**, and it was
        // invisible in free fall because a hanging ragdoll has neither relative velocity nor
        // position error. Only a body arriving at a floor supplies both.
        (float X, float Y, float Z) relative = (
            (a.Velocity.X + spinA.X) - (b.Velocity.X + spinB.X),
            (a.Velocity.Y + spinA.Y) - (b.Velocity.Y + spinB.Y),
            (a.Velocity.Z + spinA.Z) - (b.Velocity.Z + spinB.Z));

        (float X, float Y, float Z) wanted = (
            (0f - (velocityGain * relative.X)) + (errorGain * error.X),
            (0f - (velocityGain * relative.Y)) + (errorGain * error.Y),
            (0f - (velocityGain * relative.Z)) + (errorGain * error.Z));

        if (Solve(a, b, armA, armB, movableA, movableB, wanted) is not { } impulse)
        {
            return;
        }

        // Equal and opposite, which is what the engine's second block negates every term for.
        if (movableA)
        {
            Push(a, armA, impulse, 1f);
        }

        if (movableB)
        {
            Push(b, armB, impulse, -1f);
        }
    }

    /// <summary>Applies one impulse's linear and angular halves, in one direction or the other.</summary>
    private static void Push(
        IvpRigidBody body,
        (float X, float Y, float Z) arm,
        (float X, float Y, float Z) impulse,
        float sign)
    {
        (float X, float Y, float Z) signed = (
            impulse.X * sign, impulse.Y * sign, impulse.Z * sign);

        body.Velocity = (
            body.Velocity.X + (signed.X * body.InverseMass),
            body.Velocity.Y + (signed.Y * body.InverseMass),
            body.Velocity.Z + (signed.Z * body.InverseMass));

        (float X, float Y, float Z) twist = Cross(arm, signed);

        body.AngularVelocity = (
            body.AngularVelocity.X + (twist.X * body.InverseInertia.X),
            body.AngularVelocity.Y + (twist.Y * body.InverseInertia.Y),
            body.AngularVelocity.Z + (twist.Z * body.InverseInertia.Z));
    }

    /// <summary>Solves <c>K · impulse = wanted</c> for the impulse.</summary>
    /// <remarks>
    /// **The matrix is built a column at a time by applying it to each basis vector**, rather than
    /// by writing out nine products. `K·e = (1/mA + 1/mB)·e − rA × (IA⁻¹ ⊙ (rA × e)) − rB × (…)`
    /// is the same expression the skew-matrix form expands to, and stating it this way removes the
    /// transposition mistakes that form invites — the inertia here is diagonal, so the whole
    /// middle factor is an elementwise multiply.
    ///
    /// **A singular K is skipped rather than regularised.** It means the two arms and the masses
    /// leave some direction with no effective mass at all, which for a real ragdoll does not
    /// happen; forcing an answer there would be inventing one.
    /// </remarks>
    private static (float X, float Y, float Z)? Solve(
        IvpRigidBody a,
        IvpRigidBody b,
        (float X, float Y, float Z) armA,
        (float X, float Y, float Z) armB,
        bool movableA,
        bool movableB,
        (float X, float Y, float Z) wanted)
    {
        float linear =
            (movableA ? a.InverseMass : 0f) +
            (movableB ? b.InverseMass : 0f);

        (float X, float Y, float Z) Column((float X, float Y, float Z) axis)
        {
            (float X, float Y, float Z) column = (
                linear * axis.X, linear * axis.Y, linear * axis.Z);

            if (movableA)
            {
                column = Subtract(column, Twist(armA, a.InverseInertia, axis));
            }

            if (movableB)
            {
                column = Subtract(column, Twist(armB, b.InverseInertia, axis));
            }

            return column;
        }

        (float X, float Y, float Z) first = Column((1f, 0f, 0f));
        (float X, float Y, float Z) second = Column((0f, 1f, 0f));
        (float X, float Y, float Z) third = Column((0f, 0f, 1f));

        float determinant =
            (first.X * ((second.Y * third.Z) - (second.Z * third.Y))) -
            (second.X * ((first.Y * third.Z) - (first.Z * third.Y))) +
            (third.X * ((first.Y * second.Z) - (first.Z * second.Y)));

        if (MathF.Abs(determinant) <= Singular)
        {
            return null;
        }

        // Cramer's rule, column by column, which needs no inverse written out.
        float inverse = 1f / determinant;

        return (
            Determinant(wanted, second, third) * inverse,
            Determinant(first, wanted, third) * inverse,
            Determinant(first, second, wanted) * inverse);
    }

    /// <summary>The rotational part of one body's contribution: <c>r × (J ⊙ (r × axis))</c>.</summary>
    private static (float X, float Y, float Z) Twist(
        (float X, float Y, float Z) arm,
        (float X, float Y, float Z) inverseInertia,
        (float X, float Y, float Z) axis)
    {
        (float X, float Y, float Z) turn = Cross(arm, axis);

        return Cross(
            arm,
            (turn.X * inverseInertia.X, turn.Y * inverseInertia.Y, turn.Z * inverseInertia.Z));
    }

    private static float Determinant(
        (float X, float Y, float Z) first,
        (float X, float Y, float Z) second,
        (float X, float Y, float Z) third) =>
        (first.X * ((second.Y * third.Z) - (second.Z * third.Y))) -
        (second.X * ((first.Y * third.Z) - (first.Z * third.Y))) +
        (third.X * ((first.Y * second.Z) - (first.Z * second.Y)));

    private static (float X, float Y, float Z) Subtract(
        (float X, float Y, float Z) from, (float X, float Y, float Z) amount) =>
        (from.X - amount.X, from.Y - amount.Y, from.Z - amount.Z);

    private static (float X, float Y, float Z) Cross(
        (float X, float Y, float Z) first, (float X, float Y, float Z) second) =>
        ((first.Y * second.Z) - (first.Z * second.Y),
         (first.Z * second.X) - (first.X * second.Z),
         (first.X * second.Y) - (first.Y * second.X));

    /// <summary>Below this the effective-mass matrix has no inverse worth taking.</summary>
    private const float Singular = 1e-12f;
}

/// <summary>
/// A ball-and-socket's rows, built once by slot 3 and reused by every slot-4 sweep — <c>FUN_180038620</c>'s translation block
/// and <c>FUN_180038070</c> (B369, D172).
/// </summary>
/// <remarks>
/// **Read from the disassembly** (2026-09-16), the geometry block at <c>arg2</c>:
/// <code>
/// +0x00  error = anchorB_world − anchorA_world        slot 3 only; slot 4 reuses it
/// +0x10  c_k × armA  (k = the core's three axes)     turns A's core-frame spin into a world velocity at the anchor
/// +0x40  armB × c_k                                   the same for B, negated
/// +0x70  (c_k × armA)_i · invI_A[k]                    A's spin per unit impulse
/// +0xa0  (armB × c_k)_i · invI_B[k]
/// +0xd0  K⁻¹ = (r1×r2, r2×r0, r0×r1) / (r2·(r0×r1)),  K = Σ invI-weighted outer products + invMass on the diagonal
/// rel    = (vA + Σ ωA_k·(c_k × armA)) − vB + Σ ωB_k·(armB × c_k)
/// T      = K⁻¹·((x·invStep)·error − y·rel);   ωA += +0x70·T, vA += invMassA·T;   ωB += +0xa0·T, vB −= invMassB·T
/// </code>
/// <c>c_k</c> is column <c>k</c> of the core's matrix at <c>+0x90</c> and each arm is the anchor's world point less the core's
/// event position at <c>+0xf0</c>. **The spin is the CORE's**, so the impulse's angular half is turned into the core's frame
/// by the columns. *<c>IvpBallSocket.Solve</c> crossed a world arm with the spin as if it were a world vector, and rebuilt the error every sweep.*
///
/// **The anchors are taken in the core's frame**, the object offset already added; the engine composes the offset into the
/// transform's translation first, which differs from this only in rounding. **The determinant is not guarded**: the engine
/// reciprocates it with <c>rcpps</c> and one Newton step, which a plain divide stands in for.
/// </remarks>
public sealed class IvpBallSocketRows
{
    private (float X, float Y, float Z) _error;
    private readonly (float X, float Y, float Z)[] _spinA = new (float, float, float)[3];
    private readonly (float X, float Y, float Z)[] _spinB = new (float, float, float)[3];
    private readonly (float X, float Y, float Z)[] _responseA = new (float, float, float)[3];
    private readonly (float X, float Y, float Z)[] _responseB = new (float, float, float)[3];
    private (float X, float Y, float Z) _inverse0;
    private (float X, float Y, float Z) _inverse1;
    private (float X, float Y, float Z) _inverse2;

    /// <summary>Measures the error and builds the rows — slot 3's half.</summary>
    /// <param name="a">The reference body.</param>
    /// <param name="b">The attached body.</param>
    /// <param name="anchorA">The joint in <paramref name="a"/>'s core frame.</param>
    /// <param name="anchorB">The joint in <paramref name="b"/>'s core frame.</param>
    /// <exception cref="ArgumentNullException">A body is null.</exception>
    public void Build(IvpRigidBody a, IvpRigidBody b, (float X, float Y, float Z) anchorA, (float X, float Y, float Z) anchorB)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        (float X, float Y, float Z) worldA = World(a.CoreMatrix, anchorA);
        (float X, float Y, float Z) worldB = World(b.CoreMatrix, anchorB);

        _error = (worldB.X - worldA.X, worldB.Y - worldA.Y, worldB.Z - worldA.Z);

        (float X, float Y, float Z) row0 = default;
        (float X, float Y, float Z) row1 = default;
        (float X, float Y, float Z) row2 = default;

        Side(a, worldA, _spinA, _responseA, negated: false, ref row0, ref row1, ref row2);
        Side(b, worldB, _spinB, _responseB, negated: true, ref row0, ref row1, ref row2);

        (float X, float Y, float Z) cross01 = Cross(row0, row1);
        float inverse = 1f / Dot(row2, cross01);

        _inverse0 = Scale(Cross(row1, row2), inverse);
        _inverse1 = Scale(Cross(row2, row0), inverse);
        _inverse2 = Scale(cross01, inverse);
    }

    /// <summary>One sweep against the cached rows — slot 4, and the solve half of slot 3.</summary>
    /// <param name="a">The reference body.</param>
    /// <param name="b">The attached body.</param>
    /// <param name="errorGain"><c>+0x150 · x · invStep</c>.</param>
    /// <param name="velocityGain"><c>+0x14c · y</c>.</param>
    /// <exception cref="ArgumentNullException">A body is null.</exception>
    public void Solve(IvpRigidBody a, IvpRigidBody b, float errorGain, float velocityGain)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        // B's rows are `armB × c_k`, so its spin ADDS after its velocity is taken off: `((velA − vB) + ωB.z·row2) + …`.
        (float X, float Y, float Z) omegaB = b.AngularVelocity;
        (float X, float Y, float Z) relative = Add(
            Add(
                Add(Subtract(PointVelocity(a, _spinA), b.Velocity), Scale(_spinB[2], omegaB.Z)),
                Scale(_spinB[1], omegaB.Y)),
            Scale(_spinB[0], omegaB.X));

        (float X, float Y, float Z) combined = (
            (errorGain * _error.X) - (velocityGain * relative.X),
            (errorGain * _error.Y) - (velocityGain * relative.Y),
            (errorGain * _error.Z) - (velocityGain * relative.Z));

        (float X, float Y, float Z) torque = Add(
            Add(Scale(_inverse0, combined.X), Scale(_inverse1, combined.Y)), Scale(_inverse2, combined.Z));

        if (!a.Immovable)
        {
            Apply(a, _responseA, torque, a.InverseMass);
        }

        if (!b.Immovable)
        {
            Apply(b, _responseB, torque, -b.InverseMass);
        }
    }

    /// <summary>One body's rows, and its share of <c>K</c> — zero for a body the <c>0x12</c> flags hold still.</summary>
    private static void Side(
        IvpRigidBody body,
        (float X, float Y, float Z) world,
        (float X, float Y, float Z)[] spin,
        (float X, float Y, float Z)[] response,
        bool negated,
        ref (float X, float Y, float Z) row0,
        ref (float X, float Y, float Z) row1,
        ref (float X, float Y, float Z) row2)
    {
        if (body.Immovable)
        {
            Array.Clear(spin);
            Array.Clear(response);

            return;
        }

        IvpMatrix m = body.CoreMatrix;
        (float X, float Y, float Z) arm = (
            world.X - (float)m.Translation.X, world.Y - (float)m.Translation.Y, world.Z - (float)m.Translation.Z);

        (float X, float Y, float Z)[] columns =
        [
            ((float)m.M0, (float)m.M4, (float)m.M8),
            ((float)m.M1, (float)m.M5, (float)m.M9),
            ((float)m.M2, (float)m.M6, (float)m.M10),
        ];

        (float X, float Y, float Z) inertia = body.InverseInertia;
        float[] perAxis = [inertia.X, inertia.Y, inertia.Z];

        for (int k = 0; k < 3; k++)
        {
            spin[k] = negated ? Cross(arm, columns[k]) : Cross(columns[k], arm);
        }

        for (int k = 0; k < 3; k++)
        {
            response[k] = Scale(spin[k], perAxis[k]);
        }

        row0 = Add(row0, Quadratic(response, spin, 0, body.InverseMass));
        row1 = Add(row1, Quadratic(response, spin, 1, body.InverseMass));
        row2 = Add(row2, Quadratic(response, spin, 2, body.InverseMass));
    }

    /// <summary>Row <c>i</c> of one body's <c>K</c>: <c>Σ_k response_k[i]·spin_k</c>, its inverse mass on the diagonal.</summary>
    private static (float X, float Y, float Z) Quadratic(
        (float X, float Y, float Z)[] response, (float X, float Y, float Z)[] spin, int i, float inverseMass)
    {
        (float X, float Y, float Z) sum = Add(
            Add(Scale(spin[2], Lane(response[2], i)), Scale(spin[1], Lane(response[1], i))),
            Scale(spin[0], Lane(response[0], i)));

        return i switch
        {
            0 => (sum.X + inverseMass, sum.Y, sum.Z),
            1 => (sum.X, sum.Y + inverseMass, sum.Z),
            _ => (sum.X, sum.Y, sum.Z + inverseMass),
        };
    }

    /// <summary>A's world velocity at the anchor: <c>((ω.z·row2 + v) + ω.y·row1) + ω.x·row0</c>.</summary>
    private static (float X, float Y, float Z) PointVelocity(IvpRigidBody body, (float X, float Y, float Z)[] spin)
    {
        (float X, float Y, float Z) omega = body.AngularVelocity;

        return Add(Add(Add(Scale(spin[2], omega.Z), body.Velocity), Scale(spin[1], omega.Y)), Scale(spin[0], omega.X));
    }

    /// <summary>A core's spin and velocity take one impulse: <c>ω_k += Σ_i response_k[i]·T_i</c>.</summary>
    private static void Apply(
        IvpRigidBody body, (float X, float Y, float Z)[] response, (float X, float Y, float Z) torque, float inverseMass)
    {
        (float X, float Y, float Z) omega = body.AngularVelocity;

        body.AngularVelocity = (
            omega.X + Dot(response[0], torque),
            omega.Y + Dot(response[1], torque),
            omega.Z + Dot(response[2], torque));

        (float X, float Y, float Z) v = body.Velocity;

        body.Velocity = (v.X + (inverseMass * torque.X), v.Y + (inverseMass * torque.Y), v.Z + (inverseMass * torque.Z));
    }

    /// <summary>An anchor put into the world: <c>((a.z·c2 + t) + a.y·c1) + a.x·c0</c>, in float.</summary>
    private static (float X, float Y, float Z) World(IvpMatrix m, (float X, float Y, float Z) anchor) =>
        (((anchor.Z * (float)m.M2) + (float)m.Translation.X + (anchor.Y * (float)m.M1)) + (anchor.X * (float)m.M0),
         ((anchor.Z * (float)m.M6) + (float)m.Translation.Y + (anchor.Y * (float)m.M5)) + (anchor.X * (float)m.M4),
         ((anchor.Z * (float)m.M10) + (float)m.Translation.Z + (anchor.Y * (float)m.M9)) + (anchor.X * (float)m.M8));

    private static float Lane((float X, float Y, float Z) v, int i) => i switch { 0 => v.X, 1 => v.Y, _ => v.Z };

    private static float Dot((float X, float Y, float Z) l, (float X, float Y, float Z) r) =>
        ((l.Y * r.Y) + (l.X * r.X)) + (l.Z * r.Z);

    private static (float X, float Y, float Z) Cross((float X, float Y, float Z) l, (float X, float Y, float Z) r) =>
        ((l.Y * r.Z) - (l.Z * r.Y), (l.Z * r.X) - (l.X * r.Z), (l.X * r.Y) - (l.Y * r.X));

    private static (float X, float Y, float Z) Scale((float X, float Y, float Z) v, float s) => (v.X * s, v.Y * s, v.Z * s);

    private static (float X, float Y, float Z) Add((float X, float Y, float Z) l, (float X, float Y, float Z) r) =>
        (l.X + r.X, l.Y + r.Y, l.Z + r.Z);

    private static (float X, float Y, float Z) Subtract((float X, float Y, float Z) l, (float X, float Y, float Z) r) =>
        (l.X - r.X, l.Y - r.Y, l.Z - r.Z);
}
