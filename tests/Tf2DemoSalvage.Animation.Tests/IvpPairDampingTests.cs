using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Probe.Oracle;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>A pair's banked work paid back out of its relative motion — <c>FUN_180086b40</c> (B369, D172).</summary>
/// <remarks>
/// **Arithmetic from the read routine** (`docs/findings/51`, *The priority-2000 routines*): two unit masses closing at 2 with no
/// spin can lose `((1·2·2 + 1e-19) − (1·1·1 + 1·1·1))·0.5 = 1`; the PSI takes `MINSD(1·0.1, e) = 0.1` of it, and `FUN_180083f70`
/// turns that into `jv = (2 − √|4 − (0.1 + 0.1)·2|) / 2` along the closing direction. Synthetic conformance (D38).
/// </remarks>
public sealed class IvpPairDampingTests
{
    [Test]
    public void PayBack_TwoMassesClosing_StageTheTakenEnergyAsOpposingVelocities()
    {
        (IvpFrictionSystem system, IvpFrictionPair pair, IvpRigidBody first, IvpRigidBody second) = Pair();
        second.Velocity = (0f, 0f, 2f);
        pair.StoredEnergy = 100f;

        IvpPairDamping.PayBack(system);

        double jv = (2d - System.Math.Sqrt(System.Math.Abs(4d - ((0.1f + (double)0.1f) * 2d)))) / 2d;
        first.PendingVelocity.Z.ShouldBe((float)(jv + 0d), 1e-6f);
        second.PendingVelocity.Z.ShouldBe((float)-jv, 1e-6f);
        system.Environment.DampedEnergy.ShouldBe(0.1f, 1e-9d);
        pair.StoredEnergy.ShouldBe((float)((double)(float)(100d * IvpPairDamping.Decay(1d / 66d)) - 0.1f));
    }

    /// <remarks>**A bank smaller than a tenth of what can be taken is taken whole** — the second half of the MINSD.</remarks>
    [Test]
    public void PayBack_ALittleBanked_TakesAllOfIt()
    {
        (IvpFrictionSystem system, IvpFrictionPair pair, _, IvpRigidBody second) = Pair();
        second.Velocity = (0f, 0f, 2f);
        pair.StoredEnergy = 0.01f;

        IvpPairDamping.PayBack(system);

        pair.StoredEnergy.ShouldBe(0f, 1e-6f);
        system.Environment.DampedEnergy.ShouldBe((double)(float)(0.01f * IvpPairDamping.Decay(1d / 66d)), 1e-9d);
    }

    /// <remarks>**A negative bank is left decayed and nothing is paid** — <c>COMISS</c> then <c>JC</c>.</remarks>
    [Test]
    public void PayBack_ANegativeBank_PaysNothing()
    {
        (IvpFrictionSystem system, IvpFrictionPair pair, IvpRigidBody first, IvpRigidBody second) = Pair();
        second.Velocity = (0f, 0f, 2f);
        pair.StoredEnergy = -1f;

        IvpPairDamping.PayBack(system);

        first.PendingVelocity.ShouldBe((0f, 0f, 0f));
        system.Environment.DampedEnergy.ShouldBe(0d);
        pair.StoredEnergy.ShouldBeLessThan(0f);
    }

    /// <remarks>
    /// **An unmovable side is not pushed and lends the other side's mass times ten thousand** — so against a wall nearly all the
    /// reducible energy is the mover's own.
    /// </remarks>
    [Test]
    public void PayBack_AgainstAnUnmovableCore_PushesOnlyTheMover()
    {
        (IvpFrictionSystem system, IvpFrictionPair pair, IvpRigidBody mover, IvpRigidBody wall) = Pair();
        wall.Immovable = true;
        mover.Velocity = (0f, 0f, -2f);
        pair.StoredEnergy = 100f;

        IvpPairDamping.PayBack(system);

        wall.PendingVelocity.ShouldBe((0f, 0f, 0f));
        mover.PendingVelocity.Z.ShouldBeGreaterThan(0f, "slowed toward the wall's rest");
    }

    /// <remarks>
    /// **The environment's constructor writes the same field for its default step** — `+0x1b0 = 0x3feff2eed61b4202` beside
    /// `+0x108 = 1/66` (`FUN_180080d90`, findings 51) — which is the binary's own answer to compare against.
    /// </remarks>
    [Test]
    public void Decay_TheDefaultStep_IsTheBitsTheEnvironmentsConstructorWrites()
    {
        System.BitConverter.DoubleToInt64Bits(IvpPairDamping.Decay(1d / 66d)).ShouldBe(0x3feff2eed61b4202L);
    }

    private static (IvpFrictionSystem, IvpFrictionPair, IvpRigidBody, IvpRigidBody) Pair()
    {
        IvpFrictionSystem system = new(
            new IvpImpactEnvironment
            {
                InverseStep = 66d,
                Step = 1d / 66d,
                Limits = new IvpAnomalyLimits(2000f, 6, 3600f, 250, 1f, 1e30f),
                Anomalies = new VphysicsAnomalyManager(new IvpImpactReplay.FixedAnswer(answer: false)),
                Materials = new IvpReplayMaterials(new IvpReplayMaterial(0d, 0d, HasSecondFriction: false), 0d, 0d),
            });

        IvpRigidBody first = Core();
        IvpRigidBody second = Core();
        IvpFrictionPair pair = new(first, second);
        system.AddPair(pair);

        return (system, pair, first, second);
    }

    private static IvpRigidBody Core() =>
        new()
        {
            Orientation = (0d, 0d, 0d, 1d),
            WorkingOrientation = (0d, 0d, 0d, 1d),
            CoreMatrix = IvpMatrix.FromRotation((0d, 0d, 0d, 1d), (0d, 0d, 0d)),
            Mass = 1f,
            InverseMass = 1f,
        };
}
