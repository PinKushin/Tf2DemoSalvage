using System.Numerics;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary><c>CSimplePhysics</c> and <c>CBaseRopePhysics</c>, the rope's integrator and springs (`simple_physics.cpp`, `rope_physics.cpp`).</summary>
/// <remarks>
/// **The step is a fiftieth, so every answer here is a round number**: gravity of 1500 for one step moves a resting node
/// by <c>1500 · 0.02² / 2</c> = 0.3 (`simple_physics.cpp:26`, `:54`).
/// </remarks>
public sealed class RopePhysicsConformanceTests
{
    private const float Close = 1e-4f;

    private static readonly Vector3 Gravity = new(0f, 0f, -1500f);

    /// <remarks>
    /// **Half a step runs a whole one and predicts halfway back** — <c>ceil( 0.01 / 0.02 )</c> is one step, and the
    /// interpolant <c>( 0.01 − ( 0.02 − 0.02 ) ) / 0.02</c> is a half (`simple_physics.cpp:38-69`). The node falls 0.3 and
    /// is drawn at 0.15.
    /// </remarks>
    [Test]
    public void Simulate_HalfAStepUnderGravity_RunsOneStepAndPredictsHalfway()
    {
        RopePhysics physics = Free();

        physics.Simulate(0.01f, _ => Gravity, (_, _) => { });

        physics.Nodes[0].Position.Z.ShouldBe(-0.3f, Close);
        physics.Nodes[0].Predicted.Z.ShouldBe(-0.15f, Close);
    }

    /// <remarks>
    /// **The velocity carries at 0.98** — <c>pos + ( pos − prev ) · flDamp + a · dt²/2</c> with <c>flEnergy = 0.98</c>
    /// (`rope_physics.cpp:97`). Two steps from rest fall 0.3, then 0.3 · 0.98 + 0.3 more: −0.894 in all.
    /// </remarks>
    [Test]
    public void Simulate_TwoStepsUnderGravity_CarryTheFirstStepAtNinetyEightPercent()
    {
        RopePhysics physics = Free();

        physics.Simulate(0.04f, _ => Gravity, (_, _) => { });

        physics.Nodes[0].Position.Z.ShouldBe(-0.3f - ((0.3f * 0.98f) + 0.3f), Close);
    }

    /// <remarks>
    /// **A stretched spring pulls both ends in by half the excess each** (`rope_physics.cpp:137-144`): ten apart at a
    /// rest length of six, each node moves two.
    /// </remarks>
    [Test]
    public void Simulate_ASpringStretchedBeyondItsLength_PullsBothEndsInEqually()
    {
        RopePhysics physics = Pair(-5f, 5f, springLength: 6f);

        physics.Simulate(0.02f, _ => Vector3.Zero, (_, _) => { });

        (physics.Nodes[0].Position.X, physics.Nodes[1].Position.X).ShouldBe((-3f, 3f));
    }

    /// <remarks>
    /// **A spring of length zero falls back to the per-node length to decide, then corrects toward zero** — the
    /// branch Valve's own comment calls not enough (`rope_physics.cpp:128-140`). Both nodes meet in the middle.
    /// </remarks>
    [Test]
    public void Simulate_ASpringOfZeroLength_CollapsesBothEndsToTheMiddle()
    {
        RopePhysics physics = Pair(-5f, 5f, springLength: 0f);

        physics.Simulate(0.02f, _ => Vector3.Zero, (_, _) => { });

        (physics.Nodes[0].Position.X, physics.Nodes[1].Position.X).ShouldBe((0f, 0f));
    }

    /// <remarks>
    /// **A spring shorter than its length is left alone** — the solver only pulls (`:137`), so a slack rope hangs.
    /// </remarks>
    [Test]
    public void Simulate_ASpringShorterThanItsLength_IsLeftAlone()
    {
        RopePhysics physics = Pair(-5f, 5f, springLength: 20f);

        physics.Simulate(0.02f, _ => Vector3.Zero, (_, _) => { });

        (physics.Nodes[0].Position.X, physics.Nodes[1].Position.X).ShouldBe((-5f, 5f));
    }

    private static RopePhysics Free()
    {
        RopePhysics physics = new();

        physics.SetNumNodes(2);
        physics.ResetSpringLength(1000f);
        physics.Nodes[1].Position = physics.Nodes[1].Previous = new Vector3(0f, 0f, -10f);

        return physics;
    }

    private static RopePhysics Pair(float first, float second, float springLength)
    {
        RopePhysics physics = new();

        physics.SetNumNodes(2);
        physics.ResetSpringLength(springLength);
        physics.Nodes[0].Position = physics.Nodes[0].Previous = new Vector3(first, 0f, 0f);
        physics.Nodes[1].Position = physics.Nodes[1].Previous = new Vector3(second, 0f, 0f);

        return physics;
    }
}
