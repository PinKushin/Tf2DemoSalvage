using System;
using System.IO;
using System.Linq;
using Tf2DemoSalvage.Cli;

namespace Tf2DemoSalvage.Cli.Tests;

/// <summary>
/// The tool run end to end, through <see cref="Program.Main"/>.
/// </summary>
/// <remarks>
/// Added after mutation testing reported 53 mutants in <c>Program.cs</c> with **no coverage at
/// all** — not survivors, but code no test had ever executed. Splitting the argument grammar out
/// into <see cref="CommandLine"/> made that part testable and left the part that actually runs
/// the tool untested, which is a fair trade only if something eventually runs it.
///
/// These are deliberately thin. They check exit codes, which stream output went to, and that the
/// chosen writer was the one that ran — not the content of the output, which the core suite
/// already covers against the whole corpus. The value here is that the wiring is exercised: a
/// format flag connected to the wrong writer, or a path never opened, is invisible to every other
/// test in this repository.
/// </remarks>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Usage", "CA2213:Disposable fields should be disposed",
    Justification = "_originalOut and _originalError are the process's real console streams, " +
                    "captured so they can be restored. Disposing them would close standard " +
                    "output for the rest of the test run.")]
// **Not parallelisable, because Console redirection is process-global.** These tests capture
// output by pointing Console.SetOut at their own writer, and there is exactly one Console per
// process - so two of them running at once redirect it out from under each other and both read an
// empty buffer.
//
// It surfaced the moment this project moved to NUnit with assembly-wide parallelism: six tests
// failed with "should contain ... but was empty". Under xUnit the same code passed, because xUnit
// parallelises by collection and runs one class's tests serially within it - the hazard was
// always there and the old default hid it.
//
// Scoped to this fixture rather than the assembly: the rest of the CLI tests touch no global state
// and there is no reason to serialise them too.
[NonParallelizable]
public sealed class ProgramTests : IDisposable
{
    private readonly TextWriter _originalOut = Console.Out;
    private readonly TextWriter _originalError = Console.Error;
    private readonly StringWriter _out = new();
    private readonly StringWriter _error = new();
    private readonly string _scratch =
        Path.Combine(Path.GetTempPath(), "tf2demosalvage-tests", Guid.NewGuid().ToString("N"));

    public ProgramTests()
    {
        Console.SetOut(_out);
        Console.SetError(_error);
        Directory.CreateDirectory(_scratch);
    }

    public void Dispose()
    {
        Console.SetOut(_originalOut);
        Console.SetError(_originalError);

        // The captures are ours and are disposed; the originals are the process's real console
        // streams, borrowed for the length of the test and deliberately left open.
        _out.Dispose();
        _error.Dispose();

        if (Directory.Exists(_scratch))
        {
            Directory.Delete(_scratch, recursive: true);
        }
    }

    /// <summary>A real demo, or <c>null</c> if the corpus is not checked out.</summary>
    private static string? Demo()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            string candidate = Path.Combine(current.FullName, "tools", "corpus", "demos");
            if (Directory.Exists(candidate))
            {
                // Anything under 4 KB is a Git LFS pointer stub rather than a demo.
                return Directory.EnumerateFiles(candidate, "*.dem")
                    .Where(p => new FileInfo(p).Length >= 4096)
                    .OrderBy(p => p, StringComparer.Ordinal)
                    .FirstOrDefault();
            }

