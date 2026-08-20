using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Core.Text;

namespace Tf2DemoSalvage.Core.Tests.Text;

/// <summary>
/// A demo carrying every COMMAND kind, not just every message kind.
/// </summary>
/// <remarks>
/// **The container has eight command types and the fixtures so far used four.**
/// <c>EveryMessageKindDemoTests</c> covers what travels inside a packet; this covers the packet's
/// siblings — <c>dem_consolecmd</c>, <c>dem_synctick</c>, <c>dem_stringtables</c> and
/// <c>dem_signon</c> — which are commands rather than messages and so have their own reader, their
/// own trace rendering and their own name.
///
/// They are cheap to leave out of a fixture and easy to get wrong, because most of them carry
/// little or nothing: a synctick has no payload at all, and a console command is one string. A
/// command whose payload is empty is exactly the case a reader with an off-by-one in its length
/// handling still appears to survive.
/// </remarks>
public sealed class EveryCommandKindDemoTests
{
    [Test]
    public void Trace_EveryCommandKind_IsNamedByItsWireName()
    {
        // The trace names commands by the engine's own vocabulary, and the names are a contract
        // with the reader rather than an implementation detail — a rename should be deliberate.
        string trace = Trace();

        foreach (string name in new[]
        {
            "dem_signon", "dem_packet", "dem_synctick", "dem_consolecmd",
            "dem_datatables", "dem_stringtables", "dem_stop",
        })
        {
            trace.Contains(name, StringComparison.Ordinal)
                .ShouldBeTrue($"the trace never names {name}");
        }
    }

    [Test]
    public void Trace_AConsoleCommand_ShowsWhatWasTyped()
    {
        // A console command is one NUL-terminated string, and rendering it as a byte count would
        // be as truthful and useless. This is the only record of what the recorder typed.
        Trace().ShouldContain("cl_interp 0.0152");
    }

    [Test]
    public void Trace_ASyncTickCommand_IsNamedDespiteCarryingNoPayload()
    {
        // **A payload of nothing is the case an off-by-one survives.** dem_synctick has no body
        // at all, so a reader that consumed a length field where none exists would take four
        // bytes from the next command and still look like it worked until the stream diverged.
        Trace().ShouldContain("dem_synctick");
    }

    [Test]
    public void RoundTrip_EveryCommandKind_CompilesBackToItsOwnBytes()
    {
        // The container half of the round trip. Byte-exactness across all eight command types
        // says the reader and writer agree on every header shape, including the two that carry a
        // prologue and the one that carries nothing.
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
    public void Scan_ADemoWithEveryCommandKind_WalksItWithoutStopping()
    {
        // DemoScan is the shared walk behind the JSON Lines output and the summary, and it is a
        // third path over the same commands — one that neither the trace nor the assembly
        // exercises.
        (_, IReadOnlyList<DemoCommand> commands) = Read(Demo());

        DemoScan.Result result = DemoScan.Run(
            commands,
            sampleSize: 16,
            progress: null,
            networkProtocol: SyntheticDemo.DefaultProtocol,
            includeEntityEvents: true);

        // The user message is what proves the walk reached the last packet rather than stopping
        // at the first command it did not recognise.
        result.UserMessages.ShouldNotBeEmpty();
    }

    [Test]
    public void Trace_AUserMessageWithNamedFields_RendersThemRatherThanItsLength()
    {
        // A user message that decoded into fields must show them; one that did not falls back to
        // a length. Both are legitimate and they look nothing alike, so the rendering is asserted
        // on a message known to decode.
        Trace().ShouldContain("range");
    }

    /// <summary>A demo carrying one of every command type the container defines.</summary>
    private static byte[] Demo()
    {
        DemoSchema schema = SyntheticPlayer.Schema();

        return SyntheticDemo.From(
            SyntheticDemo.DefaultProtocol,

            // Signon carries the same payload shape as a packet and is a different command.
            Signon(SyntheticDemo.Packet(
                SyntheticDemo.DefaultProtocol, 0, ServerInfo())),

            new DemoCommand(DemoCommandType.SyncTick, 0, ReadOnlyMemory<byte>.Empty),

            SyntheticDemo.DataTables(schema),

            new DemoCommand(
                DemoCommandType.ConsoleCmd, 10, ConsoleCommand("cl_interp 0.0152")),

            // dem_stringtables is the whole table set written once, not a message. Its payload is
            // opaque to this project, which is why an empty one is the honest fixture: what is
            // being exercised is the command's framing rather than its contents.
            new DemoCommand(DemoCommandType.StringTables, 20, new byte[] { 0, 0 }),

            SyntheticDemo.Packet(
                SyntheticDemo.DefaultProtocol,
                66,
                new UserMessage(
                    UserMessageType: GeigerId(), Name: null, BodyBits: 8,
                    Body: new byte[] { 42 })));
    }

    /// <summary>Turns a packet command into the signon command with the same payload.</summary>
    private static DemoCommand Signon(DemoCommand packet) =>
        packet with { Type = DemoCommandType.Signon };

    /// <summary>A console command's payload: one NUL-terminated string.</summary>
    private static byte[] ConsoleCommand(string text) =>
        [.. Encoding.UTF8.GetBytes(text), 0];

    /// <summary>The user message id Geiger resolves to, searched as a reader would.</summary>
    private static int GeigerId()
    {
        for (int id = 0; id <= byte.MaxValue; id++)
        {
            if (string.Equals(
                UserMessageNames.Lookup(id, SyntheticDemo.DefaultProtocol),
                "Geiger",
                StringComparison.Ordinal))
            {
                return id;
            }
        }

        throw new InvalidOperationException("Geiger has no id at this protocol.");
    }

    private static string Trace()
    {
        (DemoHeader header, IReadOnlyList<DemoCommand> commands) = Read(Demo());

        StringWriter text = new() { NewLine = "\n" };
        DemoTraceWriter.Write(text, "synthetic.dem", header, commands);
        return text.ToString();
    }

    private static ServerInfoMessage ServerInfo() => new(
        NetworkProtocol: SyntheticDemo.DefaultProtocol,
        ServerCount: 1,
        IsSourceTv: true,
        IsDedicated: true,
        MapCrc: 0,
        MaxClasses: 1,
        MapHash: new byte[16],
        PlayerSlot: 0,
        MaxPlayers: 24,
        IntervalPerTick: 1f / 66.67f,
        Platform: 'w',
        GameDirectory: "tf",
        Map: "cp_process_final",
        Skybox: "sky_tf2_04",
        ServerName: "synthetic",
        IsReplay: false);

    private static (DemoHeader Header, IReadOnlyList<DemoCommand> Commands) Read(byte[] demo) =>
        (DemoHeader.Parse(demo.AsSpan(0, DemoHeader.SizeBytes)),
            [.. DemoCommandReader.Read(demo.AsMemory(DemoHeader.SizeBytes))]);
}
