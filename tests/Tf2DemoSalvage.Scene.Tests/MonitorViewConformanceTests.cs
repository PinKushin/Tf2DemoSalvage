using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary><c>CViewRender::DrawOneMonitor</c>'s view setup (`viewrender.cpp:3168-3238`, B511).</summary>
/// <remarks>
/// <c>monitorView = cameraView</c>, then origin, angles and <c>fov</c> from the camera, <c>m_flAspectRatio</c> 1 unless
/// <c>UseScreenAspectRatio()</c>, and with the camera's fog on <c>zFar = GetFogEnd()</c>.
/// </remarks>
public sealed class MonitorViewConformanceTests
{
    private static readonly FreeCamera Main = new()
    {
        Origin = (1f, 2f, 3f), Angles = (10f, 20f, 0f), FieldOfView = 90f, NearZ = 7f, FarZ = 28_000f, Aspect = 16f / 9f,
    };

    private static ScenePointCamera Mirror(bool screenAspect, SceneFog? fog) =>
        new(747, (-1168f, 900f, 78f), (0f, 270f, 0f), 45f, screenAspect, fog);

    [Test]
    public void Camera_BoardwalksMirrorWithItsFog_IsTheCameraSquareAndFarAtTheFogEnd()
    {
        FreeCamera view = MonitorView.Camera(Mirror(false, new SceneFog(192f, 1024f, 0f, 0f, 0f, 1f)), Main);

        (view.Origin, view.Angles, view.FieldOfView, view.Aspect, view.NearZ, view.FarZ)
            .ShouldBe(((-1168f, 900f, 78f), (0f, 270f, 0f), 45f, 1f, 7f, 1024f));
    }

    [Test]
    public void Camera_WithoutFogAndWithTheScreensAspect_KeepsTheMainViewsFarAndAspect()
    {
        FreeCamera view = MonitorView.Camera(Mirror(true, null), Main);

        (view.Aspect, view.FarZ).ShouldBe((16f / 9f, 28_000f));
    }
}
