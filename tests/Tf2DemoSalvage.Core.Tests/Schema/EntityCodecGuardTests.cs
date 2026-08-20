using System;
using System.Collections.Generic;
using System.IO;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Tests.Schema;

/// <summary>
/// The structural guards in the entity codec, and what each one is guarding against.
/// </summary>
/// <remarks>
/// **These cannot be reached from a demo, and that is the point.** Every one of them fires on a
/// property definition that no schema on the wire produces — a <c>DataTable</c> in a flattened
/// list, an array with no element template. They exist because the flattener is the highest-risk
/// code here (<c>RISKS.md</c> B4): entity updates address properties by POSITION, so a flattener
/// that emitted structure where values belong would shift every index after it and read real
/// numbers into the wrong fields. Nothing about that looks wrong afterwards.
///
/// So the guard is not defensive padding; it is the flattener's contract stated where it is used.
/// A test for it has to build the impossible input by hand, which is exactly why these lines sat
/// uncovered while the decoder itself was exercised on ten demos.
///
/// <c>docs/memory/most-of-a-decoder-is-untested.md</c> is the general form: real files take one
/// path, and the branches that matter are the ones they never take.
/// </remarks>
public sealed class EntityCodecGuardTests
{
    [Test]
    public void EncodeProperties_ADataTableInAFlattenedList_SaysItIsStructureRatherThanAValue()
    {
        // A DataTable property is a link to another table — it says where properties come from,
        // not what any of them holds. Reaching the value encoder with one means the flattener kept
        // a node it should have descended through, which shifts every index after it.
        InvalidDataException failure = Should.Throw<InvalidDataException>(
            () => EntityDecoder.EncodeProperties([Property(
                new SendProperty(
                    SendPropType.DataTable, "baseplayer", 0, "DT_BasePlayer", 0f, 0f, 0, 0),
                PropertyValue.FromInt(1))]));

        failure.Message.ShouldContain("baseplayer");
        failure.Message.ShouldContain("structure, not values");
    }

    [Test]
    public void EncodeProperties_AnArrayWithNoElementTemplate_SaysItCannotBeEncoded()
    {
        // **An array's width lives in its element template, not in the array property.** Without
        // it there is no way to know how many bits each element takes — and guessing would write a
        // body that decodes as the right count of the wrong numbers.
        InvalidDataException failure = Should.Throw<InvalidDataException>(
            () => EntityDecoder.EncodeProperties([Property(
                new SendProperty(SendPropType.Array, "m_iTeam", 0, "", 0f, 0f, 0, 4),
                PropertyValue.FromArray([PropertyValue.FromInt(2)]),
                element: null)]));

        failure.Message.ShouldContain("m_iTeam");
        failure.Message.ShouldContain("no element template");
    }

    [Test]
    public void EncodeProperties_AnArrayWithATemplate_StillEncodes()
    {
        // **The sensitivity control for the pair above.** An EncodeProperties that threw on every
        // array would satisfy the second test while breaking every player resource in the corpus.
        byte[] encoded = EntityDecoder.EncodeProperties([Property(
            new SendProperty(SendPropType.Array, "m_iTeam", 0, "", 0f, 0f, 0, 4),
            PropertyValue.FromArray([PropertyValue.FromInt(2), PropertyValue.FromInt(3)]),
            element: new SendProperty(SendPropType.Int, "000", 0, "", 0f, 0f, 3, 0))]);

        encoded.ShouldNotBeEmpty();
    }

    [Test]
    public void DecodeTempEntities_ABodyLongerThanTheStatedLength_SaysWhatItConsumed()
    {
        // **The message states its own length, so a correct reading lands on it.** Reading past it
        // means the layout is wrong for this demo rather than the demo being damaged, and the
        // numbers are what tell the two apart — so the message carries both.
        DemoSchema schema = SyntheticPlayer.SchemaWithProp();
        EntityDecoder decoder = new(
            schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));

        byte[] body = decoder.EncodeTempEntities(
            [new DecodedTempEntity(SyntheticPlayer.PropClassId, 0f, [])],
            reliable: false,
            lengthBits: 0);

        InvalidDataException failure = Should.Throw<InvalidDataException>(
            () => decoder.DecodeTempEntities(body, count: 1, lengthBits: 1));

