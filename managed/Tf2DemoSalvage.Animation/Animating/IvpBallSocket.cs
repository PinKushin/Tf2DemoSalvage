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
        float weight)
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
            (-relative.X) + (weight * error.X),
            (-relative.Y) + (weight * error.Y),
            (-relative.Z) + (weight * error.Z));

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
