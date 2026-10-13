using System.Collections.Generic;

namespace Tf2DemoSalvage.Scene;

/// <summary>What rebuilding the world's lightmaps asks of the device that holds them (D214).</summary>
/// <remarks>
/// One member, beside <see cref="IDecalUpload"/> and for its reason: deciding which regions of the atlas a light
/// style or a dynamic light rebuilt needs no GPU, so that work is written against this and a test keeps the regions.
/// </remarks>
public interface ILightmapUpload
{
    /// <summary>Uploads the regions of the lightmap atlas that were rebuilt.</summary>
    /// <param name="pixels">The whole atlas.</param>
    /// <param name="width">Its width in pixels.</param>
    /// <param name="regions">The regions that changed.</param>
    public void UpdateLightmap(byte[] pixels, int width, IReadOnlyList<AtlasRegion> regions);
}
