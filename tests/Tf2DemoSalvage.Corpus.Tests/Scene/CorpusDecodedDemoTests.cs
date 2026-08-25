using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Scene;

// The corpus helper is `Tf2DemoSalvage.Core.Tests.Corpus`, and every other suite in this project
// sits under that namespace for the same reason. Not `...Tests.Scene`, which would shadow the
// `Tf2DemoSalvage.Scene` namespace this file needs.
namespace Tf2DemoSalvage.Core.Tests.SceneDecode;

/// <summary>
/// Decoding a real demo end to end, and the report it writes while doing it.
/// </summary>
/// <remarks>
/// **The assertion the unit tests cannot make.** <c>DecodedDemoTests</c> pins the two failure
/// shapes, which needs no demo — but a decode that returns a header with an empty timeline, or one
/// whose report says "0 red, 0 blu" for a match with twelve players, passes every one of them. Only
/// a real file can tell.
///
/// This is the rule in <c>CLAUDE.md</c> applied to a move rather than to a feature: "Anything that
/// produces output is not done until an assertion has read that output on a real demo." The output
/// here is both the record and the log — the load report is what makes a later "why is nothing
/// drawing" answerable at all, and it has been silently lost before (B191's neighbours).
/// </remarks>
public sealed class CorpusDecodedDemoTests
{
    [Test]
    public void Read_OnACorpusDemo_AnswersAHeaderAndATimeline()
    {
        // **A demo the committed corpus always has**, so this runs under TF2DEMOSALVAGE_GCOR_ONLY
        // rather than skipping in CI and testing nothing there.
        string path = Corpus.Demo(AnyEraDemo);

        RecordingLogger log = new();

        DecodedDemo decoded = DecodedDemo.Read(path, log);

        decoded.Demo.MapName.ShouldNotBeNullOrWhiteSpace();

        // **Predicted as "more than none" rather than an exact count**, because the count is a
        // property of whichever specimen the corpus offers and would be a change-detector. What is
        // being measured is that the timeline was BUILT — the guard around it swallows a failure and
        // answers null, which is exactly the silent outcome worth catching.
        decoded.Timeline.ShouldNotBeNull("the timeline guard swallows failures and answers null");
        decoded.Timeline.Frames.ShouldNotBeEmpty();
    }

    [Test]
    public void Read_OnACorpusDemo_ReportsTheMapTheTickRateAndTheRoster()
    {
        // **Each line answers a different question, which is why all four are named.** The map and
        // tick count say the header parsed; the interval says whether the demo stated its own tick
        // rate or fell back to the engine default; the roster is what a colour or class defect looks
        // like from the outside — "0 red, 0 blu" the moment the file opens, rather than grey dots
        // that have to be noticed and then chased through a suite.
        RecordingLogger log = new();

        DecodedDemo.Read(Corpus.Demo(AnyEraDemo), log);

        log.Count("opening ").ShouldBe(1);
        log.Count("recorded moments").ShouldBe(1);
        log.Count("s per tick").ShouldBe(1);
        log.Count("roster:").ShouldBe(1);
        log.Count("players drawn at the midpoint").ShouldBe(1);
    }

    [Test]
    public void Read_OnACorpusDemo_ReportsAtInformationSoAReleaseRunKeepsIt()
    {
        // **The counterpart to the per-frame lines that went to Debug.** B191 was per-frame
        // diagnostics at Information reaching a per-line disk flush; the fix was to silence the
        // lines that repeat, NOT the ones that happen once. A load report costs five lines a demo
        // and is the record of what opened, so it must survive `developer 0`.
        RecordingLogger log = new();

        DecodedDemo.Read(Corpus.Demo(AnyEraDemo), log);

        log.Lines
            .Where(line => line.Message.Contains("recorded moments", StringComparison.Ordinal))
            .Select(line => line.Level)
            .ShouldAllBe(level => level == LogLevel.Information);
    }

    /// <summary>A demo present in the committed corpus, so CI runs these rather than skipping.</summary>
    private const string AnyEraDemo = "badlands";

    /// <summary>Keeps what it was told, so a diagnostic can be asserted rather than assumed.</summary>
    /// <remarks>
    /// A local copy rather than a reference to <c>Scene.Tests</c>: a test project referencing
    /// another test project drags its whole fixture set into this assembly's discovery, which would
    /// break the exact per-project counts <c>build/assert-test-count.sh</c> depends on.
    /// </remarks>
    private sealed class RecordingLogger : ILogger
    {
        private readonly List<(LogLevel Level, string Message)> _lines = [];

        public IReadOnlyList<(LogLevel Level, string Message)> Lines => _lines;

        public int Count(string fragment) =>
            _lines.Count(line => line.Message.Contains(fragment, StringComparison.Ordinal));

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        /// <summary>Always enabled, so the question is whether a line was WRITTEN.</summary>
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            _lines.Add((logLevel, formatter(state, exception)));
        }
    }
}
