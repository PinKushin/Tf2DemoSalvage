using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Tests.Net;
using Tf2DemoSalvage.Core.Text;

namespace Tf2DemoSalvage.Core.Tests.Text;

/// <summary>
/// Tests for the Quake-style trace: the demo decompiled to text, message by message.
/// </summary>
/// <remarks>
/// Modelled on <c>lmpc</c>, the Quake tool that decompiles a <c>.dem</c> to text and compiles it
/// back. Its format is block-structured — a block per demo frame, holding the messages that
/// frame carried, each a keyword followed by fields and a semicolon.
///
/// The distinction from the summary dump is the point. A summary tells you what a demo contains;
/// a trace tells you what it *is*, in order, so a reader can follow the stream and see exactly
/// where something went wrong. Aggregates hide position, and position is what matters when a
/// demo is damaged.
/// </remarks>
public sealed class DemoTraceWriterTests
{
    private static DemoHeader Header() => new()
    {
        DemoProtocol = 3,
        NetworkProtocol = 24,
        ServerName = "serveme.tf",
        ClientName = "SourceTV Demo",
        MapName = "cp_process_final",
        GameDirectory = "tf",
        PlaybackTimeSeconds = 10f,
        PlaybackTicks = 100,
        PlaybackFrames = 2,
        SignonLengthBytes = 0,
    };

    private static string Trace(IReadOnlyList<DemoCommand> commands)
    {
        StringWriter writer = new() { NewLine = "\n" };
        DemoTraceWriter.Write(writer, "sample.dem", Header(), commands, null);
        return writer.ToString();
    }

    private static byte[] TickPacket(uint tick)
    {
        BitWriter writer = new();
        writer.NetTick(tick, 0, 0);
        return writer.Build();
    }

    [Fact]
    public void EachCommand_BecomesABlockInStreamOrder()
    {
        string trace = Trace(
        [
            new(DemoCommandType.Packet, 1, TickPacket(11)),
            new(DemoCommandType.Packet, 2, TickPacket(22)),
        ]);

        string[] blocks =
        [
            .. trace.Split('\n').Where(l => l.StartsWith("block", StringComparison.Ordinal)),
        ];

        blocks.Length.ShouldBe(2);
        blocks[0].ShouldContain("tick 1");
        blocks[1].ShouldContain("tick 2");
    }

    [Fact]
    public void EachMessage_IsAKeywordWithFieldsEndingInASemicolon()
    {
        // The lmpc shape: a keyword, its fields, a terminator. Machine-readable enough to
        // recompile, human-readable enough to scan.
        string trace = Trace([new(DemoCommandType.Packet, 1, TickPacket(4242))]);

        trace.ShouldContain("net_tick tick 4242");
        trace.ShouldContain(";");
    }

    [Fact]
    public void Blocks_AreBraceDelimited()
    {
        string trace = Trace([new(DemoCommandType.Packet, 1, TickPacket(1))]);

        trace.Count(c => c == '{').ShouldBe(trace.Count(c => c == '}'));
        trace.ShouldContain("{");
    }

    [Fact]
    public void NonPacketCommands_AppearToo_SoTheStreamIsComplete()
    {
        // A trace that silently dropped dem_synctick or dem_stop would not describe the file.
        // Position and completeness are the whole point of a trace over a summary.
        string trace = Trace(
        [
            new(DemoCommandType.SyncTick, 0, ReadOnlyMemory<byte>.Empty),
            new(DemoCommandType.Packet, 1, TickPacket(1)),
            new(DemoCommandType.Stop, 2, ReadOnlyMemory<byte>.Empty),
        ]);

        trace.ShouldContain("dem_synctick");
        trace.ShouldContain("dem_stop");
    }

    [Fact]
    public void UndecodableTail_IsReportedInPlace_NotOmitted()
    {
        // A packet the reader cannot finish is exactly what this format exists to show. Saying
        // so at the point it happened is the difference between a trace and a summary.
        byte[] garbage = [0xFF, 0xFF, 0xFF, 0xFF];
        string trace = Trace([new(DemoCommandType.Packet, 1, garbage)]);

        trace.ShouldContain("stopped");
    }

    [Fact]
    public void Header_IsWrittenBeforeAnyBlock()
    {
        string trace = Trace([new(DemoCommandType.Packet, 1, TickPacket(1))]);

        trace.IndexOf("cp_process_final", StringComparison.Ordinal)
            .ShouldBeLessThan(trace.IndexOf("block", StringComparison.Ordinal));
    }

    [Fact]
    public void Output_IsDeterministicAndLineFeedOnly()
    {
        IReadOnlyList<DemoCommand> commands = [new(DemoCommandType.Packet, 1, TickPacket(7))];

        Trace(commands).ShouldBe(Trace(commands));
        Trace(commands).ShouldNotContain("\r");
    }
}
