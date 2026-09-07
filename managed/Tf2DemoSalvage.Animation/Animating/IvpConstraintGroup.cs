using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// A ragdoll joint bound to the two bodies it holds together (B58, D146).
/// </summary>
/// <remarks>
/// **The Jacobian rows are cached once per tick and reused by every sweep**, which is the whole
/// reason the engine splits the constraint vtable into an expensive slot 3 and a cheap slot 4:
/// `FUN_18003c780` calls slot 3 once with a weight of `1.0` and then slot 4 twice per iteration
/// with the relaxation weight. <see cref="Rebuild"/> is slot 3; <see cref="Sweep"/> is slot 4.
/// </remarks>
public sealed class IvpRagdollJoint
{
    /// <summary>The reference body.</summary>
    public required IvpRigidBody BodyA { get; init; }

    /// <summary>The attached body.</summary>
    public required IvpRigidBody BodyB { get; init; }

    /// <summary>The joint's frames and its three limits.</summary>
    public required IvpRagdollConstraint Constraint { get; init; }

    /// <summary>Where the joint sits in <see cref="BodyA"/>'s own space.</summary>
    /// <remarks>
    /// **The origin, because the reference body IS the child and the constraint is built at its
    /// own position.** `CreateRagdollConstraint( childElement.pObject, ragdoll.list[parentIndex]
    /// .pObject, … )` (`ragdoll_shared.cpp:253`) makes the child the reference, and a child's
    /// frame is centred on itself.
    /// </remarks>
    public (float X, float Y, float Z) AnchorA { get; init; }

    /// <summary>And where it sits in <see cref="BodyB"/>'s.</summary>
    /// <remarks>
    /// **`RagdollElement.OriginParentSpace`** — the child's origin expressed in the parent's space,
    /// which is the same point from the other end. A scout's knee sits 20.52 units along its hip's
    /// own X.
    /// </remarks>
    public (float X, float Y, float Z) AnchorB { get; init; }

    /// <summary>Called once per sweep, for tests that need to observe the ORDER.</summary>
    /// <remarks>
    /// **This exists because the sweep order is a finding, not an implementation detail.** Two
    /// iterations of descending-then-ascending is what stops a chain of joints biasing toward
    /// whichever end is solved first, and nothing about a corpse's final pose says which order
    /// produced it — so the order needs an instrument of its own.
    /// </remarks>
    public Action? Solved { get; init; }

    /// <summary>Rebuilds the cached rows — the constraint vtable's slot 3.</summary>
    /// <remarks>
    /// **Each axis's Jacobian is built around a different vector**, all three read out of
    /// `FUN_180036e10`'s dispatch: the twist solves about the bisector cached at `geom+0x110`, and
    /// each swing about the cross of the shared basis with its own axis — `cross(B[primary],
    /// A[k])`, which `FUN_1800372c0` builds and then retires when it goes degenerate.
    /// </remarks>
    public void Rebuild()
    {
        Constraint.Measure(BodyA, BodyB);

        (float X, float Y, float Z) primaryA = Rotate(BodyA, Constraint.FrameA.Primary);
        (float X, float Y, float Z) widerA = Rotate(BodyA, Constraint.FrameA.Wider);
        (float X, float Y, float Z) primaryB = Rotate(BodyB, Constraint.FrameB.Primary);

        (float X, float Y, float Z) bisector = (
            primaryA.X + primaryB.X, primaryA.Y + primaryB.Y, primaryA.Z + primaryB.Z);

        _twist = IvpJacobian.Build(BodyA, BodyB, bisector);
        _cone = IvpJacobian.Build(BodyA, BodyB, Cross(primaryB, primaryA));
        _swing = IvpJacobian.Build(BodyA, BodyB, Cross(primaryB, widerA));
    }

    /// <summary>Runs one relaxation sweep — the constraint vtable's slot 4.</summary>
    /// <param name="weight">This pass's relaxation weight.</param>
    /// <remarks>
    /// **The deflections are re-measured every sweep and the rows are not.** That is the split the
    /// engine makes, and it is what makes two sweeps mean something rather than two identical
    /// corrections: the other axes have changed these velocities since the row was built.
    /// </remarks>
    public void Sweep(float weight)
    {
        Constraint.Measure(BodyA, BodyB);

        // **The twist takes a different routine from the other two, and the difference is live.**
        // `FUN_180036e10` dispatches the bisector axis to `FUN_180036f80` and both swings to
        // `FUN_1800372c0`, which adds the overshoot where the first subtracts it. Passing the same
        // routine for all three drives two of the joint's axes backwards.
        IvpAngularLimit.Solve(
            BodyA, BodyB, Constraint.Twist, _twist,
            RateGain, weight, AxisScale, IvpAngularLimit.Routine.Bisector);

        IvpAngularLimit.Solve(
            BodyA, BodyB, Constraint.Cone, _cone,
            RateGain, weight, AxisScale, IvpAngularLimit.Routine.Swing);

        IvpAngularLimit.Solve(
            BodyA, BodyB, Constraint.Swing, _swing,
            RateGain, weight, AxisScale, IvpAngularLimit.Routine.Swing);

        // **And then the translation, which is what holds the two bodies together.** The engine
        // does it here, in the same order — `FUN_180036e10` dispatches the three axes and returns,
        // and `FUN_180038620` runs the ball-and-socket immediately after, behind a flag its own
        // template sets. See `IvpBallSocket`.
        IvpBallSocket.Solve(BodyA, BodyB, AnchorA, AnchorB, weight);

        Solved?.Invoke();
    }

