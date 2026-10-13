using System.Collections.Generic;

using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>A device that keeps every lightmap upload and glow proxy it is handed, and answers occlusion counts by entity.</summary>
internal sealed class FakeFrameDevice : ILightmapUpload, IGlowOcclusion
{
    /// <summary>Every lightmap upload: the atlas it was given, its width, and the regions, copied.</summary>
    public List<(byte[] Pixels, int Width, AtlasRegion[] Regions)> Lightmaps { get; } = [];

    /// <summary>Occlusion counts to answer, by entity.</summary>
    public Dictionary<int, (int Pixels, int Possible)> Counts { get; } = [];

    /// <summary>Every entity an occlusion result was asked for, in order.</summary>
    public List<int> Asked { get; } = [];

    /// <summary>Every set of proxies handed over, one a frame.</summary>
    public List<PixelVisibilityProxy[]> Proxies { get; } = [];

    public void UpdateLightmap(byte[] pixels, int width, IReadOnlyList<AtlasRegion> regions) =>
        Lightmaps.Add((pixels, width, [.. regions]));

    public (int Pixels, int Possible)? OcclusionResult(int entity)
    {
        Asked.Add(entity);

        return Counts.TryGetValue(entity, out (int Pixels, int Possible) counted) ? counted : null;
    }

    public void SetOcclusionProxies(IReadOnlyList<PixelVisibilityProxy> proxies) => Proxies.Add([.. proxies]);
}
