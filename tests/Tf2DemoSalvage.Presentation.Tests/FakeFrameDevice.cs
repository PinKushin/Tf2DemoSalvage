using System.Collections.Generic;
using System.Linq;

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

    /// <summary>Told each call's name as it is made, for a test that orders these calls among another fake's.</summary>
    public System.Action<string>? Told { get; set; }

    public ModelDecals? ModelDecals
    {
        get => Decals.ModelDecals;
        set => Decals.ModelDecals = value;
    }

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
        Told?.Invoke(nameof(SetParticles));
        Particles.Add(batches);
    }

    /// <summary>Every frame's water-view batches, kept apart from <see cref="Calls"/> so the effects' order stays as it was.</summary>
    public List<IReadOnlyList<IReadOnlyList<ParticleBatch>>> WaterParticles { get; } = [];

    public void SetWaterParticles(IReadOnlyList<IReadOnlyList<ParticleBatch>> views) => WaterParticles.Add(views);

    /// <summary>Every frame's sky-view batches, kept apart from <see cref="Calls"/> so the effects' order stays as it was.</summary>
    public List<ParticleBatch[]> SkyParticles { get; } = [];

    public void SetSkyParticles(IReadOnlyList<ParticleBatch> batches) => SkyParticles.Add([.. batches]);

    /// <summary>Every frame's monitor batches, kept apart from <see cref="Calls"/> so the effects' order stays as it was.</summary>
    public List<ParticleBatch[][]> MonitorParticles { get; } = [];

    public void SetMonitorParticles(IReadOnlyList<IReadOnlyList<ParticleBatch>> batches) =>
        MonitorParticles.Add([.. batches.Select(each => each.ToArray())]);

    /// <summary>Every frame's player sprites (the head labels), kept apart from <see cref="Calls"/> so the effects' order stays as it was.</summary>
    public List<ParticleBatch[]> PlayerSprites { get; } = [];

    public void SetPlayerSprites(IReadOnlyList<ParticleBatch> batches) => PlayerSprites.Add([.. batches]);

    /// <summary>Each frame's monitor proxies, one array per monitor view, copied as handed.</summary>
    public List<PixelVisibilityProxy[][]> MonitorProxies { get; } = [];

    public void SetMonitorOcclusionProxies(IReadOnlyList<IReadOnlyList<PixelVisibilityProxy>> proxies) =>
        MonitorProxies.Add([.. proxies.Select(each => each.ToArray())]);

    /// <summary>Each frame's water view proxies, one array per water view by its place, copied as handed.</summary>
    public List<PixelVisibilityProxy[][]> WaterViewProxies { get; } = [];

    public void SetWaterOcclusionProxies(IReadOnlyList<IReadOnlyList<PixelVisibilityProxy>> proxies) =>
        WaterViewProxies.Add([.. proxies.Select(each => each.ToArray())]);

    /// <summary>Every frame's shadows handed over, kept apart from <see cref="Calls"/> so the effects' order stays as it was.</summary>
    public List<Scene.ProjectedShadows.ShadowFrame> Shadows { get; } = [];

    public void SetShadows(Scene.ProjectedShadows.ShadowFrame frame) => Shadows.Add(frame);

    /// <summary>Whether a draw took the last shadow frame; every frame is taken unless a test says otherwise.</summary>
    public bool ShadowsTaken { get; set; } = true;

    /// <summary>The frame's world views, for the shadows; none, the step's own camera culling nothing, unless a test says otherwise.</summary>
    public IReadOnlyList<Scene.ProjectedShadows.ShadowWorldView> ShadowViews { get; set; } = [];

    /// <summary>Every lightmap upload: the atlas it was given, its width, and the regions, copied.</summary>
    public List<(byte[] Pixels, int Width, AtlasRegion[] Regions)> Lightmaps { get; } = [];

    /// <summary>Occlusion counts to answer, by entity.</summary>
    public Dictionary<int, (int Pixels, int Possible)> Counts { get; } = [];

    /// <summary>Every sub-view an occlusion result was asked for, by its view id, in order.</summary>
    public List<int> ViewAsked { get; } = [];

    /// <summary>Every entity an occlusion result was asked for, in order.</summary>
    public List<int> Asked { get; } = [];

    /// <summary>Every set of the main view's proxies handed over, one a frame.</summary>
    public List<PixelVisibilityProxy[]> Proxies { get; } = [];

    /// <summary>Every set of a water view's proxies handed over, with its view.</summary>
    public List<(int View, PixelVisibilityProxy[] Proxies)> WaterProxies { get; } = [];

    /// <summary>Occlusion counts to answer for a water view, by view and entity.</summary>
    public Dictionary<(int View, int Entity), (int Pixels, int Possible)> WaterCounts { get; } = [];

    public void UpdateLightmap(byte[] pixels, int width, IReadOnlyList<AtlasRegion> regions)
    {
        Calls.Add(nameof(UpdateLightmap));
        Lightmaps.Add((pixels, width, [.. regions]));
    }

    public (int Pixels, int Possible)? OcclusionResult(int entity, int view = 0)
    {
        if (view != 0)
        {
            ViewAsked.Add(view);

            return WaterCounts.TryGetValue((view, entity), out (int Pixels, int Possible) water) ? water : null;
        }

        Asked.Add(entity);

        return Counts.TryGetValue(entity, out (int Pixels, int Possible) counted) ? counted : null;
    }

    public void SetOcclusionProxies(IReadOnlyList<PixelVisibilityProxy> proxies, int view = 0)
    {
        if (view != 0)
        {
            WaterProxies.Add((view, [.. proxies]));
            return;
        }

        Calls.Add(nameof(SetOcclusionProxies));
        Proxies.Add([.. proxies]);
    }
}
