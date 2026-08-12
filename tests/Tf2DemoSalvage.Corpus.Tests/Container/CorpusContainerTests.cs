using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Tf2DemoSalvage.Core.Container;

namespace Tf2DemoSalvage.Core.Tests.Container;

/// <summary>
/// Golden-corpus regression tests (D5/D6): the container parser run against every real demo we
/// have, rather than only synthetic fixtures.
/// </summary>
/// <remarks>
/// The unit tests prove the reader handles the shapes we thought of. These prove it handles
/// files TF2 actually wrote — which is how the short <c>dem_stop</c> terminator and the
/// point-of-view-only command types were found in the first place.
///
/// Demo files live in Git LFS. Without <c>git lfs install</c> they check out as ~130-byte
/// pointer stubs, so these tests skip rather than fail confusingly — a pointer stub is a
/// missing file, not a parser bug.
/// </remarks>
public sealed class CorpusContainerTests
{
    /// <summary>Anything this small is an LFS pointer, not a demo.</summary>
    private const int SmallestPlausibleDemo = 4096;

    /// <summary>Every corpus demo's file name, as NUnit test cases.</summary>
    /// <returns>One case per demo, or a single placeholder when the corpus is absent.</returns>
    /// <remarks>
    /// A string parameter needs no wrapping - unlike an array, it is not mistaken for an argument
    /// list. The empty case still matters though: a source yielding nothing leaves the test
    /// showing as not-run rather than failing, which is the same silent pass an absent corpus
    /// produced under xUnit.
    /// </remarks>
    public static IEnumerable<string> CorpusFiles()
    {
        List<string> names = [];
        foreach (string path in EnumerateCorpus())
        {
            names.Add(Path.GetFileName(path));
        }

        if (names.Count == 0)
        {
            names.Add("(no corpus present)");
        }

        return names;
    }

    /// <summary>
    /// The one test that fails loudly when the corpus is missing. Without it, a checkout that
    /// skipped `git lfs install` would leave every corpus test passing vacuously over pointer
    /// stubs — green, and proving nothing.
    /// </summary>
    [Test]
    public void Corpus_IsPresent_AndNotLfsPointerStubs()
    {
        string? directory = FindCorpusDirectory();
        directory.ShouldNotBeNull("tools/corpus/demos was not found above the test binary.");

        // Both sides scoped to the COMMITTED corpus. This compared against every file the loader
        // returns, which since local demos joined includes tools/corpus/local — so it measured
        // four files against ten and reported "-6 of 4 are smaller than 4096 bytes". The question
        // is whether the LFS content arrived for the tracked demos; local files are not tracked
        // and cannot answer it.
        string[] onDisk = Directory.GetFiles(directory, "*.dem");
        onDisk.ShouldNotBeEmpty();

        string[] usable =
            [.. onDisk.Where(p => new FileInfo(p).Length >= SmallestPlausibleDemo)];
        usable.Length.ShouldBe(
            onDisk.Length,
            $"{onDisk.Length - usable.Length} of {onDisk.Length} demo files are smaller than " +
            $"{SmallestPlausibleDemo} bytes, which means Git LFS content was never fetched. " +
            "Run `git lfs install && git lfs checkout`.");
    }
    [TestCaseSource(nameof(CorpusFiles))]
    public void Container_EveryCorpusDemo_WalksCleanlyAndAgreesWithItsHeader(string fileName)
    {
        string? path = EnumerateCorpus().FirstOrDefault(p => Path.GetFileName(p) == fileName);
        if (path is null)
        {
            // Absence is reported loudly and once by Corpus_IsPresent, not silently here.
            return;
        }

        byte[] bytes = File.ReadAllBytes(path);
        DemoHeader header = DemoHeader.Parse(bytes);

        header.DemoProtocol.ShouldBe(3);

        // Not pinned to 24. The corpus was entirely protocol 24 until a demo recorded on the
        // June 2009 client was added, and pinning it was an assumption that every demo is
        // modern - exactly the assumption this project exists to avoid. The real invariant is
        // that the protocol is one this parser knows how to read.
        header.NetworkProtocol.ShouldBeOneOf(11, 14, 15, 16, 24);
        header.GameDirectory.ShouldBe("tf");
        header.MapName.ShouldNotBeNullOrWhiteSpace();

        List<DemoCommand> commands =
            [.. DemoCommandReader.Read(bytes.AsMemory(DemoHeader.SizeBytes))];

        // The strongest available check that the whole container was walked correctly: an
        // off-by-one in any payload size would drift and never land exactly on the header's
        // declared frame count.
        commands.Count(c => c.Type == DemoCommandType.Packet).ShouldBe(header.PlaybackFrames);

        // Every TF2 demo ends with dem_stop, and its tick should match the declared total.
        commands[^1].Type.ShouldBe(DemoCommandType.Stop);
        commands[^1].Tick.ShouldBe(header.PlaybackTicks);

        commands.Count(c => c.Type == DemoCommandType.DataTables).ShouldBe(1);

        // dem_stringtables is NOT universal, and this assertion used to say it was. The
        // protocol-14 demo carries none at all: at that era the tables arrive only as
        // svc_CreateStringTable inside the signon stream, and the separate container command
        // does not exist. Both the 2009 (protocol 15) and modern demos carry exactly one.
        //
        // Absent from proto_version.h, like the message type width (B17) and the SendPropType
        // renumbering (B18). Worth asserting in both directions rather than relaxing to
        // "zero or one": the count is a fact about the era, and a modern demo that stopped
        // carrying the command would be a real regression this must still catch.
        int expectedStringTables = header.NetworkProtocol > 14 ? 1 : 0;
        commands.Count(c => c.Type == DemoCommandType.StringTables)
            .ShouldBe(expectedStringTables, $"protocol {header.NetworkProtocol}");

        commands.ShouldContain(c => c.Type == DemoCommandType.Signon);
    }

