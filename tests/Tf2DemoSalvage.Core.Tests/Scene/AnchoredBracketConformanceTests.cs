using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// A cycle history read as the engine holds it after a `Reset()` it did not store — an entity whose bones were not set up
/// across a parity change (B878).
/// </summary>
/// <remarks>
/// **`m_nPrevNewSequenceParity` is the parity at the LAST bone setup** (written only at `c_baseanimating.cpp:716`,
/// `:1824`, `:1835`), so every update whose parity differs from it runs `m_iv_flCycle.Reset()` (`:4738-4747`): three
/// entries at `curtime` holding the value just latched (`interpolatedvar.h:740-751`). Later latches are `AddToHead` with
/// the flush (`:649`), and `GetInterpolationInfo` walks the list newest first (`:815-888`). Fixture: changetimes 7, 47, 57
/// arriving at 10, 50, 60 with cycles 0.2, 0.6, 1.0; the reset ran at 50, seeding 0.6.
/// </remarks>
public sealed class AnchoredBracketConformanceTests
{
    private static InterpolatedHistory Three()
    {
        InterpolatedHistory history = new(1);
        history.Add(7, 10, [0.2f]);
        history.Add(47, 50, [0.6f]);
        history.Add(57, 60, [1.0f]);
        return history;
    }

    private static HistoryAnchor At50(InterpolatedHistory history) => history.AnchorAt(50, new float[] { 0.6f })!.Value;

    /// <summary>
    /// **The engine's second settle site** (`interpolatedvar.h:865-871`): with 0.6 restated at 57, at 52 the pair is the
    /// reset (0.6) to 57 (0.6), the newest, equal, and no spline before it (the reset's three share one time) — settled.
    /// The plain history pairs 47 and 57 with 7 (0.2) before them, a spline whose third sample differs: not settled.
    /// </summary>
    [Test]
    public void BracketAnchored_AnEqualNewestAfterTheReset_IsSettled()
    {
        InterpolatedHistory history = new(1);
        history.Add(7, 10, [0.2f]);
        history.Add(47, 50, [0.6f]);
        history.Add(57, 60, [0.6f]);

        history.BracketAnchored(52, 60, At50(history)).ShouldBe(
            new Bracketing(InterpolatedHistory.AnchorSlot, 2, InterpolatedHistory.AnchorSlot, NoMoreChanges: true));
        history.Bracket(52, 60)!.Value.NoMoreChanges.ShouldBeFalse("the control: the plain spline is not settled");
    }

    /// <summary>
    /// **Past the reset the pair runs from it**: at 52 the walk passes 57 (newer) and stops at the reset's entries, stamped
    /// 50 — not at the latched 47, whose stamp the reset discarded.
    /// </summary>
    [Test]
    public void BracketAnchored_PastTheReset_PairsItWithTheNextEntry()
    {
        InterpolatedHistory history = Three();
        HistoryAnchor reset = At50(history);

        Bracketing pair = history.BracketAnchored(52, 60, reset)!.Value;

        (pair.Older, pair.Newer, pair.Oldest).ShouldBe((InterpolatedHistory.AnchorSlot, 2, InterpolatedHistory.AnchorSlot));
        history.ChangeTimeAt(pair.Older, reset).ShouldBe(50, "the reset's entries are stamped with its time");
        history.ValuesAt(pair.Older, reset)[0].ShouldBe(0.6f);
    }

    /// <summary>
    /// **Before every entry the engine holds the OLDEST walked** ("Didn't find any", `interpolatedvar.h:877-882`): at 48
    /// both 57 and the reset's 50 are later, so the walk ends on the reset's entries and holds 0.6 — not the latest received
    /// 1.0. The control: the plain history brackets 47 and 57 there.
    /// </summary>
    [Test]
    public void BracketAnchored_BeforeTheReset_HoldsTheResetsValue()
    {
        InterpolatedHistory history = Three();
        HistoryAnchor reset = At50(history);

        Bracketing pair = history.BracketAnchored(48, 60, reset)!.Value;

        (pair.Older, pair.Newer, pair.Oldest, pair.NoMoreChanges).ShouldBe(
            (InterpolatedHistory.AnchorSlot, InterpolatedHistory.AnchorSlot, InterpolatedHistory.AnchorSlot, false));
        history.Bracket(48, 60)!.Value.Older.ShouldBe(1, "the control: unanchored, 47 is the older entry");
    }

