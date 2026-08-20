using System.Collections.Generic;

using Tf2DemoSalvage.Viewer3D;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// Which baked frame a prop is drawn on, against Valve's cycle advance.
/// </summary>
/// <remarks>
/// **The server does not advance a cycle; the client does, every frame.** A demo carries the
/// occasional networked correction and nothing between, so a viewer that replays only what was
/// sent leaves every prop frozen on frame zero — which is exactly what a health pack looked like
/// here before this was written. <c>C_BaseAnimating::FrameAdvance</c> does the advancing, and
/// <c>c_baseanimating.cpp:5493</c> is the line that matters:
///
/// <code>
/// float addcycle = flInterval * flCycleRate * m_flPlaybackRate;
/// </code>
///
/// **Three factors, and this code had two.** The playback rate was decoded, retained, unit-tested
/// and read by nothing, so every animation played at rate 1 and anything slowed or sped up ran at
/// the wrong speed while looking perfectly smooth. That is the defect this file exists to keep
/// dead: it is invisible in a screenshot, invisible in a round trip, and invisible to any test
/// that does not multiply the three factors independently.
///
/// So the rate is varied on its own here, holding time and cycle rate fixed. A test that moved
/// two factors together would pass against the two-factor version.
/// </remarks>
public sealed class AnimationSelectionConformanceTests
{
    /// <summary>Frames baked for the one animation these tests use.</summary>
    private const int Frames = 11;

    /// <summary>Cycles per second, chosen so one second is exactly one cycle.</summary>
    private const float CycleRate = 1f;

    [Test]
    public void Select_APlaybackRateOfTwo_AdvancesTwiceAsFar()
    {
        // **The measurement that catches a missing third factor**, and the only one that can:
        // time and cycle rate are identical in both calls, so the sole difference is the rate.
        // Half a second at rate 1 is half a cycle; at rate 2 it is a whole one, which wraps to
        // the start of a looping animation.
        (int frame, _, _) = Select(cycle: 0f, seconds: 0.5, rate: 1f);
        (int doubled, _, _) = Select(cycle: 0f, seconds: 0.5, rate: 2f);

        // 11 frames span 10 intervals, so half a cycle is frame 5.
        frame.ShouldBe(5);

        // A full cycle wraps: phase returns to 0, so does the frame.
        doubled.ShouldBe(0);
    }

    [Test]
    public void Select_AZeroPlaybackRate_HoldsTheNetworkedCycle()
    {
        // Rate zero is a paused animation, and it is the case a missing factor cannot distinguish
        // from rate one: without the multiply, both advance. The prop must stay exactly where the
        // demo said it was, however much time passes.
        //
        // **10.3 seconds rather than 10.0, and the difference decides whether this test works at
        // all.** A whole number of cycles wraps back to the phase it started on, so with the
        // multiply removed the broken code lands on the same frame as the correct code and the
        // assertion passes against the bug it names. Measured — sabotaging the multiply left this
        // green until the time was changed. That is the "wrong condition" failure: an input for
        // which correct and broken predict the same observation, and no strengthening of the
        // assertion can rescue it.
        (int held, _, _) = Select(cycle: 0.5f, seconds: 10.3, rate: 0f);

        held.ShouldBe(5);
    }

    [Test]
    public void Select_TheNetworkedCycle_IsTheStartingPointNotTheAnswer()
    {
        // The cycle the demo sent is where the advance BEGINS. A viewer that used it directly and
        // ignored elapsed time is the frozen-prop bug; one that ignored the cycle and used only
        // time would drift away from the server's corrections.
        (int fromZero, _, _) = Select(cycle: 0f, seconds: 0.2, rate: 1f);
        (int fromHalf, _, _) = Select(cycle: 0.5f, seconds: 0.2, rate: 1f);

        fromZero.ShouldBe(2);
        fromHalf.ShouldBe(7);
    }

    [Test]
    public void Select_ANegativeSequence_IsSequenceZeroRatherThanAnError()
    {
        // **Every health pack in the corpus reports -1 and every one of them animates in game.**
        // A property that never changes from its default is never networked, so an absent
        // m_nSequence means the entity is still on its first sequence.
        (int frame, _, _) = Select(cycle: 0f, seconds: 0.5, rate: 1f, sequence: -1);

        frame.ShouldBe(5);
    }

    [Test]
    public void Select_ASequencePastTheModel_DrawsTheFirstFrameRatherThanThrowing()
    {
        // A sequence index the model does not have is a demo recorded against a different build.
        // Frame zero is a pose; an exception is a viewer that stops.
        Select(cycle: 0f, seconds: 0.5, rate: 1f, sequence: 99).ShouldBe((0, 0, 0f));
    }

    [Test]
    public void Select_AModelWithNoGeometry_SelectsNothing()
    {
        // A model that failed to load is remembered as empty rather than retried every frame, so
        // this is reached once per such model per frame and must be cheap and silent.
        PropModels.ModelFrames empty = new(
            Geometry: [],
            Layout: new Dictionary<int, (int, int, float)>(),
            SequenceAnimation: [],
            SequenceLoops: []);

        empty.Select(0, 0f, 1.0, 1f).ShouldBe((0, 0, 0f));
    }

    [Test]
    public void Select_ABlendBetweenFrames_ReportsTheFractionAndTheNextFrame()
    {
        // The renderer interpolates between two baked frames, so the pair and the fraction are the
        // answer rather than a single index. A quarter of the way into the interval between frame
        // 2 and frame 3 must say so, not round to one of them.
        (int frame, int next, float blend) = Select(cycle: 0.225f, seconds: 0.0, rate: 1f);

        frame.ShouldBe(2);
        next.ShouldBe(3);
        blend.ShouldBe(0.25f, 0.001f);
    }

    /// <summary>Runs the selector over a model with one looping animation.</summary>
    private static (int Frame, int Next, float Blend) Select(
        float cycle, double seconds, float rate, int sequence = 0) =>
        Model().Select(sequence, cycle, seconds, rate);

    /// <summary>
    /// A model with one sequence, one looping animation and <see cref="Frames"/> baked frames.
    /// </summary>
    /// <remarks>
    /// The geometry is empty per frame — the selector reads only the COUNT, and giving it real
    /// vertices would make the fixture large without making the measurement sharper. Frame count
    /// is 11 rather than 10 so that frames and intervals differ: they span one fewer interval than
    /// they have frames, and a fixture where the two are equal cannot tell the arithmetic apart.
    /// </remarks>
    private static PropModels.ModelFrames Model() => new(
        Geometry: [.. new IReadOnlyList<PropVertex>[Frames]],
        Layout: new Dictionary<int, (int Start, int Frames, float CyclesPerSecond)>
        {
            [0] = (0, Frames, CycleRate),
        },
        SequenceAnimation: [0],
        SequenceLoops: [true]);
}
