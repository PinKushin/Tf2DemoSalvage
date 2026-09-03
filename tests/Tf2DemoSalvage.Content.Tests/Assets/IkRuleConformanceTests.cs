using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// A rule's influence across a cycle — <c>Studio_IKRuleWeight</c>.
/// </summary>
/// <remarks>
/// **<c>bone_setup.cpp:2875</c>**, and it answers two questions in one call: how much the rule
/// counts for, and WHICH FRAME of its error track to read. A port that split them would read the
/// track at the wrong frame in the two branches that overwrite it.
///
/// **The envelope is the same four numbers an autolayer uses and this is not that mechanism.** The
/// ramps are splined here as they are there, but the frame arithmetic has no counterpart, and two
/// of the branches exist only to pin the frame.
/// </remarks>
public sealed class IkRuleConformanceTests
{
    /// <summary>How close two floats must be to count as equal here.</summary>
    private const double Tolerance = 1e-4;

    /// <summary>Frames in the fixture's animation, so the frame arithmetic has a scale.</summary>
    private const int Frames = 31;

    [Test]
    public void Weight_BeforeTheRuleStarts_IsZeroAndPinsTheFrameToTheTrackStart()
    {
        // `if (flCycle < ikRule.start) { iFrame = ikRule.iStart; fraq = 0.0f; return 0.0f; }` —
        // and the frame is the point. It was already computed above the branch, to a NEGATIVE
        // number for a cycle below the start, and this branch exists to throw that away.
        StudioIkRule rule = Ruled(start: 0.4f, peak: 0.5f, tail: 0.8f, end: 0.9f) with
        {
            FirstFrame = 7,
        };

        float weight = StudioIkRules.Weight(rule, Frames, cycle: 0.1f, out int frame, out float part);

        weight.ShouldBe(0f);
        frame.ShouldBe(7, "pinned to iStart rather than left at the negative it computed");
        part.ShouldBe(0f);
    }

    [Test]
    public void Weight_InsideTheRampIn_IsSplinedRatherThanLinear()
    {
        // `value = (flCycle - start) / (peak - start)` then `SimpleSpline(value)`. At cycle 0.45
        // with start 0.4 and peak 0.6 the linear value is 0.25, and the spline of that is
        // 3(0.0625) - 2(0.015625) = 0.15625.
        StudioIkRule rule = Ruled(start: 0.4f, peak: 0.6f, tail: 0.8f, end: 0.9f);

        StudioIkRules.Weight(rule, Frames, cycle: 0.45f, out _, out _)
            .ShouldBe(0.15625f, Tolerance, "3s^2 - 2s^3 at s = 0.25");
    }

    [Test]
    public void Weight_OnThePlateau_IsOneWithoutBeingSplined()
    {
        // `else if (flCycle < ikRule.tail) return 1.0f;` — an early return that never reaches the
        // spline. It agrees numerically, the spline of one being one, which is why this asserts the
        // value rather than the route.
        StudioIkRule rule = Ruled(start: 0.4f, peak: 0.6f, tail: 0.8f, end: 0.9f);

        StudioIkRules.Weight(rule, Frames, cycle: 0.7f, out _, out _).ShouldBe(1f);
    }

    [Test]
    public void Weight_InsideTheRampOut_FallsBackToZero()
    {
        // `value = 1.0f - ((flCycle - tail) / (end - tail))`, then splined. At cycle 0.85 with tail
        // 0.8 and end 0.9 the linear value is 0.5, whose spline is 3(0.25) - 2(0.125) = 0.5.
        StudioIkRule rule = Ruled(start: 0.4f, peak: 0.6f, tail: 0.8f, end: 0.9f);

        StudioIkRules.Weight(rule, Frames, cycle: 0.85f, out _, out _)
            .ShouldBe(0.5f, Tolerance, "the spline is symmetric about a half");
    }

    [Test]
    public void Weight_PastTheEnd_IsZeroAndPinsTheFrameToWhereTheRuleFinished()
    {
        // The last branch recomputes the frame from `end` rather than from the cycle, so a finished
        // rule keeps reading its FINAL error instead of running off the track. With start 0.4,
        // end 0.9 and 31 frames: 30 * 0.5 + 2 = 17.
        StudioIkRule rule = Ruled(start: 0.4f, peak: 0.6f, tail: 0.8f, end: 0.9f) with
        {
            FirstFrame = 2,
        };

        float weight = StudioIkRules.Weight(rule, Frames, cycle: 0.95f, out int frame, out _);

        weight.ShouldBe(0f);
        frame.ShouldBe(17, "(frames - 1) * (end - start) + iStart");
    }

    [Test]
    public void Weight_WithAnEndPastOne_WrapsACycleBelowTheStart()
    {
        // `if (ikRule.end > 1.0f && flCycle < ikRule.start) flCycle = flCycle + 1.0f;` — a footstep
        // beginning near the end of a walk cycle and finishing after it has looped. At cycle 0.05
        // against a rule running 0.9 to 1.2, the wrap makes the cycle 1.05, which is on the plateau.
        //
        // **Without the wrap this is below the start and weighs nothing**, so the two readings are
        // one and zero — the widest possible difference, which is what makes the case worth having.
        StudioIkRule rule = Ruled(start: 0.9f, peak: 1f, tail: 1.1f, end: 1.2f);

        StudioIkRules.Weight(rule, Frames, cycle: 0.05f, out _, out _)
            .ShouldBe(1f, "the cycle wraps forward into the rule's window");
    }

    /// <remarks>
    /// **The control for the wrap.** The same cycle against a rule that does NOT run past one must
    /// weigh nothing — otherwise "wrapped correctly" and "wraps everything" are the same
    /// observation.
    /// </remarks>
    [Test]
    public void Weight_WithAnEndInsideOne_DoesNotWrap()
    {
        StudioIkRule rule = Ruled(start: 0.9f, peak: 0.95f, tail: 0.98f, end: 1f);

        StudioIkRules.Weight(rule, Frames, cycle: 0.05f, out _, out _)
            .ShouldBe(0f, "an end of exactly one is not greater than one, so nothing wraps");
    }

    [Test]
    public void Weight_MidRamp_ReportsTheFrameAndFractionOfTheErrorTrack()
    {
        // `fraq = (numframes - 1) * (cycle - start) + iStart; iFrame = (int)fraq; fraq -= iFrame;`
        // With 31 frames, start 0.4, iStart 0 and cycle 0.55: 30 * 0.15 = 4.5, so frame 4 and half
        // way to frame 5. The fraction is what blends two entries of the error track, so losing it
        // would step the correction rather than sweep it.
        StudioIkRule rule = Ruled(start: 0.4f, peak: 0.9f, tail: 0.95f, end: 1f);

        StudioIkRules.Weight(rule, Frames, cycle: 0.55f, out int frame, out float part);

        frame.ShouldBe(4);
        part.ShouldBe(0.5f, Tolerance);
    }

    /// <summary>A rule with the given envelope and nothing else set.</summary>
    private static StudioIkRule Ruled(float start, float peak, float tail, float end) =>
        new(
            Type: StudioIkRuleType.Self,
            Chain: 0,
            Bone: 0,
            Slot: 0,
            Height: 0f,
            Radius: 0f,
            Floor: 0f,
            Position: default,
            Rotation: default,
            CompressedError: 0,
            FirstFrame: 0,
            ErrorIndex: 0,
            Start: start,
            Peak: peak,
            Tail: tail,
            End: end,
            Contact: 0f,
            Drop: 0f,
            Top: 0f,
            AttachmentName: 0);
}
