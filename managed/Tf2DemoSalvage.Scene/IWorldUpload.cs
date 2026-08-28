namespace Tf2DemoSalvage.Scene;

/// <summary>Somewhere to put a level's world geometry and its textures.</summary>
/// <remarks>
/// **The same seam as <see cref="IModelUpload"/>, for the same reason** (B188, D90). Deciding
/// whether the world needs uploading is not window work — it is a question about a level and what
/// already reached the GPU — but performing the upload needs a device. So the decision moves and the
/// device arrives through an interface, which is what lets the decision be tested with a fake.
///
/// **Every member is something the engine's own renderer owns rather than the client.** Source
/// uploads world geometry at level load through `modelloader`; a game DLL never does it. Ours has to
/// because this viewer IS the engine for that purpose — but the split is kept, so the code that
/// decides is not the code that talks to Direct3D.
///
/// **`HasWorld` and `HasWorldTextures` are questions, not flags to mirror.** A caller keeping its own
/// copy of "did I upload this" is how the same state comes to disagree with the device — which is
/// the whole shape of B196, and the reason `_texturesUploaded` was the ONE piece of this that had to
/// stay a caller's business (a map change invalidates it, and the device cannot know that).
/// </remarks>
public interface IWorldUpload
{
    /// <summary>Whether world geometry is currently resident.</summary>
    public bool HasWorld { get; }

    /// <summary>Whether the world's textures are currently resident.</summary>
    public bool HasWorldTextures { get; }

    /// <summary>Uploads the level's materials and lightmaps.</summary>
    /// <param name="assets">What the map read decoded.</param>
    public void UploadWorldTextures(MapAssets assets);

    /// <summary>Uploads the built world geometry.</summary>
    /// <param name="world">The batched vertices.</param>
    public void UploadWorldGeometry(MapWorld world);

    /// <summary>Gives the renderer this map's visibility, or takes it away.</summary>
    /// <param name="culling">The map's culling, or null for a map that cannot be culled.</param>
    /// <remarks>
    /// **Beside the geometry rather than beside the camera, because it belongs to the MAP.** Pairing
    /// one map's face spans with another map's vertex buffer produces runs at plausible offsets into
    /// the wrong geometry — a scrambled map rather than an error — so this is set and cleared in the
    /// same breath as the upload it describes.
    /// </remarks>
    public void SetWorldCulling(WorldCulling? culling);

    /// <summary>Points the world at a camera.</summary>
    /// <param name="matrix">The view-projection, row major, sixteen floats.</param>
    /// <param name="surfaceColours">Whether to draw the surface-category view.</param>
    // `heightCut` was a third parameter here until 2026-08-26 (B213). It cut on DEPTH, which is
    // height only under the orthographic projection D98 deleted.
    public void SetCamera(float[] matrix, bool surfaceColours = false);

    /// <summary>Releases the resident world.</summary>
    public void ClearWorld();
}
