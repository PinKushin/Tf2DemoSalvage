namespace Tf2DemoSalvage.Viewer3D;

/// <summary>
/// The projection and depth range the engine draws viewmodels with.
/// </summary>
/// <remarks>
/// **A viewmodel is drawn in its own pass, and everything that makes it visible is here.**
/// <c>CViewRender::DrawViewModels</c> (<c>viewrender.cpp:1051</c>) takes the world's view, keeps its
/// origin and angles, and replaces the field of view, the near plane and the depth range:
///
/// <code>
/// CViewSetup viewModelSetup( viewRender );
/// viewModelSetup.zNear = viewRender.zNearViewmodel;   // 1, view.cpp:643
/// viewModelSetup.zFar  = viewRender.zFarViewmodel;    // the world's far
/// viewModelSetup.fov   = viewRender.fovViewmodel;     // viewmodel_fov, 54
/// ...
/// pRenderContext->DepthRange( 0.0f, 0.1f );
/// </code>
///
/// This project put the model in the world list with the world's camera instead, and it was packed,
/// posed, instanced and listed for drawing while being nowhere on screen. Three offsets were tried
/// against that before the pass was read — see <c>docs/findings/30-viewmodel-drawing.md</c>.
/// </remarks>
internal static class ViewmodelPass
{
    /// <summary>The field of view a viewmodel is drawn with, in degrees.</summary>
    /// <remarks>
    /// <c>viewmodel_fov</c>, <c>view.cpp:111</c>, default 54 and clamped to 54..70 in the TF2 build
    /// so a player cannot set it freely either.
    ///
    /// **TF2 reads a different convar while a demo plays**, which is this project's only case:
    /// <c>ClientModeTFNormal::GetViewModelFOV</c> returns <c>viewmodel_fov_demo</c> when
    /// <c>engine->IsPlayingDemo()</c>. Its default is the same 54, so the number does not change —
    /// but the two could diverge in a future build, and the one that applies here is the demo one.
    /// </remarks>
    public const float FieldOfView = 54f;

    /// <summary>How near a viewmodel may be drawn, in units.</summary>
    /// <remarks>
    /// <c>viewEye.zNearViewmodel = 1</c>, <c>view.cpp:643</c>, against <c>VIEW_NEARZ</c> of 7 for
    /// the world. A viewmodel is AT the eye, so the world's near plane cuts the hands off.
    /// </remarks>
    public const float NearPlane = 1f;

    /// <summary>The nearest depth a viewmodel is written at.</summary>
    public const float DepthMinimum = 0f;

    /// <summary>The furthest depth a viewmodel is written at.</summary>
    /// <remarks>
    /// **This is what keeps a gun out of a wall.** Compressing the pass into the nearest tenth of
    /// the depth buffer puts every viewmodel in front of all world geometry without moving it an
    /// inch — the engine's own comment calls it a hack, and reproducing it is the difference between
    /// a weapon in hand and a weapon buried in whatever the player is standing next to.
    /// </remarks>
    public const float DepthMaximum = 0.1f;
}
