using Tf2DemoSalvage.Animation.Animating;

using Revalidate = Tf2DemoSalvage.Animation.Tests.IvpFrictionSystemRevalidatePairTests;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>A contact weighed for its cone budget — <c>FUN_180083a60</c> and <c>FUN_180077840</c> (B369, D172).</summary>
/// <remarks>
/// **Read from the decompiler** (`docs/findings/51`): the effective mass along an arm is the reciprocal of the LARGEST of the
/// three inertia terms plus the inverse mass, two movable cores give the harmonic combination, and a flagged core is left out.
/// Synthetic conformance (D38).
/// </remarks>
public sealed class IvpContactPointWeighTests
{
    [Test]
    public void EffectiveMassAlong_NoArm_IsTheInverseOfTheInverseMass()
    {
        IvpRigidBody core = new() { InverseMass = 0.25f, InverseInertia = (1f, 1f, 1f) };

        core.EffectiveMassAlong((0f, 0f, 0f)).ShouldBe(4d);
    }

    /// <remarks>
    /// **The largest term wins, not the sum.** An arm two along `y` gives `4` against the `x` inertia and `12` against the `z`
    /// one, and nothing against the `y` one; the sum would be `16`, so the two answers are `1/12` and `1/16`.
    /// </remarks>
    [Test]
    public void EffectiveMassAlong_AnArm_TakesTheLargestInertiaTerm()
    {
        IvpRigidBody core = new() { InverseMass = 0f, InverseInertia = (1f, 5f, 3f) };

        core.EffectiveMassAlong((0f, 2f, 0f)).ShouldBe(1d / 12d);
    }

    /// <remarks>**The first comparison decides it when the `x` term is the largest**, which the `z`-largest case above cannot see.</remarks>
    [Test]
    public void EffectiveMassAlong_AnArmWhoseFirstTermIsLargest_TakesThatOne()
    {
        IvpRigidBody core = new() { InverseMass = 0f, InverseInertia = (5f, 1f, 1f) };

        core.EffectiveMassAlong((0f, 2f, 0f)).ShouldBe(1d / 20d);
    }

    [Test]
    public void EffectiveMassAlong_ACoreSkippingGravity_ContributesAUnitMass()
    {
        IvpRigidBody core = new() { SkipsGravity = true, InverseMass = 0.25f, InverseInertia = (0f, 0f, 0f) };

        core.EffectiveMassAlong((0f, 0f, 0f)).ShouldBe(1d, "its own inverse mass is not read");
    }

    [Test]
    public void Weigh_AMovableAgainstTheWorld_WeighsTheMovableAlone()
    {
        IvpContactPoint contact = Weighed(out IvpRigidBody movable, out _);
        movable.InverseMass = 0.25f;
        movable.InverseInertia = (0f, 0f, 0f);

        contact.Weigh();

        contact.InverseContactMass.ShouldBe(0.25f, "one over the movable's four");
    }

    [Test]
    public void Weigh_TwoMovableCores_CombinesThemHarmonically()
    {
        IvpContactPoint contact = Weighed(out IvpRigidBody first, out IvpRigidBody second);
        second.Immovable = false;
        first.InverseMass = 0.25f;
        first.InverseInertia = (0f, 0f, 0f);
        second.InverseMass = 0.5f;
        second.InverseInertia = (0f, 0f, 0f);

        contact.Weigh();

        // 4 and 2 combine to 4·2/(4+2) = 4/3, whose reciprocal is 0.75.
        contact.InverseContactMass.ShouldBe(0.75f, 1e-6f);
    }

    /// <remarks>**Each core is weighed along ITS OWN arm**, so an immovable first side leaves the second's arm, not the first's.</remarks>
    [Test]
    public void Weigh_AnImmovableFirstSide_WeighsTheSecondAlongItsOwnArm()
    {
        IvpContactPoint contact = Weighed(out IvpRigidBody first, out IvpRigidBody second);
        first.Immovable = true;
        second.Immovable = false;
        second.InverseMass = 0f;
        second.InverseInertia = (1f, 0f, 0f);
        contact.Record!.FirstArm = (0f, 0f, 0f);
        contact.Record.SecondArm = (0f, 2f, 0f);

        contact.Weigh();

        contact.InverseContactMass.ShouldBe(4f, "the second core's own arm gives a mass of a quarter");
    }

    [Test]
    public void Weigh_AMaterialWithASecondFriction_SetsTheMaterialAxisGate()
    {
        IvpContactPoint contact = Weighed(out _, out _);
        contact.Record!.FirstMaterial = new Axed();

        contact.Weigh();

        contact.UsesMaterialAxes.ShouldBeTrue();
    }

    [Test]
    public void Weigh_PlainMaterials_LeavesTheGateClear()
    {
        IvpContactPoint contact = Weighed(out _, out _);

        contact.Weigh();

        contact.UsesMaterialAxes.ShouldBeFalse();
    }

    private static IvpContactPoint Weighed(out IvpRigidBody first, out IvpRigidBody second)
    {
        (_, _, IvpContactPoint contact) = Revalidate.Linked(out IvpRigidBody world);
        IvpContactRecord.Build(contact, Revalidate.First(contact), Revalidate.Second(contact), 0d);
        contact.SetMaterials(Revalidate.Materials.Instance);
        first = contact.FirstObject.Core!;
        second = world;

        return contact;
    }

    private sealed class Axed : IIvpMaterial
    {
        public double FrictionFactor => 1d;

        public double SecondFrictionFactor => 1d;

        public double Elasticity => 0d;

        public bool HasSecondFriction => true;
    }
}
