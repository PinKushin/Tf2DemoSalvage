using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests;

/// <summary>
/// Builds a demo's timeline once and hands the same one to every test that asks for it.
/// </summary>
/// <remarks>
/// **The single largest cost in this suite, and it is not a testing question.** Measured
/// 2026-08-19 from <c>corpus.trx</c>: the suite spends 369 seconds of CPU across 138 results, and
/// roughly 200 of those are ten tests calling <see cref="DemoTimeline.Build"/> on the same handful
/// of files. Each call re-reads the demo, re-parses the schema, and re-walks every packet to
/// produce a result identical to the one the previous test just computed.
///
/// Ten tests at about twenty seconds each:
///
/// <code>
/// PlayersAt_BetweenFrames_MovesThroughPositionsNoFrameContains  24.8 s
/// PlayersInAMatch_DoNotAllFaceTheSameWay                        21.9 s
/// Build_KeyframesCostFarLessThanAPosePerFrame                   20.6 s
/// EntitiesAreHiddenAndComeBack_RatherThanLingering              20.4 s
/// PropsAt_ReturnsFewerModelsThanTheDemoEverHeldOf               20.4 s
/// Build_SomethingSomewhereMoves                                 20.2 s
/// Build_FindsModelsOnEveryEra                                   20.2 s
/// PlayersAt_CarriesTheYawTheTrackHolds                          20.0 s
/// </code>
///
/// **Not an NUnit fixture, deliberately** — the same argument
/// <c>Tf2DemoSalvage.Viewer3D.Tests.MapCache</c> makes for maps. A shared fixture serialises the
/// tests inside it, and this assembly declares <c>ParallelScope.All</c> and
/// <c>InstancePerTestCase</c> for reasons its policy file sets out. A static memoised cache keeps
/// both: tests stay in their own classes, run in parallel exactly as before, and simply receive a
/// timeline that is already built.
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
    private static readonly ConcurrentDictionary<string, Lazy<DemoTimeline>> Built =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The timeline for a demo, built on first request and reused after.</summary>
    /// <param name="path">Full path to the demo.</param>
    /// <returns>The timeline.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <c>null</c>.</exception>
    /// <remarks>
    /// <c>ExecutionAndPublication</c> rather than the default, so two tests arriving together
    /// build it once rather than racing to build it twice — which on a twenty-second build is the
    /// whole saving.
    /// </remarks>
    public static DemoTimeline For(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return Built.GetOrAdd(
            path,
            key => new Lazy<DemoTimeline>(
                () => DemoTimeline.Build(File.ReadAllBytes(key)),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }
}
