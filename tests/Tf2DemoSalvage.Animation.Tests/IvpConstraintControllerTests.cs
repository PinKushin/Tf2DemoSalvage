using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>A constraint group as a unit's controller — priority 405, <c>FUN_18003c780</c> (B369, D172).</summary>
/// <remarks>
/// **Read from the disassembly** (`docs/findings/51`): slot 5 answers <c>0x195</c> and slot 4 tail-calls the group's own solve.
/// Synthetic conformance (D38).
/// </remarks>
public sealed class IvpConstraintControllerTests
{
    [Test]
    public void Priority_TheConstraints_Is405()
    {
        new IvpConstraintController(new IvpConstraintGroup()).Priority.ShouldBe(405);
    }

    /// <remarks>**Between friction's 600 pass and its 0 pass**, which is the order a unit's entries give.</remarks>
    [Test]
    public void Priority_TheConstraints_SitBetweenTheTwoFrictionPasses()
    {
        IvpSimulationUnit unit = new();
        IvpFrictionSystem system = new(Environment());
        unit.AddController(new IvpNormalFrictionController(system));
        unit.AddController(new IvpConstraintController(new IvpConstraintGroup()));
        unit.AddController(new IvpFrictionController(system));

        // Ascending by priority, and the PSI walks them last first.
        unit.Entries[0].Controller.Priority.ShouldBe(0);
        unit.Entries[1].Controller.Priority.ShouldBe(405);
        unit.Entries[2].Controller.Priority.ShouldBe(600);
    }

    /// <remarks>
    /// **The group solves ANGULAR limits**, so the body past its swing limit is what shows the controller ran — a separation is
    /// not what these joints correct.
    /// </remarks>
    [Test]
    public void Advance_AJointPastItsSwingLimit_CorrectsIt()
    {
        IvpRigidBody first = new();

        // 40 degrees about the narrower swing axis, against a limit of 25 — the same fixture the group's own tests use. The solve
        // reads the rotation a PSI stands at, `+0x1a0` and the matrix built from it, not `+0x180`.
        (double X, double Y, double Z, double W) turned = (0d, 0.34202015d, 0d, 0.9396926d);
        IvpRigidBody second = new()
        {
            Orientation = turned,
            WorkingOrientation = turned,
            CoreMatrix = IvpMatrix.FromRotation(turned, (0d, 0d, 0d)),
        };
        IvpConstraintGroup group = new();
        group.Joints.Add(new IvpRagdollJoint
        {
            BodyA = first,
            BodyB = second,
            Constraint = IvpRagdollConstraint.FromDegrees(
                primary: (-30f, 15f),
                narrower: (-25f, 25f),
                wider: (-79f, 57f),
                reference: IvpConstraintFrame.Identity,
                attached: IvpConstraintFrame.Identity),
        });

        new IvpConstraintController(group).Advance(new IvpSimulationUnit(), [], psiStep: 0.5f);

        first.AngularVelocity.ShouldNotBe((0f, 0f, 0f), "the limit was exceeded, so the controller's solve corrected it");
    }

    private static IvpImpactEnvironment Environment() =>
        new()
        {
            InverseStep = 2d,
            Step = 0.5d,
            Limits = new IvpAnomalyLimits(1000f, 0, 1000f, 250, 0f, 0f),
            Anomalies = ThrowingAnomalies.Instance,
            Materials = IvpFrictionSystemRevalidatePairTests.Materials.Instance,
        };

    private sealed class ThrowingAnomalies : IIvpAnomalyManager
    {
        public static readonly ThrowingAnomalies Instance = new();

        public void MaximumVelocityExceeded(IvpAnomalyLimits limits, IvpRigidBody core, ref (float X, float Y, float Z) velocity) =>
            throw new System.InvalidOperationException("Not needed for a controller-ordering test.");

        public void MaximumAngularVelocityExceeded(
            IvpAnomalyLimits limits, IvpRigidBody core, double inverseStep, ref (float X, float Y, float Z) spin) =>
            throw new System.InvalidOperationException("Not needed for a controller-ordering test.");

        public bool MaximumContactsExceeded(System.Collections.Generic.IReadOnlyList<IvpRigidBody> cores) =>
            throw new System.InvalidOperationException("Not needed for a controller-ordering test.");

        public bool MaximumCollisionsExceededCheckFreezing(IvpAnomalyLimits limits, IvpRigidBody core) =>
            throw new System.InvalidOperationException("Not needed for a controller-ordering test.");
    }
}
