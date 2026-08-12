using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Tests.Schema;

/// <summary>
/// Every element of an array carries its own encoding shape, and the codec must keep all of them.
/// </summary>
/// <remarks>
/// **RISKS B27.** Shapes were recorded for top-level properties and silently dropped for array
/// elements, because <c>ReadArray</c> called the overload of <c>ReadValue</c> that discards them.
/// The elements then re-encoded at whatever width shape 0 implies, which is the original width
/// only by luck.
///
/// It survived a corpus of thirteen demos and 111,228 snapshots. It took recordings of game modes
/// nobody had sampled — a PASS Time map with a 16-element <c>m_trackPoints</c>, which came out
/// fifteen bits long, and a custom CTF map with a one-element <c>m_vecPoints</c>, three bits long.
///
/// **This test exists because that corpus is not in CI.** The gate that found the bug walks the
/// local corpus, which is git-ignored, so a regression would be invisible to the pipeline. Here the
/// same property is asserted against a hand-built schema that needs no demo at all.
/// </remarks>
public sealed class ArrayElementShapeTests
{
    private const int InsideArray = 1 << 8;
    private const int CoordMp = 1 << 13;

    /// <summary>An array of multiplayer coordinates, whose elements each carry an in-bounds bit.</summary>
    /// <remarks>
    /// The element template is the property *before* the array and is marked
    /// <c>SPROP_INSIDEARRAY</c>, which is Source's convention and the reason the flattened list
    /// holds one entry rather than two.
    /// </remarks>
    private static DemoSchema Schema()
    {
        List<SendProperty> properties =
        [
            new(SendPropType.Float, "m_flPoint", CoordMp | InsideArray, string.Empty, 0f, 0f, 0, 0),
            new(SendPropType.Array, "m_vecPoints", 0, string.Empty, 0f, 0f, 0, 8),
        ];

        return new DemoSchema(
            [new SendTable("DT_Test", true, properties)],
            [new ServerClass(0, "CTest", "DT_Test"), new ServerClass(1, "COther", "DT_Test")]);
    }

    private static EntityDecoder Decoder() => new(Schema(), 2);

    private static PacketEntitiesMessage Header(int updated) =>
        new(2048, false, null, false, updated, 0, false, ReadOnlyMemory<byte>.Empty);

    private static DecodedEntity EntityWith(IReadOnlyList<int> shapes, params float[] values)
    {
        FlatProperty flat = Decoder().FlattenedFor(0)[0];

        return new DecodedEntity(
            3,
            0,
            7,
            EntityUpdateType.Enter,
            [
                new DecodedProperty(
                    0,
                    flat,
                    PropertyValue.FromArray([.. values.Select(PropertyValue.FromFloat)]),
                    0,
                    0,
                    shapes),
            ]);
    }

    [Test]
    public void EachElementsShapeSurvivesTheRoundTrip()
    {
        // Shapes deliberately differ between elements. Equal shapes would make a decoder that
        // returns one shape for the whole array indistinguishable from a correct one - the same
        // "wrong condition" trap as choosing inputs a broken implementation happens to agree on.
        int[] shapes = [1, 0, 1, 0];
        DecodedEntity entity = EntityWith(shapes, 8.5f, 2.25f, -4.75f, 16f);

        byte[] body = Decoder().EncodeEntities([entity], [], isDelta: false, lengthBits: 0, out int written);

        EntityDecoder reader = Decoder();
        IReadOnlyList<DecodedEntity> decoded = reader.Decode(body, Header(1), written);

        DecodedProperty property = decoded.ShouldHaveSingleItem().Properties.ShouldHaveSingleItem();
        property.ElementShapes.ShouldNotBeNull();
        property.ElementShapes.ShouldBe(shapes);
    }

    [Test]
    public void AnArrayReEncodesToTheExactBytesItDecodedFrom()
    {
        // **This checks decode/encode SYMMETRY, not encoder correctness, and the difference is
        // worth stating because it is easy to overclaim.** The body here is produced by our own
        // encoder, so an error applied uniformly appears in both the original and the re-encode
        // and they match anyway. Sabotaging the encoder to use element 0's shape for every
        // element leaves this test green; only EachElementsShapeSurvivesTheRoundTrip notices.
        //
        // What it does catch is the two halves drifting apart - a decoder that stops recording
        // shapes, or an encoder that stops honouring them, which is exactly how B27 arose. For
        // correctness against bytes this project did not write, the corpus gate is the authority,
        // and it compares against 111,228 snapshots Valve produced.
        DecodedEntity entity = EntityWith([1, 0, 1, 0], 8.5f, 2.25f, -4.75f, 16f);

        byte[] body = Decoder().EncodeEntities([entity], [], isDelta: false, lengthBits: 0, out int written);

        EntityDecoder reader = Decoder();
        IReadOnlyList<DecodedEntity> decoded = reader.Decode(body, Header(1), written);
        int consumed = reader.EntitySectionBits;

        byte[] again = reader.EncodeEntities(decoded, [], isDelta: false, lengthBits: 0, out int rewritten);

        rewritten.ShouldBe(consumed);
        rewritten.ShouldBe(written);
        again.ShouldBe(body);
    }

    [Test]
    public void EntityEndBits_ReportsWhereEachEntityFinished()
    {
        // The diagnostic that made B27 findable. A snapshot-wide difference says nothing about
        // which of three hundred entities caused it; subtracting consecutive entries here gives
        // each entity's decoded width, which narrows a mismatch to one entity and then one
        // property.
        DecodedEntity first = EntityWith([1, 0], 8.5f, 2.25f);
        DecodedEntity second = EntityWith([0], 3.5f) with { EntityIndex = 9 };

        byte[] body = Decoder().EncodeEntities(
            [first, second], [], isDelta: false, lengthBits: 0, out int written);

        EntityDecoder reader = Decoder();
        reader.Decode(body, Header(2), written);

        reader.EntityEndBits.Count.ShouldBe(2);
        reader.EntityEndBits[0].ShouldBeLessThan(reader.EntityEndBits[1]);
        reader.EntityEndBits[1].ShouldBe(reader.EntitySectionBits);
    }
}
