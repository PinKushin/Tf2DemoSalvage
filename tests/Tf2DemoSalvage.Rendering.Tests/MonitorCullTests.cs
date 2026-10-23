using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// The monitor view culls the world from its own camera, into lists of its own (finding 98 §9 step 0, B511, B581).
/// </summary>
/// <remarks>
/// **The defect this replaces**: `DrawMonitorPass` cached `Culling.Batches(...)` from the main view's culling, which
/// returns ONE reused list. The next main cull refilled it, so from the second frame on the monitor drew the main
/// view's runs; and the monitor's own cull, on its first frame, refilled the main view's list under the main draw.
/// The engine builds the monitor's visible set from its own camera (`ViewDrawScene` from `DrawMonitors`).
/// </remarks>
public sealed class MonitorCullTests
{
    [Test]
    public void Batches_AfterTheMainViewCullsSomethingElse_StillHoldTheMonitorsOwnRuns()
    {
        WorldCulling main = TwoFaceMap.Culling();
        MonitorCull monitor = new(main);
        ScenePointCamera camera = Camera();

        IReadOnlyList<WorldBatch> seen = monitor.Batches(camera, 0f, 0f, 0f, TwoFaceMap.Everything()).ShouldNotBeNull();

        seen.Count.ShouldBe(2, "the control: the monitor's camera sees both faces");

        // The main view moves on and sees nothing; through the shared culling this emptied the monitor's list.
        (main.Batches(0f, 0f, 0f, ViewFrustum.Nothing)?.Count ?? 0).ShouldBe(0);

        monitor.Batches(camera, 0f, 0f, 0f, TwoFaceMap.Everything()).ShouldNotBeNull().Count.ShouldBe(2);
        seen.Count.ShouldBe(2);
    }

    [Test]
    public void Batches_OnTheMonitorsFirstCull_LeaveTheMainViewsRunsStanding()
    {
        WorldCulling main = TwoFaceMap.Culling();
        IReadOnlyList<WorldBatch> mainRuns = main.Batches(0f, 0f, 0f, TwoFaceMap.Everything()).ShouldNotBeNull();
        MonitorCull monitor = new(main);

        (monitor.Batches(Camera(), 0f, 0f, 0f, ViewFrustum.Nothing)?.Count ?? 0).ShouldBe(0, "the control: the monitor sees nothing");

        mainRuns.Count.ShouldBe(2, "the monitor's cull rewrote the main view's runs under its draw");
    }

    [Test]
    public void Batches_ForTheSameCamera_CullOnce()
    {
        WorldCulling main = TwoFaceMap.Culling();
        MonitorCull monitor = new(main);
        ScenePointCamera camera = Camera();

        _ = monitor.Batches(camera, 0f, 0f, 0f, TwoFaceMap.Everything());

        // A still monitor camera keeps its runs whatever frustum the next frame passes, as the cache it replaces did.
        monitor.Batches(camera, 0f, 0f, 0f, ViewFrustum.Nothing).ShouldNotBeNull().Count.ShouldBe(2);
        monitor.Batches(Camera(fieldOfView: 60f), 0f, 0f, 0f, ViewFrustum.Nothing).ShouldNotBeNull().Count.ShouldBe(0, "a moved camera culls again");
    }

    [Test]
    public void Batches_WithNoCulling_AreNull() =>
        new MonitorCull(null).Batches(Camera(), 0f, 0f, 0f, TwoFaceMap.Everything()).ShouldBeNull();

    private static ScenePointCamera Camera(float fieldOfView = 90f) =>
        new(EntityIndex: 5, Origin: (0f, 0f, 0f), Angles: (0f, 0f, 0f), FieldOfView: fieldOfView, UseScreenAspectRatio: false, Fog: null);
}
