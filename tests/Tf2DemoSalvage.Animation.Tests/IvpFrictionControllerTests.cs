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

    /// <remarks>
    /// **A system with no contacts left is forgotten**: its three controller faces leave the cores that carried them, so the next
    /// rebuild has no entry for it — which is how <c>FUN_180084320</c>'s "the controller forgets the system, which deletes itself"
    /// lands in a port with no slot 7.
    /// </remarks>
    [Test]
    public void Advance_TheNormalPassWithNoContacts_TakesTheSystemOffItsCores()
    {
        (IvpFrictionSystem system, IvpContactPoint contact) = Linked();
        IvpSimulationUnit unit = new();
        IvpRigidBody core = contact.FirstObject.Core!;
        unit.Cores.Add(core);
        core.Controllers.Add(new IvpNormalFrictionController(system));
        core.Controllers.Add(new IvpFrictionController(system));
        core.Controllers.Add(new IvpRecordFrictionController(system, system.Environment, Revalidate.Sides));
        core.Controllers.Add(new IvpGravityController((0f, 0f, -600f)));
        system.Unlink(contact);

        new IvpNormalFrictionController(system).Advance(unit, [], psiStep: 0.5f);

        core.Controllers.Count.ShouldBe(1, "only gravity is left");
        core.Controllers[0].Priority.ShouldBe(1000);
    }

    [Test]
    public void Advance_TheNormalPassWithContactsLeft_KeepsTheSystemOnItsCores()
    {
        (IvpFrictionSystem system, IvpContactPoint contact) = Linked();
        IvpSimulationUnit unit = new();
        IvpRigidBody core = contact.FirstObject.Core!;
        unit.Cores.Add(core);
        core.Controllers.ShouldBe(system.Faces, "the control: the filing put the system's faces on its movable core");

        new IvpNormalFrictionController(system).Advance(unit, [], psiStep: 0.5f);

        core.Controllers.ShouldBe(system.Faces, "the system still holds a contact, so it stays");
    }

    /// <remarks>**Another system's controllers are left alone**, which a match on the controller type alone would get wrong.</remarks>
    [Test]
    public void Advance_TheNormalPassWithNoContacts_LeavesAnotherSystemsControllers()
    {
        (IvpFrictionSystem empty, IvpContactPoint contact) = Linked();
        (IvpFrictionSystem other, _) = Linked();
        IvpSimulationUnit unit = new();
        IvpRigidBody core = contact.FirstObject.Core!;
        unit.Cores.Add(core);
        core.Controllers.Add(new IvpNormalFrictionController(empty));
        core.Controllers.Add(new IvpNormalFrictionController(other));
        empty.Unlink(contact);

        new IvpNormalFrictionController(empty).Advance(unit, [], psiStep: 0.5f);

        core.Controllers.Count.ShouldBe(1);
        ((IvpNormalFrictionController)core.Controllers[0]).System.ShouldBeSameAs(other);
    }

    [Test]
    public void Priority_TheRecordPass_Is2000()
    {
        (IvpFrictionSystem system, _) = Linked();

        new IvpRecordFrictionController(system, system.Environment, Revalidate.Sides).Priority.ShouldBe(2000);
    }

    [Test]
    public void Advance_TheRecordPassOnALoneContact_RebuildsItsRecord()
    {
        (IvpFrictionSystem system, IvpContactPoint contact) = Linked();
        IvpContactRecord old = contact.Record!;
        system.Environment.Now = 3d;

        new IvpRecordFrictionController(system, system.Environment, Revalidate.Sides)
            .Advance(new IvpSimulationUnit(), [], psiStep: 0.5f);

        contact.Record.ShouldNotBeSameAs(old);
        contact.LastMeasured.ShouldBe(3d, "rebuilt at the environment's own clock");
    }

    /// <remarks>
    /// **With more than one contact every record is rebuilt and the pushes' work banked** (<c>FUN_180088ae0</c>): the pair gains
    /// <c>Σ (gap before − gap after)·push</c>, in float, last contact first. The unit's <c>0xc00</c> bits hold the payback off, so
    /// the bank is read as it was left.
    /// </remarks>
    [Test]
    public void Advance_TheRecordPassWithTwoContacts_RebuildsEveryRecordAndBanksThePushesWork()
    {
        (IvpFrictionSystem system, IvpContactPoint contact, IvpContactPoint second) = Banked();
        IvpContactRecord old = contact.Record!;
        IvpSimulationUnit unit = new() { Flags = 0x400 };

        new IvpRecordFrictionController(system, system.Environment, Revalidate.Sides).Advance(unit, [], psiStep: 0.5f);

        contact.Record.ShouldNotBeSameAs(old);
        second.Record.ShouldNotBeNull();

        // `Pairs[0].Contacts` in filing order: the first linked, then the second, walked last first.
        float expected = ((5f - second.Gap) * 0.5f) + ((3f - contact.Gap) * 2f);
        system.Pairs[0].StoredEnergy.ShouldBe(expected > 0f ? expected : 0f);
        system.Pairs[0].StoredEnergy.ShouldBeGreaterThan(0f, "the control: the gaps this fixture measures are under the ones set");
    }

    /// <remarks>
    /// **The unit's <c>0x3000</c> bits zero every pair's bank** before the payback — the same fixture banks work without them, so
    /// a zero here is the bits' doing.
    /// </remarks>
    [TestCase(0x1400, true)]
    [TestCase(0x400, false)]
    public void Advance_TheRecordPassAndTheCarryBits_ZeroTheBankOnlyWhenSet(int flags, bool zeroed)
    {
        (IvpFrictionSystem system, _, _) = Banked();
        IvpSimulationUnit unit = new() { Flags = flags };

        new IvpRecordFrictionController(system, system.Environment, Revalidate.Sides).Advance(unit, [], psiStep: 0.5f);

        (system.Pairs[0].StoredEnergy == 0f).ShouldBe(zeroed);
    }

    /// <remarks>
    /// **The payback runs only while the unit's <c>0xc00</c> bits are clear**: with them clear the bank is decayed and spent, so it
    /// no longer holds what the work alone put there.
    /// </remarks>
    [Test]
    public void Advance_TheRecordPassWithTheFastSpinBitsClear_PaysTheBankBack()
    {
        (IvpFrictionSystem held, _, _) = Banked();
        new IvpRecordFrictionController(held, held.Environment, Revalidate.Sides).Advance(new IvpSimulationUnit { Flags = 0x400 }, [], 0.5f);
        (IvpFrictionSystem paid, _, _) = Banked();

        new IvpRecordFrictionController(paid, paid.Environment, Revalidate.Sides).Advance(new IvpSimulationUnit(), [], 0.5f);

        paid.Pairs[0].StoredEnergy.ShouldBeLessThan(held.Pairs[0].StoredEnergy);
    }

    /// <summary>Two contacts whose rebuild measures gaps under the ones set, so the pushes did work.</summary>
    private static (IvpFrictionSystem, IvpContactPoint, IvpContactPoint) Banked()
    {
        (IvpFrictionSystem system, IvpContactPoint contact, IvpContactPoint second) = TwoContacts();
        contact.Gap = 3f;
        contact.NormalPush = 2f;
        second.Gap = 5f;
        second.NormalPush = 0.5f;

        // A closing speed, so a payback has relative motion to take the bank out of.
        system.Pairs[0].FirstCore.Velocity = (0f, 0f, 3f);

        return (system, contact, second);
    }

    private static (IvpFrictionSystem, IvpContactPoint, IvpContactPoint) TwoContacts()
    {
        (IvpFrictionSystem system, IvpContactPoint contact) = Linked();
        IvpContactPoint second = Revalidate.Contact(system.Pairs[0].FirstCore, system.Pairs[0].SecondCore);
        IvpFrictionLinking.LinkContactByCore(second, system.Pairs[0].FirstCore, system.Pairs[0].SecondCore, system.Environment);
        system.ContactCount.ShouldBe((short)2, "the control: the fixture really does hold two contacts");

        return (system, contact, second);
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
