using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Text;

namespace Tf2DemoSalvage.Core.Tests.Text;

/// <summary>
/// Tests for the assembly form — the text a demo can be compiled back out of.
/// </summary>
/// <remarks>
/// The corpus round trip is the real check, but it can only fail on inputs the corpus contains.
/// These cover the grammar's edges: a server name with a space in it, a comment, a float whose
/// shortest decimal form is not its value. Each one is a way a text format loses something while
/// looking fine.
/// </remarks>
public sealed class DemoAssemblyTests
{
    private static DemoHeader Header(
        string server = "serveme.tf", float playbackTime = 1814.0249f) => new()
    {
        DemoProtocol = 3,
        NetworkProtocol = 24,
        ServerName = server,
        ClientName = "SourceTV Demo",
        MapName = "cp_process_final",
        GameDirectory = "tf",
        PlaybackTimeSeconds = playbackTime,
        PlaybackTicks = 120935,
        PlaybackFrames = 120913,
        SignonLengthBytes = 850953,
    };

    private static (DemoHeader Header, IReadOnlyList<DemoCommand> Commands) RoundTrip(
        DemoHeader header, params DemoCommand[] commands)
    {
        StringWriter text = new() { NewLine = "\n" };
        DemoAssembly.Write(text, header, commands);

        using StringReader reader = new(text.ToString());
        return DemoAssembly.Parse(reader);
    }

    [Fact]
    public void AServerNameWithSpaces_SurvivesTheRoundTrip()
    {
        // Unquoted, the parser would take "Uncle" as the value and drop the rest. Real server
        // names look like this far more often than they look like a hostname.
        DemoHeader header = Header(server: "Uncle Dane's Dispenser Emporium");

        RoundTrip(header).Header.ServerName.ShouldBe("Uncle Dane's Dispenser Emporium");
    }

    [Fact]
    public void APlaybackTimeThatIsNotExactInDecimal_SurvivesExactly()
    {
        // 1814.0249 has no exact float representation, so a writer rounding to a few places
        // produces a different float and a header byte that does not match. Compared as bits
        // because that is the question - not whether the times are close.
        DemoHeader parsed = RoundTrip(Header()).Header;

        BitConverter.SingleToInt32Bits(parsed.PlaybackTimeSeconds)
            .ShouldBe(BitConverter.SingleToInt32Bits(1814.0249f));
    }

    [Fact]
    public void PayloadAndPrologue_ComeBackAsTheSameBytes()
    {
        byte[] prologue = [0xDE, 0xAD, 0xBE, 0xEF];
        byte[] payload = [0x01, 0x02, 0x03, 0xFF, 0x00, 0x7F];

        IReadOnlyList<DemoCommand> commands = RoundTrip(
            Header(),
            new DemoCommand(DemoCommandType.Packet, 42, payload, prologue)).Commands;

        DemoCommand command = commands.ShouldHaveSingleItem();
        command.Type.ShouldBe(DemoCommandType.Packet);
        command.Tick.ShouldBe(42);
        command.Prologue.ToArray().ShouldBe(prologue);
        command.Payload.ToArray().ShouldBe(payload);
    }

    [Fact]
    public void ACommandWithNoPayload_StaysThatWayRatherThanGainingAnEmptyOne()
    {
        // dem_synctick carries nothing at all, and it is not length-prefixed either - a writer
        // that gave it a zero-length payload would insert four bytes into the demo.
        IReadOnlyList<DemoCommand> commands = RoundTrip(
            Header(), new DemoCommand(DemoCommandType.SyncTick, 7, default)).Commands;

        DemoCommand command = commands.ShouldHaveSingleItem();
        command.Payload.IsEmpty.ShouldBeTrue();
        command.Prologue.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void CommentsAndBlankLines_AreIgnored()
    {
        const string source = """
            # a demo, annotated
            demo
              demoprotocol 3      # the container version

              networkprotocol 24
              server "local"
              client "c"
              map "m"
              gamedir "tf"
              playbacktime 1
              playbackticks 2
              playbackframes 3
              signonlength 4
            end

            packet 9 data 00FF   # one packet
            """;

        using StringReader reader = new(source);
        (DemoHeader header, IReadOnlyList<DemoCommand> commands) = DemoAssembly.Parse(reader);

        header.NetworkProtocol.ShouldBe(24);
        commands.ShouldHaveSingleItem().Payload.ToArray().ShouldBe(new byte[] { 0x00, 0xFF });
    }

    [Fact]
    public void AHashInsideAQuotedName_IsNotAComment()
    {
        // The control for the test above. Stripping comments without minding quotes would turn
        // this server into "clan", silently, and only for servers whose names contain a hash.
        DemoHeader header = Header(server: "clan #1 pug server");

        RoundTrip(header).Header.ServerName.ShouldBe("clan #1 pug server");
    }

    [Fact]
    public void TextWithNoHeaderBlock_IsRefused()
    {
        using StringReader reader = new("packet 1 data 00\n");

        Should.Throw<InvalidDataException>(() => DemoAssembly.Parse(reader));
    }

    [Fact]
    public void AnUnknownCommandKeyword_IsRefusedRatherThanSkipped()
    {
        // Skipping it would compile a demo missing a command, which is a file that plays wrongly
        // rather than one that fails to build.
        using StringReader reader = new(
            "demo\n  demoprotocol 3\n  networkprotocol 24\n  server \"s\"\n  client \"c\"\n" +
            "  map \"m\"\n  gamedir \"tf\"\n  playbacktime 1\n  playbackticks 2\n" +
            "  playbackframes 3\n  signonlength 4\nend\nteleport 5 data 00\n");

        Should.Throw<InvalidDataException>(() => DemoAssembly.Parse(reader));
    }
}
