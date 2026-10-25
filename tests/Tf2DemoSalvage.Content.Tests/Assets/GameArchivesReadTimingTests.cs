using System;
using System.IO;
using System.Threading;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary><see cref="GameArchives.ThreadReads"/>: the per-thread read counter <c>--measure</c> carries per slow frame (B595 part 5).</summary>
public sealed class GameArchivesReadTimingTests
{
    private string _folder = string.Empty;

    [SetUp]
    public void CreateFolder()
    {
        _folder = Path.Combine(Path.GetTempPath(), "gamearchives-reads-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_folder, "resource"));
        File.WriteAllBytes(Path.Combine(_folder, "resource", "a.res"), [1]);
    }

    [TearDown]
    public void DeleteFolder() => Directory.Delete(_folder, recursive: true);

    [Test]
    public void ThreadReads_ThreeReadsOnThisThread_CountsThreeAndIsNotSeenByAnotherThread()
    {
        GameArchives archives = GameArchives.Open(_folder);
        ArchiveReadTiming before = GameArchives.ThreadReads;

        archives.Read("resource/a.res").ShouldNotBeNull("the control: the file is there");
        archives.Read("resource/missing.res").ShouldBeNull();
        archives.Read("resource/a.res");

        ArchiveReadTiming other = default;
        Thread thread = new(() => other = GameArchives.ThreadReads);
        thread.Start();
        thread.Join();

        (GameArchives.ThreadReads - before).Reads.ShouldBe(3);
        (GameArchives.ThreadReads - before).Ticks.ShouldBeGreaterThan(0);
        other.Reads.ShouldBe(0);
    }

    [Test]
    public void ProcessPageFaults_OnWindows_IsPositive()
    {
        Assume.That(OperatingSystem.IsWindows());

        ArchiveReadTiming.ProcessPageFaults().ShouldBeGreaterThan(0);
    }
}
