using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Core.Text;

namespace Tf2DemoSalvage.Core.Tests.Text;

/// <summary>
/// Entity snapshots decompiled to text and compiled back, with a schema present.
/// </summary>
/// <remarks>
/// **<c>svc_PacketEntities</c> only gets a text form when a schema is in hand**, and until a demo
/// could be written with one, that meant a real recording. <c>EveryMessageKindDemoTests</c> shows
/// the consequence directly: with no <c>dem_datatables</c> in the demo, the snapshot renders as
/// <c>raw … # PacketEntities declined</c> — byte-exact but unreadable.
///
/// With a schema it renders as named properties with their values, which is the difference between
/// a demo that round-trips and a demo that can be read. The two are separate properties and only
/// one of them was finished.
/// </remarks>
public sealed class EntityAssemblyDemoTests
{
    [Test]
    public void Assemble_ASnapshotWithASchema_NamesItsPropertiesInsteadOfDeclining()
    {
        // The observable difference from the schema-less case, asserted on the rendered text
        // rather than on a predicate: a report built from "can this type be written" reads clean
        // while every instance quietly falls back.
        string assembly = Assemble(Demo());

        assembly.ShouldContain("svc_packetentities");
        assembly.ShouldNotContain("PacketEntities declined");

        // The properties by name, which is the whole point of having the schema.
        assembly.ShouldContain("m_vecOrigin");
        assembly.ShouldContain("m_iTeamNum");
    }

    [Test]
    public void RoundTrip_ASnapshotWithASchema_CompilesBackToItsOwnBytes()
    {
        // **Stricter than the schema-less round trip.** Without a schema the body is copied
        // verbatim as bits, so byte-exactness proves only that a hex string survived. Here the
        // snapshot is decoded into named properties, written as text, parsed back, and re-encoded
        // through SendPropEncoder — so every value makes a round trip through a decimal
        // representation and back into bits.
        byte[] original = Demo();

        DemoHeader header = DemoHeader.Parse(original.AsSpan(0, DemoHeader.SizeBytes));
        List<DemoCommand> commands =
            [.. DemoCommandReader.Read(original.AsMemory(DemoHeader.SizeBytes))];

        StringWriter text = new() { NewLine = "\n" };
        DemoAssembly.Write(text, header, commands);

        using StringReader reader = new(text.ToString());
        (DemoHeader compiledHeader, IReadOnlyList<DemoCommand> compiled) =
            DemoAssembly.Parse(reader);

        compiled.Count.ShouldBe(commands.Count);
        DemoWriter.Write(compiledHeader, compiled).ShouldBe(original);
    }

    [Test]
    public void Trace_ASnapshotWithASchema_ExpandsEntitiesRatherThanCountingThem()
    {
        // A different writer over the same demo. The trace is what a person reads, and it is a
        // separate path from the assembly — a snapshot the assembly renders perfectly can still be
        // a bare count here.
        //
        // **Entity expansion is opt-in and the default is off**, which the first draft of this
        // test did not know. Both halves are asserted, because "the trace expands entities" and
        // "the trace expands entities when asked" are different claims and only the second is
        // true.
        (DemoHeader header, IReadOnlyList<DemoCommand> commands) = Read(Demo());

        string off = Trace(header, commands, new DemoTraceOptions());
        string on = Trace(
            header, commands, new DemoTraceOptions { IncludeEntities = true });

        // Named either way: the snapshot is a message and always gets a line.
        off.ShouldContain("svc_packetentities");
        on.ShouldContain("svc_packetentities");

        // Expanded only when asked. The class name comes from dem_datatables rather than from
        // svc_ClassInfo, which TF2 does not send names in.
        off.ShouldNotContain("CTFPlayer");
        on.ShouldContain("CTFPlayer");
        on.ShouldContain("m_vecOrigin");
    }

    private static string Trace(
        DemoHeader header, IReadOnlyList<DemoCommand> commands, DemoTraceOptions options)
    {
        StringWriter text = new() { NewLine = "\n" };
        DemoTraceWriter.Write(text, "synthetic.dem", header, commands, options: options);
        return text.ToString();
    }

    [Test]
    public void Assemble_APropertyValue_SurvivesItsDecimalRendering()
    {
        // **The failure this catches is silent and is not a decode bug.** A coordinate written to
        // text with too few digits comes back as a different number, and the demo still compiles —
        // it simply describes a player standing somewhere else. Byte-exactness above would catch
        // it; this says which value moved when it does.
        string assembly = Assemble(Demo());

        using StringReader reader = new(assembly);
        (_, IReadOnlyList<DemoCommand> compiled) = DemoAssembly.Parse(reader);

        DemoSchema schema = SyntheticPlayer.Schema();
        EntityDecoder decoder = new(
            schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));

