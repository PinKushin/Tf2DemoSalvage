using System;
using System.Collections.Generic;

using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Core.Container;

namespace Tf2DemoSalvage.Cli.Tests;

/// <summary>
/// What the tool says about a demo whose header was never finalised.
/// </summary>
/// <remarks>
/// **`PlaybackTicks`, `PlaybackFrames` and `PlaybackTimeSeconds` are written by seeking back to
/// offset zero when recording stops**, so a recording that ended any other way leaves them at the
/// zeroes it started with. The file claims to be empty while holding a full match.
/// `DemoSurvey.Measure` already handles this correctly — it walks the command stream when the header
/// states nothing. **The CLI did not**, and reported the zeroes as measurements:
///
/// <code>
/// match.dem: 41,238,144 bytes, protocol 24, map cp_process_final, 0.0s of play
/// warning: match.dem declares 0 frames but holds 41,006
/// </code>
///
/// The first line is a wrong number stated as fact, which is worse than no number. The second is a
/// false alarm on every such file — the warning exists to catch a demo cut mid-write, and a header
/// that declares nothing is not evidence of that either way.
///
/// **No corpus demo can exercise this.** Measured 2026-08-21 across all 53, gcor and lcor: every one
/// declares real ticks, frames and seconds. The committed era demos are the owner's own clean
/// recordings and the local ones come from demos.tf, ETF2L, RGL and serveme. The case is common in
/// the wild — an outside agent reports 152 of 152 ESEA-sourced demos at zero — and unreachable here,
/// so the specimen is authored.
///
/// **Authored from the WRITER, not from the reader.** <c>DemoWriter</c> is validated against real
/// demos by round trip, and the 2007 client plays files it produces, so a header it emits carries
/// the engine's layout rather than this project's belief about it. That is the distinction in
/// <c>docs/memory/put-the-real-file-in-the-fixture.md</c>: synthetic is fine, sourcing it from our
/// own reader is not.
/// </remarks>
public sealed class UndeclaredHeaderReportingTests
{
    /// <summary>Collects log lines so what the tool SAYS can be asserted.</summary>
    private sealed class Captured : ILogger
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            Lines.Add($"{logLevel}: {formatter(state, exception)}");
        }
    }

    /// <summary>A header with the end-of-recording fields left at zero, as the engine leaves them.</summary>
    private static DemoHeader Unfinalised() => new()
    {
        DemoProtocol = 3,
        NetworkProtocol = 24,
        ServerName = "a server",
        ClientName = "a player",
        MapName = "cp_process_final",
        GameDirectory = "tf",

        // The three the writer fills in only on a clean stop.
        PlaybackTimeSeconds = 0f,
        PlaybackTicks = 0,
        PlaybackFrames = 0,
        SignonLengthBytes = 0,
    };

    [Test]
    public void Report_AHeaderDeclaringNoTicks_DoesNotClaimZeroSecondsOfPlay()
    {
        Captured log = new();

        Program.Report(
            log, "match.dem", bytes: 41_238_144, Unfinalised(), Packets(41_006),
            new System.Diagnostics.Stopwatch());

        string opening = log.Lines[0];

        // **The defect, stated as the thing a reader would believe.** "0.0s of play" on a
        // 41 MB file is not a missing value, it is a false one.
        opening.ShouldNotContain(
            "0.0s of play",
            Case.Sensitive,
            "the header declares nothing; reporting its zero as a duration invents a measurement");

        // And the honest form: say the count that IS known, and that the header gave none.
        opening.ShouldContain("41,006", Case.Sensitive, "the frame count really is known");
        opening.ShouldContain("declares no length", Case.Sensitive);
    }

    [Test]
    public void Report_AHeaderDeclaringNoFrames_DoesNotWarnAboutAMismatch()
    {
        Captured log = new();

        Program.Report(
            log, "match.dem", bytes: 41_238_144, Unfinalised(), Packets(41_006),
            new System.Diagnostics.Stopwatch());

        // The frame warning compares a declared count against an actual one. Zero is not a
        // declaration, so there is nothing to disagree with — and firing here would put a warning on
        // every ESEA-sourced demo in existence.
        log.Lines.ShouldNotContain(
            line => line.Contains("declares 0 frames", StringComparison.Ordinal),
            "a header that declares nothing cannot disagree with the stream");
    }

    [Test]
    public void Report_AFinalisedHeaderThatDisagrees_StillWarns()
    {
        // **The control, and it is the assertion that keeps the fix from being "delete the
        // warning".** A demo cut mid-write declares a real frame count and holds fewer, and that is
        // exactly what the warning is for.
        Captured log = new();

        DemoHeader finalised = Unfinalised() with
        {
            PlaybackTicks = 106_313,
            PlaybackFrames = 106_219,
            PlaybackTimeSeconds = 1594.695f,
        };

        Program.Report(
            log, "cut.dem", bytes: 41_238_144, finalised, Packets(41_006),
            new System.Diagnostics.Stopwatch());

        log.Lines.ShouldContain(
            line => line.Contains("declares 106,219 frames but holds 41,006", StringComparison.Ordinal),
            "a finalised header that disagrees with the stream is the case this warning exists for");

        // And a finalised header still reports its stated duration.
        log.Lines[0].ShouldContain("1,594.7s of play", Case.Sensitive);
    }

    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    [TestCase(-1f)]
    public void Report_ANonFiniteOrNegativeDuration_IsNotQuotedAsALength(float seconds)
    {
        // **A separate clause from the tick check, so it needs its own case.** A header can state a
        // perfectly good tick count and a nonsense duration — nothing ties the two fields — and
        // `{Seconds:N1}` on a NaN prints "NaN s of play". NaN also compares false against every
        // bound, so it survives any `> 0` guard written without thinking about it
        // (docs/memory/numeric-decoding-traps.md).
        //
        // No engine writes one: 0 of 433 real demos carry a negative or non-finite playback time.
        // That is why this declines to quote the field rather than trying to salvage it — there is
        // nothing to salvage, and refusing the whole file over one bad float would be worse.
        Captured log = new();

        DemoHeader header = Unfinalised() with
        {
            PlaybackTicks = 106_313,
            PlaybackFrames = 106_219,
            PlaybackTimeSeconds = seconds,
        };

        Program.Report(
            log, "odd.dem", bytes: 41_238_144, header, Packets(41_006),
            new System.Diagnostics.Stopwatch());

        log.Lines[0].ShouldNotContain("NaN", Case.Sensitive);
        log.Lines[0].ShouldNotContain("∞", Case.Sensitive);
        log.Lines[0].ShouldNotContain("of play", Case.Sensitive);
        log.Lines[0].ShouldContain("declares no length", Case.Sensitive);
    }

    /// <summary>A command list holding the given number of packet frames.</summary>
    private static List<DemoCommand> Packets(int count)
    {
        List<DemoCommand> commands = new(count);

        for (int index = 0; index < count; index++)
        {
            commands.Add(new DemoCommand(DemoCommandType.Packet, index, default));
        }

        return commands;
    }
}
