using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Text;

namespace Tf2DemoSalvage.Core.Tests.Text;

/// <summary>
/// Tests for the JSON Lines output — the machine-readable counterpart to the text dump.
/// </summary>
/// <remarks>
/// The format's whole value is that each line stands alone: a consumer can <c>grep</c> it, feed
/// it to <c>jq</c>, or stream a 120,000-event demo without holding it in memory. That only holds
/// if every line really is one complete object and nothing is ever pretty-printed across lines,
/// which is what most of these assert.
///
/// Numbers must be invariant for the same reason the text dump's are: a file written on a German
/// machine has to parse everywhere.
/// </remarks>
public sealed class DemoJsonLinesWriterTests
{
    private static DemoHeader SampleHeader() => new()
    {
        DemoProtocol = 3,
        NetworkProtocol = 24,
        ServerName = "serveme.tf",
        ClientName = "SourceTV Demo",
        MapName = "cp_process_final",
        GameDirectory = "tf",
        PlaybackTimeSeconds = 1814.0249f,
        PlaybackTicks = 120935,
        PlaybackFrames = 120913,
        SignonLengthBytes = 850953,
    };

    private static string Write(IReadOnlyList<DemoCommand>? commands = null)
    {
        StringWriter writer = new() { NewLine = "\n" };
        DemoJsonLinesWriter.Write(
            writer,
            "sample.dem",
            SampleHeader(),
            commands ?? [new(DemoCommandType.Packet, 1, new byte[8])],
            null);
        return writer.ToString();
    }