    /// <summary>
    /// **A later latch stamped at or before the reset flushes its three** (`AddToHead`, `interpolatedvar.h:649`): 48
    /// arriving at 52 leaves only itself, so at 47 the walk holds it (0.9), where unflushed it would end on the reset's 0.6.
    /// </summary>
    [Test]
    public void BracketAnchored_ALatchFlushingTheReset_HoldsTheLatch()
    {
        InterpolatedHistory history = new(1);
        history.Add(7, 10, [0.2f]);
        history.Add(47, 50, [0.6f]);
        history.Add(48, 52, [0.9f]);

        history.BracketAnchored(47, 52, At50(history)).ShouldBe(new Bracketing(2, 2, 2, NoMoreChanges: false));
    }

    /// <summary>
    /// **The plain history too holds the oldest LIVE entry before every entry** (B878 review 1): `Reset` at 30 seeds 0.6,
    /// then a latch stamped 29 arriving at 32 flushes the three (`AddToHead`, `interpolatedvar.h:649`). At 25 the engine's
    /// list is that latch alone, so it holds 0.65 — where the run's first entry, flushed, held 0.6.
    /// </summary>
    [Test]
    public void Bracket_BeforeEveryEntryWithTheRunsFirstFlushed_HoldsTheOldestLiveEntry()
    {
        InterpolatedHistory history = new(1);
        history.Add(17, 20, [0.4f]);
        history.Reset(30, [0.6f]);
        history.Add(29, 32, [0.65f]);

        history.Bracket(25, 32).ShouldBe(new Bracketing(4, 4, 4, NoMoreChanges: false));
        history.ValuesAt(4)[0].ShouldBe(0.65f);
    }

    /// <summary>**With nothing arrived after it, the reset holds and is settled**: at 52, played to 55.</summary>
    [Test]
    public void BracketAnchored_WithNothingArrivedAfterIt_HoldsTheResetSettled()
    {
        InterpolatedHistory history = Three();

        history.BracketAnchored(52, 55, At50(history)).ShouldBe(new Bracketing(
            InterpolatedHistory.AnchorSlot, InterpolatedHistory.AnchorSlot, InterpolatedHistory.AnchorSlot, NoMoreChanges: true));
    }

    /// <summary>
    /// **The spline's third sample is the reset's when the older entry follows it**: with 62 arriving at 65, a target of 59
    /// pairs 57 and 62, and the sample before 57 is the reset at 50 (0.6), not 47. Respaced to 5 ticks before 57:
    /// 0.6 + 0.4 × (1 − 5/7) = 0.7143.
    /// </summary>
    [Test]
    public void TimeFixup_AnOlderEntryAfterTheReset_RespacesFromIt()
    {
        InterpolatedHistory history = Three();
        history.Add(62, 65, [0.3f]);
        HistoryAnchor reset = At50(history);

        Bracketing pair = history.BracketAnchored(59, 65, reset)!.Value;
        float[] previous = new float[1];

        (pair.Older, pair.Newer, pair.Oldest).ShouldBe((2, 3, InterpolatedHistory.AnchorSlot));
        history.TimeFixup(pair, previous, [false], reset).ShouldBeTrue();
        previous[0].ShouldBe(0.6f + (0.4f * (1f - (5f / 7f))), 1e-6f);
    }

    /// <summary>
    /// **Once the pair's third sample arrived after the reset, the reset is out of reach**: with 67 arriving at 70, a target
    /// of 63 pairs 62 and 67 with 57 before them — exactly the plain answer.
    /// </summary>
    [Test]
    public void BracketAnchored_PastTheResetsReach_IsThePlainBracket()
    {
        InterpolatedHistory history = Three();
        history.Add(62, 65, [0.3f]);
        history.Add(67, 70, [0.5f]);

        history.BracketAnchored(63, 70, At50(history)).ShouldBe(history.Bracket(63, 70));
        history.Bracket(63, 70)!.Value.Oldest.ShouldBe(2);
    }

    /// <summary>
    /// **A stored reset after the anchor supersedes it**: `Reset` at 55 opens a newer generation, and the plain answer
    /// stands. A latch stamped 54 arriving at 58 flushes the reset's three, so at 56 the older entry (54) has no predecessor
    /// in its run — the case where building the anchor's list would give the anchor as its third sample.
    /// </summary>
    [Test]
    public void BracketAnchored_BelowANewerStoredReset_IsThePlainBracket()
    {
        InterpolatedHistory history = new(1);
        history.Add(7, 10, [0.2f]);
        history.Add(47, 50, [0.6f]);
        history.Reset(55, [0.8f]);
        history.Add(54, 58, [0.9f]);
        history.Add(57, 60, [1.0f]);

        Bracketing plain = history.Bracket(56, 60)!.Value;

        history.BracketAnchored(56, 60, At50(history)).ShouldBe(plain);
        (plain.Older, plain.Oldest).ShouldBe((5, 5), "the control: the older entry has no live predecessor in its run");
    }
}
