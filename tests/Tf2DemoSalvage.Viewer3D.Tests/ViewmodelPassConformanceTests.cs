using Tf2DemoSalvage.Viewer3D;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// The separate pass the engine draws viewmodels in.
/// </summary>
/// <remarks>
/// **A viewmodel is not drawn with the world's camera, and that is why placing one in the world
/// list puts it nowhere.** <c>CViewRender::DrawViewModels</c> (<c>viewrender.cpp:1051</c>) copies
/// the view and then replaces three things:
///
/// <code>
/// CViewSetup viewModelSetup( viewRender );
/// viewModelSetup.zNear = viewRender.zNearViewmodel;
/// viewModelSetup.zFar  = viewRender.zFarViewmodel;
/// viewModelSetup.fov   = viewRender.fovViewmodel;
/// ...
/// pRenderContext->DepthRange( 0.0f, 0.1f );
/// </code>
///
/// The origin and the angles are the world view's — the viewmodel really is at the eye — and
/// everything that makes it visible is in the projection and the depth range.
///
/// **The depth range is the reason a gun does not poke through a wall.** Compressing the whole
/// viewmodel into the nearest tenth of the buffer puts it in front of every piece of world geometry
/// without moving it, which is what the engine's own comment calls a hack and what any viewer has
/// to reproduce to get the same picture.
/// </remarks>
public sealed class ViewmodelPassConformanceTests
{
    [Test]
    public void FieldOfView_ForAViewmodel_Is54RatherThanTheWorlds()
    {
        // `viewmodel_fov`, view.cpp:111, default 54 — and clamped between 54 and 70 in the TF2
        // build, so it is not a free choice even for a player.
        //
        // **TF2 uses a separate convar while a demo is playing** and this project is always in that
        // case: ClientModeTFNormal::GetViewModelFOV returns v_viewmodel_fov_demo when
        // engine->IsPlayingDemo(). Its default is the same 54, so the number is unchanged — but the
        // fact that demo playback takes a different path is worth having written down, because a
        // future TF2 could move one and not the other.
        ViewmodelPass.FieldOfView.ShouldBe(54f);
    }

    [Test]
    public void NearPlane_ForAViewmodel_Is1RatherThanTheWorlds7()
    {
        // view.cpp:643, `viewEye.zNearViewmodel = 1`, against VIEW_NEARZ of 7 for the world
        // (view.h:26). A viewmodel sits at the eye and extends a few tens of units, so a seven-unit
        // near plane clips the part nearest the camera — the hands.
        ViewmodelPass.NearPlane.ShouldBe(1f);
        FreeCamera.WorldNearPlane.ShouldBe(7f, "VIEW_NEARZ, which the world pass uses");
    }

    [Test]
    public void DepthRange_ForAViewmodel_IsTheNearestTenth()
    {
        // `pRenderContext->DepthRange( 0.0f, 0.1f )`, viewrender.cpp. Everything drawn in the pass
        // lands in front of the world without being moved.
        ViewmodelPass.DepthMinimum.ShouldBe(0f);
        ViewmodelPass.DepthMaximum.ShouldBe(0.1f);
    }
}