    /// <summary>How far ahead the deflection is predicted — <c>param_1[0]</c>.</summary>
    /// <remarks>
    /// **Zero until its provenance is read.** It arrives in the per-sweep vector the driver builds,
    /// and what the driver puts there is not established. At zero the solve clamps the CURRENT
    /// deflection rather than the predicted one, which is a joint that resists a limit it has
    /// already broken instead of stopping short of it — a real difference, and a smaller one than
    /// inventing a number would be.
    /// </remarks>
    private const float RateGain = 0f;

    /// <summary>The per-axis scale at flag-block <c>+0xc</c>, which the writer sets to 1.</summary>
    /// <remarks><c>local_ec = 0x3f800000</c> in `FUN_18000eac0`, once per axis.</remarks>
    private const float AxisScale = 1f;

    private IvpJacobian _twist;
    private IvpJacobian _cone;
    private IvpJacobian _swing;

    private static (float X, float Y, float Z) Rotate(
        IvpRigidBody body, (float X, float Y, float Z) axis) =>
        IvpQuaternion.Rotate(body.Orientation, axis);

    private static (float X, float Y, float Z) Cross(
        (float X, float Y, float Z) left, (float X, float Y, float Z) right) =>
        (
            (left.Y * right.Z) - (left.Z * right.Y),
            (left.Z * right.X) - (left.X * right.Z),
            (left.X * right.Y) - (left.Y * right.X));
}

/// <summary>
/// The relaxation a ragdoll's joints are solved with — <c>FUN_18003c780</c> (B58, D146).
/// </summary>
/// <remarks>
/// **Symmetric Gauss-Seidel: each iteration walks the constraint list forwards and then
/// backwards.** Solving a chain in one direction leaves the far end doing all the correcting, and
/// nothing about the resulting pose says so — which is why the order is transcribed rather than
/// simplified.
///
/// **Two iterations, and it is a floor.** `FUN_18003c330` stores `additionalIterations + 2` and
/// `FUN_18003d240` reads back `- 2`, an exact inverse pair that fixes the base at 2; a ragdoll is
/// built with `group.Defaults()`, which leaves `additionalIterations` at zero
/// (`ragdoll_shared.cpp:274-276`).
///
/// **`errorTolerance` and `minErrorTicks` do NOT gate this loop**, which is what a reader would
/// assume. They drive a counter afterwards, and that counter is what `IsInErrorState` reports —
/// the call `CRagdoll::VPhysicsUpdate` makes before running `RagdollSolveSeparation`.
/// </remarks>
public sealed class IvpConstraintGroup
{
    /// <summary>The joints this group solves, in order.</summary>
    public IList<IvpRagdollJoint> Joints { get; } = [];

    /// <summary>Iterations beyond the base two — zero for every TF2 ragdoll.</summary>
    public int AdditionalIterations { get; init; }

    /// <summary>How many relaxation iterations run.</summary>
    public int Iterations => AdditionalIterations + BaseIterations;

    /// <summary>Rebuilds every joint's rows and runs the relaxation.</summary>
    /// <remarks>
    /// **The rebuild happens once, before any sweep**, and the driver passes it a weight of `1.0`
    /// rather than the relaxation weight — visible at its call site, where slot 3 is handed `XMM12`
    /// and the two slot-4 calls are handed the table entry.
    ///
    /// **A weight of zero ends the loop early.** The engine breaks out of its iteration loop on
    /// `fVar23 == 0.0`, so a table shorter than the iteration count stops the relaxation instead of
    /// reading past it.
    /// </remarks>
    public void Solve()
    {
        for (int index = 0; index < Joints.Count; index++)
        {
            Joints[index].Rebuild();
        }

        for (int iteration = 0; iteration < Iterations; iteration++)
        {
            float weight = Weight(iteration);

            if (weight == 0f)
            {
                break;
            }

            for (int index = Joints.Count - 1; index >= 0; index--)
            {
                Joints[index].Sweep(weight);
            }

            for (int index = 0; index < Joints.Count; index++)
            {
                Joints[index].Sweep(weight);
            }
        }
    }

    /// <summary>This iteration's relaxation weight, from the table at <c>0x1800eeb70</c>.</summary>
    /// <remarks>
    /// **Dumped verbatim**: `0.4, 0.4, 0.4, 0.4, 1.0, 1.0, 0.8, 0.6, 0.8, 0.8, 0.8, 0.8`. At the
    /// stock two iterations both entries are `0.4`; the rest are carried because the engine has
    /// them and `additionalIterations` is a public field on the group parameters.
    ///
    /// **Past the table the weight is zero**, which the driver treats as "stop", so a group asking
    /// for more iterations than the table holds simply stops relaxing rather than reading past it.
    /// </remarks>
    private static float Weight(int iteration) =>
        iteration >= 0 && iteration < Weights.Length ? Weights[iteration] : 0f;

    /// <summary>The base every group starts from, before <c>additionalIterations</c>.</summary>
    private const int BaseIterations = 2;

    /// <summary><c>0x1800eeb70</c>, dumped.</summary>
    private static readonly float[] Weights =
        [0.4f, 0.4f, 0.4f, 0.4f, 1f, 1f, 0.8f, 0.6f, 0.8f, 0.8f, 0.8f, 0.8f];
}
