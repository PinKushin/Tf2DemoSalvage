using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Cli;

namespace Tf2DemoSalvage.Cli.Tests;

/// <summary>
/// Tests for the verbosity flags and the level they select.
/// </summary>
/// <remarks>
/// **The property that matters most is not asserted here, because it cannot be:** every log line
/// goes to standard error, so that a trace or JSON Lines stream on standard output stays pipeable.
/// That is a property of the console provider's configuration, and it is stated in
/// <see cref="LogSetup"/> rather than tested — capturing the real console would test the framework
/// rather than this code.
/// </remarks>
public sealed class LoggingTests
{
    [TestCase("-v", Verbosity.Verbose)]
    [TestCase("--verbose", Verbosity.Verbose)]
    [TestCase("-q", Verbosity.Quiet)]
    [TestCase("--quiet", Verbosity.Quiet)]
    public void VerbosityFlags_AreParsed(string flag, Verbosity expected)
    {
        CommandLine.Parse(["demo.dem", flag]).Verbosity.ShouldBe(expected);
    }

    [Test]
    public void Parse_NoVerbosityFlag_LeavesVerbosityNormal()
    {
        CommandLine.Parse(["demo.dem"]).Verbosity.ShouldBe(Verbosity.Normal);
    }

    [Test]
    public void Parse_RepeatedVerbosityFlags_KeepsTheLast()
    {
        // Both directions, because "last wins" is only demonstrated by a pair that disagree - a
        // parser that ignored the second flag would pass a test using only one order.
        CommandLine.Parse(["demo.dem", "-q", "-v"]).Verbosity.ShouldBe(Verbosity.Verbose);
        CommandLine.Parse(["demo.dem", "-v", "-q"]).Verbosity.ShouldBe(Verbosity.Quiet);
    }

    [Test]
    public void Parse_VerbosityFlag_LeavesTheOtherOptionsAlone()
    {
        CommandLine line = CommandLine.Parse(["demo.dem", "-v", "-t", "-o", "out.txt"]);

        line.Error.ShouldBeNull();
        line.Format.ShouldBe(OutputFormat.Trace);
        line.OutputPath.ShouldBe("out.txt");
        line.Verbosity.ShouldBe(Verbosity.Verbose);
    }

    [Test]
    public void QuietStopsAtWarnings_NotAtErrors()
    {
        // Deliberate. A demo that decodes only in part is the most important thing this tool can
        // report, and a batch run over a corpus that suppressed it would claim success over files
        // it had half read. Quiet is for removing the running commentary, not the findings.
        LogSetup.LevelFor(Verbosity.Quiet).ShouldBe(LogLevel.Warning);
        LogSetup.LevelFor(Verbosity.Normal).ShouldBe(LogLevel.Information);
        LogSetup.LevelFor(Verbosity.Verbose).ShouldBe(LogLevel.Debug);
    }
}
