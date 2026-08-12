using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Primitives;
using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Core.Text;
using Tf2DemoSalvage.Core.Tests.Net;

namespace Tf2DemoSalvage.Core.Tests.Text;

/// <summary>
/// Round-trips entity snapshots through their assembly text, synthetically.
/// </summary>
/// <remarks>
/// **These paths were reachable only from the corpus suite, which cannot be mutation tested.**
/// Stryker's coverage capture is cancelled by a 180-second RPC limit against that project
/// (<c>RISKS.md</c> B34), so 142 of this file's 244 mutants had no coverage at all — the code was
/// exercised in CI and measured by nothing.
///
/// Synthetic rather than corpus-based on purpose, and not only for speed. A real demo exercises
/// whichever paths its ten recordings happen to take; a built schema can pose the cases the format
/// allows but the corpus never contains — a snapshot that removes entities, one whose slack bits
/// are non-zero, a property list that is empty.
///
/// The assertion is the encoded BITS, not the rendered text. Text-to-text would pass whenever
/// both directions shared a misunderstanding, which is exactly the failure a round trip is
/// supposed to catch.
/// </remarks>
public sealed class EntityAssemblyTests
{
    private const int Unsigned = 1 << 0;
    private const int ClassBits = 2;

    /// <summary>A small flat schema: two classes over one table of fixed-width ints.</summary>
    private static DemoSchema Schema()
    {
        List<SendProperty> properties =
        [
            new(SendPropType.Int, "m_iHealth", Unsigned, string.Empty, 0f, 0f, 10, 0),
            new(SendPropType.Int, "m_iTeamNum", Unsigned, string.Empty, 0f, 0f, 3, 0),
            new(SendPropType.Int, "m_iAmmo", Unsigned, string.Empty, 0f, 0f, 8, 0),
        ];

        return new DemoSchema(
            [new SendTable("DT_Test", true, properties)],
            [new ServerClass(0, "CTest", "DT_Test"), new ServerClass(1, "COther", "DT_Test")]);
    }

    private static EntityDecoder Decoder() => new(Schema(), ClassBits);

    private static PacketEntitiesMessage Header(int updated, int lengthBits, byte[] payload) =>
        new(2048, false, null, false, updated, lengthBits, false, payload);

    /// <summary>One entity entering with all three properties set.</summary>
    private static (byte[] Payload, int Bits) OneEnteringEntity()
    {
        BitWriter writer = new();
        writer.UBitVar(3);                                  // entity index delta
        writer.Write((uint)EntityUpdateType.Enter, 2);
        writer.Write(1, ClassBits);                         // class id
        writer.Write(0, 10);                                // serial number bits
        writer.Write(1, 1).UBitVar(0).Write(250, 10);       // m_iHealth
        writer.Write(1, 1).UBitVar(0).Write(2, 3);          // m_iTeamNum
        writer.Write(1, 1).UBitVar(0).Write(33, 8);         // m_iAmmo
        writer.Write(0, 1);                                 // no more properties
        writer.Write(0, 1);                                 // no removed entities

        return (writer.Build(), writer.BitCount);
    }

    /// <summary>Renders a snapshot, then reads the text back into a snapshot.</summary>
    private static PacketEntitiesMessage RoundTrip(byte[] payload, int bits, int updated)
    {
        PacketEntitiesMessage original = Header(updated, bits, payload);

        IReadOnlyList<string>? lines = EntityAssembly.Write(original, Decoder());
        lines.ShouldNotBeNull("the snapshot did not render, so nothing was round-tripped");

        return Reassemble(lines);
    }

    /// <summary>Feeds rendered lines back through <see cref="EntityAssembly.Build"/>.</summary>
    private static PacketEntitiesMessage Reassemble(IReadOnlyList<string> lines)
    {
        List<string> head = [.. lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries)];
        int next = 1;

