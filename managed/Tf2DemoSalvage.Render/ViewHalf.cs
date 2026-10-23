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

    /// <summary>The water views, or null when the frame is one simple view (B62).</summary>
    public WaterDraw? WaterDraw { get; set; }

    /// <summary>The visible volume's own fog the main view draws under from inside it.</summary>
    public Tf2DemoSalvage.Core.Scene.SceneFog? WaterMainFog { get; set; }

    /// <summary>The main view's draw flags when it is a <c>CSimpleWorldView</c>.</summary>
    public ViewDraws SimpleViewDraws { get; set; } = WorldRenderer.AllWaterDraws;

    /// <summary>The main view's frustum.</summary>
    public ViewFrustum Frustum { get; set; }

    /// <summary>The main view the static props' screen fades measure through (B432).</summary>
    public Core.Scene.ScreenFadeView? ScreenView { get; set; }

    /// <summary>The translucent sort's eye: <c>vecRenderOrigin</c>.</summary>
    public (float X, float Y, float Z) TranslucentEye { get; set; }

    /// <summary>The translucent sort's axis: <c>vecRenderForward</c>.</summary>
    public (float X, float Y, float Z) TranslucentForward { get; set; } = (1f, 0f, 0f);

    /// <summary>The monitor's view, or null for none (B511).</summary>
    public FreeCamera? MonitorView { get; set; }

    /// <summary>The world camera the draw sends and every pass restores.</summary>
    public (float[] Matrix, bool Colours)? WorldCamera { get; set; }

    /// <summary>What the detail sprites were last built for.</summary>
    public (float X, float Y, float Z, float Distance, float Fade, (float, float, float) Forward)? DetailBuiltFor { get; set; }

    /// <summary>The detail sprites' quads.</summary>
    public List<DetailSpriteVertex> DetailCorners { get; } = [];

    /// <summary>The quads by BSP leaf, with the translucent pass's per-leaf cursor (B434).</summary>
    public DetailSpriteLeaves DetailLeaves { get; } = new();

    /// <summary>This view's detail models as placed.</summary>
    public List<DetailModelInstance> DetailModelsPlaced { get; } = [];

    /// <summary>The same, as instances the model pass draws.</summary>
    public List<ModelInstance> DetailModelInstances { get; } = [];

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
        DetailCorners.Clear();
        DetailLeaves.Clear();
        DetailModelsPlaced.Clear();
        DetailModelInstances.Clear();
        DetailBuiltFor = null;
    }

    /// <summary>Forgets the water plan, for a new map's materials.</summary>
    public void ClearWater()
    {
        WaterDraw = null;
        WaterMainFog = null;
        SimpleViewDraws = WorldRenderer.AllWaterDraws;
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
/// <param name="MonitorCamera">The monitor's <c>point_camera</c>, or null.</param>
/// <param name="Tonemap">The <c>env_tonemap_controller</c> overrides.</param>
/// <param name="ExposureSeconds">The game time the exposure walks by.</param>
/// <param name="ResetToneMapping">The followed player changed since the last hand-off: the draw resets the tone mapping
/// first, as <c>SetObserverTarget</c>'s <c>ResetToneMapping( 1.0 )</c> did on the game thread.</param>
internal readonly record struct DrawFeeds(
    bool Specular, Fullbright Fullbright, DebugModes Debug, bool Phong,
    Tf2DemoSalvage.Core.Scene.SceneFog? WorldFog, Tf2DemoSalvage.Core.Scene.SceneFog? SkyFog,
    Tf2DemoSalvage.Core.Scene.ScenePointCamera? MonitorCamera, Tf2DemoSalvage.Core.Scene.SceneTonemap Tonemap,
    float ExposureSeconds, bool ResetToneMapping);