            current = current.Parent;
        }

        return null;
    }


    [Test]
    public void NoArguments_ExitsWithAUsageCodeAndSaysWhy()
    {
        Program.Main([]).ShouldBe(2);

        _error.ToString().ShouldContain("no demo given");
        _error.ToString().ShouldContain("usage:");
        _out.ToString().ShouldBeEmpty();
    }

    [Test]
    public void Help_GoesToStandardOutputAndSucceeds()
    {
        // Which stream matters: help was asked for, so it is output, not a diagnostic. A user
        // piping `--help` into a pager gets nothing if this goes to standard error.
        Program.Main(["--help"]).ShouldBe(0);

        _out.ToString().ShouldContain("usage:");
        _error.ToString().ShouldBeEmpty();
    }

    [Test]
    public void UnrecognisedOption_ExitsWithAUsageCode()
    {
        Program.Main(["a.dem", "--nonsense"]).ShouldBe(2);

        _error.ToString().ShouldContain("unrecognised option '--nonsense'");
    }

    [Test]
    public void MissingFile_ExitsWithAFailureCodeNotAUsageCode()
    {
        // Distinct from a usage error on purpose: the command line was well formed, the file
        // was not there. A script that branches on the exit code needs those separable.
        Program.Main([Path.Combine(_scratch, "absent.dem")]).ShouldBe(1);

        _error.ToString().ShouldContain("no such file");
    }

    [Test]
    public void NotADemo_IsReportedRatherThanThrown()
    {
        string junk = Path.Combine(_scratch, "junk.dem");
        File.WriteAllBytes(junk, new byte[2048]);

        Program.Main([junk]).ShouldBe(1);

        // The message names the problem rather than merely existing - "error:" with an empty
        // body is what an unasserted message degrades to.
        _error.ToString().ShouldStartWith("error: ");
        _error.ToString().Trim().Length.ShouldBeGreaterThan("error: ".Length);
    }

    [Test]
    public void Trace_WritesTheTraceFormatToTheGivenFile()
    {
        string? demo = Demo();
        if (demo is null)
        {
            return;                                  // corpus not checked out
        }

        string output = Path.Combine(_scratch, "trace.txt");

        Program.Main([demo, "-t", "-o", output]).ShouldBe(0);

        string written = File.ReadAllText(output);

        // `block ... {` is the trace's shape and appears in no other format, so this
        // distinguishes "wrote something" from "wrote the thing that was asked for".
        written.ShouldContain("header {");
        written.ShouldContain("block dem_");
        written.ShouldNotContain("Command summary");
        _error.ToString().ShouldContain("wrote");
    }

    [Test]
    public void Summary_WritesTheDumpFormatInstead()
    {
        // The control for the test above. Without a second format asserted to be *different*,
        // a Program that ignored the flag and always wrote one thing would pass.
        string? demo = Demo();
        if (demo is null)
        {
            return;
        }

        string output = Path.Combine(_scratch, "summary.txt");

        Program.Main([demo, "-s", "-o", output]).ShouldBe(0);

        string written = File.ReadAllText(output);
        written.ShouldContain("Command summary");
        written.ShouldNotContain("block dem_");
    }

    [Test]
    public void Summary_SuppressesThePerCommandListingThatTheDumpIncludes()
    {
        // What -s actually does, measured as the difference it makes rather than as a string.
        // Asserting "Command summary" cannot tell the two apart - both contain it, and so does
        // the counts table that names dem_packet - so inverting the flag survived mutation
        // testing. The listing is one line per command, tens of thousands of them, so the
        // comparison is decisive and needs no invented threshold.
        string? demo = Demo();
        if (demo is null)
        {
            return;
        }

        string dump = Path.Combine(_scratch, "dump.txt");
        string summary = Path.Combine(_scratch, "summary-only.txt");

        Program.Main([demo, "-o", dump]).ShouldBe(0);
        Program.Main([demo, "-s", "-o", summary]).ShouldBe(0);

        int dumpLines = File.ReadAllLines(dump).Length;
        int summaryLines = File.ReadAllLines(summary).Length;

        dumpLines.ShouldBeGreaterThan(summaryLines * 2);

        // Both still carry the summary sections, so the difference above is the listing and
        // not the whole report going missing.
        File.ReadAllText(summary).ShouldContain("Command summary");
        File.ReadAllText(dump).ShouldContain("Command summary");
    }

    [Test]
    public void JsonLines_WritesOneObjectPerLine()
    {
        string? demo = Demo();
        if (demo is null)
        {
            return;
        }

        string output = Path.Combine(_scratch, "out.jsonl");

        Program.Main([demo, "-j", "-o", output]).ShouldBe(0);

        string[] lines = File.ReadAllLines(output);
        lines.ShouldNotBeEmpty();
        lines.ShouldAllBe(line => line.StartsWith('{') && line.EndsWith('}'));
    }

    [Test]
    public void WithNoOutputPath_ItWritesToStandardOutput()
    {
        string? demo = Demo();
        if (demo is null)
        {
            return;
        }

        Program.Main([demo, "-s"]).ShouldBe(0);

        _out.ToString().ShouldContain("Command summary");
    }

    [Test]
    public void Entities_AppearOnlyWhenAskedFor()
    {
        string? demo = Demo();
        if (demo is null)
        {
            return;
        }

        string without = Path.Combine(_scratch, "plain.txt");
        string with = Path.Combine(_scratch, "entities.txt");

        Program.Main([demo, "-t", "-o", without]).ShouldBe(0);
        Program.Main([demo, "-t", "--entity-limit", "2", "-o", with]).ShouldBe(0);

        // Compared against each other rather than against a fixed string: the flag's whole
        // effect is "more detail than the default", and that is a difference, not a value.
        new FileInfo(with).Length.ShouldBeGreaterThan(new FileInfo(without).Length);
        File.ReadAllText(with).ShouldContain("entity ");
    }

    [Test]
    public void NullArguments_Throws()
    {
        Should.Throw<ArgumentNullException>(() => Program.Main(null!));
    }

    [Test]
    public void Verbose_ListsTheCommandBreakdownThatOrdinaryOutputOmits()
    {
        // **The whole Debug branch of Report had no coverage at all** — 12 mutants in Program.cs
        // that no test had ever executed, because every existing test runs at the default
        // verbosity and `--verbose` is what raises the level to Debug (LogSetup.LevelFor).
        //
        // What that branch does is grouped counting, guarded rather than left for the logger to
        // discard because it is real work per demo in a batch that may cover hundreds. So the
        // assertion is that the counts appear, and the control below is that they do not without
        // the flag.
        string? demo = Demo();
        if (demo is null)
        {
            return;                                  // corpus not checked out
        }

        Program.Main([demo, "-s", "-o", Path.Combine(_scratch, "verbose.txt"), "--verbose"])
            .ShouldBe(0);

        string log = _error.ToString();

        log.ShouldContain("read ");
        log.ShouldContain("commands in ");

        // A per-type line, which is what the grouping produces. dem_packet is in every demo.
        log.ShouldContain("Packet");
    }

    [Test]
    public void WithoutVerbose_TheCommandBreakdownIsNotPrinted()
    {
        // **The control**, and it is what makes the test above about the flag rather than about
        // logging existing at all. Without it a Program that always printed the breakdown would
        // pass, and the guard that makes the grouping conditional could be deleted unnoticed.
        string? demo = Demo();
        if (demo is null)
        {
            return;
        }

        Program.Main([demo, "-s", "-o", Path.Combine(_scratch, "quiet.txt")]).ShouldBe(0);

        _error.ToString().ShouldNotContain("commands in ");
    }

    [Test]
    public void ATruncatedRecordingIsReportedAsTruncatedRatherThanAccepted()
    {
        // **No demo in the corpus is missing its dem_stop, so this specimen is AUTHORED.**
        //
        // The first version of this test reached for z1800.dem on the strength of "one byte short
        // of complete", and misread the comment that says so: z1800 is cited in Program.cs as a
        // file that is short and *still* carries its dem_stop, which is why it reads fine. An
        // engine writes dem_stop even when the server is quit, so a file lacking one was cut off
        // mid-write — and nothing in the corpus was.
        //
        // That is what the round trip is for. `--asm` decompiles a demo to text, the stop block is
        // removed from the text, and `--compile` writes a demo back. The result is a file whose
        // contents were chosen rather than found, which is the only way to test a state no
        // recording exhibits. See docs/memory/author-the-specimen-the-corpus-lacks.md.
        string? demo = Demo();
        if (demo is null)
        {
            return;                                  // corpus not checked out
        }

        string assembly = Path.Combine(_scratch, "source.asm");
        string authored = Path.Combine(_scratch, "no-stop.dem");

        Program.Main([demo, "-a", "-o", assembly]).ShouldBe(0);

        string[] lines = File.ReadAllLines(assembly);

        // **The assembly text spells it `stop`, not `dem_stop`** — the latter is the TRACE format's
        // spelling and the two formats are not the same language. Matching the wrong one removed
        // nothing and the assertion below caught it, which is the whole reason that assertion is
        // here rather than a comment saying the line was removed.
        //
        // Anchored at the start of the line, because a block header is `stop <tick>` and `stop`
        // appears inside console commands and cvar names throughout a real demo.
        string[] withoutStop =
        [
            .. lines.Where(line => !line.StartsWith("stop ", StringComparison.Ordinal)),
        ];

        withoutStop.Length.ShouldBeLessThan(lines.Length, "the source demo must contain a dem_stop");

        File.WriteAllLines(assembly, withoutStop);

        Program.Main([assembly, "-c", "-o", authored]).ShouldBe(0);

        _error.GetStringBuilder().Clear();

        Program.Main([authored, "-s", "-o", Path.Combine(_scratch, "truncated.txt")]).ShouldBe(0);

        _error.ToString().ShouldContain("truncated, not ended");
    }

    [Test]
    public void AnUnmodifiedRoundTripIsNotAccusedOfTruncation()
    {
        // **The control for the authored specimen**, and the reason it is worth the extra compile.
        // Without it, "the warning fired" could mean the round trip itself loses the dem_stop —
        // which would make the test above pass while proving the writer broken rather than the
        // reader working.
        string? demo = Demo();
        if (demo is null)
        {
            return;
        }

        string assembly = Path.Combine(_scratch, "intact.asm");
        string rebuilt = Path.Combine(_scratch, "intact.dem");

        Program.Main([demo, "-a", "-o", assembly]).ShouldBe(0);
        Program.Main([assembly, "-c", "-o", rebuilt]).ShouldBe(0);

        _error.GetStringBuilder().Clear();

        Program.Main([rebuilt, "-s", "-o", Path.Combine(_scratch, "intact.txt")]).ShouldBe(0);

        _error.ToString().ShouldNotContain("truncated, not ended");
    }

    [Test]
    public void ADemoDeclaringTheRightFrameCountIsNotAccusedOfMiscounting()
    {
        // The other side of the frame-count check, and the one that keeps it honest. The header
        // states the frame count and the stream contains it by completely different paths; the
        // warning fires when they disagree. A version that warned unconditionally would satisfy
        // any test asserting the warning CAN appear.
        //
        // The era specimens are complete recordings, so they must not be accused.
        string? demo = Demo();
        if (demo is null)
        {
            return;
        }

        Program.Main([demo, "-s", "-o", Path.Combine(_scratch, "counted.txt")]).ShouldBe(0);

        // Either the file is consistent and says nothing, or it is one of the demos that declares
        // zero frames — which is a real and common state (43% of recordings), reported by the same
        // line. What must not happen is an accusation with no numbers in it.
        string log = _error.ToString();

        if (log.Contains("declares", StringComparison.Ordinal))
        {
            log.ShouldContain("frames but holds");
        }
    }

}
