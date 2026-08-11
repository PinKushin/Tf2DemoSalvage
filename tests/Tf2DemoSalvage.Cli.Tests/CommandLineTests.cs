using Tf2DemoSalvage.Cli;

namespace Tf2DemoSalvage.Cli.Tests;

/// <summary>
/// The argument grammar.
/// </summary>
/// <remarks>
/// The tool's only user-facing surface, and until now the only untested one. Argument parsing
/// is where a command-line program is quietly wrong most easily: an option that consumes the
/// wrong number of arguments shifts everything after it, and a flag that silently loses to
/// another produces the wrong output with no complaint. Neither is reachable from a test that
/// only inspects the output of a successful run.
/// </remarks>
public sealed class CommandLineTests
{
    [Fact]
    public void DemoPath_IsTheFirstArgument()
    {
        CommandLine line = CommandLine.Parse(["a.dem"]);

        line.DemoPath.ShouldBe("a.dem");
        line.Error.ShouldBeNull();
        line.Format.ShouldBe(OutputFormat.Dump);
    }

    [Theory]
    [InlineData("-s", OutputFormat.Summary)]
    [InlineData("--summary", OutputFormat.Summary)]
    [InlineData("-t", OutputFormat.Trace)]
    [InlineData("--trace", OutputFormat.Trace)]
    [InlineData("-j", OutputFormat.JsonLines)]
    [InlineData("--jsonl", OutputFormat.JsonLines)]
    public void FormatFlags_SelectTheirFormat(string flag, OutputFormat expected)
    {
        CommandLine.Parse(["a.dem", flag]).Format.ShouldBe(expected);
    }

    [Fact]
    public void OutputPath_ConsumesTheFollowingArgument()
    {
        // The measurement is the flag *after* the path, not the path itself. An option that
        // failed to consume its value would leave "out.txt" to be read as an option, and the
        // trace flag behind it is what shows whether the cursor landed where it should.
        CommandLine line = CommandLine.Parse(["a.dem", "-o", "out.txt", "--trace"]);

        line.OutputPath.ShouldBe("out.txt");
        line.Format.ShouldBe(OutputFormat.Trace);
        line.Error.ShouldBeNull();
    }

    [Fact]
    public void OutputPathThatLooksLikeAFlag_IsStillTakenAsThePath()
    {
        // A value is a value. Treating "-s" here as a format flag would silently write the
        // summary to standard output instead of writing a dump to a file called "-s".
        CommandLine.Parse(["a.dem", "-o", "-s"]).OutputPath.ShouldBe("-s");
    }

    [Fact]
    public void EntityLimit_ImpliesEntities()
    {
        // Asking for a limit without asking for entities describes a run that would do nothing
        // with either. Reading it as a request for both is the interpretation that cannot be a
        // mistake.
        CommandLine line = CommandLine.Parse(["a.dem", "--trace", "--entity-limit", "5"]);

        line.EntitySnapshotLimit.ShouldBe(5);
        line.IncludeEntities.ShouldBeTrue();
    }

    [Fact]
    public void Entities_CanBeAskedForWithoutALimit()
    {
        CommandLine line = CommandLine.Parse(["a.dem", "-t", "-e"]);

        line.IncludeEntities.ShouldBeTrue();
        line.EntitySnapshotLimit.ShouldBe(0);
    }

    [Theory]
    [InlineData(new[] { "a.dem", "-o" }, "-o requires a path")]
    [InlineData(new[] { "a.dem", "--entity-limit" }, "--entity-limit requires a count")]
    [InlineData(new[] { "a.dem", "-x" }, "unrecognised option '-x'")]
    public void MalformedArguments_AreReportedRatherThanIgnored(string[] args, string expected)
    {
        CommandLine.Parse(args).Error.ShouldBe(expected);
    }

    [Theory]
    [InlineData("nonsense")]
    [InlineData("-3")]
    public void EntityLimit_RejectsWhatIsNotACount(string value)
    {
        // Negative and non-numeric both, because a limit is used as a comparison bound - a
        // negative one would silently expand nothing while looking like it asked for something.
        CommandLine.Parse(["a.dem", "--entity-limit", value]).Error.ShouldNotBeNull();
    }

