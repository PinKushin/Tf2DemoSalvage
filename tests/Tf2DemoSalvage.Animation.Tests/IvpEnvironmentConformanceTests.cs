using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// The simulation an IVP environment runs each step (B58, D146).
/// </summary>
/// <remarks>
/// **The order is established from the binary, not chosen.** Gravity is applied by a per-environment
/// controller (`FUN_180074c80`) that accumulates into velocity; the constraint solve then adjusts
/// that same velocity; and the integrator (`FUN_180099a00`) reads it last. Each stage is transcribed
/// on its own elsewhere — this covers the assembly.
///
/// **Everything a viewer reads is one step old, twice over**, and the assembly is where that becomes
/// visible: a body gains speed on the step gravity is applied and MOVES on the next one, because the
/// integrator advances position by `PreviousVelocity`.
/// </remarks>
public sealed class IvpEnvironmentConformanceTests
{
    private const double Close = 1e-4;

    /// <remarks>
    /// **Valve's own step, and the reason it is not the frame time.** `PhysicsLevelInit` calls
    /// `physenv->SetSimulationTimestep( gpGlobals->interval_per_tick )` under the comment *"Always
    /// run client physics at this rate - helps keep ragdolls stable"* (`physics.cpp:177-180`), so a
    /// viewer drawing at 300 frames a second still steps physics at the demo's tick rate.
    /// </remarks>
    [Test]
    public void Simulate_OverOneStep_LeavesTheBodyStillButMoving()
    {
        IvpEnvironment environment = new(1f / 66f);
        IvpRigidBody body = new();

        environment.Add(body);
        environment.Simulate();

        body.Velocity.Z.ShouldBe(-800f / 66f, Close, "gravity reached it");

        body.Position.Z.ShouldBe(
            0d, "but it has not moved yet — the integrator advances by the PREVIOUS velocity");
    }

    /// <remarks>
    /// **The second step is where it moves**, by the speed the first step gave it. That one-step lag
    /// is `FUN_180099a00`'s own ordering and is not an artefact of this assembly.
    /// </remarks>
    [Test]
    public void Simulate_OverTwoSteps_MovesByTheFirstStepsVelocity()
    {
        IvpEnvironment environment = new(1f / 66f);
        IvpRigidBody body = new();

        environment.Add(body);
        environment.Simulate();
        environment.Simulate();

        body.Position.Z.ShouldBe(-800d / 66d / 66d, Close);
    }

    /// <remarks>
    /// **An immovable body is not integrated at all** — the island driver skips a core whose flag
    /// byte carries the bit (`FUN_1800909d0`), and the contact builder gives it no mass or inertia.
    /// That is how static map geometry is represented: an ordinary body with a bit set, not a
    /// separate type.
    ///
    /// **The body below is given a VELOCITY, and the first version of this test did not.** Sabotage
    /// caught that: with the skip inverted, a zero-velocity body stays at the origin whether it is
    /// integrated or not, so the assertion passed against broken code. A body that would move if
    /// stepped is the only input that separates "skipped" from "stepped and went nowhere".
    /// </remarks>
    [Test]
    public void Simulate_WithAnImmovableBody_LeavesItExactlyWhereItWas()
    {
        IvpEnvironment environment = new(1f / 66f);

        IvpRigidBody floor = new()
        {
            Immovable = true,
            SkipsGravity = true,
            Velocity = (50f, 0f, 0f),
            PreviousVelocity = (50f, 0f, 0f),
        };

        environment.Add(floor);
        environment.Simulate();
        environment.Simulate();

        floor.Position.ShouldBe((0d, 0d, 0d), "stepped, it would have travelled");
        floor.LastStepped.ShouldBe(0d, "and its clock is never advanced either");
    }

    /// <remarks>
    /// **The clock advances by the step, and each body records when it was last touched** —
    /// `core+0x1d0 = env+0x188`. It matters because the integrator derives its POSITION delta from
    /// that difference rather than from the nominal step, so a body that missed steps catches up in
    /// one longer move.
    /// </remarks>
    [Test]
    public void Simulate_OverSeveralSteps_AdvancesTheClockByTheStepEachTime()
    {
        IvpEnvironment environment = new(0.5f);
        IvpRigidBody body = new();

        environment.Add(body);

        environment.Simulate();
        environment.Now.ShouldBe(0.5d, Close);

        environment.Simulate();
        environment.Now.ShouldBe(1d, Close);

        body.LastStepped.ShouldBe(1d, Close);
    }

    /// <remarks>
    /// **A body added late is stepped from the clock it joined at, not from zero.** Without that its
    /// first position delta would be the environment's whole lifetime and it would be flung across
    /// the map — the failure mode a corpse created mid-demo would hit every time.
    /// </remarks>
    [Test]
    public void Add_AfterTheClockHasRun_StartsTheBodyFromTheCurrentTime()
    {
        IvpEnvironment environment = new(0.5f);

        environment.Simulate();
        environment.Simulate();

        IvpRigidBody late = new() { Velocity = (10f, 0f, 0f), PreviousVelocity = (10f, 0f, 0f) };

        environment.Add(late);

        late.LastStepped.ShouldBe(1d, Close, "it joined at the current time, not at zero");

        environment.Simulate();

        late.Position.X.ShouldBe(5d, Close, "one step of 0.5s at 10 units a second");
    }

    /// <remarks>
    /// **Gravity is opt-out per body**, because in the engine it is membership of the controller's
    /// list rather than a property of the body — `EnableGravity` adds and removes.
    /// </remarks>
    [Test]
    public void Simulate_WithABodyThatSkipsGravity_StillIntegratesIt()
    {
        IvpEnvironment environment = new(0.5f);
        IvpRigidBody floating = new() { SkipsGravity = true, Velocity = (4f, 0f, 0f) };

        environment.Add(floating);
        environment.Simulate();
        environment.Simulate();

        floating.Velocity.Z.ShouldBe(0f, "no gravity");
        floating.Position.X.ShouldBe(2d, Close, "but it still moves under its own velocity");
    }
}
