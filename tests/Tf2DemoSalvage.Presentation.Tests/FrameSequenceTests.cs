using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Render;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>The order the stages of a frame run in.</summary>
/// <remarks>
/// **These exist because the order was wrong for as long as it lived in the form** (B203). Every
/// stage had its own tests and all of them passed; nothing could ask what order they ran in,
/// because the answer was a sequence of statements inside a window.
///
/// **The prediction is Valve's, written down before ours was measured** — `view.cpp:778-796` for
/// camera-then-listener, `cdll_client_int.cpp:1308` for simulate-before-render.
/// </remarks>
public sealed class FrameSequenceTests
{
    [Test]
    public void Run_OverAFrame_FollowsTheEnginesStageOrder()
    {
        // The whole claim in one assertion. Valve simulates in `HudUpdate`, before the view exists;
        // then builds the camera; then sets the audio state from that same eye; then vis; then
        // draws.
        RecordingSteps steps = new();

        FrameSequence.Run(steps);

        steps.Ran.ShouldBe(
        [
            "Simulate",
            "PlaceCamera",
            "UpdateListener",
            "ProjectWorld",
            "TakeShot",
            "BuildOverlay",
            "Draw",
        ]);
    }

    [Test]
    public void Run_OverAFrame_HearsFromTheEyeItJustPlaced()
    {
        // **The narrow claim, stated on its own so a failure names the cause.** `SetAudioState` is
        // four statements after `ComputeCameraVariables` and reads the same `viewEye`. Ours ran
        // sound FIRST, so the listener sat where the eye had been on the previous frame — audible
        // as sound lagging the view during a fast turn, and indistinguishable from a wrong
        // panning law.
        RecordingSteps steps = new();

        FrameSequence.Run(steps);

        steps.Ran.IndexOf("UpdateListener")
            .ShouldBeGreaterThan(steps.Ran.IndexOf("PlaceCamera"));
    }

    [Test]
    public void Run_OverAFrame_SimulatesBeforePlacingTheCamera()
    {
        // The other half, also stated alone. `UpdateAllSystems` runs in `HudUpdate` — before the
        // render, not inside it. Ours advanced the demo AFTER uploading the camera, so the frame
        // drew tick T+1's entities through tick T's eye, and the viewmodel camera (rebuilt during
        // the advance) through T+1's.
        RecordingSteps steps = new();

        FrameSequence.Run(steps);

        steps.Ran.IndexOf("Simulate")
            .ShouldBeLessThan(steps.Ran.IndexOf("PlaceCamera"));
    }

    [TestCase("Simulate", nameof(FramePhases.Advance))]
    [TestCase("PlaceCamera", nameof(FramePhases.Camera))]
    [TestCase("UpdateListener", nameof(FramePhases.Sound))]
    [TestCase("ProjectWorld", nameof(FramePhases.Project))]
    [TestCase("TakeShot", nameof(FramePhases.Capture))]
    [TestCase("BuildOverlay", nameof(FramePhases.Hud))]
    [TestCase("Draw", nameof(FramePhases.Draw))]
    public void Run_WithOneSlowStageOfSeven_ChargesTheTimeToThatStagesColumn(
        string stage, string column)
    {
        // **This replaces the pairing test that guarded `FramePhases.Between`** (B203), and is
        // strictly stronger: rather than checking one arithmetic chain it walks EVERY stage and
        // pins it to its column. A stage wired to the wrong field — the mislabelling the old
        // positional shape made easy — fails here whichever pair got swapped.
        //
        // One slow stage at a time, because a single expensive stage is the only condition where
        // correct and mislabelled predict different columns. With every stage equally fast, every
        // possible mapping agrees.
        RecordingSteps steps = new() { SlowStage = stage };

        FramePhases phases = FrameSequence.Run(steps);

        long charged = Column(phases, column);

        foreach (string other in Columns)
        {
            if (other == column)
            {
                continue;
            }

            charged.ShouldBeGreaterThan(
                Column(phases, other),
                $"{stage} was the only slow stage, so {column} should exceed {other}");
        }
    }

