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
}
