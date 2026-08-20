using System;
using System.Collections.Generic;
using System.IO;

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

    /// <summary>One decoded property standing on a definition built by hand.</summary>
    private static DecodedProperty Property(
        SendProperty definition, PropertyValue value, SendProperty? element = null) =>
        new(0, new FlatProperty(definition, "DT_Test", element), value);
}
