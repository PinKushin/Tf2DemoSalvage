using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// What an entity that never sent a sequence is playing.
/// </summary>
/// <remarks>
/// **Zero, which is the engine's default — not a sentinel of our own.** `m_nSequence` is a plain
/// int on `CBaseAnimating`, initialised to 0 (`BaseAnimatingOverlay.cpp:104`), and a delta format
/// only sends what changed from the baseline. So "never mentioned" and "sequence 0" are the same
/// statement about the wire, and there is nothing for a third value to mean.
///
/// **This project used −1 for it, and every consumer immediately undid that.** `EntityModels` clamps
/// with `Math.Max(0, pose.Sequence)` in two places, and `PropModels.Select` opens with
/// `int wanted = sequence &lt; 0 ? 0 : sequence;` under a comment stating the rule outright: *"A
/// sequence the demo never mentioned is sequence zero, not an error."* Three sites converting the
/// sentinel back into the value it stood for.
///
/// **It was not harmless, because one consumer does not clamp — it compares.**
/// `InterpolateCycle` treats a change of sequence as a cut and stops interpolating the cycle:
///
/// <code>
/// if (from.Sequence != to.Sequence) { return from.Cycle; }
/// </code>
///
/// So an entity whose first keyframe was "absent" (−1) and whose next stated sequence 0 registered a
/// sequence change that never happened, and its animation was cut at that boundary rather than
/// interpolated through. With the engine's default the two keyframes agree and the cycle flows.
///
/// Found by auditing nullable accessors for invented unknowns, on the owner's rule that Valve's data
/// has no nulls — it is values, not pointers, so absence is always a default and never a third
/// state.
/// </remarks>
public sealed class AbsentSequenceTests
{
    [Test]
    public void APoseThatWasNeverToldASequenceIsPlayingSequenceZero()
    {
        ScenePose pose = new();

        pose.Sequence.ShouldBe(0);
    }

    [Test]
    public void AnAbsentSequenceFollowedByZeroIsNotACut()
    {
        // **The behaviour the sentinel changed.** Two keyframes, the first never told a sequence and
        // the second explicitly on sequence 0 — the same animation throughout, so the cycle must
        // interpolate rather than freeze at the first keyframe's value.
        //
        // Under the old default these disagreed (−1 against 0), InterpolateCycle saw a sequence
        // change, and returned `from.Cycle` unchanged for the whole span.
        ScenePropTrack track = new(entityIndex: 1, modelPath: "a.mdl", serialNumber: 1);

        track.Add(0, new ScenePose { Cycle = 0.00f });
        track.Add(10, new ScenePose { Sequence = 0, Cycle = 0.20f });

        ScenePose middle = track.At(5).ShouldNotBeNull();

        // Halfway between the two keyframes, so halfway through the cycle. A cut would report the
        // first keyframe's 0.0 for the whole span.
        //
        // **The gap is deliberately not half a cycle.** A cycle is circular and interpolation takes
        // the short way round, so 0.0 to 0.5 is equidistant in both directions and the correct
        // answer is genuinely ambiguous — the first draft of this test used exactly that and got
        // 0.75, which is not wrong. An input where the right answer is undefined cannot measure
        // anything.
        middle.Cycle.ShouldBe(0.10f, tolerance: 0.01f);
    }
}