        return EntityAssembly.Build(head, () => next < lines.Count ? lines[next++] : null, Decoder());
    }

    [Fact]
    public void AnEnteringEntity_RoundTripsToTheSameBits()
    {
        // The whole claim in one assertion: text carrying named properties is enough to rebuild
        // the exact bits the snapshot was decoded from.
        (byte[] payload, int bits) = OneEnteringEntity();

        PacketEntitiesMessage rebuilt = RoundTrip(payload, bits, updated: 1);

        rebuilt.LengthBits.ShouldBe(bits);
        rebuilt.Body.ToArray().ShouldBe(payload);
    }

    [Fact]
    public void TheRenderedTextNamesTheProperties()
    {
        // A viewer's reason for this format existing. Asserted on the exact values, not on
        // "contains something": a renderer that emitted every property with the same value would
        // satisfy a presence check.
        (byte[] payload, int bits) = OneEnteringEntity();

        IReadOnlyList<string>? lines = EntityAssembly.Write(Header(1, bits, payload), Decoder());

        lines.ShouldNotBeNull();
        string[] props = [.. lines.Where(l => l.TrimStart().StartsWith("prop ", StringComparison.Ordinal))];

        // Whole lines, not fragments. The shape (`0/4/0`) and the type letter (`i`) are what a
        // reader on the other side needs to rebuild the property, so a test that ignored them
        // would pass while the format lost the parts that make it reversible.
        props.Length.ShouldBe(3);
        props[0].Trim().ShouldBe("prop 0/4/0 DT_Test.m_iHealth i 250");
        props[1].Trim().ShouldBe("prop 1/4/0 DT_Test.m_iTeamNum i 2");
        props[2].Trim().ShouldBe("prop 2/4/0 DT_Test.m_iAmmo i 33");
    }

    [Fact]
    public void ASnapshotThatCannotBeDecoded_RendersAsNullRatherThanThrowing()
    {
        // Deliberately truncated: the class id runs off the end. Returning null is what lets the
        // trace fall back to hex for one message instead of abandoning the demo, which is the
        // behaviour this project exists to have.
        BitWriter writer = new();
        writer.UBitVar(3);
        writer.Write((uint)EntityUpdateType.Enter, 2);

        byte[] payload = writer.Build();

        EntityAssembly.Write(Header(1, writer.BitCount, payload), Decoder()).ShouldBeNull();
    }

    [Fact]
    public void Build_RefusesTextWhoseEntityBlockIsNeverClosed()
    {
        // A truncated file must fail as bad input rather than reading past the end of the list.
        List<string> lines =
        [
            "svc_packetentities delta=0 from=- max=2048 baseline=0 updatebaseline=0 updated=1 bits=64 {",
            "  entity 3 ENTER class=1 serial=0 ibits=0 {",
        ];

        Should.Throw<InvalidDataException>(() => Reassemble(lines));
    }


    [Fact]
    public void LeaveAndDelete_SurviveAsDistinctUpdateTypes()
    {
        // Leave and Delete are different events - one stops updating an entity, the other removes
        // it - and the text has to keep them apart. Collapsing them would be invisible in a
        // property-value check and wrong for any viewer tracking who is still in the world.
        BitWriter writer = new();
        writer.UBitVar(3);
        writer.Write((uint)EntityUpdateType.Leave, 2);
        writer.UBitVar(0);
        writer.Write((uint)EntityUpdateType.Delete, 2);
        writer.Write(0, 1);

        byte[] payload = writer.Build();
        PacketEntitiesMessage message =
            new(2048, true, 100, false, 2, writer.BitCount, false, payload);

        IReadOnlyList<string>? lines = EntityAssembly.Write(message, Decoder());

        lines.ShouldNotBeNull();
        string[] entities =
            [.. lines.Where(l => l.TrimStart().StartsWith("entity ", StringComparison.Ordinal))];

        entities.Length.ShouldBe(2);
        entities[0].ShouldContain("LEAVE");
        entities[1].ShouldContain("DELETE");
    }

    [Fact]
    public void ADeltaSnapshot_RoundTripsToTheSameBits()
    {
        // A delta carries no class id - the decoder supplies it from when the entity entered - so
        // this exercises a different path through both directions than the entering case.
        EntityDecoder decoder = Decoder();

        BitWriter enter = new();
        enter.UBitVar(3);
        enter.Write((uint)EntityUpdateType.Enter, 2);
        enter.Write(1, ClassBits);
        enter.Write(0, 10);
        enter.Write(1, 1).UBitVar(0).Write(7, 10);
        enter.Write(0, 1);
        enter.Write(0, 1);
        decoder.Decode(enter.Build(), Header(1, enter.BitCount, enter.Build()), enter.BitCount);

        BitWriter update = new();
        update.UBitVar(3);
        update.Write((uint)EntityUpdateType.Delta, 2);
        update.Write(1, 1).UBitVar(0).Write(66, 10);
        update.Write(0, 1);
        update.Write(0, 1);

        byte[] payload = update.Build();
        PacketEntitiesMessage message =
            new(2048, true, 100, false, 1, update.BitCount, false, payload);

        IReadOnlyList<string>? lines = EntityAssembly.Write(message, decoder);
        lines.ShouldNotBeNull("the delta did not render");

        // A second decoder, primed the same way, so Build starts from the same knowledge the
        // writer had. Rebuilding a delta needs the class the enter established.
        EntityDecoder rebuilder = Decoder();
        rebuilder.Decode(enter.Build(), Header(1, enter.BitCount, enter.Build()), enter.BitCount);

        List<string> head = [.. lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries)];
        int next = 1;
        PacketEntitiesMessage rebuilt = EntityAssembly.Build(
            head, () => next < lines.Count ? lines[next++] : null, rebuilder);

        rebuilt.Body.ToArray().ShouldBe(payload);
    }

    [Fact]
    public void Write_RejectsANullMessageOrDecoder()
    {
        Should.Throw<ArgumentNullException>(() => EntityAssembly.Write(null!, Decoder()));
        Should.Throw<ArgumentNullException>(
            () => EntityAssembly.Write(Header(0, 0, []), null!));
    }
}
