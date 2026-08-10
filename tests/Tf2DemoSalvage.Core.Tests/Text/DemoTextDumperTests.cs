using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Tests.Net;
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
        DemoDumpOptions? options = null,
        IProgress<DumpProgress>? progress = null)
    {
        StringWriter writer = new() { NewLine = "\n" };
        DemoTextDumper.Write(
            writer, fileName, header ?? SampleHeader(), commands ?? SampleCommands(), options,
            progress);
        return writer.ToString();
    }

    [Fact]
    public void Write_RealGameEvents_AreCountedAndListed()
    {
        // Every earlier test here passed commands with zeroed payloads, so nothing decoded and
        // the whole rendering path went untested - the mutation gate reported the entire
        // section as uncovered. Asserting "none decoded" tests the empty case only.
        string dump = Dump(commands: EventPackets());

        dump.ShouldContain("Events");
        dump.ShouldContain("player_hurt");
        dump.ShouldContain("2");                       // the count column
        dump.ShouldContain("userid=7");                // a field from Describe
        dump.ShouldContain("tick 99");                 // the timeline
    }

    [Fact]
    public void Write_EventSection_RendersExactly()
    {
        // The dump exists to be diffed - against a previous run, and eventually against another
        // parser - so its exact text is the contract, not an implementation detail. Asserting
        // substrings leaves every blank line, column width and separator free to change
        // silently, which is what the mutation gate found: the section was covered and almost
        // none of its rendering was pinned.
        // Command listing off, so the event section is the tail of the dump and can be compared
        // whole rather than sliced at the next heading.
        string dump = Dump(
            commands: EventPackets(),
            options: new DemoDumpOptions { IncludeCommandListing = false });
        int start = dump.IndexOf("Game events", StringComparison.Ordinal);

        // A raw string literal, so the expected text reads as the output it describes rather
        // than as a run of escapes. The explicit trailing newline is the section's closing
        // blank line: without it here, deleting that WriteLine would survive.
        dump[start..].ShouldBe(
            """
            Game events
            --------------------------------------------------------------------
            Events             3 across 2 types

              player_hurt                             2
              player_death                            1

              First 3 in order:

              tick 99       player_hurt                  userid=7
              tick 99       player_hurt                  userid=8
              tick 99       player_death                 userid=7
            """.ReplaceLineEndings("\n") + "\n\n");
    }

    [Fact]
    public void Write_EventWithSeveralFields_SeparatesThemWithASingleSpace()
    {
        // Every other event fixture here carries one field, so the separator between fields was
        // never exercised - deleting it survived mutation testing.
        BitWriter definitions = new();
        BitWriter body = new();
        body.Write(1, 9).String("player_hurt");
        body.Write((uint)GameEventValueType.Short, 3).String("userid");
        body.Write((uint)GameEventValueType.Byte, 3).String("health");
        body.Write((uint)GameEventValueType.None, 3);
        definitions.Message(NetMessageType.GameEventList).Write(1, 9).Write((uint)body.BitCount, 20);
        AppendBitwise(definitions, body);

        BitWriter events = new();
        BitWriter eventBody = new();
        eventBody.Write(1, 9).Write(7, 16).Write(85, 8);
        events.Message(NetMessageType.GameEvent).Write((uint)eventBody.BitCount, 11);
        AppendBitwise(events, eventBody);

        string dump = Dump(commands:
        [
            new(DemoCommandType.Signon, 0, definitions.Build()),
            new(DemoCommandType.Packet, 42, events.Build()),
        ]);

        dump.ShouldContain("userid=7 health=85");
    }

    [Fact]
    public void Write_EventFieldsNamingPlayers_ResolveToNames()
    {
        // The point of reading userinfo: a kill should read as a name, not an integer. userid 7
        // is the player at entity 1 in this fixture.
        string dump = Dump(commands: DemoFixtures.EventNamingAPlayer());

        dump.ShouldContain("userid=Sassy(7)");
    }

    [Fact]
    public void Write_NonAsciiPlayerName_ReachesTheOutputIntact()
    {
        // End to end, because that is where this broke. Every layer decoded UTF-8 correctly on
        // its own while the header did not, and one dump printed the same player as "miałker"
        // and "mia??ker" in two places. TF2 names are arbitrary client-chosen bytes and players
        // use that freely, so this is an ordinary case rather than an edge one.
        //
        // Each character fails differently under a byte-oriented reader: two-byte Cyrillic,
        // three-byte CJK and CJK punctuation, and a four-byte sequence outside the Basic
        // Multilingual Plane, which is a surrogate pair in a .NET string.
        //
        // Steam will not accept an emoji in a display name, so that last one is not a realistic
        // TF2 player - it is here because the decoder must not corrupt any valid UTF-8 it is
        // handed, and nothing else in the suite exercises a four-byte sequence end to end. The
        // Cyrillic and CJK are ordinary.
        const string awkward = "Пётр・大将🚀";

        string dump = Dump(commands: DemoFixtures.EventNamingAPlayer(playerName: awkward));

        dump.ShouldContain($"userid={awkward}(7)");
    }

    [Fact]
    public void Write_UnknownPlayerReference_StaysARawNumber()
    {
        // Resolution falls back rather than guessing. A field this parser wrongly believes
        // names a player, or an id belonging to someone who left, prints the number it read -
        // which is honest, where inventing a name would not be.
        string dump = Dump(commands: DemoFixtures.EventNamingAPlayer(userId: 99));

        dump.ShouldContain("userid=99");
        dump.ShouldNotContain("userid=Sassy");
    }

    [Fact]
    public void Write_FieldsThatAreNotPlayerReferences_StayNumeric()
    {
        // Found on a real demo, not in a fixture. Resolving every numeric field produced
        // damageamount=Ardaddy Ultrasex(14) - 14 damage colliding with user id 14 - and turned
        // inflictor_entindex into a player when the inflictor is a weapon entity. Falling back
        // on unknown ids does not help: the value was known, it simply was not a player.
        //
        // So resolution is an allowlist of field names. damageamount carries 7 here, which is a
        // real user id in this fixture, and must still print as a number.
        string dump = Dump(commands: DemoFixtures.EventNamingAPlayer(fieldName: "damageamount"));

        dump.ShouldContain("damageamount=7");
        dump.ShouldNotContain("damageamount=Sassy");
    }

    [Fact]
    public void Write_AbsentAssister_ReadsAsNone()
    {
        // TF2 sends a large sentinel rather than a null for an unassisted kill, so the raw
        // number would otherwise appear as a player id nobody holds.
        string dump = Dump(commands: DemoFixtures.EventNamingAPlayer(userId: 16384, fieldName: "assister"));

        dump.ShouldContain("assister=none");
    }

    [Fact]
    public void Write_EventsSortedByFrequency_MostCommonFirst()
    {
        string dump = Dump(commands: EventPackets());
        string[] lines = dump.Split('\n');

        int hurt = Array.FindIndex(lines, l => l.Contains("player_hurt", StringComparison.Ordinal));
        int death = Array.FindIndex(lines, l => l.Contains("player_death", StringComparison.Ordinal));

        // player_hurt occurs twice, player_death once. Sorting the other way, or not at all,
        // would put the rarer event first on this input.
        hurt.ShouldBeGreaterThan(0);
        hurt.ShouldBeLessThan(death);
    }

    [Fact]
    public void Write_EventSample_IsCappedAtTheConfiguredSize()
    {
        string dump = Dump(
            commands: EventPackets(),
            options: new DemoDumpOptions { GameEventSampleSize = 1 });

        // Three events, one sampled. The count summary still reports all three.
        dump.ShouldContain("First 1 in order");
        dump.ShouldContain("3 across 2 types");
    }

    [Fact]
    public void Write_NoDecodableMessages_SaysSoRatherThanOmittingTheSection()
    {
        // The sample commands carry zeroed payloads, so nothing decodes. A section that simply
        // vanished would be indistinguishable from a demo with no events, which is a different
        // thing entirely.
        Dump().ShouldContain("Game events");
        Dump().ShouldContain("none decoded");
    }

    [Fact]
    public void Write_EventsOff_OmitsTheSectionEntirely()
    {
        Dump(options: new DemoDumpOptions { IncludeGameEvents = false })
            .ShouldNotContain("Game events");
    }

    [Fact]
    public void Write_ReportsProgressThroughTheEventScan()
    {
        // The event scan walks every packet, which on a full match is tens of thousands of
        // them and takes long enough that silence looks like a hang.
        List<DumpProgress> reports = [];

        Dump(commands: ManyPackets(500), progress: new SyncProgress(reports.Add));

        reports.ShouldNotBeEmpty();
        reports[^1].Completed.ShouldBe(reports[^1].Total);
        reports.ShouldAllBe(r => r.Completed <= r.Total);
    }

    [Fact]
    public void Write_ProgressRises_AndEndsAtTheTotal()
    {
        // Monotonic and finishing at 100% are the two properties a caller draws a bar from.
        // A report that jumped backwards or stopped at 97% would render as a stuck bar.
        List<DumpProgress> reports = [];

        Dump(commands: ManyPackets(500), progress: new SyncProgress(reports.Add));

        for (int i = 1; i < reports.Count; i++)
        {
            reports[i].Completed.ShouldBeGreaterThanOrEqualTo(reports[i - 1].Completed);
        }

        reports[^1].Fraction.ShouldBe(1d);
    }

    [Theory]
    [InlineData(0, 10, 0d)]
    [InlineData(5, 10, 0.5d)]
    [InlineData(10, 10, 1d)]
    public void Fraction_HandlesItsRange(int completed, int total, double expected)
    {
        new DumpProgress("s", completed, total).Fraction.ShouldBe(expected);
    }

    [Fact]
    public void Fraction_WithNothingToDo_IsOneRatherThanDividingByZero()
    {
        // A stage with no work is finished, not undefined. Reporting 0 would render as a bar
        // stuck at empty for a demo with no commands.
        new DumpProgress("s", 0, 0).Fraction.ShouldBe(1d);
        new DumpProgress("s", 0, -1).Fraction.ShouldBe(1d);
    }

    [Theory]
    [InlineData(0, 4, "Scan [----]   0%")]
    [InlineData(2, 4, "Scan [##--]  50%")]
    [InlineData(4, 4, "Scan [####] 100%")]
    public void ToBar_DrawsProportionally(int completed, int width, string expected)
    {
        new DumpProgress("Scan", completed, 4).ToBar(width).ShouldBe(expected);
    }

    [Fact]
    public void ToBar_NonPositiveWidth_IsRejected()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new DumpProgress("s", 1, 2).ToBar(0));
        Should.Throw<ArgumentOutOfRangeException>(() => new DumpProgress("s", 1, 2).ToBar(-1));
    }

    [Fact]
    public void Write_ProgressReportsAtTheIntervalAndAtTheEnd()
    {
        // 512 commands per report plus a final one. With 600 commands that is exactly two:
        // one at 512, one at 600. A wrong interval or a missing final report changes the count,
        // and the final report is what makes a bar reach 100%.
        List<DumpProgress> reports = [];

        Dump(commands: ManyPackets(600), progress: new SyncProgress(reports.Add));

        reports.Count.ShouldBe(2);
        reports[0].Completed.ShouldBe(512);
        reports[1].Completed.ShouldBe(600);
    }

    [Fact]
    public void Write_SkippedCommandsStillAdvanceProgress()
    {
        // Console commands are not scanned for events, but they must still count - otherwise a
        // demo with a long run of them shows a stalled bar and never reaches its total.
        List<DumpProgress> reports = [];
        IReadOnlyList<DemoCommand> commands =
        [
            .. Enumerable.Range(0, 600)
                .Select(i => new DemoCommand(DemoCommandType.ConsoleCmd, i, new byte[4])),
        ];

        Dump(commands: commands, progress: new SyncProgress(reports.Add));

        reports[^1].Completed.ShouldBe(600);
        reports[^1].Fraction.ShouldBe(1d);
    }

    [Fact]
    public void Write_NoProgressListener_StillWorks()
    {
        // Progress is optional; every existing caller passes nothing.
        Dump(progress: null).ShouldContain("Demo dump:");
    }

    /// <summary>Reports synchronously, so a test can assert without waiting.</summary>
    private sealed class SyncProgress(Action<DumpProgress> onReport) : IProgress<DumpProgress>
    {
        public void Report(DumpProgress value) => onReport(value);
    }

    /// <summary>
    /// Two packets carrying real, decodable game events: a list defining two event types, then
    /// three events using them.
    /// </summary>
    private static IReadOnlyList<DemoCommand> EventPackets()
    {
        // Definitions first, then three events using them. The definitions must arrive in an
        // earlier command than the events, exactly as in a real demo: a game event carries only
        // an id, so without a prior svc_GameEventList it decodes to nothing.
        BitWriter definitions = new();
        DemoFixtures.WriteEventList(definitions);

        BitWriter events = new();
        DemoFixtures.WriteEvent(events, 1, 7);
        DemoFixtures.WriteEvent(events, 1, 8);
        DemoFixtures.WriteEvent(events, 2, 7);

        return
        [
            new(DemoCommandType.Signon, 0, definitions.Build()),
            new(DemoCommandType.Packet, 99, events.Build()),
        ];
    }

    /// <summary>Copies a body bit by bit, so it lands at the reader's current unaligned offset.</summary>
    /// <param name="writer">Destination.</param>
    /// <param name="body">Bits to append.</param>
    private static void AppendBitwise(BitWriter writer, BitWriter body)
    {
        byte[] bytes = body.Build();
        for (int bit = 0; bit < body.BitCount; bit++)
        {
            writer.Write((uint)((bytes[bit / 8] >> (bit % 8)) & 1), 1);
        }
    }

    private static IReadOnlyList<DemoCommand> ManyPackets(int count) =>
    [
        .. Enumerable.Range(0, count)
            .Select(i => new DemoCommand(DemoCommandType.Packet, i, new byte[8])),
    ];

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

        // Two rules around each of the title block, Players, Chat and Game events, then a
        // blank line before each following section. Deleting any of those WriteLine calls
        // survived mutation testing until this assertion existed.
        lines.Count(l => l.StartsWith("------", StringComparison.Ordinal)).ShouldBe(8);
        lines.ShouldContain("Players");
        lines.ShouldContain("Chat");
        lines.ShouldContain("Game events");
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
        StringWriter writer = new();

        Should.Throw<ArgumentNullException>(
            () => DemoTextDumper.Write(writer, "x.dem", null!, SampleCommands(), null));
    }

    [Fact]
    public void Write_NullCommands_Throws()
    {
        StringWriter writer = new();

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
