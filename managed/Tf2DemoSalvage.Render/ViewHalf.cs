using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Render;

/// <summary>One half of the view state `SetCamera` hands the draw: its own culling, its own runs, and what the cull decided
/// beside them (B581 rung 2, D228).</summary>
/// <remarks>
/// **Two of these, used alternately, so the next frame's cull can run while the previous frame draws** — the engine's
/// queued context records into one frame while the render thread plays the other (finding 99). Each half owns a
/// <see cref="WorldCulling"/> and an <see cref="OverlayRenderLists"/> of its own, because both reuse the lists they
/// return: through one object, a cull for frame N+1 would rewrite frame N's runs under its draw.
///
/// **Each half remembers the view it culled for**, so a still camera still pays nothing: each half culls once, then both
/// skip. **Everything else `SetCamera` decides lives here too** (finding 98 §9 step 1): the sky decision, the water
/// plan, the frustums and eyes, the world camera and the detail sprites, so the draw reads only the half it was handed
/// (<see cref="ViewHalves.Job"/>).
/// </remarks>
internal sealed class ViewHalf
{
    /// <summary>This half's culling of the map, or null on a map without one.</summary>
    public WorldCulling? Culling { get; set; }

    /// <summary>This half's overlay queues.</summary>
    public OverlayRenderLists? OverlayLists { get; set; }

    /// <summary>The view this half's runs were culled for, or null to cull on its next camera.</summary>
    public ((float X, float Y, float Z) Origin, (float Pitch, float Yaw, float Roll) Angles,
            float Fov, float Near, float Far, float Aspect)? CulledFor { get; set; }

    /// <summary>The world's visible runs; null for "draw every batch".</summary>
    public IReadOnlyList<WorldBatch>? VisibleBatches { get; set; }

    /// <summary>The blended surfaces by leaf place.</summary>
    public TranslucentLeafRuns? TranslucentLeaves { get; set; }

    /// <summary>The opaque world's overlays, in draw order.</summary>
    public IReadOnlyList<WorldBatch>? Overlays { get; set; }

    /// <summary>The 3D skybox room's runs.</summary>
    public IReadOnlyList<WorldBatch> SkyBatches { get; set; } = [];

    /// <summary>The sky view's own overlay queues, since a queue reuses the lists it returns (B711).</summary>
    public OverlayRenderLists? SkyOverlayLists { get; set; }

    /// <summary>
    /// The sky view's world list beside its runs (B711): its blended runs by its own leaf places and its overlays, from the
    /// same walk; null with no sky view. Its pass installs them while it draws.
    /// </summary>
    public (TranslucentLeafRuns? Translucent, IReadOnlyList<WorldBatch> Overlays)? SkyLists { get; set; }

    /// <summary>What the eye's leaf could see of the sky.</summary>
    public SkyboxVisibility SkyVisible { get; set; }

    /// <summary>Where the cull's camera stood: the 2D sky's centre.</summary>
    public (float X, float Y, float Z) Eye { get; set; }

    /// <summary>Whether this half has culled for a camera, so <see cref="Eye"/> means something.</summary>
    public bool Placed { get; set; }

    /// <summary>The sky view's frustum.</summary>
    public ViewFrustum SkyFrustum { get; set; }

    /// <summary>The sky view's eye, for its translucent sort.</summary>
    public (float X, float Y, float Z) SkyEye { get; set; }

    /// <summary>The sky view's matrix, or null when there is no sky room to draw.</summary>
    public float[]? SkyCamera { get; set; }

    /// <summary>The sky view's camera, which its effects face and its glow proxies are built in (B711); null with no sky room.</summary>
    public FreeCamera? SkyView { get; set; }

    /// <summary>The main view's water decision and its water views' lists (B62, B711).</summary>
    public WaterPlan Water { get; } = new();

    /// <summary>
    /// The monitor views this frame draws, in <c>DrawMonitors</c>' order (B715): the first <see cref="MonitorCount"/> of the
    /// pool, each kept at its place across frames. Emptied with a new map's culling or world.
    /// </summary>
    public List<MonitorHalf> Monitors { get; } = [];

    /// <summary>How many of <see cref="Monitors"/> draw this frame.</summary>
    public int MonitorCount { get; set; }

    /// <summary>The monitor views the scene collates, those of <see cref="Monitors"/> it can, in order; reused, as <see cref="CollatedWater"/>.</summary>
    public List<MonitorRenderableView> MonitorViews { get; } = [];

    /// <summary>The water views the scene collates, each monitor's then the main view's (<see cref="WaterPlan.Collate"/>), reused.</summary>
    public List<RenderableView> CollatedWater { get; } = [];

    /// <summary>The main view's frustum.</summary>
    public ViewFrustum Frustum { get; set; }

    /// <summary>The main view the static props' screen fades measure through (B432).</summary>
    public Core.Scene.ScreenFadeView? ScreenView { get; set; }

    /// <summary>The translucent sort's eye: <c>vecRenderOrigin</c>.</summary>
    public (float X, float Y, float Z) TranslucentEye { get; set; }

