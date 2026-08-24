using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Logging;

namespace Tf2DemoSalvage.Logging.Tests;

/// <summary>
/// The file sink, whose every behaviour was paid for by a defect in the logger it replaces.
/// </summary>
/// <remarks>
/// **These are regression tests before they are unit tests (D83).** `ViewerLog` was hand-rolled and
/// accumulated its guarantees one bug at a time — a per-line open-and-close that turned a chatty
/// warning into a 37 MB file, retention that raced its own siblings into 207 files against a limit
/// of 50, and IO failures that had to cost their lines and nothing else. Converting to `ILogger`
/// keeps all of it, so all of it is asserted here rather than trusted to survive a move.
/// </remarks>
public sealed class FileLoggerProviderTests
{
    private string _folder = string.Empty;

    /// <summary>Reads the log while the writer still holds it.</summary>
    /// <remarks>
    /// **`File.ReadAllLines` cannot do this, and finding out was the point.** It opens with
    /// `FileShare.Read`, which refuses to co-exist with the writer's `FileAccess.Write` handle — so
    /// every test here failed with "the process cannot access the file" until the reader asked for
    /// `FileShare.ReadWrite` too.
    ///
    /// That is not a test artefact. Reading the log of a RUNNING viewer is the normal case: the UI
    /// suite counts lines in it to decide what the viewer did, and diagnosing a live session means
    /// tailing it. A reader this strict would have made both impossible while proving nothing.
    /// </remarks>
    private static string[] ReadLines(string path)
    {
        using FileStream file = new(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        using StreamReader reader = new(file);

        List<string> lines = [];

        while (reader.ReadLine() is { } line)
        {
            lines.Add(line);
        }

        return [.. lines];
    }

    [SetUp]
    public void CreateFolder()
    {
        _folder = Path.Combine(Path.GetTempPath(), $"tf2ds-log-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_folder);
    }

    [TearDown]
    public void RemoveFolder()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            // A held handle on a Windows agent is not a test failure. The folder is under the
            // system temp directory and the OS will take it.
        }
    }

    [Test]
    public void Log_AnInformationLine_MatchesTheFormatViewerLogWrote()
    {
        // **Byte-identical output is a REQUIREMENT, not a courtesy.** `ViewerSession` in the UI
        // suite counts occurrences of literal substrings in this file to decide whether the viewer
        // did something, and several of this project's diagnostics are greps over it. A format
        // change would break those silently — they would simply count zero.
        using FileLoggerProvider provider = new(_folder, "viewer", banner: "TF2 Demo Salvage test");

        provider.CreateLogger("assets").LogInformation("entity palette: 673 classes");

        string line = ReadLines(provider.Path)[^1];

        // HH:mm:ss.fff, five columns of level, then [area] and the message.
        line.ShouldMatch(@"^\d{2}:\d{2}:\d{2}\.\d{3}      \[assets\] entity palette: 673 classes$");
    }

    [Test]
    public void Log_AWarning_UsesTheSameFiveColumnLevelAsBefore()
    {
        using FileLoggerProvider provider = new(_folder, "viewer");

        provider.CreateLogger("render").LogWarning("shader would not compile");

        ReadLines(provider.Path)[^1]
            .ShouldMatch(@"^\d{2}:\d{2}:\d{2}\.\d{3} WARN \[render\] shader would not compile$");
    }

    [Test]
    public void Log_AnException_RecordsItsTypeAndMessageButNotItsStack()
    {
        // `ViewerLog.Warn(area, what, failure)` wrote exactly this shape. These are exceptions that
        // were CAUGHT and handled, so which one and what happened instead is the useful part; a
        // stack per handled fallback buries the log it is meant to make readable.
        using FileLoggerProvider provider = new(_folder, "viewer");

        provider.CreateLogger("props").LogWarning(
            new InvalidDataException("checksum 818143611 does not match"),
            "reading sp_510.vhv");

        string line = ReadLines(provider.Path)[^1];

        line.ShouldContain("reading sp_510.vhv: InvalidDataException: checksum 818143611 does not match");
        line.ShouldNotContain("   at ");
    }

