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
}
