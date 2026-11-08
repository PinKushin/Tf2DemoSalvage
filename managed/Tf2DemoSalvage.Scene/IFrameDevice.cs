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
    /// The world surfaces this frame's main view draws, in draw order, and the displacements its walk reached — what
    /// <c>R_DrawWorldLists</c> hands the shadow manager surface by surface (B702). Null surfaces for a map the view cannot
    /// cull, which draws every surface.
    /// </summary>
    public (IReadOnlyList<int>? Surfaces, IReadOnlyList<ReachedDisplacement>? Displacements) WorldDrawn { get; }

    /// <summary>This frame's render-to-texture shadows (B702): the casters drawn into the atlas, and the world's decals.</summary>
    /// <param name="frame">The shadows; <see cref="ProjectedShadows.ShadowFrame.None"/> for none.</param>
    public void SetShadows(ProjectedShadows.ShadowFrame frame);
}
