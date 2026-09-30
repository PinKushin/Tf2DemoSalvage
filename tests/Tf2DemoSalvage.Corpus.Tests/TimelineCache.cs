using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Core.Tests;

/// <summary>
/// Builds a demo's timeline once and hands the same one to every test that asks for it while it is
/// kept — and keeps only the last <see cref="Kept"/>.
/// </summary>
/// <remarks>
/// **The single largest cost in this suite, and it is not a testing question.** Measured
/// 2026-08-19 from <c>corpus.trx</c>: the suite spends 369 seconds of CPU across 138 results, and
/// roughly 200 of those are ten tests calling <see cref="DemoTimeline.Build"/> on the same handful
/// of files. Each call re-reads the demo, re-parses the schema, and re-walks every packet to
/// produce a result identical to the one the previous test just computed.
///
/// **Bounded, because a timeline is large** (B439). Until 2026-09-30 this kept every demo's timeline
/// for the life of the run; with the 49 local demos present the host reached 39 GB private on a 32
/// GB machine, 43 minutes in, and was stopped (B438). The largest timeline is not z1800's 635 MB but
/// a local one's 4.7 GB, and a timeline runs forty to eighty-four times its demo's size, so the local
/// corpus alone is on the order of 80 GB of them — B439 has the measurements.
///
/// **So at most two are kept that nobody is using: up to 9.4 GB.** Tests hold the timelines they
/// are using whatever the cache does; <see cref="LruCache{TKey, TValue}"/> hands one back to anybody
/// else who asks while somebody still holds it, so two copies never exist.
///
/// **Sweeps ask in <see cref="WarmFirst"/> order.** A dozen tests walk every demo, and they start
/// minutes apart. In the order <c>Corpus.FilesWithSchema</c> gives, a sweep that starts after two
/// others have moved on would rebuild every timeline they built; asking for the one somebody already
/// has joins them instead, so the sweeps travel together and each timeline is built about once.
/// Every build is logged, so a run shows whether that held.
///
/// **Not an NUnit fixture, deliberately** — the same argument
/// <c>Tf2DemoSalvage.Viewer3D.Tests.MapCache</c> makes for maps. A shared fixture serialises the
/// tests inside it, and this assembly declares <c>ParallelScope.All</c> and
/// <c>InstancePerTestCase</c> for reasons its policy file sets out. A static cache keeps both: tests
/// stay in their own classes, run in parallel exactly as before, and receive a timeline that is
/// already built when another test has it.
///
/// **A timeline keeps a sample between calls, and it is safe to share anyway** (B438). <c>PropsAt</c>
/// caches what it built for the last tick it was asked (B259's incremental rebuild), and until B438 that
/// cache was neither locked nor exact: 2026-09-30, three of four parallel cases on z1800 found no prop
/// at ticks where each, run alone, found it. It is now taken under a lock, and every path through it
/// answers what a timeline built cold answers — <c>DemoTimelineSampleOrderTests</c> interleaves callers
/// and runs them on threads to hold it to that. So a test here may ask any tick of a shared timeline,
/// in any order.
/// </remarks>
internal static class TimelineCache
{
    /// <summary>How many built timelines the cache keeps beyond those tests are holding.</summary>
    private const int Kept = 2;

    private static readonly LruCache<string, DemoTimeline> Built =
        new(Kept, Build, StringComparer.OrdinalIgnoreCase);

    /// <summary>The timeline for a demo, built unless the cache or a test already has it.</summary>
    /// <param name="path">Full path to the demo.</param>
    /// <returns>The timeline.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <c>null</c>.</exception>
    public static DemoTimeline For(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return Built.Get(path);
    }

    /// <summary>Demos in the order a sweep should ask for them: one already built, before one to build.</summary>
    /// <param name="paths">Every demo the sweep visits.</param>
    /// <returns>The same demos, each once, decided after each has been used.</returns>
    public static IEnumerable<string> WarmFirst(IEnumerable<string> paths) => Built.WarmFirst(paths);

    private static DemoTimeline Build(string path)
    {
        long started = Stopwatch.GetTimestamp();
        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));

        // Every build, so a run can be read for rebuilds: a demo named twice was built twice.
        TestContext.Progress.WriteLine(
            $"TIMELINE built {Path.GetFileName(path)} in " +
            $"{Stopwatch.GetElapsedTime(started).TotalSeconds:0.0} s");

        return timeline;
    }
}
