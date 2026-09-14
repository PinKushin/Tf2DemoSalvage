using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// A body's transform at a moment inside a step — <c>FUN_1800734e0</c> — and the fields it reads (B369).
/// </summary>
/// <remarks>
/// **The time-of-impact search evaluates both bodies at lattice times inside the step**, so this is
/// what "where is the limb at t" means to IVP. Read from `vphysics.dll` (`docs/findings/51`):
///
/// <code>
///   position(t) = core+0x150 + core+0x170 × (float)(t − core+0x1d0)
///   rotation(t) = interpolate(core+0x180, core+0x1a0, (float)(t − core+0x1d0) × core+0x1d8)
/// </code>
///
/// `core+0x1d8` is the inverse of the step, written by two paths that differ in one respect:
///
/// <code>
///   integrator (FUN_180099a00, both callers):  dt ≤ 1e-10 ? 1e10 : (float)(1.0 / dt)
///   sleep reset (FUN_180078bd0 from env+0x110): (float)(1.0 / step), unguarded
/// </code>
///
/// **The reset also discards what a sleeping core must not carry.** It zeroes the staged velocities at
/// `core+0x110` and `+0x120` as well as the three real ones, and copies `0x1a0 := 0x180` so both ends of
/// the rotation are the same and a sleeping body stays put mid-step. This project's sleep did neither.
/// </remarks>
public sealed class IvpRigidBodyTransformAtConformanceTests
{
    private const float Tolerance = 1e-5f;

    private static IvpRigidBody Moving() => new()
    {
        Position = (10d, 20d, 30d),
        PreviousVelocity = (2f, 4f, -6f),
        Orientation = (0f, 0f, 0f, 1f),
        WorkingOrientation = (0f, 0f, 0.70710677f, 0.70710677f),
        LastStepped = 5d,
        InverseStep = 10f,
    };

    /// <remarks>
    /// `1.0 / 0.015` taken in double and narrowed, as `FUN_1800909d0` does.
    /// </remarks>
    [Test]
    public void Step_WithAFifteenMillisecondStep_SetsInverseStepToItsReciprocal()
    {
        IvpRigidBody body = new();

        IvpIntegrator.Step(body, 0.015d, 0.015f, phase: 0);

        body.InverseStep.ShouldBe((float)(1.0 / (double)0.015f));
    }

    /// <remarks>
    /// **The guard is the island driver's, `DAT_1800fcfa0` = `1e-10`.** A step of zero would otherwise
    /// divide to infinity; the engine substitutes `1e10` instead.
    /// </remarks>
    [Test]
    public void Step_WithAVanishingStep_SetsInverseStepToTenBillion()
    {
        IvpRigidBody body = new();

        IvpIntegrator.Step(body, 0d, 0f, phase: 0);

        body.InverseStep.ShouldBe(1e10f);
    }

    /// <remarks>
    /// Half a step after the stamp: `(float)(5.05 − 5)` = 0.05, so the position moves by half a step's
    /// committed velocity — `(0.1, 0.2, −0.3)` — and the rotation fraction is `0.05 × 10` = ½, which
    /// takes the identity halfway to a quarter turn about Z: 45°, `(0, 0, sin 22.5°, cos 22.5°)`.
    /// </remarks>
    [Test]
    public void TransformAt_HalfAStepAfterTheStamp_IsHalfwayInBothPositionAndRotation()
    {
        IvpRigidBody body = Moving();

        ((double X, double Y, double Z) position, (double X, double Y, double Z, double W) rotation) =
            body.TransformAt(5.05d);

        position.X.ShouldBe(10.1d, Tolerance);
        position.Y.ShouldBe(20.2d, Tolerance);
        position.Z.ShouldBe(29.7d, Tolerance);

        rotation.Z.ShouldBe(0.38268343f, Tolerance);
        rotation.W.ShouldBe(0.92387953f, Tolerance);
    }

    /// <remarks>
    /// At the stamp both the elapsed time and the fraction are zero: the committed transform exactly.
    /// </remarks>
    [Test]
    public void TransformAt_TheStampItself_IsTheCommittedTransform()
    {
        IvpRigidBody body = Moving();

        ((double X, double Y, double Z) position, (double X, double Y, double Z, double W) rotation) =
            body.TransformAt(5d);

        position.X.ShouldBe(10d);
        position.Y.ShouldBe(20d);
        position.Z.ShouldBe(30d);

        rotation.Z.ShouldBe(0f, Tolerance);
        rotation.W.ShouldBe(1f, Tolerance);
    }

    /// <remarks>
    /// **After the reset a body evaluated mid-step stays at its committed orientation**, because both
    /// ends of the interpolation are now the same rotation. Before it, the same call turns toward the
    /// predicted orientation — which is exactly the divergence this pins.
    /// </remarks>
    [Test]
    public void Sleep_ThenTransformAtHalfAStep_StaysAtTheCommittedOrientation()
    {
        IvpRigidBody body = Moving();

        body.Sleep(0.1f);

        ((double X, double Y, double Z) _, (double X, double Y, double Z, double W) rotation) =
            body.TransformAt(5.05d);

        rotation.Z.ShouldBe(0f, Tolerance);
        rotation.W.ShouldBe(1f, Tolerance);
    }

    /// <remarks>
    /// **The staged velocities go too** (`core+0x110`, `+0x120`), which this project's sleep kept — a
    /// push staged just before sleep would otherwise land on the first step after waking.
    /// </remarks>
    [Test]
    public void Sleep_WithStagedVelocity_DiscardsEveryVelocityIncludingTheStagedOnes()
    {
        IvpRigidBody body = Moving();
        body.Velocity = (1f, 1f, 1f);
        body.AngularVelocity = (1f, 1f, 1f);
        body.PendingVelocity = (3f, 3f, 3f);
        body.PendingAngularVelocity = (3f, 3f, 3f);

        body.Sleep(0.1f);

        body.Velocity.ShouldBe((0f, 0f, 0f));
        body.AngularVelocity.ShouldBe((0f, 0f, 0f));
        body.PreviousVelocity.ShouldBe((0f, 0f, 0f));
        body.PendingVelocity.ShouldBe((0f, 0f, 0f));
        body.PendingAngularVelocity.ShouldBe((0f, 0f, 0f));
    }

    /// <remarks>
    /// **The reset's inverse is `env+0x110`, unguarded** — `1.0 / step` in double, narrowed on the copy.
    /// </remarks>
    [Test]
    public void Sleep_WithAStep_SetsInverseStepFromTheEnvironmentsUnguardedReciprocal()
    {
        IvpRigidBody body = new();

        body.Sleep(0.015f);

        body.InverseStep.ShouldBe((float)(1.0 / (double)0.015f));
    }
}
