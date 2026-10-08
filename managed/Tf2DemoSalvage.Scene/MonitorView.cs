using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>The view <c>CViewRender::DrawOneMonitor</c> builds from a <c>point_camera</c> (B511).</summary>
public static class MonitorView
{
    /// <summary>The monitor view: the main view's setup with the camera's origin, angles and FOV (`viewrender.cpp:3196-3224`).</summary>
    /// <param name="camera">The camera on the monitor.</param>
    /// <param name="main">The main view, whose near and far planes and aspect it starts from.</param>
    /// <returns>The camera to render <c>_rt_Camera</c> through.</returns>
    /// <remarks>
    /// <c>m_flAspectRatio</c> is 1 unless <c>m_bUseScreenAspectRatio</c>, which leaves 0 — "use the viewport's", and the
    /// viewport is the square target, so it is 1 there too; the screen's aspect is taken as the flag's name says.
    /// <i>Interpolated</i>: how a 0 aspect resolves inside <c>Push3DView</c> is not in the SDK. With the camera's fog
    /// enabled, <c>zFar</c> is the fog's end.
    /// </remarks>
    public static FreeCamera Camera(ScenePointCamera camera, FreeCamera main)
    {
        System.ArgumentNullException.ThrowIfNull(main);

        return new FreeCamera
        {
            Origin = camera.Origin,
            Angles = camera.Angles,
            FieldOfView = camera.FieldOfView,
            NearZ = main.NearZ,
            FarZ = camera.Fog is { } fog ? fog.End : main.FarZ,
            Aspect = camera.UseScreenAspectRatio ? main.Aspect : 1f,
        };
    }
}
