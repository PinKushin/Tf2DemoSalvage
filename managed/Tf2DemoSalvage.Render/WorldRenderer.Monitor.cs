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
    internal ComPtr<ID3D11ShaderResourceView> CameraTargetReference()
    {
        if (_cameraView.Handle is null)
        {
            (_cameraTexture, _cameraTarget, _cameraView) = WaterTarget();
        }

        _cameraView.AddRef();
        return _cameraView;
    }

    /// <summary>The monitor's target and depth, which a refracting effect drawn in it copies (B711).</summary>
    internal WaterFrameTarget CameraFrame => new(_cameraTarget, _waterDepth, CameraTargetSize, CameraTargetSize);

    /// <summary>Whether any material binds <c>_rt_Camera</c>, so a frame with nothing to show it skips the pass.</summary>
    public bool HasCameraTarget => _cameraView.Handle is not null;

    /// <summary><c>CViewRender::DrawOneMonitor</c> (`viewrender.cpp:3168-3238`): the scene from a camera, into <c>_rt_Camera</c>.</summary>
    /// <param name="context">The context.</param>
    /// <param name="camera">The monitor view's matrix.</param>
    /// <param name="fog">The fog it draws under — the camera's own when enabled, else the view's.</param>
    /// <param name="drawSkybox">Draws the 2D sky through the matrix — <c>SKYBOX_2DSKYBOX_VISIBLE</c>.</param>
    /// <param name="drawEntities">Draws the opaque then translucent renderables.</param>
    /// <param name="restore">Rebinds the main view's target, viewport and camera afterwards.</param>
    /// <param name="endView">
    /// The close of its <c>DrawExecute</c>, after the translucent world: its pixel visibility tests (`viewrender.cpp:5556-5560`,
    /// B711); null for none.
    /// </param>
    /// <param name="water">
    /// The camera's own water views, drawn first into the global water targets (its <c>DrawWorldAndEntities</c>, B711), with
    /// its target as their frame; null for a simple view.
    /// </param>
    /// <param name="simpleDraws">The water sides its simple view draws, <c>CSimpleWorldView::Setup</c>'s (`:5710`); null for both.</param>
    /// <param name="fogClear">The simple view's eye-in-volume clear colour (`:5772-5780`), or null for none.</param>
    /// <remarks>
    /// <c>Push3DView( monitorView, VIEW_CLEAR_DEPTH | VIEW_CLEAR_COLOR, pRenderTarget )</c> then
    /// <c>ViewDrawScene( false, SKYBOX_2DSKYBOX_VISIBLE, monitorView, 0, VIEW_MONITOR )</c>: cleared, the flat sky, the
    /// world, the renderables, the translucent world. The clear colour is black, as nothing sets another.
    /// </remarks>
    public void DrawMonitor(
        ComPtr<ID3D11DeviceContext> context, float[] camera, SceneFog? fog, Action<float[]>? drawSkybox,
        Action drawEntities, Action restore, Action? endView = null, WaterDraw? water = null,
        ViewDraws? simpleDraws = null, (float Red, float Green, float Blue)? fogClear = null)
    {
        ArgumentNullException.ThrowIfNull(drawEntities);
        ArgumentNullException.ThrowIfNull(restore);

        if (_cameraTarget.Handle is null)
        {
            return;
        }

        // The depth buffer is the water views', the same size and used one view at a time.
        EnsureWaterDepth();

        // **Unbound everywhere first**: a texture cannot be an input and the output at once, and a model on the
        // monitor's own screen could have left it bound in any slot.
        UnbindShaderResources(context);

        ViewDraws drawn = ViewDraws;

        // **Its own water views first** (B711): `ViewDrawScene( …, VIEW_MONITOR )` reaches `DrawWorldAndEntities`, which adds
        // the reflection and refraction from this camera ahead of its world view, into the one `_rt_WaterReflection` and
        // `_rt_WaterRefraction` the main view's overwrite later (`viewrender.cpp:2649-2718`, `:5855-6222`). They leave its
        // flags and height clip for the world below; the target, viewport and camera are bound here after.
        WaterView? own = water is not null ? DrawWaterViews(context, water, static () => { }) : null;

        if (own is null)
        {
            SetWaterView(context, HeightClip.None);
        }

        UnbindShaderResources(context);

        float* black = stackalloc float[4] { 0f, 0f, 0f, 1f };
        Silk.NET.Direct3D11.Viewport viewport = new(0f, 0f, CameraTargetSize, CameraTargetSize, 0f, 1f);

        context.OMSetRenderTarget(_cameraTarget, _waterDepth);
        context.RSSetViewports(1, in viewport);
        context.ClearRenderTargetView(_cameraTarget, black);
        context.ClearDepthStencilView(_waterDepth, (uint)ClearFlag.Depth, 1f, 0);

        SetCamera(
            _device, context, camera, _cameraSwitches.Colours, _cameraSwitches.Specular, _cameraSwitches.Fullbright,
            _cameraSwitches.Debug, _cameraSwitches.Phong, fog);

        drawSkybox?.Invoke(camera);

        // Its view's own sides of the water: the water views' main entry, or the simple view's, which from inside a volume
        // clears to its fog after the sky (`CSimpleWorldView`, `viewrender.cpp:5710`, `:5772-5780`), as the main view's does.
        if (own is { } entry)
        {
            ViewDraws = entry.Draw;
        }
        else
        {
            BeginSimpleView(context, _cameraTarget, simpleDraws ?? AllWaterDraws, fogClear);
        }

        // The monitor's own shadow decals, cut from the surfaces its cull drew (B702), in its world pass.
        _shadowWorldList = ShadowList.Monitor;

        try
        {
            Draw(context);
        }
        finally
        {
            _shadowWorldList = ShadowList.World;
        }

        drawEntities();
        DrawTranslucentWorld(context);

        // `CAboveWaterView::CIntersectionView`, into its own target after its world (`:5958-5961`).
        if (water is not null)
        {
            DrawWaterIntersection(context, water);
        }

        endView?.Invoke();
        ViewDraws = drawn;

        // The main view sets its own after: through its water views, or none for a simple one.
        SetWaterView(context, HeightClip.None);
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
        ReleaseOwnedGammaTwin(_cameraTarget);
        _cameraView.Dispose();
        _cameraTarget.Dispose();
        _cameraTexture.Dispose();
    }
}
