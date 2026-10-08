using System;

using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Render;

/// <summary><c>_rt_Camera</c> and the monitor view drawn into it (B511).</summary>
internal sealed unsafe partial class WorldRenderer
{
    /// <summary><c>tf_monitor_resolution</c> 1024 (`tf_rendertargets.cpp:16,41`), the square <c>_rt_Camera</c> is created at.</summary>
    internal const int CameraTargetSize = 1024;

    private ComPtr<ID3D11Texture2D> _cameraTexture;
    private ComPtr<ID3D11RenderTargetView> _cameraTarget;
    private ComPtr<ID3D11ShaderResourceView> _cameraView;

    /// <summary>How many monitor views drew since the map loaded — the pass's own count, for its tests and the log.</summary>
    public int MonitorViewsDrawn { get; private set; }

    /// <summary>The <c>_rt_Camera</c> view, created on first ask; one more reference for the caller to own.</summary>
    private ComPtr<ID3D11ShaderResourceView> CameraTargetReference()
    {
        if (_cameraView.Handle is null)
        {
            (_cameraTexture, _cameraTarget, _cameraView) = WaterTarget();
        }

        _cameraView.AddRef();
        return _cameraView;
    }

    /// <summary>Whether any material binds <c>_rt_Camera</c>, so a frame with nothing to show it skips the pass.</summary>
    public bool HasCameraTarget => _cameraView.Handle is not null;

    /// <summary><c>CViewRender::DrawOneMonitor</c> (`viewrender.cpp:3168-3238`): the scene from a camera, into <c>_rt_Camera</c>.</summary>
    /// <param name="context">The context.</param>
    /// <param name="camera">The monitor view's matrix.</param>
    /// <param name="fog">The fog it draws under — the camera's own when enabled, else the view's.</param>
    /// <param name="drawSkybox">Draws the 2D sky through the matrix — <c>SKYBOX_2DSKYBOX_VISIBLE</c>.</param>
    /// <param name="drawEntities">Draws the opaque then translucent renderables.</param>
    /// <param name="restore">Rebinds the main view's target, viewport and camera afterwards.</param>
    /// <remarks>
    /// <c>Push3DView( monitorView, VIEW_CLEAR_DEPTH | VIEW_CLEAR_COLOR, pRenderTarget )</c> then
    /// <c>ViewDrawScene( false, SKYBOX_2DSKYBOX_VISIBLE, monitorView, 0, VIEW_MONITOR )</c>: cleared, the flat sky, the
    /// world, the renderables, the translucent world. The clear colour is black, as nothing sets another.
    /// </remarks>
    public void DrawMonitor(
        ComPtr<ID3D11DeviceContext> context, float[] camera, SceneFog? fog, Action<float[]>? drawSkybox,
        Action drawEntities, Action restore)
    {
        ArgumentNullException.ThrowIfNull(drawEntities);
        ArgumentNullException.ThrowIfNull(restore);

        if (_cameraTarget.Handle is null)
        {
            return;
        }

        // The depth buffer is the water views', the same size and used one view at a time.
        EnsureWaterTargets();

        // **Unbound everywhere first**: a texture cannot be an input and the output at once, and a model on the
        // monitor's own screen could have left it bound in any slot.
        UnbindShaderResources(context);

        float* black = stackalloc float[4] { 0f, 0f, 0f, 1f };
        Silk.NET.Direct3D11.Viewport viewport = new(0f, 0f, CameraTargetSize, CameraTargetSize, 0f, 1f);

        context.OMSetRenderTargets(1u, _cameraTarget.GetAddressOf(), _waterDepth);
        context.RSSetViewports(1, in viewport);
        context.ClearRenderTargetView(_cameraTarget, black);
        context.ClearDepthStencilView(_waterDepth, (uint)ClearFlag.Depth, 1f, 0);

        SetCamera(
            _device, context, camera, _cameraSwitches.Colours, _cameraSwitches.Specular, _cameraSwitches.Fullbright,
            _cameraSwitches.Debug, _cameraSwitches.Phong, fog);

        drawSkybox?.Invoke(camera);

        // ponytail: the world draws the MAIN view's visible set and simple water, not a vis of its own from the camera;
        // a camera that sees leaves the player cannot would show holes. Recompute the PVS from the camera if one does.
        ViewDraws drawn = ViewDraws;

        ViewDraws = AllWaterDraws;
        Draw(context);
        drawEntities();
        DrawTranslucentWorld(context);
        ViewDraws = drawn;

        UnbindShaderResources(context);
        MonitorViewsDrawn++;
        restore();
    }

    private static void UnbindShaderResources(ComPtr<ID3D11DeviceContext> context)
    {
        ComPtr<ID3D11ShaderResourceView> none = default;

        for (uint slot = 0; slot < 16; slot++)
        {
            context.PSSetShaderResources(slot, 1, ref none);
        }
    }

    private void DisposeMonitor()
    {
        _cameraView.Dispose();
        _cameraTarget.Dispose();
        _cameraTexture.Dispose();
    }
}
