using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Tests.Net;
using Tf2DemoSalvage.Core.Text;
using Tf2DemoSalvage.Core.Primitives;

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

    [Fact]
    public void GameEventPlayerFields_ResolveToNames()
    {
        // The trace printed `player_hurt userid 18 ... attacker 18` while the summary of the same
        // demo printed `userid=cutemobb(18) ... attacker=gummo(17)`. Two outputs of one file
        // disagreeing about who a kill belongs to, with the less readable one being the deliverable
        // a person reads.
        //
        // The id stays alongside the name for the same reason the class id does: it is what makes
        // the line checkable against another parser or a raw dump.
        Trace(DemoFixtures.EventNamingAPlayer()).ShouldContain("userid Sassy(7)");

        // The control, and it is the whole reason the rule is an allowlist rather than "resolve
        // small integers". The same value 7 in a field that does NOT name a player must stay a
        // number: the summary once resolved everything numeric and produced
        // `damageamount=Ardaddy Ultrasex(14)` on a real demo, because 14 damage collided with
        // user id 14.
        string control = Trace(DemoFixtures.EventNamingAPlayer(fieldName: "damageamount"));

        control.ShouldContain("damageamount 7");
        control.ShouldNotContain("Sassy");
    }

    [Fact]
    public void Entities_AreNamedByTheirClass_NotByItsNumber()
    {
        // The trace is the deliverable a person reads, so it should not be the least readable
        // output this project produces. It printed `class 212` while the JSON Lines writer - the
        // machine format - printed "CTFPlayer", which is backwards: the schema already carries
        // the name and only the text dump was throwing it away.
        //
        // The number stays alongside it. A reader comparing against another parser's output, or
        // against a raw bit dump, needs the id, and a name alone would make that impossible.
        StringWriter writer = new() { NewLine = "\n" };
        DemoTraceWriter.Write(
            writer, "sample.dem", Header(), DemoFixtures.EntityLifecycle(), null,
            new DemoTraceOptions { IncludeEntities = true });
        string trace = writer.ToString();

        // Asserted through to the end of the line, not just up to the class name. The first
        // version stopped after `COther(1)` and passed against output that read
        // `class CWorld(277)props 0;` - the substitution had eaten the following space, and an
        // assertion that stops at the interesting token cannot see what it collided with.
        trace.ShouldContain(
            $"entity {DemoFixtures.EnteringEntity} ENTER class " +
            $"{DemoFixtures.EnteringClassName}({DemoFixtures.EnteringClassId}) props 0;");

        // Leave and Delete name their class too, from the id remembered when the entity entered.
        trace.ShouldContain("entity 1 LEAVE class ");
        trace.ShouldContain("entity 2 DELETE class ");
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
    public void LocalField_IsNamedInTheTraceRatherThanRenderedAsNothing()
    {
        // A `local` field is declared by the server and deliberately not transmitted, so it has
        // no value to print. Converting its null to a string produced an empty one, which came
        // out as `hidden ` - indistinguishable from a field that carried an empty string, and
        // with a trailing space that makes the line ambiguous to anything reading it back.
        //
        // The control is the field beside it: a real value must still print as itself, or this
        // test would pass equally on a writer that printed `local` for everything.
        BitWriter packet = new();
        GameEventFixtures.AppendList(
            packet,
            (11, "arena_win_panel",
            [
                (GameEventValueType.Short, "winning_team"),
                (GameEventValueType.Local, "player_1"),
            ]));

        GameEventFixtures.AppendEvent(
            packet, new BitWriter().Write(11, 9).Write(3, 16));

        string trace = Trace([new(DemoCommandType.Packet, 1, packet.Build())]);

        trace.ShouldContain("svc_gameevent arena_win_panel winning_team 3 player_1 local");
    }

    [Fact]
    public void Output_IsDeterministicAndLineFeedOnly()
    {
        IReadOnlyList<DemoCommand> commands = [new(DemoCommandType.Packet, 1, TickPacket(7))];

        Trace(commands).ShouldBe(Trace(commands));
        Trace(commands).ShouldNotContain("\r");
    }

    [Fact]
    public void AWholeSmallTrace_MatchesItsExpectedText()
    {
        // A golden output, added because mutation testing scored this file at 48.8% - the
        // lowest in the project outside the JSON writer, and this is the primary deliverable
        // (D18). The cause was a defect shape rather than a missing case: every test asserted
        // the output was *trace-shaped* - that it contained "block dem_" - and none asserted
        // that a field carried the right value. Blanking `map`, `server`, `client` or
        // `playback_frames` one at a time survived the whole suite. A report that names the
        // wrong map is worse than one that fails.
        //
        // Pinning the entire text kills that class of mutant at once and stays readable, where
        // forty separate assertions would not. The fixture is deliberately tiny: a header whose
        // server name contains quotes so escaping is exercised rather than assumed, one packet
        // carrying two messages, and the two command kinds that render without a body.
        DemoHeader header = new()
        {
            DemoProtocol = 3,
            NetworkProtocol = 24,
            ServerName = "a \"quoted\" server",
            ClientName = "SourceTV Demo",
            MapName = "cp_process_final",
            GameDirectory = "tf",
            PlaybackTimeSeconds = 1.5f,
            PlaybackTicks = 100,
            PlaybackFrames = 2,
            SignonLengthBytes = 0,
        };

        BitWriter packet = new();
        packet.NetTick(11, 0, 0);
        packet.Message(NetMessageType.StringCmd).String("echo hi");

        StringWriter writer = new() { NewLine = "\n" };
        DemoTraceWriter.Write(
            writer,
            "sample.dem",
            header,
            [
                new(DemoCommandType.Packet, 1, packet.Build()),
                new(DemoCommandType.SyncTick, 2, default),
                new(DemoCommandType.Stop, 3, default),
            ],
            null);

        writer.ToString().ShouldBe(
            """
            // sample.dem
            header {
                demo_protocol 3;
                network_protocol 24;
                server "a \"quoted\" server";
                client "SourceTV Demo";
                map "cp_process_final";
                game "tf";
                playback_time 1.500000;
                playback_ticks 100;
                playback_frames 2;
            }

            block dem_packet tick 1 {
                net_tick tick 11 frametime 0.000000;
                svc_stufftext "echo hi";
            }
            block dem_synctick tick 2;
            block dem_stop tick 3;

            """.ReplaceLineEndings("\n"));
    }

    [Theory]
    [InlineData("plain", "\"plain\"")]
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
    [InlineData("back\\slash", "\"back\\\\slash\"")]
    [InlineData("two\nlines", "\"two\\nlines\"")]
    [InlineData("carriage\rreturn", "\"carriage\\rreturn\"")]
    public void StringsAreEscapedSoTheTraceCanBeReadBack(string raw, string expected)
    {
        // Each escape case survived mutation individually. They are not cosmetic: an unescaped
        // quote or newline in a server name closes the field early and makes the rest of the
        // line unparseable, which is the one thing a trace must never do. Server names are
        // operator-chosen free text, so this is reachable from real data rather than theory.
        //
        // Driven through the header's server name rather than by calling Quote directly, so
        // the test measures what the file actually emits.
        DemoHeader header = Header() with { ServerName = raw };

        StringWriter writer = new() { NewLine = "\n" };
        DemoTraceWriter.Write(writer, "s.dem", header, [], null);

        writer.ToString().ShouldContain($"server {expected};");
    }
}
