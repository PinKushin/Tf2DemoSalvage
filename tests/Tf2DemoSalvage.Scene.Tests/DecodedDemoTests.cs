using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Core.Container;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// Reading a demo and building its timeline, and which of the two a failure is allowed to cost.
/// </summary>
/// <remarks>
/// **The two failures are deliberately different shapes, and that is the whole of what is worth
/// pinning here.** A demo whose TIMELINE will not build still has a header, a map name and a length
/// worth showing, so that failure is caught and costs the player positions alone. A file that is not
/// a demo has nothing left to show, so it throws and the caller decides what to say.
///
/// This lived in <c>MainForm</c> as a private record and a static <c>Decode</c> (B188, D90), where
/// nothing tested it — it was already form-free, so the only thing keeping it untested was the file
/// it sat in.
///
/// **Built from an AUTHORED demo rather than from the corpus, and that is the stronger instrument
/// of the two.** A corpus test skips when the corpus is absent and takes minutes when it is not, so
/// it kills no mutants — and more importantly a real demo cannot isolate the case that matters here,
/// because every real demo carries a schema. Writing one that deliberately does NOT is what puts the
/// header on one side of the guard and the timeline on the other
/// (<c>docs/memory/author-the-specimen-the-corpus-lacks.md</c>).
///
/// <c>DemoWriter</c> is production code, so the specimen costs nothing to keep. An end-to-end
/// assertion over a real demo lives in <c>CorpusDecodedDemoTests</c> as well — it catches what a
/// synthetic one cannot, a roster that decodes to nothing — but the load is carried here.
/// </remarks>
public sealed class DecodedDemoTests
{
    [Test]
    public void Read_OnADemoWithNoSchema_KeepsTheHeaderAndAnEmptyTimeline()
    {
        // **A schemaless demo is not a failed one, and finding that out corrected the test rather
        // than the code.** The first version of this predicted a null timeline, on the assumption
        // that no `dem_datatables` means nothing to build from. It does not: `DemoTimeline.Build`
        // answers an EMPTY timeline, which is the honest result for a recording with no entities in
        // it — so the guard's catch was never on this path and asserting null measured a behaviour
        // that does not exist.
        string path = Temporary(SchemalessDemo("cp_process_final"));

        try
        {
            DecodedDemo decoded = DecodedDemo.Read(path, NullLogger.Instance);

            decoded.Demo.MapName.ShouldBe("cp_process_final");
            decoded.Timeline.ShouldNotBeNull();
            decoded.Timeline.Frames.ShouldBeEmpty();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void Read_OnADemoWhoseSchemaIsCorrupt_KeepsTheHeaderAndAnswersNoTimeline()
    {
        // **The pair the guard exists for, and no real demo can express it.** A file whose schema
        // will not parse still has a header, a map name and a length worth showing, so the timeline
        // failing must cost the player positions and NOTHING else. Asserting only that the timeline
        // is null would pass against a decode that returned nothing at all, which is why both halves
        // are measured in one test.
        //
        // **This does NOT bless throwing as a decode path, and the distinction matters.** The
        // owner's rule: "build should basically never throw any exceptions, we just read bytes, turn
        // them into quake script, and compile that script back to a byte identical demo". Decoding a
        // real demo must be total — a throw on one is our defect, not an expected outcome
        // (`docs/memory/decode-must-be-total.md`). The input here is eight bytes of deliberate
        // garbage that no demo TF2 wrote contains, so what is pinned is the BACKSTOP: when something
        // does escape, the header survives it. A guard that should never fire still has to work.
        string path = Temporary(CorruptSchemaDemo("cp_process_final"));

        try
        {
            DecodedDemo decoded = DecodedDemo.Read(path, NullLogger.Instance);

            decoded.Demo.MapName.ShouldBe("cp_process_final");
            decoded.Timeline.ShouldBeNull();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void Read_OnADemoWhoseSchemaIsCorrupt_ReportsTheHeaderAndWarnsAboutTheTimeline()
    {
        // **The two are logged at different levels because they are different events.** The header
        // is what opened and belongs in a release run; the timeline failing is a defect in the file
        // and is a warning. A decode that reported neither would look identical from the outside to
        // one that worked.
        string path = Temporary(CorruptSchemaDemo("koth_viaduct"));
        RecordingLogger log = new();

        try
        {
            DecodedDemo.Read(path, log);
        }
        finally
        {
            File.Delete(path);
        }

        log.Lines
            .Where(line => line.Message.Contains("koth_viaduct", StringComparison.Ordinal))
            .Select(line => line.Level)
            .ShouldAllBe(level => level == LogLevel.Information);

        log.Lines
            .Where(line => line.Message.Contains("position timeline", StringComparison.Ordinal))
            .Select(line => line.Level)
            .ShouldContain(LogLevel.Warning);
    }

    [Test]
    public void Read_WithBytesThatAreNotADemo_Throws()
    {
        // **Throws rather than answering an empty demo**, which is the distinction that matters: a
        // caller cannot tell an empty demo from a short one, and the viewer would open a file that
        // is not a demo and show nothing while reporting success.
        string path = Temporary(new byte[64]);

        try
        {
            Should.Throw<Exception>(() => DecodedDemo.Read(path, NullLogger.Instance));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void Read_WithAMissingFile_Throws()
    {
        // The ordinary case behind "the playlist points at a file that has been moved".
        Should.Throw<Exception>(() =>
            DecodedDemo.Read(
                Path.Combine(Path.GetTempPath(), "no-such-demo-4f8c1e.dem"), NullLogger.Instance));
    }

    [Test]
    public void Read_WithNoLogger_Refuses()
    {
        // Everything this method finds is reported rather than returned, so a null sink is a caller
        // mistake rather than a quiet mode.
        Should.Throw<ArgumentNullException>(() => DecodedDemo.Read("anything.dem", demo: null!));
    }

    [Test]
    public void Read_WithNoPath_Refuses()
    {
        Should.Throw<ArgumentNullException>(() => DecodedDemo.Read(null!, NullLogger.Instance));
    }

    /// <summary>Writes bytes to a temporary file and answers its path.</summary>
    private static string Temporary(byte[] bytes)
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".dem");

        File.WriteAllBytes(path, bytes);

        return path;
    }

    /// <summary>A structurally valid demo that carries no entity schema.</summary>
    /// <remarks>
    /// **Authored through <see cref="DemoWriter"/>, which is production code**, so this is the
    /// container's own idea of a demo rather than bytes assembled by the test. A specimen built by
    /// hand from a reading of the format would prove only that the two readings agree.
    ///
    /// One <c>dem_stop</c> and nothing else: the header is complete and parses, and there is no
    /// <c>dem_datatables</c> — which produces an EMPTY timeline rather than a failed one, the fact
    /// that corrected the first version of these tests.
    /// </remarks>
    private static byte[] SchemalessDemo(string map) => Demo(map, []);

    /// <summary>A demo carrying a <c>dem_datatables</c> the schema reader cannot parse.</summary>
    /// <remarks>
    /// **This is the input that separates the guard's two sides**, and it is one no real file
    /// contains — every demo TF2 wrote carries a schema that parses. Authoring it is the whole
    /// argument for a synthetic specimen over a corpus one: a corpus test cannot reach this branch
    /// at all, so it can never kill a mutant in it
    /// (<c>docs/memory/a-faithful-fixture-can-be-blind.md</c>).
    /// </remarks>
    private static byte[] CorruptSchemaDemo(string map) =>
        Demo(
            map,
            [
                new DemoCommand(
                    DemoCommandType.DataTables,
                    Tick: 0,
                    Payload: new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x7F, 0x7F, 0x7F, 0x7F }),
            ]);

    /// <summary>A demo with the given commands, terminated properly.</summary>
    private static byte[] Demo(string map, IReadOnlyList<DemoCommand> commands) =>
        DemoWriter.Write(
            Header(map),
            [
                .. commands,
                new DemoCommand(DemoCommandType.Stop, Tick: 0, Payload: ReadOnlyMemory<byte>.Empty),
            ]);

    /// <summary>A complete, parseable header.</summary>
    private static DemoHeader Header(string map) =>
        new()
        {
            DemoProtocol = 3,
            NetworkProtocol = 24,
            ServerName = "a synthetic specimen",
            ClientName = "DecodedDemoTests",
            MapName = map,
            GameDirectory = "tf",
            PlaybackTimeSeconds = 0f,
            PlaybackTicks = 0,
            PlaybackFrames = 0,
            SignonLengthBytes = 0,
        };
}
