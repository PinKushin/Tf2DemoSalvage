using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// What <c>CRagdoll::VPhysicsUpdate</c> reads of a ragdoll's sleep (`ragdoll.cpp:189-215`): <c>RagdollIsAsleep</c>, the
/// active list a frozen body stays on for its frame, and <c>RagdollComputeExactBbox</c> (B702, finding 107).
/// </summary>
/// <remarks>
/// **Decompiled from `vphysics.dll`** (2026-11-08): <c>CPhysicsObject::IsAsleep</c> `0x18001cab0` is the sleep bits at
/// <c>+0x41</c> and the core not on the revive list; the frozen listener `0x1800169c0` marks an object without taking it off
/// the active list, and `0x180016090`, at the start of the next <c>Simulate</c>, removes it. Two bodies with no joint, so each
/// has its own unit and can sleep alone, in a world with no gravity, so a body left alone is still and IVP freezes it.
/// </remarks>
public sealed class IvpRagdollSleepConformanceTests
{
    private static readonly Vector3 RootAt = new(10f, 20f, 30f);
    private static readonly Vector3 ChildAt = new(110f, 20f, 30f);

    /// <remarks>
    /// `RagdollComputeExactBbox` (`ragdoll_shared.cpp:587-612`) starts at the origin and takes in each element's collide box at
    /// its position and angles; `VPhysicsUpdate` subtracts the origin (`ragdoll.cpp:201-203`). The root is turned a quarter about
    /// Source Z, which sends a hull point `(x, y, z)` to `(−y, x, z)` — written out here rather than through a quaternion.
    /// </remarks>
    [Test]
    public void ExactBounds_ATurnedRootAndAPlacedChild_IsEveryHullPointAboutTheRoot()
    {
        IvpRagdoll ragdoll = Ragdoll(out _);
        IReadOnlyList<Vector3> rootHull = ragdoll.Body.Elements[0].Hull;
        IReadOnlyList<Vector3> childHull = ragdoll.Body.Elements[1].Hull;
        rootHull.Count.ShouldBe(4, "the control: the hull came through");

        // The units control: the fixture's 0.1 m reach along IVP x is Source x, in inches, so a hull left in metres fails here.
        float reach = 0f;

        foreach (Vector3 point in rootHull)
        {
            reach = System.MathF.Max(reach, point.X);
        }

        reach.ShouldBe(0.1f * IvpTransform.InchesPerMetre, 1e-3f);

        Vector3 min = RootAt;
        Vector3 max = RootAt;

        foreach (Vector3 point in rootHull)
        {
            Vector3 placed = RootAt + new Vector3(-point.Y, point.X, point.Z);
            (min, max) = (Vector3.Min(min, placed), Vector3.Max(max, placed));
        }

        foreach (Vector3 point in childHull)
        {
            (min, max) = (Vector3.Min(min, ChildAt + point), Vector3.Max(max, ChildAt + point));
        }

        (max - min).X.ShouldBeGreaterThan(100f, "the control: the child is a hundred inches along x");

        (Vector3 exactMin, Vector3 exactMax) = ragdoll.ExactBounds();

        exactMin.X.ShouldBe(min.X - RootAt.X, 1e-3f);
        exactMin.Y.ShouldBe(min.Y - RootAt.Y, 1e-3f);
        exactMin.Z.ShouldBe(min.Z - RootAt.Z, 1e-3f);
        exactMax.X.ShouldBe(max.X - RootAt.X, 1e-3f);
        exactMax.Y.ShouldBe(max.Y - RootAt.Y, 1e-3f);
        exactMax.Z.ShouldBe(max.Z - RootAt.Z, 1e-3f);
    }

    /// <remarks>
    /// **A new body's unit is asleep and its core waits on the revive list**, which the first PSI drains. `IsAsleep` answers false
    /// for a core on that list (`0x18001cab0`'s second test), so a ragdoll not yet simulated is not asleep.
    /// </remarks>
    [Test]
    public void AllAsleep_ANewRagdollListedForRevive_IsFalseThoughItsUnitsAreAsleep()
    {
        IvpRagdoll ragdoll = Ragdoll(out _);

        ragdoll.Bodies[0].Unit!.State.ShouldBe(IvpSimulationUnit.AsleepState, "the control: born in a sleeping unit");
        ragdoll.Bodies[0].ReviveQueued.ShouldBeTrue();
        ragdoll.AllAsleep.ShouldBeFalse();
        ragdoll.AnyAwake.ShouldBeFalse("its units are frozen; the revive comes in the step");
    }

    [Test]
    public void AllAsleep_AfterIvpFreezesEveryStillBody_IsTrueAndNoneIsAwake()
    {
        IvpRagdoll ragdoll = Ragdoll(out IvpRagdollWorld world);

        world.Simulate(1f / 66f);
        ragdoll.AnyAwake.ShouldBeTrue("the control: the first step revived both");
        ragdoll.AllAsleep.ShouldBeFalse();

        int steps = StepUntil(world, () => !ragdoll.AnyAwake);

        steps.ShouldBeLessThan(MaximumSteps, "IVP's rest check froze both still bodies");
        ragdoll.AllAsleep.ShouldBeTrue();
    }

