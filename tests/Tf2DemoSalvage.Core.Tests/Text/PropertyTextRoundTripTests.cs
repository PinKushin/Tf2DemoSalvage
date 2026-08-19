using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Core.Text;

namespace Tf2DemoSalvage.Core.Tests.Text;

/// <summary>
/// Every property value rendered to text and compiled back into the same bits.
/// </summary>
/// <remarks>
/// **The text form is a separate code path from the wire form, and it is the one a person edits.**
/// <c>SyntheticPropertyTypeTests</c> proves the encoder and decoder agree on each type; this
/// proves the value survives being written as text and read back — which is what makes a demo
/// editable rather than merely reproducible.
///
/// The failure it guards is specific to text: a float written with too few digits comes back as a
/// different number and the demo still compiles, describing something slightly other than what was
/// recorded. Byte-exactness catches that; a value assertion alone would not, because the value
/// would look reasonable.
///
/// Every type is in one entity deliberately. The assembly writes them as a tagged sequence, so a
/// tag read as its neighbour consumes the wrong number of tokens and everything after it shifts —
/// which one property at a time cannot show.
/// </remarks>
public sealed class PropertyTextRoundTripTests
{
    private const int Unsigned = 1 << 0;
    private const int Coord = 1 << 1;
    private const int NoScale = 1 << 2;
    private const int NormalOrVarInt = 1 << 5;
    private const int InsideArray = 1 << 8;

    [Test]
    public void Assemble_AnEntityCarryingEveryPropertyType_CompilesBackToItsOwnBytes()
    {
        // The whole point: text out, text in, bits identical. A float rendered with too few digits
        // fails here and nowhere else.
        byte[] original = Demo();

        (DemoHeader header, IReadOnlyList<DemoCommand> commands) = Read(original);

        StringWriter text = new() { NewLine = "\n" };
        DemoAssembly.Write(text, header, commands);

        using StringReader reader = new(text.ToString());
        (DemoHeader compiledHeader, IReadOnlyList<DemoCommand> compiled) =
            DemoAssembly.Parse(reader);

        compiled.Count.ShouldBe(commands.Count);
        DemoWriter.Write(compiledHeader, compiled).ShouldBe(original);
    }

    [Test]
    public void Assemble_EachPropertyValue_IsRenderedRatherThanLeftAsBits()
    {
        // Byte-exactness above is satisfied by a raw hex fallback, so it cannot say the values
        // were understood. This does: each one appears by name in the text.
        string assembly = Assemble(Demo());

        assembly.ShouldNotContain("PacketEntities declined");

        foreach (string name in new[]
        {
            "m_iPlain", "m_iVarInt", "m_uVarInt", "m_flNoScale", "m_flNormal",
            "m_flCoord", "m_szName", "m_iAmmo",
        })
        {
            assembly.ShouldContain(name);
        }

        // The string's own text, which is the one value a tag confusion would drop rather than
        // corrupt — a string read as a vector consumes three tokens and loses it entirely.
        assembly.ShouldContain("Ко́т");
    }

    [Test]
    public void Assemble_AnArraysElements_AreWrittenWithTheirCountAndReadBackByIt()
    {
        // An array renders as a count followed by that many values, and the elements use the
        // ELEMENT template's encoding rather than the array's. A reader taking the declared
        // capacity instead of the written count consumes eight values where three were sent.
        string assembly = Assemble(Demo());

        using StringReader reader = new(assembly);
        (_, IReadOnlyList<DemoCommand> compiled) = DemoAssembly.Parse(reader);

        DecodedEntity entity = Decode(compiled);

        PropertyValue ammo = entity.Properties
            .First(property => property.Definition.Property.Name == "m_iAmmo")
            .Value;

        ammo.AsArray.Select(element => element.AsInt).ShouldBe([10, 0, 255]);
    }

    [Test]
    public void Assemble_EveryValue_ComesBackEqualToWhatWentIn()
    {
        // The value-level companion to the byte comparison. Byte-exactness proves the bits match;
        // this says which value moved when they do not, which is the difference between a failure
        // that names a property and one that names an offset.
        using StringReader reader = new(Assemble(Demo()));
        (_, IReadOnlyList<DemoCommand> compiled) = DemoAssembly.Parse(reader);

        DecodedEntity entity = Decode(compiled);

        Value(entity, "m_iPlain").AsInt.ShouldBe(4242);
        Value(entity, "m_iVarInt").AsInt.ShouldBe(-1234);
        Value(entity, "m_uVarInt").AsInt.ShouldBe(3_000_000_000L);
        Value(entity, "m_flNoScale").AsFloat.ShouldBe(1.2345678f);
        Value(entity, "m_flNormal").AsFloat.ShouldBe(-0.5f, 0.001f);
        Value(entity, "m_flCoord").AsFloat.ShouldBe(12.5f, 0.05f);
        Value(entity, "m_szName").AsString.ShouldBe("Ко́т");
    }

    private static PropertyValue Value(DecodedEntity entity, string name) =>
        entity.Properties
            .First(property => string.Equals(
                property.Definition.Property.Name, name, StringComparison.Ordinal))
            .Value;

