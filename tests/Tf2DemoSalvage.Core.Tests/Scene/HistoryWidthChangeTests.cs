using System;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// An entry is addressed by where it was WRITTEN, not by the history's current width (B386).
/// </summary>
/// <remarks>
/// **`_values` is one flat list of floats and `ValuesAt` sliced it at a fixed stride**, which is
/// correct exactly while every entry in it has the same width. Two documented behaviours make that
/// false together:
///
/// 1. **`SetMaxCount` changes the width and then calls `Reset`** — the engine's own order
///    (<c>interpolatedvar.h:1272</c>), driven by the MODEL: `m_iv_flPoseParameter.SetMaxCount(
///    hdr-&gt;GetNumPoseParameters() )` (<c>c_baseanimating.cpp:1124</c>), so the width moves whenever an
///    entity changes model mid-recording.
/// 2. **`Reset` deletes nothing**, which is this project's one licensed difference from the engine:
///    the engine's `ClearHistory()` empties the list, ours keeps the entries so a scrub backwards
///    still finds them and makes the boundary a GENERATION instead.
///
/// The engine can hold only one width at a time because it threw the others away. We hold them all,
/// so the list is a mixed stride being read at a single one — and every entry after the first width
/// change is addressed at the wrong offset.
///
/// **It fails two different ways, and only one of them is loud.**
///
/// - **Widened** — the computed offset runs past the end of the list and `Slice` throws
///   <see cref="ArgumentOutOfRangeException"/>. That is the crash: a headless capture of
///   `20130518_0313_cp_granary_blu_blu` at tick 27692 dies in `Bracket` → `Settled` → `Same` →
///   `ValuesAt` before writing its PNG.
/// - **Narrowed** — the computed offset lands *inside* the list and the read succeeds, returning
///   floats belonging to some other entry. No throw, no log, a pose built from the wrong numbers.
///
/// The second is the reason the fix is not a bounds guard: clamping the index would convert the
/// crash into the silent half rather than into a correct read. The exception type is carrying the
/// information (`docs/memory/an-exception-type-can-be-load-bearing.md`) — it names a bad
/// *argument*, and the bad argument is the offset.
///
/// **Synthetic, and stronger than a corpus test for being so** (D38): the test puts the values in,
/// so it knows what a correct read returns. A corpus test could only compare two readings of one
/// file, and both readings go through the same broken addressing.
/// </remarks>
public sealed class HistoryWidthChangeTests
{
    /// <summary>The six components a six-pose-parameter model's entry carries.</summary>
    private static readonly float[] Wide = [3f, 4f, 5f, 6f, 7f, 8f];

    /// <summary>A history whose width grows mid-life, as a model change makes it.</summary>
    /// <returns>Two narrow entries, the three <c>Reset</c> seeded at the new width, and one after.</returns>
    private static InterpolatedHistory Widened()
    {
        InterpolatedHistory history = new(1);

        history.Add(10, 10, [1f]);
        history.Add(20, 20, [2f]);

        // `OnNewModel`: the model now has six pose parameters where the old one had a single
        // component. `SetMaxCount` moves the width and re-seeds three entries at it.
        history.SetMaxCount(Wide.Length, 30, Wide);

        history.Add(40, 40, Wide);

        return history;
    }

    [Test]
    public void ValuesAt_EveryEntrySeededByAWideningReset_ReturnsWhatItWasGiven()
    {
        InterpolatedHistory history = Widened();

        // Entries 2, 3 and 4 are `Reset`'s three, entry 5 is the append after it. Every one of them
        // was handed `Wide` and must read back as `Wide` — the test put the value there.
        for (int entry = 2; entry < history.Count; entry++)
        {
            history.ValuesAt(entry).ToArray().ShouldBe(Wide, $"entry {entry}");
        }
    }

    [Test]
    public void ValuesAt_AnEntrySeededByANarrowingReset_ReturnsWhatItWasGiven()
    {
        InterpolatedHistory history = new(3);

        history.Add(10, 10, [1f, 2f, 3f]);

        // The other direction, and the dangerous one: the offset lands inside the list rather than
        // past its end, so this read SUCCEEDS and returns entry zero's second component.
        history.SetMaxCount(1, 20, [9f]);

        history.ValuesAt(1).ToArray().ShouldBe([9f]);
    }

    [Test]
    public void Bracket_AcrossAWidthChange_PairsTheTwoEntriesEitherSideOfTheTarget()
    {
        InterpolatedHistory history = Widened();

        // The production shape, and the one the viewer crashed on: `Bracket` reaches `Settled`,
        // which compares the newer entry's values against the older's through `Same`.
        Bracketing pair = history.Bracket(35d, 40).ShouldNotBeNull();

        pair.Older.ShouldBe(4);
        pair.Newer.ShouldBe(5);
        pair.Oldest.ShouldBe(3);

        // The newest entry IS the head, the pair holds one value, and `Reset`'s three share a
        // changetime so the older interval is zero — settled on the second clause, not the third.
        pair.NoMoreChanges.ShouldBeTrue();
    }

    [Test]
    public void Bracket_ScrubbedBackBeforeAWidthChange_ReadsTheOldEntriesAtTheirOwnWidth()
    {
        InterpolatedHistory history = Widened();

        // **The half of the licensed difference that pays for the retention.** The engine's
        // `ClearHistory()` deleted these, so it could never be asked; we keep them precisely so a
        // scrub backwards still finds them, which means reading a ONE-component entry out of a
        // history whose current width is six.
        Bracketing pair = history.Bracket(15d, 20).ShouldNotBeNull();

        pair.Older.ShouldBe(0);
        pair.Newer.ShouldBe(1);

        // Their own width, not the history's — the whole point of the per-entry offset. A consumer
        // reading `Width` components would run off the end of these, which is why `PoseBetween`
        // takes `Math.Min(stated.Count, from.Length)` and lets the stated value stand past it.
        history.ValuesAt(pair.Older).ToArray().ShouldBe([1f]);
        history.ValuesAt(pair.Newer).ToArray().ShouldBe([2f]);
    }
}
