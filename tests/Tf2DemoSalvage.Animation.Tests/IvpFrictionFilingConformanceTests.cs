using Tf2DemoSalvage.Animation.Animating;

using Revalidate = Tf2DemoSalvage.Animation.Tests.IvpFrictionSystemRevalidatePairTests;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// The normal pass dropping contacts that have drawn apart — the lone contact's tail in <c>FUN_180084490</c> and the filing pass
/// <c>FUN_1800a9bf0</c> runs between its sort and its solve (B369, D172).
/// </summary>
/// <remarks>
/// **Read from the disassembly** (`docs/findings/51`, *`FUN_1800a9bf0(system, event)`, the many-contact priority-0 routine*). Found
/// missing by the paired `.phy` drop: vphysics had dropped a crate's second contact at a gap of 0.033 while the port pushed it.
/// Synthetic conformance (D38).
/// </remarks>
public sealed class IvpFrictionFilingConformanceTests
{
    /// <remarks>**A lone contact at or past `block[0x47]` is dropped after its push** (`COMISS block[0x47], gap; JBE`).</remarks>
    [TestCase(0.03f, 0)]
    [TestCase(0.02f, 1)]
    public void SolveNormalPushes_ALoneContactsGap_DropsItOnlyAtOrPastTheRestingGap(float gap, int left)
    {
        (IvpFrictionSystem system, IvpContactPoint contact) = Linked();
        contact.Gap = gap;

        system.SolveNormalPushes(inverseStep: 66f);

        system.ContactCount.ShouldBe((short)left);
    }

    /// <remarks>**A lone contact whose record found the touch outside its feature is dropped** (`record+0x76 == 1`).</remarks>
    [Test]
    public void SolveNormalPushes_ALoneContactMeasuredOutside_IsDropped()
    {
        (IvpFrictionSystem system, IvpContactPoint contact) = Linked();
        contact.Gap = 0.02f;
        contact.Record!.Outside = true;

        system.SolveNormalPushes(inverseStep: 66f);

        system.ContactCount.ShouldBe((short)0);
    }

    /// <remarks>
    /// **The heap's filing pass drops a contact at or past `block[0x47]` before it is solved** (`COMISS`/`JNC`), and leaves the one
    /// inside it.
    /// </remarks>
    [Test]
    public void SolveHeap_AContactPastTheRestingGap_IsDroppedAndTheOtherKept()
    {
        (IvpFrictionSystem system, IvpContactPoint first, IvpContactPoint second) = TwoContacts();
        first.Gap = 0.02f;
        second.Gap = 0.03f;

        system.SolveHeap(inverseStep: 66f);

        system.ContactCount.ShouldBe((short)1);
        system.FirstContact.ShouldBeSameAs(first);
    }

    /// <remarks>
    /// **A contact just past the contact gap moves to the head when both friction cores carry bit 0** — `gap > block[0x46] +
    /// block[0x43]` (`0.01·d` past it) and `(core₀ &amp; core₁) &amp; 1` — unlinked and linked again. The controls: a gap under the line,
    /// or one core without the bit, stays where it is.
    /// </remarks>
    [TestCase(0.02f, true, true)]
    [TestCase(0.012f, true, false)]
    [TestCase(0.02f, false, false)]
    public void SolveHeap_AContactPastTheContactGapOnFlaggedCores_MovesToTheHead(float gap, bool flagged, bool moved)
    {
        (IvpFrictionSystem system, IvpContactPoint first, IvpContactPoint second) = TwoContacts();
        IvpContactPoint tail = system.FirstContact == first ? second : first;
        IvpContactPoint head = system.FirstContact!;
        head.Gap = 0.012f;
        tail.Gap = gap;
        // The friction cores are separate bodies here, so the test reads them rather than the physical cores.
        IvpRigidBody firstFriction = new() { FlagBit0 = flagged };
        IvpRigidBody secondFriction = new() { FlagBit0 = true };

        foreach (IvpContactPoint point in new[] { head, tail })
        {
            point.FirstObject.FrictionCore = firstFriction;
            point.SecondObject.FrictionCore = secondFriction;
            point.Record!.Outside = false;
        }

        system.SolveHeap(inverseStep: 66f);

        system.ContactCount.ShouldBe((short)2, "the control: neither is past the resting gap");
        (system.FirstContact == tail).ShouldBe(moved);
    }

    /// <remarks>
    /// **A removed contact leaves both objects' friction synapse lists** — the destructor `FUN_180083210`'s last step, so the next
    /// collision of the same features builds a new contact rather than finding the removed one.
    /// </remarks>
    [Test]
    public void RemoveContact_AContact_LeavesBothObjectsContactLists()
    {
        (IvpFrictionSystem system, IvpContactPoint contact) = Linked();
        contact.FirstObject.ContactPoints.ShouldContain(contact, "the control: the constructor linked it to both");
        contact.SecondObject.ContactPoints.ShouldContain(contact, "the control");

        system.RemoveContact(contact, contact.FirstObject.Core!, contact.SecondObject.Core!, now: 0d);

        contact.FirstObject.ContactPoints.ShouldNotContain(contact);
        contact.SecondObject.ContactPoints.ShouldNotContain(contact);
    }

    private static (IvpFrictionSystem, IvpContactPoint) Linked()
    {
        (IvpFrictionSystem system, _, IvpContactPoint contact) = Revalidate.Linked(out _);
        Inside(contact);

        return (system, contact);
    }

    /// <summary>
    /// A built record, inside its feature and on objects whose friction cores are their cores. *The fabricated geometry measures
    /// outside*, which the drop reads, so each test says so rather than inheriting it.
    /// </summary>
    private static void Inside(IvpContactPoint contact)
    {
        IvpContactRecord.Build(contact, Revalidate.First(contact), Revalidate.Second(contact), 0d);
        contact.Record!.Outside = false;
        contact.FirstObject.FrictionCore = contact.FirstObject.Core;
        contact.SecondObject.FrictionCore = contact.SecondObject.Core;
    }

    private static (IvpFrictionSystem, IvpContactPoint, IvpContactPoint) TwoContacts()
    {
        (IvpFrictionSystem system, IvpContactPoint first) = Linked();
        IvpContactPoint second = Revalidate.Contact(system.Pairs[0].FirstCore, system.Pairs[0].SecondCore);
        IvpFrictionLinking.LinkContactByCore(second, system.Pairs[0].FirstCore, system.Pairs[0].SecondCore, system.Environment);
        Inside(second);
        system.ContactCount.ShouldBe((short)2, "the control: the fixture holds two contacts");

        return (system, first, second);
    }
}
