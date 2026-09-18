using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>The constraint solve in each core's own frame — <c>FUN_180037bd0</c> and <c>FUN_180038070</c> (B369, D172).</summary>
/// <remarks>Synthetic conformance (D38); the reading is in <c>docs/findings/51</c>, *A ragdoll in IVP space*.</remarks>
public sealed class IvpCoreFrameSolveTests
{
    /// <remarks>
    /// **World→core is the matrix's transpose.** A core turned 90° about Z sees world +Y as its own +X; the forward rotation
    /// would give −X.
    /// </remarks>
    [Test]
    public void BuildInCore_ACoreTurnedAboutZ_TakesTheWorldAxisIntoItsOwnFrame()
    {
        (double X, double Y, double Z, double W) turned = (0d, 0d, System.Math.Sqrt(0.5d), System.Math.Sqrt(0.5d));
        IvpRigidBody a = new() { CoreMatrix = IvpMatrix.FromRotation(turned, (0d, 0d, 0d)) };
        IvpRigidBody b = new();

        IvpJacobian rows = IvpJacobian.BuildInCore(a, b, (0f, 1f, 0f));

        rows.AxisA.X.ShouldBe(1f, 1e-6f);
        rows.AxisA.Y.ShouldBe(0f, 1e-6f);
        rows.AxisB.Y.ShouldBe(-1f, 1e-6f);
    }

    /// <remarks>
    /// **One velocity-only solve with the exact <c>K⁻¹</c> stops the anchors' relative motion**, and body B's spin must enter
    /// that motion with its own sign: B turns about Z with its anchor one unit along X, so its anchor moves along +Y.
    /// </remarks>
    [Test]
    public void Solve_BodyBSpinningAboutTheAnchor_LeavesNoRelativeVelocityAtTheAnchor()
    {
        IvpRigidBody a = new() { CoreMatrix = IvpMatrix.FromRotation((0d, 0d, 0d, 1d), (0d, 0d, 0d)) };
        IvpRigidBody b = new()
        {
            CoreMatrix = IvpMatrix.FromRotation((0d, 0d, 0d, 1d), (-1d, 0d, 0d)),
            AngularVelocity = (0f, 0f, 2f),
        };
        IvpBallSocketRows rows = new();

        rows.Build(a, b, (0f, 0f, 0f), (1f, 0f, 0f));
        rows.Solve(a, b, errorGain: 0f, velocityGain: 1f);

        // Anchor velocities: A's is vA + ωA × armA (arm zero), B's is vB + ωB × (1, 0, 0).
        (float X, float Y, float Z) spinB = b.AngularVelocity;
        (float X, float Y, float Z) anchorB = (
            b.Velocity.X + ((spinB.Y * 0f) - (spinB.Z * 0f)),
            b.Velocity.Y + ((spinB.Z * 1f) - (spinB.X * 0f)),
            b.Velocity.Z + ((spinB.X * 0f) - (spinB.Y * 1f)));

        a.Velocity.X.ShouldBe(anchorB.X, 1e-4f);
        a.Velocity.Y.ShouldBe(anchorB.Y, 1e-4f);
        a.Velocity.Z.ShouldBe(anchorB.Z, 1e-4f);
        a.Velocity.Y.ShouldNotBe(0f, "B's spin moved its anchor, so the solve must have pushed A to follow");
    }
}