        failure.Message.ShouldContain("stated 1");
    }

    [Test]
    public void DecodeTempEntities_ABodyOfItsStatedLength_IsAccepted()
    {
        // The control again: an overrun check that fired unconditionally would look identical from
        // the test above and would refuse every effect in every demo.
        DemoSchema schema = SyntheticPlayer.SchemaWithProp();
        EntityDecoder decoder = new(
            schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));

        byte[] body = decoder.EncodeTempEntities(
            [new DecodedTempEntity(SyntheticPlayer.PropClassId, 0f, [])],
            reliable: false,
            lengthBits: 0);

        decoder.DecodeTempEntities(body, count: 1, lengthBits: body.Length * 8)
            .ShouldHaveSingleItem().ClassId.ShouldBe(SyntheticPlayer.PropClassId);
    }

    [Test]
    public void Decode_AnArrayLongerThanItsDefinitionAllows_SaysBothNumbers()
    {
        // **An array's count is sized from its declared maximum, not sent at a fixed width**, so a
        // count larger than the maximum still fits in the field — 5 in a 3-bit count for a
        // four-element array. Nothing about the bits says it is wrong; only the definition does.
        //
        // Reading it anyway would consume five elements' worth of bits from a body holding four,
        // which desynchronises everything after it in the same entity. The guard names both
        // numbers because "an array is too long" leaves a reader nothing to check against.
        EntityDecoder decoder = ArrayDecoder();
        IReadOnlyList<FlatProperty> flat = decoder.FlattenedFor(ArrayClassId);

        // Encoded through the writer, which takes the caller's word for the count — so this is a
        // body a buggy sender could genuinely produce rather than bytes typed out by hand.
        byte[] body = EntityDecoder.EncodeProperties(
        [
            new DecodedProperty(
                0,
                flat[0],
                PropertyValue.FromArray(
                [
                    PropertyValue.FromInt(1), PropertyValue.FromInt(2),
                    PropertyValue.FromInt(3), PropertyValue.FromInt(4),
                    PropertyValue.FromInt(5),
                ])),
        ]);

        decoder.SetBaseline(ArrayClassId, body);

        InvalidDataException failure = Should.Throw<InvalidDataException>(
            () => decoder.Baseline(ArrayClassId));

        failure.Message.ShouldContain("5 elements");
        failure.Message.ShouldContain("4");
    }

    [Test]
    public void Decode_AnArrayOfTheLengthItsDefinitionAllows_IsAccepted()
    {
        // The control: a guard that refused every array would satisfy the test above and break the
        // player resource, which is where team and class live for every modern demo.
        EntityDecoder decoder = ArrayDecoder();
        IReadOnlyList<FlatProperty> flat = decoder.FlattenedFor(ArrayClassId);

        byte[] body = EntityDecoder.EncodeProperties(
        [
            new DecodedProperty(
                0,
                flat[0],
                PropertyValue.FromArray(
                    [PropertyValue.FromInt(1), PropertyValue.FromInt(2)])),
        ]);

        decoder.SetBaseline(ArrayClassId, body);

        decoder.Baseline(ArrayClassId).ShouldNotBeNull()
            .ShouldHaveSingleItem().Value.AsArray.Count.ShouldBe(2);
    }

    /// <summary>Class id of the array-bearing class below.</summary>
    private const int ArrayClassId = 0;

    /// <summary>Flag marking the element template that precedes an array property.</summary>
    /// <remarks>
    /// <c>SPROP_INSIDEARRAY</c>. Source emits the template as an ordinary property immediately
    /// before the array and marks it with this, which is how the flattener tells the two apart —
    /// the template is skipped in its own right and attached to the array that follows it.
    /// </remarks>
    private const int InsideArray = 1 << 8;

    /// <summary>A decoder whose one class carries a four-element array.</summary>
    private static EntityDecoder ArrayDecoder()
    {
        DemoSchema schema = new(
            [
                new SendTable("DT_Test", NeedsDecoder: true,
                [
                    new SendProperty(
                        SendPropType.Int, "000", InsideArray, "", 0f, 0f, 8, 0),
                    new SendProperty(SendPropType.Array, "m_iTeam", 0, "", 0f, 0f, 0, 4),
                ]),
            ],
            [new ServerClass(ArrayClassId, "CTest", "DT_Test")]);

        return new EntityDecoder(schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));
    }

    /// <summary>One decoded property standing on a definition built by hand.</summary>
    private static DecodedProperty Property(
        SendProperty definition, PropertyValue value, SendProperty? element = null) =>
        new(0, new FlatProperty(definition, "DT_Test", element), value);
}
