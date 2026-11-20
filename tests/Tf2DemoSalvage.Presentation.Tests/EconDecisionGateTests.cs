namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>
/// When a frame's pose decides its econ effects, and whether that decision landed (B69 g; review: the wiring had no test that
/// could fail). <c>ProcessOnDataChangedEvents</c> and <c>SimulateEntities</c> run once per simulated frame
/// (cdll_client_int.cpp:2193-2239); a skip's frames and a seek's replay draw no effects (B708), and the landing decides afresh
/// (D221) — told so whichever reader took the skip first.
/// </summary>
public sealed class EconDecisionGateTests
{
    /// <summary>
    /// **A skip's frames decide nothing, the landing decides as landed, and the frame after as played**: the skip is taken
    /// on the first skipping frame and held until a decision runs.
    /// </summary>
    [Test]
    public void Next_ASkipThenItsLanding_DecidesNothingThenLandedThenPlayed()
    {
        EconDecisionGate gate = new();
        FrameSkip skip = new(500, 2, 2) { Skipped = true };

        bool? skipping = gate.Next(skip, skipping: true, replaying: false, hasSystems: true);
        bool? landing = gate.Next(skip, skipping: false, replaying: false, hasSystems: true);
        bool? after = gate.Next(skip with { Generation = 3 }, skipping: false, replaying: false, hasSystems: true);

        (skipping, landing, after).ShouldBe((null, true, false));
    }

    /// <summary>A seek's replay poses decide nothing and do not spend the landing; the control is the frame that follows.</summary>
    [Test]
    public void Next_AReplayPose_DecidesNothingAndKeepsTheLanding()
    {
        EconDecisionGate gate = new();
        FrameSkip skip = new(500, 2, 2) { Skipped = true };

        bool? replay = gate.Next(skip, skipping: false, replaying: true, hasSystems: true);
        bool? landing = gate.Next(skip, skipping: false, replaying: false, hasSystems: true);

        (replay, landing).ShouldBe((null, true));
    }

    /// <summary>A level with no particle systems decides nothing and keeps the landing for the first frame that can.</summary>
    [Test]
    public void Next_AFrameWithNoSystems_KeepsTheLanding()
    {
        EconDecisionGate gate = new();
        FrameSkip skip = new(500, 2, 2) { Skipped = true };

        (gate.Next(skip, false, false, hasSystems: false), gate.Next(skip, false, false, hasSystems: true)).ShouldBe((null, true));
    }

    /// <summary>The control: played frames decide as played.</summary>
    [Test]
    public void Next_PlayedFrames_DecideAsPlayed()
    {
        EconDecisionGate gate = new();
        FrameSkip played = new(500, 2, 0);

        (gate.Next(played, false, false, true), gate.Next(played with { Generation = 3 }, false, false, true)).ShouldBe((false, false));
    }
}