    [Test]
    public void CreateLogger_TheSameCategoryTwice_IsTheSameLogger()
    {
        // Categories are created per call site in a hot path; handing back a new object each time
        // would allocate one per frame in the renderer.
        using FileLoggerProvider provider = new(_folder, "viewer");

        provider.CreateLogger("render").ShouldBeSameAs(provider.CreateLogger("render"));
        provider.CreateLogger("render").ShouldNotBeSameAs(provider.CreateLogger("assets"));
    }

    [Test]
    public void Path_ARunsLog_IsStampedAndCarriesTheProcessId()
    {
        // **The process id is in the name because more than one viewer can be alive.** A UI suite
        // launches one per fixture, and anything reading "the newest log" then gets whichever
        // instance wrote last — the stamp alone cannot separate two started in the same second.
        //
        // Asserted as a NAMING RULE rather than by making two providers and comparing. Two in one
        // process within the same second genuinely would collide, and the honest way to say that is
        // that the id separates PROCESSES; sleeping a second to dodge it would be testing the clock.
        using FileLoggerProvider provider = new(_folder, "viewer");

        Path.GetFileName(provider.Path).ShouldMatch(
            $@"^viewer-\d{{8}}-\d{{6}}-{Environment.ProcessId.ToString(CultureInfo.InvariantCulture)}\.log$");
    }

    [Test]
    public void Log_ManyLines_KeepsOneHandleRatherThanReopening()
    {
        // The behaviour this asserts is "the file is readable WHILE open", which an
        // open-append-close writer also satisfies — so this is not sensitive to the fix on its own.
        // What it does catch is a writer that never flushes: AutoFlush is on precisely because this
        // project debugs by log and a buffered tail lost in a crash is the part worth having.
        using FileLoggerProvider provider = new(_folder, "viewer");

        ILogger logger = provider.CreateLogger("render");

        for (int at = 0; at < 500; at++)
        {
            logger.LogInformation("a frame was drawn");
        }

        ReadLines(provider.Path).Length.ShouldBe(500, "every line must be flushed as written");
    }

    [Test]
    public void Log_WhenTheFolderCannotBeWritten_CostsItsLinesAndNothingElse()
    {
        // **The priority this whole type is built around.** A viewer that refuses to open a demo
        // because it cannot write a log has it backwards. A file path pointing at an existing FILE
        // rather than a directory is the cheapest way to make the open fail on every platform.
        string blocked = Path.Combine(_folder, "not-a-directory");

        File.WriteAllText(blocked, "this is a file, so creating a directory here fails");

        using FileLoggerProvider provider = new(blocked, "viewer");

        provider.Failed.ShouldBeTrue("the folder could not be created, so writing was abandoned");

        // The assertion is that this does not throw.
        provider.CreateLogger("assets").LogWarning("something the viewer still has to survive");
    }

    [Test]
    public void Time_AnOperation_LogsHowLongItTook()
    {
        using FileLoggerProvider provider = new(_folder, "viewer");

        // Disposed immediately: the elapsed time is not what is under test, the LINE is. Sleeping
        // to make a bigger number would be testing the clock, and this project bans a sleep in a
        // test for exactly that reason.
        provider.CreateLogger("map").Time("loading cp_process_f12.bsp").Dispose();

        ReadLines(provider.Path)[^1]
            .ShouldMatch(@"\[map\] loading cp_process_f12\.bsp took \d+\.\d{2}s$");
    }

    [Test]
    public void IsEnabled_DebugAndTrace_AreOffSoAPerFrameLineCannotFloodTheFile()
    {
        // Measured on the logger this replaces: one five-minute run wrote 450,157 lines, 440,412 of
        // them the same per-frame warning. The warning was a real defect and was fixed, but a sink
        // that accepts Trace from a renderer will meet the next one.
        using FileLoggerProvider provider = new(_folder, "viewer");

        ILogger logger = provider.CreateLogger("render");

        logger.IsEnabled(LogLevel.Trace).ShouldBeFalse();
        logger.IsEnabled(LogLevel.Debug).ShouldBeFalse();
        logger.IsEnabled(LogLevel.Information).ShouldBeTrue();
        logger.IsEnabled(LogLevel.Warning).ShouldBeTrue();

        logger.LogDebug("a per-frame detail");

        ReadLines(provider.Path).ShouldBeEmpty("nothing below Information reaches the file");
    }
}
