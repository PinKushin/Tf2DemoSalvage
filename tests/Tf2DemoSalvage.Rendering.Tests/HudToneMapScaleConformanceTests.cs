using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>The tone-map scale the HUD is drawn under: the class portrait's model is the one HUD draw it reaches.</summary>
/// <remarks>
/// `CViewRender::Render` (viewrender.cpp:2211-2216), after `DoEnginePostProcessing` and `PerformScreenSpaceEffects` and
/// before the 2D section (`VGui_Paint`, :2401):
/// <code>
/// if ( g_pMaterialSystemHardwareConfig-&gt;GetHDRType() == HDR_TYPE_INTEGER )
///     pRenderContext-&gt;SetToneMappingScaleLinear(Vector(1,1,1));
/// </code>
/// `CPotteryWheelPanel::Paint` and `CMDLPanel::OnPaint3D` set no scale of their own, so a HUD model draws at 1, not at
/// the scene's exposure. The viewer drew it at the exposure, about 20 levels darker than TF2 at the helmet (finding 78).
/// </remarks>
public sealed class HudToneMapScaleConformanceTests
{
    [Test]
    public void AfterPostProcessing_UnderIntegerHdr_IsOne() =>
        Device3D.ToneMapScaleAfterPostProcessing(HdrType.IntegerHdr, 0.625f).ShouldBe(1f);

    /// <remarks>The control: the reset is gated on `HDR_TYPE_INTEGER`; any other type keeps the scale it had.</remarks>
    [TestCase(HdrType.FloatHdr)]
    [TestCase(HdrType.None)]
    public void AfterPostProcessing_UnderAnotherHdrType_KeepsTheScenesScale(HdrType type) =>
        Device3D.ToneMapScaleAfterPostProcessing(type, 0.625f).ShouldBe(0.625f);
}
