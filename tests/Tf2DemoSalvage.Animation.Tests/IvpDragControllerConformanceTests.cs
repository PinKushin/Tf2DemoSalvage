using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// vphysics' air drag — the environment's drag controller at priority <c>500</c> (vtable <c>1800ebfe8</c>, slot 4
/// <c>FUN_180016370</c>) and the object's two drag scalars <c>FUN_18001bc60</c> and <c>FUN_18001bb20</c> (B369, D172).
/// </summary>
/// <remarks>
/// **Read from the disassembly, 2026-09-16.** Every expectation below is the instruction sequence carried by hand with values
/// the test chose, so a regrouping or a moved factor reddens it. Synthetic conformance (D38).
/// </remarks>
public sealed class IvpDragControllerConformanceTests
{
    /// <remarks>
    /// **The linear coefficient scales the X term alone** — `MULSS XMM0,[+0x58]` sits between the first `ANDPS` and the first
    /// `ADDSS`: `(|v.x·b.x|·c + |v.y·b.y|) + |v.z·b.z|`, the velocity turned into the core first (`FUN_180070620`). With
    /// `b = (1, 2, 3)`, `c = 0.5` and `v = (2, −3, 4)`: `1 + 6 + 12 = 19`, where a coefficient on the sum would give `9.5`.
    /// </remarks>
    [Test]
    public void LinearDrag_AnUnturnedCore_ScalesOnlyTheXTermByTheCoefficient()
    {
        IvpRigidBody core = Core();
        core.DragBasis = (1f, 2f, 3f);
        core.DragCoefficient = 0.5f;

        IvpDrag.Linear(core, (2f, -3f, 4f)).ShouldBe(19f);
    }

    /// <remarks>
    /// **The linear drag reads the velocity in the core's own axes**: a core turned a quarter about Z sees world `(0, 2, 0)` along
    /// its own −X or +X, so only the X basis counts — `|±2·1|·1 = 2`, where the world axes would give `|2·5| = 10`.
    /// </remarks>
    [Test]
    public void LinearDrag_ATurnedCore_ReadsTheVelocityInItsOwnAxes()
    {
        IvpRigidBody core = new()
        {
            CoreMatrix = IvpMatrix.FromRotation((0d, 0d, System.Math.Sqrt(0.5d), System.Math.Sqrt(0.5d)), (0d, 0d, 0d)),
            DragBasis = (1f, 5f, 0f),
            DragCoefficient = 1f,
        };

        IvpDrag.Linear(core, (0f, 2f, 0f)).ShouldBe(2f, 1e-5f);
    }

    /// <remarks>
    /// **The angular coefficient scales the X term alone too, and the basis is the left operand**:
    /// `(|b.y·w.y| + |b.x·w.x|·c) + |b.z·w.z|`, no turn. With `b = (1, 2, 3)`, `c = 0.5`, `w = (2, −3, 4)`: `6 + 1 + 12 = 19`.
    /// </remarks>
    [Test]
    public void AngularDrag_ASpin_ScalesOnlyTheXTermByTheCoefficient()
    {
        IvpRigidBody core = Core();
        core.AngularDragBasis = (1f, 2f, 3f);
        core.AngularDragCoefficient = 0.5f;

        IvpDrag.Angular(core, (2f, -3f, 4f)).ShouldBe(19f);
    }

    /// <remarks>
    /// **Velocity loses `(step·ρ)·(drag·−0.5)` of itself, the product in double and the sum in float**:
    /// `v = (float)((double)v·(double)k) + v`. Drag `|2·0.1|·1 = 0.2`, `k = (0.5·2)·(0.2·−0.5) = −0.1`.
    /// </remarks>
    [Test]
    public void Advance_AMovingCore_LosesVelocityToTheLinearDrag()
    {
        IvpRigidBody core = Core();
        core.DragBasis = (0.1f, 0f, 0f);
        core.DragCoefficient = 1f;
        core.Velocity = (2f, 0f, 0f);

        new IvpDragController().Advance(new IvpSimulationUnit(), [core], psiStep: 0.5f);

        float k = (0.5f * 2f) * (0.2f * -0.5f);
        core.Velocity.X.ShouldBe((float)(2d * k) + 2f);
        core.Velocity.X.ShouldBeLessThan(2f, "the control: drag slowed it");
    }

    /// <remarks>
    /// **Spin loses `−(drag·ρ·step)` of itself, with no half.** Drag `|0.1·2|·1 = 0.2`, `k = −(0.2·2·0.5) = −0.2`.
    /// </remarks>
    [Test]
    public void Advance_ASpinningCore_LosesSpinToTheAngularDrag()
    {
        IvpRigidBody core = Core();
        core.AngularDragBasis = (0.1f, 0f, 0f);
        core.AngularDragCoefficient = 1f;
        core.AngularVelocity = (2f, 0f, 0f);

        new IvpDragController().Advance(new IvpSimulationUnit(), [core], psiStep: 0.5f);

        float k = -(0.2f * 2f * 0.5f);
        core.AngularVelocity.X.ShouldBe((float)(2d * k) + 2f);
    }

    /// <remarks>**The factor is floored at −1** (`MAXSS` against `DAT_1800ea9f8`), so a drag stronger than the velocity stops it.</remarks>
    [Test]
    public void Advance_ADragStrongerThanTheVelocity_StopsItRatherThanReversingIt()
    {
        IvpRigidBody core = Core();
        core.DragBasis = (100f, 0f, 0f);
        core.DragCoefficient = 1f;
        core.Velocity = (2f, 0f, 0f);

        new IvpDragController().Advance(new IvpSimulationUnit(), [core], psiStep: 0.5f);

        core.Velocity.X.ShouldBe(0f);
    }

    /// <remarks>**The density is the controller's own**, `2.0f` from the environment constructor and `SetAirDensity`'s to change.</remarks>
    [Test]
    public void AirDensity_ANewController_IsTwo() => new IvpDragController().AirDensity.ShouldBe(2f);

    [Test]
    public void Priority_TheDragController_Is500() => new IvpDragController().Priority.ShouldBe(500);

    private static IvpRigidBody Core() =>
        new() { CoreMatrix = IvpMatrix.FromRotation((0d, 0d, 0d, 1d), (0d, 0d, 0d)) };
}