    private static DecodedEntity Decode(IReadOnlyList<DemoCommand> commands)
    {
        EntityDecoder decoder = Decoder();

        PacketEntitiesMessage snapshot = commands
            .Where(command => command.Type == DemoCommandType.Packet)
            .SelectMany(Messages)
            .OfType<PacketEntitiesMessage>()
            .Last();

        return decoder.Decode(snapshot.Body.Span, snapshot, snapshot.LengthBits)
            .ShouldHaveSingleItem();
    }

    private static IEnumerable<INetMessage> Messages(DemoCommand command)
    {
        NetDecodeState state = new() { NetworkProtocol = SyntheticDemo.DefaultProtocol };
        return NetMessageReader.Read(command.Payload.Span, state).Messages;
    }

    /// <summary>A demo whose one entity carries a property of every encoding.</summary>
    private static byte[] Demo()
    {
        EntityDecoder decoder = Decoder();
        IReadOnlyList<FlatProperty> flat = decoder.FlattenedFor(0);

        Dictionary<string, PropertyValue> values = new()
        {
            ["m_iPlain"] = PropertyValue.FromInt(4242),
            ["m_iVarInt"] = PropertyValue.FromInt(-1234),
            ["m_uVarInt"] = PropertyValue.FromInt(3_000_000_000L),
            ["m_flNoScale"] = PropertyValue.FromFloat(1.2345678f),
            ["m_flNormal"] = PropertyValue.FromFloat(-0.5f),
            ["m_flCoord"] = PropertyValue.FromFloat(12.5f),
            ["m_szName"] = PropertyValue.FromString("Ко́т"),
            ["m_iAmmo"] = PropertyValue.FromArray(
            [
                PropertyValue.FromInt(10),
                PropertyValue.FromInt(0),
                PropertyValue.FromInt(255),
            ]),
        };

        List<DecodedProperty> properties = [];
        foreach ((string name, PropertyValue value) in values)
        {
            int index = flat.Select((entry, i) => (entry, i))
                .First(pair => pair.entry.Property.Name == name).i;

            properties.Add(new DecodedProperty(index, flat[index], value));
        }

        properties.Sort((left, right) => left.Index.CompareTo(right.Index));

        DecodedEntity entity = new(1, 0, 1, EntityUpdateType.Enter, properties);
        byte[] body = decoder.EncodeEntities([entity], [], isDelta: false, 0, out int bits);

        return SyntheticDemo.From(
            SyntheticDemo.DefaultProtocol,
            SyntheticDemo.DataTables(Schema()),
            SyntheticDemo.Packet(
                SyntheticDemo.DefaultProtocol,
                66,
                new PacketEntitiesMessage(
                    MaxEntries: 64,
                    IsDelta: false,
                    DeltaFromTick: null,
                    BaselineIndex: false,
                    UpdatedEntries: 1,
                    LengthBits: bits,
                    UpdateBaseline: false,
                    Body: body)));
    }

    private static EntityDecoder Decoder()
    {
        DemoSchema schema = Schema();
        return new EntityDecoder(schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));
    }

    private static DemoSchema Schema() => new(
        [
            new SendTable("DT_Test", NeedsDecoder: true,
            [
                new SendProperty(SendPropType.Int, "m_iPlain", 0, "", 0f, 0f, 20, 0),
                new SendProperty(SendPropType.Int, "m_iVarInt", NormalOrVarInt, "", 0f, 0f, 32, 0),
                new SendProperty(
                    SendPropType.Int, "m_uVarInt", NormalOrVarInt | Unsigned, "", 0f, 0f, 32, 0),
                new SendProperty(SendPropType.Float, "m_flNoScale", NoScale, "", 0f, 0f, 32, 0),
                new SendProperty(SendPropType.Float, "m_flNormal", NormalOrVarInt, "", 0f, 0f, 0, 0),
                new SendProperty(SendPropType.Float, "m_flCoord", Coord, "", 0f, 0f, 0, 0),
                new SendProperty(SendPropType.String, "m_szName", 0, "", 0f, 0f, 0, 0),

                // The element template immediately precedes its array, marked so the flattener
                // attaches it rather than emitting an entry for it.
                new SendProperty(SendPropType.Int, "element", InsideArray, "", 0f, 0f, 12, 0),
                new SendProperty(SendPropType.Array, "m_iAmmo", 0, "", 0f, 0f, 0, 8),
            ]),
        ],
        [new ServerClass(0, "CTest", "DT_Test")]);

    private static string Assemble(byte[] demo)
    {
        (DemoHeader header, IReadOnlyList<DemoCommand> commands) = Read(demo);

        StringWriter text = new() { NewLine = "\n" };
        DemoAssembly.Write(text, header, commands);
        return text.ToString();
    }

    private static (DemoHeader Header, IReadOnlyList<DemoCommand> Commands) Read(byte[] demo) =>
        (DemoHeader.Parse(demo.AsSpan(0, DemoHeader.SizeBytes)),
            [.. DemoCommandReader.Read(demo.AsMemory(DemoHeader.SizeBytes))]);
}
