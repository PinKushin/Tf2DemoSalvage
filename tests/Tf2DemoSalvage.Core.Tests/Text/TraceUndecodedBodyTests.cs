using System;
using System.Collections.Generic;
using System.IO;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Core.Text;

namespace Tf2DemoSalvage.Core.Tests.Text;

/// <summary>
/// A body that will not decode, and what the trace does about it.
/// </summary>
/// <remarks>
/// **This project's position is that decoding must be total** — a demo the engine plays is a demo
/// this reader should read at 100% with no errors, so a body that fails to decode is our defect
/// rather than the file's (<c>docs/memory/decode-must-be-total.md</c>). That makes these paths
/// unreachable from any real demo, and it is exactly why they need testing: they are what the
/// trace does on the day the position turns out to be wrong.
///
/// Two properties, and the second is the one that matters:
///
/// - the failure is **named in place**, so "this demo has no effects" and "the effects would not
///   decode" do not look identical in the output;
/// - the walk **continues**, so one bad body costs its own line rather than the rest of the file.
///
/// A trace that threw would lose everything after the failure, which on a corrupt demo is the
/// worst possible outcome: the part that still decodes is the part that would say what happened.
///
/// The corruption is a **stated count that the body cannot support** rather than random bytes,
/// because random bytes usually decode into something. A snapshot claiming five entities with one
/// entity's worth of bits runs off the end deterministically.
/// </remarks>
public sealed class TraceUndecodedBodyTests
{
    [Test]
    public void Trace_ASnapshotClaimingMoreEntitiesThanItsBodyHolds_ReportsItAsUndecoded()
    {
        string trace = Trace(BrokenSnapshotDemo());

        trace.ShouldContain("svc_packetentities");
        trace.ShouldContain("undecoded");
    }

    [Test]
    public void Trace_AFailedSnapshot_StillWalksTheRestOfTheDemo()
    {
        // **The property that makes the failure survivable.** A message after the broken snapshot
        // must still appear, which is what says the walk resumed rather than unwound.
        Trace(BrokenSnapshotDemo()).ShouldContain("dem_stop");
    }

    [Test]
    public void Trace_ASoundListClaimingMoreSoundsThanItsBodyHolds_ReportsItAsUndecoded()
    {
        // Sounds are decoded by a separate codec with its own catch, so the two failures are two
        // code paths rather than one shared handler.
        string trace = Trace(BrokenSoundDemo());

        trace.ShouldContain("svc_sounds");
        trace.ShouldContain("undecoded");
    }

    [Test]
    public void Scan_ADemoWithABrokenBody_CompletesRatherThanThrowing()
    {
        // DemoScan is the walk behind the summary and the JSON Lines output, and it has its own
        // handling. A tool that crashed on a damaged demo would be useless on exactly the file
        // someone most wants explained.
        (_, IReadOnlyList<DemoCommand> commands) = Read(BrokenSnapshotDemo());

        DemoScan.Result result = DemoScan.Run(
            commands,
            sampleSize: 8,
            progress: null,
            networkProtocol: SyntheticDemo.DefaultProtocol,
            includeEntityEvents: true);

        result.ShouldNotBeNull();
    }

    [Test]
    public void Trace_TheSameDemoUncorrupted_ReportsNoFailure()
    {
        // **The control.** Every assertion above looks for the word "undecoded", and a trace that
        // printed it unconditionally — or a decoder that refused every body — would satisfy all of
        // them. The uncorrupted demo differs only in the stated count.
        string trace = Trace(SnapshotDemo(claimed: 1));

        trace.ShouldContain("svc_packetentities");
        trace.ShouldNotContain("undecoded");
    }

    /// <summary>A demo whose snapshot claims five entities and encodes one.</summary>
    private static byte[] BrokenSnapshotDemo() => SnapshotDemo(claimed: 5);

    /// <summary>A demo with one entity, whose snapshot claims <paramref name="claimed"/>.</summary>
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
                    Body: body)),
            new DemoCommand(DemoCommandType.Stop, 67, ReadOnlyMemory<byte>.Empty));
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

    private static string Trace(byte[] demo)
    {
        (DemoHeader header, IReadOnlyList<DemoCommand> commands) = Read(demo);

        StringWriter text = new() { NewLine = "\n" };
        DemoTraceWriter.Write(
            text,
            "synthetic.dem",
            header,
            commands,
            options: new DemoTraceOptions { IncludeEntities = true });

        return text.ToString();
    }

    private static (DemoHeader Header, IReadOnlyList<DemoCommand> Commands) Read(byte[] demo) =>
        (DemoHeader.Parse(demo.AsSpan(0, DemoHeader.SizeBytes)),
            [.. DemoCommandReader.Read(demo.AsMemory(DemoHeader.SizeBytes))]);
}
