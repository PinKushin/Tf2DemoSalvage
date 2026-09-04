using System;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>The collection and pause account appended to the per-second frame line.</summary>
/// <remarks>
/// **This was `MainForm.GarbageThisSecond`** (B188, D90): twenty-four lines of subtraction, a
/// threshold and a format string, with no view content whatsoever — and no tests, because reaching
/// it meant constructing a form.
///
/// **The readings are passed in rather than read from `GC`**, which is what makes any of this
/// assertable. A counter calling `GC.CollectionCount` itself can only be tested by persuading the
/// runtime to collect, which is neither deterministic nor fast.
/// </remarks>
public sealed class GarbageCounterTests
{
    [Test]
    public void Since_WithNothingSinceTheLastReading_IsEmpty()
    {
        // A quiet second stays one line. The counter is primed with a reading first, or "no change"
        // and "never read anything" would be the same observation.
        GarbageCounter counter = new();

        counter.Since(new GarbageReading(10, 5, 2, Milliseconds(400), Bytes));

        counter.Since(new GarbageReading(10, 5, 2, Milliseconds(400), Bytes)).ShouldBeEmpty();
    }

    [Test]
    public void Since_AfterCollections_ReportsTheDeltaRatherThanTheTotal()
    {
        // **The distinguishing input, and it is the whole point of the type.** `GC.CollectionCount`
        // is monotonic since process start, so a counter that printed it raw would report a growing
        // number every second and read as a leak. Totals and deltas differ here by construction:
        // 12/6/2 against 2/1/0.
        GarbageCounter counter = new();

        counter.Since(new GarbageReading(10, 5, 2, Milliseconds(400), Bytes));

        counter.Since(new GarbageReading(12, 6, 2, Milliseconds(430), Bytes + Megabyte))
            .ShouldBe("; gc 2/1/0 paused 30 ms, allocated 1 MB");
    }

    [Test]
    public void Since_WithAPauseButNoCollections_StillReports()
    {
        // **The `&&` in the quiet-second guard, which an `||` would break silently.** Pause time can
        // grow without the collection COUNT moving — a single long gen2 that began before this
        // second and finished inside it. That is precisely B163's symptom: the app freezes for most
        // of a second while the frame rate on either side is untouched. Reporting it is the reason
        // the pause number is there at all.
        GarbageCounter counter = new();

        counter.Since(new GarbageReading(3, 1, 0, Milliseconds(100), Bytes));

        counter.Since(new GarbageReading(3, 1, 0, Milliseconds(640), Bytes + (2 * Megabyte)))
            .ShouldBe("; gc 0/0/0 paused 540 ms, allocated 2 MB");
    }

    [Test]
    public void Since_WithAPauseUnderAMillisecond_IsEmpty()
    {
        // The other side of that guard: sub-millisecond drift is not a stall, and printing it would
        // put a `gc 0/0/0` on almost every line and drown the seconds that matter.
        GarbageCounter counter = new();

        counter.Since(new GarbageReading(3, 1, 0, Milliseconds(100), Bytes));

        counter.Since(new GarbageReading(3, 1, 0, Milliseconds(100.6), Bytes)).ShouldBeEmpty();
    }

    [Test]
    public void Since_OnTheVeryFirstReading_IsEmptyRatherThanTheProcessTotal()
    {
        // **A counter with no previous reading has a delta of "everything since process start".**
        // Left unguarded, the first line of every session reports the startup collections as though
        // they happened in that second — a large number, in the log's most-read line, that is not
        // wrong so much as meaningless.
        GarbageCounter counter = new();

        counter.Since(new GarbageReading(40, 12, 3, Milliseconds(900), Bytes)).ShouldBeEmpty();
    }

    /// <remarks>
    /// **Collections are a proxy for allocation and the bytes are the variable** (B262). The audit
    /// asked for *"allocations per warmed frame"* and the line answered with gen 0 COUNTS, which move
    /// with the allocation rate but are quantised by whatever the runtime's gen 0 budget happens to
    /// be — so 26 against 35 says "more" and never says how much more.
    ///
    /// **The delta rule is the same one every other field here follows**, and for the same reason:
    /// <c>GC.GetTotalAllocatedBytes</c> is monotonic since process start, so printed raw it grows all
    /// session and reads as a leak.
    /// </remarks>
    [Test]
    public void Since_AfterAllocations_ReportsTheBytesRatherThanTheTotal()
    {
        GarbageCounter counter = new();

        counter.Since(new GarbageReading(10, 5, 2, Milliseconds(400), 900_000_000L));

        counter.Since(new GarbageReading(12, 6, 2, Milliseconds(430), 1_000_000_000L))
            .ShouldBe("; gc 2/1/0 paused 30 ms, allocated 95.4 MB");
    }

    /// <remarks>
    /// **Allocation alone must not break the quiet-second rule.** Every frame allocates something, so
    /// a second reported because bytes moved is a second reported ALWAYS — which would put a `gc`
    /// suffix on every line in the log and drown the seconds where something happened, the exact
    /// outcome <see cref="Since_WithAPauseUnderAMillisecond_IsEmpty"/> exists to prevent.
    ///
    /// **And nothing is lost by it.** A second that allocated without provoking a collection
    /// allocated less than the gen 0 budget, which is the same statement the empty line already makes.
    /// </remarks>
    [Test]
    public void Since_WithAllocationButNoCollectionOrPause_IsStillEmpty()
    {
        GarbageCounter counter = new();

        counter.Since(new GarbageReading(3, 1, 0, Milliseconds(100), 5_000L));

        counter.Since(new GarbageReading(3, 1, 0, Milliseconds(100), 4_000_000L)).ShouldBeEmpty();
    }

    // **`GarbageReading.FromRuntime` has no test here, and the reason is worth stating rather than
    // leaving as a gap.** Three ways to test it were considered and each fails on its own terms:
    //
    // - Forcing a collection is `GC.Collect()`, which SonarLint refuses (S1215) — correctly, and
    //   suppressing an analyzer to make a marginal test possible is the wrong trade.
    // - Asserting the generation ordering invariant (gen0 >= gen1 >= gen2, since collecting a higher
    //   generation collects the lower ones) catches a swapped index only when the counts DIFFER. In
    //   a process that has not collected, 0 >= 0 >= 0 holds under every permutation, so the test
    //   passes probabilistically — which is the one thing this project will not accept from a test.
    // - Allocating until a collection happens means reading `GC.CollectionCount` in the arrangement
    //   to decide when to stop, so the test's own mechanism fails the same way as the thing it is
    //   testing.
    //
    // What is left is a four-argument pass-through to the runtime, and `StopwatchTime` already set
    // the precedent for that shape: *"deliberately trivial: everything worth testing lives in the
    // presenter, which is why this exists at all."* Everything with a decision in it — the delta,
    // the first-reading guard, the quiet-second threshold, the format — is above and is covered.

    /// <summary>A plausible process-lifetime allocation total to take deltas against.</summary>
    /// <remarks>
    /// **Large on purpose.** The counter's whole job is to report the delta rather than the total, so
    /// a start value near zero would let a bug that printed the total pass three of these tests.
    /// </remarks>
    private const long Bytes = 900_000_000L;

    private const long Megabyte = 1024L * 1024L;

    private static TimeSpan Milliseconds(double value) => TimeSpan.FromMilliseconds(value);
}
