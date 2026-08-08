using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Text;

namespace Tf2DemoSalvage.Core.Tests.Text;

/// <summary>
/// Tests for the human-readable dump — the project's first actual deliverable.
/// </summary>
/// <remarks>
/// Two properties matter beyond "it prints something". The output must be deterministic and
/// culture-invariant, because it is intended to be diffed: against a previous run to spot
/// regressions, and eventually against another parser's output. A dump that renders 1814.02
/// as "1814,02" on a German machine cannot be diffed against one that does not.
/// </remarks>
public sealed class DemoTextDumperTests
{
    private static DemoHeader SampleHeader() => new()
    {
        DemoProtocol = 3,
        NetworkProtocol = 24,
        ServerName = "serveme.tf (#1055422)",
        ClientName = "SourceTV Demo",
        MapName = "cp_process_final",
        GameDirectory = "tf",
        PlaybackTimeSeconds = 1814.0249f,
        PlaybackTicks = 120935,
        PlaybackFrames = 120913,
        SignonLengthBytes = 850953,
    };

    private static IReadOnlyList<DemoCommand> SampleCommands() =>
    [
        new(DemoCommandType.Signon, 0, new byte[128]),
        new(DemoCommandType.DataTables, 0, new byte[64]),
        new(DemoCommandType.Packet, 1, new byte[16]),
        new(DemoCommandType.Packet, 2, new byte[8]),
        new(DemoCommandType.Stop, 120935, ReadOnlyMemory<byte>.Empty),
    ];

    private static string Dump(
        DemoHeader? header = null,
        IReadOnlyList<DemoCommand>? commands = null,
        string fileName = "sample.dem",
        DemoDumpOptions? options = null)
    {
        var writer = new StringWriter { NewLine = "\n" };
        DemoTextDumper.Write(
            writer, fileName, header ?? SampleHeader(), commands ?? SampleCommands(), options);
        return writer.ToString();
    }

    [Fact]
    public void Write_IncludesEveryHeaderField()
    {
        string dump = Dump();

        dump.ShouldContain("sample.dem");
        dump.ShouldContain("cp_process_final");
        dump.ShouldContain("serveme.tf (#1055422)");
        dump.ShouldContain("SourceTV Demo");
        dump.ShouldContain("120935");
        dump.ShouldContain("120913");
        dump.ShouldContain("850953");
    }

    [Fact]
    public void Write_SummarisesCommandCountsByType()
    {
        string dump = Dump();

        dump.ShouldContain("dem_packet");
        dump.ShouldContain("dem_datatables");

        // Two packets in the sample; the summary must say so.
        string summaryLine = dump.Split('\n').First(l => l.Contains("dem_packet", StringComparison.Ordinal));
        summaryLine.ShouldContain("2");
    }

    [Fact]
    public void Write_ListsEveryCommandWithTickAndPayloadSize()
    {
        string dump = Dump();

        // One line per command in the listing section, plus the summary occurrences.
        dump.ShouldContain("dem_stop");
        dump.ShouldContain("128");  // signon payload size
        dump.Split('\n').Count(l => l.Contains("dem_packet", StringComparison.Ordinal))
            .ShouldBeGreaterThanOrEqualTo(3);  // 1 summary + 2 listing rows
    }

    [Fact]
    public void Write_SummaryOnly_OmitsThePerCommandListing()
    {
        string[] lines = Dump(options: new DemoDumpOptions { IncludeCommandListing = false })
            .Split('\n');

        lines.ShouldContain("Command summary");
        // Checked as a whole line, not a substring: "Commands" is a substring of "Command
        // summary", and an earlier version of this test asserted on a padding width that
        // never appeared, so it passed whether the listing was emitted or not.
        lines.ShouldNotContain("Commands");
    }

    [Fact]
    public void Write_IsDeterministic_ForTheSameInput()
    {
        Dump().ShouldBe(Dump());
    }

    [Fact]
    public void Write_UsesInvariantFormatting_RegardlessOfCurrentCulture()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            // A culture that uses a comma as the decimal separator. Without invariant
            // formatting the playback time would render as "1814,02" and stop being diffable.
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            string german = Dump();

            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            string invariant = Dump();

            german.ShouldBe(invariant);
            german.ShouldContain("1814.02");
            german.ShouldNotContain("1814,02");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Write_UsesLineFeedEndings_SoOutputDiffsCleanlyAcrossPlatforms()
    {
        string dump = Dump();

        dump.ShouldNotContain("\r");
    }