    /// <remarks>`RagdollIsAsleep` asks EVERY element (`ragdoll_shared.cpp:616-620`); one drifting body keeps the ragdoll awake.</remarks>
    [Test]
    public void AllAsleep_OneBodyFrozenAndOneDrifting_IsFalseAndAnyAwakeIsTrue()
    {
        IvpRagdoll ragdoll = Ragdoll(out IvpRagdollWorld world);

        world.Simulate(1f / 66f);
        ragdoll.Bodies[1].Velocity = (1f, 0f, 0f);

        int steps = StepUntil(world, () => ragdoll.Bodies[0].Unit is { State: IvpSimulationUnit.AsleepState });

        steps.ShouldBeLessThan(MaximumSteps, "the control: the still root froze");
        ragdoll.Bodies[1].Unit!.State.ShouldNotBe(IvpSimulationUnit.AsleepState, "the control: the drifting child did not");
        ragdoll.AllAsleep.ShouldBeFalse();
        ragdoll.AnyAwake.ShouldBeTrue();
    }

    /// <remarks>
    /// **Stillness is timed by the clock, not by the calls** (`CheckSettleStationaryRagdoll`, `ragdoll.cpp:268-298`): `dt =
    /// curtime - m_flLastOriginChangeTime`, where a move of more than an inch on an axis restamps the time. The engine calls it only
    /// from `VPhysicsUpdate`, so two calls five seconds apart are what a corpse off the active list in between would see. The first
    /// call moves the root from `m_vecLastOrigin`'s `(0, 0, 0)` to `(10, 20, 30)` and stamps 0.1; 4.95 s later is short of
    /// `ragdoll_sleepaftertime`, 5.1 s is past it.
    /// </remarks>
    [Test]
    public void CheckSettle_StillFiveSecondsByTheClock_ForcesSleepWhateverTheCallsBetween()
    {
        IvpRagdoll ragdoll = Ragdoll(out IvpRagdollWorld world);
        world.Simulate(1f / 66f);
        ragdoll.AllAsleep.ShouldBeFalse("the control: awake, so the forced sleep can run");
        ragdoll.LastOriginChange = 0d;

        ragdoll.CheckSettle(0.1d);
        ragdoll.LastOriginChange.ShouldBe(0.1d, "the root moved from (0, 0, 0)");

        ragdoll.CheckSettle(5.05d);
        ragdoll.Asleep.ShouldBeFalse("4.95 s still");

        ragdoll.CheckSettle(5.2d);
        ragdoll.Asleep.ShouldBeTrue("5.1 s still");
        ragdoll.AllAsleep.ShouldBeTrue("PhysForceRagdollToSleep put every body to sleep");
    }

    /// <remarks>
    /// "It's totally asleep, don't worry about forcing it to settle" — `if ( m_allAsleep ) return;` (`ragdoll.cpp:283-285`): a
    /// ragdoll IVP has frozen is still past five seconds and is not forced.
    /// </remarks>
    [Test]
    public void CheckSettle_EveryBodyAsleep_DoesNotForceSleep()
    {
        IvpRagdoll ragdoll = Ragdoll(out IvpRagdollWorld world);
        world.Simulate(1f / 66f);
        StepUntil(world, () => !ragdoll.AnyAwake).ShouldBeLessThan(MaximumSteps, "the control: IVP froze it");
        ragdoll.AllAsleep.ShouldBeTrue();
        ragdoll.CheckSettle(0d);
        ragdoll.LastOriginChange = -100d;

        ragdoll.CheckSettle(1d);
        ragdoll.LastOriginChange.ShouldBe(-100d, "the control: still, so the 101 s stand");

        ragdoll.Asleep.ShouldBeFalse();
    }

    /// <summary>Thirty seconds at 66 ticks a second — far past IVP's rest delay.</summary>
    private const int MaximumSteps = 66 * 30;

    private static int StepUntil(IvpRagdollWorld world, System.Func<bool> done)
    {
        int steps = 0;

        while (!done() && steps < MaximumSteps)
        {
            world.Simulate(1f / 66f);
            steps++;
        }

        return steps;
    }

    /// <summary>Two unjoined bodies, each with a lopsided hull, the root turned a quarter about Source Z.</summary>
    private static IvpRagdoll Ragdoll(out IvpRagdollWorld world)
    {
        world = new IvpRagdollWorld(1f / 66f, Vector3.Zero, new VphysicsSurfaceProps([]));
        const float Squared = IvpTransform.InchesPerMetre * IvpTransform.InchesPerMetre;

        PhysicsModel physics = PhysicsModel.From(
            [
                new PhysicsSolid(0, "bip_root", "", "flesh", 10f, 1f, 0f, 0f, 100f, 0f),
                new PhysicsSolid(1, "bip_child", "bip_root", "flesh", 2f, 1f, 0f, 0f, 20f, 0f),
            ],
            [],
            2,
            checksum: 0,
            collisionRules: null,
            hulls: [[Ledge()], [Ledge()]],
            massProperties:
            [
                new PhysicsMassProperties(Vector3.Zero, Vector3.One / Squared),
                new PhysicsMassProperties(Vector3.Zero, Vector3.One / Squared),
            ]);

        return IvpRagdoll.Create(
            world,
            RagdollBody.Build(physics, RagdollSkeletons.Straight())!,
            [(RootAt, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, System.MathF.PI / 2f)), (ChildAt, Quaternion.Identity)]);
    }

    /// <summary>A tetrahedron with a different reach on each IVP axis, in metres.</summary>
    private static PhysicsLedge Ledge() =>
        new(
            [new Vector3(0f, 0f, 0f), new Vector3(0.1f, 0f, 0f), new Vector3(0f, 0.05f, 0f), new Vector3(0f, 0f, 0.02f)],
            [(0, 1, 2), (0, 3, 1), (0, 2, 3), (1, 3, 2)],
            [(0, 0, 0), (0, 0, 0), (0, 0, 0), (0, 0, 0)],
            [0, 0, 0, 0],
            [0, 0, 0, 0],
            Vector3.Zero,
            0.1f);
}
