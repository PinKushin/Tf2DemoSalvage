namespace Tf2DemoSalvage.Scene;

/// <summary>The device as the viewer's presenters drive it (D214): declared here, implemented by Render's device.</summary>
/// <remarks>
/// **So a presenter can hand the device what it decided, and the window names neither the decision nor its types**
/// [no engine counterpart: the engine's renderer is reached through `IVRenderView` and `IMaterialSystem`, which game
/// code holds directly]. It begins with the settings a new device is given; the frame's calls join it as they leave
/// the window.
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

    /// <summary>`r_3dsky`.</summary>
    public int Draw3dSky { get; set; }

    /// <summary>`cl_detaildist`.</summary>
    public float DetailDistance { get; set; }

    /// <summary>`cl_detailfade`.</summary>
    public float DetailFadeWidth { get; set; }
}
