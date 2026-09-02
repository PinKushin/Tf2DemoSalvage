using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// A keyframe is interpolated from when its value APPLIED, not from when the packet arrived.
/// </summary>
/// <remarks>
/// **<c>OnLatchInterpolatedVariables</c> stamps a history entry with the entity's own clock**, not
/// with the packet: <c>GetLastChangeTime</c> returns <c>GetSimulationTime()</c> for every
/// simulation-latched variable, origin and angles among them (<c>c_baseentity.cpp:2806</c>). This
/// project stamped both with the packet tick, and on a SourceTV recording a player's simulation
/// tick differs from the packet's by four ticks on exactly half its updates — 60 ms of jitter on
/// the fastest things on screen, shared by nothing else (B273).
///
/// **The list is still keyed by arrival and that is deliberate.** Keying it by the applied time was
/// tried first: an entity that does not simulate keeps one simulation time for minutes, so every
/// state change it made collapsed onto a single tick, and `NoDrawTrackTests` caught an entity that
/// was hidden and never handed back. A pose carries more than the interpolated quantities.
/// </remarks>
public sealed class AppliedTimeTests
{
    [Test]
    public void At_AKeyframeThatAppliedBeforeItArrived_InterpolatesFromTheEarlierTime()
    {
        ScenePropTrack track = new(entityIndex: 3, "models/props/crate.mdl");

        track.Add(0, new ScenePose { X = 0f }, appliedAt: 0);

        // Arrived at 20, but the entity says it applied at 10 — so by the time anything samples
        // between them, the whole move is already over.
        track.Add(20, new ScenePose { X = 100f }, appliedAt: 10);

        // **Sampled at 20, which is two conditions at once.** The keyframe must have ARRIVED — a
        // client cannot be pulled toward an update it has not received, which is the causality rule
        // `At` applies against the arrival tick — and the drawn moment, an interpolation delay
        // behind at tick 12, must be past the APPLIED time of 10.
        ScenePose at = track.At(20d)!.Value;

        at.X.ShouldBe(100f, "the value applied at tick 10 and the drawn moment is past it");
    }

    /// <remarks>
    /// **The control**: the same two keyframes with no lag must still be mid-move at that sample,
    /// because the value then applied at 20 rather than 10. Without this the test above passes
    /// against code that ignores the applied time and simply clamps early.
    /// </remarks>
    [Test]
    public void At_TheSameKeyframesWithNoLag_AreStillPartWayThrough()
    {
        ScenePropTrack track = new(entityIndex: 3, "models/props/crate.mdl");

        track.Add(0, new ScenePose { X = 0f });
        track.Add(20, new ScenePose { X = 100f });

        ScenePose at = track.At(20d)!.Value;

        at.X.ShouldBeGreaterThan(0f, "the same sample must be PART WAY when nothing lagged");
        at.X.ShouldBeLessThan(100f);
    }

    /// <remarks>
    /// **A repeat records the applied time of the RESTATEMENT**, which is what keeps the hold
    /// interval on the same clock as the endpoints. Mixing an arrival tick into that fraction is
    /// the class of defect this whole entry is about, one level down.
    /// </remarks>
    [Test]
    public void At_ARepeatedPose_HoldsUntilTheRestatementsAppliedTime()
    {
        ScenePropTrack track = new(entityIndex: 3, "models/props/crate.mdl");

        track.Add(0, new ScenePose { X = 0f }, appliedAt: 0);

        // Restated unchanged, arriving at 40 and applying at 30: the entity was still there then.
        track.Add(40, new ScenePose { X = 0f }, appliedAt: 30);
        track.Add(50, new ScenePose { X = 100f }, appliedAt: 50);

        // Drawn at 42, between the restatement's applied time of 30 and the move's of 50.
        ScenePose at = track.At(50d)!.Value;

        // Without the hold record the fraction would run from tick 0 and read 80 units along;
        // from the restatement it is halfway, and the spline puts it near the middle.
        at.X.ShouldBeGreaterThan(20f);
        at.X.ShouldBeLessThan(80f);
    }
}