        Core.Net.PacketEntitiesMessage snapshot = compiled
            .Where(command => command.Type == DemoCommandType.Packet)
            .SelectMany(command => Messages(command))
            .OfType<Core.Net.PacketEntitiesMessage>()
            .Last();

        DecodedEntity player = decoder
            .Decode(snapshot.Body.Span, snapshot, snapshot.LengthBits)
            .ShouldHaveSingleItem();

        (float x, float y) = Value(player, "m_vecOrigin").AsVectorXY;
        x.ShouldBe(512f, 0.5f);
        y.ShouldBe(-1024f, 0.5f);

        Value(player, "m_iTeamNum").AsInt.ShouldBe(2);
    }

    [Test]
    public void Assemble_TempEntitiesWithASchema_NamesTheEffectInsteadOfDeclining()
    {
        // The other half of EntityAssembly, and the one the schema-less demo cannot reach: a temp
        // entity's text form needs the schema for the same reason a snapshot's does. Without one
        // it renders as `raw … # TempEntities declined`.
        string assembly = Assemble(EffectDemo());

        assembly.ShouldContain("svc_tempentities");
        assembly.ShouldNotContain("TempEntities declined");
    }

    [Test]
    public void RoundTrip_TempEntitiesWithASchema_CompileBackToTheirOwnBytes()
    {
        // Effects go out to text as a class and a delay and come back through
        // EncodeTempEntities, so this exercises the encoder's one guess - that a repeated class is
        // omitted - against bytes rather than against values. The writer falls back to raw when
        // its re-encoding disagrees with the demo, which would show here as the assertion above
        // failing rather than as a byte mismatch.
        byte[] original = EffectDemo();

        (DemoHeader header, IReadOnlyList<DemoCommand> commands) = Read(original);

        StringWriter text = new() { NewLine = "\n" };
        DemoAssembly.Write(text, header, commands);

        using StringReader reader = new(text.ToString());
        (DemoHeader compiledHeader, IReadOnlyList<DemoCommand> compiled) =
            DemoAssembly.Parse(reader);

        DemoWriter.Write(compiledHeader, compiled).ShouldBe(original);
    }

    /// <summary>A demo carrying a schema and a burst of temp entities.</summary>
    /// <remarks>
    /// Class 0 throughout, because <c>SyntheticPlayer</c>'s schema declares one server class and
    /// the class-id field is sized by that count — the same constraint <c>TempEntityCodecTests</c>
    /// ran into. Two effects rather than one, so the repeat-the-previous-class rule is on the wire
    /// here and not merely in the codec's unit tests.
    /// </remarks>
    private static byte[] EffectDemo()
    {
        DemoSchema schema = SyntheticPlayer.Schema();
        EntityDecoder decoder = new(
            schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));

        DecodedTempEntity effect = new(ClassId: 0, DelaySeconds: 0f, Properties: []);
        byte[] body = decoder.EncodeTempEntities([effect, effect], reliable: false, lengthBits: 0);

        return SyntheticDemo.From(
            SyntheticDemo.DefaultProtocol,
            SyntheticDemo.DataTables(schema),
            SyntheticDemo.Packet(
                SyntheticDemo.DefaultProtocol,
                66,
                new Core.Net.TempEntitiesMessage(
                    Count: 2, BodyBits: body.Length * 8, Body: body)));
    }

    /// <summary>A demo carrying a schema and one player, positioned distinctively.</summary>
    private static byte[] Demo() =>
        SyntheticPlayer.Demo(new Dictionary<string, PropertyValue>
        {
            ["m_vecOrigin"] = PropertyValue.FromVectorXY(512f, -1024f),
            ["m_vecOrigin[2]"] = PropertyValue.FromFloat(64f),
            ["m_iTeamNum"] = PropertyValue.FromInt(2),
            ["m_lifeState"] = PropertyValue.FromInt(0),
        });

    private static PropertyValue Value(DecodedEntity entity, string name) =>
        entity.Properties
            .First(property => string.Equals(
                property.Definition.Property.Name, name, StringComparison.Ordinal))
            .Value;

    private static IEnumerable<Core.Net.INetMessage> Messages(DemoCommand command)
    {
        Core.Net.NetDecodeState state = new()
        {
            NetworkProtocol = SyntheticDemo.DefaultProtocol,
        };

        return Core.Net.NetMessageReader.Read(command.Payload.Span, state).Messages;
    }

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
