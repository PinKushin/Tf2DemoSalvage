using System.Collections.Generic;

namespace Tf2DemoSalvage.Scene;

/// <summary>What placing decals asks of the device that draws them: the models it holds, and somewhere to put the result (D214).</summary>
/// <remarks>
/// The fourth of the device's seams, beside <see cref="IModelUpload"/> and <see cref="IWorldUpload"/>, and for the same
/// reason: the work of deciding what a decal is and where it lands needs no GPU, so it is written against these four
/// members and a test answers them with lists.
/// </remarks>
public interface IDecalUpload
{
    /// <summary>The decals on entities' models, drawn after each model with its bones (B415); null for none.</summary>
    public ModelDecals? ModelDecals { get; set; }

    /// <summary>A model's batches at its first frame, which index its vertices.</summary>
    /// <param name="modelPath">The model.</param>
    /// <returns>Its runs, or empty.</returns>
    public IReadOnlyList<WorldBatch> ModelBatches(string modelPath);

    /// <summary>Replaces the decals the game has placed on the world — bullet holes and the demo's decal events (B415).</summary>
    /// <param name="vertices">Their triangles, grouped by material.</param>
    /// <param name="batches">One run per material.</param>
    /// <param name="entityBatches">The runs on brush entities, drawn with them after the world's.</param>
    public void UploadShotDecals(
        IReadOnlyList<WorldVertex> vertices, IReadOnlyList<WorldBatch> batches, IReadOnlyList<WorldBatch> entityBatches);
}
