using System.Collections.Generic;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// Everything a frame's presenters ask of the device between the models and the draw: the lightmaps a light style
/// rebuilt, the decals, the glows' occlusion counts, and the frame's particle batches (D214).
/// </summary>
/// <remarks>
/// **The three seams the parts already had, and one member more.** Each part asks its own narrow interface; the
/// presenter that runs them in order is handed one device and gives each part the face of it that part knows, so a test
/// of the order needs one fake and no graphics card.
/// </remarks>
public interface IFrameDevice : ILightmapUpload, IDecalUpload, IGlowOcclusion
{
    /// <summary>The particle batches this frame draws, in draw order.</summary>
    /// <param name="batches">The batches.</param>
    public void SetParticles(IReadOnlyList<ParticleBatch> batches);

    /// <summary>
    /// Each water view's particle batches, in <see cref="RenderableViews.Water"/>'s order: the effects its own collation listed,
    /// billboarded to its camera (B711). Empty for none.
    /// </summary>
    /// <param name="views">The batches per water view.</param>
    public void SetWaterParticles(IReadOnlyList<IReadOnlyList<ParticleBatch>> views);

    /// <summary>
    /// The 3D skybox view's particle batches: the effects, sprites and strips its own collation listed, billboarded to the sky
    /// camera, drawn in the sky pass (B711). Empty for none.
    /// </summary>
    /// <param name="batches">The batches.</param>
    public void SetSkyParticles(IReadOnlyList<ParticleBatch> batches);

    /// <summary>
    /// The monitor view's particle batches: the effects, sprites and strips its own collation listed, billboarded to the point
    /// camera, drawn in the monitor pass (B711). Empty for none.
    /// </summary>
    /// <param name="batches">The batches.</param>
    public void SetMonitorParticles(IReadOnlyList<ParticleBatch> batches);

    /// <summary>
    /// This frame's world views in the order they draw — monitors, the 3D sky, the water views, the main view — each with
    /// the leaves its <c>ComputeShadowTextures</c> enumerates shadows by and the surfaces <c>R_DrawWorldLists</c> hands the
    /// shadow manager (B702). Empty before a camera is set.
    /// </summary>
    public IReadOnlyList<ProjectedShadows.ShadowWorldView> ShadowViews { get; }

    /// <summary>This frame's render-to-texture shadows (B702): the casters drawn into the atlas, and the world's decals.</summary>
    /// <param name="frame">The shadows; <see cref="ProjectedShadows.ShadowFrame.None"/> for none.</param>
    public void SetShadows(ProjectedShadows.ShadowFrame frame);

    /// <summary>
    /// Whether a draw was handed the last <see cref="SetShadows"/> frame — answered on the game thread, where the frame is
    /// submitted, so it never races the draw (B702). False means its redraws are still owed
    /// (<see cref="ProjectedShadows.ShadowCasters.Undrawn"/>).
    /// </summary>
    public bool ShadowsTaken { get; }
}