    [Test]
    public void Run_OverAFrame_HandsTheOverlayItBuiltToTheDraw()
    {
        // The overlay travels as an argument rather than through a field on the view — the shape
        // that produced B193 and B196, where a moved assignment became a property nobody set.
        RecordingSteps steps = new();

        FrameSequence.Run(steps);

        steps.Drawn.ShouldBeSameAs(steps.Built);
    }

    [Test]
    public void Run_WithNoSteps_Refuses()
    {
        Should.Throw<ArgumentNullException>(() => FrameSequence.Run(steps: null!));
    }

    /// <summary>Every per-stage column of <see cref="FramePhases"/>, excluding the total.</summary>
    private static readonly string[] Columns =
    [
        nameof(FramePhases.Sound),
        nameof(FramePhases.Camera),
        nameof(FramePhases.Project),
        nameof(FramePhases.Advance),
        nameof(FramePhases.Capture),
        nameof(FramePhases.Hud),
        nameof(FramePhases.Draw),
    ];

    /// <summary>Reads one named column, so the mapping can be asserted from a table.</summary>
    private static long Column(FramePhases phases, string column) => column switch
    {
        nameof(FramePhases.Sound) => phases.Sound,
        nameof(FramePhases.Camera) => phases.Camera,
        nameof(FramePhases.Project) => phases.Project,
        nameof(FramePhases.Advance) => phases.Advance,
        nameof(FramePhases.Capture) => phases.Capture,
        nameof(FramePhases.Hud) => phases.Hud,
        nameof(FramePhases.Draw) => phases.Draw,
        _ => throw new ArgumentOutOfRangeException(nameof(column), column, "not a phase column"),
    };

    /// <summary>Records which stage ran when, and can make one of them slow.</summary>
    private sealed class RecordingSteps : IFrameSteps
    {
        private readonly List<HudQuad> _overlay = [];

        /// <summary>The stages that ran, in order.</summary>
        public List<string> Ran { get; } = [];

        /// <summary>A stage to burn time in, or null for none.</summary>
        public string? SlowStage { get; init; }

        /// <summary>What <see cref="BuildOverlay"/> returned.</summary>
        public IReadOnlyList<HudQuad>? Built { get; private set; }

        /// <summary>What <see cref="Draw"/> was given.</summary>
        public IReadOnlyList<HudQuad>? Drawn { get; private set; }

        public void Simulate() => Mark("Simulate");

        public void PlaceCamera() => Mark("PlaceCamera");

        public void UpdateListener() => Mark("UpdateListener");

        public void ProjectWorld() => Mark("ProjectWorld");

        public void TakeShot() => Mark("TakeShot");

        public IReadOnlyList<HudQuad> BuildOverlay()
        {
            Mark("BuildOverlay");
            Built = _overlay;
            return _overlay;
        }

        public void Draw(IReadOnlyList<HudQuad> overlay)
        {
            Mark("Draw");
            Drawn = overlay;
        }

        /// <summary>Notes that a stage ran, spinning if it is the one chosen to be slow.</summary>
        /// <remarks>
        /// **A spin rather than a sleep**, because the ban on `Thread.Sleep` is about
        /// synchronisation and this is neither waiting for nor racing anything — it is making a
        /// measurable amount of work happen. Spinning on the same clock the code under test reads
        /// is also the only way to guarantee the tick count actually moves.
        /// </remarks>
        private void Mark(string stage)
        {
            Ran.Add(stage);

            if (stage != SlowStage)
            {
                return;
            }

            long until = System.Diagnostics.Stopwatch.GetTimestamp()
                + (System.Diagnostics.Stopwatch.Frequency / 200);

            System.Threading.SpinWait.SpinUntil(
                () => System.Diagnostics.Stopwatch.GetTimestamp() >= until);
        }
    }
}
