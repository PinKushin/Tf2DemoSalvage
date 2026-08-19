using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Tests.Schema;

/// <summary>
/// Every property encoding a schema can declare, round-tripped through an entity snapshot.
/// </summary>
/// <remarks>
/// **The synthetic fixtures built so far only ever used four encodings** — a plain integer, a
/// range-encoded float, a two-component vector and a nested table — because that is all a player's
/// position and pose need. Everything else in <c>SendPropEncoder</c> was reachable only from real
/// demos: varints, no-scale floats, normals, the three coordinate forms, strings and arrays.
///
/// That is the shape of a fixture gap rather than a coverage number. A property type nothing
/// writes is a property type nothing checks, and the flag that decides between two of them is
/// **the same bit**: 1 &lt;&lt; 5 is <c>SPROP_NORMAL</c> on a float and <c>SPROP_VARINT</c> on an
/// integer, disambiguated only by the property's declared type. A test that never encodes both
/// cannot see them confused.
///
/// Each case below encodes a chosen value through a real entity snapshot and reads it back, so
/// what is measured is the encoder and decoder agreeing on a value the test named — not on
/// whatever a recording happened to contain.
/// </remarks>
public sealed class SyntheticPropertyTypeTests
{
    /// <summary><c>SPROP_UNSIGNED</c>.</summary>
    private const int Unsigned = 1 << 0;

    /// <summary><c>SPROP_COORD</c>.</summary>
    private const int Coord = 1 << 1;

    /// <summary><c>SPROP_NOSCALE</c>.</summary>
    private const int NoScale = 1 << 2;

    /// <summary><c>SPROP_NORMAL</c> on a float, <c>SPROP_VARINT</c> on an integer. One bit.</summary>
    private const int NormalOrVarInt = 1 << 5;

    private const int CoordMp = 1 << 13;
    private const int CoordMpLowPrecision = 1 << 14;
    private const int CoordMpIntegral = 1 << 15;

    [Test]
    public void RoundTrip_AVarIntProperty_CarriesSignedValuesBothWays()
    {
        // A varint is the same flag bit as SPROP_NORMAL and differs only by the property's type,
        // so a negative value is the case that separates a signed varint from an unsigned one:
        // zigzag keeps small magnitudes short, and reading it as unsigned produces a large
        // positive number rather than an error.
        Value("m_iSigned", Int("m_iSigned", NormalOrVarInt), PropertyValue.FromInt(-1234))
            .AsInt.ShouldBe(-1234);

        Value("m_iSigned", Int("m_iSigned", NormalOrVarInt), PropertyValue.FromInt(1234))
            .AsInt.ShouldBe(1234);
    }

    [Test]
    public void RoundTrip_AnUnsignedVarInt_KeepsALargeValueUnsigned()
    {
        // Without SPROP_UNSIGNED the same bits zigzag, so a value above int.MaxValue/2 comes back
        // as a different number entirely rather than as a failure.
        Value(
            "m_uLarge",
            Int("m_uLarge", NormalOrVarInt | Unsigned),
            PropertyValue.FromInt(3_000_000_000L))
            .AsInt.ShouldBe(3_000_000_000L);
    }

    [Test]
    public void RoundTrip_ANoScaleFloat_KeepsFullPrecision()
    {
        // SPROP_NOSCALE sends the raw 32 bits rather than a fraction of a declared range, so a
        // value with a long mantissa survives exactly. A range-encoded property would quantise it.
        Value(
            "m_flExact",
            Float("m_flExact", NoScale, low: 0f, high: 0f, bits: 32),
            PropertyValue.FromFloat(1.2345678f))
            .AsFloat.ShouldBe(1.2345678f);
    }

    [Test]
    public void RoundTrip_ANormalFloat_KeepsItsSignSeparatelyFromItsMagnitude()
    {
        // A normal is a sign bit and eleven fraction bits, which is a different shape from every
        // other float here — the sign is not part of the value. Negative zero and a small negative
        // are the cases that separate "sign carried" from "sign inferred".
        Value("m_flNormal", Float("m_flNormal", NormalOrVarInt, 0f, 0f, 0), PropertyValue.FromFloat(-0.5f))
            .AsFloat.ShouldBe(-0.5f, 0.001f);

        Value("m_flNormal", Float("m_flNormal", NormalOrVarInt, 0f, 0f, 0), PropertyValue.FromFloat(0.75f))
            .AsFloat.ShouldBe(0.75f, 0.001f);
    }

    [Test]
    public void RoundTrip_ACoordProperty_KeepsItsIntegerAndFractionParts()
    {
        // A coordinate is a presence bit per part, then an integer and a five-bit fraction. The
        // interesting values are the ones where one part is absent: a whole number sends no
        // fraction, and a pure fraction sends no integer.
        SendProperty coord = Float("m_vecCoord", Coord, 0f, 0f, 0);

        Value("m_vecCoord", coord, PropertyValue.FromFloat(12.5f)).AsFloat.ShouldBe(12.5f, 0.05f);
        Value("m_vecCoord", coord, PropertyValue.FromFloat(64f)).AsFloat.ShouldBe(64f, 0.05f);
        Value("m_vecCoord", coord, PropertyValue.FromFloat(0.25f)).AsFloat.ShouldBe(0.25f, 0.05f);
        Value("m_vecCoord", coord, PropertyValue.FromFloat(-12.5f)).AsFloat.ShouldBe(-12.5f, 0.05f);
    }

