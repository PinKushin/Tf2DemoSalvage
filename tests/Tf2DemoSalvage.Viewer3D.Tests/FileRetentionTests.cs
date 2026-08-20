using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Tf2DemoSalvage.Viewer3D;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// Keeping the newest N files of a kind, and leaving every other kind alone.
/// </summary>
/// <remarks>
/// **This exists because the viewer quietly wrote 929 MB to the user's disk.** Measured
/// 2026-08-19: 233 screenshots at 203 MB with no pruning at all, and 207 run logs against a stated
/// limit of 50. Neither announced itself, which is the whole problem — the owner's words were that
/// it "just saves a bunch of shit to my pc that i wont notice and will grow till my SDD is full".
///
/// Two separate defects sat behind that number and only one of them is arithmetic:
///
/// - the screenshot path had no retention code of any kind;
/// - the log prune ran BEFORE the process wrote its own file, so a UI suite or a mutation run —
///   which start many viewers at once — had every process compute its deletions from a directory
///   snapshot taken before any of the siblings had written. Each trimmed to the limit and then
///   each added one, so the final count was the limit plus however many raced.
///
/// The retention itself is one shared function rather than two copies, because a fix applied in two
/// places is two things that can drift — and these two had already drifted to the point where one
/// of them did not exist.
/// </remarks>
public sealed class FileRetentionTests
{
    private string _folder = string.Empty;

    [SetUp]
    public void CreateFolder()
    {
        _folder = Path.Combine(Path.GetTempPath(), "tf2-retention-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    [TearDown]
    public void RemoveFolder()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
            // Already gone; nothing to clean up.
        }
    }

    [Test]
    public void Keep_MoreFilesThanTheLimit_LeavesExactlyTheLimit()
    {
        // **Exactly the limit, not one under it.** The original loop ran `index <= length - keep`,
        // which deletes one file too many and leaves keep-1. That is invisible on a folder of 200
        // and is the kind of arithmetic slip a test written from the code rather than the intent
        // reproduces instead of catching.
        Write("viewer-", 60);

        FileRetention.Keep(_folder, "viewer-*.log", 50);

        Matching("viewer-*.log").Length.ShouldBe(50);
    }

    [Test]
    public void Keep_ExactlyTheLimit_DeletesNothing()
    {
        // The boundary the off-by-one lives on: at the limit there is nothing to remove, and the
        // original guard `length < keep` fell through here and deleted one.
        Write("viewer-", 50);

        FileRetention.Keep(_folder, "viewer-*.log", 50);

        Matching("viewer-*.log").Length.ShouldBe(50);
    }

    [Test]
    public void Keep_FewerFilesThanTheLimit_DeletesNothing()
    {
        Write("viewer-", 3);

        FileRetention.Keep(_folder, "viewer-*.log", 50);

        Matching("viewer-*.log").Length.ShouldBe(3);
    }

    [Test]
    public void Keep_TheFilesItKept_AreTheNEWEST()
    {
        // **Which ones survive is the point, and it is not implied by the count.** A prune that
        // kept the oldest would pass every count assertion above while throwing away the run
        // somebody is trying to read. Names are stamped, so ordinal order is chronological order.
        Write("viewer-", 10);

        FileRetention.Keep(_folder, "viewer-*.log", 3);

        Matching("viewer-*.log")
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ShouldBe(["viewer-007.log", "viewer-008.log", "viewer-009.log"]);
    }

    [Test]
    public void Keep_OtherKindsOfFile_AreNotTouched()
    {
        // **The control, and it guards a real hazard.** Logs and screenshots share one folder, so
        // a sweep written as "delete old files" would take the captures somebody pressed a key to
        // keep. This is the same mistake as pruning a shared measurement directory by a name glob
        // and deleting a neighbour's run.
        Write("viewer-", 60, ".log");
        Write("shot-", 5, ".png");

        FileRetention.Keep(_folder, "viewer-*.log", 10);

        Matching("viewer-*.log").Length.ShouldBe(10);
        Matching("shot-*.png").Length.ShouldBe(5);
    }

    [Test]
    public void Keep_RunConcurrentlyByManyWriters_StillConvergesToTheLimit()
    {
        // **The defect that actually filled the disk.** Every viewer process pruned before writing
        // its own file, so with a suite launching many at once each one computed its deletions
        // from a snapshot none of the siblings had touched yet: all trimmed to the limit, then all
        // added one.
        //
        // Written as write-then-prune, which is what the fix changes the call order to. The last
        // writer to finish sees the full set and trims it, so the folder converges however the
        // interleaving falls. Twenty writers against a limit of five is enough to fail reliably
        // against the old order.
        const int Writers = 20;
        const int Limit = 5;

        Parallel.For(0, Writers, index =>
        {
            File.WriteAllText(
                Path.Combine(
                    _folder,
                    string.Create(CultureInfo.InvariantCulture, $"viewer-{index:D3}.log")),
                "x");

            FileRetention.Keep(_folder, "viewer-*.log", Limit);
        });

        // Never MORE than the limit. Fewer is possible and acceptable under a race — two writers
        // can both decide to delete the same file — but the old code left Writers + Limit behind,
        // which is the unbounded growth this is about.
        Matching("viewer-*.log").Length.ShouldBeLessThanOrEqualTo(Limit);
    }

    [Test]
    public void Keep_AFolderThatDoesNotExist_DoesNothingRatherThanThrowing()
    {
        // Retention is tidiness. A viewer that failed to start because it could not clean up would
        // be trading the actual job for the housekeeping.
        Should.NotThrow(
            () => FileRetention.Keep(
                Path.Combine(_folder, "absent"), "viewer-*.log", 5));
    }

    /// <summary>Writes <paramref name="count"/> stamped files, oldest name first.</summary>
    private void Write(string prefix, int count, string extension = ".log")
    {
        for (int index = 0; index < count; index++)
        {
            File.WriteAllText(
                Path.Combine(
                    _folder,
                    string.Create(
                        CultureInfo.InvariantCulture, $"{prefix}{index:D3}{extension}")),
                "x");
        }
    }

    private string[] Matching(string pattern) => Directory.GetFiles(_folder, pattern);
}