    [Test]
    public void Container_PointOfViewDemo_CarriesUserCommands()
    {
        string? path = EnumerateCorpus()
            .FirstOrDefault(p => Path.GetFileName(p).Contains("pov", StringComparison.Ordinal));
        if (path is null)
        {
            return;
        }

        byte[] bytes = File.ReadAllBytes(path);
        List<DemoCommand> commands =
            [.. DemoCommandReader.Read(bytes.AsMemory(DemoHeader.SizeBytes))];

        // These command types appear only in POV demos. SourceTV recordings contain none, so
        // this path had no coverage at all until a POV demo joined the corpus.
        commands.ShouldContain(c => c.Type == DemoCommandType.UserCmd);
        commands.ShouldContain(c => c.Type == DemoCommandType.ConsoleCmd);
    }

    [Test]
    public void Container_SourceTvDemo_ContainsNoUserCommands()
    {
        string? path = EnumerateCorpus()
            .FirstOrDefault(p => Path.GetFileName(p).Contains("stv", StringComparison.Ordinal));
        if (path is null)
        {
            return;
        }

        byte[] bytes = File.ReadAllBytes(path);
        List<DemoCommand> commands =
            [.. DemoCommandReader.Read(bytes.AsMemory(DemoHeader.SizeBytes))];

        commands.ShouldNotContain(c => c.Type == DemoCommandType.UserCmd);
    }

    /// <summary>
    /// Delegates to <see cref="Corpus"/> rather than locating the demos again.
    /// </summary>
    /// <remarks>
    /// This file used to carry its own copy of the walk-up search and the stub filter. The copies
    /// agreed until `Corpus` learned to include `tools/corpus/local/`, and then this one silently
    /// did not: every `[Fact]` here saw the extra demos and the `[Theory]` did not, so the suite
    /// reported the same case count while taking three times as long. Two implementations of
    /// "where are the demos" is one too many.
    /// </remarks>
    private static IEnumerable<string> EnumerateCorpus() => Corpus.Files();

    private static string? FindCorpusDirectory() => Corpus.Directory();
}
