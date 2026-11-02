namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// Which water targets a Water face may sample while a water view draws (B663): never the one being drawn into.
/// </summary>
/// <remarks>
/// The engine has no gate for a brush entity's water face inside a refraction or reflection view (B659's read), and what
/// D3D9 returns when a shader samples the bound render target is unread. D3D11 nulls such an input, so ours binds it as
/// nothing explicitly. These are the two decisions that choice rests on, tested without a window: which target a view's
/// draws land in (as <c>DrawWaterViews</c> picks its output), and which inputs that leaves bound.
/// </remarks>
public sealed class WaterViewInputsConformanceTests
{
    [TestCase(true, false, WaterViewTarget.Reflection)]
    [TestCase(true, true, WaterViewTarget.Reflection)]
    [TestCase(false, false, WaterViewTarget.Refraction)]
    [TestCase(false, true, WaterViewTarget.BackBuffer)]
    public void DrawnInto_ByViewAndRoute_IsTheTargetTheDrawsLandIn(bool reflection, bool viaFrame, WaterViewTarget expected) =>
        WorldRenderer.DrawnInto(reflection, viaFrame).ShouldBe(expected);

    [TestCase(WaterViewTarget.BackBuffer, true, true)]
    [TestCase(WaterViewTarget.Refraction, false, true)]
    [TestCase(WaterViewTarget.Reflection, true, false)]
    public void WaterInputsWhileDrawing_ByTargetDrawnInto_NeverTheOutput(WaterViewTarget drawingInto, bool refraction, bool reflection) =>
        WorldRenderer.WaterInputsWhileDrawing(drawingInto).ShouldBe((refraction, reflection));
}
