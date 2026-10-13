using System.Collections.Generic;

using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>A device that keeps every lightmap upload and glow proxy it is handed, and answers occlusion counts by entity.</summary>
internal sealed class FakeFrameDevice : IFrameDevice
{
    /// <summary>The decal half of the device, kept as the decals' own fake keeps it.</summary>
    public FakeDecalUpload Decals { get; } = new();

    /// <summary>Every set of particle batches handed over, one a frame.</summary>
    public List<IReadOnlyList<ParticleBatch>> Particles { get; } = [];

    /// <summary>Every call that hands the device something, by name, in the order made.</summary>
    public List<string> Calls { get; } = [];

    public ModelDecals? ModelDecals
    {
        get => Decals.ModelDecals;
        set => Decals.ModelDecals = value;
    }

    public IReadOnlyList<WorldVertex>? SkinnedVertices(string modelPath) => Decals.SkinnedVertices(modelPath);

    public IReadOnlyList<WorldBatch> ModelBatches(string modelPath) => Decals.ModelBatches(modelPath);

    public void UploadShotDecals(
        IReadOnlyList<WorldVertex> vertices, IReadOnlyList<WorldBatch> batches, IReadOnlyList<WorldBatch> entityBatches)
    {
        Calls.Add(nameof(UploadShotDecals));
        Decals.UploadShotDecals(vertices, batches, entityBatches);
    }

    public void SetParticles(IReadOnlyList<ParticleBatch> batches)
    {
        Calls.Add(nameof(SetParticles));
        Particles.Add(batches);
    }

    /// <summary>Every lightmap upload: the atlas it was given, its width, and the regions, copied.</summary>
    public List<(byte[] Pixels, int Width, AtlasRegion[] Regions)> Lightmaps { get; } = [];

    /// <summary>Occlusion counts to answer, by entity.</summary>
    public Dictionary<int, (int Pixels, int Possible)> Counts { get; } = [];

    /// <summary>Every entity an occlusion result was asked for, in order.</summary>
    public List<int> Asked { get; } = [];

    /// <summary>Every set of proxies handed over, one a frame.</summary>
    public List<PixelVisibilityProxy[]> Proxies { get; } = [];

    public void UpdateLightmap(byte[] pixels, int width, IReadOnlyList<AtlasRegion> regions)
    {
        Calls.Add(nameof(UpdateLightmap));
        Lightmaps.Add((pixels, width, [.. regions]));
    }

    public (int Pixels, int Possible)? OcclusionResult(int entity)
    {
        Asked.Add(entity);

        return Counts.TryGetValue(entity, out (int Pixels, int Possible) counted) ? counted : null;
    }

    public void SetOcclusionProxies(IReadOnlyList<PixelVisibilityProxy> proxies)
    {
        Calls.Add(nameof(SetOcclusionProxies));
        Proxies.Add([.. proxies]);
    }
}