    [Fact]
    public void NoArguments_IsAnError()
    {
        CommandLine.Parse([]).Error.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("-h")]
    [InlineData("--help")]
    [InlineData("/?")]
    public void HelpFlags_AreRecognisedAndAreNotErrors(string flag)
    {
        CommandLine line = CommandLine.Parse([flag]);

        line.HelpRequested.ShouldBeTrue();
        line.Error.ShouldBeNull();
    }

    [Fact]
    public void LastFormatFlagWins()
    {
        // Documenting the rule rather than leaving it to whichever branch happens to run last.
        CommandLine.Parse(["a.dem", "-t", "-s"]).Format.ShouldBe(OutputFormat.Summary);
        CommandLine.Parse(["a.dem", "-s", "-t"]).Format.ShouldBe(OutputFormat.Trace);
    }

    [Fact]
    public void WithNoFlags_EntitiesAreOffAndUnlimited()
    {
        // The defaults, asserted rather than assumed. Mutation testing found this: flipping
        // the initial value of `entities` to true survived the entire suite, because every
        // test that cared about entities had asked for them.
        CommandLine line = CommandLine.Parse(["a.dem"]);

        line.IncludeEntities.ShouldBeFalse();
        line.EntitySnapshotLimit.ShouldBe(0);
        line.OutputPath.ShouldBeNull();
        line.HelpRequested.ShouldBeFalse();
    }

    [Fact]
    public void EntityLimitOfZero_IsValidAndMeansAll()
    {
        // Zero is the documented "no limit" value, so rejecting it is a bug rather than
        // strictness. Mutation testing found this too: changing the guard from `limit < 0` to
        // `limit <= 0` survived, because no test had ever passed zero.
        CommandLine line = CommandLine.Parse(["a.dem", "--entity-limit", "0"]);

        line.Error.ShouldBeNull();
        line.EntitySnapshotLimit.ShouldBe(0);
        line.IncludeEntities.ShouldBeTrue();
    }

    [Theory]
    [InlineData("--output")]
    [InlineData("-o")]
    public void BothOutputSpellings_Work(string flag)
    {
        CommandLine.Parse(["a.dem", flag, "out.txt"]).OutputPath.ShouldBe("out.txt");
    }

    [Theory]
    [InlineData("--entities")]
    [InlineData("-e")]
    public void BothEntitySpellings_Work(string flag)
    {
        CommandLine.Parse(["a.dem", flag]).IncludeEntities.ShouldBeTrue();
    }

    [Fact]
    public void ErrorText_NamesWhatWasWrong()
    {
        // The messages are the only thing a user sees when a command line is rejected, so they
        // are behaviour. Every one of these survived mutation to an empty string.
        CommandLine.Parse([]).Error.ShouldBe("no demo given");
        CommandLine.Parse(["a.dem", "--entity-limit", "nope"]).Error
            .ShouldBe("--entity-limit needs a non-negative number, not 'nope'");
    }

    [Fact]
    public void AnInvalidCommandLine_HasNoDemoPathToActOn()
    {
        // Error and DemoPath are checked together deliberately: a caller that tested only for
        // a null Error would otherwise be handed a path-shaped value to open.
        CommandLine line = CommandLine.Parse(["a.dem", "-x"]);

        line.Error.ShouldNotBeNull();
        line.DemoPath.ShouldBeEmpty();
    }

    [Fact]
    public void HelpDoesNotCarryADemoPath()
    {
        CommandLine.Parse(["--help"]).DemoPath.ShouldBeEmpty();
    }


    [Theory]
    [InlineData("-a")]
    [InlineData("--asm")]
    public void AsmFlag_SelectsTheAssemblyFormat(string flag)
    {
        CommandLine.Parse(["demo.dem", flag]).Format.ShouldBe(OutputFormat.Assembly);
    }

    [Fact]
    public void Compile_WithoutAnOutputPath_IsRefused()
    {
        // A demo is binary and standard output is text. Allowing it would write a file that looks
        // plausible in a terminal and does not play - a failure with no error message, which is
        // the worst shape a CLI mistake can take.
        CommandLine line = CommandLine.Parse(["demo.dasm", "-c"]);

        line.Error.ShouldNotBeNull();
        line.Error.ShouldContain("-o");
    }

    [Fact]
    public void Compile_WithAnOutputPath_IsAccepted()
    {
        CommandLine line = CommandLine.Parse(["demo.dasm", "--compile", "-o", "out.dem"]);

        line.Error.ShouldBeNull();
        line.Compile.ShouldBeTrue();
        line.OutputPath.ShouldBe("out.dem");
    }
}
