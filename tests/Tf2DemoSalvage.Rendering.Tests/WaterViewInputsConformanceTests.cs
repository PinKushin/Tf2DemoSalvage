namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// What a Water face samples for each water target while a water view draws (B663).
/// </summary>
/// <remarks>
/// The engine draws a brush entity's water face inside the refraction view and binds the live target it is drawing into
/// (shaderapidx9 <c>0x1800149b0</c>, no guard), which D3D9 leaves undefined. **NOT PARITY:** the lead's pick (D244) is to
/// sample a snapshot of that target, and every other target live. These are the two decisions it rests on, tested
/// without a window: which target a view's draws land in (as <c>DrawWaterViews</c> picks its output), and what each slot
/// binds given that.
/// </remarks>
public sealed class WaterViewInputsConformanceTests
{
    [TestCase(true, false, WaterViewTarget.Reflection)]
    [TestCase(true, true, WaterViewTarget.Reflection)]
    [TestCase(false, false, WaterViewTarget.Refraction)]
    [TestCase(false, true, WaterViewTarget.BackBuffer)]
    public void DrawnInto_ByViewAndRoute_IsTheTargetTheDrawsLandIn(bool reflection, bool viaFrame, WaterViewTarget expected) =>
        WorldRenderer.DrawnInto(reflection, viaFrame).ShouldBe(expected);

    [TestCase(WaterViewTarget.Refraction, WaterViewTarget.BackBuffer, false)]
    [TestCase(WaterViewTarget.Reflection, WaterViewTarget.BackBuffer, false)]
    [TestCase(WaterViewTarget.Refraction, WaterViewTarget.Refraction, true)]
    [TestCase(WaterViewTarget.Reflection, WaterViewTarget.Refraction, false)]
    [TestCase(WaterViewTarget.Reflection, WaterViewTarget.Reflection, true)]
    [TestCase(WaterViewTarget.Refraction, WaterViewTarget.Reflection, false)]
    public void WaterSampleWhileDrawing_BySlotAndTargetDrawnInto_SnapshotsOnlyTheOutput(
        WaterViewTarget slot, WaterViewTarget drawingInto, bool snapshot) =>
        WorldRenderer.WaterSampleWhileDrawing(slot, drawingInto)
            .ShouldBe(snapshot ? WaterViewSample.Snapshot : WaterViewSample.Live);
}
