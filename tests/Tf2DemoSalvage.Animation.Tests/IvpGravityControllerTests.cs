using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>Gravity as a unit's controller — the entry at priority 1000, <c>FUN_180074c80</c> (B369, D172).</summary>
/// <remarks>
/// **Read from the disassembly** (`docs/findings/51`): a core flagged <c>0x10</c> is skipped entirely, and the rest take the
/// damping, the staged-velocity flush and then the acceleration. Synthetic conformance (D38).
/// </remarks>
public sealed class IvpGravityControllerTests
{
    [Test]
    public void Advance_ACoreTakingGravity_FlushesItsStagedVelocityBeforeTheAcceleration()
    {
        IvpRigidBody core = new() { PendingVelocity = (1f, 0f, 0f) };

        new IvpGravityController((0f, 0f, -10f)).Advance([core], psiStep: 0.5f);

        core.Velocity.ShouldBe((1f, 0f, -5f), "the staged velocity is live before gravity is added to it");
        core.PendingVelocity.ShouldBe((0f, 0f, 0f));
    }

    [Test]
    public void Advance_ACoreThatSkipsGravity_IsLeftAlone()
    {
        IvpRigidBody core = new() { SkipsGravity = true, PendingVelocity = (1f, 0f, 0f) };

        new IvpGravityController((0f, 0f, -10f)).Advance([core], psiStep: 0.5f);

        core.Velocity.ShouldBe((0f, 0f, 0f));
        core.PendingVelocity.ShouldBe((1f, 0f, 0f), "not even its staged velocity is flushed");
    }

    [Test]
    public void Advance_ACoreOnTheSecondAcceleration_TakesThatOne()
    {
        IvpRigidBody core = new() { UsesAlternateGravity = true };

        new IvpGravityController((0f, 0f, -10f), (0f, 4f, 0f)).Advance([core], psiStep: 0.5f);

        core.Velocity.ShouldBe((0f, 2f, 0f));
    }

    [Test]
    public void Priority_Gravity_Is1000()
    {
        new IvpGravityController((0f, 0f, 0f)).Priority.ShouldBe(1000);
    }
}
