namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// `CViewRender::Render` widens the view's field of view by the screen's width against 4:3 before it draws (B518).
/// </summary>
/// <remarks>
/// <code>
/// float aspectRatio = engine->GetScreenAspectRatio() * 0.75f;	 // / (4/3)
/// float limitedAspectRatio = aspectRatio;
/// if ( ( sv_restrict_aspect_ratio_fov.GetInt() > 0 &amp;&amp; engine->IsWindowedMode() &amp;&amp; gpGlobals->maxClients > 1 ) || ... == 2 )
///     limitedAspectRatio = MIN( aspectRatio, 1.85f * 0.75f );
/// viewEye.fov = ScaleFOVByWidthRatio( viewEye.fov, limitedAspectRatio );
/// viewEye.fovViewmodel = ScaleFOVByWidthRatio( viewEye.fovViewmodel, aspectRatio );
/// </code>
/// (`view.cpp:1074-1084`). The expected angles are `2·atan(tan(fov/2)·ratio)` worked outside this code, in doubles.
/// </remarks>
public sealed class ViewFovConformanceTests
{
    [Test]
    public void Rendered_AtFourByThree_IsTheFovItself() =>
        new ViewFov(90f, 90).Rendered(4f / 3f).ShouldBe(90f, 1e-3f);

    [Test]
    public void Rendered_AtSixteenByNine_Is106Point26() =>
        new ViewFov(90f, 90).Rendered(16f / 9f).ShouldBe(106.2602f, 1e-3f);

    [Test]
    public void Rendered_WiderThan1Point85_IsCappedAt1Point85()
    {
        // tan(45°)·1.85·0.75 = 1.3875; 2·atan = 108.4379°. 21:9 uncapped would be 120.51°.
        new ViewFov(90f, 90).Rendered(21f / 9f).ShouldBe(108.4379f, 1e-3f);
    }

    /// <remarks>Uncapped, 21:9 is tan(45°)·1.75 = 1.75; 2·atan = 120.5104°.</remarks>
    [Test]
    public void Rendered_WiderThan1Point85Unrestricted_IsNotCapped() =>
        new ViewFov(90f, 90).Rendered(21f / 9f, restricted: false).ShouldBe(120.5104f, 1e-3f);

    /// <remarks>
    /// `view.cpp:1077-1078`: <c>( sv_restrict_aspect_ratio_fov.GetInt() > 0 &amp;&amp; engine->IsWindowedMode() &amp;&amp;
    /// gpGlobals->maxClients > 1 ) || sv_restrict_aspect_ratio_fov.GetInt() == 2</c>. The cvar is the SERVER's, so a demo
    /// carries it; 1 unless the server said otherwise.
    /// </remarks>
    [TestCase(1, true, 24, true)]
    [TestCase(1, false, 24, false, Description = "exclusive full screen is not a window")]
    [TestCase(1, true, 1, false, Description = "a single-player server")]
    [TestCase(0, true, 24, false)]
    [TestCase(2, false, 1, true, Description = "2 restricts everywhere")]
    [TestCase(3, false, 24, false, Description = "above zero, not 2, and not windowed")]
    public void RestrictsAspect_ByTheServersSwitchTheWindowAndTheServersSize_IsTheEnginesCondition(
        int serverSwitch, bool windowed, int maxClients, bool expected) =>
        ViewFov.RestrictsAspect(serverSwitch, windowed, maxClients).ShouldBe(expected);

    [Test]
    public void RenderedViewmodel_WiderThan1Point85_IsNotCapped()
    {
        // The viewmodel takes `aspectRatio`, not `limitedAspectRatio`: tan(27°)·1.75 = 0.89167; 2·atan = 83.4448°.
        new ViewFov(90f, 90).RenderedViewmodel(54f, 21f / 9f).ShouldBe(83.4448f, 1e-3f);
    }

    [Test]
    public void RenderedViewmodel_WhileZoomed_ScalesTheFollowedFov()
    {
        // 54 - (90 - 70) = 34 first (view.cpp:725), then widened: tan(17°)·4/3 = 0.40764; 2·atan = 44.3556°.
        new ViewFov(70f, 90).RenderedViewmodel(54f, 16f / 9f).ShouldBe(44.3556f, 1e-3f);
    }
}