    private static IReadOnlyList<JsonDocument> Lines(string output) =>
    [
        .. output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonDocument.Parse(line)),
    ];

    [Fact]
    public void EveryLine_IsOneCompleteJsonObject()
    {
        // The defining property. If any record were pretty-printed, splitting on newlines would
        // produce fragments and this would throw rather than fail an assertion.
        IReadOnlyList<JsonDocument> lines = Lines(Write());

        lines.ShouldNotBeEmpty();
        lines.ShouldAllBe(l => l.RootElement.ValueKind == JsonValueKind.Object);
    }

    [Fact]
    public void EveryLine_CarriesATypeDiscriminator()
    {
        // A consumer filters by this before knowing anything else about a line, so a record
        // without one is unusable even though it is valid JSON.
        foreach (JsonDocument line in Lines(Write()))
        {
            line.RootElement.TryGetProperty("type", out JsonElement type).ShouldBeTrue();
            type.GetString().ShouldNotBeNullOrEmpty();
        }
    }

    [Fact]
    public void FirstLine_IsTheHeader()
    {
        // Position matters: a streaming consumer reads the header to learn the map and tick
        // rate before interpreting anything after it.
        JsonElement header = Lines(Write())[0].RootElement;

        header.GetProperty("type").GetString().ShouldBe("header");
        header.GetProperty("map").GetString().ShouldBe("cp_process_final");
        header.GetProperty("networkProtocol").GetInt32().ShouldBe(24);
        header.GetProperty("playbackFrames").GetInt32().ShouldBe(120913);
    }

    [Fact]
    public void Numbers_AreInvariant_RegardlessOfCulture()
    {
        // A comma decimal separator would produce "1814,02" - which is either invalid JSON or,
        // worse, parses as two array elements somewhere downstream.
        CultureInfo original = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
            string output = Write();

            Lines(output)[0].RootElement.GetProperty("playbackTimeSeconds")
                .GetDouble().ShouldBe(1814.0249, 0.001);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = original;
        }
    }

    [Fact]
    public void Output_UsesLineFeedEndings()
    {
        // Carriage returns would make each line's trailing character part of the JSON on
        // platforms that split on LF only.
        Write().ShouldNotContain("\r");
    }

    [Fact]
    public void Output_IsDeterministic()
    {
        Write().ShouldBe(Write());
    }

    [Fact]
    public void NoDecodableContent_StillWritesTheHeader()
    {
        // A demo whose packets decode to nothing is still a demo. Emitting no lines at all
        // would be indistinguishable from a failed run.
        IReadOnlyList<JsonDocument> lines = Lines(Write());

        lines.Count.ShouldBeGreaterThanOrEqualTo(1);
        lines[0].RootElement.GetProperty("type").GetString().ShouldBe("header");
    }

    [Fact]
    public void TheHeaderLine_MatchesItsExpectedJson()
    {
        // A golden line, added because mutation testing scored this file at 19.5% - the lowest
        // in the project. The cause was a defect shape rather than a missing case: the tests
        // asserted the output was *JSON-shaped* and that `type` said "header", and nothing
        // asserted that any other field carried the right value. Blanking `map`, `server`,
        // `client` or `playbackTicks` one at a time survived the whole suite.
        //
        // Pinning the line kills that class of mutant at once. It also pins the key names,
        // which are this format's contract with anything downstream - renaming one silently
        // breaks every consumer, and no other test would have noticed.
        DemoHeader header = new()
        {
            DemoProtocol = 3,
            NetworkProtocol = 24,
            ServerName = "serveme.tf",
            ClientName = "SourceTV Demo",
            MapName = "cp_process_final",
            GameDirectory = "tf",
            PlaybackTimeSeconds = 1.5f,
            PlaybackTicks = 100,
            PlaybackFrames = 2,
            SignonLengthBytes = 0,
        };

        StringWriter writer = new() { NewLine = "\n" };
        DemoJsonLinesWriter.Write(writer, "sample.dem", header, []);

        writer.ToString().Trim().ShouldBe(
            """
            {"type":"header","file":"sample.dem","demoProtocol":3,"networkProtocol":24,"server":"serveme.tf","client":"SourceTV Demo","map":"cp_process_final","gameDirectory":"tf","playbackTimeSeconds":1.5,"playbackTicks":100,"playbackFrames":2,"signonLengthBytes":0}
            """);
    }

    [Fact]
    public void EveryLineKind_IsProducedFromARealDemo()
    {
        // The other half of the 19.5%: 47 mutants with no coverage at all, because the player,
        // chat and event branches never ran. A hand-built fixture carrying a userinfo table, a
        // chat message and a game event means writing three interlocking wire formats
        // correctly, and every attempt at that in this project has produced a fixture that
        // parsed to nothing rather than a test that failed usefully. A real demo is cheaper and
        // stronger evidence.
        //
        // Values are checked, not just line kinds. A player line naming nobody, or an event
        // line with no name, is the failure this is for.
        IReadOnlyList<string> corpus = Corpus.Files();
        if (corpus.Count == 0)
        {
            return;                                  // corpus not checked out
        }

        // A SourceTV demo by preference: it carries a full roster, where a POV demo of a solo
        // listen server names one player and would make "player lines exist" a weaker claim.
        string path = corpus.FirstOrDefault(
            p => Path.GetFileName(p).Contains("stv", StringComparison.Ordinal)) ?? corpus[0];

        byte[] bytes = File.ReadAllBytes(path);
        DemoHeader header = DemoHeader.Parse(bytes);
        List<DemoCommand> commands =
            [.. DemoCommandReader.Read(bytes.AsMemory(DemoHeader.SizeBytes)).Take(3000)];

        StringWriter writer = new() { NewLine = "\n" };
        DemoJsonLinesWriter.Write(writer, Path.GetFileName(path), header, commands);

        List<JsonDocument> lines = Lines(writer.ToString()).ToList();
        Dictionary<string, int> kinds = [];
        foreach (JsonDocument line in lines)
        {
            string kind = line.RootElement.GetProperty("type").GetString()!;
            kinds[kind] = kinds.GetValueOrDefault(kind) + 1;
        }

        kinds.ShouldContainKey("header");
        kinds.ShouldContainKey("player");
        kinds.ShouldContainKey("event");

        JsonElement player = lines
            .First(l => l.RootElement.GetProperty("type").GetString() == "player").RootElement;
        player.GetProperty("name").GetString().ShouldNotBeNullOrWhiteSpace();
        player.GetProperty("userId").GetInt32().ShouldBeGreaterThanOrEqualTo(0);

        JsonElement fired = lines
            .First(l => l.RootElement.GetProperty("type").GetString() == "event").RootElement;
        fired.GetProperty("name").GetString().ShouldNotBeNullOrWhiteSpace();
        fired.GetProperty("tick").GetInt32().ShouldBeGreaterThanOrEqualTo(0);
    }
}
