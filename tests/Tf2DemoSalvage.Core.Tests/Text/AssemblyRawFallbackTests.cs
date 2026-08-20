using System;
using System.Collections.Generic;
using System.IO;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Core.Text;

namespace Tf2DemoSalvage.Core.Tests.Text;

/// <summary>
/// A body the assembler cannot expand, and the bits it keeps instead.
/// </summary>
/// <remarks>
/// **The assembly form has two jobs and only one of them is readability.** It must compile back to
/// the demo it came from, byte for byte — so a message this project cannot express in text is not
/// an error, it is a message that keeps its raw bits and reads as a hex blob. Losing the
/// readability of one snapshot is a cost; losing the bits is a corrupted demo.
///
/// That fallback is unreachable from a real recording, because a real recording decodes. It is
/// reachable the moment one does not, and this is the property that decides whether the tool is
/// still usable then: **a demo this project cannot fully understand must still survive a round
/// trip.** That is the salvage claim in the project's name, stated as a test.
///
/// The failures are stated counts a body cannot support rather than random bytes, because random
/// bytes usually decode into something.
/// </remarks>
public sealed class AssemblyRawFallbackTests
{
    [Test]
    public void RoundTrip_ASnapshotThatWillNotDecode_KeepsItsBitsExactly()
    {
        // The largest of the three and the one that matters most: a snapshot is most of a demo's
        // bits, so a fallback that dropped it would produce a file a fraction of the size.
        byte[] demo = BrokenSnapshotDemo();

        RoundTrip(demo).ShouldBe(demo);
    }

    [Test]
    public void Assemble_ASnapshotThatWillNotDecode_IsNotExpandedIntoEntities()
    {
        // **The observable that says the fallback happened**, rather than the round trip passing
        // for some other reason. An expanded snapshot opens a block and lists entities; a raw one
        // is a single line of hex.
        Assemble(BrokenSnapshotDemo()).ShouldNotContain("entity 1 ENTER");
    }

    [Test]
    public void RoundTrip_TempEntitiesThatWillNotDecode_KeepTheirBitsExactly()
    {
        // Temp entities have their own decoder and their own catch, so the fallback needs stating
        // separately or one of the two drifts.
        byte[] demo = BrokenEffectDemo();

        RoundTrip(demo).ShouldBe(demo);
    }

    [Test]
    public void RoundTrip_ASoundListThatWillNotDecode_KeepsItsBitsExactly()
    {
        byte[] demo = BrokenSoundDemo();

        RoundTrip(demo).ShouldBe(demo);
    }

    [Test]
    public void Assemble_TheSameMessagesUncorrupted_AreExpanded()
    {
        // **The control, and it is what makes the three above mean anything.** An assembler that
        // wrote every message as raw bits would round-trip all of them perfectly while producing
        // a text form nobody could read — which is the failure this fallback is one step away
        // from, and it would never show up in a byte comparison.
        Assemble(SnapshotDemo(claimed: 1)).ShouldContain("entity 1 ENTER");
    }

    /// <summary>A demo whose snapshot claims five entities and encodes one.</summary>
    private static byte[] BrokenSnapshotDemo() => SnapshotDemo(claimed: 5);

    private static byte[] SnapshotDemo(int claimed)
    {
        DemoSchema schema = SyntheticPlayer.Schema();
        EntityDecoder decoder = new(
            schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));

        IReadOnlyList<FlatProperty> flat = decoder.FlattenedFor(SyntheticPlayer.PlayerClassId);
        int index = IndexOf(flat, "m_lifeState");

        DecodedEntity entity = new(
            EntityIndex: 1,
            ClassId: SyntheticPlayer.PlayerClassId,
            SerialNumber: 1,
            EntityUpdateType.Enter,
            [new DecodedProperty(index, flat[index], PropertyValue.FromInt(1))]);

        byte[] body = decoder.EncodeEntities([entity], [], isDelta: false, 0, out int bits);

        return SyntheticDemo.From(
            SyntheticDemo.DefaultProtocol,
            SyntheticDemo.DataTables(schema),
            SyntheticDemo.Packet(
                SyntheticDemo.DefaultProtocol,
                66,
                new PacketEntitiesMessage(
                    MaxEntries: 64,
                    IsDelta: false,
                    DeltaFromTick: null,
                    BaselineIndex: false,
                    UpdatedEntries: claimed,
                    LengthBits: bits,
                    UpdateBaseline: false,
                    Body: body)));
    }

    /// <summary>A demo whose temp entities claim more effects than the body holds.</summary>
    private static byte[] BrokenEffectDemo()
    {
        DemoSchema schema = SyntheticPlayer.SchemaWithProp();
        EntityDecoder decoder = new(
            schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));

        byte[] body = decoder.EncodeTempEntities(
            [new DecodedTempEntity(SyntheticPlayer.PropClassId, 0f, [])],
            reliable: false,
            lengthBits: 0);

        return SyntheticDemo.From(
            SyntheticDemo.DefaultProtocol,
            SyntheticDemo.DataTables(schema),
            SyntheticDemo.Packet(
                SyntheticDemo.DefaultProtocol,
                66,
                new TempEntitiesMessage(
                    Count: 8, BodyBits: body.Length * 8, Body: body)));
    }

    /// <summary>A demo whose sound list claims four sounds and encodes one.</summary>
    private static byte[] BrokenSoundDemo()
    {
        (byte[] body, int bits) = SoundEncoder.Encode(
            [
                new DecodedSound(
                    EntityIndex: 5, SoundNumber: 3, Flags: 0, Channel: 6,
                    IsAmbient: false, IsSentence: false, SequenceNumber: 0, Volume: 1f,
                    SoundLevel: 75, Pitch: 100, DelaySeconds: 0f,
                    OriginX: 0f, OriginY: 0f, OriginZ: 0f, SpeakerEntity: -1,
                    SpecialDsp: 0,
                    Sent: SoundFields.Entity | SoundFields.SoundNumber),
            ],
            SyntheticDemo.DefaultProtocol);

        return SyntheticDemo.Containing(
            SyntheticDemo.DefaultProtocol,
            new SoundsMessage(IsReliable: false, Count: 4, BodyBits: bits, Body: body));
    }

    private static int IndexOf(IReadOnlyList<FlatProperty> flat, string name)
    {
        for (int index = 0; index < flat.Count; index++)
        {
            if (string.Equals(flat[index].Property.Name, name, StringComparison.Ordinal))
            {
                return index;
            }
        }

        throw new InvalidOperationException($"The schema has no '{name}'.");
    }

    private static byte[] RoundTrip(byte[] demo)
    {
        using StringReader reader = new(Assemble(demo));
        (DemoHeader header, IReadOnlyList<DemoCommand> commands) = DemoAssembly.Parse(reader);

        return DemoWriter.Write(header, commands);
    }

    private static string Assemble(byte[] demo)
    {
        DemoHeader header = DemoHeader.Parse(demo.AsSpan(0, DemoHeader.SizeBytes));
        List<DemoCommand> commands =
            [.. DemoCommandReader.Read(demo.AsMemory(DemoHeader.SizeBytes))];

        StringWriter text = new() { NewLine = "\n" };
        DemoAssembly.Write(text, header, commands);
        return text.ToString();
    }
}