    [Fact]
    public void Write_NoCommands_StillEmitsTheHeaderAndAnEmptySummary()
    {
        string dump = Dump(commands: []);

        dump.ShouldContain("cp_process_final");
        dump.ShouldNotBeEmpty();
    }

    [Fact]
    public void Write_ReportsWhenTheCommandCountDisagreesWithTheHeader()
    {
        // The header declares 120,913 frames but the sample holds two packets. A dump that
        // stays silent about that is hiding the single most useful correctness signal we have.
        string dump = Dump();

        dump.ShouldContain("MISMATCH", Case.Insensitive);
    }

    [Fact]
    public void Write_ReportsAgreementWhenCountsMatch()
    {
        DemoHeader header = SampleHeader() with { PlaybackFrames = 2, PlaybackTicks = 120935 };

        string dump = Dump(header: header);

        dump.ShouldNotContain("MISMATCH", Case.Insensitive);
    }

    [Theory]
    [InlineData("Demo protocol")]
    [InlineData("Network protocol")]
    [InlineData("Server")]
    [InlineData("Client")]
    [InlineData("Map")]
    [InlineData("Game directory")]
    [InlineData("Playback time")]
    [InlineData("Playback ticks")]
    [InlineData("Playback frames")]
    [InlineData("Signon length")]
    [InlineData("Command summary")]
    [InlineData("Frame check")]
    public void Write_EmitsEveryLabel(string label)
    {
        // Values alone are not the contract - an unlabelled column of numbers is not a
        // readable dump. Mutation testing found every one of these labels deletable without
        // a single test noticing.
        Dump().ShouldContain(label);
    }

    [Theory]
    [InlineData(DemoCommandType.Signon, "dem_signon")]
    [InlineData(DemoCommandType.Packet, "dem_packet")]
    [InlineData(DemoCommandType.SyncTick, "dem_synctick")]
    [InlineData(DemoCommandType.ConsoleCmd, "dem_consolecmd")]
    [InlineData(DemoCommandType.UserCmd, "dem_usercmd")]
    [InlineData(DemoCommandType.DataTables, "dem_datatables")]
    [InlineData(DemoCommandType.Stop, "dem_stop")]
    [InlineData(DemoCommandType.StringTables, "dem_stringtables")]
    public void Write_UsesValveWireNames_NotEnumMemberNames(
        DemoCommandType type, string expected)
    {
        // The dump is read alongside Valve's own documentation, so it must say dem_packet
        // rather than Packet.
        string dump = Dump(commands: [new DemoCommand(type, 1, new byte[4])]);

        dump.ShouldContain(expected);
        dump.ShouldNotContain($" {type} ");
    }

    [Fact]
    public void Write_HasTheExpectedSectionStructure()
    {
        string[] lines = Dump().Split('\n');

        // Two rules separating the title block, then a blank line before the summary, then a
        // blank line before the listing. Deleting any of those WriteLine calls survived
        // mutation testing until this assertion existed.
        lines.Count(l => l.StartsWith("------", StringComparison.Ordinal)).ShouldBe(2);
        lines[1].ShouldStartWith("Demo dump:");
        lines.ShouldContain("Command summary");
        lines.ShouldContain("Commands");

        // Positional, not a count: a count of "at least three blank lines" tolerates one
        // being deleted. Each section must actually be preceded by its separating blank.
        lines[Array.IndexOf(lines, "Command summary") - 1].ShouldBeEmpty();
        lines[Array.IndexOf(lines, "Commands") - 1].ShouldBeEmpty();
    }

    [Fact]
    public void Write_NullOptions_DefaultsToIncludingTheListing()
    {
        // Guards the `options ??= new(...)` coalesce: replacing it with a plain assignment
        // would silently discard whatever the caller passed.
        Dump(options: null).ShouldContain("Commands");
    }

    [Fact]
    public void Write_NullHeader_Throws()
    {
        var writer = new StringWriter();

        Should.Throw<ArgumentNullException>(
            () => DemoTextDumper.Write(writer, "x.dem", null!, SampleCommands(), null));
    }

    [Fact]
    public void Write_NullCommands_Throws()
    {
        var writer = new StringWriter();

        Should.Throw<ArgumentNullException>(
            () => DemoTextDumper.Write(writer, "x.dem", SampleHeader(), null!, null));
    }

    [Fact]
    public void Write_NullWriter_Throws()
    {
        Should.Throw<ArgumentNullException>(
            () => DemoTextDumper.Write(null!, "x.dem", SampleHeader(), SampleCommands(), null));
    }
}
