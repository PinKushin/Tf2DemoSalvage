using System;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Probe.Oracle;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>Measuring a pair's contacts again and dropping the ones now outside — <c>FUN_180083b30</c> (B369, D172).</summary>
/// <remarks>
/// **Read from the disassembly** (`docs/findings/51`): the pair's contacts are walked last to first, each record rebuilt
/// (`FUN_18008d0c0`) and its materials set (`FUN_1800908d0`), and a contact whose record's `+0x76` is set is removed as
/// <c>FUN_180083e40</c> removes it. It returns how many are left. Synthetic conformance (D38): a point against an edge is
/// outside on its first measure when it lies before the edge's start, and inside on every later one.
/// </remarks>
public sealed class IvpFrictionSystemRevalidatePairTests
{
    [Test]
    public void RevalidatePair_AFreshContactOutsideTheEdge_IsRemovedAndNoneAreLeft()
    {
        (IvpFrictionSystem system, IvpFrictionPair pair, IvpContactPoint contact) = Linked(out _);

        short left = system.RevalidatePair(pair, Sides, Materials.Instance, now: 1d);

        left.ShouldBe((short)0);
        system.ContactCount.ShouldBe((short)0);
        system.Pairs.ShouldBeEmpty();
        system.SplitCheckDue.ShouldBeTrue();
        contact.FrictionSystem.ShouldBeNull();
    }

    [Test]
    public void RevalidatePair_AContactAlreadyMeasured_StaysAndGetsANewRecordAndMaterials()
    {
        (IvpFrictionSystem system, IvpFrictionPair pair, IvpContactPoint contact) = Linked(out _);
        IvpContactRecord old = IvpContactRecord.Build(contact, First(contact), Second(contact), 0d);

        short left = system.RevalidatePair(pair, Sides, Materials.Instance, now: 1d);

        left.ShouldBe((short)1);
        system.ContactCount.ShouldBe((short)1);
        contact.Record.ShouldNotBeSameAs(old, "the record is rebuilt");
        contact.Record!.Outside.ShouldBeFalse();
        contact.Friction.ShouldBe(0.5f, "the materials are set on the rebuilt record");
    }

    [Test]
    public void RevalidatePair_OneOutsideOneInside_RemovesOnlyTheOutsideOne()
    {
        (IvpFrictionSystem system, IvpFrictionPair pair, IvpContactPoint measured) = Linked(out IvpRigidBody world);
        IvpContactRecord.Build(measured, First(measured), Second(measured), 0d);
        IvpContactPoint fresh = Contact(measured.FirstObject.Core!, world);
        IvpFrictionLinking.LinkContactByCore(fresh, measured.FirstObject.Core!, world, system.Environment);

        short left = system.RevalidatePair(pair, Sides, Materials.Instance, now: 1d);

        left.ShouldBe((short)1);
        pair.Contacts.ShouldBe([measured]);
        system.FirstContact.ShouldBe(measured);
        system.SplitCheckDue.ShouldBeFalse();
    }

    /// <remarks>
    /// **Last to first is observable**: with the outside contact filed first, a forward walk removes it and stops one short,
    /// never rebuilding the contact after it.
    /// </remarks>
    [Test]
    public void RevalidatePair_OutsideContactFiledFirst_StillRebuildsTheOneAfterIt()
    {
        (IvpFrictionSystem system, IvpFrictionPair pair, IvpContactPoint outside) = Linked(out IvpRigidBody world);
        IvpContactPoint measured = Contact(outside.FirstObject.Core!, world);
        IvpFrictionLinking.LinkContactByCore(measured, outside.FirstObject.Core!, world, system.Environment);
        IvpContactRecord old = IvpContactRecord.Build(measured, First(measured), Second(measured), 0d);

        short left = system.RevalidatePair(pair, Sides, Materials.Instance, now: 1d);

        left.ShouldBe((short)1);
        measured.Record.ShouldNotBeSameAs(old, "the later contact is still visited");
    }

    private static readonly IvpLedgeSide Vertex =
        IvpContactGeometryConformanceTests.Side([(0f, 0f, 0f), (1f, 0f, 0f), (0f, 1f, 0f)], (-1d, 0d, 3d));

    private static readonly IvpLedgeSide Flat = IvpContactGeometryConformanceTests.Flat();

    internal static (IvpLedgeSide First, IvpLedgeSide Second) Sides(IvpContactPoint contact) => (Vertex, Flat);

    internal static IvpContactBody First(IvpContactPoint contact) => new(Vertex, contact.FirstObject.Core!, 0f);

    internal static IvpContactBody Second(IvpContactPoint contact) => new(Flat, contact.SecondObject.Core!, 0f);

    internal static (IvpFrictionSystem, IvpFrictionPair, IvpContactPoint) Linked(out IvpRigidBody world)
    {
        IvpRigidBody movable = new();
        world = new IvpRigidBody { Immovable = true };
        IvpContactPoint contact = Contact(movable, world);
        IvpFrictionSystem system = IvpFrictionLinking.LinkContactByCore(contact, movable, world, Environment());

        return (system, system.Pairs[0], contact);
    }

    internal static IvpContactPoint Contact(IvpRigidBody movable, IvpRigidBody world)
    {
        IvpContactPoint contact = IvpContactGeometryConformanceTests.Contact(IvpFeatureKind.Point, Vertex, IvpFeatureKind.Edge, Flat);
        contact.FirstObject.Core = movable;
        contact.SecondObject.Core = world;
        contact.FirstObject.Material = new IvpReplayMaterial(0d, 0d, HasSecondFriction: false);
        contact.SecondObject.Material = contact.FirstObject.Material;

        return contact;
    }

    internal static IvpImpactEnvironment Environment() =>
        new()
        {
            InverseStep = 0d,
            Step = 0d,
            Limits = new IvpAnomalyLimits(0f, 0, 0f, 0, 0f, 0f),
            Anomalies = new VphysicsAnomalyManager(new IvpImpactReplay.FixedAnswer(answer: false)),
            Materials = new IvpReplayMaterials(new IvpReplayMaterial(0d, 0d, HasSecondFriction: false), 0d, 0d),
            GravityLength = 0f,
        };

    internal sealed class Materials : IIvpMaterialManager
    {
        public static readonly Materials Instance = new();

        public IIvpMaterial MaterialAt(IvpCollisionObject collisionObject, int index) =>
            new IvpReplayMaterial(0d, 0d, HasSecondFriction: false);

        public double FrictionFactor(IvpContactRecord record) => 0.5d;

        public double Elasticity(IvpContactRecord record) => 0.1d;
    }
}
