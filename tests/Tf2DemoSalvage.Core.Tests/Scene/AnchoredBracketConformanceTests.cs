using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// A cycle history read as if `Reset()` had run at one entry's arrival — the engine's history for an entity whose bones
/// were not set up across a parity change (B878).
/// </summary>
/// <remarks>
/// **`m_nPrevNewSequenceParity` is the parity at the LAST bone setup** (written only at `c_baseanimating.cpp:716`,
/// `:1824`, `:1835`), so every update whose parity differs from it runs `m_iv_flCycle.Reset()` (`:4738-4747`): three
/// entries at `curtime` holding the value just latched (`interpolatedvar.h:740-751`). The newest such arrival is the
/// ANCHOR, and the engine's list is the live entries arrived after it plus those three. Fixture: changetimes 7, 47, 57
/// arriving at 10, 50, 60 with cycles 0.2, 0.6, 1.0; the anchor is the entry arriving at 50.
/// </remarks>
public sealed class AnchoredBracketConformanceTests
{
    private const int Anchor = 1;

    private static InterpolatedHistory Three()
    {
        InterpolatedHistory history = new(1);
        history.Add(7, 10, [0.2f]);
        history.Add(47, 50, [0.6f]);
        history.Add(57, 60, [1.0f]);
        return history;
    }

    /// <summary>
    /// **Past the anchor's arrival the pair runs from it**: at 52 the engine's walk passes 57 (newer) and stops at the
    /// anchor's entries, stamped 50 — not at the latched 47, whose stamp the reset discarded.
    /// </summary>
    [Test]
    public void BracketAnchored_PastTheAnchorsArrival_PairsTheAnchorWithTheNextEntry()
    {
        InterpolatedHistory history = Three();

        Bracketing pair = history.BracketAnchored(52, 60, Anchor)!.Value;

        (pair.Older, pair.Newer, pair.Oldest).ShouldBe((InterpolatedHistory.AnchorSlot, 2, InterpolatedHistory.AnchorSlot));
        history.ChangeTimeAt(pair.Older, Anchor).ShouldBe(50, "the reset's entries are stamped with the arrival");
        history.ValuesAt(pair.Older, Anchor)[0].ShouldBe(0.6f);
    }

    /// <summary>
    /// **Before every entry the engine has, nothing brackets and the networked value holds**: at 48 the anchor (50) and
    /// 57 are both later, so `GetInterpolationInfo` fails and `m_pValue` — the latest received, 1.0 — stays. The control:
    /// the history read normally brackets 47 and 57 there.
    /// </summary>
    [Test]
    public void BracketAnchored_BeforeTheAnchorsArrival_HoldsTheLatestReceived()
    {
        InterpolatedHistory history = Three();

        Bracketing pair = history.BracketAnchored(48, 60, Anchor)!.Value;

        (pair.Older, pair.Newer, pair.Oldest).ShouldBe((2, 2, 2));
        history.Bracket(48, 60)!.Value.Older.ShouldBe(1, "the control: unanchored, 47 is the older entry");
    }

    /// <summary>**With nothing arrived after the anchor, the anchor holds and is settled**: at 52, played to 55.</summary>
    [Test]
    public void BracketAnchored_WithNothingArrivedAfterIt_HoldsTheAnchorSettled()
    {
        InterpolatedHistory history = Three();

        history.BracketAnchored(52, 55, Anchor).ShouldBe(new Bracketing(
            InterpolatedHistory.AnchorSlot, InterpolatedHistory.AnchorSlot, InterpolatedHistory.AnchorSlot, NoMoreChanges: true));
    }

    /// <summary>
    /// **The spline's third sample is the anchor when the older entry has no live predecessor after it**: with 62 arriving
    /// at 65, a target of 59 pairs 57 and 62, and the sample before 57 is the anchor at 50 (value 0.6), not 47. Respaced
    /// to 5 ticks before 57: 0.6 + 0.4 × (1 − 5/7) = 0.7143.
    /// </summary>
    [Test]
    public void TimeFixup_AnOlderEntryWhosePredecessorIsTheAnchor_RespacesFromTheAnchor()
    {
        InterpolatedHistory history = Three();
        history.Add(62, 65, [0.3f]);

        Bracketing pair = history.BracketAnchored(59, 65, Anchor)!.Value;
        float[] previous = new float[1];

        (pair.Older, pair.Newer, pair.Oldest).ShouldBe((2, 3, InterpolatedHistory.AnchorSlot));
        history.TimeFixup(pair, previous, [false], Anchor).ShouldBeTrue();
        previous[0].ShouldBe(0.6f + (0.4f * (1f - (5f / 7f))), 1e-6f);
    }

    /// <summary>
    /// **Once the older entry has a live predecessor after the anchor, the anchor no longer matters**: with 67 arriving at
    /// 70, a target of 63 pairs 62 and 67 with 57 before them — exactly the unanchored answer.
    /// </summary>
    [Test]
    public void BracketAnchored_PastTheAnchorsReach_IsTheUnanchoredBracket()
    {
        InterpolatedHistory history = Three();
        history.Add(62, 65, [0.3f]);
        history.Add(67, 70, [0.5f]);

        history.BracketAnchored(63, 70, Anchor).ShouldBe(history.Bracket(63, 70));
        history.Bracket(63, 70)!.Value.Oldest.ShouldBe(2);
    }

    /// <summary>
    /// **A real reset after the anchor supersedes it**: `Reset` at 55 opens a newer generation, so the anchor at 50 is
    /// below the floor and the answer is the unanchored one. A latch stamped 54 arriving at 58 flushes the reset's three
    /// entries, so at 56 the older entry (54) has no predecessor in its generation — the case an anchor taken across the
    /// boundary would give the anchor as its third sample.
    /// </summary>
    [Test]
    public void BracketAnchored_BelowANewerResetsGeneration_IsTheUnanchoredBracket()
    {
        InterpolatedHistory history = new(1);
        history.Add(7, 10, [0.2f]);
        history.Add(47, 50, [0.6f]);
        history.Reset(55, [0.8f]);
        history.Add(54, 58, [0.9f]);
        history.Add(57, 60, [1.0f]);

        Bracketing plain = history.Bracket(56, 60)!.Value;

        history.BracketAnchored(56, 60, Anchor).ShouldBe(plain);
        (plain.Older, plain.Oldest).ShouldBe((5, 5), "the control: the older entry has no live predecessor in its run");
    }
}
