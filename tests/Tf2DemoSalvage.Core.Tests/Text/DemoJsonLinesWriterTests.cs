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
}