    [Test]
    public void RoundTrip_TheMultiplayerCoordForms_EachKeepTheirValue()
    {
        // Three separate encodings sharing a family: the standard multiplayer coord, a
        // low-precision one with three fraction bits instead of five, and an integral one with no
        // fraction at all. They differ in width, so reading one as another desynchronises rather
        // than rounding.
        Value("m_flMp", Float("m_flMp", CoordMp, 0f, 0f, 0), PropertyValue.FromFloat(20.5f))
            .AsFloat.ShouldBe(20.5f, 0.05f);

        Value(
            "m_flMpLow",
            Float("m_flMpLow", CoordMp | CoordMpLowPrecision, 0f, 0f, 0),
            PropertyValue.FromFloat(20.5f))
            .AsFloat.ShouldBe(20.5f, 0.2f);

        Value(
            "m_flMpInt",
            Float("m_flMpInt", CoordMp | CoordMpIntegral, 0f, 0f, 0),
            PropertyValue.FromFloat(21f))
            .AsFloat.ShouldBe(21f, 0.05f);
    }

    [Test]
    public void RoundTrip_AStringProperty_SurvivesIncludingNonAscii()
    {
        // Strings are length-prefixed and UTF-8. An ASCII reader corrupts a name into a plausible
        // one rather than failing, which is why the international case is asserted rather than
        // assumed — see docs/memory/international-names-are-required.md.
        Value("m_szName", String("m_szName"), PropertyValue.FromString("Ко́т"))
            .AsString.ShouldBe("Ко́т");

        Value("m_szName", String("m_szName"), PropertyValue.FromString(string.Empty))
            .AsString.ShouldBe(string.Empty);
    }

    [Test]
    public void RoundTrip_AnArrayProperty_KeepsEveryElementAndItsLength()
    {
        // **An array's elements use the ELEMENT template's encoding, not the array's**, which is
        // the detail a reader gets wrong by using the array property's own width for each element.
        // A count that is not the capacity is what separates "read the stated length" from "read
        // the declared maximum".
        PropertyValue read = Value(
            "m_iAmmo",
            Array("m_iAmmo", elements: 8),
            PropertyValue.FromArray(
            [
                PropertyValue.FromInt(10),
                PropertyValue.FromInt(0),
                PropertyValue.FromInt(255),
            ]));

        read.AsArray.Select(element => element.AsInt).ShouldBe([10, 0, 255]);
    }

    /// <summary>Encodes one property on an entity and reads its value back.</summary>
    private static PropertyValue Value(string name, SendProperty property, PropertyValue value)
    {
        DemoSchema schema = new(
            [new SendTable("DT_Test", NeedsDecoder: true, Properties(property))],
            [new ServerClass(0, "CTest", "DT_Test")]);

        EntityDecoder decoder = new(
            schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));

        IReadOnlyList<FlatProperty> flat = decoder.FlattenedFor(0);
        int index = flat.Select((entry, i) => (entry, i))
            .First(pair => pair.entry.Property.Name == name).i;

        DecodedEntity entity = new(
            1, 0, 1, EntityUpdateType.Enter, [new DecodedProperty(index, flat[index], value)]);

        byte[] body = decoder.EncodeEntities([entity], [], isDelta: false, 0, out int bits);

        PacketEntitiesMessage header = new(
            MaxEntries: 64,
            IsDelta: false,
            DeltaFromTick: null,
            BaselineIndex: false,
            UpdatedEntries: 1,
            LengthBits: bits,
            UpdateBaseline: false,
            Body: body);

        return decoder.Decode(body, header, bits)
            .ShouldHaveSingleItem()
            .Properties.ShouldHaveSingleItem()
            .Value;
    }

    /// <summary>
    /// The property, preceded by its element template when it is an array.
    /// </summary>
    /// <remarks>
    /// An array's element template is the property immediately before it, marked with the
    /// inside-array flag so the flattener attaches it rather than emitting it as an entry of its
    /// own. Declaring the array without one is what makes an array undecodable.
    /// </remarks>
    private static IReadOnlyList<SendProperty> Properties(SendProperty property) =>
        property.Type == SendPropType.Array
            ? [Int("element", InsideArray), property]
            : [property];

    /// <summary>The flattener's marker for an array's element template.</summary>
    private const int InsideArray = 1 << 8;

    private static SendProperty Int(string name, int flags = 0) =>
        new(SendPropType.Int, name, flags, string.Empty, 0f, 0f, 32, 0);

    private static SendProperty Float(string name, int flags, float low, float high, int bits) =>
        new(SendPropType.Float, name, flags, string.Empty, low, high, bits, 0);

    private static SendProperty String(string name) =>
        new(SendPropType.String, name, 0, string.Empty, 0f, 0f, 0, 0);

    private static SendProperty Array(string name, int elements) =>
        new(SendPropType.Array, name, 0, string.Empty, 0f, 0f, 0, elements);
}
