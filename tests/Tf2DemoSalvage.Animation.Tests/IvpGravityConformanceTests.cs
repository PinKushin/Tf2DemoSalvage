using System.Collections.Generic;

using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// How gravity reaches a body's velocity in IVP (B58, D146).
/// </summary>
/// <remarks>
/// **Transcribed from `FUN_180074c80`**, which is slot 4 of the gravity controller's vtable
/// (`0x1800ea728`) — a per-environment singleton held at `env+0x0`. Finding it took a published
/// method name rather than pattern matching: `IPhysicsObject::EnableGravity( bool )` must have
/// something to switch, and what it switches turned out to be **membership of the controller's
/// list** rather than a flag.
///
/// <code>
/// if ((*pbVar4 &amp; 0x10) == 0) {
///   …
///   if ((*pbVar4 &amp; 0x20) == 0) { g = controller+0x10/0x14/0x18; }
///   else                        { g = controller+0x20/0x24/0x28; }
///   *(float *)(pbVar4 + 0x140) = g.x * dt + *(float *)(pbVar4 + 0x140);
///   *(float *)(pbVar4 + 0x148) = g.z * dt + *(float *)(pbVar4 + 0x148);
///   *(float *)(pbVar4 + 0x144) = g.y * dt + *(float *)(pbVar4 + 0x144);
/// }
/// </code>
///
/// **`v += g * dt`, with no mass term** — which is what makes it gravity rather than drag: nothing
/// in it is velocity-dependent, and an acceleration must not scale with mass.
/// </remarks>
public sealed class IvpGravityConformanceTests
{
    private const double Close = 1e-5;

    /// <summary>Valve's <c>sv_gravity</c> default, down Z.</summary>
    private static (float X, float Y, float Z) Down => (0f, 0f, -800f);

    /// <remarks>
    /// **A plain constant-acceleration step.** At the client's own physics rate — the demo's tick
    /// interval — one step of 800 units per second squared over 1/66 s is 12.12 units per second.
    /// </remarks>
    [Test]
    public void Apply_ToAnOrdinaryBody_AddsGravityTimesTheStep()
    {
        IvpRigidBody body = new();

        IvpGravity.Apply([body], Down, 1f / 66f);

        body.Velocity.X.ShouldBe(0f);
        body.Velocity.Y.ShouldBe(0f);
        body.Velocity.Z.ShouldBe(-800f / 66f, Close);
    }

    /// <remarks>
    /// **It accumulates rather than assigns**, which is why the engine can apply gravity and a
    /// constraint impulse in the same step and get both. Two steps double it exactly, because
    /// nothing here is velocity-dependent.
    /// </remarks>
    [Test]
    public void Apply_Twice_AccumulatesRatherThanReplacing()
    {
        IvpRigidBody body = new();

        IvpGravity.Apply([body], Down, 0.5f);
        IvpGravity.Apply([body], Down, 0.5f);

        body.Velocity.Z.ShouldBe(-800f, Close);
    }

    /// <remarks>
    /// **Bit `0x10` on the core's flag byte skips the body entirely** — `if ((*pbVar4 &amp; 0x10) == 0)`
    /// wraps everything, including the two helper calls before the add. This is what
    /// `EnableGravity( false )` amounts to for a body still in the list.
    /// </remarks>
    [Test]
    public void Apply_ToABodyThatSkipsGravity_LeavesItAlone()
    {
        IvpRigidBody body = new() { SkipsGravity = true };

        IvpGravity.Apply([body], Down, 1f);

        body.Velocity.Z.ShouldBe(0f);
    }

    /// <remarks>
    /// **Bit `0x20` selects a SECOND gravity vector**, held beside the default on the controller at
    /// `+0x20/0x24/0x28` rather than `+0x10/0x14/0x18`. IVP therefore supports per-object gravity,
    /// and a transcription carrying one global vector would be right for TF2 and wrong for the
    /// engine — which is exactly the kind of narrowing this project treats as a defect.
    ///
    /// The alternate below points UP and is a different magnitude, so a reader that ignored the bit
    /// fails on both sign and size rather than on neither.
    /// </remarks>
    [Test]
    public void Apply_ToABodyOnTheAlternateVector_UsesThatVectorInstead()
    {
        IvpRigidBody body = new() { UsesAlternateGravity = true };

        IvpGravity.Apply([body], Down, 1f, alternate: (0f, 0f, 200f));

        body.Velocity.Z.ShouldBe(200f, Close);
    }

    /// <remarks>
    /// **The control on the test above**: a body that has NOT asked for the alternate takes the
    /// default even when an alternate is supplied. Without this, a reader that always used the
    /// alternate would pass the previous test.
    /// </remarks>
    [Test]
    public void Apply_ToAnOrdinaryBodyWhenAnAlternateExists_StillUsesTheDefault()
    {
        IvpRigidBody body = new();

        IvpGravity.Apply([body], Down, 1f, alternate: (0f, 0f, 200f));

        body.Velocity.Z.ShouldBe(-800f, Close);
    }

    /// <remarks>
    /// **Gravity does not touch the previous-step velocity cache**, and that matters because the
    /// integrator moves a body by `PreviousVelocity` rather than by `Velocity`
    /// (`FUN_180099a00`). So a body gains speed on the step gravity is applied and MOVES on the
    /// next one — the one-step lag that runs through the whole solver.
    /// </remarks>
    [Test]
    public void Apply_ToAnyBody_LeavesThePreviousStepVelocityAlone()
    {
        IvpRigidBody body = new() { PreviousVelocity = (1f, 2f, 3f) };

        IvpGravity.Apply([body], Down, 1f);

        body.PreviousVelocity.ShouldBe((1f, 2f, 3f));
    }

    /// <remarks>
    /// **Every registered body, in one call** — the engine walks the controller's own list
    /// (`controller+0x1e8`, count at `+0x1e2`) rather than being called per object, which is why
    /// `EnableGravity` is list membership.
    /// </remarks>
    [Test]
    public void Apply_ToSeveralBodies_MovesEachOfThem()
    {
        List<IvpRigidBody> bodies = [new(), new() { SkipsGravity = true }, new()];

        IvpGravity.Apply(bodies, Down, 1f);

        bodies[0].Velocity.Z.ShouldBe(-800f, Close);
        bodies[1].Velocity.Z.ShouldBe(0f, "this one opted out");
        bodies[2].Velocity.Z.ShouldBe(-800f, Close);
    }
}
