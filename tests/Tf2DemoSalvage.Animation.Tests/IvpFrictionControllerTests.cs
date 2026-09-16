using Tf2DemoSalvage.Animation.Animating;

using Revalidate = Tf2DemoSalvage.Animation.Tests.IvpFrictionSystemRevalidatePairTests;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>The friction system's tangential controller at priority 600 — <c>FUN_1800843c0</c> (B369, D172).</summary>
/// <remarks>
/// **Read from the decompiler** (`docs/findings/51`): one contact takes its own clamp and solve, more take the per-pair budget
/// summed from <c>NormalPush × Friction × InverseContactMass</c> times the step squared. Synthetic conformance (D38).
/// </remarks>
public sealed class IvpFrictionControllerTests
{
    [Test]
    public void Priority_TheTangentialPass_Is600()
    {
        new IvpFrictionController(System()).Priority.ShouldBe(600);
    }

    [Test]
    public void BudgetFor_APairOfContacts_SumsEachProductAndScalesByTheStepSquared()
    {
        IvpFrictionPair pair = new(new IvpRigidBody(), new IvpRigidBody());
        pair.Contacts.Add(Contact(normalPush: 2f, friction: 3f, inverseMass: 4f));
        pair.Contacts.Add(Contact(normalPush: 1f, friction: 1f, inverseMass: 1f));

        // (2·3·4 + 1·1·1) × 0.5² = 25 × 0.25.
        IvpFrictionController.BudgetFor(pair, psiStep: 0.5f).ShouldBe(6.25f, 1e-4f);
    }

    [Test]
    public void BudgetFor_APairWithNoContacts_IsZero()
    {
        IvpFrictionPair pair = new(new IvpRigidBody(), new IvpRigidBody());

        IvpFrictionController.BudgetFor(pair, psiStep: 0.5f).ShouldBe(0f);
    }

    [Test]
    public void Advance_ALoneContactOverItsBudget_ClampsItAndCarriesTheExcess()
    {
        (IvpFrictionSystem system, IvpContactPoint contact) = Linked();
        contact.NormalPush = 1f;
        contact.Friction = 1f;
        contact.InverseContactMass = 4f;
        contact.Slide = (5f, 0f);

        new IvpFrictionController(system).Advance(new IvpSimulationUnit(), [], psiStep: 0.5f);

        // The budget is 0.5² × 4 × (1 × 1) = 1, so the slide is clamped to it and the excess is (5 − 1) × 1 × 1.
        contact.Slide.Span.ShouldBe(1f, 1e-3f);
        contact.SlideExcess.ShouldBe(4f, 1e-3f);
        contact.FirstMeasure.ShouldBeTrue("a clamped contact's history is re-armed");
    }

    [Test]
    public void Advance_ALoneContactInsideItsBudget_IsLeftAlone()
    {
        (IvpFrictionSystem system, IvpContactPoint contact) = Linked();
        contact.NormalPush = 1f;
        contact.Friction = 1f;
        contact.InverseContactMass = 4f;
        contact.Slide = (0.1f, 0f);

        new IvpFrictionController(system).Advance(new IvpSimulationUnit(), [], psiStep: 0.5f);

        contact.Slide.ShouldBe((0.1f, 0f));
        contact.SlideExcess.ShouldBe(0f);
    }

    /// <remarks>
    /// **The lone path reads the system's LIST HEAD, not its pairs** — so a single contact is still solved when its pair holds
    /// none, which is the only input that tells the two branches apart.
    /// </remarks>
    [Test]
    public void Advance_ALoneContactWhosePairIsEmpty_IsStillSolved()
    {
        (IvpFrictionSystem system, IvpContactPoint contact) = Linked();
        system.Pairs[0].Contacts.Clear();
        contact.NormalPush = 1f;
        contact.Friction = 1f;
        contact.InverseContactMass = 4f;
        contact.Slide = (5f, 0f);

        new IvpFrictionController(system).Advance(new IvpSimulationUnit(), [], psiStep: 0.5f);

        contact.Slide.Span.ShouldBe(1f, 1e-3f);
    }

