using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene;

/// <summary>The device as the viewer's presenters drive it (D214): declared here, implemented by Render's device.</summary>
/// <remarks>
/// **So a presenter can hand the device what it decided, and the window names neither the decision nor its types**
/// [no engine counterpart: the engine's renderer is reached through `IVRenderView` and `IMaterialSystem`, which game
/// code holds directly]. The settings a new device is given, and the frame: the view's setup beside the camera, what
/// the pose culls against, and the draw.
/// </remarks>
public interface IViewerDevice
{
    /// <summary>`mat_vsync`.</summary>
    public bool VerticalSync { get; set; }

    /// <summary>`mat_phong`.</summary>
    public bool Phong { get; set; }

    /// <summary>`cl_drawmonitors`.</summary>
    public bool DrawMonitors { get; set; }

    /// <summary>`mat_disable_bloom`.</summary>
    public bool DisableBloom { get; set; }

    /// <summary>`mat_force_tonemap_scale`; zero is the automatic exposure.</summary>
    public float ForceToneMapScale { get; set; }

    /// <summary>`cl_drawhud`.</summary>
    public bool DrawHud { get; set; }

    /// <summary>`mat_queue_mode`: 0 draws on the game thread, 2 on a render thread, -1 the engine's default for this machine.</summary>
    public int QueueMode { get; set; }

    /// <summary>`r_3dsky`.</summary>
    public int Draw3dSky { get; set; }

    /// <summary>`cl_detaildist`.</summary>
    public float DetailDistance { get; set; }

    /// <summary>`cl_detailfade`.</summary>
    public float DetailFadeWidth { get; set; }

    /// <summary>Whether a map's world is on the device.</summary>
    public bool HasWorld { get; }

    /// <summary>The fog in force this frame (B139).</summary>
    public SceneFog? WorldFog { get; set; }

    /// <summary>The 3D sky's fog this frame.</summary>
    public SceneFog? SkyFog { get; set; }

    /// <summary>The `point_camera` a monitor shows this frame, or null.</summary>
    public ScenePointCamera? MonitorCamera { get; set; }

    /// <summary>The tonemap controller's state this frame.</summary>
    public SceneTonemap Tonemap { get; set; }

    /// <summary>Game time the exposure walks by this frame (B514).</summary>
    public float ExposureSeconds { get; set; }

    /// <summary>The followed player, whose change resets the exposure.</summary>
    public int? ObserverTarget { get; set; }

    /// <summary>Sets the frame's camera: the projection and the cull volume come from the one thing.</summary>
    /// <param name="camera">The camera.</param>
    /// <param name="surfaceColours">Whether the surface-colour view is on.</param>
    public void SetCamera(FreeCamera camera, bool surfaceColours = false);

    /// <summary>Waits for the frame in flight, if one is (`mat_queue_mode 2`): called at the pose's entry, because the pose
    /// rewrites the bones, flex and instance lists the draw reads, while <see cref="SetCamera"/> writes only its own view
    /// half and so overlaps the draw (B581 rung 2, finding 98 §9 step 5). Free in mode 0.</summary>
    public void FinishDrawBeforePose();

    /// <summary>The parts of the last <see cref="SetCamera"/>, in stopwatch ticks, each timed inside it where it ran (B243).</summary>
    public IReadOnlyList<(string Part, long Ticks)> SetCameraParts { get; }

    /// <summary>The frustum of the camera last set: the one the world cull and the draw use.</summary>
    public ViewFrustum Frustum { get; }

    /// <summary>The views' leaf lists the scene collates its renderables over (B254, B262).</summary>
    public RenderableViews Views { get; }

    /// <summary>The eye of the camera last set, or null before one is.</summary>
    public (float X, float Y, float Z)? Eye { get; }

    /// <summary>The view a static prop's screen fade is measured in, or null.</summary>
    public ScreenFadeView? ScreenView { get; }

    /// <summary>
    /// Whether a monitor view can draw this frame — `cl_drawmonitors` on and a world material binding `_rt_Camera` — so the
    /// models only it draws (the first-person player, B511) are worth posing.
    /// </summary>
    public bool DrawsMonitor { get; }

    /// <summary>The recorder's screen overlay this frame, or null.</summary>
    public ScreenOverlayMaterial? ScreenOverlay { get; set; }

    /// <summary>Models a monitor view force-draws though the main view does not (B511).</summary>
    public IReadOnlyList<ModelInstance> MonitorOnlyModels { get; set; }

    /// <summary>Draws the frame: the device's `DrawFrame`, with its two retired channels (the flat fill, the flat markers) empty.</summary>
    /// <param name="red">The background's red.</param>
    /// <param name="green">The background's green.</param>
    /// <param name="blue">The background's blue.</param>
    /// <param name="mapLines">World-space debug lines: `mat_leafvis` (D95).</param>
    /// <param name="models">The moment's model instances.</param>
    /// <param name="viewmodels">The viewmodel pass's instances.</param>
    /// <param name="viewmodelCamera">The viewmodel's own camera matrix, or null to draw none.</param>
    /// <param name="vgui">The frame's VGUI, or null.</param>
    public void DrawScene(
        float red,
        float green,
        float blue,
        IReadOnlyList<((float X, float Y, float Z) From, (float X, float Y, float Z) To)> mapLines,
        IReadOnlyList<ModelInstance>? models,
        IReadOnlyList<ModelInstance>? viewmodels,
        float[]? viewmodelCamera,
        VguiDrawList? vgui);
}
