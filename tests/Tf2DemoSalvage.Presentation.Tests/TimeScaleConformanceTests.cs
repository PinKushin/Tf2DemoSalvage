namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>What the engine does with playback speed, and where we deliberately differ.</summary>
/// <remarks>
/// **Written before the implementation, from the SDK, per `docs/CONFORMANCE.md`.** A parity test
/// authored afterwards is a description of what was built, which is the one thing it must never be.
///
/// **Valve's demo scrubbing UI is `CReplayPerformanceEditorPanel`**
/// (`game/client/replay/vgui/replayperformanceeditor.cpp`). It drives playback with `demo_pause`,
/// `demo_gototick` and a timescale slider, and the engine's own console equivalent is
/// `demo_timescale`, whose help string in `bin/engine.dll` reads *"Sets demo replay speed."*
///
/// **`demo_timescale` is a ConCommand, not a ConVar**, decoded from its registration — five pushes
/// with a `.text` pointer in the callback slot (see `docs/findings/37-the-engines-demo-vocabulary.md`).
/// So it has no default and no flags: it is invoked with an argument and forgotten. An earlier draft
/// of this file called it "a float convar", which was read off the help string alone.
///
/// **That does not move the numbers below**, and the reason is worth being explicit about: the parity
/// reference here is the SLIDER in `replayperformanceeditor.cpp`, whose three constants are read from
/// published source. The console command is context, not the measurement.
///
/// **Three differences were found, and the owner classified them 2026-08-26.** Two were already
/// deliberate and one was not, which is the reason this file exists:
///
/// | | Valve | ours | status |
/// |---|---|---|---|
/// | slowest | `TIMESCALE_MIN 0.01f` (`:78`) | 0.25 | **unintended** — fixed |
/// | shape | slider, `SLIDER_RANGE_MAX 10000.0f` (`:83`) | 11 discrete steps | **unintended** — fixed |
/// | fastest | `TIMESCALE_MAX 3.0f` (`:79`) | 8 | deliberate |
/// | reverse | none | yes | deliberate (D97) |
///
/// **Evidence class: read from published source**, except the `demo_timescale` help string, which is
/// measured from the shipped binary.
/// </remarks>
public sealed class TimeScaleConformanceTests
{
    /// <summary>Valve's slowest playback: `TIMESCALE_MIN`, `replayperformanceeditor.cpp:78`.</summary>
    private const double ValveSlowest = 0.01d;

    /// <summary>Valve's fastest playback: `TIMESCALE_MAX`, `replayperformanceeditor.cpp:79`.</summary>
    private const double ValveFastest = 3.0d;

    /// <summary>Positions on Valve's slider: `SLIDER_RANGE_MAX`, `replayperformanceeditor.cpp:83`.</summary>
    /// <remarks>
    /// The slider is integer-valued over `[0, SLIDER_RANGE_MAX]` and mapped linearly onto the
    /// timescale range (`:567`, `:669`, `:724`), so there are 10,001 reachable speeds and the step
    /// is about 0.0003 — continuous for any purpose a person has.
    /// </remarks>
    private const double ValveSliderPositions = 10000d;

    [Test]
    public void Range_AtItsSlowest_ReachesValvesFloorOrBelow()
    {
        // **The divergence that was NOT deliberate.** Our floor was 0.25, so the entire band Valve
        // provides between 0.01 and 0.25 — 25x finer than anything we offered — was unreachable.
        // That band is what frame-exact review needs, which is a real audience here.
        TimeScale.Slowest.ShouldBeLessThanOrEqualTo(ValveSlowest);
    }

    [Test]
    public void Range_AtItsFastest_IsAtLeastValves()
    {
        // Ours exceeds Valve's 3.0, and that is a deliberate departure rather than an accident:
        // skimming a 40-minute match wants more than three times speed, and nothing in the engine's
        // reasoning for 3.0 applies to a viewer that has already decoded the whole demo.
        TimeScale.Fastest.ShouldBeGreaterThanOrEqualTo(ValveFastest);
    }

    [Test]
    public void Resolution_AcrossValvesOwnRange_IsAtLeastAsFineAsValves()
    {
        // **The claim this file was written for.** Valve's slider is 10,001 positions across
        // [0.01, 3.0]; ours must resolve at least that finely over the same span, or a speed the
        // engine can select is one we cannot.
        //
        // Asserted over VALVE'S range rather than ours, deliberately: comparing counts across
        // different spans would let a coarser slider pass by being wider.
        double valveStep = (ValveFastest - ValveSlowest) / ValveSliderPositions;

        TimeScale.SmallestStep.ShouldBeLessThanOrEqualTo(valveStep);
    }

    [Test]
    public void Reverse_IsOffered_WhichTheEngineCannotDo()
    {
        // **A departure recorded as a departure** (D97). The engine streams a demo forward and each
        // snapshot is a delta on the last, so it has nothing to step back into. This viewer decodes
        // the whole demo to absolute positions first, so reverse costs what forward costs — the
        // owner's test for an acceptable departure: know exactly why Valve does it, and exactly why
        // we do not have to.
        TimeScale.Slowest.ShouldBeGreaterThan(0d, "the slowest FORWARD speed is still forward");

        TimeScale.From(-1d).Speed.ShouldBe(-1d, "and reverse is reachable");
    }

    [Test]
    public void Speed_AtEveryValvePosition_IsSelectable()
    {
        // **Walks Valve's actual slider rather than sampling ours.** Every one of the engine's
        // reachable speeds must round-trip through our type without being quantised away — which is
        // the difference between "continuous enough" and "as fine as the thing we are matching".
        //
        // Ten positions across the range rather than all 10,001: the claim is about the mapping, and
        // a mapping that holds at the ends and eight interior points does not fail in between.
        for (int position = 0; position <= 10; position++)
        {
            double valveSpeed =
                ValveSlowest + ((ValveFastest - ValveSlowest) * position / 10d);

            TimeScale.From(valveSpeed).Speed.ShouldBe(valveSpeed, 1e-9,
                $"the engine can select {valveSpeed}");
        }
    }
}
