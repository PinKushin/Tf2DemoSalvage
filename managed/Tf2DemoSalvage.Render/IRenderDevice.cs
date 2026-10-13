namespace Tf2DemoSalvage.Render;

/// <summary>
/// The device as the viewer's session drives it between frames: Valve's render switches, the capture, and the
/// buffers' size and mode.
/// </summary>
/// <remarks>
/// **An interface so the decisions around these calls can be tested without a GPU** (D214, slice 9): which switch a
/// menu item throws, what a capture waits for, when the display is taken exclusively. <see cref="Device3D"/> is the
/// one implementation that draws; a test's stand-in records what it was told. What a frame draws is
/// <c>IViewerDevice</c>'s, in Scene.
/// </remarks>
public interface IRenderDevice
{
    /// <summary>Valve's <c>mat_wireframe</c>.</summary>
    public bool Wireframe { get; set; }

    /// <summary>Valve's <c>mat_specular</c>.</summary>
    public bool Specular { get; set; }

    /// <summary>Valve's <c>mat_phong</c>.</summary>
    public bool Phong { get; set; }

    /// <summary>Valve's <c>mat_fullbright</c>.</summary>
    public Fullbright Fullbright { get; set; }

    /// <summary>Valve's <c>r_drawworld</c>.</summary>
    public bool DrawWorld { get; set; }

    /// <summary>Valve's <c>r_drawentities</c>.</summary>
    public bool DrawEntities { get; set; }

    /// <summary>Which of Valve's per-surface debug views are showing.</summary>
    public DebugModes Debug { get; set; }

    /// <summary>Whether the bloom amount and the exposure have stopped walking, so a capture shows a settled frame.</summary>
    public bool PostProcessingSettled { get; }

    /// <summary>The values <see cref="PostProcessingSettled"/> read, for the log.</summary>
    public string PostProcessingReport { get; }

    /// <summary>Writes the next drawn frame to a PNG.</summary>
    /// <param name="path">Where to write it.</param>
    public void CaptureNextFrame(string path);

    /// <summary>Throws the uploaded world away.</summary>
    public void ClearWorld();

    /// <summary>Takes the display exclusively, or gives it back.</summary>
    /// <param name="enabled">Whether to take it.</param>
    /// <returns>Whether the mode asked for is the mode now in force.</returns>
    public bool SetExclusiveFullScreen(bool enabled);

    /// <summary>Resizes the swap chain and everything sized to it.</summary>
    /// <param name="width">The new width in pixels.</param>
    /// <param name="height">The new height in pixels.</param>
    public void Resize(int width, int height);
}