    [Test]
    public void Advance_TwoContactsInOnePair_ClampsThemAgainstTheSharedBudget()
    {
        (IvpFrictionSystem system, IvpContactPoint first) = Linked();
        IvpContactPoint second = Revalidate.Contact(system.Pairs[0].FirstCore, system.Pairs[0].SecondCore);
        IvpFrictionLinking.LinkContactByCore(second, system.Pairs[0].FirstCore, system.Pairs[0].SecondCore, system.Environment);
        IvpContactRecord.Build(second, Revalidate.First(second), Revalidate.Second(second), 0d);

        foreach (IvpContactPoint contact in system.Pairs[0].Contacts)
        {
            contact.NormalPush = 1f;
            contact.Friction = 1f;
            contact.InverseContactMass = 2f;
            contact.Slide = (5f, 0f);
        }

        new IvpFrictionController(system).Advance(new IvpSimulationUnit(), [], psiStep: 0.5f);

        // Two contacts summing 1·1·2 each, times 0.5², gives a shared budget of one.
        first.Slide.Span.ShouldBe(1f, 1e-3f);
        second.Slide.Span.ShouldBe(1f, 1e-3f);
    }

    [Test]
    public void Priority_TheNormalPass_IsZero()
    {
        new IvpNormalFrictionController(System()).Priority.ShouldBe(0);
    }

    /// <remarks>**The stale-entries bits every PSI**: bit 9 cleared, bit 8 set, which is what rebuilds a unit's entries.</remarks>
    [Test]
    public void Advance_TheNormalPass_ClearsBitNineAndSetsBitEight()
    {
        (IvpFrictionSystem system, _) = Linked();
        IvpSimulationUnit unit = new() { Flags = 0x200 };

        new IvpNormalFrictionController(system).Advance(unit, [], psiStep: 0.5f);

        (unit.Flags & 0x300).ShouldBe(0x100);
    }

    [Test]
    public void Advance_TheNormalPassWithASplitDue_ClearsTheFlag()
    {
        (IvpFrictionSystem system, _) = Linked();
        system.SplitCheckDue = true;

        new IvpNormalFrictionController(system).Advance(new IvpSimulationUnit(), [], psiStep: 0.5f);

        system.SplitCheckDue.ShouldBeFalse();
    }

    [Test]
    public void Advance_TheNormalPassOnALoneContact_GivesItANormalPush()
    {
        (IvpFrictionSystem system, IvpContactPoint contact) = Linked();
        contact.Gap = 0f;

        new IvpNormalFrictionController(system).Advance(new IvpSimulationUnit(), [], psiStep: 0.5f);

        contact.NormalPush.ShouldBeGreaterThan(0f, "a contact inside its gap is pushed out");
    }

    [Test]
    public void Advance_TheNormalPassWithNoContacts_StillSetsTheUnitsBits()
    {
        (IvpFrictionSystem system, IvpContactPoint contact) = Linked();
        system.Unlink(contact);
        IvpSimulationUnit unit = new() { Flags = 0x200 };

        new IvpNormalFrictionController(system).Advance(unit, [], psiStep: 0.5f);

        (unit.Flags & 0x300).ShouldBe(0x100);
    }

    private static IvpContactPoint Contact(float normalPush, float friction, float inverseMass)
    {
        (_, IvpContactPoint contact) = Linked();
        contact.NormalPush = normalPush;
        contact.Friction = friction;
        contact.InverseContactMass = inverseMass;

        return contact;
    }

    private static (IvpFrictionSystem, IvpContactPoint) Linked()
    {
        (IvpFrictionSystem system, _, IvpContactPoint contact) = Revalidate.Linked(out _);
        IvpContactRecord.Build(contact, Revalidate.First(contact), Revalidate.Second(contact), 0d);

        return (system, contact);
    }

    private static IvpFrictionSystem System() => Linked().Item1;
}
