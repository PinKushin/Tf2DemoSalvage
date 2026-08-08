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

    public static TheoryData<string> CorpusFiles()
    {
        TheoryData<string> data = new();
        foreach (string path in EnumerateCorpus())
        {
            data.Add(Path.GetFileName(path));
        }

        // TheoryData may not be empty or xUnit fails the discovery rather than skipping.
        if (data.Count == 0)
        {
            data.Add("(no corpus present)");
        }

        return data;
    }

    /// <summary>
    /// The one test that fails loudly when the corpus is missing. Without it, a checkout that
    /// skipped `git lfs install` would leave every corpus test passing vacuously over pointer
    /// stubs — green, and proving nothing.
    /// </summary>
    [Fact]
    public void Corpus_IsPresent_AndNotLfsPointerStubs()
    {
        string? directory = FindCorpusDirectory();
        directory.ShouldNotBeNull("tools/corpus/demos was not found above the test binary.");

        string[] onDisk = Directory.GetFiles(directory, "*.dem");
        onDisk.ShouldNotBeEmpty();

        string[] usable = [.. EnumerateCorpus()];
        usable.Length.ShouldBe(
            onDisk.Length,
            $"{onDisk.Length - usable.Length} of {onDisk.Length} demo files are smaller than " +
            $"{SmallestPlausibleDemo} bytes, which means Git LFS content was never fetched. " +
            "Run `git lfs install && git lfs checkout`.");
    }

    [Theory]
    [MemberData(nameof(CorpusFiles))]
    public void Container_EveryCorpusDemo_WalksCleanlyAndAgreesWithItsHeader(string fileName)
    {
        string? path = EnumerateCorpus().FirstOrDefault(p => Path.GetFileName(p) == fileName);
        if (path is null)
        {
            // Absence is reported loudly and once by Corpus_IsPresent, not silently here.
            return;
        }

        byte[] bytes = File.ReadAllBytes(path!);
        DemoHeader header = DemoHeader.Parse(bytes);

        header.DemoProtocol.ShouldBe(3);
        header.NetworkProtocol.ShouldBe(24);
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
        commands.Count(c => c.Type == DemoCommandType.StringTables).ShouldBe(1);
        commands.ShouldContain(c => c.Type == DemoCommandType.Signon);
    }

    [Fact]
    public void Container_PointOfViewDemo_CarriesUserCommands()
    {
        string? path = EnumerateCorpus()
            .FirstOrDefault(p => Path.GetFileName(p).Contains("pov", StringComparison.Ordinal));
        if (path is null)
        {
            return;
        }

        byte[] bytes = File.ReadAllBytes(path!);
        List<DemoCommand> commands =
            [.. DemoCommandReader.Read(bytes.AsMemory(DemoHeader.SizeBytes))];

        // These command types appear only in POV demos. SourceTV recordings contain none, so
        // this path had no coverage at all until a POV demo joined the corpus.
        commands.ShouldContain(c => c.Type == DemoCommandType.UserCmd);
        commands.ShouldContain(c => c.Type == DemoCommandType.ConsoleCmd);
    }

    [Fact]
    public void Container_SourceTvDemo_ContainsNoUserCommands()
    {
        string? path = EnumerateCorpus()
            .FirstOrDefault(p => Path.GetFileName(p).Contains("stv", StringComparison.Ordinal));
        if (path is null)
        {
            return;
        }

        byte[] bytes = File.ReadAllBytes(path!);
        List<DemoCommand> commands =
            [.. DemoCommandReader.Read(bytes.AsMemory(DemoHeader.SizeBytes))];

        commands.ShouldNotContain(c => c.Type == DemoCommandType.UserCmd);
    }

    private static IEnumerable<string> EnumerateCorpus()
    {
        string? directory = FindCorpusDirectory();
        if (directory is null)
        {
            return [];
        }

        return Directory
            .EnumerateFiles(directory, "*.dem")
            .Where(p => new FileInfo(p).Length >= SmallestPlausibleDemo)
            .OrderBy(p => p, StringComparer.Ordinal);
    }

    /// <summary>
    /// Walks up from the test binary to the repository root, identified by the corpus directory
    /// itself. Avoids hard-coding a relative depth that breaks when the output path changes.
    /// </summary>
    private static string? FindCorpusDirectory()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);

        while (current is not null)
        {
            string candidate = Path.Combine(current.FullName, "tools", "corpus", "demos");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        return null;
    }
}
