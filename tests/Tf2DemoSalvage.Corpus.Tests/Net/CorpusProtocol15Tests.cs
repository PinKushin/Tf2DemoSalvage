using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Core.Text;
using Tf2DemoSalvage.Probe.Probes;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// The corpus's three protocol-15 demos, named in full: build 3862's POV, and the two SourceTV
/// recordings from the builds after it that B440 is about.
/// </summary>
/// <remarks>
/// **Named rather than selected**, because a selection hid one of these for a month: every sweep
/// stopped at the CEVO demo, so the ESEA one behind it — failing the same way — was never reached
/// (<c>docs/memory/a-corpus-selection-outlives-its-corpus.md</c>). Each case here is its own test.
///
/// The two SourceTV demos live in the local corpus, which is not committed; without them the cases
/// skip, saying which file is missing. The POV is gcor and always runs.
///
/// **What these settle about the filed hypothesis.** B440 guessed that SourceTV recorded at this era
/// laid its packets out differently — an extra or missing prologue field, a different header on an
/// HLTV connection. The first signon's <c>svc_ServerInfo</c> starts at bit 0 of the payload the
/// container reader hands over, and restates the header's protocol and map: the container is right,
/// and nothing about it is SourceTV's. What differs is the build — six-bit type fields, and
/// <c>DPT_VectorXY</c> in the schema's numbering — which <c>Protocol15ConformanceTests</c> cites.
/// </remarks>
public sealed class CorpusProtocol15Tests
{
    /// <summary>CEVO's match server: SourceTV, named by <c>tv_autorecord</c> for 9 November 2010.</summary>
    private const string Cevo = "auto-20101109-2141-cp_badlands.dem";

    /// <summary>ESEA LAN 1618: SourceTV, <c>cp_snakewater_b9</c>.</summary>
    private const string Esea = "esea_match_2184869.dem";

    /// <summary>TF2 build 3862 of 4 June 2009, recorded on the period client: the control.</summary>
    private const string June2009Pov = "tf2-2009-build3862-pov-cp_badlands.dem";

    [TestCase(Cevo)]
    [TestCase(Esea)]
    public void FirstSignon_OfALaterProtocol15SourceTv_OpensWithServerInfoAtItsFirstBit(string name)
    {
        byte[] bytes = File.ReadAllBytes(Demo(name));
        DemoHeader header = DemoHeader.Parse(bytes);
        header.NetworkProtocol.ShouldBe(15);
        header.ClientName.ShouldBe("SourceTV Demo");

        DemoCommand first = DemoCommandReader.Read(bytes.AsMemory(DemoHeader.SizeBytes)).First();
        first.Type.ShouldBe(DemoCommandType.Signon);
        first.Prologue.Length.ShouldBe(76 + 8, "democmdinfo_t and two sequence numbers, as for a POV");

        NetMessageReadResult result = NetMessageReader.Read(
            first.Payload.Span, new NetDecodeState { NetworkProtocol = (ushort)header.NetworkProtocol });

        result.MessageStartBits[0].ShouldBe(0);
        ServerInfoMessage info = result.Messages[0].ShouldBeOfType<ServerInfoMessage>();
        info.NetworkProtocol.ShouldBe((ushort)header.NetworkProtocol);
        info.Map.ShouldBe(header.MapName);
        info.IsSourceTv.ShouldBeTrue();
    }

    [TestCase(June2009Pov, NetMessage.OldTypeBits)]
    [TestCase(Cevo, NetMessage.TypeBits)]
    [TestCase(Esea, NetMessage.TypeBits)]
    public void MessageTypeBits_OfEachProtocol15Demo_IsTheWidthItsBuildWrote(string name, int typeBits)
    {
        byte[] bytes = File.ReadAllBytes(Demo(name));
        NetDecodeState state = new() { NetworkProtocol = (ushort)DemoHeader.Parse(bytes).NetworkProtocol };

        DemoCommand first = DemoCommandReader.Read(bytes.AsMemory(DemoHeader.SizeBytes)).First();
        _ = NetMessageReader.Read(first.Payload.Span, state);

        state.NetworkProtocol.ShouldBe((ushort)15);
        state.MessageTypeBits.ShouldBe(typeBits);
    }

    /// <summary>
    /// The census's messages stage re-encodes every message of both later-build demos bit for bit.
    /// </summary>
    /// <remarks>
    /// Its write state never reads a packet, so the six-bit width the read state settled has to be
    /// carried to it; before that, every ServerInfo came back one bit short (B451). Counts measured
    /// 2026-10-01.
    /// </remarks>
    [TestCase(Cevo, 114_315)]
    [TestCase(Esea, 311_418)]
    public void DecodeCensusMessages_OfALaterProtocol15SourceTv_ReencodesEveryMessage(string name, long messages)
    {
        string path = Demo(name);
        string csv = Path.Combine(Path.GetTempPath(), "b451-" + Guid.NewGuid().ToString("N") + ".csv");

        try
        {
            new DecodeCensusProbe().Run(TextWriter.Null, [path, "--csv", csv, "--stages", "messages"]);

            CensusRow row = CensusCsv.Read(csv).Values.Single();
            row["messages_detail"].ShouldContain(" re-encoded bit for bit");
            row.Status("messages").ShouldBe("pass", row["messages_detail"]);
            row["messages"].ShouldBe(messages.ToString(System.Globalization.CultureInfo.InvariantCulture));
            row["messages_exact"].ShouldBe(row["messages"]);
        }
        finally
        {
            File.Delete(csv);
            File.Delete(Path.ChangeExtension(csv, ".md"));
        }
    }