    /// <summary>The translucent sort's axis: <c>vecRenderForward</c>.</summary>
    public (float X, float Y, float Z) TranslucentForward { get; set; } = (1f, 0f, 0f);

    /// <summary>The sky view's screen, for its shadows' areas; null when there is no sky room to draw.</summary>
    public Core.Scene.ScreenFadeView? SkyScreen { get; set; }

    /// <summary>The world camera the draw sends and every pass restores.</summary>
    public (float[] Matrix, bool Colours)? WorldCamera { get; set; }

    /// <summary>The main view's detail build, around its eye.</summary>
    public DetailBuild Detail { get; } = new();

    /// <summary>The 3D sky view's own, around the sky camera (B711); empty when no sky view draws.</summary>
    public DetailBuild SkyDetail { get; } = new();

    /// <summary>The per-frame feeds and material switches this half's draw reads, taken at its hand-off (finding 98 §9 step 4).</summary>
    public DrawFeeds Feeds { get; set; }

    private CameraUpload? _pendingCamera;

    /// <summary>Records the world camera for this half's draw to send, replacing any recorded since its last draw.</summary>
    /// <param name="upload">The values, captured now on the game thread.</param>
    public void RecordCamera(CameraUpload upload) => _pendingCamera = upload;

    /// <summary>The camera recorded since this half's last draw, which this forgets.</summary>
    /// <returns>The values, or null when nothing was recorded.</returns>
    public CameraUpload? TakeCamera()
    {
        CameraUpload? taken = _pendingCamera;
        _pendingCamera = null;

        return taken;
    }

    /// <summary>Hands this half's runs to the world renderer, which draws from them.</summary>
    /// <param name="world">The renderer.</param>
    public void InstallInto(WorldRenderer world)
    {
        world.VisibleBatches = VisibleBatches;
        world.TranslucentLeaves = TranslucentLeaves;
        world.Overlays = Overlays;
        world.SkyBatches = SkyBatches;
    }

    /// <summary>Forgets the detail sprites and models, for a new map's props.</summary>
    public void ClearDetail()
    {
        Detail.Clear();
        SkyDetail.Clear();
        Water.ClearDetail();

        foreach (MonitorHalf monitor in Monitors)
        {
            monitor.Detail.Clear();
            monitor.Water.ClearDetail();
        }
    }

    /// <summary>Forgets every water plan, for a new map's materials.</summary>
    public void ClearWater()
    {
        Water.Clear();
        CollatedWater.Clear();

        foreach (MonitorHalf monitor in Monitors)
        {
            monitor.Water.Clear();
            monitor.WaterFor = null;
            monitor.WaterViews = 0;
        }
    }
}

/// <summary>A world camera upload as the game thread decided it, sent when its half's draw begins (finding 98 §9 step 2).</summary>
/// <param name="Matrix">The view-projection, sixteen floats.</param>
/// <param name="Colours">Flat category colours instead of textures.</param>
/// <param name="Specular"><c>mat_specular</c>.</param>
/// <param name="Fullbright"><c>mat_fullbright</c>.</param>
/// <param name="Debug">The debug views.</param>
/// <param name="Phong"><c>mat_phong</c>.</param>
/// <param name="Fog">The main view's fog.</param>
internal readonly record struct CameraUpload(
    float[] Matrix, bool Colours, bool Specular, Fullbright Fullbright, DebugModes Debug, bool Phong,
    Tf2DemoSalvage.Core.Scene.SceneFog? Fog);

/// <summary>What the game thread fed the device for one frame, as the draw of that frame reads it (finding 98 §9 step 4).</summary>
/// <remarks>Taken whole at the hand-off (<see cref="ViewHalves.Hand"/>), so a setter for the next frame never reaches a draw
/// already handed: the device's own fields are the game thread's, and the draw reads only this.</remarks>
/// <param name="Specular"><c>mat_specular</c>.</param>
/// <param name="Fullbright"><c>mat_fullbright</c>.</param>
/// <param name="Debug">The debug views.</param>
/// <param name="Phong"><c>mat_phong</c>.</param>
/// <param name="WorldFog">The world's fog.</param>
/// <param name="SkyFog">The 3D sky's fog.</param>
/// <param name="Tonemap">The <c>env_tonemap_controller</c> overrides.</param>
/// <param name="ExposureSeconds">The game time the exposure walks by.</param>
/// <param name="ResetToneMapping">The followed player changed since the last hand-off: the draw resets the tone mapping
/// first, as <c>SetObserverTarget</c>'s <c>ResetToneMapping( 1.0 )</c> did on the game thread.</param>
internal readonly record struct DrawFeeds(
    bool Specular, Fullbright Fullbright, DebugModes Debug, bool Phong,
    Tf2DemoSalvage.Core.Scene.SceneFog? WorldFog, Tf2DemoSalvage.Core.Scene.SceneFog? SkyFog,
    Tf2DemoSalvage.Core.Scene.SceneTonemap Tonemap,
    float ExposureSeconds, bool ResetToneMapping);
