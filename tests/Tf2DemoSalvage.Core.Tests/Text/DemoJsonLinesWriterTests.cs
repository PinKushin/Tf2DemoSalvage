using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Core.Tests.Net;
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

    private static string Write(
        IReadOnlyList<DemoCommand>? commands = null, DemoHeader? header = null)
    {
        StringWriter writer = new() { NewLine = "\n" };
        DemoJsonLinesWriter.Write(
            writer,
            "sample.dem",
            header ?? SampleHeader(),
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
    public void EntityLifecycle_IsReportedAsItsOwnLineType()
    {
        // Phase 1 asks for a normalized event stream covering "entity spawn/update/delete", and
        // this machine format carried header, players, chat and game events but nothing about
        // entities at all - the one part of the demo the schema work exists to reach.
        //
        // Lifecycle only, not per-property updates: entering, leaving and deleting are thousands
        // of lines in a long demo where property changes are millions, and a consumer asking "when
        // did this entity exist" should not have to read the second to learn the first.
        IReadOnlyList<JsonDocument> lines = Lines(Write(commands: DemoFixtures.EntityLifecycle()));

        JsonElement[] entities =
        [
            .. lines.Select(l => l.RootElement)
                .Where(e => e.GetProperty("type").GetString() == "entity")
        ];

        entities.Length.ShouldBe(3);

        entities[0].GetProperty("event").GetString().ShouldBe("enter");
        entities[0].GetProperty("entity").GetInt32().ShouldBe(DemoFixtures.EnteringEntity);
        entities[0].GetProperty("tick").GetInt32().ShouldBe(42);
        entities[0].GetProperty("class").GetString().ShouldBe(DemoFixtures.EnteringClassName);
        entities[0].GetProperty("serial").GetInt32().ShouldBe(DemoFixtures.EnteringSerial);

        entities[1].GetProperty("event").GetString().ShouldBe("leave");
        entities[1].GetProperty("entity").GetInt32().ShouldBe(1);

        entities[2].GetProperty("event").GetString().ShouldBe("delete");
        entities[2].GetProperty("entity").GetInt32().ShouldBe(2);

        // Serial is written only on entry, where it is what distinguishes a reused index from
        // the entity that held it before. On a leave it would be a fabricated zero.
        entities[1].TryGetProperty("serial", out _).ShouldBeFalse();
    }

    [Fact]
    public void NonAsciiText_RoundTripsThroughTheJson()
    {
        // Asserted by parsing the line back rather than by searching the raw text, because
        // whether the writer emits the characters literally or as \uXXXX escapes is its own
        // business - both are valid JSON and both must read back as the same string. Searching
        // for the literal would be a test of the escaping policy, not of the data surviving.
        DemoHeader header = SampleHeader() with
        {
            MapName = "cp_köln_b3",
            ServerName = "Sërvér ✦ EU",
            ClientName = "Пётр🚀",
        };

        JsonDocument first = Lines(Write(header: header))[0];

        first.RootElement.GetProperty("map").GetString().ShouldBe("cp_köln_b3");
        first.RootElement.GetProperty("server").GetString().ShouldBe("Sërvér ✦ EU");
        first.RootElement.GetProperty("client").GetString().ShouldBe("Пётр🚀");
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

}