    [TestCase(Cevo)]
    [TestCase(Esea)]
    public void Decode_ALaterProtocol15SourceTv_ReadsEveryPacketToTheEnd(string name)
    {
        // Every packet, not the first few: B440's first symptom was the signon, and its esea trace
        // then carried 106,415 "stopped after" lines — a stop anywhere is the same defect.
        byte[] bytes = File.ReadAllBytes(Demo(name));
        DemoHeader header = DemoHeader.Parse(bytes);
        NetDecodeState state = new() { NetworkProtocol = (ushort)header.NetworkProtocol };

        int packets = 0;
        List<string> stopped = [];

        foreach (DemoCommand command in DemoCommandReader.Read(bytes.AsMemory(DemoHeader.SizeBytes)))
        {
            if (command.Type is not (DemoCommandType.Signon or DemoCommandType.Packet))
            {
                continue;
            }

            packets++;
            if (NetMessageReader.Read(command.Payload.Span, state).StopReason is { } reason)
            {
                stopped.Add($"{command.Type} at tick {command.Tick}: {reason}");
            }
        }

        TestContext.Out.WriteLine($"{name}: {packets:N0} packets, {stopped.Count:N0} stopped");

        packets.ShouldBeGreaterThan(0);
        stopped.ShouldBeEmpty(
            $"{name}: {stopped.Count} of {packets} packets stopped; the first: " +
            string.Join(" | ", stopped.Take(3)));
    }

    [TestCase(Cevo)]
    [TestCase(Esea)]
    public void Schema_OfALaterProtocol15SourceTv_ReadsWithVectorXyAndAgreesWithServerInfo(string name)
    {
        // Two routes to one number: the class list at the end of dem_datatables, and max_classes in
        // svc_ServerInfo. B440's schema read one table and 26,207 classes.
        byte[] bytes = File.ReadAllBytes(Demo(name));
        DemoHeader header = DemoHeader.Parse(bytes);
        List<DemoCommand> commands = [.. DemoCommandReader.Read(bytes.AsMemory(DemoHeader.SizeBytes))];

        DemoSchema schema = SendTableParser.Parse(
            commands.First(command => command.Type == DemoCommandType.DataTables).Payload.Span,
            (ushort)header.NetworkProtocol);

        NetDecodeState state = new() { NetworkProtocol = (ushort)header.NetworkProtocol };
        _ = NetMessageReader.Read(commands[0].Payload.Span, state);
        ServerInfoMessage info = state.ServerInfo.ShouldNotBeNull();

        schema.ServerClasses.Count.ShouldBe((int)info.MaxClasses);
        schema.FindTable("DT_TFPlayer").ShouldNotBeNull();

        // The numbering's own evidence: a type only the later builds' numbering has.
        schema.Tables.SelectMany(table => table.Properties)
            .ShouldContain(property => property.Type == SendPropType.VectorXY);
    }

    [TestCase(Cevo, 39)]     // 39 + 120 = B444's 159
    [TestCase(Esea, 120)]
    public void UserMessage41_OfALaterProtocol15SourceTv_DecodesAsCheapBreakModelAndTracesByName(
        string name, int expected)
    {
        // B444: the later builds register CheapBreakModel at 41 (the 2010 client). Before the fix
        // build 3862's SPHapWeapEvent refused every one of these and the trace printed `#41`.
        byte[] bytes = File.ReadAllBytes(Demo(name));
        DemoHeader header = DemoHeader.Parse(bytes);
        List<DemoCommand> commands = [.. DemoCommandReader.Read(bytes.AsMemory(DemoHeader.SizeBytes))];
        NetDecodeState state = new() { NetworkProtocol = (ushort)header.NetworkProtocol };

        List<UserMessage> id41 = [];
        foreach (DemoCommand command in commands)
        {
            if (command.Type is DemoCommandType.Signon or DemoCommandType.Packet)
            {
                id41.AddRange(NetMessageReader.Read(command.Payload.Span, state)
                    .Messages.OfType<UserMessage>().Where(user => user.UserMessageType == 41));
            }
        }

        TestContext.Out.WriteLine($"{name}: {id41.Count} id-41 messages");
        id41.Count.ShouldBe(expected);
        id41.Count(user => user.Name != "CheapBreakModel" || user.Fields == null)
            .ShouldBe(0, $"of {id41.Count} id-41 messages in {name}");

        StringWriter trace = new() { NewLine = "\n" };
        DemoTraceWriter.Write(trace, name, header, commands);
        trace.ToString().ShouldNotContain("svc_usermessage #41 ");
    }

    /// <summary>The demo with exactly this file name, or skips saying it is absent.</summary>
    /// <remarks>
    /// From <see cref="Corpus.Files"/>, not <see cref="Corpus.Demo"/>: that one selects among demos
    /// whose schema parses, which is what these two did not do, so it reported them absent.
    /// </remarks>
    private static string Demo(string name)
    {
        string? path = Corpus.Files()
            .FirstOrDefault(file => string.Equals(Path.GetFileName(file), name, StringComparison.Ordinal));

        if (path is null)
        {
            Assert.Ignore(
                $"{name} is not present. The protocol-15 SourceTV demos live in the local corpus, " +
                "which is not committed; unset TF2DEMOSALVAGE_GCOR_ONLY and add them to run this.");
        }

        return path;
    }
}
